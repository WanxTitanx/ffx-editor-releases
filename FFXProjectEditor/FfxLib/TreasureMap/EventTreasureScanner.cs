using FFXProjectEditor.FfxLib.Atel;
using FFXProjectEditor.FfxLib.Event;
using FFXProjectEditor.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.TreasureMap;

// ── EventTreasureScanner ─────────────────────────────────────────────────────────────
// Scans a single field event (.ebp / EV01 chunk 0, ATEL bytecode) for treasure chests.
//
// WHY THIS EXISTS (reverse-engineering context, see docs/reverse/TREASURE_CHEST_RE_VALIDATION_2026-08-06.md):
//   - A "chest" is proven by the ATEL native call obtainTreasure (0x015B) /
//     obtainTreasureSilently (0x01A7). Decompiled FFX_Atel_Common_obtainTreasure@0x85A740
//     shows it loads the takara.bin row, adds the item, spends gil, or registers a model.
//   - The visual chest model is ENGINE-SIDE (treasure slot table @0x1130F2F, 22-byte
//     records with opened/closed flags) — per-field model ids like 0x5002/0x50AA are NOT
//     used as confirmation criteria anymore.
//   - The event ATEL chunk is the SAME container family as the battle AiFile. The header
//     is: +0x00 codeLen, +0x10 declared, +0x14 workersTotal, +0x30 scriptStart,
//     +0x36 workerCount, +0x38 u32[workerCount] worker-offset table. The bytecode starts
//     at scriptStart (NOT at chunk offset 0) — this was the root cause of the scanner
//     returning 0 candidates for a long time.
//
// COORDINATES: most chest workers push x/y/z via PUSH_FLOAT_ARR (opcode 0x2F) which reads
// a runtime float pool (ctx[0][7] + 4*index + ctx[1], ctx = AiScriptStateMachine slot) —
// NOT parseable from the file. Only PUSH_IM / PUSH_CTX_FLOAT10 constants are recoverable,
// so only a handful of chests get a real position; the rest use the bounds placeholder.
// ──────────────────────────────────────────────────────────────────────────────────────

public sealed record EventTreasureScanResult(
    string EventPath, string EventId,
    IReadOnlyList<EventTreasureCandidate> Candidates,
    int WorkerCount, int StatementCount);

public static class EventTreasureScanner
{
    // ── Legacy speculative chest model ids ─────────────────────────────────────────────
    // Kept as hints only. RE discarded these as confirmation criteria (see TreasureMapIndex
    // ConfirmedChestCandidates) because the visual chest model id is dynamic (0x5000 + slot
    // from FFX_Field_RegisterModelRecord@0x7AB930), not a fixed constant.
    public const int StandardChestModelId = 0x5002;
    public const int BlueChestModelId = 0x50AA;

    public static EventTreasureScanResult Scan(string eventPath)
    {
        DebugLog.Info("TreasureMap.Scan", $"Scanning {Path.GetFileName(eventPath)}");
        EventPackage package;
        try { package = EventPackage.Read(eventPath); }
        catch (Exception ex) { DebugLog.Warn("TreasureMap.Scan", $"Failed to read {Path.GetFileName(eventPath)}: {ex.Message}"); throw; }

        if (package.AtelBytes.Length < 0x38)
        {
            DebugLog.Info("TreasureMap.Scan", $"{Path.GetFileName(eventPath)} ATEL chunk too short for header");
            return new EventTreasureScanResult(eventPath, Path.GetFileNameWithoutExtension(eventPath).ToLowerInvariant(), [], 0, 0);
        }

        string eventId = Path.GetFileNameWithoutExtension(eventPath).ToLowerInvariant();
        byte[] atel = package.AtelBytes;

        // ATEL header (same as battle AiFile, proven 397/397):
        //   +0x00 u32 codeLen · +0x10 u32 declared · +0x14 u16 workersTotal
        //   +0x30 u32 scriptStart · +0x36 u16 workerCount · +0x38 u32[workerCount] worker-offset table
        int codeLen = BitConverter.ToInt32(atel, 0x00);
        int scriptStart = BitConverter.ToInt32(atel, 0x30);
        int workerCount = BitConverter.ToUInt16(atel, 0x36);

        if (scriptStart < 0 || scriptStart >= atel.Length || codeLen < 0 || scriptStart + codeLen > atel.Length)
        {
            DebugLog.Warn("TreasureMap.Scan", $"{Path.GetFileName(eventPath)}: ATEL header invalid (scriptStart=0x{scriptStart:X} codeLen=0x{codeLen:X} blob={atel.Length})");
            return new EventTreasureScanResult(eventPath, eventId, [], 0, 0);
        }

        var instructions = EventAtelDisassembler.DisassembleStructured(
            atel,
            start: scriptStart,
            length: codeLen,
            resolveCall: op => { try { return EventCallGlossary.Entries.TryGetValue(op, out string? n) ? n : $"0x{op:X4}"; } catch { return $"0x{op:X4}"; } });

        // Build workers from the header's worker-offset table (accurate boundaries),
        // falling back to STOP-split if the table is inconsistent.
        var workers = SplitWorkers(instructions, atel, scriptStart, codeLen, workerCount);

        // Float constant pools per worker (ZanarkandWorkshop extraction, 2026-08-06):
        // PUSH_FLOAT_ARR (0x2F) reads a worker's FloatConstantBits[operand] IN THE FILE.
        // Worker descriptors (and their float tables) live in the DATA area of the chunk —
        // NOT inside [scriptStart, scriptStart+codeLen) — so we parse them directly from
        // the offset table (@0x34 per the ZW battle layout; @0x36 as fallback).
        var workerPools = BuildWorkerPools(atel);

        var candidates = new List<EventTreasureCandidate>();
        foreach (WorkerScanState w in workers)
        {
            EventTreasureCandidate? c = ScanWorker(w, eventPath, eventId, workerPools);
            if (c is not null) candidates.Add(c);
        }

        int chestWorkers = candidates.Count(c => c.HasChestModel);
        DebugLog.Info("TreasureMap.Scan", $"{Path.GetFileName(eventPath)}: start=0x{scriptStart:X} len=0x{codeLen:X} workers={workers.Count} candidates={candidates.Count} chestWorkers={chestWorkers} statements={instructions.Count}");
        return new EventTreasureScanResult(eventPath, eventId, candidates, workers.Count, instructions.Count);
    }

    public static List<WorkerScanState> SplitWorkersForTest(
            List<DisassemblyInstruction> instructions, byte[] atel)
        => SplitWorkers(instructions, atel,
            atel.Length > 0x30 ? BitConverter.ToInt32(atel, 0x30) : 0,
            atel.Length > 0x00 ? BitConverter.ToInt32(atel, 0x00) : 0,
            atel.Length > 0x38 ? BitConverter.ToUInt16(atel, 0x36) : 0);

    private static List<WorkerScanState> SplitWorkers(
        IReadOnlyList<DisassemblyInstruction> instructions, byte[] atel,
        int scriptStart, int codeLen, int workerCount)
    {
        // Try the worker-offset table first: each entry is a bytecode-offset of a worker's entry point.
        if (workerCount > 0 && workerCount <= 4096 && 0x38 + workerCount * 4 <= atel.Length)
        {
            var offsets = new List<int>(workerCount);
            for (int i = 0; i < workerCount; i++)
            {
                int off = BitConverter.ToInt32(atel, 0x38 + i * 4);
                offsets.Add(off);
            }
            offsets.Sort();
            // Entry points must land within the bytecode range [scriptStart, scriptStart+codeLen).
            if (offsets.All(o => o >= scriptStart && o < scriptStart + codeLen) && offsets[0] == scriptStart)
            {
                var workers = new List<WorkerScanState>();
                for (int i = 0; i < offsets.Count; i++)
                {
                    int start = offsets[i];
                    int end = i + 1 < offsets.Count ? offsets[i + 1] : scriptStart + codeLen;
                    var range = instructions.Where(x => x.Offset >= start && x.Offset < end).ToList();
                    if (range.Count > 0)
                    { var ws = new WorkerScanState(workers.Count); ws.Instructions.AddRange(range); workers.Add(ws); }
                }
                if (workers.Count > 0) return workers;
            }
        }

        // Fallback: split on STOP terminators.
        var workersByStop = new List<WorkerScanState>();
        var cur = new WorkerScanState(workersByStop.Count);
        foreach (DisassemblyInstruction inst in instructions)
        {
            cur.Instructions.Add(inst);
            if (inst.Mnemonic is "STOP" or "STOP_SELF" or "STOP_UNBIND" or "STOP_UNBIND_SELF")
            { workersByStop.Add(cur); cur = new WorkerScanState(workersByStop.Count); }
        }
        if (cur.Instructions.Count > 0) workersByStop.Add(cur);
        return workersByStop;
    }

    // ── BuildWorkerPools ───────────────────────────────────────────────────────────────
    // Parses the ATEL worker descriptors (headers + INT/FLOAT constant pools) directly from
    // the worker-offset table. The descriptors live in the DATA area of the chunk, outside
    // [scriptStart, scriptStart+codeLen). Tries workerCount@0x34 (ZanarkandWorkshop battle
    // layout, provenance: AtelScriptDocument) first, then @0x36 (the legacy event guess).
    // Returns [] when neither table can be parsed — callers then fall back to the legacy
    // operand-as-value behavior for PUSH_FLOAT_ARR.
    private static IReadOnlyList<AtelWorkerInfo> BuildWorkerPools(byte[] atel)
    {
        foreach (int countOffset in new[] { 0x34, 0x36 })
        {
            int wc = BitConverter.ToUInt16(atel, countOffset);
            if (wc <= 0 || wc > 4096 || 0x38 + wc * 4 > atel.Length) continue;
            try { return AtelScriptParser.ReadWorkers(atel, wc, 0x38); }
            catch (InvalidOperationException) { /* try next candidate */ }
        }
        return [];
    }

    private static EventTreasureCandidate? ScanWorker(
        WorkerScanState w, string eventPath, string eventId, IReadOnlyList<AtelWorkerInfo> workerPools)
    {
        var insts = w.Instructions;
        List<int> modelIds = new(), treasureIds = new();
        var positions = new List<EventPosition>();
        bool silent = false; int fn = 0;
        // ── Two parallel stacks ────────────────────────────────────────────────────────────
        // stack  : raw int stack used to resolve the operands of obtainTreasure /
        //          setModelResourceId (the pushed value is the treasure/model id).
        // fstack : value stack + a "reliable" tag. Only constants pushed from the file
        //          (PUSH_IM, PUSH_CTX_FLOAT10, PUSH_FLOAT_ARR) are marked reliable; runtime
        //          variables are not, so setPosition only accepts constant x/z.
        // floatTable : worker frame +0x30 (10 floats). POP_CTX_UNK2B (opcode 0x2B) writes
        //          them, PUSH_CTX_FLOAT10 (0x26) reads them back.
        var stack = new Stack<int>();
        var fstack = new List<(float val, bool rel)>();
        var floatTable = new float[10];

        for (int i = 0; i < insts.Count; i++)
        {
            DisassemblyInstruction inst = insts[i];
            // ── Push handling ────────────────────────────────────────────────────────────
            // PUSH_IM          : 16-bit immediate -> short (world coordinate unit).
            // PUSH_CTX_FLOAT10 : index into floatTable (reliable only if already populated
            //                    by a file-visible POP_CTX_UNK2B in this worker).
            // PUSH_FLOAT_ARR   : reads a RUNTIME float pool (index + runtime ctx offset) —
            //                    the operand is NOT the value, so this only marks unreliable
            //                    unless the pool gets resolved by a runtime probe.
            if (inst.Mnemonic is "PUSH_IM" or "PUSHI" or "PUSHII")
            {
                float v = unchecked((short)inst.Operand);
                fstack.Add((v, true)); stack.Push(inst.Operand);
            }
            else if (inst.Mnemonic is "PUSH_CTX_FLOAT10")
            {
                int idx = inst.Operand & 0xF;
                float val = idx < floatTable.Length ? floatTable[idx] : 0f;
                fstack.Add((val, idx < floatTable.Length)); // reliable while the float table was populated by file-visible POP
                stack.Push(unchecked((int)BitConverter.SingleToInt32Bits(val)));
            }
            else if (inst.Mnemonic is "PUSH_FLOAT_ARR")
            {
                // 2026-08-06 (ZanarkandWorkshop extraction): PUSH_FLOAT_ARR reads a FLOAT from
                // a worker's constant pool stored IN THE FILE — FloatConstantBits[operand].
                // Like the ZW ResolveFloatConstant(), we pick the first worker whose pool
                // contains the index. Falls back to legacy operand-as-value when unavailable.
                var pool = workerPools.FirstOrDefault(p => p.FloatConstantCount > inst.Operand);
                float cell = pool is not null ? pool.GetFloatConstant(inst.Operand)
                    : (float)unchecked((short)inst.Operand);
                fstack.Add((cell, pool is not null));
            }
            else if (inst.Mnemonic is "PUSH_INT_A" or "PUSH_INT_B" or "PUSH_INT_C" or "PUSH_INT_D" or "PUSH_GLOBAL" or "PUSH_VAR")
            { stack.Push(inst.Operand); fstack.Add((inst.Operand, false)); } // unreliable (runtime var)
            else if (inst.Mnemonic is "POP_CTX_UNK2B") // stores into worker frame +0x30 float table
            {
                int idx = inst.Operand & 0xF;
                if (idx < floatTable.Length && fstack.Count > 0)
                    floatTable[idx] = fstack[^1].val;
            }

            if (inst.IsNativeCall && !string.IsNullOrEmpty(inst.GlossaryName))
            {
                string name = inst.GlossaryName;
                // ── Native call handling ──────────────────────────────────────────────────
                // setPosition (0x0013): pops up to 3 values (x, y, z) from the value stack.
                //   Accept only when both x and z are file constants (reliable) — runtime
                //   get-position chains (0x0080-0x0082) are NOT recovered.
                // obtainTreasure (0x015B) / Silently (0x01A7): pops the treasure id — this is
                //   the PROVEN chest signature (see FFX_Atel_Common_obtainTreasure@0x85A740).
                // setModelResourceId (0x505B): pops a model id (hint only, see header note).
                // Other calls: deliberately do NOT clear the value stack. Chest workers often
                //   push x/y/z via PUSH_IM, call unrelated setup natives, then setPosition;
                //   trusting the PUSH_IM values recovers more positions (bounds projection
                //   filters false positives downstream).
                if (name == "setPosition")
                {
                    // Real stack model: setPosition pops up to 3 values (x, y, z) from the value stack.
                    if (fstack.Count >= 1)
                    {
                        float z = fstack[^1].val; bool rz = fstack[^1].rel;
                        float y = fstack.Count >= 2 ? fstack[^2].val : 0f; bool ry = fstack.Count >= 2 ? fstack[^2].rel : false;
                        float x = fstack.Count >= 3 ? fstack[^3].val : 0f; bool rx = fstack.Count >= 3 ? fstack[^3].rel : false;
                        // Only accept if at least x and z came from file constants (reliable).
                        if (rx && rz)
                            positions.Add(new EventPosition(x, y, z, inst.Offset, fn));
                    }
                    fstack.Clear();
                    stack.Clear();
                }
                else if (name == "obtainTreasure")
                { if (stack.Count > 0) treasureIds.Add(stack.Pop()); }
                else if (name == "obtainTreasureSilently")
                { if (stack.Count > 0) { treasureIds.Add(stack.Pop()); silent = true; } }
                else if (name == "setModelResourceId")
                { if (stack.Count > 0) modelIds.Add(stack.Pop()); }
                // NOTE: do NOT null/deprecate the value stack on unknown native calls.
                // Many chest workers push x/y/z via PUSH_IM, then call unrelated native
                // setup calls, then setPosition. Trusting PUSH_IM values (rx/rz) recovers
                // far more constant positions; map-bounds projection filters false positives.
            }
        }
        if (treasureIds.Count == 0 && modelIds.Count == 0) return null;
        return new EventTreasureCandidate(eventPath, eventId, FieldAssetDiscovery.ToFieldId(eventId),
            w.Index, treasureIds.Distinct().OrderBy(id => id).ToArray(),
            positions, modelIds.Distinct().OrderBy(id => id).ToArray(), silent);
    }

    public sealed class WorkerScanState
    {
        public int Index { get; }
        public List<DisassemblyInstruction> Instructions { get; } = new();
        public int Start { get; set; }
        public int End { get; set; }
        public WorkerScanState(int index) => Index = index;
    }
}
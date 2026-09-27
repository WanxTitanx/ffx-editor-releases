using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Ai
{
    // FFX battle-AI bytecode codec (disassembler + byte-exact re-emitter).
    //
    // The FFX HD battle AI is a little stack VM whose script lives inside each monster_*.bin AiFile blob
    // (the region between the monster header's AiFilePointer and WorkerFilePointer). One instruction is
    // [opcode] for stack operators, or [opcode][u16 LE operand] for the operand-bearing opcodes.
    //
    // The opcode->length table and operand encoding are PROVEN EMPIRICALLY, not guessed:
    //   - 361 monsters / 180,836 instructions (RT0); instruction-stream contiguity re-verified no corpus atual.
    //   - every opcode maps to exactly ONE length across the whole corpus (no ambiguity).
    //   - operand = u16 little-endian, proven via D8 (call): operand == decompiler's [XXXXh] 29,523/0.
    //   - script region == AiFile[scriptStart .. end]; walk closes exactly at end with zero leftover
    //     on all 361 (trailer == 0), and RT0 re-emit is byte-identical 361/361.
    // See docs/reverse/FFX_AI_BYTECODE_OPCODE_TABLE_PROVEN_2026-06-04.md and work/_scratch_ai/*.py.
    //
    // Dependency-free by design (only System.*): usable from both the Avalonia editor and the standalone
    // RuntimeTools/AiScriptLab RT0 gate without pulling editor/Xe.BinaryMapper deps.

    /// <summary>What an operand-bearing opcode's u16 means. Names cross-checked against the public
    /// FFX ATEL reference (Karifean/FFXDataParser, built on the Fahrenheit Ghidra RE of FFX.exe);
    /// our opcode-length table + operand width were independently PROVEN from the corpus census.</summary>
    public enum AiOperandKind
    {
        None,        // 1-byte stack operator (no operand)
        Immediate,   // AE PUSHII: push immediate int16
        FloatConst,  // AF PUSHF: push from the float constant pool by index
        IntConst,    // AD PUSHI: push int32 from the worker refInts pool by index
        VarLoad,     // 9F PUSHV: push variable (priv / battleVar) by index
        VarStore,    // A0 POPV: store to variable by index
        ArrLoad,     // A2 PUSHAR: push array element by index
        ArrStore,    // A3 POPAR: store array element by index
        FuncId,      // B5 CALL / D8 CALLPOPA: native call, operand = function id (high nibble = namespace)
        JumpIndex,   // B0 JMP / D6 POPXCJMP / D7 POPXNCJMP: branch, operand = worker jump-table index
    }

    /// <summary>One decoded VM instruction. Edit-ready: Emit() rebuilds bytes from Opcode+Operand.</summary>
    public sealed class AiInstruction
    {
        /// <summary>Byte offset of this instruction relative to the start of the AiFile blob.</summary>
        public int Offset { get; init; }
        public byte Opcode { get; set; }
        public bool HasOperand { get; init; }
        /// <summary>u16 little-endian operand (valid only when HasOperand). Settable for future editing.</summary>
        public ushort Operand { get; set; }
        public AiOperandKind OperandKind { get; init; }

        public int Length => HasOperand ? 3 : 1;

        /// <summary>Deterministic re-encoding: [opcode] or [opcode][lo][hi]. Re-emits the original bytes
        /// for an unedited instruction (this is what makes RT0 byte-identical).</summary>
        public byte[] Emit()
        {
            return HasOperand
                ? new byte[] { Opcode, (byte)(Operand & 0xFF), (byte)((Operand >> 8) & 0xFF) }
                : new byte[] { Opcode };
        }
    }

    /// <summary>One ATEL worker: an independent script unit with event entrypoints + a jump-table.
    /// Layout cross-confirmed in IDA (FFX_Atel_GetScriptWorker* @ 0x86A5E0/0x86BB10) and proven over
    /// 897/897 workers vs the corpus oracle. Entrypoint/jump targets are CODE-RELATIVE offsets
    /// (add ScriptStart for the absolute AiFile offset / the matching AiInstruction.Offset).</summary>
    public sealed class AiWorker
    {
        public int Index { get; init; }
        /// <summary>Worker descriptor offset within the AiFile (worker[k] = AiFile + u32@(0x38+4k)).</summary>
        public int DescriptorOffset { get; init; }
        public int PrivateDataLength { get; init; }   // u16 @ desc+0x10
        /// <summary>Event entrypoint code-relative offsets (init/main/onTurn/onHit/... ; u32[]@desc+0x20).</summary>
        public required IReadOnlyList<int> Entrypoints { get; init; }
        /// <summary>Jump-table: branch operand (B0/D7/D6) indexes this; value = code-relative target
        /// offset (u32[]@desc+0x24, immediately after the entrypoint table).</summary>
        public required IReadOnlyList<int> JumpTargets { get; init; }
        /// <summary>INFERRED worker type from the calls in its code (95.4% vs oracle; Camera exact). The real
        /// type/slot live in the WorkerFile as DERIVED values — proven not byte-recoverable. Inferred only.</summary>
        public string? InferredType { get; set; }
        /// <summary>Slot derived from the inferred type (Combat=0x3D, Camera=0, Motion=0x40). Inferred.</summary>
        public int InferredSlot { get; set; } = -1;
    }

    /// <summary>A typed script variable (8-byte descriptor @ varsOffset + 8*i). storage 0x52=battleVar,
    /// 0x56=private(priv); index = which battleVar/priv slot; typeId 1=btlChr. Names match the oracle
    /// (battleVarNNNN / privNNNN). VarLoad/VarStore operands index this list.</summary>
    public sealed class AiVariable
    {
        public int Index { get; init; }       // position in the table (= the 9F/A0 operand)
        public byte Storage { get; init; }
        public int Slot { get; init; }
        public int TypeId { get; init; }
        public string Name => Storage switch
        {
            0x52 => $"battleVar{Slot:X4}",
            0x56 => $"priv{Slot:X4}",
            0x54 => AiSaveDataVariableNames.Get(Slot) is string n ? $"saveData{Slot:X4}_{n}" : $"saveData{Slot:X4}",
            _ => $"var{Index}",
        };
    }

    public sealed class AiScriptFile
    {
        /// <summary>The exact AiFile blob as read (header + worker/var table + script code + data tables).</summary>
        public required byte[] OriginalAiFileBytes { get; init; }
        /// <summary>Script code length in bytes (u32 @ 0x00).</summary>
        public int CodeLength { get; init; }
        /// <summary>Declared AiFile length (u32 @ 0x10).</summary>
        public int DeclaredLength { get; init; }
        /// <summary>Workers total (@ 0x14).</summary>
        public int WorkersTotal { get; init; }
        /// <summary>Offset (relative to AiFile start) where script code begins (u32 @ 0x30).</summary>
        public int ScriptStart { get; init; }
        /// <summary>Header + worker/variable table bytes [0 .. ScriptStart). Preserved verbatim (parsed later).</summary>
        public required byte[] HeaderBytes { get; init; }
        /// <summary>Decoded script instructions, in order, covering [ScriptStart .. ScriptStart+CodeLength).</summary>
        public required IReadOnlyList<AiInstruction> Instructions { get; init; }
        /// <summary>Post-code data section [ScriptStart+CodeLength .. end): constant pool, jump tables, worker
        /// private data. Preserved verbatim for now (parsed later).</summary>
        public required byte[] DataBytes { get; init; }
        /// <summary>True when the instruction walk consumed exactly CodeLength bytes (the length table is
        /// self-consistent for this monster). False = a 3-byte opcode is missing from the table or the
        /// region is corrupt — never silently mis-walk.</summary>
        public bool CodeWalkClosedExactly { get; init; }
        /// <summary>Opcodes seen in the code region that are NOT in the known opcode set (should be empty).</summary>
        public required IReadOnlyList<byte> UnknownOpcodes { get; init; }
        /// <summary>Parsed workers (entrypoints + jump-tables). Read-only view; the underlying bytes live in
        /// HeaderBytes/DataBytes and are preserved verbatim (so RT0 is unaffected).</summary>
        public required IReadOnlyList<AiWorker> Workers { get; init; }
        /// <summary>Typed script variables (VarLoad/VarStore operands index this). Script-level.</summary>
        public IReadOnlyList<AiVariable> Variables { get; init; } = Array.Empty<AiVariable>();
        /// <summary>Float constant pool offset (AiFile-relative); AF/PUSHF operand = index, value = f32@(off+4*idx).</summary>
        public int FloatPoolOffset { get; init; } = -1;
        /// <summary>Int32 constant pool offset (AiFile-relative); AD/PUSHI operand = index, value = i32@(off+4*idx).</summary>
        public int IntPoolOffset { get; init; } = -1;

        public bool HasScript => Instructions.Count > 0;
    }

    public static class AiScript_File
    {
        public const int OffCodeLength = 0x00;     // u32 LE: script code length (bytes)
        public const int OffDeclaredLength = 0x10; // u32 LE: AiFile length
        public const int OffWorkersTotal = 0x14;   // workers total (category A)
        public const int OffScriptStart = 0x30;    // u32 LE: script code start (relative to AiFile)
        public const int OffWorkerCount = 0x36;    // u16: worker count (drives the worker-offset table)
        public const int OffWorkerOffsetTable = 0x38; // u32[workerCount]: worker[k] = AiFile + table[k]
        public const int MinHeaderLength = 0x34;
        // Worker descriptor field offsets (relative to the worker descriptor).
        const int WdEntrypoints = 0x08, WdJumps = 0x0A, WdPrivLen = 0x10,
            WdVars = 0x14, WdIntPool = 0x18, WdFloatPool = 0x1C, WdEntryTable = 0x20, WdJumpTable = 0x24;

        // Corpus opcodes plus RE-confirmed NOP, comparisons, EOR and SRL
        // (FFX_EVENTVM_OPS_2026-09-17, native interpreter @0x864180). Used to flag anything
        // outside the known set (a strong signal the walk strayed into data or hit a truly new opcode).
        static readonly bool[] KnownOpcode = BuildKnown();
        static bool[] BuildKnown()
        {
            var k = new bool[256];
            foreach (byte b in new byte[]{
                0x00,0x01,0x02,0x03,0x04,0x05,0x06,0x07,0x08,0x09,0x0A,0x0B,0x0C,0x0D,0x0E,0x0F,0x12,0x13,0x14,0x15,0x16,0x17,0x18,0x19,
                0x29,0x2A,0x2B,0x2C,0x3C,0x59,0x5A,0x5B,0x5C,0x5D,0x5E,0x60,0x67,0x68,0x69,0x6A,0x6B,0x6C,0x6E,
                0x9F,0xA0,0xA2,0xA3,0xAD,0xAE,0xAF,0xB0,0xB5,0xD6,0xD7,0xD8}) k[b] = true;
            return k;
        }
        public static bool IsKnownOpcode(byte opcode) => KnownOpcode[opcode];

        // VM length rule (cross-confirmed by FFXDataParser ScriptInstruction): an opcode with bit 0x80 set
        // carries a u16 little-endian operand (3 bytes total); otherwise it is a 1-byte stack operator.
        // This generalizes our 361-monster census (every 3-byte opcode we saw has 0x80; every 1-byte one
        // does not) and the per-monster gate (walk must close exactly on codeLen) catches any violation.
        const byte OperandBit = 0x80;
        static readonly AiOperandKind[] Kinds = new AiOperandKind[256];

        static AiScript_File()
        {
            void Set(byte op, AiOperandKind k) { Kinds[op] = k; }
            Set(0x9F, AiOperandKind.VarLoad);    // PUSHV
            Set(0xA0, AiOperandKind.VarStore);   // POPV
            Set(0xA2, AiOperandKind.ArrLoad);    // PUSHAR
            Set(0xA3, AiOperandKind.ArrStore);   // POPAR
            Set(0xAD, AiOperandKind.IntConst);   // PUSHI (int32 refInts pool)
            Set(0xAE, AiOperandKind.Immediate);  // PUSHII (int16)
            Set(0xAF, AiOperandKind.FloatConst); // PUSHF (float pool)
            Set(0xB0, AiOperandKind.JumpIndex);  // JMP
            Set(0xB5, AiOperandKind.FuncId);     // CALL
            Set(0xD6, AiOperandKind.JumpIndex);  // POPXCJMP (jump if true)
            Set(0xD7, AiOperandKind.JumpIndex);  // POPXNCJMP (jump if false)
            Set(0xD8, AiOperandKind.FuncId);     // CALLPOPA (void call)
        }

        public static bool IsOperandBearing(byte opcode) => (opcode & OperandBit) != 0;
        public static int InstructionLength(byte opcode) => (opcode & OperandBit) != 0 ? 3 : 1;

        /// <summary>The operand role of an opcode, derived from the OPCODE ALONE (None for 1-byte operators).
        /// Callers that build AiInstruction by hand (e.g. the editor's AiAsmRow) may not set OperandKind, so
        /// validators/analysis should resolve the kind through this, never trusting AiInstruction.OperandKind.</summary>
        public static AiOperandKind OperandKindOf(byte opcode) => IsOperandBearing(opcode) ? Kinds[opcode] : AiOperandKind.None;

        public enum AiInstructionDecodeStatus
        {
            /// <summary>Provado no corpus RT0 (KnownOpcode, walk exato 361/361).</summary>
            Confirmed,
            /// <summary>Operando de 3 bytes pela regra VM, fora do corpus - tamanho/rol inferidos.</summary>
            Hypothesis,
            /// <summary>1 byte lido com estrutura, sem semantica provada.</summary>
            Decoded,
        }

        public static AiInstructionDecodeStatus DecodeStatusOf(byte opcode) =>
            IsKnownOpcode(opcode) ? AiInstructionDecodeStatus.Confirmed
            : IsOperandBearing(opcode) ? AiInstructionDecodeStatus.Hypothesis
            : AiInstructionDecodeStatus.Decoded;

        static ushort U16(byte[] b, int o) => (ushort)(b[o] | (b[o + 1] << 8));
        static uint U32(byte[] b, int o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
        static void WriteU32(byte[] b, int o, uint v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24); }

        /// <summary>Disassemble an AiFile blob into a structured, edit-ready, byte-exact model.</summary>
        public static AiScriptFile Read(byte[] aiFile)
        {
            ArgumentNullException.ThrowIfNull(aiFile);
            if (aiFile.Length < MinHeaderLength)
            {
                // Stub / degenerate AiFile (no script). Preserve verbatim, no instructions.
                return new AiScriptFile
                {
                    OriginalAiFileBytes = aiFile,
                    CodeLength = 0,
                    DeclaredLength = aiFile.Length,
                    WorkersTotal = 0,
                    ScriptStart = aiFile.Length,
                    HeaderBytes = aiFile.ToArray(),
                    Instructions = Array.Empty<AiInstruction>(),
                    DataBytes = Array.Empty<byte>(),
                    CodeWalkClosedExactly = true,
                    UnknownOpcodes = Array.Empty<byte>(),
                    Workers = Array.Empty<AiWorker>(),
                };
            }

            int codeLength = (int)U32(aiFile, OffCodeLength);
            int declared = (int)U32(aiFile, OffDeclaredLength);
            int workers = U16(aiFile, OffWorkersTotal);
            int scriptStart = (int)U32(aiFile, OffScriptStart);

            if (scriptStart < MinHeaderLength || scriptStart > aiFile.Length)
                throw new InvalidDataException(
                    $"AiFile script-start 0x{scriptStart:X} out of range (len 0x{aiFile.Length:X}).");
            int codeEnd = scriptStart + codeLength;
            if (codeLength < 0 || codeEnd > aiFile.Length)
                throw new InvalidDataException(
                    $"AiFile code [0x{scriptStart:X}..0x{codeEnd:X}) runs past EOF (len 0x{aiFile.Length:X}).");

            // Walk ONLY the bounded code region [scriptStart .. codeEnd). The codeLength gives an exact
            // boundary the walk must land on — if it doesn't, the length table is wrong for this monster
            // (we surface that via CodeWalkClosedExactly instead of mis-walking into the data tables).
            var instructions = new List<AiInstruction>();
            var unknown = new SortedSet<byte>();
            int pos = scriptStart;
            while (pos < codeEnd)
            {
                byte op = aiFile[pos];
                if (!KnownOpcode[op]) unknown.Add(op);
                bool hasOperand = (op & OperandBit) != 0;
                int len = hasOperand ? 3 : 1;
                if (pos + len > codeEnd)
                    break; // straddles the code boundary -> stop (folds into DataBytes; flagged below)
                instructions.Add(new AiInstruction
                {
                    Offset = pos,
                    Opcode = op,
                    HasOperand = hasOperand,
                    Operand = hasOperand ? U16(aiFile, pos + 1) : (ushort)0,
                    OperandKind = hasOperand ? Kinds[op] : AiOperandKind.None,
                });
                pos += len;
            }

            bool closedExactly = pos == codeEnd;
            byte[] header = aiFile[..scriptStart];
            byte[] data = aiFile[pos..]; // post-code section (+ any straddle remainder if not closed exactly)

            IReadOnlyList<AiWorker> workerList = ParseWorkers(aiFile);
            // Script-level tables (vars + const pools) live in every worker descriptor (same pointers).
            var (vars, floatPool, intPool) = ParseScriptData(aiFile, workerList);
            InferWorkerTypes(workerList, instructions, scriptStart, codeLength);

            return new AiScriptFile
            {
                OriginalAiFileBytes = aiFile,
                CodeLength = codeLength,
                DeclaredLength = declared,
                WorkersTotal = workers,
                ScriptStart = scriptStart,
                HeaderBytes = header,
                Instructions = instructions,
                DataBytes = data,
                CodeWalkClosedExactly = closedExactly,
                UnknownOpcodes = unknown.ToArray(),
                Workers = workerList,
                Variables = vars,
                FloatPoolOffset = floatPool,
                IntPoolOffset = intPool,
            };
        }

        // Worker descriptor offsets for the script-level tables: +0x14 varsOff, +0x18 varsEnd/intPool,
        // +0x1C floatPool. Variables are 8-byte descriptors [storage<<24|slot][typeId]; count=(end-off)/8.
        static (IReadOnlyList<AiVariable>, int floatPool, int intPool) ParseScriptData(byte[] aiFile, IReadOnlyList<AiWorker> workers)
        {
            if (workers.Count == 0) return (Array.Empty<AiVariable>(), -1, -1);
            int desc = workers[0].DescriptorOffset;
            if (desc + 0x20 > aiFile.Length) return (Array.Empty<AiVariable>(), -1, -1);
            int varsOff = (int)U32(aiFile, desc + 0x14);
            int varsEnd = (int)U32(aiFile, desc + 0x18); // == int pool start
            int floatPool = (int)U32(aiFile, desc + 0x1C);
            var vars = new List<AiVariable>();
            if (varsOff > 0 && varsEnd > varsOff && varsEnd <= aiFile.Length)
            {
                int n = (varsEnd - varsOff) / 8;
                for (int i = 0; i < n && varsOff + 8 * i + 8 <= aiFile.Length; i++)
                {
                    uint lo = U32(aiFile, varsOff + 8 * i);
                    uint hi = U32(aiFile, varsOff + 8 * i + 4);
                    vars.Add(new AiVariable { Index = i, Storage = (byte)(lo >> 24), Slot = (int)(lo & 0xFFFFFF), TypeId = (int)hi });
                }
            }
            return (vars, floatPool, varsEnd);
        }

        /// <summary>Parse the worker table (entrypoints + jump-tables). Defensive: returns what it can,
        /// never throws on malformed data. Worker[k] = AiFile + u32@(0x38+4k); descriptor fields per
        /// the proven layout. Entrypoint/jump values are code-relative offsets.</summary>
        static IReadOnlyList<AiWorker> ParseWorkers(byte[] aiFile)
        {
            if (aiFile.Length < OffWorkerOffsetTable + 4) return Array.Empty<AiWorker>();
            int count = U16(aiFile, OffWorkerCount);
            if (count <= 0 || count > 256) return Array.Empty<AiWorker>();
            if (OffWorkerOffsetTable + 4 * count > aiFile.Length) return Array.Empty<AiWorker>();

            var workers = new List<AiWorker>(count);
            for (int k = 0; k < count; k++)
            {
                int desc = (int)U32(aiFile, OffWorkerOffsetTable + 4 * k);
                if (desc < 0 || desc + WdJumpTable + 4 > aiFile.Length) break;
                int entry = U16(aiFile, desc + WdEntrypoints);
                int jumps = U16(aiFile, desc + WdJumps);
                int privLen = U16(aiFile, desc + WdPrivLen);
                int entryTab = (int)U32(aiFile, desc + WdEntryTable);
                int jumpTab = (int)U32(aiFile, desc + WdJumpTable);
                workers.Add(new AiWorker
                {
                    Index = k,
                    DescriptorOffset = desc,
                    PrivateDataLength = privLen,
                    Entrypoints = ReadOffsetTable(aiFile, entryTab, entry),
                    JumpTargets = ReadOffsetTable(aiFile, jumpTab, jumps),
                });
            }
            return workers;
        }

        /// <summary>
        /// Maps each reachable instruction to the worker(s) that can execute it by walking entrypoints and branches.
        /// This remains correct for appended guarded blocks, whose physical position is no longer inside the original
        /// contiguous worker range.
        /// </summary>
        public static IReadOnlyDictionary<int, IReadOnlyList<int>> InstructionOwners(AiScriptFile script)
        {
            ArgumentNullException.ThrowIfNull(script);
            var byRelativeOffset = new Dictionary<int, (AiInstruction instruction, int next)>();
            for (int i = 0; i < script.Instructions.Count; i++)
            {
                AiInstruction instruction = script.Instructions[i];
                int relative = instruction.Offset - script.ScriptStart;
                int next = i + 1 < script.Instructions.Count
                    ? script.Instructions[i + 1].Offset - script.ScriptStart
                    : script.CodeLength;
                byRelativeOffset[relative] = (instruction, next);
            }

            var owners = new Dictionary<int, HashSet<int>>();
            foreach (AiWorker worker in script.Workers)
            {
                var pending = new Stack<int>(worker.Entrypoints);
                var visited = new HashSet<int>();
                while (pending.Count > 0)
                {
                    int relative = pending.Pop();
                    if (!visited.Add(relative) || !byRelativeOffset.TryGetValue(relative, out var node)) continue;

                    int absolute = script.ScriptStart + relative;
                    if (!owners.TryGetValue(absolute, out HashSet<int>? set))
                    {
                        set = new HashSet<int>();
                        owners[absolute] = set;
                    }
                    set.Add(worker.Index);

                    AiInstruction instruction = node.instruction;
                    if (instruction.Opcode == 0xB0)
                    {
                        if (instruction.Operand < worker.JumpTargets.Count)
                            pending.Push(worker.JumpTargets[instruction.Operand]);
                        continue;
                    }
                    if (instruction.Opcode == 0xD6 || instruction.Opcode == 0xD7)
                    {
                        if (instruction.Operand < worker.JumpTargets.Count)
                            pending.Push(worker.JumpTargets[instruction.Operand]);
                        if (node.next < script.CodeLength) pending.Push(node.next);
                        continue;
                    }
                    if (instruction.Opcode == 0x3C || instruction.Opcode == 0x40) continue;
                    if (node.next < script.CodeLength) pending.Push(node.next);
                }
            }

            return owners.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<int>)pair.Value.OrderBy(index => index).ToArray());
        }

        // Combat-defining calls (action/targeting/decision) that don't leak to Motion workers.
        // IDA-verified: 0x701B=overrideAttemptedCommand, 0x7034=endBattle/writeByteToActors, 0x7106=doesChrKnowCommand
        // are NOT combat-defining. Removed from set per SUPERMD cross-validation.
        static readonly HashSet<ushort> CombatCalls = new()
        { 0x700B, 0x7010, 0x701E, 0x7021, 0x705A, 0x7014, 0x7019 };

        // Infer each worker's type from the calls in its code region (owner-by-range). Camera-namespace
        // call -> Camera (exact); a combat-defining call -> Combat; else Motion (~95% vs oracle). The real
        // type/slot are WorkerFile-derived (proven not byte-recoverable), so this is INFERRED only.
        static void InferWorkerTypes(IReadOnlyList<AiWorker> workers, IReadOnlyList<AiInstruction> instrs, int scriptStart, int codeLength)
        {
            var order = workers.Where(w => w.Entrypoints.Count > 0)
                .Select(w => (start: scriptStart + w.Entrypoints.Min(), w))
                .OrderBy(t => t.start).ToList();
            for (int o = 0; o < order.Count; o++)
            {
                int lo = order[o].start;
                int hi = o + 1 < order.Count ? order[o + 1].start : scriptStart + codeLength;
                bool camera = false, combat = false;
                foreach (AiInstruction i in instrs)
                {
                    if (i.Offset < lo || i.Offset >= hi || i.OperandKind != AiOperandKind.FuncId) continue;
                    if ((i.Operand >> 12) == 6) camera = true;
                    else if (CombatCalls.Contains(i.Operand)) combat = true;
                }
                AiWorker w = order[o].w;
                w.InferredType = camera ? "CameraHandler" : combat ? "CombatHandler" : "MotionHandler";
                w.InferredSlot = camera ? 0x00 : combat ? 0x3D : 0x40;
            }
        }

        static IReadOnlyList<int> ReadOffsetTable(byte[] aiFile, int tableOff, int count)
        {
            if (count <= 0 || tableOff < 0 || tableOff + 4 * count > aiFile.Length) return Array.Empty<int>();
            var r = new int[count];
            for (int i = 0; i < count; i++) r[i] = (int)U32(aiFile, tableOff + 4 * i);
            return r;
        }

        /// <summary>Re-emit the AiFile blob from the model. For an unedited model this is byte-identical
        /// to OriginalAiFileBytes (RT0) — proven 361/361 over the real monster corpus.</summary>
        public static byte[] Write(AiScriptFile script)
        {
            ArgumentNullException.ThrowIfNull(script);
            int total = script.HeaderBytes.Length + script.DataBytes.Length
                        + script.Instructions.Sum(i => i.Length);
            byte[] output = new byte[total];
            int o = 0;
            Array.Copy(script.HeaderBytes, 0, output, o, script.HeaderBytes.Length);
            o += script.HeaderBytes.Length;
            foreach (AiInstruction instr in script.Instructions)
            {
                byte[] raw = instr.Emit();
                Array.Copy(raw, 0, output, o, raw.Length);
                o += raw.Length;
            }
            Array.Copy(script.DataBytes, 0, output, o, script.DataBytes.Length);
            return output;
        }

        /// <summary>RT0 self-check: Write(Read(x)) == x.</summary>
        public static bool RoundTripsByteIdentical(byte[] aiFile)
        {
            byte[] re = Write(Read(aiFile));
            return re.AsSpan().SequenceEqual(aiFile);
        }

        /// <summary>Slice the AiFile blob out of a full monster_*.bin (AiFilePointer @4 .. WorkerFilePointer @8).
        /// Returns null when the monster has no AI partition.</summary>
        public static byte[]? SliceAiFileFromMonster(byte[] monsterBin)
        {
            ArgumentNullException.ThrowIfNull(monsterBin);
            if (monsterBin.Length < 0x30) return null;
            int aiPtr = (int)U32(monsterBin, 0x04);
            int workerPtr = (int)U32(monsterBin, 0x08);
            if (aiPtr <= 0 || workerPtr <= aiPtr || workerPtr > monsterBin.Length) return null;
            byte[] ai = new byte[workerPtr - aiPtr];
            Array.Copy(monsterBin, aiPtr, ai, 0, ai.Length);
            return ai;
        }

        // ---- control-flow / value editors (edit the data tables in-place; byte-local, length-preserving) ----

        /// <summary>Edit a float in the constant pool (AF/PUSHF operand = index). Returns a new AiFile with
        /// only the 4 pool bytes changed. Operand-stable: the PUSHF instruction is untouched, the VALUE moves.</summary>
        public static byte[] EditFloatConst(AiScriptFile script, int index, float value)
        {
            ArgumentNullException.ThrowIfNull(script);
            int off = script.FloatPoolOffset + 4 * index;
            return PatchU32(script.OriginalAiFileBytes, off, BitConverter.SingleToUInt32Bits(value), "float const");
        }

        /// <summary>Edit an int32 in the refInts pool (AD/PUSHI operand = index). 4 bytes changed.</summary>
        public static byte[] EditIntConst(AiScriptFile script, int index, int value)
        {
            ArgumentNullException.ThrowIfNull(script);
            int off = script.IntPoolOffset + 4 * index;
            return PatchU32(script.OriginalAiFileBytes, off, unchecked((uint)value), "int const");
        }

        /// <summary>Re-route a jump: set worker[workerIndex]'s jump-table[jumpIndex] to a new CODE-RELATIVE
        /// target offset. The B0/D6/D7 instruction (operand = jump index) is untouched; only the table u32
        /// moves. Returns a new AiFile (4 bytes changed). The target must be < CodeLength (a valid code offset).</summary>
        public static byte[] EditJumpTarget(AiScriptFile script, int workerIndex, int jumpIndex, int codeRelativeTarget)
        {
            ArgumentNullException.ThrowIfNull(script);
            if (workerIndex < 0 || workerIndex >= script.Workers.Count)
                throw new ArgumentOutOfRangeException(nameof(workerIndex));
            AiWorker w = script.Workers[workerIndex];
            if (jumpIndex < 0 || jumpIndex >= w.JumpTargets.Count)
                throw new ArgumentOutOfRangeException(nameof(jumpIndex));
            if (codeRelativeTarget < 0 || codeRelativeTarget > script.CodeLength)
                throw new ArgumentOutOfRangeException(nameof(codeRelativeTarget), "jump target must be a valid code offset (< CodeLength).");
            byte[] ai = script.OriginalAiFileBytes;
            int jumpTab = (int)U32(ai, w.DescriptorOffset + WdJumpTable);
            return PatchU32(ai, jumpTab + 4 * jumpIndex, unchecked((uint)codeRelativeTarget), "jump target");
        }

        static byte[] PatchU32(byte[] src, int off, uint value, string what)
        {
            if (off < 0 || off + 4 > src.Length)
                throw new InvalidOperationException($"{what} offset 0x{off:X} out of range (len 0x{src.Length:X}).");
            byte[] o = (byte[])src.Clone();
            o[off] = (byte)(value & 0xFF);
            o[off + 1] = (byte)((value >> 8) & 0xFF);
            o[off + 2] = (byte)((value >> 16) & 0xFF);
            o[off + 3] = (byte)((value >> 24) & 0xFF);
            return o;
        }

        /// <summary>Rebuild the script code region removing [removeStartCodeRelative .. removeEndCodeRelative)
        /// and/or inserting new instructions at that point, remapping every code-relative reference
        /// (worker entrypoints, jump-table targets and the worker descriptor data-section pointers) so the
        /// result is a valid larger/smaller AiFile. Null when the removal range is invalid. This is the
        /// structural primitive behind sequence-level authoring (target recipes, clone remaps).</summary>
        public static byte[]? RebuildWithCodeRegion(
            AiScriptFile script,
            int removeStartCodeRelative,
            int removeEndCodeRelative,
            IReadOnlyList<AiInstruction> insertedInstructions,
            out string error)
        {
            error = string.Empty;
            IReadOnlyList<AiInstruction> ins = script.Instructions;
            if (removeStartCodeRelative < 0 || removeEndCodeRelative < removeStartCodeRelative || removeEndCodeRelative > script.CodeLength)
            {
                error = $"remove range {removeStartCodeRelative}..{removeEndCodeRelative} invalid (code length 0x{script.CodeLength:X}).";
                return null;
            }

            int startIndex = 0;
            while (startIndex < ins.Count && ins[startIndex].Offset - script.ScriptStart < removeStartCodeRelative)
                startIndex++;
            int endIndex = startIndex;
            while (endIndex < ins.Count && ins[endIndex].Offset - script.ScriptStart < removeEndCodeRelative)
                endIndex++;

            var rebuilt = new List<AiInstruction>(ins.Count - (endIndex - startIndex) + insertedInstructions.Count);
            for (int i = 0; i < startIndex; i++) rebuilt.Add(ins[i]);
            rebuilt.AddRange(insertedInstructions);
            for (int i = endIndex; i < ins.Count; i++) rebuilt.Add(ins[i]);

            int removedBytes = removeEndCodeRelative - removeStartCodeRelative;
            int addedBytes = 0;
            foreach (AiInstruction instr in insertedInstructions) addedBytes += instr.Length;
            int delta = addedBytes - removedBytes;
            if (delta == 0 && insertedInstructions.Count == 0)
            {
                error = "no-op rebuild (nothing removed, nothing inserted).";
                return null;
            }

            var code = new List<byte>(script.CodeLength + delta);
            foreach (AiInstruction instr in rebuilt) code.AddRange(instr.Emit());

            byte[] output = new byte[script.HeaderBytes.Length + code.Count + script.DataBytes.Length];
            int o = 0;
            Array.Copy(script.HeaderBytes, 0, output, 0, script.HeaderBytes.Length);
            o += script.HeaderBytes.Length;
            code.CopyTo(output, o);
            o += code.Count;
            Array.Copy(script.DataBytes, 0, output, o, script.DataBytes.Length);

            WriteU32(output, OffCodeLength, (uint)(script.CodeLength + delta));
            WriteU32(output, OffDeclaredLength, (uint)(script.DeclaredLength + delta));
            if (delta == 0)
                return output;

            int insertionAbs = script.ScriptStart + removeStartCodeRelative;
            var oldByRel = new Dictionary<int, int>(ins.Count);
            for (int i = 0; i < ins.Count; i++)
                oldByRel[ins[i].Offset - script.ScriptStart] = i;
            var oldToNew = new Dictionary<AiInstruction, int>(rebuilt.Count);
            var newRel = new int[rebuilt.Count];
            int rel = 0;
            for (int i = 0; i < rebuilt.Count; i++)
            {
                newRel[i] = rel;
                rel += rebuilt[i].Length;
                if (!insertedInstructions.Contains(rebuilt[i]))
                    oldToNew[rebuilt[i]] = i;
            }

            int RemapValue(int v)
            {
                if (oldByRel.TryGetValue(v, out int oldIdx) && oldToNew.TryGetValue(ins[oldIdx], out int newIdx))
                    return newRel[newIdx];
                return v;
            }

            foreach (AiWorker w in script.Workers)
            {
                int entryTabNew = (int)U32(output, w.DescriptorOffset + WdEntryTable) + delta;
                int entryCount = (int)U16(output, w.DescriptorOffset + WdEntrypoints);
                for (int k = 0; k < entryCount; k++)
                {
                    int v = (int)U32(output, entryTabNew + 4 * k);
                    int nv = RemapValue(v);
                    if (nv != v)
                        WriteU32(output, entryTabNew + 4 * k, (uint)nv);
                }

                int jumpTabNew = (int)U32(output, w.DescriptorOffset + WdJumpTable) + delta;
                int jumpCount = (int)U16(output, w.DescriptorOffset + WdJumps);
                for (int k = 0; k < jumpCount; k++)
                {
                    int v = (int)U32(output, jumpTabNew + 4 * k);
                    int nv = RemapValue(v);
                    if (nv != v)
                        WriteU32(output, jumpTabNew + 4 * k, (uint)nv);
                }

                foreach (int field in new[] { WdVars, WdIntPool, WdFloatPool, WdEntryTable, WdJumpTable })
                {
                    int p = w.DescriptorOffset + field;
                    int v = (int)U32(output, p);
                    if (v >= insertionAbs)
                        WriteU32(output, p, (uint)(v + delta));
                }
            }

            return output;
        }

        /// <summary>Finds the first 4-byte private slot that is already covered by some worker private storage
        /// but is not declared in the script variable table. Structural only: using the returned slot from a
        /// specific worker still requires that worker's PrivateDataLength to cover slot+4.</summary>
        public static bool TryFindFreePrivateVariableSlot(AiScriptFile script, out int slot, out string reason)
        {
            ArgumentNullException.ThrowIfNull(script);
            slot = -1;
            int maxPrivateLength = script.Workers.Count == 0 ? 0 : script.Workers.Max(w => w.PrivateDataLength);
            if (maxPrivateLength <= 0)
            {
                reason = Strings.U_Ai_NoWorkerPrivateStorage;
                return false;
            }

            HashSet<int> used = script.Variables
                .Where(v => v.Storage == 0x56)
                .Select(v => v.Slot)
                .ToHashSet();
            for (int candidate = 0; candidate + 4 <= maxPrivateLength; candidate += 4)
            {
                if (used.Contains(candidate)) continue;
                slot = candidate;
                reason = string.Format(Strings.U_Ai_PrivSlotFree, candidate, maxPrivateLength);
                return true;
            }

            reason = string.Format(Strings.U_Ai_PrivSlotsAllUsed, maxPrivateLength);
            return false;
        }

        /// <summary>Append one f32 to the script float constant pool (immediately before the code block at
        /// ScriptStart). Returns the grown AiFile and the PUSHF pool index. Reuses an existing slot when the
        /// value already matches.</summary>
        public static (byte[] AiFile, int PoolIndex) AppendFloatConst(AiScriptFile script, float value)
        {
            ArgumentNullException.ThrowIfNull(script);
            if (script.FloatPoolOffset < 0 || script.ScriptStart < script.FloatPoolOffset + 4)
                throw new InvalidOperationException("script has no usable float pool layout");
            byte[] ai = script.OriginalAiFileBytes;
            int floatPool = script.FloatPoolOffset;
            int floatPoolEnd = script.ScriptStart;
            int count = (floatPoolEnd - floatPool) / 4;
            for (int i = 0; i < count; i++)
            {
                float existing = BitConverter.ToSingle(ai, floatPool + 4 * i);
                if (MathF.Abs(existing - value) < 1e-5f)
                    return (ai, i);
            }

            int poolIndex = count;
            const int add = 4;
            int insertion = floatPoolEnd;
            byte[] output = new byte[ai.Length + add];
            Array.Copy(ai, 0, output, 0, insertion);
            WriteU32(output, insertion, BitConverter.SingleToUInt32Bits(value));
            Array.Copy(ai, insertion, output, insertion + add, ai.Length - insertion);
            WriteU32(output, OffDeclaredLength, U32(output, OffDeclaredLength) + (uint)add);
            WriteU32(output, OffScriptStart, U32(output, OffScriptStart) + (uint)add);

            void Reloc(int off, bool strict = false)
            {
                if (off < 0 || off + 4 > output.Length) return;
                uint v = U32(output, off);
                if (strict ? v > insertion : v >= insertion)
                    WriteU32(output, off, v + (uint)add);
            }

            int workerCount = U16(ai, OffWorkerCount);
            for (int k = 0; k < workerCount; k++) Reloc(OffWorkerOffsetTable + 4 * k);
            for (int k = 0; k < workerCount; k++)
            {
                int oldDesc = (int)U32(ai, OffWorkerOffsetTable + 4 * k);
                int newDesc = oldDesc >= insertion ? oldDesc + add : oldDesc;
                Reloc(newDesc + WdVars, strict: true);
                Reloc(newDesc + WdIntPool);
                Reloc(newDesc + WdFloatPool);
                Reloc(newDesc + WdEntryTable);
                Reloc(newDesc + WdJumpTable);
            }

            return (output, poolIndex);
        }

        /// <summary>Append one script variable descriptor to the variable table. This grows only the AiFile
        /// variable table and relocates AiFile-relative offsets; it does not grow worker private storage.
        /// Duplicate descriptors are allowed when <paramref name="allowDuplicateDescriptor"/> is true, which is
        /// useful for portable template aliases that must materialize a higher var[N] without overwriting an
        /// existing descriptor.</summary>
        public static byte[] AppendVariableDescriptor(
            AiScriptFile script,
            byte storage,
            int slot,
            int typeId = 1,
            bool allowDuplicateDescriptor = false)
        {
            ArgumentNullException.ThrowIfNull(script);
            if (slot < 0)
                throw new ArgumentOutOfRangeException(nameof(slot), "variable slot must be non-negative.");
            if (script.Workers.Count == 0)
                throw new InvalidOperationException("script has no worker descriptors.");

            if (storage == 0x56)
            {
                if ((slot % 4) != 0)
                    throw new ArgumentOutOfRangeException(nameof(slot), "private variable slot must be 4-byte aligned.");
                int maxPrivateLength = script.Workers.Count == 0 ? 0 : script.Workers.Max(w => w.PrivateDataLength);
                if (slot + 4 > maxPrivateLength)
                    throw new InvalidOperationException($"priv{slot:X4} is outside existing private storage length 0x{maxPrivateLength:X}.");
            }

            if (!allowDuplicateDescriptor &&
                script.Variables.Any(v => v.Storage == storage && v.Slot == slot && v.TypeId == typeId))
            {
                string label = storage switch
                {
                    0x56 => $"priv{slot:X4}",
                    0x52 => $"battleVar{slot:X4}",
                    0x54 => $"saveData{slot:X4}",
                    _ => $"storage 0x{storage:X2} slot 0x{slot:X4}",
                };
                throw new InvalidOperationException($"{label} is already declared in the variable table.");
            }

            byte[] ai = script.OriginalAiFileBytes;
            int firstDesc = script.Workers[0].DescriptorOffset;
            int varsOff = (int)U32(ai, firstDesc + WdVars);
            int varsEnd = (int)U32(ai, firstDesc + WdIntPool);
            if (varsOff <= 0 || varsEnd < varsOff || varsEnd > ai.Length)
                throw new InvalidOperationException($"bad variable table range 0x{varsOff:X}..0x{varsEnd:X}.");

            const int add = 8;
            int insertion = varsEnd;
            byte[] output = new byte[ai.Length + add];
            Array.Copy(ai, 0, output, 0, insertion);
            WriteU32(output, insertion, (0x56u << 24) | ((uint)slot & 0x00FFFFFF));
            WriteU32(output, insertion + 4, (uint)typeId);
            Array.Copy(ai, insertion, output, insertion + add, ai.Length - insertion);

            WriteU32(output, OffDeclaredLength, U32(output, OffDeclaredLength) + (uint)add);
            if (U32(output, OffScriptStart) >= insertion)
                WriteU32(output, OffScriptStart, U32(output, OffScriptStart) + (uint)add);

            void Reloc(int off, bool strict = false)
            {
                if (off < 0 || off + 4 > output.Length) return;
                uint v = U32(output, off);
                if (strict ? v > insertion : v >= insertion)
                    WriteU32(output, off, v + (uint)add);
            }

            int workerCount = U16(ai, OffWorkerCount);
            for (int k = 0; k < workerCount; k++) Reloc(OffWorkerOffsetTable + 4 * k);
            for (int k = 0; k < workerCount; k++)
            {
                int oldDesc = (int)U32(ai, OffWorkerOffsetTable + 4 * k);
                int newDesc = oldDesc >= insertion ? oldDesc + add : oldDesc;
                Reloc(newDesc + WdVars, strict: true); // empty table start stays before the inserted descriptor
                Reloc(newDesc + WdIntPool);
                Reloc(newDesc + WdFloatPool);
                Reloc(newDesc + WdEntryTable);
                Reloc(newDesc + WdJumpTable);
            }

            return output;
        }

        /// <summary>Append one script variable descriptor pointing to an EXISTING free private slot. This grows only
        /// the AiFile variable table and relocates AiFile-relative offsets; it does not grow worker private storage.
        /// Proven offline by AiScriptLab --var-grow. RT2 is still required before public recipes rely on it.</summary>
        public static byte[] AppendPrivateVariableDescriptor(AiScriptFile script, int slot, int typeId = 1) =>
            AppendVariableDescriptor(script, 0x56, slot, typeId, allowDuplicateDescriptor: false);

        /// <summary>GROW primitive (AI Assembler): append raw instruction bytes to the END of the code and
        /// relocate every data-section offset by +delta, producing a valid LARGER AiFile. Existing code,
        /// entrypoints and jump targets are untouched (append-only), so a caller can route a jump
        /// (EditJumpTarget) to the new block at code-relative offset == the OLD CodeLength. This is how you
        /// ADD behavior beyond the byte-local (same-length) budget. The result re-parses cleanly with Read().</summary>
        public static byte[] AppendCode(AiScriptFile script, byte[] appendInstrBytes)
        {
            ArgumentNullException.ThrowIfNull(script);
            ArgumentNullException.ThrowIfNull(appendInstrBytes);
            byte[] ai = script.OriginalAiFileBytes;
            if (appendInstrBytes.Length == 0) return ai.ToArray();

            int scriptStart = script.ScriptStart;
            int oldCodeLen = script.CodeLength;
            int dataStart = scriptStart + oldCodeLen;   // first byte of the data section (AiFile-relative)
            int delta = appendInstrBytes.Length;
            if (dataStart > ai.Length)
                throw new InvalidOperationException($"data section start 0x{dataStart:X} past EOF (len 0x{ai.Length:X}).");

            // header + old code | appended code | data section (shifted by +delta)
            byte[] o = new byte[ai.Length + delta];
            Array.Copy(ai, 0, o, 0, dataStart);
            Array.Copy(appendInstrBytes, 0, o, dataStart, delta);
            Array.Copy(ai, dataStart, o, dataStart + delta, ai.Length - dataStart);

            // Bump any AiFile-relative offset that points into the (now shifted) data section.
            void Reloc(int off)
            {
                if (off < 0 || off + 4 > o.Length) return;
                uint v = U32(o, off);
                if (v >= (uint)dataStart) WriteU32(o, off, v + (uint)delta);
            }

            // Header: codeLen, declared/total length, and the worker-offset table (entries point at descriptors).
            WriteU32(o, OffCodeLength, (uint)(oldCodeLen + delta));
            WriteU32(o, OffDeclaredLength, U32(o, OffDeclaredLength) + (uint)delta);
            int workerCount = U16(ai, OffWorkerCount);
            for (int k = 0; k < workerCount; k++) Reloc(OffWorkerOffsetTable + 4 * k);

            // Each worker descriptor's data-pointing fields: varsOff(+0x14), intPool(+0x18), floatPool(+0x1C),
            // entrypoint-table(+0x20), jump-table(+0x24). Table CONTENTS are code-relative -> untouched (append).
            for (int k = 0; k < workerCount; k++)
            {
                int desc = (int)U32(ai, OffWorkerOffsetTable + 4 * k);   // original descriptor offset
                if (desc < dataStart) continue;
                int d = desc + delta;                                   // its position in the new file
                Reloc(d + 0x14); Reloc(d + 0x18); Reloc(d + 0x1C); Reloc(d + WdEntryTable); Reloc(d + WdJumpTable);
            }
            return o;
        }

        /// <summary>FULL AI ASSEMBLER — rebuild the AiFile from an edited instruction list (insert / remove /
        /// modify instructions ANYWHERE), automatically relocating ALL code-relative targets (entrypoints +
        /// jump tables) via an old->new offset map, plus the post-code data section. The list may mix:
        ///  - EXISTING instructions (their <see cref="AiInstruction.Offset"/> identifies the old position), and
        ///  - INSERTED instructions with Offset &lt; 0 (brand new; jumps reach them only if explicitly retargeted).
        /// A branch/entrypoint that targets a REMOVED instruction throws (dangling). A no-edit rebuild is
        /// byte-identical (RT0). This is "edit the AI 100% free".</summary>
        public static byte[] Rebuild(AiScriptFile script, IReadOnlyList<AiInstruction> newInstrs)
        {
            ArgumentNullException.ThrowIfNull(script);
            ArgumentNullException.ThrowIfNull(newInstrs);
            byte[] ai = script.OriginalAiFileBytes;
            int scriptStart = script.ScriptStart;
            int oldCodeLen = script.CodeLength;
            int oldDataStart = scriptStart + oldCodeLen;

            // 1. Emit new code + build old->new code-relative offset map (for instructions with a known old pos).
            var map = new Dictionary<int, int>();
            var code = new List<byte>(oldCodeLen + 16);
            int posRel = 0;
            foreach (AiInstruction ins in newInstrs)
            {
                if (ins.Offset >= 0) map[ins.Offset - scriptStart] = posRel;   // old code-rel -> new code-rel
                code.AddRange(ins.Emit());
                posRel += ins.Length;
            }
            int newCodeLen = posRel;
            int delta = newCodeLen - oldCodeLen;

            int Remap(int codeRelTarget)
            {
                if (map.TryGetValue(codeRelTarget, out int nt)) return nt;
                if (codeRelTarget == oldCodeLen) return newCodeLen;            // one-past-end (loop/return sentinels)
                throw new InvalidOperationException($"branch/entrypoint target 0x{codeRelTarget:X} hits a removed instruction.");
            }

            // 2. Assemble: header (unchanged region) + new code + data section (shifted by the code-length delta).
            byte[] o = new byte[scriptStart + newCodeLen + (ai.Length - oldDataStart)];
            Array.Copy(ai, 0, o, 0, scriptStart);
            code.CopyTo(o, scriptStart);
            Array.Copy(ai, oldDataStart, o, scriptStart + newCodeLen, ai.Length - oldDataStart);

            // 3. Header scalars + relocate every data-section POINTER (>= oldDataStart) by +delta.
            WriteU32(o, OffCodeLength, (uint)newCodeLen);
            WriteU32(o, OffDeclaredLength, U32(o, OffDeclaredLength) + (uint)delta);
            void RelocPtr(int off) { if (off >= 0 && off + 4 <= o.Length) { uint v = U32(o, off); if (v >= (uint)oldDataStart) WriteU32(o, off, v + (uint)delta); } }
            int workerCount = U16(ai, OffWorkerCount);
            for (int k = 0; k < workerCount; k++) RelocPtr(OffWorkerOffsetTable + 4 * k);
            for (int k = 0; k < workerCount; k++)
            {
                int desc = (int)U32(ai, OffWorkerOffsetTable + 4 * k);
                int d = desc >= oldDataStart ? desc + delta : desc;
                RelocPtr(d + 0x14); RelocPtr(d + 0x18); RelocPtr(d + 0x1C); RelocPtr(d + WdEntryTable); RelocPtr(d + WdJumpTable);
            }

            // 4. Remap entrypoint + jump TABLE CONTENTS (code-relative) via the old->new map (after the table
            //    pointers themselves were relocated in step 3, so they point at the right place now).
            for (int k = 0; k < workerCount; k++)
            {
                int desc = (int)U32(ai, OffWorkerOffsetTable + 4 * k);
                int d = desc >= oldDataStart ? desc + delta : desc;
                if (d + WdJumpTable + 4 > o.Length) continue;
                int entries = U16(o, d + WdEntrypoints), jumps = U16(o, d + WdJumps);
                int entryTab = (int)U32(o, d + WdEntryTable), jumpTab = (int)U32(o, d + WdJumpTable);
                for (int i = 0; i < entries; i++) { int off = entryTab + 4 * i; if (off >= 0 && off + 4 <= o.Length) WriteU32(o, off, (uint)Remap((int)U32(o, off))); }
                for (int i = 0; i < jumps; i++)   { int off = jumpTab + 4 * i;  if (off >= 0 && off + 4 <= o.Length) WriteU32(o, off, (uint)Remap((int)U32(o, off))); }
            }
            return o;
        }

        // ---- conditional control-flow assembler (adds NEW branches: grow a worker's jump-table) ----

        /// <summary>JUMP-TABLE GROW — append <paramref name="newCodeRelativeTargets"/>.Count NEW slots to
        /// worker[workerIndex]'s jump-table (holding those CODE-RELATIVE targets), bump its jumpCount@desc+0x0A,
        /// and relocate everything physically after the insertion point by +4N. This is the missing primitive that
        /// lets a snippet ADD a branch (B0/D6/D7): a branch references a slot by INDEX, so a new branch needs a new
        /// slot. The new slots' indices are [oldJumpCount .. oldJumpCount+N).
        ///
        /// STRUCTURE (empirically proven 346/346, confirmed in the design pass): per worker jumpTab == entryTab +
        /// entries*4 (the jump-table sits immediately after the entry-table); ALL workers' entry/jump tables are
        /// packed densely at the END of the post-code DATA section in worker order with no gaps; the descriptors,
        /// variables and const pools all live in the HEADER region [0..scriptStart) and NEVER move. So inserting 4N
        /// bytes at (this worker's jumpTab + oldJumps*4) shifts only the later workers' tables + the trailer; every
        /// data-section POINTER whose value >= the insertion offset is bumped by +4N (this worker's OWN entry/jump
        /// pointers are left as-is: its table grows in place). A NO-OP grow returns the input verbatim (RT0).
        /// Returns a new, larger AiFile that re-parses cleanly with Read().</summary>
        public static byte[] GrowWorkerJumpTable(AiScriptFile script, int workerIndex, IReadOnlyList<int> newCodeRelativeTargets)
        {
            ArgumentNullException.ThrowIfNull(script);
            ArgumentNullException.ThrowIfNull(newCodeRelativeTargets);
            byte[] ai = script.OriginalAiFileBytes;
            if (newCodeRelativeTargets.Count == 0) return ai.ToArray();
            if (workerIndex < 0 || workerIndex >= script.Workers.Count)
                throw new ArgumentOutOfRangeException(nameof(workerIndex));
            foreach (int t in newCodeRelativeTargets)
                if (t < 0 || t > script.CodeLength)
                    throw new ArgumentOutOfRangeException(nameof(newCodeRelativeTargets),
                        $"jump target 0x{t:X} is not a valid code offset (<= CodeLength 0x{script.CodeLength:X}).");

            AiWorker w = script.Workers[workerIndex];
            int desc = w.DescriptorOffset;
            int oldJumps = U16(ai, desc + WdJumps);
            int jumpTab = (int)U32(ai, desc + WdJumpTable);
            int n = newCodeRelativeTargets.Count;
            int add = 4 * n;
            int insertion = jumpTab + 4 * oldJumps;   // end of this worker's current jump table
            if (jumpTab < 0 || insertion < 0 || insertion > ai.Length)
                throw new InvalidOperationException($"jump-table insertion offset 0x{insertion:X} out of range (len 0x{ai.Length:X}).");

            byte[] o = new byte[ai.Length + add];
            Array.Copy(ai, 0, o, 0, insertion);
            for (int k = 0; k < n; k++) WriteU32(o, insertion + 4 * k, (uint)newCodeRelativeTargets[k]);
            Array.Copy(ai, insertion, o, insertion + add, ai.Length - insertion);

            // bump this worker's jumpCount (u16 @ desc+0x0A); the descriptor is in the header, it did not move.
            ushort newJumps = (ushort)(oldJumps + n);
            o[desc + WdJumps] = (byte)(newJumps & 0xFF);
            o[desc + WdJumps + 1] = (byte)((newJumps >> 8) & 0xFF);

            // totalLength@0x10 grows; codeLength@0x00 is unchanged (the code region did not grow).
            WriteU32(o, OffDeclaredLength, U32(o, OffDeclaredLength) + (uint)add);

            // Relocate every data-section pointer whose value >= insertion by +add — EXCEPT this worker's own
            // entry/jump-table pointers (its table extends in place; its entryTab is before the insertion). The
            // worker-offset table + every OTHER worker's vars/pools/entryTab/jumpTab move iff they sit after the cut.
            void Reloc(int off) { if (off >= 0 && off + 4 <= o.Length) { uint v = U32(o, off); if (v >= (uint)insertion) WriteU32(o, off, v + (uint)add); } }
            int workerCount = U16(ai, OffWorkerCount);
            for (int k = 0; k < workerCount; k++) Reloc(OffWorkerOffsetTable + 4 * k);   // descriptors are in header -> won't move
            for (int k = 0; k < workerCount; k++)
            {
                int d = (int)U32(ai, OffWorkerOffsetTable + 4 * k);   // descriptors don't move (header)
                Reloc(d + 0x14); Reloc(d + 0x18); Reloc(d + 0x1C);    // vars / intPool / floatPool (all in header anyway)
                if (k == workerIndex) continue;                       // leave the grown worker's own table pointers
                Reloc(d + WdEntryTable); Reloc(d + WdJumpTable);
            }
            return o;
        }

        /// <summary>CONDITIONAL ASSEMBLER — prepend a guarded action to an existing worker entrypoint.
        /// Default mode preserves the original handler. Builds, appended at the end of the code:
        ///   [guard]            (must leave exactly ONE boolean on the stack)
        ///   D7 POPXNCJMP -&gt; rejoin   (if the guard is FALSE, skip the action)
        ///   [action]
        ///   rejoin: B0 JMP -&gt; original-entrypoint-code   (run the original handler either way)
        /// then repoints worker[workerIndex].entrypoint[entrypointIndex] to the start of that block, and GROWS the
        /// worker's jump-table by the 2 new slots (skip-target + original-entrypoint). Result: when the event fires,
        /// the guard runs first; if true the action executes; either way control falls through to the original code.
        /// This is how "counterattack on hit", "enrage on turn N", "rotate command by RNG" become one insertion.
        ///
        /// If stopAfterAction is true, the generated block becomes:
        ///   [guard]
        ///   D7 POPXNCJMP -&gt; original-entrypoint-code
        ///   [action]
        ///   RET
        /// This means a failed guard still continues into the older/original chain, but a successful action stops
        /// that pass. This is the human "Pare aqui" used to avoid accidental infinite-looking hook chains.
        ///
        /// STRUCTURALLY proven offline (the AiFile re-parses, the walk closes, no dangling). The guard/action ops
        /// being SEMANTICALLY correct (stack-balanced, right fields) is the caller's responsibility and the in-game
        /// (RT2) behaviour must be confirmed via the probe before shipping a specific template as production.</summary>
        public static byte[] AppendGuardedAction(AiScriptFile script, int workerIndex, int entrypointIndex,
            IReadOnlyList<AiInstruction> guard, IReadOnlyList<AiInstruction> action, bool stopAfterAction = false)
        {
            ArgumentNullException.ThrowIfNull(script);
            ArgumentNullException.ThrowIfNull(guard);
            ArgumentNullException.ThrowIfNull(action);
            if (workerIndex < 0 || workerIndex >= script.Workers.Count)
                throw new ArgumentOutOfRangeException(nameof(workerIndex));
            AiWorker w = script.Workers[workerIndex];
            if (entrypointIndex < 0 || entrypointIndex >= w.Entrypoints.Count)
                throw new ArgumentOutOfRangeException(nameof(entrypointIndex));

            int baseRel = script.CodeLength;                 // the appended block starts here (code-relative)
            int originalEntry = w.Entrypoints[entrypointIndex];
            int oldJumps = U16(script.OriginalAiFileBytes, w.DescriptorOffset + WdJumps);
            int slotRejoin = oldJumps;        // default: slot holding rejoin; stop mode: slot holding skip-to-original
            int slotOriginal = oldJumps + 1;  // default-only: slot holding the original-entrypoint offset

            int guardLen = guard.Sum(i => i.Length);
            int actionLen = action.Sum(i => i.Length);
            int rejoinOff = baseRel + guardLen + 3 + actionLen;   // the B0 JMP that rejoins the original handler
            int stopAfterOff = rejoinOff;                          // the RET offset in stopAfterAction mode

            AiInstruction Jump(byte opcode, int slot) => new AiInstruction
            { Offset = -1, Opcode = opcode, HasOperand = true, Operand = (ushort)slot, OperandKind = AiOperandKind.JumpIndex };

            // Assemble the block as instructions and APPEND via Rebuild (the proven relocator — it correctly
            // relocates the data-section pointers of header-resident worker descriptors, which AppendCode skips).
            var newInstrs = new List<AiInstruction>(script.Instructions);
            newInstrs.AddRange(guard);
            newInstrs.Add(Jump(0xD7, slotRejoin));        // POPXNCJMP -> rejoin/skip target (skip the action if guard is false)
            newInstrs.AddRange(action);
            if (stopAfterAction)
            {
                newInstrs.Add(new AiInstruction
                { Offset = -1, Opcode = 0x3C, HasOperand = false, Operand = 0, OperandKind = OperandKindOf(0x3C) });
            }
            else
            {
                newInstrs.Add(Jump(0xB0, slotOriginal));  // JMP -> original entrypoint code (run the original handler)
            }
            byte[] rebuilt = Rebuild(script, newInstrs);

            // Repoint the chosen entrypoint to the block start (its entry-table slot holds a code-relative offset).
            AiScriptFile s2 = Read(rebuilt);
            WriteEntrypointOffset(rebuilt, s2, workerIndex, entrypointIndex, baseRel);

            if (stopAfterAction)
            {
                // Grow by one slot: failed guard skips to the older/original chain; successful guard ends at RET.
                // SENTINEL REMAP mirrors the default mode: if the original entrypoint was the old one-past-end,
                // route to the new one-past-end just past RET instead of back to the appended block start.
                int skipTarget = originalEntry == baseRel ? stopAfterOff + 1 : originalEntry;
                byte[] grown = GrowWorkerJumpTable(Read(rebuilt), workerIndex, new[] { skipTarget });
                AiScriptFile s3 = Read(grown);
                WriteEntrypointOffset(grown, s3, workerIndex, entrypointIndex, baseRel);
                return grown;
            }

            // Grow the worker's jump-table by the two new slots (rejoin target, original-entry target).
            // SENTINEL REMAP: an entrypoint whose value == CodeLength is the "empty/return handler" one-past-end
            // sentinel (the case Rebuild.Remap handles specially). Routing the rejoin B0 to that raw value would
            // point at baseRel (== old CodeLength == the block start) -> an infinite loop. Map it to the NEW
            // one-past-end (just past the appended B0 = rejoinOff+3) so the rejoin falls through / returns instead.
            int originalTarget = originalEntry == baseRel ? rejoinOff + 3 : originalEntry;
            byte[] grown2 = GrowWorkerJumpTable(Read(rebuilt), workerIndex, new[] { rejoinOff, originalTarget });
            AiScriptFile s4 = Read(grown2);
            // GrowWorkerJumpTable can shift data-section layout; repoint AFTER grow so onTurn hits the new block.
            WriteEntrypointOffset(grown2, s4, workerIndex, entrypointIndex, baseRel);
            return grown2;
        }

        /// <summary>CONDITIONAL ASSEMBLER (TOP-INJECT) — insert a guarded action at the PHYSICAL START of an
        /// existing worker entrypoint instead of appending it at the end of the code. This is used for
        /// battle-start openers where visual/runtime order should stay "top-most" in the disassembly and a
        /// successful action can simply fall through into the older entrypoint body (or RET early when
        /// <paramref name="stopAfterAction"/> is true).
        ///
        /// Shape:
        ///   [guard]
        ///   D7 POPXNCJMP -> old-entrypoint-body
        ///   [action]
        ///   [RET if stopAfterAction]
        ///   old-entrypoint-body...
        ///
        /// The chosen entrypoint is repointed to the inserted block start (which occupies the ORIGINAL
        /// code-relative entrypoint offset after the rebuild). Existing entrypoints/jumps that targeted the old
        /// body are remapped by Rebuild to the shifted body automatically.</summary>
        public static byte[] PrependGuardedActionAtEntrypointTop(AiScriptFile script, int workerIndex, int entrypointIndex,
            IReadOnlyList<AiInstruction> guard, IReadOnlyList<AiInstruction> action, bool stopAfterAction = false)
        {
            ArgumentNullException.ThrowIfNull(script);
            ArgumentNullException.ThrowIfNull(guard);
            ArgumentNullException.ThrowIfNull(action);
            if (workerIndex < 0 || workerIndex >= script.Workers.Count)
                throw new ArgumentOutOfRangeException(nameof(workerIndex));
            AiWorker w = script.Workers[workerIndex];
            if (entrypointIndex < 0 || entrypointIndex >= w.Entrypoints.Count)
                throw new ArgumentOutOfRangeException(nameof(entrypointIndex));

            int originalEntry = w.Entrypoints[entrypointIndex];
            if (originalEntry < 0 || originalEntry > script.CodeLength)
                throw new InvalidOperationException($"entrypoint target 0x{originalEntry:X} fora do code range.");

            int insertionIndex;
            if (originalEntry == script.CodeLength)
            {
                insertionIndex = script.Instructions.Count;
            }
            else
            {
                int originalAbs = script.ScriptStart + originalEntry;
                insertionIndex = -1;
                for (int i = 0; i < script.Instructions.Count; i++)
                {
                    if (script.Instructions[i].Offset == originalAbs)
                    {
                        insertionIndex = i;
                        break;
                    }
                }

                if (insertionIndex < 0)
                    throw new InvalidOperationException(
                        string.Format(Strings.U_Ai_EntrypointNotInstr, originalEntry));
            }

            int oldJumps = U16(script.OriginalAiFileBytes, w.DescriptorOffset + WdJumps);
            int slotSkipToOriginal = oldJumps;
            int guardLen = guard.Sum(i => i.Length);
            int actionLen = action.Sum(i => i.Length);
            int insertedLen = guardLen + 3 + actionLen + (stopAfterAction ? 1 : 0);
            int skipTarget = originalEntry + insertedLen;

            AiInstruction Jump(byte opcode, int slot) => new AiInstruction
            { Offset = -1, Opcode = opcode, HasOperand = true, Operand = (ushort)slot, OperandKind = AiOperandKind.JumpIndex };

            var newInstrs = new List<AiInstruction>(script.Instructions.Count + guard.Count + action.Count + 2);
            for (int i = 0; i < insertionIndex; i++)
                newInstrs.Add(script.Instructions[i]);
            newInstrs.AddRange(guard);
            newInstrs.Add(Jump(0xD7, slotSkipToOriginal)); // guard false -> original entrypoint body
            newInstrs.AddRange(action);
            if (stopAfterAction)
            {
                newInstrs.Add(new AiInstruction
                { Offset = -1, Opcode = 0x3C, HasOperand = false, Operand = 0, OperandKind = OperandKindOf(0x3C) });
            }
            for (int i = insertionIndex; i < script.Instructions.Count; i++)
                newInstrs.Add(script.Instructions[i]);

            byte[] rebuilt = Rebuild(script, newInstrs);
            AiScriptFile s2 = Read(rebuilt);
            WriteEntrypointOffset(rebuilt, s2, workerIndex, entrypointIndex, originalEntry);

            byte[] grown = GrowWorkerJumpTable(Read(rebuilt), workerIndex, new[] { skipTarget });
            AiScriptFile s3 = Read(grown);
            WriteEntrypointOffset(grown, s3, workerIndex, entrypointIndex, originalEntry);
            return grown;
        }

        static void WriteEntrypointOffset(byte[] ai, AiScriptFile script, int workerIndex, int entrypointIndex, int codeRelTarget)
        {
            int entryTab = (int)U32(ai, script.Workers[workerIndex].DescriptorOffset + WdEntryTable);
            WriteU32(ai, entryTab + 4 * entrypointIndex, (uint)codeRelTarget);
        }

        /// <summary>Splice an edited AiFile blob back into a full monster_*.bin (must be the SAME length —
        /// operand edits never change length). Returns the new monster bytes; only the AiFile region
        /// [AiFilePointer..WorkerFilePointer) changes, every other section is preserved verbatim.</summary>
        public static byte[] SpliceAiFileIntoMonster(byte[] monsterBin, byte[] newAiFile)
        {
            ArgumentNullException.ThrowIfNull(monsterBin);
            ArgumentNullException.ThrowIfNull(newAiFile);
            if (monsterBin.Length < 0x30) throw new InvalidOperationException("monster bin too small.");
            int aiPtr = (int)U32(monsterBin, 0x04);
            int workerPtr = (int)U32(monsterBin, 0x08);
            if (aiPtr <= 0 || workerPtr <= aiPtr || workerPtr > monsterBin.Length)
                throw new InvalidOperationException("monster bin has no AI partition.");
            if (newAiFile.Length != workerPtr - aiPtr)
                throw new InvalidOperationException(
                    $"edited AiFile length {newAiFile.Length} != original {workerPtr - aiPtr} (operand edits must preserve length).");
            byte[] outBin = (byte[])monsterBin.Clone();
            Array.Copy(newAiFile, 0, outBin, aiPtr, newAiFile.Length);
            return outBin;
        }

        /// <summary>GROW/SHRINK-aware splice (AI Assembler structural save): replace the AiFile partition with
        /// one of a DIFFERENT length, shift every later section by +delta, and rewrite all section pointers +
        /// FileSize in the 0x34 monster header. The AiFile is written VERBATIM (the header prefix is copied
        /// first, then the AiFile overwrites the overlapping Padding[0x30..0x34) bytes) so AiFile[0..4)=codeLength
        /// is NOT clobbered. The rebuilt AiFile is zero-padded up to a 16-byte boundary so the WorkerFile stays
        /// 16-aligned (corpus invariant + the game heap is 16-aligned). RE-proven safe: the loader sizes the VM
        /// from worker counts + per-worker datalen (NOT the partition size / DeclaredLength@0x10 / codeLength@0x00),
        /// so trailing AiFile pad is never read; and it resolves sections via the in-file header pointers, so the
        /// relocation is honored. See docs/reverse/FFX_AIFILE_LOADER_RENAME_QUEUE_2026-06-05.md.
        /// Only the AiFile region + the header section-pointer ints change; every other section byte is preserved.</summary>
        public static byte[] SpliceAiFileIntoMonsterGrow(byte[] monsterBin, byte[] newAiFile)
        {
            ArgumentNullException.ThrowIfNull(monsterBin);
            ArgumentNullException.ThrowIfNull(newAiFile);
            if (monsterBin.Length < 0x34) throw new InvalidOperationException("monster bin too small.");
            int aiPtr = (int)U32(monsterBin, 0x04);
            int workerPtr = (int)U32(monsterBin, 0x08);
            if (aiPtr != 0x30)
                throw new InvalidOperationException($"unexpected AiFilePointer 0x{aiPtr:X} (expected 0x30).");
            if (workerPtr <= aiPtr || workerPtr > monsterBin.Length)
                throw new InvalidOperationException("monster bin has no AI partition.");

            // Authoritative size = the actual file length: for real monster files FileSize@0x20 == length
            // (RE-confirmed across the corpus), and using length preserves EVERY byte after the AiFile partition
            // (no trailing-slack drop). FileSize@0x20 is rewritten to the new total below.
            int oldFileSize = monsterBin.Length;

            // Pad the AiFile partition up to a 16-byte boundary so WorkerFilePointer stays 16-aligned.
            int padded = (newAiFile.Length + 0xF) & ~0xF;
            int oldPartition = workerPtr - aiPtr;
            int delta = padded - oldPartition;

            // No size change after padding: verbatim same-length splice of the (padded) AiFile, no relocation.
            if (delta == 0)
                return SpliceAiFileIntoMonster(monsterBin, ZeroPad(newAiFile, padded));

            byte[] outBin = new byte[oldFileSize + delta];
            // 1) header prefix [0,0x30) verbatim (Signature + 7 pointers + FileSize + Padding[0..12)).
            Array.Copy(monsterBin, 0, outBin, 0, aiPtr);
            // 2) new (zero-padded) AiFile at 0x30 — its codeLength@0..4 overwrites the overlapping Padding[12..16).
            Array.Copy(newAiFile, 0, outBin, aiPtr, newAiFile.Length);   // [aiPtr+newLen .. aiPtr+padded) already 0
            // 3) every later section, shifted by +delta.
            Array.Copy(monsterBin, workerPtr, outBin, aiPtr + padded, oldFileSize - workerPtr);
            // 4) rewrite the section pointers (>= old workerPtr) + FileSize. AiFilePointer@0x04 (=0x30) is unchanged.
            foreach (int ptrOff in new[] { 0x08, 0x0C, 0x10, 0x14, 0x18, 0x1C })
            {
                uint v = U32(outBin, ptrOff);
                if (v >= (uint)workerPtr) WriteU32(outBin, ptrOff, v + (uint)delta);
            }
            WriteU32(outBin, 0x20, (uint)(oldFileSize + delta));
            return outBin;
        }

        static byte[] ZeroPad(byte[] src, int len)
        {
            if (src.Length >= len) return src;
            byte[] o = new byte[len];
            Array.Copy(src, 0, o, 0, src.Length);
            return o;
        }

        // ---- human-readable listing (for the editor / debugging) ----
        // Mnemonics follow the established FFX ATEL naming (FFXDataParser); the temp-register banks are
        // contiguous ranges. Our length table/operand width are proven; these names are the community
        // convention to keep disassembly recognizable.

        public static string Mnemonic(byte opcode)
        {
            // Temp-register banks: 59-5C POPI0-3, 5D-66 POPF0-9, 67-6A PUSHI0-3, 6B-74 PUSHF0-9.
            if (opcode >= 0x59 && opcode <= 0x5C) return $"POPI{opcode - 0x59}";
            if (opcode >= 0x5D && opcode <= 0x66) return $"POPF{opcode - 0x5D}";
            if (opcode >= 0x67 && opcode <= 0x6A) return $"PUSHI{opcode - 0x67}";
            if (opcode >= 0x6B && opcode <= 0x74) return $"PUSHF{opcode - 0x6B}";
            return OpNames.TryGetValue(opcode, out string? n) ? n : $"op_{opcode:X2}";
        }

        /// <summary>FUNCSPACE of a call func-id = its high nibble (cross-checked vs FFXDataParser).</summary>
        public static string CallNamespace(ushort funcId) => (funcId >> 12) switch
        {
            0x0 => "Common", 0x1 => "Math", 0x4 => "SgEvent", 0x5 => "ChEvent", 0x6 => "Camera",
            0x7 => "Battle", 0x8 => "Map", 0x9 => "Mount", 0xB => "Movie", 0xC => "Debug", 0xD => "AbilityMap",
            _ => "ns" + (funcId >> 12).ToString("X"),
        };

        /// <summary>Friendly name for a few high-traffic call func-ids (else the hex id).</summary>
        public static string CallName(ushort funcId) =>
            CallTargets.TryGetValue(funcId, out string? n) ? n : $"{funcId:X4}h";

        /// <summary>Just the human-readable operand gloss (no offset/raw/mnemonic). Used by Format and by the
        /// editor's per-row "meaning" so the user never has to read a raw hex id.</summary>
        public static string OperandGloss(AiInstruction i) => i.HasOperand ? OperandGloss(i.Opcode, i.Operand) : "";

        /// <summary>Operand gloss from a RAW opcode+operand (for the assembler rows, which hold bytes rather
        /// than a parsed AiInstruction). Kind comes from <see cref="OperandKindOf"/>.</summary>
        public static string OperandGloss(byte opcode, ushort operand)
        {
            if (!IsOperandBearing(opcode)) return "";
            return OperandKindOf(opcode) switch
            {
                AiOperandKind.FuncId => $"{CallNamespace(operand)}.{CallName(operand)}",
                AiOperandKind.JumpIndex => $"→ jump[{operand}]",
                AiOperandKind.VarLoad => $"var[{operand}]",
                AiOperandKind.VarStore => $"var[{operand}]",
                AiOperandKind.ArrLoad => $"arr[{operand}]",
                AiOperandKind.ArrStore => $"arr[{operand}]",
                AiOperandKind.Immediate => ImmediateGloss(operand),
                AiOperandKind.FloatConst => $"fconst[{operand}]",
                AiOperandKind.IntConst => $"iconst[{operand}]",
                _ => $"{operand:X4}h",
            };
        }

        // A PUSHII immediate, annotated with the btlActor TARGET name when it is one of the unambiguous negative
        // sentinels (0xFFE9..0xFFFF = -23..-1; AiTargetNames). Restricted to that range so common positive immediates
        // are never mislabelled as targets.
        static string ImmediateGloss(ushort operand)
        {
            string g = $"{operand}  [{operand:X}h]";
            if (operand >= 0xFFE9)
            {
                string? t = AiTargetNames.Get(operand);
                if (t != null) return $"{g}  (alvo: {t})";
            }
            return g;
        }

        /// <summary>Short plain-language help for an opcode (by mnemonic family) — for editor tooltips so the
        /// user isn't guessing what PUSHII / CALLPOPA / POPI mean.</summary>
        public static string OpcodeHelp(byte opcode)
        {
            string m = Mnemonic(opcode);
            // Operand-bearing opcodes: classify by the PROVEN operand kind (not fragile mnemonic prefixes —
            // e.g. 0xAF "PUSHF" is a float-CONSTANT push, not a "PUSHF#" register bank). OperandKindOf is authoritative.
            if (IsOperandBearing(opcode))
            {
                return OperandKindOf(opcode) switch
                {
                    AiOperandKind.FuncId => string.Format(Strings.U_Ai_OpcodeHelpFuncId, m),
                    AiOperandKind.JumpIndex => string.Format(Strings.U_Ai_OpcodeHelpJump, m),
                    AiOperandKind.Immediate or AiOperandKind.IntConst => string.Format(Strings.U_Ai_OpcodeHelpInt, m),
                    AiOperandKind.FloatConst => string.Format(Strings.U_Ai_OpcodeHelpFloat, m),
                    AiOperandKind.VarLoad => string.Format(Strings.U_Ai_OpcodeHelpVarLoad, m),
                    AiOperandKind.VarStore => string.Format(Strings.U_Ai_OpcodeHelpVarStore, m),
                    AiOperandKind.ArrLoad => string.Format(Strings.U_Ai_OpcodeHelpArrLoad, m),
                    AiOperandKind.ArrStore => string.Format(Strings.U_Ai_OpcodeHelpArrStore, m),
                    _ => string.Format(Strings.U_Ai_OpcodeHelpGeneric, m, OperandKindOf(opcode)),
                };
            }
            // No operand: temp-register banks (PUSHI#/PUSHF#/POPI#/POPF#) + control ops.
            if (m.StartsWith("POPI")) return string.Format(Strings.U_Ai_OpcodeHelpPopi, m);
            if (m.StartsWith("POPF")) return string.Format(Strings.U_Ai_OpcodeHelpPopf, m);
            if (m.StartsWith("PUSHI")) return string.Format(Strings.U_Ai_OpcodeHelpPushI, m);
            if (m.StartsWith("PUSHF")) return string.Format(Strings.U_Ai_OpcodeHelpPushF, m);
            return string.Format(Strings.U_Ai_OpcodeHelpFallback, opcode, m);
        }

        /// <summary>One-line disassembly of an instruction (offset, raw hex, mnemonic, operand gloss).</summary>
        public static string Format(AiInstruction i)
        {
            string raw = Convert.ToHexString(i.Emit());
            string badge = DecodeStatusOf(i.Opcode) == AiInstructionDecodeStatus.Confirmed ? "" : "? ";
            if (!i.HasOperand)
                return $"{badge}{i.Offset:X4}  {raw,-6}  {Mnemonic(i.Opcode)}";
            return $"{badge}{i.Offset:X4}  {raw,-6}  {Mnemonic(i.Opcode),-10} {OperandGloss(i)}";
        }

        public static string Disassemble(AiScriptFile script)
        {
            var sb = new StringBuilder();
            sb.Append($"; AiFile len=0x{script.OriginalAiFileBytes.Length:X} declared=0x{script.DeclaredLength:X} ")
              .Append($"workers={script.Workers.Count} scriptStart=0x{script.ScriptStart:X} ")
              .Append($"code=0x{script.CodeLength:X} instructions={script.Instructions.Count}\n");
            foreach (AiWorker w in script.Workers)
            {
                sb.Append($"; worker {w.Index} [{w.InferredType ?? "?"}~slot{w.InferredSlot:X2}]: entrypoints[")
                  .Append(string.Join(" ", w.Entrypoints.Select(e => $"0x{script.ScriptStart + e:X}")))
                  .Append($"] jumps[")
                  .Append(string.Join(" ", w.JumpTargets.Select(j => $"0x{script.ScriptStart + j:X}")))
                  .Append("]\n");
            }
            // Label map: mark entrypoint offsets (wW_eI) and jump-target offsets (L_xxxx) so the listing
            // reads with control-flow anchors (entrypoint/jump targets are code-relative -> add ScriptStart).
            var labels = new Dictionary<int, string>();
            foreach (AiWorker w in script.Workers)
            {
                for (int ei = 0; ei < w.Entrypoints.Count; ei++)
                {
                    int abs = script.ScriptStart + w.Entrypoints[ei];
                    if (!labels.ContainsKey(abs)) labels[abs] = $"w{w.Index}_e{ei}";
                }
                foreach (int jt in w.JumpTargets)
                {
                    int abs = script.ScriptStart + jt;
                    if (!labels.ContainsKey(abs)) labels[abs] = $"L_{abs:X4}";
                }
            }
            IReadOnlyDictionary<int, IReadOnlyList<int>> instructionOwners = InstructionOwners(script);
            // Keep the original range rule only as a fallback for unreachable/dead code. Appended guarded blocks
            // are physically at the end of the code, so their real owner must come from the CFG walk above.
            var order = script.Workers
                .Where(w => w.Entrypoints.Count > 0)
                .Select(w => (start: script.ScriptStart + w.Entrypoints.Min(), worker: w))
                .OrderBy(t => t.start)
                .ToList();
            AiWorker? OwnerOf(int off)
            {
                if (instructionOwners.TryGetValue(off, out IReadOnlyList<int>? exact) && exact.Count == 1)
                    return script.Workers[exact[0]];
                AiWorker? cur = null;
                foreach (var (start, worker) in order) { if (off >= start) cur = worker; else break; }
                return cur;
            }

            for (int idx = 0; idx < script.Instructions.Count; idx++)
            {
                AiInstruction i = script.Instructions[idx];
                if (labels.TryGetValue(i.Offset, out string? label))
                    sb.Append(label).Append(":\n");

                string? gloss = ResolveGloss(script, i, OwnerOf, labels);
                // For a field-access dispatcher call, the field index is a fixed stack arg (getters i-1,
                // setters i-2). Append the field name — correct per the corpus call patterns.
                if (gloss != null && i.OperandKind == AiOperandKind.FuncId
                    && FieldArgBack.TryGetValue(i.Operand, out int back) && idx - back >= 0)
                {
                    AiInstruction fa = script.Instructions[idx - back];
                    if (fa.HasOperand && FieldNameForFunc(i.Operand, fa.Operand) is string fn) gloss += $"  .{fn}";
                }
                if (gloss != null)
                {
                    string raw = Convert.ToHexString(i.Emit());
                    sb.Append($"{i.Offset:X4}  {raw,-6}  {Mnemonic(i.Opcode),-10} {gloss}\n");
                }
                else
                    sb.Append(Format(i)).Append('\n');
            }
            return sb.ToString();
        }

        // Field-access dispatchers -> how many instructions back the field-index PUSHII sits (getters 1, setters 2).
        static readonly Dictionary<ushort, int> FieldArgBack = new()
        {
            [0x70AA] = 1, [0x70AC] = 1, [0x700F] = 1, [0x701A] = 1, [0x706C] = 1, // getters: AE<field> CALL  (700F: AE<chr> AE<field>)
            [0x70AB] = 2, [0x70B2] = 2, [0x7018] = 2,                           // setters: AE<field> AE/AF<value> CALL
        };

        // Resolve an instruction's operand to a human gloss: jump->label, AF->float value, AD->int value,
        // var load/store -> variable name. Returns null to fall back to the raw Format().
        static string? ResolveGloss(AiScriptFile s, AiInstruction i, Func<int, AiWorker?> ownerOf, Dictionary<int, string> labels)
        {
            switch (i.OperandKind)
            {
                case AiOperandKind.FuncId:
                    return $"{CallNamespace(i.Operand)}.{CallName(i.Operand)}";
                case AiOperandKind.JumpIndex:
                    AiWorker? owner = ownerOf(i.Offset);
                    if (owner != null && i.Operand < owner.JumpTargets.Count)
                    {
                        int t = s.ScriptStart + owner.JumpTargets[i.Operand];
                        return "-> " + (labels.TryGetValue(t, out string? ln) ? ln : $"0x{t:X}");
                    }
                    return null;
                case AiOperandKind.FloatConst:
                    if (s.FloatPoolOffset >= 0 && s.FloatPoolOffset + 4 * i.Operand + 4 <= s.OriginalAiFileBytes.Length)
                        return BitConverter.ToSingle(s.OriginalAiFileBytes, s.FloatPoolOffset + 4 * i.Operand)
                            .ToString(System.Globalization.CultureInfo.InvariantCulture);
                    return null;
                case AiOperandKind.IntConst:
                    if (s.IntPoolOffset >= 0 && s.IntPoolOffset + 4 * i.Operand + 4 <= s.OriginalAiFileBytes.Length)
                        return BitConverter.ToInt32(s.OriginalAiFileBytes, s.IntPoolOffset + 4 * i.Operand).ToString();
                    return null;
                case AiOperandKind.VarLoad:
                case AiOperandKind.VarStore:
                    return i.Operand < s.Variables.Count ? s.Variables[i.Operand].Name : null;
                default:
                    return null;
            }
        }

        // Opcode mnemonics (FFX ATEL convention). 0x80-bit opcodes carry a u16 operand.
        // Operations are opcode & 0x7F (native: FFX_Atel_InterpretWorkerOpcodes @0x864180 switch;
        // FFX_Atel_FetchOpcode @0x869D00 strips the 0x80 operand-flag). Names from that switch + ATEL.
        static readonly Dictionary<byte, string> OpNames = new()
        {
            [0x00] = "NOP", [0x01] = "LOR", [0x02] = "LAND", [0x03] = "OR", [0x04] = "XOR", [0x05] = "AND",
            [0x06] = "EQ", [0x07] = "NE", [0x08] = "GTU", [0x09] = "LSU",
            [0x0A] = "GT", [0x0B] = "LS", [0x0C] = "GTEU", [0x0D] = "LSEU",
            [0x0E] = "GTE", [0x0F] = "LSE",
            [0x10] = "BON", [0x11] = "BOFF",
            [0x12] = "SLL", [0x13] = "SRL", [0x14] = "ADD", [0x15] = "SUB",
            [0x16] = "MUL", [0x17] = "DIV",
            [0x18] = "MOD", [0x19] = "NOT", [0x1A] = "NEG", [0x1B] = "FIXADRS", [0x1C] = "BNOT",
            [0x25] = "POPA", [0x26] = "PUSHA",
            [0x28] = "PUSHX",
            // 0x29-0x2C: dual-purpose — stack register ops AND switch/case — keep our proven labels (automation
            // code matches "CASE"/"SWITCH" by string). Fahrenheit-Ghidra equivalents: PUSHY/POPX/REPUSH/POPY.
            [0x29] = "CASE", [0x2A] = "SETSW", [0x2B] = "RAND", [0x2C] = "SWITCH",
            [0x34] = "RTS",
            [0x36] = "REQ", [0x37] = "REQSW", [0x38] = "REQEW",
            [0x39] = "PREQ", [0x3A] = "PREQSW", [0x3B] = "PREQEW",
            [0x3C] = "RET", [0x3D] = "RETN", [0x3E] = "RETT", [0x3F] = "RETTN",
            [0x40] = "HALT",
            [0x45] = "FREQ", [0x46] = "TREQ", [0x47] = "BREQ", [0x48] = "BFREQ", [0x49] = "BTREQ",
            [0x4A] = "FREQSW", [0x4B] = "TREQSW", [0x4C] = "BREQSW", [0x4D] = "BFREQSW", [0x4E] = "BTREQSW",
            [0x4F] = "FREQEW", [0x50] = "TREQEW", [0x51] = "BREQEW", [0x52] = "BFREQEW", [0x53] = "BTREQEW",
            [0x54] = "DRET",
            [0x77] = "REQWAIT", [0x78] = "PREQWAIT", [0x79] = "REQCHG", [0x7A] = "ACTREQ",
            // Operand-bearing opcodes (0x80-bit). Register banks (POPI/POPF/PUSHI/PUSHF) handled inline in Mnemonic().
            [0x9D] = "LABEL", [0x9E] = "TAG",
            [0x9F] = "PUSHV", [0xA0] = "POPV", [0xA2] = "PUSHAR", [0xA3] = "POPAR",
            [0xA4] = "POPARL", [0xA7] = "PUSHARP",
            [0xAD] = "PUSHI", [0xAE] = "PUSHII", [0xAF] = "PUSHF",
            [0xB0] = "JMP", [0xB1] = "CJMP", [0xB2] = "NCJMP", [0xB3] = "JSR", [0xB5] = "CALL",
            [0xC1] = "PUSHN", [0xC2] = "PUSHT", [0xC3] = "PUSHVP", [0xC4] = "PUSHFIX",
            [0xD5] = "POPXJMP", [0xD6] = "POPXCJMP", [0xD7] = "POPXNCJMP", [0xD8] = "CALLPOPA",
            [0xF5] = "PUSHAINTER", [0xF6] = "SYSTEM",
        };

        // Call-target names (147), extracted from the corpus oracle (every CALL the decompiler names,
        // namespace-matched to the func-id high nibble). work/_scratch_ai/ai_callnames.py.
        static readonly Dictionary<ushort, string> CallTargets = new()
        {
            [0x0000] = "wait", [0x005F] = "halt", [0x00A9] = "GetRandomValue",
            [0x400A] = "motionBlurEffect",
            [0x6003] = "camGetPos", [0x6004] = "camSetPolar", [0x6010] = "camMove", [0x6014] = "camMoveAcc",
            [0x6016] = "camResetMove", [0x601A] = "camWait", [0x6020] = "refSetPos", [0x6021] = "refGetPos",
            [0x602E] = "refMove", [0x6032] = "refMoveAcc", [0x6034] = "refResetMove", [0x6038] = "refWait",
            [0x603A] = "camSetRoll", [0x603B] = "camSetScrDpt", [0x603F] = "refSetBtl", [0x6040] = "camSetBtlPolar",
            [0x6041] = "refSetBtlPolar", [0x6044] = "camSetBtlPolar2", [0x6045] = "refSetBtlPolar2",
            [0x604D] = "camSetChrPolar2", [0x6077] = "camGetRoll", [0x6078] = "camGetScrDpt", [0x607D] = "camBlur",
            [0x6080] = "camRand",
            [0x7000] = "btlTerminateAction", [0x7003] = "btlDirTarget", [0x7006] = "btlDirBasic",
            [0x7007] = "startMotion", [0x7008] = "awaitMotion", [0x7009] = "setGravity", [0x700A] = "setHeight", [0x700B] = "performCommand",
            [0x700C] = "btlMove", [0x700D] = "btlDirPos", [0x7010] = "findMatchingChr", [0x7012] = "btlTerminateEffect",
            [0x7014] = "chosenCommand", [0x7015] = "print", [0x7016] = "stopMotion", [0x7017] = "btlSetNormalEffect",
            [0x7019] = "usedCommand", [0x701B] = "overrideAttemptedCommand", [0x701C] = "btlSetMotionLevel",
            [0x701D] = "btlGetMotionLevel", [0x701E] = "countChrOverlap", [0x701F] = "btlChgWaitMotion",
            [0x7021] = "dereferenceCharacter", [0x7022] = "SetAmbushState", [0x7023] = "btlDistTarget",
            [0x7024] = "CurrentEncounter", [0x7025] = "findMatchingChrIncludingUntargetable", [0x7026] = "btlSetWeak",
            [0x7028] = "scaleOwnSize", [0x7029] = "setSelfFloating", [0x702A] = "btlCheckBtlPos", [0x702B] = "btlCheckMotion", [0x702C] = "btlSetHoming",
            [0x702D] = "btlResetMove", [0x702E] = "btlMoveTargetDist", [0x702F] = "btlOut", [0x7030] = "btlGetMoveFlag",
            [0x7031] = "btlStartMotion", [0x7032] = "setActorFacingAngle", [0x7033] = "btlSetEnMapID", [0x7034] = "endBattle", [0x7036] = "btlSetTrans",
            [0x7037] = "addCommand", [0x7038] = "removeCommand", [0x7039] = "btlTerminateDeath", [0x703A] = "btlSetSpeed",
            [0x703B] = "setCommandDisabled", [0x703C] = "runBtlSceneA", [0x703F] = "camReq", [0x7040] = "btlMagicStart",
            [0x7041] = "btlMagicEnd", [0x7046] = "btlSplineStart", [0x7047] = "btlSplineRegist", [0x7048] = "btlSplineRegistPos",
            [0x7049] = "btlSplineMove", [0x704A] = "btlCheckMove", [0x704B] = "btlReqMotion", [0x7050] = "reviveOrReinitialize", [0x7051] = "btlWaitNormalEffect",
            [0x7052] = "attachActor", [0x7053] = "btlMoveJump", [0x7054] = "btlSetChrPosElem", [0x7055] = "btlSetBodyHit",
            [0x7057] = "btlDirMove", [0x7058] = "btlCheckMotionNum", [0x7059] = "btlMoveTargetDist2D", [0x705A] = "forcePerformCommand",
            [0x705D] = "btlSetBindEffect", [0x7062] = "btlSetHitEffect", [0x7063] = "btlWaitHitEffect", [0x706B] = "btlSetModelHide",
            [0x706E] = "btlReqVoice", [0x706F] = "btlSetMotion2", [0x7071] = "btlStatusOff", [0x706C] = "readMovePropertyForActor", [0x7078] = "btlSoundEffectFade", [0x707A] = "btlGetCalcResult",
            [0x707B] = "btlSoundEffect", [0x707F] = "btlSetBtlPos", [0x7082] = "btlSetFreeEffect", [0x7083] = "btlSetAfterImage",
            [0x7084] = "btlResetAfterImage", [0x7085] = "btlMoveAttack", [0x7086] = "btlUseChrMpLimit", [0x7087] = "btlSoundEffectFade",
            [0x7088] = "btlRegSoundEffect", [0x708E] = "btlCheckBtlPos2", [0x708F] = "btlDirPosBasic", [0x7091] = "changeActorNameToCharName",
            [0x7093] = "btlCheckDirFlag", [0x7096] = "btlGetReflect", [0x7097] = "runBtlSceneB", [0x709D] = "btlWaitMotion_avoid",
            [0x709E] = "btlSetMotionSignal", [0x70A1] = "dereferenceEnemy", [0x70A5] = "btlSetMapCenter", [0x70A8] = "btlSetMotionData",
            // field-access dispatchers (the field index is a stack arg; see FieldName/FieldNames):
            [0x700F] = "readChrProperty", [0x7018] = "writeChrProperty", [0x701A] = "readMoveProperty",
            [0x70AA] = "getStatField", [0x70AB] = "setStatField", [0x70AC] = "getMotionField", [0x70B2] = "setMotionField",
            [0x70B0] = "btlDistTargetFrame2",
            [0x70B1] = "btlPrintSp", [0x70B5] = "btlResetMotionSpeed", [0x70BB] = "btlMoveLeave", [0x70BE] = "btlGetChrDir",
            [0x70C3] = "btlGetChrTargetDir2", [0x70C9] = "btlSoundEffect3", [0x70CC] = "initializeMatchingGroupTo",
            [0x70CD] = "addToMatchingGroup", [0x70CE] = "removeFromMatchingGroup", [0x70D1] = "btlGetNomEff", [0x70D5] = "setSummoner",
            [0x70D7] = "btlSetDamageMotion", [0x70DC] = "changeChrName", [0x70E0] = "isCounterattackAllowed", [0x70E2] = "btlSetTexAnime",
            [0x70ED] = "giveItem", [0x70EF] = "btlSetEffSignal", [0x7104] = "changeCommandAnimation", [0x7106] = "doesChrKnowCommand",
            [0x7107] = "btlDirPosBasic2", [0x7108] = "btlDirBasic2", [0x7109] = "btlSetAppear", [0x7115] = "btlDirResetLeave",
            [0x7117] = "overrideDeathAnimationWithCommand", [0x7118] = "btlSetSummonGameOver", [0x711C] = "btlSetGameOverEffNum",
            [0x711D] = "btlSetShadowHeight", [0x8000] = "setMapLayerVisibility",

            // ── IDs discovered via IDA cross-validation (SUPERMD_CROSSVALIDATION_REPORT.md) ──
            // These IDs were verified against the Battle funcspace table at 0xC42618 (16-byte entries).
            // Names are inferred from IDA function names. Some entries are duplicates (same function
            // registered in multiple slots — normal for ATEL's 3-slot dispatch: CALLPOPA@+0, float@+8, int@+12).
            // [0x7000] = "btlTerminateAction",           // Terminate battle action
            // [0x7001] = "setBattleFlagB",                // Set battle flag B (namespace 0, NOT GetRandomValue)
            // [0x7002] = "launchBattle",                  // Launch battle
            // [0x7003] = "constantOne",                   // Return constant 1
            // [0x7004] = "btlDirTarget",                  // Direction target
            // [0x7005] = "setGlobalFloat",                // Set global float
            // [0x7006] = "getGlobalByteC9CE",             // Get global byte
            // [0x700E] = "applyActionResultsToAllTargets", // Apply action results to all targets
            // [0x7011] = "setGlobalFlagA8FC",             // Set global flag A8FC
            // [0x7013] = "setActorMotionScalar",           // Set actor motion scalar
            // [0x7017] = "btlDispatchOpcode",              // Dispatch opcode (same as 0x7018?)
            // [0x7020] = "setAmbushState",                 // Set ambush state
            // [0x7022] = "currentEncounter",               // (duplicate of 0x7024?)
            // [0x7024] = "btlSetWeak",                    // Set weakness (NOTE: conflicts with existing "CurrentEncounter" label — needs RT2)
            // [0x7025] = "getActorStatusByte",             // Get actor status byte
            // [0x7027] = "setSelfFloating",                // Set self floating
            // [0x702F] = "returnFlagCheck3",               // Return flag check 3
            // [0x7030] = "func704C",                       // Unknown
            // [0x7031] = "check8C00",                      // Check 8C00
            // [0x7032] = "returnZero",                     // Return zero
            // [0x7033] = "setAnimId",                      // Set animation ID
            // [0x7034] = "writeByteToActors",              // Write byte to actors
            // [0x7035] = "reviveOrReinitialize",           // (duplicate of 0x7050?)
            // [0x7037] = "isActorMotionEqual",             // Check actor motion equal
            // [0x7039] = "listItemSelectClamped",          // List item select (clamped)
            // [0x7040] = "camReqExecPoll",                 // Camera request exec poll
            // [0x7043] = "func7043",                       // Unknown
            // [0x7044] = "listItemSelect",                 // List item select
            // [0x7045] = "listItemSelectClamped2",         // (duplicate?)
            // [0x7046] = "computeDistanceScaled",         // Compute distance scaled
            // [0x7048] = "clearFlag112A8E4",               // Clear flag 112A8E4
            // [0x704F] = "func704C_2",                     // (duplicate?)
            // [0x7055] = "setActorFacingAngle",            // Set actor facing angle
            // [0x7056] = "checkBtlPos",                    // Check battle position
            // [0x7058] = "btlSetHoming",                   // Set homing
            // ── End IDA-verified IDs ──
        };

        /// <summary>Friendly name for a field index in the correct function-specific field space.</summary>
        static string? FieldNameForFunc(ushort funcId, ushort fieldId) => funcId switch
        {
            0x700F or 0x7018 or 0x70AA or 0x70AB => AiChrPropertyNames.Get(fieldId),
            0x70AC or 0x70B2 => AiMotionPropertyNames.Get(fieldId),
            0x701A or 0x706C => AiMovePropertyNames.Get(fieldId),
            _ => null,
        };
    }
}

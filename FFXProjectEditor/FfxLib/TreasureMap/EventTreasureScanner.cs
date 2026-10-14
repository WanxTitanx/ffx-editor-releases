using FFXProjectEditor.Diagnostics;
using FFXProjectEditor.FfxLib.Atel;
using FFXProjectEditor.FfxLib.Event;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.TreasureMap;

public sealed record EventTreasureScanResult(
    string EventPath, string EventId,
    IReadOnlyList<EventTreasureCandidate> Candidates,
    int WorkerCount, int StatementCount);

// Field EV01 uses descriptors at header +0x38, not bytecode entry points. Function
// offsets are relative to ScriptCodeOffset; PUSHI/PUSHF address the owning worker's
// constant pools. See AtelScriptParser and FFX_EVENTVM_OPS_2026-09-17.md.
public static class EventTreasureScanner
{
    public const int StandardChestModelId = 0x5002;
    public const int BlueChestModelId = 0x50AA;

    public static EventTreasureScanResult Scan(string eventPath)
    {
        byte[] atel = EventPackage.Read(eventPath).AtelBytes;
        string eventId = Path.GetFileNameWithoutExtension(eventPath).ToLowerInvariant();
        RequireRange(atel, 0, AtelScriptParser.StaticHeaderLength, "header");
        int scriptStart = BitConverter.ToInt32(atel, 0x30);
        int codeLength = BitConverter.ToInt32(atel, 0);
        RequireRange(atel, scriptStart, codeLength, "bytecode");
        var instructions = EventAtelDisassembler.DisassembleAtelChunk(atel);
        var workers = ReadWorkers(atel, instructions, scriptStart, codeLength);
        var candidates = workers.Select(w => ScanWorker(w, eventPath, eventId)).OfType<EventTreasureCandidate>().ToArray();
        DebugLog.Info("TreasureMap.Scan", $"{eventId}: workers={workers.Count}, candidates={candidates.Length}, statements={instructions.Count}");
        return new EventTreasureScanResult(eventPath, eventId, candidates, workers.Count, instructions.Count);
    }

    public static List<WorkerScanState> SplitWorkersForTest(List<DisassemblyInstruction> instructions, byte[] atel)
    {
        RequireRange(atel, 0, AtelScriptParser.StaticHeaderLength, "header");
        return ReadWorkers(atel, instructions, BitConverter.ToInt32(atel, 0x30), BitConverter.ToInt32(atel, 0));
    }

    private static List<WorkerScanState> ReadWorkers(byte[] atel, IReadOnlyList<DisassemblyInstruction> instructions, int scriptStart, int codeLength)
    {
        RequireRange(atel, scriptStart, codeLength, "bytecode");
        int count = BitConverter.ToUInt16(atel, 0x34); // +0x36 is the actor count.
        if (count > 4096) throw new InvalidDataException("ATEL worker count exceeds the supported limit.");
        RequireRange(atel, 0x38, count * 4, "worker table");
        for (int i = 0; i < count; i++)
        {
            int offset = BitConverter.ToInt32(atel, 0x38 + i * 4);
            RequireRange(atel, offset, AtelScriptParser.WorkerHeaderLength, "worker header");
            foreach ((int sizeOffset, int tableOffset) in new[] { (4, 0x18), (6, 0x1C), (8, 0x20), (10, 0x24) })
            {
                int length = BitConverter.ToUInt16(atel, offset + sizeOffset) * 4;
                if (length > 0) RequireRange(atel, BitConverter.ToInt32(atel, offset + tableOffset), length, "worker data table");
            }
        }
        var pools = AtelScriptParser.ReadWorkers(atel, count);
        var functions = pools.Select(w => AtelScriptParser.ReadInt32Table(atel, w.FunctionTable, w.FunctionCount, "functions")).ToArray();
        var instructionOffsets = instructions.Select(i => i.Offset - scriptStart).ToHashSet();
        var boundaries = functions.SelectMany(f => f).Distinct().OrderBy(x => x).ToList();
        if (boundaries.Any(x => x < 0 || x >= codeLength || !instructionOffsets.Contains(x)))
            throw new InvalidDataException("ATEL function entry is outside the bytecode or inside an operand.");
        boundaries.Add(codeLength);
        var endByStart = boundaries.Zip(boundaries.Skip(1)).ToDictionary(p => p.First, p => p.Second);
        var bodies = new Dictionary<int, DisassemblyInstruction[]>();
        int cursor = 0;
        foreach (var (start, end) in endByStart)
        {
            while (cursor < instructions.Count && instructions[cursor].Offset < scriptStart + start) cursor++;
            int first = cursor;
            while (cursor < instructions.Count && instructions[cursor].Offset < scriptStart + end) cursor++;
            var body = new DisassemblyInstruction[cursor - first];
            for (int j = 0; j < body.Length; j++) body[j] = instructions[first + j];
            bodies.Add(start, body);
        }
        var result = new List<WorkerScanState>();
        for (int i = 0; i < count; i++)
        {
            var state = new WorkerScanState(i) { Pool = pools[i] };
            var seen = new HashSet<int>();
            var jumpOffsets = AtelScriptParser.ReadInt32Table(atel, pools[i].JumpTable, pools[i].JumpCount, "jumps");
            foreach (int offset in jumpOffsets)
            {
                if (offset < 0 || offset >= codeLength || !instructionOffsets.Contains(offset))
                    throw new InvalidDataException("ATEL jump target is outside the bytecode or inside an operand.");
                state.JumpTargets.Add(scriptStart + offset);
            }
            for (int fn = 0; fn < functions[i].Count; fn++)
            {
                int start = functions[i][fn];
                if (!seen.Add(start)) continue;
                var body = bodies[start];
                state.Functions.Add((fn, body));
                state.Instructions.AddRange(body);
            }
            if (state.Instructions.Count > 0)
            {
                state.Start = state.Instructions.Min(x => x.Offset);
                state.End = scriptStart + endByStart[functions[i].Max()];
            }
            result.Add(state);
        }
        return result;
    }

    private static EventTreasureCandidate? ScanWorker(WorkerScanState worker, string path, string eventId)
    {
        var treasureIds = new HashSet<int>();
        var modelIds = new HashSet<int>();
        var positions = new List<EventPosition>();
        bool silent = false;
        foreach (var (functionIndex, instructions) in worker.Functions)
        {
            // Unknown operations, native calls and control-flow joins invalidate prior
            // values instead of turning variable indices or stale arguments into IDs.
            var stack = new List<double?>();
            var registers = new double?[14];
            double? Pop() { if (stack.Count == 0) return null; var value = stack[^1]; stack.RemoveAt(stack.Count - 1); return value; }
            void Invalidate() { stack.Clear(); Array.Clear(registers); }
            foreach (var instruction in instructions)
            {
                if (worker.JumpTargets.Contains(instruction.Offset)) Invalidate();
                int operand = instruction.Operand;
                string op = instruction.Mnemonic;
                switch (op)
                {
                    case "PUSHII": stack.Add(unchecked((short)operand)); break;
                    case "PUSHI": stack.Add(operand < worker.Pool!.IntegerConstantValues.Count ? worker.Pool.IntegerConstantValues[operand] : null); break;
                    case "PUSHF": stack.Add(operand < worker.Pool!.FloatConstantBits.Count ? worker.Pool.GetFloatConstant((ushort)operand) : null); break;
                    case "REPUSH": stack.Add(stack.Count > 0 ? stack[^1] : null); break;
                    case "UMINUS": stack.Add(-Pop()); break;
                    case "NOP": case "TAG": case "SYSTEM": break;
                    case "CALL": case "CALLPOPA":
                        if (operand is 0x015B or 0x01A7)
                        {
                            if (Pop() is double id && double.IsFinite(id) && id >= 0 && id <= ushort.MaxValue && id == Math.Truncate(id))
                            { treasureIds.Add((int)id); silent |= operand == 0x01A7; }
                        }
                        else if (operand == 0x0013)
                        {
                            var z = Pop(); var y = Pop(); var x = Pop();
                            if (x.HasValue && y.HasValue && z.HasValue && float.IsFinite((float)x.Value) && float.IsFinite((float)y.Value) && float.IsFinite((float)z.Value))
                                positions.Add(new EventPosition((float)x.Value, (float)y.Value, (float)z.Value, instruction.Offset, functionIndex));
                        }
                        else if (operand == 0x505B && Pop() is double model && model >= 0 && model <= ushort.MaxValue && model == Math.Truncate(model))
                            modelIds.Add((int)model);
                        Invalidate();
                        break;
                    default:
                        if (op.Length == 5 && op.StartsWith("POPI") && op[4] is >= '0' and <= '3') registers[op[4] - '0'] = Pop();
                        else if (op.Length == 5 && op.StartsWith("POPF") && op[4] is >= '0' and <= '9') registers[4 + op[4] - '0'] = Pop();
                        else if (op.Length == 6 && op.StartsWith("PUSHI") && op[5] is >= '0' and <= '3') stack.Add(registers[op[5] - '0']);
                        else if (op.Length == 6 && op.StartsWith("PUSHF") && op[5] is >= '0' and <= '9') stack.Add(registers[4 + op[5] - '0']);
                        else Invalidate();
                        break;
                }
            }
        }
        if (treasureIds.Count == 0) return null;
        return new EventTreasureCandidate(path, eventId, FieldAssetDiscovery.ToFieldId(eventId), worker.Index,
            treasureIds.OrderBy(x => x).ToArray(), positions, modelIds.OrderBy(x => x).ToArray(), silent);
    }

    private static void RequireRange(byte[] bytes, int offset, int length, string description)
    {
        if (offset < 0 || length < 0 || (long)offset + length > bytes.Length)
            throw new InvalidDataException($"ATEL {description} is outside the event data.");
    }

    public sealed class WorkerScanState
    {
        public int Index { get; }
        public List<DisassemblyInstruction> Instructions { get; } = new();
        public int Start { get; set; }
        public int End { get; set; }
        internal AtelWorkerInfo? Pool { get; init; }
        internal List<(int Index, DisassemblyInstruction[] Instructions)> Functions { get; } = new();
        internal HashSet<int> JumpTargets { get; } = new();
        public WorkerScanState(int index) => Index = index;
    }
}

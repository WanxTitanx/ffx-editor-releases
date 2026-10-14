using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;

namespace FFXProjectEditor.FfxLib.BattleMap;

internal sealed record BattleAtelCameraPose(float[] Eye, float[] Target, float? Roll, float? ScreenDepth,
    int Worker, int Entrypoint, int EyeCallOffset, int TargetCallOffset, string PositionCommand);

/// <summary>
/// Reads the final constant pose in the first camera setup entrypoint, bounded by
/// the actual camera worker and its entrypoints. It does not execute the battle VM.
/// Camera worker type 0 comes from chunk1; calls 6002/6020 set eye/reference XYZ.
/// Provenance: docs/reverse/data/wave12/atel_map.csv and the camera polar RE ledger.
/// </summary>
internal static class BattleAtelCameraPoseReader
{
    internal static BattleAtelCameraPose? Read(string battleId, byte[] bytes)
    {
        if (bytes.Length < 16) return null;
        int chunks = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        if (chunks is < 3 or > 32 || 4L + chunks * 4L > bytes.Length) return null;
        var setup = BattleCameraSetup_File.ReadFromBattleBin(battleId, bytes);
        if (setup.Script is not { CodeWalkClosedExactly: true } script) return null;
        return ReadScript(script, CameraWorkers(bytes, script.Workers.Count));
    }

    internal static BattleAtelCameraPose? ReadScript(AiScriptFile script, IReadOnlyList<int> cameraWorkers)
    {
        int[] boundaries = script.Workers.SelectMany(w => w.Entrypoints)
            .Where(x => x >= 0 && x < script.CodeLength).Append(script.CodeLength).Distinct().Order().ToArray();
        foreach (int workerIndex in cameraWorkers)
        {
            if (workerIndex < 0 || workerIndex >= script.Workers.Count) continue;
            var worker = script.Workers[workerIndex];
            var visited = new HashSet<int>();
            for (int entryIndex = 0; entryIndex < worker.Entrypoints.Count; entryIndex++)
            {
                int start = worker.Entrypoints[entryIndex];
                if (start < 0 || start >= script.CodeLength || !visited.Add(start)) continue;
                int end = boundaries.First(x => x > start);
                var instructions = script.Instructions.Where(x => x.Offset >= script.ScriptStart + start && x.Offset < script.ScriptStart + end).ToArray();
                if (!instructions.Any(x => x.OperandKind == AiOperandKind.FuncId &&
                    x.Operand is 0x6002 or 0x6004 or 0x6020 or 0x603F or 0x6040 or 0x6041 or 0x6044 or 0x6045 or 0x604D)) continue;
                // An unresolved initial setup must not become a later attack or
                // ambush camera merely because that callback has constant values.
                return ReadEntry(script, instructions, workerIndex, entryIndex);
            }
        }
        return null;
    }

    private static BattleAtelCameraPose? ReadEntry(AiScriptFile script, AiInstruction[] instructions, int worker, int entry)
    {
        float[]? eye = null, target = null, polar = null;
        float? roll = null, depth = null;
        int eyeOffset = -1, targetOffset = -1;
        string command = "";
        bool returned = false, settled = false;
        foreach (var (instruction, index) in instructions.Select((x, i) => (x, i)))
        {
            // Branches/subroutines can choose a different setup at runtime. Do not
            // combine their lexical writes and pretend the result was executed.
            if (instruction.OperandKind == AiOperandKind.JumpIndex ||
                instruction.Opcode is 0xB0 or 0xB1 or 0xB2 or 0xB3 or 0xD5 or 0xD6 or 0xD7) return null;
            if (instruction.Opcode == 0x3C) { returned = true; break; }
            if (instruction.OperandKind != AiOperandKind.FuncId) continue;
            switch (instruction.Operand)
            {
                case 0x6020:
                    settled = false;
                    target = Arguments(script, instructions, index, 3); targetOffset = instruction.Offset;
                    break;
                case 0x6002:
                    settled = false;
                    eye = Arguments(script, instructions, index, 3);
                    polar = null; eyeOffset = instruction.Offset; command = "camSetPos";
                    break;
                case 0x6004:
                    settled = false;
                    polar = Arguments(script, instructions, index, 3);
                    eye = null; eyeOffset = instruction.Offset; command = "camSetPolar";
                    break;
                case 0x603A: roll = Arguments(script, instructions, index, 1)?[0]; break;
                case 0x603B: depth = Arguments(script, instructions, index, 1)?[0]; break;
                case 0x603F: case 0x6040: case 0x6041: case 0x6044: case 0x6045: case 0x604D:
                    // Actor-aware operations require runtime state. Later explicit
                    // writes can restore a resolvable pose within this same entry.
                    eye = target = polar = null;
                    settled = false;
                    break;
                case 0x6003: case 0x6021: case 0x6077: case 0x6078: // getters
                case 0x6016: case 0x6034: // reset movement
                    break;
                case 0x601A: case 0x6038: // wait for movement to finish
                    settled = true;
                    break;
                default:
                    if ((instruction.Operand >> 12) == 6) { eye = target = polar = null; settled = false; }
                    break;
            }
        }
        // Some opening entries wait on the completed movement, then fall through
        // into the next callback. Keep that settled pose without interpreting it.
        if ((!returned && !settled) || target == null) return null;
        if (polar != null && polar[2] > 0)
        {
            var p = BattleCameraSetup_File.PolarToEye(target[0], target[1], target[2], polar[0], polar[1], polar[2]);
            eye = new[] { p.X, p.Y, p.Z };
        }
        if (eye == null || eye.Concat(target).Any(x => !float.IsFinite(x)) ||
            eye.Zip(target).Sum(p => Math.Pow(p.First - p.Second, 2)) < 1e-8) return null;
        return new(eye, target, roll, depth, worker, entry, eyeOffset, targetOffset, command);
    }

    private static float[]? Arguments(AiScriptFile script, AiInstruction[] instructions, int callIndex, int count)
    {
        if (callIndex < count) return null;
        var values = new float[count];
        for (int i = 0; i < count; i++)
        {
            var push = instructions[callIndex - count + i];
            if (push.Opcode == 0xAE) values[i] = (short)push.Operand;
            else if (push.Opcode == 0xAF && script.FloatPoolOffset >= 0)
            {
                long offset = (long)script.FloatPoolOffset + push.Operand * 4L;
                if (offset < 0 || offset > script.OriginalAiFileBytes.Length - 4L) return null;
                values[i] = BinaryPrimitives.ReadSingleLittleEndian(script.OriginalAiFileBytes.AsSpan((int)offset, 4));
            }
            else return null;
            if (!float.IsFinite(values[i])) return null;
        }
        return values;
    }

    private static IReadOnlyList<int> CameraWorkers(byte[] bytes, int workerCount)
    {
        var result = new List<int>();
        if (bytes.Length < 16) return result;
        int start = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8));
        int end = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12));
        if (start < 16 || start >= bytes.Length || end <= (long)start + 1 || end > bytes.Length) return result;
        ReadOnlySpan<byte> mapping = bytes.AsSpan(start, end - start);
        int count = mapping[0], records = (mapping[1] + 3) & ~1;
        if (records > mapping.Length - count * 4) return result;
        for (int i = 0; i < count; i++)
        {
            int offset = records + i * 4, index = mapping[offset], type = mapping[offset + 1];
            if (type != 0) continue;
            int section = BinaryPrimitives.ReadUInt16LittleEndian(mapping.Slice(offset + 2, 2));
            if (section > mapping.Length - 2) return Array.Empty<int>();
            int tags = BinaryPrimitives.ReadUInt16LittleEndian(mapping.Slice(section, 2));
            if (section + 2L + tags * 2L > mapping.Length) return Array.Empty<int>();
            if (index < workerCount && !result.Contains(index)) result.Add(index);
        }
        return result;
    }
}

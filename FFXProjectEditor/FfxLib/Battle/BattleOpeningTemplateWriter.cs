using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;

namespace FFXProjectEditor.FfxLib.Battle;

/// <summary>Adds an independent opening worker without reusing a camera/actor worker.</summary>
public static class BattleOpeningTemplateWriter
{
    private static readonly int[] HeaderPointers = { 0x08, 0x0C, 0x20, 0x28, 0x2C, 0x30 };
    private static readonly int[] DescriptorPointers = { 0x14, 0x18, 0x1C, 0x20, 0x24, 0x30 };

    public static byte[] CreateMonstersFirst(byte[] original, string expectedRevision)
    {
        if (BattleOpeningWriter.Revision(original) != expectedRevision)
            throw new InvalidOperationException("The encounter changed after it was loaded.");
        var layout = BattleOpeningWriter.ReadLayout(original);
        var source = layout.Script;
        var ai = source.OriginalAiFileBytes;
        int count = source.Workers.Count;
        if (count >= 255 || U16(ai, 0x14) != count || U16(ai, 0x34) != count ||
            U16(ai, 0x36) != count || U16(ai, 0x16) != 0 || U16(ai, 0x1C) != 0)
            throw new InvalidDataException("Adding an opening requires the primary-worker encounter layout.");
        if (count == 0 && source.CodeLength != 0) throw new InvalidDataException("Unowned ATEL code cannot be extended.");
        var map = layout.Map;
        if (map.Length < 2) throw new InvalidDataException("Truncated worker map.");
        if (map[1] > 137 && map.Length > 139 && map[139] != 255 && BattleOpeningWriter.Read(original).Count > 0)
            throw new InvalidOperationException("This encounter already has editable opening values.");
        int insertion = checked(0x38 + count * 4);
        int codeEnd = checked(source.ScriptStart + source.CodeLength);
        if (insertion > source.ScriptStart || codeEnd > ai.Length)
            throw new InvalidDataException("Invalid ATEL layout for an additional worker.");
        var boundaries = source.Instructions.Select(i => i.Offset - source.ScriptStart).ToHashSet();
        foreach (var worker in source.Workers)
        {
            if (worker.DescriptorOffset < insertion || worker.DescriptorOffset + 0x34 > source.ScriptStart)
                throw new InvalidDataException("Encounter worker descriptors are not in the supported header region.");
            if (U16(ai, worker.DescriptorOffset + 8) != worker.Entrypoints.Count ||
                U16(ai, worker.DescriptorOffset + 10) != worker.JumpTargets.Count ||
                worker.Entrypoints.Concat(worker.JumpTargets).Any(offset => !boundaries.Contains(offset)))
                throw new InvalidDataException("Existing ATEL event or jump tables are incomplete.");
        }

        if (map[1] > 137 && map.Length > 139 && map[139] != 255)
        {
            if (!BattleOpeningWriter.TryResolveHook(source, map, out int worker, out int entry))
                throw new InvalidOperationException("The existing lifecycle mapping has no supported opening event.");
            return PrependExisting(original, layout.AiOffset, source, map, worker, entry);
        }

        var code = new List<byte> { 0x3C, 0xD8, 0x5F, 0x00, 0x3C }; // init / dormant main
        void Push(ushort value) { code.Add(0xAE); code.Add((byte)value); code.Add((byte)(value >> 8)); }
        void Set(ushort target, ushort field, ushort value)
        { Push(target); Push(field); Push(value); code.AddRange(new byte[] { 0xD8, 0x18, 0x70 }); }
        Set(0xFFF1, BattleOpeningWriter.FirstStrike, 1);
        Set(0xFFF1, BattleOpeningWriter.CurrentTurnDelay, 0);
        Set(0xFFF2, BattleOpeningWriter.FirstStrike, 0);
        Set(0xFFF2, BattleOpeningWriter.CurrentTurnDelay, 2);
        code.Add(0x3C);
        while ((code.Count & 3) != 0) code.Add(0x3C); // Unreachable RET padding preserves data alignment.

        int deltaCode = code.Count;
        int tail = checked(ai.Length + 4 + deltaCode);
        int descriptor = Align4(tail), entries = descriptor + 0x34;
        int aiDelta = Align128(checked(entries + 12 - ai.Length));
        byte[] updatedAi = new byte[checked(ai.Length + aiDelta)];
        Array.Copy(ai, 0, updatedAi, 0, insertion);
        Array.Copy(ai, insertion, updatedAi, insertion + 4, codeEnd - insertion);
        code.CopyTo(updatedAi, codeEnd + 4);
        Array.Copy(ai, codeEnd, updatedAi, codeEnd + 4 + deltaCode, ai.Length - codeEnd);
        int Relocate(int value)
        {
            if (value is 0 or -1) return value;
            if (value < insertion || value > ai.Length) throw new InvalidDataException("Unsupported ATEL pointer.");
            return checked(value + 4 + (value >= codeEnd ? deltaCode : 0));
        }
        foreach (int offset in HeaderPointers) I32(updatedAi, offset, Relocate(I32(ai, offset)));
        // For a zero-code file, ScriptStart is also the old data boundary. It must
        // point to the new code, unlike data pointers which move past that code.
        I32(updatedAi, 0x30, source.ScriptStart + 4);
        I32(updatedAi, 0, checked(source.CodeLength + deltaCode));
        I32(updatedAi, 0x10, updatedAi.Length);
        U16(updatedAi, 0x14, count + 1); U16(updatedAi, 0x34, count + 1); U16(updatedAi, 0x36, count + 1);
        foreach (var worker in source.Workers)
        {
            int before = worker.DescriptorOffset, after = before + 4;
            I32(updatedAi, 0x38 + worker.Index * 4, after);
            foreach (int offset in DescriptorPointers) I32(updatedAi, after + offset, Relocate(I32(ai, before + offset)));
        }
        I32(updatedAi, insertion, descriptor);
        if (count > 0) Array.Copy(updatedAi, source.Workers[0].DescriptorOffset + 4, updatedAi, descriptor, 0x34);
        else
        {
            // Immediate literals and native calls need no variables or constant pools.
            I32(updatedAi, descriptor + 0x14, entries);
            I32(updatedAi, descriptor + 0x18, entries);
            I32(updatedAi, descriptor + 0x1C, entries);
        }
        U16(updatedAi, descriptor + 8, 3); U16(updatedAi, descriptor + 10, 0);
        U16(updatedAi, descriptor + 0x10, 0); // This worker never accesses private variables.
        I32(updatedAi, descriptor + 0x20, entries); I32(updatedAi, descriptor + 0x24, entries + 12);
        I32(updatedAi, descriptor + 0x2C, 0); // Runtime private-data pointer is initialized by the VM.
        I32(updatedAi, entries, source.CodeLength);
        I32(updatedAi, entries + 4, source.CodeLength + 1);
        I32(updatedAi, entries + 8, source.CodeLength + 5);

        byte[] updatedMap = AddMapping(map, count);
        int mapOffset = I32(original, 8), formationOffset = I32(original, 12);
        int mapDelta = updatedMap.Length - map.Length, totalDelta = checked(aiDelta + mapDelta);
        byte[] output = new byte[checked(original.Length + totalDelta)];
        Array.Copy(original, 0, output, 0, layout.AiOffset);
        updatedAi.CopyTo(output, layout.AiOffset);
        updatedMap.CopyTo(output, mapOffset + aiDelta);
        Array.Copy(original, formationOffset, output, formationOffset + totalDelta, original.Length - formationOffset);
        int chunks = I32(original, 0) - 1;
        for (int i = 0; i <= chunks; i++)
        {
            int at = 4 + i * 4, value = I32(original, at);
            if (value is 0 or -1) continue;
            // Some stock encounters declare absent tail chunks past EOF. Keep their distance
            // past EOF unchanged; only the script/map/formation spans are dereferenced.
            if (value < layout.AiOffset) throw new InvalidDataException("Unsupported battle chunk bounds.");
            I32(output, at, checked(value + (value >= mapOffset ? aiDelta : 0) + (value >= formationOffset ? mapDelta : 0)));
        }

        var reread = BattleOpeningWriter.ReadLayout(output);
        if (reread.Script.Workers.Count != count + 1 || !reread.Script.CodeWalkClosedExactly ||
            !reread.Script.OriginalAiFileBytes.AsSpan(reread.Script.ScriptStart, source.CodeLength)
                .SequenceEqual(ai.AsSpan(source.ScriptStart, source.CodeLength)))
            throw new InvalidDataException("Opening creation did not preserve the original ATEL code.");
        for (int i = 0; i < count; i++)
            if (!source.Workers[i].Entrypoints.SequenceEqual(reread.Script.Workers[i].Entrypoints) ||
                !source.Workers[i].JumpTargets.SequenceEqual(reread.Script.Workers[i].JumpTargets))
                throw new InvalidDataException("Opening creation changed an existing event or jump target.");
        var values = BattleOpeningWriter.Read(output);
        if (values.Count != 4 || values[0].Target != 0xFFF1 || values[0].Value != 1 ||
            values[1].Value != 0 || values[2].Target != 0xFFF2 || values[2].Value != 0 || values[3].Value != 2)
            throw new InvalidDataException("Opening creation failed semantic readback.");
        if (!original.AsSpan(formationOffset).SequenceEqual(output.AsSpan(formationOffset + totalDelta)))
            throw new InvalidDataException("Opening creation changed formation, positions or other encounter chunks.");
        return output;
    }

    private static byte[] AddMapping(byte[] original, int worker)
    {
        // Byte zero is the worker base for the secondary script, not the record count.
        // Some encounters have an unmapped worker (bsil03_03, mihn00_06/07).
        int pre = original[1], records = pre + ((pre & 1) == 0 ? 2 : 3);
        if (records > original.Length || original[0] != worker) throw new InvalidDataException("Unsupported worker-map base.");
        int count = original.AsSpan(2, pre).ToArray().Where(value => value != 255).Select(value => (int)value).DefaultIfEmpty(-1).Max() + 1;
        int data = checked(records + count * 4);
        if (count >= 255 || data > original.Length) throw new InvalidDataException("Worker mapping cannot grow.");
        int newPre = Math.Max(pre, 138), newRecords = newPre + ((newPre & 1) == 0 ? 2 : 3);
        int delta = newRecords - records + 4;
        int payload = checked(original.Length + delta);
        byte[] result = new byte[checked(original.Length + Align128(delta + 14))];
        result[0] = (byte)(worker + 1); result[1] = (byte)newPre;
        Array.Fill(result, (byte)255, 2, newPre);
        Array.Copy(original, 2, result, 2, pre); result[139] = (byte)count;
        if (newPre == pre && records > pre + 2)
            Array.Copy(original, pre + 2, result, pre + 2, records - pre - 2);
        Array.Copy(original, records, result, newRecords, count * 4);
        Array.Copy(original, data, result, data + delta, original.Length - data);
        for (int i = 0; i < count; i++)
        {
            int offset = U16(original, records + i * 4 + 2);
            if (offset < data || offset + 2 > original.Length || offset + delta > ushort.MaxValue)
                throw new InvalidDataException("Invalid existing worker-map payload.");
            int length = U16(original, offset);
            if (offset + 2L + length * 2L > original.Length) throw new InvalidDataException("Truncated worker-map payload.");
            U16(result, newRecords + i * 4 + 2, offset + delta);
        }
        if (payload > ushort.MaxValue) throw new InvalidDataException("Opening event map is too large.");
        int record = newRecords + count * 4;
        result[record] = (byte)worker; result[record + 1] = 6; U16(result, record + 2, payload);
        U16(result, payload, 6);
        for (int i = 0; i < 6; i++) U16(result, payload + 2 + i * 2, i == 5 ? 2 : ushort.MaxValue);
        return result;
    }

    // Give event 5 its own new entry and rejoin the original handler with a worker-local jump.
    // Other lifecycle events may share the old entry; their mappings remain untouched.
    private static byte[] PrependExisting(byte[] original, int aiOffset, AiScriptFile source, byte[] map, int workerIndex, int entry)
    {
        var worker = source.Workers[workerIndex];
        if (worker.Entrypoints.Count >= ushort.MaxValue || worker.JumpTargets.Count >= ushort.MaxValue)
            throw new InvalidDataException("The existing worker cannot accept another entry or jump.");
        byte[] ai = source.OriginalAiFileBytes;
        int end = source.ScriptStart + source.CodeLength;
        var code = new List<byte>();
        void Push(ushort value) { code.Add(0xAE); code.Add((byte)value); code.Add((byte)(value >> 8)); }
        void Set(ushort actor, ushort field, ushort value)
        { Push(actor); Push(field); Push(value); code.AddRange(new byte[] { 0xD8, 0x18, 0x70 }); }
        Set(0xFFF1, BattleOpeningWriter.FirstStrike, 1); Set(0xFFF1, BattleOpeningWriter.CurrentTurnDelay, 0);
        Set(0xFFF2, BattleOpeningWriter.FirstStrike, 0); Set(0xFFF2, BattleOpeningWriter.CurrentTurnDelay, 2);
        code.Add(0xB0); code.Add((byte)worker.JumpTargets.Count); code.Add((byte)(worker.JumpTargets.Count >> 8));
        while ((code.Count & 3) != 0) code.Add(0x3C);
        int entries = Align4(checked(ai.Length + code.Count));
        int jumps = checked(entries + (worker.Entrypoints.Count + 1) * 4);
        int delta = Align128(checked(jumps + (worker.JumpTargets.Count + 1) * 4 - ai.Length));
        byte[] edited = new byte[checked(ai.Length + delta)];
        Array.Copy(ai, edited, end); code.CopyTo(edited, end);
        Array.Copy(ai, end, edited, end + code.Count, ai.Length - end);
        int Relocate(int value)
        {
            if (value is 0 or -1) return value;
            if (value < 0x38 || value > ai.Length) throw new InvalidDataException("Unsupported ATEL pointer.");
            return checked(value + (value >= end ? code.Count : 0));
        }
        foreach (int at in HeaderPointers) I32(edited, at, Relocate(I32(ai, at)));
        foreach (var w in source.Workers)
            foreach (int at in DescriptorPointers) I32(edited, w.DescriptorOffset + at, Relocate(I32(ai, w.DescriptorOffset + at)));
        I32(edited, 0, source.CodeLength + code.Count); I32(edited, 0x10, edited.Length);
        U16(edited, worker.DescriptorOffset + 8, worker.Entrypoints.Count + 1);
        U16(edited, worker.DescriptorOffset + 10, worker.JumpTargets.Count + 1);
        I32(edited, worker.DescriptorOffset + 0x20, entries); I32(edited, worker.DescriptorOffset + 0x24, jumps);
        for (int i = 0; i < worker.Entrypoints.Count; i++) I32(edited, entries + i * 4, worker.Entrypoints[i]);
        I32(edited, entries + worker.Entrypoints.Count * 4, source.CodeLength);
        for (int i = 0; i < worker.JumpTargets.Count; i++) I32(edited, jumps + i * 4, worker.JumpTargets[i]);
        I32(edited, jumps + worker.JumpTargets.Count * 4, worker.Entrypoints[entry]);
        byte[] editedMap = map.ToArray();
        int records = map[1] + ((map[1] & 1) == 0 ? 2 : 3);
        int table = U16(map, records + map[139] * 4 + 2);
        U16(editedMap, table + 12, worker.Entrypoints.Count);
        int mapOffset = I32(original, 8), formation = I32(original, 12);
        byte[] output = new byte[checked(original.Length + delta)];
        Array.Copy(original, output, aiOffset); edited.CopyTo(output, aiOffset); editedMap.CopyTo(output, mapOffset + delta);
        Array.Copy(original, formation, output, formation + delta, original.Length - formation);
        int chunks = I32(original, 0) - 1;
        for (int i = 0; i <= chunks; i++)
        {
            int at = 4 + i * 4, value = I32(original, at);
            if (value is 0 or -1) continue;
            if (value < aiOffset) throw new InvalidDataException("Unsupported battle chunk bounds.");
            I32(output, at, checked(value + (value >= mapOffset ? delta : 0)));
        }
        var after = BattleOpeningWriter.ReadLayout(output).Script;
        if (BattleOpeningWriter.Read(output).Count != 4 || !after.OriginalAiFileBytes.AsSpan(after.ScriptStart, source.CodeLength)
                .SequenceEqual(ai.AsSpan(source.ScriptStart, source.CodeLength)) ||
            !original.AsSpan(formation).SequenceEqual(output.AsSpan(formation + delta)))
            throw new InvalidDataException("Opening prefix failed preservation or semantic readback.");
        foreach (var w in source.Workers)
            if (!w.Entrypoints.SequenceEqual(after.Workers[w.Index].Entrypoints.Take(w.Entrypoints.Count)) ||
                !w.JumpTargets.SequenceEqual(after.Workers[w.Index].JumpTargets.Take(w.JumpTargets.Count)))
                throw new InvalidDataException("Opening prefix changed an existing control-flow target.");
        return output;
    }

    private static int Align4(int value) => checked((value + 3) & ~3);
    private static int Align128(int value) => checked((value + 127) & ~127);
    private static int I32(byte[] bytes, int at) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at, 4));
    private static int U16(byte[] bytes, int at) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at, 2));
    private static void I32(byte[] bytes, int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at, 4), value);
    private static void U16(byte[] bytes, int at, int value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at, 2), checked((ushort)value));
}

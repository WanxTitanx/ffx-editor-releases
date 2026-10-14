using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.MagicDll;

/// <summary>Owns the proven timing locations and relationships of one immutable PPP input.</summary>
internal sealed class MagicTimingSnapshot
{
    internal const uint FrameUnit = 4096;
    internal const uint Infinite = 0x70000000;
    private readonly byte[] _source;
    private readonly int _dataPtr;
    private readonly Dictionary<(MagicTimingKind Kind, int Offset), MagicTimingField> _fields = new();
    private readonly List<ProgramLayout> _programs = new();
    private sealed record Sequence(int Slot, int Start, int Stride, int Count, bool Complete);
    private sealed record ProgramLayout(int Section, int Head, Sequence[] Sequences);
    internal IReadOnlyList<MagicTimingField> Fields { get; }

    internal MagicTimingSnapshot(MagicDllFile file)
    {
        _dataPtr = file.DataSectionRawPtr;
        byte[] original = file.FileBytes;
        if (_dataPtr < 0 || _dataPtr > original.Length - file.Data.Length)
            throw new InvalidDataException("PPP timing snapshot is outside the file.");
        _source = original.AsSpan(_dataPtr, file.Data.Length).ToArray();
        Guid id = Guid.NewGuid();
        void Add(MagicTimingKind kind, int at, int owner)
        {
            if (at < 0 || at > _source.Length - 4)
                throw new InvalidDataException("PPP timing operand is outside .data.");
            _fields.TryAdd((kind, at), new MagicTimingField(kind, at, owner, Read(_source, at), id));
        }
        foreach (var program in file.Roots.SelectMany(r => r.Programs)
            .Where(p => p.SlotCount > 0).DistinctBy(p => p.ProgramAbs))
        {
            Add(MagicTimingKind.SectionDuration, program.SectionAbs, program.SectionAbs);
            Add(MagicTimingKind.ProgramStart, program.ProgramAbs + 16, program.ProgramAbs);
            Add(MagicTimingKind.ProgramDuration, program.ProgramAbs + 20, program.ProgramAbs);
            Add(MagicTimingKind.LoopStart, program.ProgramAbs + 24, program.ProgramAbs);
            Add(MagicTimingKind.LoopEnd, program.ProgramAbs + 28, program.ProgramAbs);
            var sequences = new List<Sequence>();
            foreach (var slot in program.Slots)
            {
                bool complete = slot.Keyframes.Count == slot.KeyframeCount && slot.KeyframeCount > 0;
                sequences.Add(new(slot.SlotAbs, slot.RecordOffset, slot.NativeRecordWidth,
                    slot.KeyframeCount, complete));
                foreach (var frame in slot.Keyframes)
                    Add(MagicTimingKind.KeyframeTime, frame.RecordOffset, slot.RecordOffset);
            }
            _programs.Add(new(program.SectionAbs, program.ProgramAbs, sequences.ToArray()));
        }
        Fields = Array.AsReadOnly(_fields.Values.OrderBy(f => f.DataOffset).ThenBy(f => f.Kind).ToArray());
    }

    internal bool TryApply(byte[] working, IReadOnlyList<(MagicTimingField Field, uint Tick)> edits,
        out byte[]? result, out string error)
    {
        result = null;
        error = string.Empty;
        if (_dataPtr < 0 || _dataPtr > working.Length - _source.Length)
            return Fail("PPP timing layout changed.", out error);
        var values = new Dictionary<int, uint>();
        foreach (var edit in edits)
        {
            var field = edit.Field;
            if (!_fields.TryGetValue((field.Kind, field.DataOffset), out var captured) ||
                captured != field || !field.IsEditable)
                return Fail("Timing field does not belong to this writable document snapshot.", out error);
            if (edit.Tick >= Infinite || edit.Tick % FrameUnit != 0)
                return Fail("Timing edits require finite whole-frame ticks; special values are preserved.", out error);
            if (values.TryGetValue(field.DataOffset, out uint previous) && previous != edit.Tick)
                return Fail("Shared timing operands have conflicting values.", out error);
            values[field.DataOffset] = edit.Tick;
        }
        byte[] current = working.AsSpan(_dataPtr, _source.Length).ToArray();
        byte[] candidate = (byte[])current.Clone();
        foreach (var edit in values) BitConverter.GetBytes(edit.Value).CopyTo(candidate, edit.Key);

        bool Same(int at, int size) => at >= 0 && size >= 0 && at <= current.Length - size &&
            current.AsSpan(at, size).SequenceEqual(_source.AsSpan(at, size));
        foreach (var program in _programs)
        {
            int head = program.Head, section = program.Section;
            bool programChanged = Enumerable.Range(16, 16).Any(i => values.ContainsKey(head + i));
            bool sectionChanged = values.ContainsKey(section);
            bool sequenceChanged = program.Sequences.Any(s =>
                values.Keys.Any(at => at >= s.Start && (long)at < (long)s.Start + (long)s.Stride * s.Count));
            if (!programChanged && !sectionChanged && !sequenceChanged) continue;
            if (!Same(section + 4, 12) || !Same(head, 16) || !Same(head + 32, 8))
                return Fail("PPP timing structural metadata changed.", out error);
            foreach (var sequence in program.Sequences)
            {
                if (!Same(sequence.Slot, 16) || !sequence.Complete ||
                    !Same(checked(sequence.Start + sequence.Stride * sequence.Count), 4))
                    return Fail("PPP timing sequence is incomplete or its metadata changed.", out error);
                bool changed = Enumerable.Range(0, sequence.Count)
                    .Any(index => values.ContainsKey(sequence.Start + index * sequence.Stride));
                if (changed)
                {
                    long previous = -1;
                    for (int index = 0; index < sequence.Count; index++)
                    {
                        uint tick = Read(candidate, sequence.Start + index * sequence.Stride);
                        if (tick >= Infinite) continue;
                        if (tick % FrameUnit != 0 || tick <= previous)
                            return Fail("Keyframes must remain ordered on distinct native whole-frame ticks.", out error);
                        previous = tick;
                    }
                }
                uint oldLife = Read(current, head + 20), newLife = Read(candidate, head + 20);
                if (newLife < Infinite && (programChanged || changed))
                    for (int index = 0; index < sequence.Count; index++)
                    {
                        int at = sequence.Start + index * sequence.Stride;
                        if (Read(current, at) < oldLife && Read(candidate, at) >= newLife)
                            return Fail("Program duration would remove an active keyframe; retime its events first.", out error);
                    }
            }
            uint loopStart = Read(candidate, head + 24), loopEnd = Read(candidate, head + 28);
            if ((values.ContainsKey(head + 24) || values.ContainsKey(head + 28)) &&
                (loopStart >= Infinite || loopEnd >= Infinite || loopStart >= loopEnd))
                return Fail("Loop start must precede a finite loop end.", out error);
            ulong oldEnd = (ulong)Read(current, head + 16) + Read(current, head + 20);
            ulong newEnd = (ulong)Read(candidate, head + 16) + Read(candidate, head + 20);
            uint oldSection = Read(current, section), newSection = Read(candidate, section);
            if (current[section + 4] == 0 && oldSection < Infinite && newSection < Infinite &&
                oldEnd <= oldSection && newEnd > newSection)
                return Fail("Section duration would truncate a contained program.", out error);
        }
        result = (byte[])working.Clone();
        foreach (var edit in values) BitConverter.GetBytes(edit.Value).CopyTo(result, _dataPtr + edit.Key);
        return true;
    }

    private static uint Read(byte[] bytes, int at) => BitConverter.ToUInt32(bytes, at);
    private static bool Fail(string message, out string error) { error = message; return false; }
}

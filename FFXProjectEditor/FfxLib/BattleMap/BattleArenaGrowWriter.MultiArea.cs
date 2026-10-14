using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.BattleMap;

public static partial class BattleArenaGrowWriter
{
    private static readonly int[] MultiAreaPointerFields = { 0x10, 0x14, 0x18, 0x1C, 0x20, 0x24, 0x28, 0x2C };

    /// <summary>
    /// Grows the two native 96-byte area records used by Dome formations. The
    /// single-area Plan/GrowMonsters contract still refuses multi-area input.
    /// Every area's pointers participate in capacity and rebase validation;
    /// staging reserves are extended when the following area's data leaves no room.
    /// </summary>
    public static byte[] GrowMonstersInTwoAreas(byte[] original,
        IReadOnlyList<IReadOnlyList<(float X, float Y, float Z)>> positions)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(positions);
        (int start, int length) = BattleArenaPositionWriter.LocateChunk3(original);
        const int areas = 2, headerLength = areas * BattleArenaAnchors_File.AreaRecordStride;
        if (start < 0 || length < headerLength || original[start] != 0 || original[start + 1] != areas
            || positions.Count != areas)
            throw new InvalidDataException("Expected two native stride-96 area records and two position lists.");

        int chunks = ReadInt32(original, 0) - 1;
        if (chunks is < 4 or > 7 || 4L + chunks * 4L + 4L > original.Length
            || ReadInt32(original, 4 + chunks * 4) != original.Length)
            throw new InvalidDataException("Invalid battle chunk directory or terminal offset.");

        var pointers = new Dictionary<(int Area, int Field), int>();
        var boundaries = new SortedSet<int> { length };
        for (int area = 0; area < areas; area++)
        {
            int rec = start + area * BattleArenaAnchors_File.AreaRecordStride;
            int oldCount = original[rec + 6];
            var points = positions[area];
            if (oldCount < 1 || points == null || points.Count < oldCount || points.Count > HardActorCap
                || points.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)))
                throw new ArgumentException("Two-area edits must preserve existing capacity, remain finite and use at most eight actors.", nameof(positions));
            if (original[rec] != 0 || original[rec + 1] != areas)
                throw new InvalidDataException("Unsupported area header.");
            foreach (int field in MultiAreaPointerFields)
            {
                int pointer = ReadInt32(original, rec + field);
                if (pointer < headerLength || pointer % ElementStride != 0 || (long)pointer + ElementStride > length)
                    throw new InvalidDataException("Area pointer is outside the aligned payload.");
                pointers.Add((area, field), pointer);
                boundaries.Add(pointer);
            }
        }

        int Capacity(int pointer)
        {
            int next = boundaries.First(b => b > pointer);
            if ((next - pointer) % ElementStride != 0)
                throw new InvalidDataException("Unaligned area-array boundary.");
            return (next - pointer) / ElementStride;
        }
        var insertions = new SortedDictionary<int, byte[]>();
        for (int area = 0; area < areas; area++)
        {
            int rec = start + area * BattleArenaAnchors_File.AreaRecordStride;
            int oldCount = original[rec + 6];
            foreach (int field in MultiAreaPointerFields)
            {
                int pointer = pointers[(area, field)], capacity = Capacity(pointer);
                int count = field switch
                {
                    0x10 or 0x14 => original[rec + 4],
                    0x18 => original[rec + 5],
                    0x1C or 0x20 or 0x24 => oldCount,
                    _ => 1
                };
                if (count < 1 || capacity < count)
                    throw new InvalidDataException("Declared area array crosses another area's pointer.");
                if (field is not (0x1C or 0x20 or 0x24)) continue;
                if (pointers.Count(p => p.Value == pointer) != 1)
                    throw new InvalidDataException("Shared monster-array pointers require a separate proven authoring contract.");
                int missing = positions[area].Count - capacity;
                if (missing <= 0) continue;
                int at = checked(start + pointer + capacity * ElementStride);
                byte[] added = new byte[checked(missing * ElementStride)];
                if (field != 0x20)
                    for (int i = 0; i < missing; i++)
                        original.AsSpan(start + pointer + (oldCount - 1) * ElementStride, ElementStride)
                            .CopyTo(added.AsSpan(i * ElementStride));
                if (!insertions.TryAdd(at, added))
                    throw new InvalidDataException("Conflicting array insertion boundaries.");
            }
        }

        int totalAdded = insertions.Sum(i => i.Value.Length);
        byte[] output = new byte[checked(original.Length + totalAdded)];
        int from = 0, to = 0;
        foreach (var insertion in insertions)
        {
            int size = insertion.Key - from;
            original.AsSpan(from, size).CopyTo(output.AsSpan(to));
            to += size;
            insertion.Value.CopyTo(output, to);
            to += insertion.Value.Length;
            from = insertion.Key;
        }
        original.AsSpan(from).CopyTo(output.AsSpan(to));

        int Rebase(int absolute) => checked(absolute + insertions.Where(i => i.Key <= absolute).Sum(i => i.Value.Length));
        foreach (var pointer in pointers)
        {
            int rec = start + pointer.Key.Area * BattleArenaAnchors_File.AreaRecordStride;
            WriteInt32(output, rec + pointer.Key.Field, Rebase(start + pointer.Value) - start);
        }
        for (int chunk = 4; chunk <= chunks; chunk++)
        {
            int offset = ReadInt32(original, 4 + chunk * 4);
            if (offset is 0 or -1) continue;
            if (offset < start + length || offset > original.Length)
                throw new InvalidDataException("Later chunk offset is not after the area payload.");
            WriteInt32(output, 4 + chunk * 4, Rebase(offset));
        }
        for (int area = 0; area < areas; area++)
        {
            int rec = start + area * BattleArenaAnchors_File.AreaRecordStride;
            output[rec + 6] = checked((byte)positions[area].Count);
            int live = start + ReadInt32(output, rec + 0x20);
            for (int i = 0; i < positions[area].Count; i++)
            {
                var point = positions[area][i]; int at = live + i * ElementStride;
                WriteFloat(output, at, point.X); WriteFloat(output, at + 4, point.Y); WriteFloat(output, at + 8, point.Z);
                // Existing W is part of the original. New entries use the native zero.
                if (i >= original[rec + 6]) WriteFloat(output, at + 12, 0f);
            }
        }
        return output;
    }
}

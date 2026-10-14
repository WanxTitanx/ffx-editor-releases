using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.MagicDll;

/// <summary>Confines geometry authoring to existing signed XYZ arrays. Evidence and native
/// consumers: PPP_GEOMETRY_EDITOR_20261003.md. No pointer, count, topology or R6 exception.</summary>
internal sealed class MagicGeometrySnapshot
{
    // Bound the expanded object graph, including partially overlapping aliases. The
    // largest observed DLL has 14,538 points; hostile counts must not exhaust the UI.
    internal const int MaxExpandedPoints = 100_000;
    private readonly byte[] _source;
    private readonly int _dataPtr;
    private readonly int _fileLength;
    private readonly Dictionary<int, MagicGeometryField> _fields = new();
    private readonly List<(int Start, int Size)> _anchors = new();
    internal IReadOnlyList<MagicGeometryResource> Resources { get; }
    internal IReadOnlyList<MagicGeometryField> Fields { get; }
    internal IReadOnlyList<(int Start, int Size)> NonRecordRanges { get; }
    internal IReadOnlyList<(int Start, int Size)> ImmutableRanges => _anchors;

    internal MagicGeometrySnapshot(MagicDllFile file)
    {
        _dataPtr = file.DataSectionRawPtr;
        _fileLength = file.FileBytes.Length;
        if (_dataPtr < 0 || _dataPtr > _fileLength - file.Data.Length)
            throw new InvalidDataException("PPP geometry is outside the captured file.");
        _source = file.FileBytes.AsSpan(_dataPtr, file.Data.Length).ToArray();
        var resources = new List<MagicGeometryResource>();
        var protectedRanges = new List<(int Start, int Size)>();
        var nonRecordRanges = new List<(int Start, int Size)>();
        var opaqueStarts = new HashSet<int>();
        var normalArrays = new List<(MagicGeometryResource Owner, long Start, uint Count)>();
        void Protect(long at, long size, bool anchor = true, bool record = false)
        {
            if (at < 0 || at >= _source.Length || size <= 0) return;
            var range = ((int)at, (int)Math.Min(size, _source.Length - at));
            protectedRanges.Add(range);
            if (!record) nonRecordRanges.Add(range);
            if (anchor) _anchors.Add(range);
        }
        void Opaque(long at)
        {
            if (at >= 0 && at < _source.Length) opaqueStarts.Add((int)at);
        }
        foreach (var root in file.Roots)
        {
            int r = root.RootAbs;
            Protect(r, 32);
            Protect((long)r + root.Table1Rel, 4L * root.PrimaryCount);
            Protect((long)r + root.Table2Rel, 32L * root.DescriptorCount);
            Protect((long)r + root.Table3Rel, 4L * root.Aux3Count);
            Protect((long)r + root.Table4Rel, 8L * root.Aux4Count);
            foreach (var p in root.Programs)
            {
                Protect(p.SectionAbs, 16, false);
                _anchors.Add((p.SectionAbs + 4, 12));
                Protect(p.ProgramAbs, 40L + 16L * p.SlotCount, false);
                _anchors.Add((p.ProgramAbs, 16));
                _anchors.Add((p.ProgramAbs + 32, 8 + 16 * p.SlotCount));
                foreach (int relative in new[] { 8, 12 })
                    if (Read(p.SectionAbs + relative) is uint ptr && ptr != 0)
                        Opaque((long)p.SectionAbs + ptr);
                foreach (var slot in p.Slots)
                {
                    Protect(slot.RecordOffset, (long)slot.NativeRecordWidth * slot.KeyframeCount + 4, false, true);
                    if (slot.SecondaryCallbackRel != 0)
                        Opaque((long)p.SectionAbs + slot.SecondaryCallbackRel);
                }
            }
            for (int i = 0; i < root.Aux3Count; i++)
                if (Read(r + root.Table3Rel + 4 * i) is uint ptr && ptr != 0) Opaque((long)r + ptr);
            for (int i = 0; i < root.Aux4Count; i++)
            {
                int at = r + root.Table4Rel + 8 * i;
                uint ptr = Read(at + 4);
                if (ptr != 0) Protect((long)r + ptr, 2L * U16(at + 2));
            }
            foreach (var descriptor in root.Descriptors)
            {
                int at = descriptor.EntryAbs;
                uint count = Read(at), ptr = Read(at + 20);
                long start = (long)r + ptr;
                var resource = new MagicGeometryResource(r, descriptor.Index, at, count,
                    start <= int.MaxValue ? (int)start : -1);
                resources.Add(resource);
                uint normals = Read(at + 24), mesh = Read(at + 28);
                if (normals != 0) normalArrays.Add((resource, (long)r + normals, Read(at + 4)));
                if (count == 0) continue;
                if (ptr == 0 || (start & 1) != 0 || !Fits(start, 6L * count))
                    resource.Error = "Point array is missing, unaligned or outside .data.";
                if (mesh == 0) continue; // Point-only resources are native, not malformed meshes.
                long header = (long)r + mesh;
                Protect(header, 32);
                if (!Fits(header, 32))
                {
                    resource.Error = "Mesh header is outside .data.";
                    continue;
                }
                int h = (int)header;
                long vertices = header + Read(h + 8), normalStart = header + Read(h + 12);
                uint vertexCount = U16(h + 18);
                if (normals != 0 && normalStart != (long)r + normals)
                    resource.Error = "Mesh and descriptor normal pointers disagree.";
                // Geometry is serialized before its point array in this proven layout.
                // Validate each primitive so a forged point pointer cannot cut into its data.
                if (vertices != start || vertexCount != count || start < header + 32 ||
                    !ValidatePrimitives(h, start, vertexCount))
                    resource.Error = "Mesh points or primitive layout is not supported for authoring.";
                else Protect(header, start - header);
            }
        }

        // Parameter arrays and untyped resources end at the next declared object. They may
        // never alias a point array, even when a malicious pointer gives both the same start.
        int[] boundaries = protectedRanges.SelectMany(p => new[] { p.Start, p.Start + p.Size })
            .Concat(resources.Where(r => r.PointDataOffset >= 0).Select(r => r.PointDataOffset))
            .Concat(normalArrays.Where(n => Fits(n.Start, 1)).Select(n => (int)n.Start))
            .Concat(opaqueStarts).Append(_source.Length).Distinct().OrderBy(x => x).ToArray();
        foreach (var (owner, start, count) in normalArrays)
        {
            long end = start + 6L * count;
            int next = Fits(start, 1) ? boundaries.First(x => x > start) : _source.Length;
            // Descriptor +4 is the serialized count of six-byte vectors at +24.
            // Confirm the extent against an independent declared object and zero
            // alignment padding; never use the position count for this other array.
            bool valid = count > 0 && (start & 1) == 0 && Fits(start, 6L * count) &&
                end <= next && (next == end || next == owner.RootOffset +
                    ((end - owner.RootOffset + 3) & ~3L) || next == owner.RootOffset +
                    ((end - owner.RootOffset + 15) & ~15L)) &&
                _source.AsSpan((int)end, next - (int)end).IndexOfAnyExcept((byte)0) < 0;
            if (!valid) owner.Error = "Normal array does not fit its serialized allocation boundary.";
            if (Fits(start, 1)) Protect(start, next - start);
        }
        foreach (int at in opaqueStarts)
            Protect(at, boundaries.First(x => x > at) - at);
        var protectedSorted = protectedRanges.OrderBy(p => p.Start).ToArray();
        var candidates = resources.Where(r => r.PointCount > 0 && r.Error == null)
            .OrderBy(r => r.PointDataOffset).ToArray();
        foreach (var resource in candidates)
        {
            long end = resource.PointDataOffset + 6L * resource.PointCount;
            if (protectedSorted.Any(p => p.Start < end && (long)p.Start + p.Size > resource.PointDataOffset))
                resource.Error = "Point array overlaps PPP metadata, commands or another resource.";
        }
        for (int i = 0; i < candidates.Length; i++)
        for (int j = i + 1; j < candidates.Length && candidates[j].PointDataOffset <
                 candidates[i].PointDataOffset + 6L * candidates[i].PointCount; j++)
            if ((candidates[j].PointDataOffset - candidates[i].PointDataOffset) % 6 != 0)
                candidates[i].Error = candidates[j].Error = "Overlapping point arrays disagree on XYZ alignment.";

        int expandedPoints = 0;
        var pointArrays = new Dictionary<(int Start, uint Count), IReadOnlyList<MagicGeometryPoint>>();
        foreach (var resource in resources.Where(r => r.Error == null && r.PointCount > 0))
        {
            var key = (resource.PointDataOffset, resource.PointCount);
            if (pointArrays.TryGetValue(key, out var shared))
            {
                resource.Points = shared;
                continue;
            }
            if (resource.PointCount > MaxExpandedPoints - expandedPoints)
            {
                resource.Error = "Point arrays exceed the bounded document authoring budget.";
                continue;
            }
            expandedPoints += (int)resource.PointCount;
            var points = new MagicGeometryPoint[(int)resource.PointCount];
            for (int i = 0; i < points.Length; i++)
            {
                var axes = new MagicGeometryField[3];
                for (int axis = 0; axis < 3; axis++)
                {
                    int at = resource.PointDataOffset + 6 * i + 2 * axis;
                    if (!_fields.TryGetValue(at, out var field))
                        _fields.Add(at, field = new MagicGeometryField(at, axis, BitConverter.ToInt16(_source, at)));
                    axes[axis] = field;
                }
                points[i] = new MagicGeometryPoint(i, Array.AsReadOnly(axes));
            }
            resource.Points = Array.AsReadOnly(points);
            pointArrays.Add(key, resource.Points);
        }
        Resources = Array.AsReadOnly(resources.ToArray());
        Fields = Array.AsReadOnly(_fields.Values.OrderBy(f => f.DataOffset).ToArray());
        NonRecordRanges = Array.AsReadOnly(nonRecordRanges.Distinct().ToArray());
    }

    private bool ValidatePrimitives(int header, long points, uint vertices)
    {
        int remaining = U16(header + 16);
        long cursor = (long)header + Read(header + 4);
        if (cursor < header + 32 || cursor > points) return false;
        while (remaining > 0)
        {
            if (!Fits(cursor, 16) || cursor + 16 > points) return false;
            int at = (int)cursor, kind = _source[at + 1], count = U16(at + 2);
            int stride = kind switch { 0 => 20, 1 => 28, 2 => 32, 3 => 40,
                4 => 24, 5 => 32, 6 => 40, 7 => 48, _ => 0 };
            if (stride == 0 || count == 0 || count > remaining) return false;
            long end = cursor + 16L + count * stride;
            if (end > points) return false;
            int perPrimitive = kind < 4 ? 3 : 4;
            for (int i = 0; i < count; i++)
            for (int j = 0; j < perPrimitive; j++)
                if (U16(at + 16 + i * stride + 4 * perPrimitive + 2 * j) >= vertices) return false;
            cursor = end;
            remaining -= count;
        }
        return true;
    }

    internal bool TryApply(byte[] working, IReadOnlyList<(MagicGeometryField Field, short Value)> edits,
        out byte[]? result, out string error)
    {
        result = null;
        error = string.Empty;
        if (working.Length != _fileLength || _dataPtr > working.Length - _source.Length)
            return Fail("PPP geometry layout changed.", out error);
        var values = new Dictionary<int, short>();
        foreach (var (field, value) in edits)
        {
            if (field == null || !_fields.TryGetValue(field.DataOffset, out var issued) || !ReferenceEquals(field, issued))
                return Fail("Geometry operand does not belong to this document snapshot.", out error);
            if (values.TryGetValue(field.DataOffset, out short previous) && previous != value)
                return Fail("Shared geometry operands have conflicting values.", out error);
            values[field.DataOffset] = value;
        }
        foreach (var (at, size) in _anchors.Distinct())
            if (!working.AsSpan(_dataPtr + at, size).SequenceEqual(_source.AsSpan(at, size)))
                return Fail("PPP geometry structural metadata changed.", out error);
        result = (byte[])working.Clone();
        foreach (var (at, value) in values) BitConverter.GetBytes(value).CopyTo(result, _dataPtr + at);
        return true;
    }

    private bool Fits(long at, long size) => at >= 0 && size >= 0 && at <= _source.Length - size;
    private uint Read(int at) => Fits(at, 4) ? BitConverter.ToUInt32(_source, at) : 0;
    private ushort U16(int at) => Fits(at, 2) ? BitConverter.ToUInt16(_source, at) : (ushort)0;
    private static bool Fail(string message, out string error) { error = message; return false; }
}

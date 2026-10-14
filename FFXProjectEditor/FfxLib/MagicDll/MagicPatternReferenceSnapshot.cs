using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.MagicDll;

/// <summary>VertexAttend +12 selects an Aux4 emission pattern. Native consumers use
/// signed descriptor/count words and a root-relative U16 vertex index stream.
/// Retain the original stream capacity across substitutions and anchor its layout.</summary>
internal sealed class MagicPatternReferenceSnapshot
{
    private sealed record Pattern(int Count, uint Points);
    private readonly byte[] _source;
    private readonly int _raw;
    private readonly int _fileLength;
    private readonly Dictionary<int, List<(int Root, short Original)>> _owners = new();
    private readonly Dictionary<(int Root, int Index), Pattern> _patterns = new();
    private readonly List<(int Start, int Size)> _anchors;

    internal MagicPatternReferenceSnapshot(MagicDllFile file, MagicGeometrySnapshot geometry)
    {
        _raw = file.DataSectionRawPtr;
        _fileLength = file.FileBytes.Length;
        if (!file.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots).Any(s => s.OpcodeName == "pppVertexAttend"))
        {
            _source = Array.Empty<byte>();
            _anchors = new();
            return;
        }
        _source = file.Data.ToArray();
        _anchors = geometry.ImmutableRanges.ToList();
        var records = file.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots)
            .Select(s => (Start: s.RecordOffset, End: (long)s.RecordOffset + (long)s.NativeRecordWidth * s.KeyframeCount + 4))
            .ToArray();
        var resources = geometry.Resources.ToDictionary(r => (r.RootOffset, r.DescriptorIndex));
        long wordsRead = 0;
        foreach (var root in file.Roots)
        {
            long table = (long)root.RootAbs + root.Table4Rel;
            if (!Fits(table, root.Aux4Count * 8L)) continue;
            for (int index = 0; index < root.Aux4Count; index++)
            {
                int entry = (int)table + index * 8;
                int descriptor = BitConverter.ToInt16(_source, entry);
                int count = BitConverter.ToInt16(_source, entry + 2);
                uint relative = BitConverter.ToUInt32(_source, entry + 4);
                long start = (long)root.RootAbs + relative;
                if (descriptor < 0 || count <= 0 || relative == 0 || (start & 1) != 0 ||
                    !Fits(start, count * 2L) || wordsRead + count > 2_000_000 ||
                    !resources.TryGetValue((root.RootAbs, descriptor), out var resource) ||
                    resource.Error != null || resource.PointCount == 0 ||
                    records.Any(r => start < r.End && start + count * 2L > r.Start)) continue;
                wordsRead += count;
                bool valid = true;
                for (int i = 0; i < count; i++)
                    if (BitConverter.ToUInt16(_source, (int)start + i * 2) >= resource.PointCount)
                    { valid = false; break; }
                if (!valid) continue;
                _patterns[(root.RootAbs, index)] = new Pattern(count, resource.PointCount);
                _anchors.Add((entry, 8));
                _anchors.Add(((int)start, count * 2));
            }
            foreach (var slot in root.Programs.SelectMany(p => p.Slots).Where(s => s.OpcodeName == "pppVertexAttend"))
            {
                if (slot.NativeRecordWidth < 14) continue;
                for (int frame = 0; frame < slot.KeyframeCount; frame++)
                {
                    int record = checked(slot.RecordOffset + frame * slot.NativeRecordWidth);
                    if (!Fits(record, slot.NativeRecordWidth)) continue;
                    if (!_owners.TryGetValue(record, out var owners))
                        _owners.Add(record, owners = new());
                    owners.Add((root.RootAbs, BitConverter.ToInt16(_source, record + 12)));
                }
            }
        }
    }

    internal bool TryValidate(byte[] working, int record, short requested)
    {
        if (working.Length != _fileLength || !_owners.TryGetValue(record, out var owners) || owners.Count == 0)
            return false;
        foreach (var span in _anchors)
            if (!Fits(span.Start, span.Size) ||
                !_source.AsSpan(span.Start, span.Size).SequenceEqual(working.AsSpan(_raw + span.Start, span.Size)))
                return false;
        // Native VertexAttend skips all negative pattern indices, including -1.
        if (requested < 0) return true;
        foreach (var owner in owners)
        {
            if (!_patterns.TryGetValue((owner.Root, requested), out var next) ||
                !_patterns.TryGetValue((owner.Root, owner.Original), out var original) ||
                next.Count != original.Count || next.Points != original.Points)
                return false;
        }
        return true;
    }

    private bool Fits(long at, long size) => at >= 0 && size >= 0 && at <= _source.Length - size;
}

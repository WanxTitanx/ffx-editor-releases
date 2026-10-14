using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace FFXProjectEditor.FfxLib.MagicDll;

/// <summary>Native +4 operands with private ownership, immutable layout anchors and bounded
/// resource choices. No arbitrary pointer, timestamp or resource-table write is exposed.</summary>
internal sealed class MagicPrefixSnapshot
{
    internal const uint DisabledResource = 0xFFFF;
    private readonly byte[] _source;
    private readonly int _raw;
    private readonly int _length;
    private readonly Dictionary<int, MagicPrefixOperand> _fields = new();
    private readonly List<(int Start, int Size)> _anchors = new();
    internal IReadOnlyList<MagicPrefixOperand> Fields { get; }
    private sealed record Owner(MagicDllRoot Root, MagicProgram Program, MagicSlot Slot);

    // Exact native record reads, not an exception for every schema at offset four.
    private static MagicPrefixOperandKind? Kind(string? opcode) => opcode switch
    {
        "pppKeLnsLp" or "pppKeLnsLpSft" or "pppKeLnsLpT" or "pppKeGrvTgt" or
        "pppNeiLightEikyo" or "pppNeiPointLight" or "pppFpPointLight" or "pppFpPointLightModel" or
        "pppFpPointLightModelScl" or "pppFpPointLightVsf" or "pppFpPointLightVsfScl" or "pppDrawRain" or
        "pppKeHitBall" or "pppKeThTp" or "pppKeThTp2" or "pppKeHmgEff" or "pppPointRAp" or "pppKeShpDtt" => MagicPrefixOperandKind.Float,
        "pppDrawMdl" or "pppDrawMdl2" or "pppDrawMdlSemi2" or "pppDrawMdlSemi3" or
            "pppDrawShape" => MagicPrefixOperandKind.DrawResource,
        _ => null,
    };

    internal MagicPrefixSnapshot(MagicDllFile file, MagicGeometrySnapshot geometry)
    {
        _raw = file.DataSectionRawPtr;
        _length = file.FileBytes.Length;
        _source = file.FileBytes.AsSpan(_raw, file.Data.Length).ToArray();
        var owners = new Dictionary<int, List<Owner>>();
        var protectedRanges = new List<(int Start, long Size)>();
        protectedRanges.AddRange(geometry.NonRecordRanges.Select(p => (p.Start, (long)p.Size)));
        _anchors.AddRange(geometry.ImmutableRanges);
        var timestamps = new HashSet<int>();
        var layouts = new Dictionary<(int Root, uint Index), string>();
        bool Fits(long at, long size) => at >= 0 && size >= 0 && at <= _source.Length - size;
        void Protect(int at, long size)
        {
            if (Fits(at, size) && size > 0) protectedRanges.Add((at, size));
        }
        foreach (var root in file.Roots)
        {
            Protect(root.RootAbs, 32);
            Protect(root.RootAbs + root.Table1Rel, 4L * root.PrimaryCount);
            Protect(root.RootAbs + root.Table2Rel, 32L * root.DescriptorCount);
            Protect(root.RootAbs + root.Table3Rel, 4L * root.Aux3Count);
            Protect(root.RootAbs + root.Table4Rel, 8L * root.Aux4Count);
            foreach (var program in root.Programs)
            {
                Protect(program.SectionAbs, 16);
                Protect(program.ProgramAbs, 40L + 16L * program.SlotCount);
                foreach (var slot in program.Slots)
                {
                    foreach (var frame in slot.Keyframes)
                    {
                        timestamps.Add(frame.RecordOffset);
                        if (!owners.TryGetValue(frame.RecordOffset, out var list))
                            owners.Add(frame.RecordOffset, list = new());
                        list.Add(new Owner(root, program, slot));
                    }
                }
            }
            foreach (var resource in geometry.Resources.Where(r => r.RootOffset == root.RootAbs))
            {
                Protect(resource.PointDataOffset, 6L * resource.PointCount);
                int at = resource.DescriptorOffset;
                uint normal = Read(at + 24), normalCount = Read(at + 4), mesh = Read(at + 28);
                if (normal != 0) Protect(root.RootAbs + (int)normal, 6L * normalCount);
                if (mesh == 0 || resource.Error != null || resource.Points.Count == 0) continue;
                int h = root.RootAbs + (int)mesh;
                int primitive = h + (int)Read(h + 4);
                int end = resource.PointDataOffset;
                if (!Fits(h, 32) || !Fits(primitive, end - (long)primitive)) continue;
                byte[] header = _source.AsSpan(h, 32).ToArray();
                header.AsSpan(4, 12).Clear(); // Serialized self-relative pointers may differ.
                byte[] signature = new byte[40 + end - primitive];
                BitConverter.GetBytes(resource.PointCount).CopyTo(signature, 0);
                BitConverter.GetBytes(normalCount).CopyTo(signature, 4);
                header.CopyTo(signature, 8);
                _source.AsSpan(primitive, end - primitive).CopyTo(signature.AsSpan(40));
                layouts[(root.RootAbs, (uint)resource.DescriptorIndex)] =
                    Convert.ToHexString(SHA256.HashData(signature));
                _anchors.Add((h, 32));
                _anchors.Add((primitive, end - primitive));
                Protect(h, end - (long)h);
            }
        }

        var shapeLayouts = MagicShapeResourceLayouts.Read(_source, file.Roots, _anchors, protectedRanges);

        foreach (var (record, list) in owners.OrderBy(p => p.Key))
        {
            var kind = Kind(list[0].Slot.OpcodeName);
            if (kind == null || !Fits(record, 8)) continue;
            uint bits = Read(record + 4);
            string? error = null;
            if (list.Any(o => Kind(o.Slot.OpcodeName) != kind || o.Slot.NativeRecordWidth < 8 ||
                    o.Slot.KeyframeCount == 0 || o.Slot.Keyframes.Count != o.Slot.KeyframeCount))
                error = "Shared record owners disagree on the native +4 operand contract.";
            if (Enumerable.Range(1, 7).Any(delta => timestamps.Contains(record + delta)) ||
                protectedRanges.Any(p => p.Start < record + 8 && p.Start + p.Size > record + 4))
                error = "The +4 operand overlaps PPP metadata, a timestamp or a resource.";
            var choices = new HashSet<uint>();
            if (kind == MagicPrefixOperandKind.Float)
            {
                if (!float.IsFinite(BitConverter.UInt32BitsToSingle(bits)))
                    error = "Special floating-point operands are preserved read-only.";
            }
            else
            {
                HashSet<uint>? consensus = null;
                foreach (var owner in list)
                {
                    var nativeLayouts = owner.Slot.OpcodeName == "pppDrawShape" ? shapeLayouts : layouts;
                    var bases = owner.Slot.Keyframes.Select(f => Read(f.RecordOffset + 4))
                        .Where(value => value != DisabledResource).Distinct().ToArray();
                    var allowed = new HashSet<uint> { DisabledResource };
                    if (bases.Length > 0 && bases.All(i => nativeLayouts.ContainsKey((owner.Root.RootAbs, i))))
                    {
                        string[] signatures = bases.Select(i => nativeLayouts[(owner.Root.RootAbs, i)]).Distinct().ToArray();
                        if (signatures.Length == 1)
                            allowed.UnionWith(nativeLayouts.Where(p => p.Key.Root == owner.Root.RootAbs &&
                                p.Value == signatures[0]).Select(p => p.Key.Index));
                    }
                    else if (bits != DisabledResource)
                        error = "The original draw resource could not be validated.";
                    consensus ??= new HashSet<uint>(allowed);
                    consensus.IntersectWith(allowed);
                }
                if (consensus != null) choices.UnionWith(consensus);
                // The existing reference can always be kept; changing it must satisfy the
                // complete sequence/owner consensus, including topology and texture keys.
                choices.Add(bits);
                if (choices.Count < 2) error ??= "No compatible draw resource is available.";
            }
            var field = new MagicPrefixOperand(record, kind.Value, list[0].Slot.OpcodeName!, bits,
                error == null, Array.AsReadOnly(choices.OrderBy(x => x).ToArray()), error);
            _fields.Add(field.DataOffset, field);
            foreach (var owner in list)
            {
                _anchors.Add((owner.Root.RootAbs, 32));
                _anchors.Add((owner.Root.RootAbs + owner.Root.Table2Rel, 32 * owner.Root.DescriptorCount));
                _anchors.Add((owner.Slot.SlotAbs, 16));
                _anchors.Add((owner.Program.SectionAbs + 4, 12));
                _anchors.Add((owner.Program.ProgramAbs, 16));
                _anchors.Add((owner.Program.ProgramAbs + 32, 8));
                int sentinel = owner.Slot.RecordOffset + owner.Slot.NativeRecordWidth * owner.Slot.KeyframeCount;
                if (Fits(sentinel, 4)) _anchors.Add((sentinel, 4));
            }
        }
        Fields = Array.AsReadOnly(_fields.Values.ToArray());
    }

    internal bool TryApply(byte[] working, IReadOnlyList<(MagicPrefixOperand Field, uint Bits)> edits,
        out byte[]? result, out string error)
    {
        result = null;
        error = string.Empty;
        if (working.Length != _length || _raw > working.Length - _source.Length)
            return Fail("Native +4 document layout changed.", out error);
        var values = new Dictionary<int, uint>();
        foreach (var (field, bits) in edits)
        {
            if (field == null || !_fields.TryGetValue(field.DataOffset, out var issued) ||
                !ReferenceEquals(field, issued) || !field.IsEditable)
                return Fail("The +4 operand was not issued by this writable document.", out error);
            if (field.Kind == MagicPrefixOperandKind.Float && !float.IsFinite(BitConverter.UInt32BitsToSingle(bits)))
                return Fail("The native float operand requires a finite float32 value.", out error);
            if (field.Kind == MagicPrefixOperandKind.DrawResource && !field.ResourceChoices.Contains(bits))
                return Fail("Draw resource is outside the compatible native resource choices.", out error);
            if (values.TryGetValue(field.DataOffset, out uint previous) && previous != bits)
                return Fail("Shared +4 operands have conflicting edits.", out error);
            values[field.DataOffset] = bits;
        }
        foreach (var (at, size) in _anchors.Distinct())
            if (!working.AsSpan(_raw + at, size).SequenceEqual(_source.AsSpan(at, size)))
                return Fail("Native +4 structural metadata or resource topology changed.", out error);
        result = (byte[])working.Clone();
        foreach (var (at, value) in values) BitConverter.GetBytes(value).CopyTo(result, _raw + at);
        return true;
    }
    private uint Read(int at) => at >= 0 && at <= _source.Length - 4 ? BitConverter.ToUInt32(_source, at) : 0;
    private static bool Fail(string message, out string error) { error = message; return false; }
}

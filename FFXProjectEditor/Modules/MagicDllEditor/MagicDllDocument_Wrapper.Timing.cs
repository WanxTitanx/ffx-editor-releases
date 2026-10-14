using System;
using System.Collections.Generic;
using FFXProjectEditor.FfxLib.MagicDll;

namespace FFXProjectEditor.Modules.MagicDllEditor;

public sealed partial class MagicDllDocument_Wrapper
{
    private MagicTimingSnapshot? _timingSnapshot;
    private MagicGeometrySnapshot? _geometrySnapshot;
    private MagicPrefixSnapshot? _prefixSnapshot;
    public IReadOnlyList<MagicPrefixOperand> PrefixOperands =>
        _prefixSnapshot?.Fields ?? Array.Empty<MagicPrefixOperand>();
    public bool TryApplyPrefixEdits(IReadOnlyList<(MagicPrefixOperand Field, uint Bits)> edits,
        out string error) => TryApplyDocumentEdits(Array.Empty<(MagicField, byte[])>(),
            Array.Empty<(MagicTimingField, uint)>(), Array.Empty<(MagicGeometryField, short)>(),
            edits, out error);
    public IReadOnlyList<MagicTimingField> TimingFields =>
        _timingSnapshot?.Fields ?? Array.Empty<MagicTimingField>();
    public IReadOnlyList<MagicGeometryResource> GeometryResources =>
        _geometrySnapshot?.Resources ?? Array.Empty<MagicGeometryResource>();
    public IReadOnlyList<MagicGeometryField> GeometryFields =>
        _geometrySnapshot?.Fields ?? Array.Empty<MagicGeometryField>();

    public bool TryApplyGeometryEdits(IReadOnlyList<(MagicGeometryField Field, short Value)> edits,
        out string error) => TryApplyDocumentEdits(Array.Empty<(MagicField, byte[])>(),
            Array.Empty<(MagicTimingField, uint)>(), edits, out error);

    public bool TryApplyTimingEdits(IReadOnlyList<(MagicTimingField Field, uint Tick)> edits,
        out string error) => TryApplyDocumentEdits(Array.Empty<(MagicField, byte[])>(), edits, out error);

    internal bool TryApplyDocumentEdits(IReadOnlyList<(MagicField Field, byte[] Bytes)> payload,
        IReadOnlyList<(MagicTimingField Field, uint Tick)> timing, out string error) =>
        TryApplyDocumentEdits(payload, timing, Array.Empty<(MagicGeometryField, short)>(), out error);

    internal bool TryApplyDocumentEdits(IReadOnlyList<(MagicField Field, byte[] Bytes)> payload,
        IReadOnlyList<(MagicTimingField Field, uint Tick)> timing,
        IReadOnlyList<(MagicGeometryField Field, short Value)> geometry, out string error) =>
        TryApplyDocumentEdits(payload, timing, geometry, Array.Empty<(MagicPrefixOperand, uint)>(), out error);

    internal bool TryApplyDocumentEdits(IReadOnlyList<(MagicField Field, byte[] Bytes)> payload,
        IReadOnlyList<(MagicTimingField Field, uint Tick)> timing,
        IReadOnlyList<(MagicGeometryField Field, short Value)> geometry,
        IReadOnlyList<(MagicPrefixOperand Field, uint Bits)> prefix, out string error)
    {
        error = string.Empty;
        if (_workingBytes == null || _timingSnapshot == null || _geometrySnapshot == null || _prefixSnapshot == null)
        {
            error = "No document loaded.";
            return false;
        }
        byte[] previous = (byte[])_workingBytes.Clone();
        string previousSha = ShaAfter;
        byte[]? candidate = null;
        if (!TryApplyFieldEdits(payload, out error)) return false;
        if (timing.Count > 0 && !_timingSnapshot.TryApply(_workingBytes, timing,
                out candidate, out error))
        {
            _workingBytes = previous;
            ShaAfter = previousSha;
            return false;
        }
        candidate ??= _workingBytes;
        if (geometry.Count > 0 && !_geometrySnapshot.TryApply(candidate, geometry, out candidate, out error))
        {
            _workingBytes = previous;
            ShaAfter = previousSha;
            return false;
        }
        if (prefix.Count > 0 && !_prefixSnapshot.TryApply(candidate!, prefix, out candidate, out error))
        {
            _workingBytes = previous;
            ShaAfter = previousSha;
            return false;
        }
        if (timing.Count > 0 || geometry.Count > 0 || prefix.Count > 0)
        {
            // Revalidate the exact candidate before promotion. A time change cannot
            // silently remove PPP roots/programs by changing parser bounds.
            if (!TryParseBytes(candidate!, out MagicDllFile? parsed, out error) ||
                parsed!.Roots.Count != _file!.Roots.Count ||
                System.Linq.Enumerable.Sum(parsed.Roots, r => r.TotalSlots) !=
                    System.Linq.Enumerable.Sum(_file.Roots, r => r.TotalSlots))
            {
                _workingBytes = previous;
                ShaAfter = previousSha;
                if (string.IsNullOrEmpty(error)) error = "PPP document edit changed the parsed structure.";
                return false;
            }
            _workingBytes = candidate;
            ShaAfter = ComputeSha256Hex(_workingBytes!);
        }
        return true;
    }
}

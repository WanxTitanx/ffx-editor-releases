using System;
using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.MagicDll;

/// <summary>A signed coordinate issued by one immutable geometry snapshot.</summary>
public sealed class MagicGeometryField
{
    public int DataOffset { get; }
    public int Axis { get; }
    public short OriginalValue { get; }
    internal MagicGeometryField(int offset, int axis, short value)
    {
        DataOffset = offset;
        Axis = axis;
        OriginalValue = value;
    }
}

public sealed record MagicGeometryPoint(int Index, IReadOnlyList<MagicGeometryField> Components);

/// <summary>Descriptor +0 is the point count; +20 locates signed XYZ triples relative to the root.
/// A null +28 mesh is valid for point-only resources. Pointer/count words remain read-only.</summary>
public sealed class MagicGeometryResource
{
    public int RootOffset { get; }
    public int DescriptorIndex { get; }
    public int DescriptorOffset { get; }
    public uint PointCount { get; }
    public int PointDataOffset { get; }
    public string? Error { get; internal set; }
    public IReadOnlyList<MagicGeometryPoint> Points { get; internal set; } = Array.Empty<MagicGeometryPoint>();

    internal MagicGeometryResource(int root, int index, int descriptor, uint count, int points)
    {
        RootOffset = root;
        DescriptorIndex = index;
        DescriptorOffset = descriptor;
        PointCount = count;
        PointDataOffset = points;
    }
}

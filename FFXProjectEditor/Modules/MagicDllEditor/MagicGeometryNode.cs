using System.Globalization;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MagicDllEditor;

public sealed class MagicGeometryNode : MagicNode
{
    public MagicGeometryField Source { get; }
    public string OriginalValue { get; private set; }
    public bool IsDirty => Value != OriginalValue;
    public void MarkSaved() => OriginalValue = Value;
    public void ResetToOriginal() => Value = OriginalValue;
    public MagicGeometryNode(MagicGeometryField source)
    {
        Source = source;
        Value = source.OriginalValue.ToString(CultureInfo.InvariantCulture);
        OriginalValue = Value;
    }
    public override string Label => Source.Axis switch { 0 => "X", 1 => "Y", _ => "Z" };
    public override string TypeLabel => "s16";
    public override string OffsetLabel => $".data+0x{Source.DataOffset:X}";
    public override string MetaText => $"{OffsetLabel} · {Value}";
    public override bool IsNumeric => true;
    public override bool IsValueEditable => true;
    public double MinValue => short.MinValue;
    public double MaxValue => short.MaxValue;
    public string TypeHumanLabel => Strings.U_Md_GeometryCoordinateType;
    public override string DetailText => Strings.U_Md_GeometryCoordinateHint;
    public bool IsReadOnlyNoticeVisible => false;
}

internal sealed class MagicGeometryNoticeNode : MagicNode
{
    public override string Label => Strings.U_Md_GeometryUnavailable;
    public override string DetailText => Strings.U_Md_GeometryUnsupportedHint;
    public string ReadOnlyReason => DetailText;
    public bool IsReadOnlyNoticeVisible => true;
}

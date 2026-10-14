using System.Globalization;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MagicDllEditor;

public sealed class MagicTimingNode : MagicNode
{
    public MagicTimingField Source { get; }
    public string OriginalValue { get; private set; }
    public bool IsDirty => Value != OriginalValue;
    public void MarkSaved() => OriginalValue = Value;
    public void ResetToOriginal() => Value = OriginalValue;

    public MagicTimingNode(MagicTimingField field)
    {
        Source = field;
        Value = field.Frames?.ToString("0.############", CultureInfo.InvariantCulture)
            ?? $"0x{field.OriginalTick:X8}";
        OriginalValue = Value;
    }
    public override string Label => Source.Kind switch
    {
        MagicTimingKind.SectionDuration => Strings.U_Md_SectionDuration,
        MagicTimingKind.ProgramStart => Strings.U_Md_ProgramStart,
        MagicTimingKind.ProgramDuration => Strings.U_Md_ProgramDuration,
        MagicTimingKind.LoopStart => Strings.U_Md_LoopStart,
        MagicTimingKind.LoopEnd => Strings.U_Md_LoopEnd,
        _ => Strings.U_Md_KeyframeTime,
    };
    public override string TypeLabel => "q12";
    public override string OffsetLabel => $".data+0x{Source.DataOffset:X}";
    public override string MetaText => $"{OffsetLabel} · {Value}";
    public override bool IsNumeric => Source.Frames.HasValue;
    public override bool IsValueEditable => Source.IsEditable;
    public double MinValue => 0;
    public double MaxValue => MagicTimingSnapshot.Infinite / 4096 - 1;
    public string TypeHumanLabel => Strings.U_Md_TimingFrames;
    public override string DetailText => Strings.U_Md_TimingHint;
    public string ReadOnlyReason => Strings.U_Md_TimingSpecialReadOnly;
    public bool IsReadOnlyNoticeVisible => !IsValueEditable;
}

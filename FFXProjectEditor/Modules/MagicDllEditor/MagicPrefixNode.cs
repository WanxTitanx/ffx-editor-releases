using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MagicDllEditor;

public sealed record MagicResourceChoice(uint Index, bool IsFlipbook = false)
{
    public override string ToString() => Index == MagicPrefixSnapshot.DisabledResource
        ? Strings.U_Md_ResourceDisabled
        : string.Format(CultureInfo.CurrentCulture, IsFlipbook ? Strings.U_Md_ShapeResourceChoice : Strings.U_Md_ResourceChoice, Index);
}

public sealed class MagicPrefixNode : MagicNode
{
    public MagicPrefixOperand Source { get; }
    public override IReadOnlyList<MagicResourceChoice> Choices { get; }
    public string OriginalValue { get; private set; }
    public bool IsDirty => Value != OriginalValue;
    public void MarkSaved() => OriginalValue = Value;
    public void ResetToOriginal()
    {
        Value = OriginalValue;
        if (IsChoice && uint.TryParse(Value, out uint index))
            SelectedChoice = Choices.FirstOrDefault(c => c.Index == index);
    }
    private MagicResourceChoice? _selectedChoice;
    public override MagicResourceChoice? SelectedChoice
    {
        get => _selectedChoice;
        set
        {
            if (SetProperty(ref _selectedChoice, value) && value != null)
                Value = value.Index.ToString(CultureInfo.InvariantCulture);
        }
    }
    public MagicPrefixNode(MagicPrefixOperand source)
    {
        Source = source;
        Choices = Array.AsReadOnly(source.ResourceChoices.Select(i => new MagicResourceChoice(i, source.Opcode == "pppDrawShape")).ToArray());
        Value = source.Kind == MagicPrefixOperandKind.Float
            ? BitConverter.UInt32BitsToSingle(source.OriginalBits).ToString("R", CultureInfo.InvariantCulture)
            : source.OriginalBits.ToString(CultureInfo.InvariantCulture);
        if (IsChoice) SelectedChoice = Choices.FirstOrDefault(c => c.Index == source.OriginalBits);
        OriginalValue = Value;
    }
    public override string Label => Source.Kind == MagicPrefixOperandKind.DrawResource
        ? Strings.U_Md_DrawResource
        : Source.Opcode == "pppKeGrvTgt" ? Strings.U_Md_TargetParameter
        : Source.Opcode is not ("pppKeLnsLp" or "pppKeLnsLpT") ? string.Format(CultureInfo.CurrentCulture, Strings.U_Md_NativeOperand, "0x04")
        : Strings.U_Md_SpringDelta;
    public override string TypeLabel => Source.Kind == MagicPrefixOperandKind.Float ? "f32" : "u32";
    public override string OffsetLabel => $".data+0x{Source.DataOffset:X}";
    public override string MetaText => $"{OffsetLabel} · {(IsChoice ? SelectedChoice?.ToString() : Value)}";
    public override bool IsChoice => Source.Kind == MagicPrefixOperandKind.DrawResource;
    public override bool IsNumeric => Source.Kind == MagicPrefixOperandKind.Float &&
        float.IsFinite(BitConverter.UInt32BitsToSingle(Source.OriginalBits));
    public override bool IsValueEditable => Source.IsEditable;
    public double MinValue => float.MinValue;
    public double MaxValue => float.MaxValue;
    public string TypeHumanLabel => TypeLabel;
    public override string DetailText => IsChoice ? (Source.Opcode == "pppDrawShape" ? Strings.U_Md_ShapeResourceChoiceHint : Strings.U_Md_ResourceChoiceHint) :
        Source.Opcode == "pppKeGrvTgt" ? Strings.U_Md_TargetParameterHint
        : Source.Opcode is not ("pppKeLnsLp" or "pppKeLnsLpT") ? Strings.U_Md_NativeOperandHint
        : Strings.U_Md_SpringDeltaHint;
    public string ReadOnlyReason => Strings.U_Md_PrefixReadOnly;
    public bool IsReadOnlyNoticeVisible => !IsValueEditable;
}

using System;
using System.Collections.Generic;
using System.Globalization;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MagicDllEditor;

public sealed class MagicKeyframeNode : MagicNode
{
    private readonly MagicKeyframe _frame;
    public override IReadOnlyList<MagicNode> Children { get; }

    public MagicKeyframeNode(MagicKeyframe frame, IReadOnlyList<MagicNode> fields)
    {
        _frame = frame;
        Children = fields;
        Value = Convert.ToHexString(frame.Record);
    }

    public override string Label => string.Format(CultureInfo.CurrentCulture,
        Strings.U_Md_KeyframeLabel, _frame.Index + 1);
    private string Time => _frame.Frame?.ToString("0.###", CultureInfo.CurrentCulture)
        ?? string.Format(CultureInfo.CurrentCulture, Strings.U_Md_KeyframeSpecialTick, $"{_frame.Tick:X8}");
    public override string MetaText => $"{Time} · 0x{_frame.RecordOffset:X}";
    public override string DetailText => string.Format(CultureInfo.CurrentCulture,
        Strings.U_Md_KeyframeDetail, Time, $"{_frame.RecordOffset:X}", _frame.Record.Length);
}

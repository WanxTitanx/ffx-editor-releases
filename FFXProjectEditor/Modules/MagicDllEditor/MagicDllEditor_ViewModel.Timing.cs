using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MagicDllEditor;

internal partial class MagicDllEditor_ViewModel
{
    private readonly Dictionary<(MagicTimingKind Kind, int Offset), MagicTimingNode> _timingNodes = new();
    private Dictionary<(MagicTimingKind Kind, int Offset), MagicTimingField> _timingFields = new();
    private void AddKeyframeTime(List<MagicNode> fields, int at)
    {
        if (_timingFields.TryGetValue((MagicTimingKind.KeyframeTime, at), out var field))
            fields.Insert(0, GetTimingNode(field));
    }
    private MagicTimingNode GetTimingNode(MagicTimingField field)
    {
        var key = (field.Kind, field.DataOffset);
        if (_timingNodes.TryGetValue(key, out var node)) return node;
        node = new MagicTimingNode(field)
        {
            EditNotified = _ =>
            {
                OnPropertyChanged(nameof(IsDirty));
                OnPropertyChanged(nameof(StatusText));
            },
        };
        _timingNodes.Add(key, node);
        return node;
    }
    private MagicGroupNode ProgramTimingGroup(MagicProgram program, int index) => new($"P{index} · {Strings.U_Md_TimingGroup}",
        _document.TimingFields.Where(field => field.Kind != MagicTimingKind.KeyframeTime &&
                (field.OwnerOffset == program.ProgramAbs ||
                 field.Kind == MagicTimingKind.SectionDuration && field.OwnerOffset == program.SectionAbs))
            .Select(field => (MagicNode)GetTimingNode(field)).ToArray());

    private bool TryGetPendingTiming(out List<(MagicTimingField Field, uint Tick)> edits)
    {
        edits = new();
        foreach (var node in _timingNodes.Values.Where(n => n.IsDirty))
        {
            if (!node.IsValueEditable || !decimal.TryParse(node.Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out decimal frames) || frames < 0 ||
                frames != decimal.Truncate(frames) || frames > (decimal)node.MaxValue)
            {
                AppendLog(Strings.U_Md_TimingInvalid, MagicLogLevel.Error);
                return false;
            }
            edits.Add((node.Source, checked((uint)frames * 4096)));
        }
        return true;
    }
}

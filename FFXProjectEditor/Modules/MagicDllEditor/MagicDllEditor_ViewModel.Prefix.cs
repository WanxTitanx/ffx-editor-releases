using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MagicDllEditor;

internal partial class MagicDllEditor_ViewModel
{
    private readonly Dictionary<int, MagicPrefixNode> _prefixNodes = new();
    private Dictionary<int, MagicPrefixOperand> _prefixFields = new();
    private void AddPrefixOperand(List<MagicNode> fields, int record)
    {
        if (!_prefixFields.TryGetValue(record + 4, out var field)) return;
        if (!_prefixNodes.TryGetValue(field.DataOffset, out var node))
        {
            node = new MagicPrefixNode(field)
            {
                EditNotified = _ =>
                {
                    OnPropertyChanged(nameof(IsDirty));
                    OnPropertyChanged(nameof(StatusText));
                },
            };
            _prefixNodes.Add(field.DataOffset, node);
        }
        fields.RemoveAll(n => n is MagicFieldNode f && f.Offset == 4);
        fields.Insert(0, node);
    }

    private bool TryGetPendingPrefix(out List<(MagicPrefixOperand Field, uint Bits)> edits)
    {
        edits = new();
        foreach (var node in _prefixNodes.Values.Where(n => n.IsDirty))
        {
            if (!node.IsValueEditable)
            {
                AppendLog(Strings.U_Md_PrefixInvalid, MagicLogLevel.Error);
                return false;
            }
            uint bits;
            if (node.Source.Kind == MagicPrefixOperandKind.Float)
            {
                if (!float.TryParse(node.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ||
                    !float.IsFinite(value))
                {
                    AppendLog(Strings.U_Md_PrefixInvalid, MagicLogLevel.Error);
                    return false;
                }
                bits = BitConverter.SingleToUInt32Bits(value);
            }
            else if (!uint.TryParse(node.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out bits) ||
                !node.Source.ResourceChoices.Contains(bits))
            {
                AppendLog(Strings.U_Md_PrefixInvalid, MagicLogLevel.Error);
                return false;
            }
            edits.Add((node.Source, bits));
        }
        return true;
    }
}

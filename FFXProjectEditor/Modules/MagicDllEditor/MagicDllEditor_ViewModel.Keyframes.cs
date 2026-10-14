using System;
using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;

namespace FFXProjectEditor.Modules.MagicDllEditor;

internal partial class MagicDllEditor_ViewModel
{
    // Field ownership follows the document, not the currently filtered tree. A shared
    // physical operand has one edit buffer even if multiple programs refer to it.
    private readonly Dictionary<(int Record, int Offset, string Opcode, string Name), MagicFieldNode>
        _fieldNodes = new();
    private MagicDllFile? _fieldNodeDocument;

    private void ResetFieldCache(MagicDllFile? file)
    {
        if (ReferenceEquals(file, _fieldNodeDocument)) return;
        _fieldNodes.Clear();
        _timingNodes.Clear();
        _geometryNodes.Clear();
        _prefixNodes.Clear();
        _prefixFields = _document.PrefixOperands.ToDictionary(field => field.DataOffset);
        _timingFields = _document.TimingFields.ToDictionary(field => (field.Kind, field.DataOffset));
        _fieldNodeDocument = file;
    }

    private MagicFieldNode GetFieldNode(MagicField field, MagicFamilySchema? schema, int nativeWidth)
    {
        var key = (field.RecordOffset, field.Offset, field.OpcodeName ?? "", field.Name);
        if (_fieldNodes.TryGetValue(key, out var existing)) return existing;
        var node = new MagicFieldNode(field.Name, MapType(field.Type), field.Offset, field.Width,
            field.Semantics, $"{field.OpcodeName} — record @0x{field.RecordOffset:X}",
            initialValue: FormatValue(field),
            schemaAllowsWrite: (long)field.Offset + field.Width <= nativeWidth &&
                MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, field.Offset, field.Width))
        {
            SourceField = field,
            EditNotified = _ =>
            {
                OnPropertyChanged(nameof(IsDirty));
                OnPropertyChanged(nameof(StatusText));
            },
        };
        _fieldNodes.Add(key, node);
        return node;
    }

    private IReadOnlyList<MagicNode> BuildSlotFields(MagicSlot slot, MagicFamilySchema? schema)
    {
        if (slot.Keyframes.Count <= 1)
        {
            var fields = _document.ResolveAllFields(slot)
                .Select(field => (MagicNode)GetFieldNode(field, schema, slot.NativeRecordWidth)).ToList();
            AddKeyframeTime(fields, slot.RecordOffset);
            AddPrefixOperand(fields, slot.RecordOffset);
            return fields;
        }

        return slot.Keyframes.Select(frame =>
        {
            var fields = _document.ResolveKeyframeFields(slot, frame.Index)
                .Select(field => (MagicNode)GetFieldNode(field, schema, slot.NativeRecordWidth)).ToList();
            AddKeyframeTime(fields, frame.RecordOffset);
            AddPrefixOperand(fields, frame.RecordOffset);
            return (MagicNode)new MagicKeyframeNode(frame, fields);
        }).ToArray();
    }
}

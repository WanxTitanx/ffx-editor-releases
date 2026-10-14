using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MagicDllEditor;

internal partial class MagicDllEditor_ViewModel
{
    private readonly Dictionary<int, MagicGeometryNode> _geometryNodes = new();

    private MagicGeometryNode GetGeometryNode(MagicGeometryField field)
    {
        if (_geometryNodes.TryGetValue(field.DataOffset, out var node)) return node;
        node = new MagicGeometryNode(field)
        {
            EditNotified = _ =>
            {
                OnPropertyChanged(nameof(IsDirty));
                OnPropertyChanged(nameof(StatusText));
            },
        };
        _geometryNodes.Add(field.DataOffset, node);
        return node;
    }

    private void AddGeometryPoints(List<MagicNode> fields, MagicDllRoot root, MagicDescriptor descriptor)
    {
        var resource = _document.GeometryResources.FirstOrDefault(r =>
            r.RootOffset == root.RootAbs && r.DescriptorIndex == descriptor.Index);
        if (resource == null) return;
        if (resource.Error != null)
        {
            fields.Add(new MagicGeometryNoticeNode());
            return;
        }
        // Pages keep navigation usable for meshes with thousands of points.
        var points = resource.Points.Select(p => (MagicNode)new MagicGroupNode(
            string.Format(CultureInfo.CurrentCulture, Strings.U_Md_GeometryPoint, p.Index),
            p.Components.Select(f => (MagicNode)GetGeometryNode(f)).ToArray())).ToArray();
        if (points.Length == 0) return;
        var pages = points.Chunk(64).Select((page, index) => (MagicNode)new MagicGroupNode(
            $"{index * 64}–{index * 64 + page.Length - 1}", page)).ToArray();
        fields.Insert(0, new MagicGroupNode(Strings.U_Md_GeometryPoints,
            points.Length <= 64 ? points : pages));
    }

    private bool TryGetPendingGeometry(out List<(MagicGeometryField Field, short Value)> edits)
    {
        edits = new();
        foreach (var node in _geometryNodes.Values.Where(n => n.IsDirty))
        {
            if (!short.TryParse(node.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out short value))
            {
                AppendLog(Strings.U_Md_GeometryInvalid, MagicLogLevel.Error);
                return false;
            }
            edits.Add((node.Source, value));
        }
        return true;
    }
}

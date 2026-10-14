using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.TreasureMap;
using Xunit;
using Xunit.Abstractions;

namespace FFXProjectEditor.Tests.Core;

public class TreasureCoordMapTests
{
    private readonly ITestOutputHelper _out;
    public TreasureCoordMapTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Map_ObjectCoords_To_GuideSpace()
    {
        string mapPath = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\map\bika\bika02\bin\mapout.vpa";
        if (!File.Exists(mapPath)) return;

        var archive = Map1Archive.Read(mapPath);
        var guide = GuideMapGeometry.Read(archive);
        var sb = new System.Text.StringBuilder();

        // Range dos vértices do guide map (int16)
        foreach (var m in guide.Models)
        {
            var xs = m.Vertices.Select(v => v.X).ToList();
            var zs = m.Vertices.Select(v => v.Z).ToList();
            sb.AppendLine($"model {m.SceneIndex}: verts={m.Vertices.Count} X[{xs.Min():F1}..{xs.Max():F1}] Z[{zs.Min():F1}..{zs.Max():F1}] bounds=({m.BoundsMin.X:F1},{m.BoundsMin.Z:F1})..({m.BoundsMax.X:F1},{m.BoundsMax.Z:F1}) scale={m.LocalTransform.M11:F3}");
        }

        // Testa a conversão obj -> guide
        var objs = MapObjectExtractor.ExtractFromObjectTable(mapPath);
        var chestObjs = objs.Where(o => o.ModelId >= 0x5000 && o.ModelId <= 0x50C7).ToList();
        sb.AppendLine($"\n=== CONVERSÃO obj(0-65535) -> guide ===");
        foreach (var o in chestObjs.Take(12))
        {
            float gx1 = (float)o.X / 65535f * 200f - 100f;
            float gz1 = (float)o.Z / 65535f * 200f - 100f;
            // alternativa: usa o scale do guide map
            float scale = guide.Models[0].LocalTransform.M11;
            float gx2 = o.X * scale / 10f;
            float gz2 = o.Z * scale / 10f;
            sb.AppendLine($"  slot={o.ModelId - 0x5000} obj=({o.X},{o.Z}) -> norm=({gx1:F1},{gz1:F1}) scale=({gx2:F1},{gz2:F1})");
        }

        File.WriteAllText(@"C:\Users\wande\Documents\ffx-editor-main\work\_coord_map.txt", sb.ToString());
        _out.WriteLine(sb.ToString());
    }
}
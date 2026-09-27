using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.TreasureMap;
using Xunit;
using Xunit.Abstractions;

namespace FFXProjectEditor.Tests.Core;

public class TreasureProjectTests
{
    private readonly ITestOutputHelper _out;
    public TreasureProjectTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Project_MapObjects_ToGuideSpace()
    {
        string mapPath = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\map\bika\bika02\bin\mapout.vpa";
        if (!File.Exists(mapPath)) return;

        var archive = Map1Archive.Read(mapPath);
        var guide = GuideMapGeometry.Read(archive);
        var m = guide.Models[0];
        var sb = new System.Text.StringBuilder();

        // Bounds do guide map
        float minX = m.BoundsMin.X, maxX = m.BoundsMax.X;
        float minZ = m.BoundsMin.Z, maxZ = m.BoundsMax.Z;
        sb.AppendLine($"Guide bounds: X[{minX:F1}..{maxX:F1}] Z[{minZ:F1}..{maxZ:F1}]");

        var objs = MapObjectExtractor.ExtractFromObjectTable(mapPath);
        var chestObjs = objs.Where(o => o.ModelId >= 0x5000 && o.ModelId <= 0x50C7).ToList();

        // Normalização por eixo: obj(0-65535) -> guide bounds
        sb.AppendLine($"\n=== PROJEÇÃO (normalização por eixo) ===");
        foreach (var o in chestObjs.Take(20))
        {
            float gx = (float)o.X / 65535f * (maxX - minX) + minX;
            float gz = (float)o.Z / 65535f * (maxZ - minZ) + minZ;
            sb.AppendLine($"  slot={o.ModelId - 0x5000} obj=({o.X},{o.Z}) -> guide=({gx:F1},{gz:F1})");
        }

        // Distribuição
        var gxs = chestObjs.Select(o => (float)o.X / 65535f * (maxX - minX) + minX).ToList();
        var gzs = chestObjs.Select(o => (float)o.Z / 65535f * (maxZ - minZ) + minZ).ToList();
        sb.AppendLine($"\nDistribuição guide X: [{gxs.Min():F1}..{gxs.Max():F1}] Z: [{gzs.Min():F1}..{gzs.Max():F1}]");

        File.WriteAllText(@"C:\Users\wande\Documents\ffx-editor-main\work\_project.txt", sb.ToString());
        _out.WriteLine(sb.ToString());
    }
}
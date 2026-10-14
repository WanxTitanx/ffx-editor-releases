using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.TreasureMap;
using Xunit;
using Xunit.Abstractions;

namespace FFXProjectEditor.Tests.Core;

public class TreasureMapObjectTests
{
    private readonly ITestOutputHelper _out;
    public TreasureMapObjectTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Extract_And_Project_MapObjects()
    {
        string mapPath = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\map\bika\bika02\bin\mapout.vpa";
        if (!File.Exists(mapPath)) return;

        var archive = Map1Archive.Read(mapPath);
        var guide = GuideMapGeometry.Read(archive);
        var objs = MapObjectExtractor.ExtractFromObjectTable(mapPath);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Objetos extraídos: {objs.Count}");
        sb.AppendLine($"Na faixa de baú (0x5000-0x50C7): {objs.Count(o => o.IsChestRange)}");
        sb.AppendLine($"Guide models: {guide.Models.Count}");

        var m0 = guide.Models[0];
        sb.AppendLine($"Model 0 bounds: X[{m0.BoundsMin.X:F1}..{m0.BoundsMax.X:F1}] Z[{m0.BoundsMin.Z:F1}..{m0.BoundsMax.Z:F1}]");

        sb.AppendLine("\n=== Objetos na faixa de baú (projetados) ===");
        foreach (var o in objs.Where(o => o.IsChestRange).Take(15))
        {
            var (gx, gz) = MapObjectExtractor.ProjectToGuide(o, m0);
            bool inBounds = gx >= m0.BoundsMin.X && gx <= m0.BoundsMax.X && gz >= m0.BoundsMin.Z && gz <= m0.BoundsMax.Z;
            sb.AppendLine($"  slot={o.Slot} obj=({o.X},{o.Z}) guide=({gx:F1},{gz:F1}) {(inBounds ? "OK" : "FORA")}");
        }

        File.WriteAllText(@"C:\Users\wande\Documents\ffx-editor-main\work\_mapobj_test.txt", sb.ToString());
        _out.WriteLine(sb.ToString());
    }
}
using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.TreasureMap;
using Xunit;
using Xunit.Abstractions;

namespace FFXProjectEditor.Tests.Core;

public class TreasureCoordValidateTests
{
    private readonly ITestOutputHelper _out;
    public TreasureCoordValidateTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Validate_MapObjectCoords_Against_GuideMapBounds()
    {
        string mapPath = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\map\bika\bika02\bin\mapout.vpa";
        if (!File.Exists(mapPath)) return;

        var archive = Map1Archive.Read(mapPath);
        var guide = GuideMapGeometry.Read(archive);
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"GuideMap models: {guide.Models.Count}");
        foreach (var m in guide.Models)
            sb.AppendLine($"  model {m.SceneIndex}: bounds=({m.BoundsMin.X:F1},{m.BoundsMin.Z:F1})..({m.BoundsMax.X:F1},{m.BoundsMax.Z:F1}) scale={m.LocalTransform.M11:F2}");

        var objs = MapObjectExtractor.ExtractFromObjectTable(mapPath);
        var chestObjs = objs.Where(o => o.ModelId >= 0x5000 && o.ModelId <= 0x50C7).ToList();
        sb.AppendLine($"\nObjetos baú: {chestObjs.Count}");
        foreach (var o in chestObjs.Take(15))
            sb.AppendLine($"  slot={o.ModelId - 0x5000} x={o.X} z={o.Z}");

        File.WriteAllText(@"C:\Users\wande\Documents\ffx-editor-main\work\_coord_validate.txt", sb.ToString());
        _out.WriteLine(sb.ToString());
    }
}
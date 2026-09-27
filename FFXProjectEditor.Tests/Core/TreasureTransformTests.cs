using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.TreasureMap;
using Xunit;
using Xunit.Abstractions;

namespace FFXProjectEditor.Tests.Core;

public class TreasureTransformTests
{
    private readonly ITestOutputHelper _out;
    public TreasureTransformTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Dump_Transform_And_Test_Conversions()
    {
        string mapPath = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\map\bika\bika02\bin\mapout.vpa";
        if (!File.Exists(mapPath)) return;

        var archive = Map1Archive.Read(mapPath);
        var guide = GuideMapGeometry.Read(archive);
        var sb = new System.Text.StringBuilder();

        foreach (var m in guide.Models)
        {
            var t = m.LocalTransform;
            sb.AppendLine($"model {m.SceneIndex}: M11={t.M11:F4} M22={t.M22:F4} M33={t.M33:F4} M41={t.M41:F2} M42={t.M42:F2} M43={t.M43:F2}");
            sb.AppendLine($"  bounds=({m.BoundsMin.X:F1},{m.BoundsMin.Z:F1})..({m.BoundsMax.X:F1},{m.BoundsMax.Z:F1})");
        }

        // Testa conversões para os objetos
        var objs = MapObjectExtractor.ExtractFromObjectTable(mapPath);
        var chestObjs = objs.Where(o => o.ModelId >= 0x5000 && o.ModelId <= 0x50C7).ToList();
        sb.AppendLine($"\n=== CONVERSÕES (slot 154: obj=(4870,677)) ===");
        var m0 = guide.Models[0];
        float scale = m0.LocalTransform.M11;
        sb.AppendLine($"scale={scale:F4}");
        sb.AppendLine($"  a) obj*scale/10: x={4870*scale/10:F1} z={677*scale/10:F1}");
        sb.AppendLine($"  b) obj*scale: x={4870*scale:F1} z={677*scale:F1}");
        sb.AppendLine($"  c) obj/10: x={4870/10f:F1} z={677/10f:F1}");
        sb.AppendLine($"  d) norm 0-65535->bounds: x={(float)4870/65535f*(72.3f-(-89.1f))+(-89.1f):F1} z={(float)677/65535f*(98f-(-100f))+(-100f):F1}");

        // Vértices do guide map (world) para referência
        sb.AppendLine($"\nVértices guide (amostra):");
        foreach (var v in m0.Vertices.Take(10))
            sb.AppendLine($"  ({v.X:F1},{v.Z:F1})");

        File.WriteAllText(@"C:\Users\wande\Documents\ffx-editor-main\work\_transform.txt", sb.ToString());
        _out.WriteLine(sb.ToString());
    }
}
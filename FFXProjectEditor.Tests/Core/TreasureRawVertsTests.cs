using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.TreasureMap;
using Xunit;
using Xunit.Abstractions;

namespace FFXProjectEditor.Tests.Core;

public class TreasureRawVertsTests
{
    private readonly ITestOutputHelper _out;
    public TreasureRawVertsTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Dump_RawVerts_And_Objects()
    {
        string mapPath = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\map\bika\bika02\bin\mapout.vpa";
        if (!File.Exists(mapPath)) return;

        var archive = Map1Archive.Read(mapPath);
        var guide = GuideMapGeometry.Read(archive);
        var sb = new System.Text.StringBuilder();

        // Vértices do guide map (world coords). Reconstruir int16: int16 = world / (scale/10)
        foreach (var m in guide.Models)
        {
            float scale = m.LocalTransform.M11;
            var rawXs = m.Vertices.Select(v => (int)Math.Round(v.X / scale * 10f)).ToList();
            var rawZs = m.Vertices.Select(v => (int)Math.Round(v.Z / scale * 10f)).ToList();
            sb.AppendLine($"model {m.SceneIndex}: raw int16 X[{rawXs.Min()}..{rawXs.Max()}] Z[{rawZs.Min()}..{rawZs.Max()}]");
        }

        var objs = MapObjectExtractor.ExtractFromObjectTable(mapPath);
        var chestObjs = objs.Where(o => o.ModelId >= 0x5000 && o.ModelId <= 0x50C7).ToList();
        sb.AppendLine($"\nObjetos baú (signed int16):");
        foreach (var o in chestObjs.Take(15))
        {
            int sx = o.X > 32767 ? o.X - 65536 : o.X;
            int sz = o.Z > 32767 ? o.Z - 65536 : o.Z;
            sb.AppendLine($"  slot={o.ModelId - 0x5000} signed=({sx},{sz})");
        }

        File.WriteAllText(@"C:\Users\wande\Documents\ffx-editor-main\work\_raw_verts.txt", sb.ToString());
        _out.WriteLine(sb.ToString());
    }
}
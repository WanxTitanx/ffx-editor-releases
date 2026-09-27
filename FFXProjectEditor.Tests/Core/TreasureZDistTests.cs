using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.TreasureMap;
using Xunit;
using Xunit.Abstractions;

namespace FFXProjectEditor.Tests.Core;

public class TreasureZDistTests
{
    private readonly ITestOutputHelper _out;
    public TreasureZDistTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Z_Distribution_AcrossFields()
    {
        string master = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master";
        if (!Directory.Exists(master)) return;

        var index = TreasureMapIndexBuilder.Build(master);
        var sb = new System.Text.StringBuilder();

        foreach (var field in index.Fields.Take(25))
        {
            var objs = MapObjectExtractor.ExtractFromObjectTable(field.MapPath);
            var chestObjs = objs.Where(o => o.ModelId >= 0x5000 && o.ModelId <= 0x50C7).ToList();
            if (chestObjs.Count == 0) continue;
            var xs = chestObjs.Select(o => o.X).ToList();
            var zs = chestObjs.Select(o => o.Z).ToList();
            sb.AppendLine($"{field.FieldId}: {chestObjs.Count} objs X[{xs.Min()}..{xs.Max()}] Z[{zs.Min()}..{zs.Max()}]");
        }

        File.WriteAllText(@"C:\Users\wande\Documents\ffx-editor-main\work\_z_dist.txt", sb.ToString());
        _out.WriteLine(sb.ToString());
    }
}
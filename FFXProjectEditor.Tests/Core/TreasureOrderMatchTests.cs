using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.TreasureMap;
using Xunit;
using Xunit.Abstractions;

namespace FFXProjectEditor.Tests.Core;

public class TreasureOrderMatchTests
{
    private readonly ITestOutputHelper _out;
    public TreasureOrderMatchTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Match_MapObjects_To_TreasureWorkers_ByOrder()
    {
        string master = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master";
        if (!Directory.Exists(master)) return;

        var index = TreasureMapIndexBuilder.Build(master);
        var sb = new System.Text.StringBuilder();

        // Para cada campo com candidatos, extrai objetos do mapa e workers
        foreach (var field in index.Fields)
        {
            var candidates = index.ConfirmedChestCandidates.Where(c => c.FieldId == field.FieldId).ToList();
            if (candidates.Count == 0) continue;

            var objs = MapObjectExtractor.ExtractFromObjectTable(field.MapPath);
            var chestObjs = objs.Where(o => o.ModelId >= 0x5000 && o.ModelId <= 0x50C7).ToList();

            sb.AppendLine($"\n=== {field.FieldId}: {candidates.Count} workers, {chestObjs.Count} objetos baú ===");
            sb.AppendLine($"  Workers (treasure ids): {string.Join(", ", candidates.Select(c => string.Join("+", c.TreasureIds)))}");
            sb.AppendLine($"  Objetos baú (slot: x,z): {string.Join(", ", chestObjs.Select(o => $"{o.ModelId - 0x5000}:{o.X},{o.Z}"))}");

            // Match por ordem
            int n = Math.Min(candidates.Count, chestObjs.Count);
            sb.AppendLine($"  MATCH por ordem (primeiros {n}):");
            for (int i = 0; i < n; i++)
            {
                var c = candidates[i];
                var o = chestObjs[i];
                sb.AppendLine($"    worker {i}: treasure={string.Join("+", c.TreasureIds)} -> slot={o.ModelId - 0x5000} pos=({o.X},{o.Z})");
            }
        }

        File.WriteAllText(@"C:\Users\wande\Documents\ffx-editor-main\work\_bika_match.txt", sb.ToString());
        _out.WriteLine(sb.ToString());
    }
}
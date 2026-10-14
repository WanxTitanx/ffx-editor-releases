using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.TreasureMap;
using Xunit;
using Xunit.Abstractions;

namespace FFXProjectEditor.Tests.Core;

// ── TreasureMapCoverageTests ───────────────────────────────────────────────────────────
// Mede a cobertura de posição de baús usando os OBJETOS DO MAPA (MAP1 section 2).
// O mapa tem TODOS os objetos de campo com posição (model_id 0x5000+slot -> x,z).
// Compara com a cobertura atual (event scanner: 9/338 = 2.7%).
// ──────────────────────────────────────────────────────────────────────────────────────
public class TreasureMapCoverageTests
{
    private readonly ITestOutputHelper _out;
    public TreasureMapCoverageTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void MapObjects_Coverage_AllFields()
    {
        string master = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master";
        if (!Directory.Exists(master)) return;

        var index = TreasureMapIndexBuilder.Build(master);
        var sb = new System.Text.StringBuilder();

        int totalChestObjs = 0;
        int fieldsWithChests = 0;
        var perField = new List<(string FieldId, int ChestObjs, int Workers)>();

        foreach (var field in index.Fields)
        {
            var objs = MapObjectExtractor.ExtractFromObjectTable(field.MapPath);
            var chestObjs = objs.Where(o => o.ModelId >= 0x5000 && o.ModelId <= 0x50C7).ToList();
            int workers = index.ConfirmedChestCandidates.Count(c => c.FieldId == field.FieldId);
            if (chestObjs.Count > 0)
            {
                totalChestObjs += chestObjs.Count;
                fieldsWithChests++;
                perField.Add((field.FieldId, chestObjs.Count, workers));
            }
        }

        sb.AppendLine($"=== COBERTURA VIA MAPA (seção 2) ===");
        sb.AppendLine($"Campos com objetos baú: {fieldsWithChests}");
        sb.AppendLine($"Total objetos baú (0x5000-0x50C7): {totalChestObjs}");
        sb.AppendLine($"Candidatos de evento (obtainTreasure): {index.ConfirmedChestCandidates.Count}");
        sb.AppendLine($"Com posição ATEL (atual): {index.ConfirmedChestCandidates.Count(c => c.Positions.Count > 0)}");

        sb.AppendLine($"\n=== POR CAMPO (objetos baú vs workers) ===");
        foreach (var (fid, chests, workers) in perField.OrderByDescending(p => p.Item2).Take(20))
            sb.AppendLine($"  {fid}: {chests} objetos baú, {workers} workers");

        File.WriteAllText(@"C:\Users\wande\Documents\ffx-editor-main\work\_map_coverage.txt", sb.ToString());
        _out.WriteLine(sb.ToString());
    }
}
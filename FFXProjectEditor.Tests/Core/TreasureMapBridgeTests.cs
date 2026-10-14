using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.TreasureMap;
using Xunit;
using Xunit.Abstractions;

namespace FFXProjectEditor.Tests.Core;

// ── TreasureMapBridgeTests ─────────────────────────────────────────────────────────────
// Cruza os candidatos de baú (event scanner) com os objetos do mapa (MAP1 section 2).
// O mapa tem TODOS os objetos de campo com posição (model_id 0x5000+slot -> x,z).
// O scanner captura o model_id via setModelResourceId. A ponte model_id -> posição
// resolve a posição real de quase todos os baús (vs 2.7% atual).
// ──────────────────────────────────────────────────────────────────────────────────────
public class TreasureMapBridgeTests
{
    private readonly ITestOutputHelper _out;
    public TreasureMapBridgeTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Bridge_MapObjects_ResolveChestPositions()
    {
        string master = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master";
        if (!Directory.Exists(master)) return;

        var index = TreasureMapIndexBuilder.Build(master);
        var locIndex = ChestLocationIndexBuilder.Build(index);

        int total = index.ConfirmedChestCandidates.Count;
        int withModelId = index.ConfirmedChestCandidates.Count(c => c.ModelIds.Count > 0);
        int withPos = locIndex.Locations.Count(l => l.GuideX.HasValue);

        _out.WriteLine($"=== BRIDGE MAPA x EVENTO ===");
        _out.WriteLine($"Candidatos confirmados: {total}");
        _out.WriteLine($"Com model_id (setModelResourceId): {withModelId}");
        _out.WriteLine($"Com posição (ATEL constants): {withPos}");

        // Para cada candidato com model_id, tenta achar a posição no mapa
        int resolvedByMap = 0;
        var resolved = new List<string>();
        foreach (var c in index.ConfirmedChestCandidates)
        {
            if (c.ModelIds.Count == 0) continue;
            var field = index.Fields.FirstOrDefault(f => f.FieldId == c.FieldId);
            if (field == null) continue;
            var objs = MapObjectExtractor.ExtractFromObjectTable(field.MapPath);
            foreach (int mid in c.ModelIds)
            {
                var match = objs.FirstOrDefault(o => o.ModelId == mid);
                if (match != null)
                {
                    resolvedByMap++;
                    resolved.Add($"{c.FieldId} #{string.Join(",", c.TreasureIds)} model={mid:X4} x={match.X} z={match.Z}");
                    break;
                }
            }
        }

        _out.WriteLine($"Resolvidos via MAPA (model_id -> posição): {resolvedByMap} ({Pct(resolvedByMap, total)})");
        _out.WriteLine("\n=== AMOSTRA RESOLVIDOS ===");
        foreach (var r in resolved.Take(15))
            _out.WriteLine($"  {r}");
    }

    private static string Pct(int n, int total) => total == 0 ? "0%" : $"{100.0 * n / total:F1}%";
}

// MapObjectExtractor agora vive no FfxLib (FFXProjectEditor/FfxLib/TreasureMap/MapObjectExtractor.cs).
// Este teste usa o do FfxLib (ExtractFromObjectTable/ProjectToGuide).
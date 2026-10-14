using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.TreasureMap;
using Xunit;
using Xunit.Abstractions;

namespace FFXProjectEditor.Tests.Core;

// ── TreasureCoverageMeasureTests ──────────────────────────────────────────────────────
// Mede a cobertura de posição dos baús: quantos candidatos confirmados têm posição real
// (GuideX/GuideZ) vs placeholder (Unresolved). Temporário para a lane TreasureEditor.
// ──────────────────────────────────────────────────────────────────────────────────────
public class TreasureCoverageMeasureTests
{
    private readonly ITestOutputHelper _out;
    public TreasureCoverageMeasureTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Measure_PositionCoverage()
    {
        string master = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master";
        if (!Directory.Exists(master)) return;

        var index = TreasureMapIndexBuilder.Build(master);
        var locIndex = ChestLocationIndexBuilder.Build(index);

        int total = index.ConfirmedChestCandidates.Count;
        int withPos = locIndex.Locations.Count(l => l.GuideX.HasValue);
        int unresolved = locIndex.Locations.Count(l => !l.GuideX.HasValue);
        int exact = locIndex.Locations.Count(l => l.Confidence == ChestLocationConfidence.Exact);
        int conditional = locIndex.Locations.Count(l => l.Confidence == ChestLocationConfidence.Conditional);

        _out.WriteLine($"=== COBERTURA DE POSIÇÃO ===");
        _out.WriteLine($"Candidatos confirmados (obtainTreasure): {total}");
        _out.WriteLine($"Com posição real (GuideX/Z): {withPos} ({Pct(withPos, total)})");
        _out.WriteLine($"Unresolved (placeholder): {unresolved} ({Pct(unresolved, total)})");
        _out.WriteLine($"Exact: {exact} | Conditional: {conditional}");

        // Top fields com mais baús
        _out.WriteLine("\n=== TOP 10 FIELDS POR BAÚS ===");
        foreach (var g in locIndex.Locations.GroupBy(l => l.FieldId)
                     .OrderByDescending(g => g.Count()).Take(10))
        {
            int gp = g.Count(l => l.GuideX.HasValue);
            _out.WriteLine($"{g.Key}: {g.Count()} baús ({gp} com posição)");
        }
    }

    private static string Pct(int n, int total) => total == 0 ? "0%" : $"{100.0 * n / total:F1}%";
}
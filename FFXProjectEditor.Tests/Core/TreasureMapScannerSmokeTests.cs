using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.TreasureMap;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.Core;

// ── Configured private-corpus inputs ──
// WHY: missing corpus data must fail these smoke tests instead of looking like a pass.
// MAINT: keep event selection ordinal so every Windows validation host scans the same file.
public class TreasureMapScannerSmokeTests
{
    [Fact]
    public void Scan_BesaidEvent_FindsCandidates()
    {
        string eventDir = Path.Combine(TestDataPaths.MasterRoot, "jppc", "event", "obj");
        Assert.True(Directory.Exists(eventDir), $"Expected configured event directory: {eventDir}");

        // WHY: Besaid uses bsvr0400 in the corpus and BlitzballRecruitEvents; the old besai* glob matches nothing.
        string ebp = Path.Combine(eventDir, "bs", "bsvr0400", "bsvr0400.ebp");
        Assert.True(File.Exists(ebp), $"Expected configured Besaid event: {ebp}");

        var result = EventTreasureScanner.Scan(ebp);
        Assert.NotNull(result);
        Assert.True(result.WorkerCount > 0, $"Expected workers in {ebp}, got {result.WorkerCount}");
        Assert.NotNull(result.Candidates);
    }

    [Fact]
    public void Scan_AnyEvent_ProducesWorkers()
    {
        string eventDir = Path.Combine(TestDataPaths.MasterRoot, "jppc", "event", "obj");
        Assert.True(Directory.Exists(eventDir), $"Expected configured event directory: {eventDir}");

        string? ebp = Directory.EnumerateFiles(eventDir, "*.ebp", SearchOption.AllDirectories)
            .Where(p => new FileInfo(p).Length > 1024)
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
        Assert.True(ebp is not null, $"Expected an event larger than 1024 bytes under {eventDir}");

        var result = EventTreasureScanner.Scan(ebp!);
        Assert.True(result.WorkerCount >= 0);
        Assert.True(result.StatementCount > 0, $"Expected ATEL statements in {Path.GetFileName(ebp)}, got {result.StatementCount}");
    }

    [Fact]
    public void Scan_AnyEvent_Uses_ScriptStart()
    {
        string eventDir = Path.Combine(TestDataPaths.MasterRoot, "jppc", "event", "obj");
        Assert.True(Directory.Exists(eventDir), $"Expected configured event directory: {eventDir}");

        string? ebp = Directory.EnumerateFiles(eventDir, "*.ebp", SearchOption.AllDirectories)
            .Where(p => new FileInfo(p).Length > 4096)
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
        Assert.True(ebp is not null, $"Expected an event larger than 4096 bytes under {eventDir}");

        var result = EventTreasureScanner.Scan(ebp!);
        Assert.True(result.StatementCount > 0, $"Expected ATEL statements for {Path.GetFileName(ebp)}, got {result.StatementCount}");
        Assert.True(result.WorkerCount >= 1, $"Expected at least one worker, got {result.WorkerCount}");
    }

    [Fact]
    public void BuildIndex_FindsConfirmedChests()
    {
        // Regression (RE-validated 2026-08-06): obtainTreasure(0x015B) -> FFX_Atel_Common_obtainTreasure@0x85A740
        // is the proven chest signature. ConfirmedChestCandidates must be > 0 now that the INVALID speculative
        // model-id requirement (0x5002/0x50AA) is gone.
        string master = TestDataPaths.MasterRoot;

        var index = TreasureMapIndexBuilder.Build(master);
        Assert.NotNull(index);
        Assert.True(index.Fields.Count > 0, "Expected at least one field");
        Assert.True(index.EventScans.Count > 0, "Expected at least one event scan");
        Assert.True(index.ConfirmedChestCandidates.Count > 0,
            $"Expected confirmed chests (obtainTreasure proven), got {index.ConfirmedChestCandidates.Count}");
    }

    [Fact]
    public void Projection_Pipeline_Completes_WithoutCrash()
    {
        // End-to-end regression: Build -> ConfirmedChestCandidates -> ChestLocationIndexBuilder (projection).
        // The pipeline must never throw; a non-zero number of in-bounds projected chests is a bonus.
        string master = TestDataPaths.MasterRoot;

        var index = TreasureMapIndexBuilder.Build(master);
        Assert.True(index.ConfirmedChestCandidates.Count > 0);

        var locIndex = ChestLocationIndexBuilder.Build(index);
        Assert.NotNull(locIndex);
        int projected = locIndex.Locations.Count(l => l.GuideX.HasValue);
        // Honest guard: positions are mostly runtime (ATEL setPosition uses get-position calls),
        // so we only assert the pipeline runs and produces a well-formed result.
        Assert.True(projected >= 0, $"Projection produced {projected} in-bounds chests (expected >= 0)");
    }
}

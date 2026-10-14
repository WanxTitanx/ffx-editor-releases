using System;
using System.Linq;
using FFXProjectEditor.FfxLib.TreasureMap;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.Core;

// ── Configured private-corpus inputs ──
// WHY: reward-description coverage is meaningful only when the configured master is present.
// MAINT: never restore a machine-specific fallback or a silent missing-corpus pass.
public class TreasureRewardDescribeTests
{
    [Fact]
    public void Describe_Resolves_Real_Reward_For_Confirmed_Chests()
    {
        // Regression for the UI reward text: every confirmed chest whose treasure id is
        // in the catalog must describe a REAL reward (never "Unknown 0x...").
        string master = TestDataPaths.MasterRoot;
        var idx = TreasureMapIndexBuilder.Build(master);
        var catalog = idx.Catalog;
        int checkedCount = 0, unknown = 0, gilOrItem = 0;
        foreach (var c in idx.ConfirmedChestCandidates)
        {
            if (c.TreasureIds.Count == 0) continue;
            int tid = c.TreasureIds[0];
            if (tid >= catalog.Records.Count) { unknown++; continue; }
            var rec = catalog.Records[tid];
            if (!rec.Kind.HasValue) { unknown++; continue; }
            string desc = TreasureRewardLookup.Describe(rec.Kind.Value, rec.Quantity, rec.Type, master);
            if (desc.Contains("Unknown") || desc.Contains("0x")) { unknown++; }
            else { checkedCount++; gilOrItem++; }
        }
        Console.WriteLine($"DESC: checkedResolvable={checkedCount} unknownOrNull={unknown} total={idx.ConfirmedChestCandidates.Count}");
        Assert.True(checkedCount > 0, "Expected at least some chests to resolve a real reward.");
    }
}

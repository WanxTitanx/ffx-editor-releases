using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.TreasureMap;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;
namespace FFXProjectEditor.Tests.Core;
// ── Configured private-corpus inputs ──
// WHY: these regressions must fail when the configured master or takara.bin is unavailable.
// MAINT: the writer test must continue to save only a unique temporary copy of takara.bin.
public class RewardSelectionAndEditTests
{
    [Fact]
    public void Most_Chest_Rewards_Resolve_To_Picker()
    {
        // Regression for the UI fix: the reward combo was empty ("hidden") because
        // SelectedReward was never initialized. Now it resolves by EncodedId==Type for
        // the vast majority of confirmed chests (Gil resolves via Type==0).
        string master=TestDataPaths.MasterRoot;
        var idx=TreasureMapIndexBuilder.Build(master); var cat=idx.Catalog;
        int total=0, resolvable=0;
        foreach(var c in idx.ConfirmedChestCandidates){
            if(c.TreasureIds.Count==0) continue;
            int tid=c.TreasureIds[0]; if(tid>=cat.Records.Count) continue;
            var rec=cat.Records[tid]; if(!rec.Kind.HasValue) continue;
            total++;
            var opts=TreasureRewardLookup.Build(rec.Kind.Value, master);
            if(opts.Any(o=>o.EncodedId==rec.Type)) resolvable++;
        }
        Assert.True(resolvable > total * 0.8, $"Only {resolvable}/{total} rewards resolve to the picker.");
    }

    [Fact]
    public void Edit_Record_RoundTrips_Through_Writer()
    {
        // RT0: an edited treasure record must persist byte-exactly through
        // TreasureCatalogWriter + SaveTransaction and read back with the new fields.
        string src=Path.Combine(TestDataPaths.MasterRoot, "jppc", "battle", "kernel", "takara.bin");
        Assert.True(File.Exists(src), $"Expected configured treasure catalog: {src}");
        string tmp=Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", $"takara_rt_{Guid.NewGuid():N}.bin");
        try{
            File.Copy(src, tmp);
            var cat=TreasureCatalog.Read(tmp);
            var rec=cat.Records.FirstOrDefault(r=>r.Kind.HasValue && r.Kind.Value!=TreasureKind.Gil && r.Type!=0) ?? cat.Records.First(r=>r.Kind.HasValue);
            var edited=rec with { Quantity=(byte)(rec.Quantity==5?7:5) };
            byte[] bytes=TreasureCatalogWriter.Write(cat, cat.Records.Select(r=>r.Id==edited.Id?edited:r));
            TreasureCatalogSaveTransaction.Save(cat, bytes);
            var reread=TreasureCatalog.Read(tmp);
            var rr=reread.Records[edited.Id];
            Assert.Equal(edited.Quantity, rr.Quantity);
            Assert.Equal(edited.RawKind, rr.RawKind);
            Assert.Equal(edited.Type, rr.Type);
            Console.WriteLine($"RT0 EDIT: id={edited.Id} qty {rec.Quantity}->{rr.Quantity} kind 0x{rr.RawKind:X2} type 0x{rr.Type:X4}");
        } finally { try{File.Delete(tmp);}catch{} }
    }
}

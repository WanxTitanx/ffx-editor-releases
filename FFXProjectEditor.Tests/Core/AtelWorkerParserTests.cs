using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.TreasureMap;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;
namespace FFXProjectEditor.Tests.Core;
public class AtelWorkerParserTests
{
    [Fact]
    public void Gain_FloatConstPositions()
    {
        string master=TestDataPaths.MasterRoot;
        var idx=TreasureMapIndexBuilder.Build(master);
        var conf=idx.ConfirmedChestCandidates;
        int withPos=conf.Count(c=>c.Positions.Count>0);
        int totalPos=conf.Sum(c=>c.Positions.Count);
        var loc=ChestLocationIndexBuilder.Build(idx);
        int proj=loc.Locations.Count(l=>l.GuideX.HasValue);
        var withPosF=conf.Where(c=>c.Positions.Count>0).Select(c=>c.FieldId).Distinct().ToArray();
        File.WriteAllText(TestDataPaths.ReportPath("gain-result.txt"),
            $"confirmed={conf.Count} withPos={withPos} totalPos={totalPos} projected={proj} fieldsWithPos=[{string.Join(",",withPosF)}] fieldsProj={loc.Locations.Where(l=>l.GuideX.HasValue).Select(l=>l.FieldId).Distinct().Count()}");
        Assert.NotEmpty(conf);
        Assert.True(withPos > 0, $"No confirmed chest positions found (confirmed={conf.Count}, totalPos={totalPos}, projected={proj}).");
    }
}

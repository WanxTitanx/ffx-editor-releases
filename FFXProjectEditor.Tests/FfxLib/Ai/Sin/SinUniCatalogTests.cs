using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Ai.Sin;
using Xunit;

namespace FFXProjectEditor.Tests.FfxLib.Ai.Sin;

public sealed class SinUniCatalogTests
{
    static IEnumerable<AiSinPresetEntry> Drafts => BikanelSinPrototypeCatalog.All
        .Concat(CalmLandsSinPrototypeCatalog.All).Concat(StolenFaythSinPrototypeCatalog.All)
        .Concat(GagazetSinPrototypeCatalog.All).Concat(ZanarkandSinPrototypeCatalog.All);

    [Theory]
    [InlineData("UNI-009", "0x612A")]
    [InlineData("UNI-013A", "0x612D")]
    [InlineData("UNI-013B", "0x612E")]
    [InlineData("UNI-015", "0x6130", "0x6131")]
    [InlineData("UNI-020", "0x613A", "0x613B")]
    [InlineData("UNI-031", "0x6149")]
    [InlineData("UNI-039", "0x6153", "0x6154")]
    public void Card_ExposesActualCommandsInCastOrder(string id, params string[] operands)
    {
        var entry = Drafts.Single(x => x.Id == id);
        int previous = -1;
        foreach (string operand in operands)
        {
            int position = entry.DisplayPreview.IndexOf(operand, System.StringComparison.Ordinal);
            Assert.True(position > previous, $"{id}: missing or unordered {operand} in preview");
            previous = position;
        }
    }

    [Fact]
    public void ForbiddenRiteCards_DoNotClaimTheExistingStatusWriterIsUnproven()
    {
        foreach (var entry in Drafts.Where(x => x.Id is "UNI-039" or "UNI-040"))
        {
            Assert.Contains("Forbidden Rite", entry.DisplayPreview);
            Assert.DoesNotContain("não está comprovada", entry.Risk);
            Assert.DoesNotContain("não estão comprovados", entry.Risk);
        }
    }
}

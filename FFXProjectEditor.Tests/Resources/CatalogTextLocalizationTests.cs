using System.Globalization;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Resources;
using Xunit;

namespace FFXProjectEditor.Tests.Resources;

public class CatalogTextLocalizationTests
{
    private const string BiblePrefix = "U_AiBibleText";
    private const string BibleSource = "Premio de Blitzball normalizado (parser existe): regra prize+220->takara = proved-candidate offline para treasure; tech = partial; overdrive = metadata-only; premio especifico por-evento = blocked (runtime/save). Nao e RT2/in-game.";
    private const string MagicPrefix = "U_MagicSemantics";
    private const string MagicSource = "angulo X de rotacao (s32, unidades PPP; 256 = default)";

    [Theory]
    [InlineData("en")]
    [InlineData("pt")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("it")]
    [InlineData("ja")]
    [InlineData("ko")]
    [InlineData("zh")]
    public void EmbeddedCatalogProse_UsesTheSelectedSatellite(string cultureName)
    {
        CultureInfo original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);

            Assert.NotEqual(BibleSource, Strings.LocalizeCatalogText(BiblePrefix, BibleSource));
            Assert.NotEqual(MagicSource, Strings.LocalizeCatalogText(MagicPrefix, MagicSource));

            AiBibleEntry entry = Assert.Single(AiBibleCatalog.All, e => e.Id == "guard:setstat-signature");
            Assert.NotEqual("setStatField nao recebe actorRef", entry.Title);

            MagicFamilySchema family = MagicFieldMap.LoadEmbedded().Families["pppAngle"];
            Assert.NotEqual(MagicSource, family.Fields[0].Semantics);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void BibleSearchAndEvidenceClassification_RemainCultureIndependent()
    {
        CultureInfo original = CultureInfo.CurrentUICulture;
        try
        {
            AiBibleEntry entry = Assert.Single(AiBibleCatalog.All, e => e.Id == "guard:setstat-signature");

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            string[] englishBadges = AiBibleEvidence.Tokens(entry).ToArray();
            Assert.Contains(entry, AiBibleCatalog.Search("setStatField", AiBibleCatalog.All.Count));

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("pt");
            string[] portugueseBadges = AiBibleEvidence.Tokens(entry).ToArray();
            Assert.Equal(englishBadges, portugueseBadges);
            Assert.Contains(entry, AiBibleCatalog.Search("setStatField", AiBibleCatalog.All.Count));
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.FfxLib.Mods;
using Xunit;
using FFXProjectEditor.Modules.HookModAuthoring;

namespace FFXProjectEditor.Tests.Core;

public sealed class HookPrivateFixtureFactAttribute : FactAttribute
{
    public HookPrivateFixtureFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FFX_HOOK_PRIVATE_FIXTURES")))
            Skip = "Requires explicitly selected private fixture paths; missing corpus is not validation.";
    }
}
public class HookPrivatePackageTests
{
    [HookPrivateFixtureFact, Trait("Category", "PrivateHookCorpus")]
    public async Task EditorCommandsProduceAnAdmittedIsolatedPackageWithAllRuntimeTogglesOff()
    {
        string master=Environment.GetEnvironmentVariable("FFX_HOOK_MASTER")??throw new InvalidOperationException("FFX_HOOK_MASTER required.");
        string output=Environment.GetEnvironmentVariable("FFX_HOOK_DELIVERY_OUTPUT")??throw new InvalidOperationException("FFX_HOOK_DELIVERY_OUTPUT required.");
        string executable=Environment.GetEnvironmentVariable("FFX_HOOK_GAME_EXE")??throw new InvalidOperationException("FFX_HOOK_GAME_EXE required.");
        var model=new HookModAuthoring_DataModel(master){OnlyModEnabled=true,RegistryJson="{}",Locale="new_uspc",Destination=output,
            ValidatorPath=Environment.GetEnvironmentVariable("FFX_HOOK_CONTRACT_VALIDATOR")!,ExecutablePath=executable};
        await model.PrepareAbilitiesCommand.ExecuteAsync(null);Assert.Contains("ADMITTED RT0",model.PreviewText);
        model.PreparePresetCommand.Execute(null);model.PrepareRewardsCommand.Execute(null);model.PreviewPackageCommand.Execute(null);
        Assert.True(model.CanExportPackage,model.Status);model.ExportPackageCommand.Execute(null);Assert.True(Directory.Exists(output),model.Status);
        Assert.True(File.Exists(Path.Combine(output,"output-inventory.json")));Assert.True(File.Exists(Path.Combine(output,"rollback.json")));
        Assert.Contains("monster_rewards = 0",File.ReadAllText(Path.Combine(output,"hook-profile.ini")));
        string relative="data/mods/ffx_ps2/ffx/master/new_uspc/battle/kernel/a_ability.bin";
        Assert.Equal(File.ReadAllBytes(Path.Combine(master,"new_uspc/battle/kernel/a_ability.bin")),File.ReadAllBytes(Path.Combine(output,relative)));
        Assert.Contains("SHA-256",File.ReadAllText(Path.Combine(output,"rt2-plan.md")));
    }

    [HookPrivateFixtureFact, Trait("Category", "PrivateHookCorpus")]
    public async Task LanguageUiAddsAdmittedPayloadToPackageWithoutPrivateReferenceOrAudioChanges()
    {
        string hooks=Environment.GetEnvironmentVariable("FFX_HOOKS_ROOT")!;
        string languageOutput=Environment.GetEnvironmentVariable("FFX_HOOK_LANGUAGE_OUTPUT")!;
        string output=Environment.GetEnvironmentVariable("FFX_HOOK_DELIVERY_OUTPUT")!;
        var model=new HookModAuthoring_DataModel(Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")))
        {
            OnlyModEnabled=true,HooksSourcePath=hooks,VbfPath=Environment.GetEnvironmentVariable("FFX_HOOK_VBF")!,
            TextValidatorPath=Environment.GetEnvironmentVariable("FFX_HOOK_TEXT_VALIDATOR")!,
            LanguageRecipeJson=File.ReadAllText(Path.Combine(hooks,"tools/text_languages/pt-BR.demo.json")),LanguageDestination=languageOutput,Destination=output
        };
        await model.BuildLanguageCommand.ExecuteAsync(null);Assert.True(model.CanExportLanguagePack,model.Status);
        model.PresetJson=(new VanguardPreset{TextLocale="pt-BR"}).Serialize();model.PreparePresetCommand.Execute(null);model.PreviewPackageCommand.Execute(null);
        Assert.True(model.CanExportPackage,model.Status);model.ExportPackageCommand.Execute(null);Assert.True(Directory.Exists(output),model.Status);
        Assert.True(File.Exists(Path.Combine(output,"languages/pt-BR/manifest.json")));Assert.False(Directory.Exists(Path.Combine(output,"private-reference")));
        string ini=File.ReadAllText(Path.Combine(output,"hook-profile.ini"));Assert.Contains("text_locale = pt-BR",ini);Assert.DoesNotContain("voice",ini,StringComparison.OrdinalIgnoreCase);
    }

    [HookPrivateFixtureFact, Trait("Category", "PrivateHookCorpus")]
    public void RealLocaleBanksValidateAndVanillaInputsRemainIdentical()
    {
        string master = Environment.GetEnvironmentVariable("FFX_HOOK_MASTER") ?? throw new InvalidOperationException("FFX_HOOK_MASTER required.");
        foreach (string locale in Mod002Authoring.Locales)
        {
            string a = Path.Combine(master,locale,"battle/kernel/a_ability.bin"), p = Path.Combine(master,locale,"battle/kernel/arms_rate.bin");
            byte[] abilities = File.ReadAllBytes(a), prices = File.ReadAllBytes(p);
            string before = ModAuthoringFiles.Sha256(abilities), priceBefore = ModAuthoringFiles.Sha256(prices);
            HookAbilityRegistry.ValidateBank(abilities, prices, HookAbilityRegistry.Mapping(), locale);
            var reauthored=HookAbilityBankAuthoring.Generate(abilities,prices,locale);
            Assert.Equal(abilities,reauthored.Abilities);Assert.Equal(prices,reauthored.Prices);
            var session = new Mod002Authoring(abilities,prices,Mod002Manifest.Default(),locale);
            Assert.Equal(abilities,session.Preview(session.Rows).Abilities);
            var edited = session.Rows.ToArray(); edited[0] = edited[0] with { Price = edited[0].Price + 1 };
            var changed = session.Preview(edited);
            Assert.Equal(abilities,changed.Abilities);
            HookAbilityRegistry.ValidateBank(changed.Abilities,changed.Prices,HookAbilityRegistry.Mapping(),locale);
            Assert.Equal(before,TextLanguagePackageExporter.StreamHash(a)); Assert.Equal(priceBefore,TextLanguagePackageExporter.StreamHash(p));
        }
    }

    [HookPrivateFixtureFact, Trait("Category", "PrivateHookCorpus")]
    public async Task BuildLanguageFontsAndRecipientPreflightFromSelectedPrivateVbf()
    {
        string hooks = Environment.GetEnvironmentVariable("FFX_HOOKS_ROOT") ?? throw new InvalidOperationException("FFX_HOOKS_ROOT required.");
        string output = Environment.GetEnvironmentVariable("FFX_HOOK_LANGUAGE_OUTPUT") ?? throw new InvalidOperationException("FFX_HOOK_LANGUAGE_OUTPUT required.");
        string vbf = Environment.GetEnvironmentVariable("FFX_HOOK_VBF") ?? throw new InvalidOperationException("FFX_HOOK_VBF required.");
        string validator = Environment.GetEnvironmentVariable("FFX_HOOK_TEXT_VALIDATOR") ?? throw new InvalidOperationException("FFX_HOOK_TEXT_VALIDATOR required.");
        byte[] recipe = File.ReadAllBytes(Path.Combine(hooks,"tools/text_languages/pt-BR.demo.json"));
        var build = await TextLanguagePackageExporter.BuildAsync(new() { Mode = HookProjectMode.OnlyMod }, "python3", hooks, vbf, recipe, output, validator, CancellationToken.None);
        Assert.Contains("ADMITTED RT0",build.Report); Assert.Contains("0 failures",build.Report);
        Assert.Equal(ModAuthoringFiles.Sha256(recipe),build.RecipeHash);
        Assert.True(File.Exists(Path.Combine(output,"languages/pt-BR/font/base.ftc")));
        foreach (string kind in new[] { "font", "shadow" }) foreach (int parity in new[] { 0,1 })
            Assert.True(File.Exists(Path.Combine(output,$"languages/pt-BR/font/{kind}_0_{parity}.dds.phyre")));
        Assert.True(File.Exists(Path.Combine(output,"inventory.json")));
    }
}

using System;
using System.IO;
using System.Text;
using FFXProjectEditor.FfxLib.Mods;
using FFXProjectEditor.Modules.HookModAuthoring;
using Xunit;

namespace FFXProjectEditor.Tests.Core;
public class HookProjectUiContractTests
{
    [Fact]
    public void OpeningVanillaAndInvokingHookCommandsCannotPrepareAnExport()
    {
        var model=new HookModAuthoring_DataModel(Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")));
        Assert.False(model.OnlyModEnabled);Assert.False(model.HasSession);Assert.False(model.CanExportPackage);
        model.PrepareRewardsCommand.Execute(null);model.PreparePresetCommand.Execute(null);model.PreparePreferencesCommand.Execute(null);
        Assert.False(model.CanExportPackage);Assert.False(model.CanExportLanguagePack);Assert.False(model.WorkshopEnabled);
        Assert.Equal("Vanilla",model.ProjectModeLabel);
    }
    [Fact]
    public void AnyRewardEditOrModeChangeInvalidatesPackageConfirmation()
    {
        string root=Path.Combine(Path.GetTempPath(),"ffx-ui-preview-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var model=new HookModAuthoring_DataModel(Path.Combine(root,"source")){OnlyModEnabled=true,Destination=Path.Combine(root,"export")};
            model.PrepareRewardsCommand.Execute(null);model.PreviewPackageCommand.Execute(null);Assert.True(model.CanExportPackage);
            model.Rewards.Selected!.ApRate=2;Assert.False(model.CanExportPackage);
            model.PrepareRewardsCommand.Execute(null);model.PreviewPackageCommand.Execute(null);Assert.True(model.CanExportPackage);
            model.OnlyModEnabled=false;Assert.False(model.CanExportPackage);model.ExportPackageCommand.Execute(null);Assert.False(Directory.Exists(Path.Combine(root,"export")));
        }
        finally {Directory.Delete(root,true);}
    }
    [Fact]
    public void EnablingAConsumerDoesNotExportAnIncompletePackage()
    {
        string root=Path.Combine(Path.GetTempPath(),"ffx-dependency-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var model=new HookModAuthoring_DataModel(Path.Combine(root,"source")){OnlyModEnabled=true,Destination=Path.Combine(root,"out"),PresetJson=(new VanguardPreset{MonsterRewardsEnabled=true}).Serialize()};
            model.PreparePresetCommand.Execute(null);model.PreviewPackageCommand.Execute(null);Assert.False(model.CanExportPackage);
            model.PrepareRewardsCommand.Execute(null);model.PreviewPackageCommand.Execute(null);Assert.True(model.CanExportPackage);
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public void EditingOneMonstersRatesNeverEditsTheOtherOrTheNativeLoot()
    {
        var model=new MonsterRewards_DataModel();model.Selected=model.Rows[1];model.Selected.ApRate=20;model.Selected.GilRate=7;
        model.Selected=model.Rows[2];Assert.Equal(1,model.Selected.ApRate);Assert.Equal(1,model.Selected.GilRate);
        var parsed=MonsterRewardSettingsCodec.Parse(model.ExportBytes());Assert.Equal(new MonsterRewardRates(20,7),parsed[1]);Assert.Equal(MonsterRewardRates.Neutral,parsed[2]);
    }
    [Fact]
    public void TextRecipeRequiresProtectedContractAndCannotClaimCompleteCoverage()
    {
        const string recipe="{\"locale\":\"pt-BR\",\"display_name\":\"Português\",\"pack_version\":\"1.0.0\",\"resources\":{\"menu_txt.bin\":[{\"row\":0,\"slot\":0,\"text\":\"{TIDUS} {VAR:0}\"}]}}";
        Assert.Single(TextLanguageRecipeEditor.Parse(Encoding.UTF8.GetBytes(recipe)));
        Assert.Throws<InvalidDataException>(()=>TextLanguageRecipeEditor.Parse(Encoding.UTF8.GetBytes(recipe.Replace("pt-BR","es-MX"))));
        Assert.Throws<InvalidDataException>(()=>TextLanguageRecipeEditor.Parse(Encoding.UTF8.GetBytes(recipe.Replace("\"slot\":0","\"slot\":4"))));
        Assert.Equal(78,ArcanaAssetAuthoring.Catalog.Count);
    }
}

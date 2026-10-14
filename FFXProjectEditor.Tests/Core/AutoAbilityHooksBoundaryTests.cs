using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Mods;
using FFXProjectEditor.Modules.AutoAbilityEditor;
using FFXProjectEditor.Modules.HookModAuthoring;
using FFXProjectEditor.Services;
using FFXProjectEditor.Tests.Services;
using Xunit;

namespace FFXProjectEditor.Tests.Core;

[Collection(ProjectServiceCollection.Name)]
public class AutoAbilityHooksBoundaryTests
{
    [Theory]
    [InlineData(134, true)]
    [InlineData(148, false)]
    public void OpeningVanillaNeedsNoProfile_ModRowsRequireSeparateAuthoring(int count, bool vanillaWritable)
    {
        string root = Path.Combine(Path.GetTempPath(), "ffx-profile-boundary-" + Guid.NewGuid().ToString("N"));
        string? previous = Project_Service.Instance.ProjectPath;
        AutoAbilityEditor_DataModel? model = null;
        try
        {
            string a = Path.Combine(root, "new_uspc/battle/kernel/a_ability.bin"), p = Path.Combine(root, "jppc/battle/kernel/arms_rate.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(a)!); Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            var (ability, price) = HookModAuthoringTests.Kernel(count);
            File.WriteAllBytes(a, ability); File.WriteAllBytes(p, price);
            Project_Service.Instance.ProjectPath = root;
            model = new AutoAbilityEditor_DataModel();
            Assert.Equal(vanillaWritable, model.EditingSurfaceEnabled);
            Assert.Equal(ability, File.ReadAllBytes(a)); Assert.Equal(price, File.ReadAllBytes(p));
            Assert.Equal(vanillaWritable ? 0 : 13, model.LoadedAbilities.Count(r => r.IsMod002Row));
            if (vanillaWritable)
            {
                model.EditSession!.AutoSaveEnabled = false;
                model.SelectedAbility!.NameText = "Edited vanilla name";
                model.Save();
                Assert.Equal("Edited vanilla name", AutoAbility_File.Read(File.ReadAllBytes(a), File.ReadAllBytes(p)).Entries[0].NameText);
                Assert.Empty(Directory.GetFiles(root, "*.json", SearchOption.AllDirectories));
            }
            else
            {
                Assert.Null(model.EditSession);
                model.Save(); model.Undo(); model.Discard();
                Assert.Equal(ability, File.ReadAllBytes(a)); Assert.Equal(price, File.ReadAllBytes(p));
            }
        }
        finally
        {
            model?.EditSession?.Dispose(); Project_Service.Instance.ProjectPath = previous;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void UnifiedRegistryIdentifiesSpiraAndLeavesUnrelatedVanillaRowsAlone()
    {
        var (a,p)=HookModAuthoringTests.Kernel(135);var authored=HookAbilityBankAuthoring.Generate(a,p,"new_uspc");
        var table=AutoAbility_File.Read(authored.Abilities,authored.Prices);
        Assert.Equal(40,table.Entries.Count(e=>HookAbilityRegistry.Identify(e)!=null));
        Assert.All(table.Entries.Take(135),e=>Assert.Null(HookAbilityRegistry.Identify(e)));
    }
    [Fact]
    public void ProfileDeactivationClearsImportedDataAndPreview()
    {
        var model = new HookModAuthoring_DataModel("") { OnlyModEnabled = true, Mod002Enabled = true, Mod004Enabled = true, Mod005Enabled = true };
        model.Abilities.Add(new HookModAbilityRow { Id = 135 });
        model.PreviewText = "old preview";
        model.Mod002Enabled = false;
        Assert.Empty(model.Abilities); Assert.Empty(model.PreviewText); Assert.False(model.CanExport);
        Assert.True(model.WorkshopEnabled);
    }
}

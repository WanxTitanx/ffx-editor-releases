using System;
using System.IO;
using System.Globalization;
using System.Buffers.Binary;
using System.Text;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using FFXProjectEditor.Modules.AuroraChamber;
using FFXProjectEditor.FfxLib.BattleMap;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Services;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub;

[Collection(FFXProjectEditor.Tests.Services.ProjectServiceCollection.Name)]
public sealed class AuroraWorkspaceTests
{
    private static XDocument View() => XDocument.Load(Path.Combine(TestDataPaths.RepoRoot,
        "FFXProjectEditor", "Modules", "AuroraChamber", "AuroraChamber_Control.axaml"));

    [Fact]
    public void EncounterList_IncludesProjectBattlesEvenWhenTheRandomTableHasAnEntry()
    {
        string root = Path.Combine(Path.GetTempPath(), "aurora-battle-list-" + Guid.NewGuid().ToString("N"));
        var project = Project_Service.Instance;
        string? previous = project.ProjectPath;
        AuroraChamber_DataModel? model = null;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "master"));
            project.ProjectPath = Path.Combine(root, "master");
            foreach (string id in new[] { "nagi05_00", "nagi05_06", "nagi05_74" })
            {
                string path = project.GetPathBattle(id);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, new byte[32]);
            }
            byte[] table = new byte[0x27];
            BinaryPrimitives.WriteInt32LittleEndian(table.AsSpan(4), 0x10);
            BinaryPrimitives.WriteInt32LittleEndian(table.AsSpan(8), 0x1E);
            BinaryPrimitives.WriteInt32LittleEndian(table.AsSpan(12), table.Length);
            Encoding.ASCII.GetBytes("nagi05").CopyTo(table, 0x16);
            table[0x1E] = 1; table[0x1F] = 1; table[0x20] = 1;
            table[0x24] = 1; table[0x25] = 0; table[0x26] = 1;
            Directory.CreateDirectory(Path.GetDirectoryName(project.Path_KernelEncounterTable)!);
            File.WriteAllBytes(project.Path_KernelEncounterTable, table);
            model = new AuroraChamber_DataModel();
            Assert.True(model.SelectSceneByMapKey("nagi05"));
            Assert.Equal(new[] { "nagi05_00", "nagi05_06", "nagi05_74" }, model.Battles);
        }
        finally
        {
            model?.CloseRealGamePreview();
            project.ProjectPath = previous;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RootCollectionBindings_ResolveToExistingViewModelProperties()
    {
        var bindings = View().Descendants()
            .Where(e => !e.Ancestors().Any(a => a.Name.LocalName == "DataTemplate"))
            .Select(e => (string?)e.Attribute("ItemsSource"))
            .Where(s => s != null)
            .Select(s => Regex.Match(s!, @"^\{Binding\s+(\w+)[,}]").Groups[1].Value)
            .Where(s => s.Length > 0).ToArray();
        Assert.Contains("OpeningEditorRows", bindings);
        foreach (string binding in bindings)
            Assert.True(typeof(AuroraChamber_DataModel).GetProperty(binding) != null,
                $"The root ItemsSource binding '{binding}' has no view-model property.");
    }

    [Fact]
    public async Task MissingScene_ReturnsVisibleFeedbackInsteadOfLeavingTheOldStatus()
    {
        var model = new AuroraChamber_DataModel { SelectedScene = null, RenderStatus = "previous status" };
        try
        {
            var result = await model.RenderSelectedEmbeddedAsync();
            Assert.False(result.Ok);
            Assert.Null(result.Url);
            Assert.Equal(result.Message, model.RenderStatus);
            Assert.NotEqual("previous status", model.RenderStatus);
        }
        finally { model.CloseRealGamePreview(); }
    }

    [Fact]
    public async Task HdOnlyAction_DoesNotOpenTheBattlePreviewForAMapOnlyLocation()
    {
        var model = new AuroraChamber_DataModel
        {
            SelectedScene = new AuroraSceneRow(BattleMap_Scene.CreateMapOnlyVirtual("test00")),
            SelectedBattle = "test00_00"
        };
        var server = ViewerHubService.Server;
        try
        {
            Assert.False(model.CanOpenHdMapEditor);
            Assert.True(model.CanOpenBattlePreview);
            var result = await model.RenderSelectedEmbeddedAsync(hdOnly: true);
            Assert.False(result.Ok);
            Assert.Null(result.Url);
            Assert.Equal(model.HdMapAvailability, result.Message);
            Assert.Equal(result.Message, model.RenderStatus);
            Assert.Same(server, ViewerHubService.Server);
        }
        finally { model.CloseRealGamePreview(); }
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void MountedHdMap_DoesNotRequireTheOptionalExporter(bool mounted, bool exporter, bool expected)
    {
        var scene = new BattleMap_Scene
        {
            AreaCode = "test", SceneId = "test00_a", MapKey = "test00", Variant = "a", MapIndex = 0,
            PrimaryDaeRelPath = "test/test00_a/test00_a.dae.phyre", PrimaryDaeFullPath = "",
            PrimaryDaeSize = 10000, HasPrimaryDae = true, HasTwoDSlice = false,
            AllDaeCount = 1, TextureCount = 1, HasAhwin32 = false
        };
        Assert.Equal(expected, AuroraSceneRenderer.CanUseHdGeometry(scene, mounted, exporter));
        Assert.False(AuroraSceneRenderer.CanUseHdGeometry(BattleMap_Scene.CreateMapOnlyVirtual("test00"), mounted, exporter));
    }

    [Fact]
    public void BusyState_DisablesEncounterChangesAndAuthoringActions()
    {
        var model = new AuroraChamber_DataModel { SelectedBattle = "test00_00" };
        try
        {
            model.Battles.Add("test00_00");
            Assert.True(model.CanSelectEncounter);
            Assert.True(model.CanOpenBattlePreview);
            model.IsRendering = true;
            Assert.False(model.CanSelectEncounter);
            Assert.False(model.CanOpenBattlePreview);
            Assert.False(model.CanDuplicateMonster);
            Assert.False(model.CanRemoveMonster);
            Assert.False(model.CanOpenHdMapEditor);
        }
        finally { model.CloseRealGamePreview(); }
    }

    [Fact]
    public void ChangingEncounter_ClearsFeedbackFromThePreviousEncounter()
    {
        var model = new AuroraChamber_DataModel { SelectedBattle = "test00_00" };
        try
        {
            model.PreviewStatus = "old preview";
            model.AuthorStatus = "old formation";
            model.SelectedBattle = "test00_01";
            Assert.False(model.HasPreviewStatus);
            Assert.False(model.HasAuthorStatus);
        }
        finally { model.CloseRealGamePreview(); }
    }

    [Fact]
    public void MountedState_NotifiesTheLocationSummaryBinding()
    {
        var row = new AuroraSceneRow(BattleMap_Scene.CreateMapOnlyVirtual("test00"));
        var names = new System.Collections.Generic.List<string?>();
        row.PropertyChanged += (_, e) => names.Add(e.PropertyName);
        row.IsMounted = true;
        Assert.Contains(nameof(row.Summary), names);
        Assert.Contains(nameof(row.DetailSummary), names);
    }

    [Fact]
    public void NavigationListsStayVisibleAndToolsAreGroupedInHorizontalTabs()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var view = View();
        foreach (string name in new[] { "SceneList", "EncounterPicker" })
        {
            var list = Assert.Single(view.Descendants().Where(e => (string?)e.Attribute(x + "Name") == name));
            Assert.Equal("ListBox", list.Name.LocalName);
            Assert.DoesNotContain(list.Ancestors(), e => e.Name.LocalName is "Expander" or "TabItem");
        }
        var tools = Assert.Single(view.Descendants().Where(e => (string?)e.Attribute(x + "Name") == "WorkspaceTools"));
        Assert.Equal("TabControl", tools.Name.LocalName);
        Assert.Equal("0", (string?)tools.Attribute("SelectedIndex"));
        Assert.Equal(5, tools.Elements().Count(e => e.Name.LocalName == "TabItem"));
        var battle = Assert.Single(view.Descendants().Where(e => (string?)e.Attribute(x + "Name") == "OpenBattlePreview"));
        Assert.DoesNotContain(battle.Ancestors(), e => e.Name.LocalName is "Expander" or "TabItem");
        var hd = Assert.Single(view.Descendants().Where(e => (string?)e.Attribute(x + "Name") == "OpenHdMapEditor"));
        Assert.Contains(hd.Ancestors(), e => (string?)e.Attribute(x + "Name") == "HdMapTools");
        Assert.Equal("{Binding CanOpenHdMapEditor}", (string?)hd.Attribute("IsEnabled"));
    }

    [Fact]
    public void EditableInputs_HaveAccessibleNamesIndependentOfTheirPlaceholders()
    {
        var inputs = View().Descendants().Where(e => e.Name.LocalName is "TextBox" or "ComboBox" or "NumericUpDown").ToArray();
        Assert.NotEmpty(inputs);
        foreach (var input in inputs)
            Assert.False(string.IsNullOrWhiteSpace((string?)input.Attribute("AutomationProperties.Name")),
                $"An editable {input.Name.LocalName} needs an accessible name.");
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("pt-BR")]
    public void CameraAllFilter_RestoresEveryParameterInEachUiLanguage(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        AuroraChamber_DataModel? model = null;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            model = new AuroraChamber_DataModel();
            model.PopulateCameraKnobs(new BattleCameraSetup_File
            {
                OriginalBinBytes = Array.Empty<byte>(), BattleId = "fixture", Chunk0Offset = 0, Chunk0Length = 16,
                Calls = Array.Empty<CameraCall>(), Notes = Array.Empty<string>(),
                FloatParams = new[]
                {
                    new CameraFloatParam { PoolIndex = 1, Value = 45, Roles = new[] { "polar" }, UseCount = 1 },
                    new CameraFloatParam { PoolIndex = 2, Value = 90, Roles = new[] { "refPos" }, UseCount = 1 }
                }
            });
            model.CameraRoleFilter = "polar";
            Assert.Single(model.CameraKnobs);
            model.CameraRoleFilter = Strings.U_Au_All;
            Assert.Equal(2, model.CameraKnobs.Count);
        }
        finally
        {
            model?.CloseRealGamePreview();
            CultureInfo.CurrentUICulture = previous;
        }
    }
}

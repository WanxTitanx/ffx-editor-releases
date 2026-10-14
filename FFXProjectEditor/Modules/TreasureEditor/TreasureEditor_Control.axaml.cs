using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FFXProjectEditor.Modules.Common;
using FFXProjectEditor.Modules.TreasureEditor;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Services;
using System.Collections.Generic;

namespace FFXProjectEditor;

public partial class TreasureEditor_Control : UserControl, IRestorableModule
{
    readonly TreasureEditor_DataModel dataModel;

    public TreasureEditor_Control()
    {
        dataModel = new TreasureEditor_DataModel();
        DataContext = dataModel;
        InitializeComponent();
        ConfigureCapabilityBadge();
    }

    private void ConfigureCapabilityBadge()
    {
        TreasureCapabilityBadge.SetCapability(new FFXProjectEditor.Core.CapabilityDescriptor
        {
            Id = "treasure-editor",
            Domain = "Treasure",
            Title = "Treasure & Chest Editor",
            Description = Strings.U_Tre_TreasureTablesTitle,
            Mode = FFXProjectEditor.Core.CapabilityMode.OfflineWriter,
            Evidence = FFXProjectEditor.Core.EvidenceLevel.Production,
            Platforms = new[] { FFXProjectEditor.Core.Platform.PC },
            RequiredDependencies = System.Array.Empty<string>(),
            OptionalDependencies = System.Array.Empty<string>(),
            Risks = System.Array.Empty<string>(),
            AllowedOperations = new[] { FFXProjectEditor.Core.AllowedOperation.Read, FFXProjectEditor.Core.AllowedOperation.Edit },
            ProhibitedOperations = System.Array.Empty<FFXProjectEditor.Core.AllowedOperation>(),
            Preconditions = System.Array.Empty<string>(),
            DocumentationLinks = System.Array.Empty<string>(),
            OwnerAgent = "Jarvis"
        });
    }

    private void Button_ReviewDiff_Click(object? sender, RoutedEventArgs e)
    {
        AudioStudio_Service.Instance.PlayEditorOpen();
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is Main_Window mainWindow)
        {
            var preview = new Core.OperationPreview
            {
                OperationId = "treasure-editor-preview",
                DisplayName = "Treasure Table (takara.bin)",
                FileCount = 1,
                TotalBytes = 1024,
                OverallRisk = Core.RiskLevel.Safe,
                FilePreviewSummaries = new System.Collections.Generic.List<Core.FilePreviewSummary>
                {
                    new Core.FilePreviewSummary
                    {
                        FileId = "takara.bin",
                        SourceRelativePath = "battle/kernel/takara.bin",
                        OutputRelativePath = "battle/kernel/takara.bin",
                        BeforeHash = "N/A",
                        PredictedAfterHash = "N/A",
                        ByteDiffLines = new System.Collections.Generic.List<string> { "Offset 0x0000..0x0400 [Modificado]" },
                        DisassemblyDiffLines = new System.Collections.Generic.List<string> { dataModel.EditSession?.BehaviorSummary ?? "Recompensas de Gil, Itens e Equipamentos." },
                        SemanticDiffLines = new System.Collections.Generic.List<string> { dataModel.EditSession?.SessionSummary ?? Strings.F2_changes_in_the_treasure_and_rewards_tabl_991290a7 },
                        HumanSummary = Strings.F2_hardcoded_preview_adapted_to_v2_0711926b
                    }
                }
            };
            mainWindow.ShowChangeSetPreview(preview);
        }
    }

    private void Button_Refresh(object? sender, RoutedEventArgs e)
    {
        dataModel.RefreshFromDisk();
        AudioStudio_Service.Instance.PlayEditorOpen();
    }

    private void Button_Save(object? sender, RoutedEventArgs e)
    {
        dataModel.Save();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_Undo(object? sender, RoutedEventArgs e)
    {
        dataModel.Undo();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_Discard(object? sender, RoutedEventArgs e)
    {
        dataModel.Discard();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_SetGilKind(object? sender, RoutedEventArgs e)
    {
        dataModel.SetSelectedTreasureKind(0x00);
    }

    private void Button_SetItemKind(object? sender, RoutedEventArgs e)
    {
        dataModel.SetSelectedTreasureKind(0x02);
    }

    private void Button_SetGearKind(object? sender, RoutedEventArgs e)
    {
        dataModel.SetSelectedTreasureKind(0x05);
    }

    private void Button_SetKeyItemKind(object? sender, RoutedEventArgs e)
    {
        dataModel.SetSelectedTreasureKind(0x0A);
    }

    private void Button_OpenBukiGetAtlas(object? sender, RoutedEventArgs e)
    {
        int? bukiRow = dataModel.GetSelectedBukiGetRow();
        if (!bukiRow.HasValue)
            return;

        this.FindAncestorOfType<Main_Window>()?.OpenBukiGetRewards(bukiRow.Value);
        AudioStudio_Service.Instance.PlayEditorOpen();
    }

    // --- IRestorableModule (Jarvis-UI Fase D §D2) ---
    // Back/forward: captura filtro + seleção atual; restore repõe filtro (reaplica o ApplyFilter
    // via OnFilterTextChanged) e tenta re-selecionar a row pelo índice na lista exibida.
    public Dictionary<string, object?>? CaptureState()
    {
        int? selectedIndex = dataModel.SelectedTreasure is { } row
            ? dataModel.DisplayedTreasures.IndexOf(row)
            : null;
        return new()
        {
            ["filterText"] = dataModel.FilterText,
            ["selectedIndex"] = selectedIndex >= 0 ? selectedIndex : null,
        };
    }

    public void RestoreState(Dictionary<string, object?>? state)
    {
        if (state == null) return;

        if (state.TryGetValue("filterText", out object? filterObj) && filterObj is string filter)
            dataModel.FilterText = filter;

        if (state.TryGetValue("selectedIndex", out object? indexObj) && indexObj is int idx && idx >= 0)
        {
            var displayed = dataModel.DisplayedTreasures;
            if (idx < displayed.Count)
                dataModel.SelectedTreasure = displayed[idx];
        }
    }
}

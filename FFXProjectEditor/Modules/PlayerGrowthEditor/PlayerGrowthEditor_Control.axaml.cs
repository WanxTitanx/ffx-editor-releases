using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules.Common;
using FFXProjectEditor.Modules.PlayerGrowthEditor;
using FFXProjectEditor.Services;
using System.Collections.Generic;

namespace FFXProjectEditor;

public partial class PlayerGrowthEditor_Control : UserControl, IRestorableModule
{
    readonly PlayerGrowthEditor_DataModel dataModel;

    public PlayerGrowthEditor_Control()
    {
        dataModel = new PlayerGrowthEditor_DataModel();
        DataContext = dataModel;
        InitializeComponent();
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

    // --- IRestorableModule (Jarvis-UI Fase D §D2) ---
    public Dictionary<string, object?>? CaptureState()
    {
        int? selectedIndex = dataModel.SelectedCharacter is { } row
            ? dataModel.DisplayedCharacters.IndexOf(row)
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
            var displayed = dataModel.DisplayedCharacters;
            if (idx < displayed.Count)
                dataModel.SelectedCharacter = displayed[idx];
        }
    }
}

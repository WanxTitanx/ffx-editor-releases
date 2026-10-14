using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules.Common;
using FFXProjectEditor.Modules.FormationEditor;
using FFXProjectEditor.Services;
using System.Collections.Generic;

namespace FFXProjectEditor;

public partial class FormationEditor_Control : UserControl, IRestorableModule
{
    readonly FormationEditor_DataModel dataModel;

    public FormationEditor_Control()
    {
        dataModel = new FormationEditor_DataModel();
        DataContext = dataModel;
        InitializeComponent();
    }

    private void Button_Refresh(object? sender, RoutedEventArgs e)
    {
        dataModel.RefreshFromDisk();
        AudioStudio_Service.Instance.PlayEditorOpen();
    }

    private void Button_SyncField(object? sender, RoutedEventArgs e)
    {
        dataModel.SyncToField();
        AudioStudio_Service.Instance.PlayAlternative();
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
    // Formation tem coleção `Battles` (sem Displayed*); seleção é por `SelectedBattle`.
    public Dictionary<string, object?>? CaptureState()
    {
        int? selectedIndex = dataModel.SelectedBattle is { } row
            ? dataModel.Battles.IndexOf(row)
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
            var battles = dataModel.Battles;
            if (idx < battles.Count)
                dataModel.SelectedBattle = battles[idx];
        }
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules.Common;
using FFXProjectEditor.Modules.KeyItemEditor;
using FFXProjectEditor.Services;
using System.Collections.Generic;

namespace FFXProjectEditor;

public partial class KeyItemEditor_Control : UserControl, IRestorableModule
{
    readonly KeyItemEditor_DataModel dataModel;

    public KeyItemEditor_Control()
    {
        dataModel = new KeyItemEditor_DataModel();
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
        int? selectedIndex = dataModel.SelectedItem is { } row
            ? dataModel.DisplayedItems.IndexOf(row)
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
            var displayed = dataModel.DisplayedItems;
            if (idx < displayed.Count)
                dataModel.SelectedItem = displayed[idx];
        }
    }
}

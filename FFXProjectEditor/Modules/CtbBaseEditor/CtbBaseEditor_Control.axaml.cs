using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules.CtbBaseEditor;
using FFXProjectEditor.Modules.Common;
using FFXProjectEditor.Services;
using System.Collections.Generic;

namespace FFXProjectEditor;

public partial class CtbBaseEditor_Control : UserControl, IRestorableModule
{
    readonly CtbBaseEditor_DataModel dataModel;

    public CtbBaseEditor_Control()
    {
        dataModel = new CtbBaseEditor_DataModel();
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
        int? selectedIndex = dataModel.SelectedRow is { } row
            ? dataModel.DisplayedRows.IndexOf(row)
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
            var displayed = dataModel.DisplayedRows;
            if (idx < displayed.Count)
                dataModel.SelectedRow = displayed[idx];
        }
    }
}

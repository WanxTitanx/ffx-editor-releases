using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules.EncounterTableExplorer;
using FFXProjectEditor.Services;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor;

public partial class EncounterTableExplorer_Control : UserControl, IRestorableModule
{
    readonly EncounterTableExplorer_DataModel dataModel;

    /// <summary>Raised when the user double-clicks a decoded table → request opening the Aurora Chamber rendered at
    /// that encounter's map (e.g. "bika02"). Wired by Main_Window to the cross-module render bridge.</summary>
    public event Action<string>? RequestOpenAuroraChamberForMap;

    public EncounterTableExplorer_Control()
    {
        dataModel = new EncounterTableExplorer_DataModel();
        DataContext = dataModel;
        InitializeComponent();
    }

    private void Button_Refresh(object? sender, RoutedEventArgs e)
    {
        dataModel.RefreshFromDisk();
        AudioStudio_Service.Instance.PlayEditorOpen();
    }

    // Double-click a table → jump to its scene in the Aurora Chamber (the map key bridges encounter → btlmap scene).
    private void Tables_DoubleTapped(object? sender, TappedEventArgs e)
    {
        string? map = dataModel.SelectedTable?.Entry.Map;
        if (!string.IsNullOrWhiteSpace(map))
        {
            RequestOpenAuroraChamberForMap?.Invoke(map);
            AudioStudio_Service.Instance.PlayConfirm();
        }
    }
}

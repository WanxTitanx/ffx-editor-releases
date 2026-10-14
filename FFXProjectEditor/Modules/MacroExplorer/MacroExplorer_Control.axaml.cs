using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules.MacroExplorer;
using FFXProjectEditor.Services;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor;

public partial class MacroExplorer_Control : UserControl, IRestorableModule
{
    readonly MacroExplorer_DataModel dataModel;

    public MacroExplorer_Control()
    {
        dataModel = new MacroExplorer_DataModel();
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
        dataModel.SaveCurrentSource();
    }

    private void Button_Undo(object? sender, RoutedEventArgs e)
    {
        dataModel.UndoCurrentSource();
    }

    private void Button_Discard(object? sender, RoutedEventArgs e)
    {
        dataModel.DiscardCurrentSource();
    }
}

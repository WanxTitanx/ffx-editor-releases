using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules.WeaponNameExplorer;
using FFXProjectEditor.Services;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor;

public partial class WeaponNameExplorer_Control : UserControl, IRestorableModule
{
    readonly WeaponNameExplorer_DataModel dataModel;

    public WeaponNameExplorer_Control()
    {
        dataModel = new WeaponNameExplorer_DataModel();
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

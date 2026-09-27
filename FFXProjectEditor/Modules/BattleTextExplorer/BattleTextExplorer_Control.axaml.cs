using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules.BattleTextExplorer;
using FFXProjectEditor.Services;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor;

public partial class BattleTextExplorer_Control : UserControl, IRestorableModule
{
    readonly BattleTextExplorer_DataModel dataModel;

    public BattleTextExplorer_Control()
    {
        dataModel = new BattleTextExplorer_DataModel();
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

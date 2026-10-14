using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules.StringExplorer;
using FFXProjectEditor.Services;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor;

public partial class StringExplorer_Control : UserControl, IRestorableModule
{
    readonly StringExplorer_DataModel dataModel;

    public StringExplorer_Control()
    {
        dataModel = new StringExplorer_DataModel();
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
        if (dataModel.SaveCurrentSource())
        {
            AudioStudio_Service.Instance.PlayConfirm();
        }
    }

    private void Button_Undo(object? sender, RoutedEventArgs e)
    {
        dataModel.UndoCurrentSource();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_Discard(object? sender, RoutedEventArgs e)
    {
        dataModel.DiscardCurrentSource();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_OpenLinkedMonster(object? sender, RoutedEventArgs e)
    {
        StringExplorerRecord? record = dataModel.SelectedRecord;
        if (record?.IsMonsterLocalizationRecord != true)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is Main_Window mainWindow)
        {
            mainWindow.OpenMonsterEditorFromExternalModule(record.MonsterIndex);
            AudioStudio_Service.Instance.PlayConfirm();
        }
    }
}

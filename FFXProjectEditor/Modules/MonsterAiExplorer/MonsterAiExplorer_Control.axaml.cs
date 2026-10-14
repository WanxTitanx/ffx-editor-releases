using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules.MonsterAiExplorer;
using FFXProjectEditor.Services;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor;

public partial class MonsterAiExplorer_Control : UserControl, IRestorableModule
{
    readonly MonsterAiExplorer_DataModel dataModel;

    public MonsterAiExplorer_Control()
    {
        dataModel = new MonsterAiExplorer_DataModel();
        DataContext = dataModel;
        InitializeComponent();
    }

    private void Button_Refresh(object? sender, RoutedEventArgs e)
    {
        dataModel.RefreshFromDisk();
        AudioStudio_Service.Instance.PlayConfirm();
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules.MixTableEditor;
using FFXProjectEditor.Services;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor;

public partial class MixTableEditor_Control : UserControl, IRestorableModule
{
    private readonly MixTableEditor_DataModel dataModel;

    public MixTableEditor_Control()
    {
        InitializeComponent();
        dataModel = new MixTableEditor_DataModel();
        DataContext = dataModel;
    }

    private void Button_Refresh(object? sender, RoutedEventArgs e)
    {
        dataModel.RefreshFromDisk();
        AudioStudio_Service.Instance.PlayConfirm();
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

    private void Button_ClearResult(object? sender, RoutedEventArgs e)
    {
        dataModel.ClearSelectedResult();
        AudioStudio_Service.Instance.PlayMiniEditorConfirm();
    }
}

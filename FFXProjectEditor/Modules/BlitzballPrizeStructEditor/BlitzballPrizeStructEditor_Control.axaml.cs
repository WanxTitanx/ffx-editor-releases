using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules.BlitzballPrizeStructEditor;
using FFXProjectEditor.Services;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor;

public partial class BlitzballPrizeStructEditor_Control : UserControl, IRestorableModule
{
    readonly BlitzballPrizeStructEditor_DataModel dataModel;

    public BlitzballPrizeStructEditor_Control()
    {
        dataModel = new BlitzballPrizeStructEditor_DataModel();
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
}

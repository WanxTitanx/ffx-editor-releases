using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules.AlBhedDictionaryEditor;
using FFXProjectEditor.Services;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor;

public partial class AlBhedDictionaryEditor_Control : UserControl, IRestorableModule
{
    readonly AlBhedDictionaryEditor_DataModel dataModel;

    public AlBhedDictionaryEditor_Control()
    {
        dataModel = new AlBhedDictionaryEditor_DataModel();
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

using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules.SpiraForgeHub;
using FFXProjectEditor.Services;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor;

public partial class SpiraForgeHub_Control : UserControl, IRestorableModule
{
    readonly SpiraForgeHub_DataModel dataModel;

    public SpiraForgeHub_Control()
    {
        dataModel = new SpiraForgeHub_DataModel();
        DataContext = dataModel;
        InitializeComponent();
    }

    private void Button_Refresh(object? sender, RoutedEventArgs e)
    {
        dataModel.RefreshFromDisk();
        AudioStudio_Service.Instance.PlayEditorOpen();
    }

    private void Button_OpenInMapViewer(object? sender, RoutedEventArgs e)
    {
        dataModel.TryOpenInMapViewer();
        AudioStudio_Service.Instance.PlayConfirm();
    }
}

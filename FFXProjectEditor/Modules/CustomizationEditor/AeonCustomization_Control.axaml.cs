using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules.CustomizationEditor;
using FFXProjectEditor.Services;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor;

public partial class AeonCustomization_Control : UserControl, IRestorableModule
{
    readonly CustomizationEditor_DataModel dataModel;

    public AeonCustomization_Control()
    {
        dataModel = new CustomizationEditor_DataModel();
        DataContext = dataModel;
        InitializeComponent();
    }

    private void Button_Refresh(object? sender, RoutedEventArgs e)
    {
        dataModel.RefreshFromDisk();
        AudioStudio_Service.Instance.PlayEditorOpen();
    }

    private void Button_GearSave(object? sender, RoutedEventArgs e)
    {
        dataModel.SaveGear();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_GearUndo(object? sender, RoutedEventArgs e)
    {
        dataModel.UndoGear();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_GearDiscard(object? sender, RoutedEventArgs e)
    {
        dataModel.DiscardGear();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_AeonSave(object? sender, RoutedEventArgs e)
    {
        dataModel.SaveAeon();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_AeonUndo(object? sender, RoutedEventArgs e)
    {
        dataModel.UndoAeon();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_AeonDiscard(object? sender, RoutedEventArgs e)
    {
        dataModel.DiscardAeon();
        AudioStudio_Service.Instance.PlayAlternative();
    }
}

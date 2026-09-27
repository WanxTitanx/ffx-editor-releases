using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules.CelestialWeaponEditor;
using FFXProjectEditor.Modules.Common;
using FFXProjectEditor.Services;

namespace FFXProjectEditor;

public partial class CelestialWeaponEditor_Control : UserControl, IRestorableModule
{
    readonly CelestialWeaponEditor_DataModel dataModel;

    public CelestialWeaponEditor_Control()
    {
        dataModel = new CelestialWeaponEditor_DataModel();
        DataContext = dataModel;
        InitializeComponent();
    }

    private void Button_Refresh(object? sender, RoutedEventArgs e)
    {
        dataModel.Refresh();
        AudioStudio_Service.Instance.PlayEditorOpen();
    }

}

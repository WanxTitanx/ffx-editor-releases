using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.FfxLib.Save;
using FFXProjectEditor.Modules.SaveEditor;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor;

public partial class SaveEditorEquipment_Control : UserControl, IRestorableModule
{
    public SaveEditorEquipment_Control() => InitializeComponent();

    void Apply_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SaveEditor_DataModel m)
            m.ApplySelectedEquipment();
    }

    void Copy_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SaveEditor_DataModel m)
            m.RunBatchAction(FfxSaveSection.Equipment, 46, m.SelectedEquipment?.SlotIndex ?? 0);
    }

    void Paste_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SaveEditor_DataModel m)
            m.RunBatchAction(FfxSaveSection.Equipment, 47, m.SelectedEquipment?.SlotIndex ?? 0);
    }
}

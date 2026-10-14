using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.FfxLib.Save;
using FFXProjectEditor.Modules.SaveEditor;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor;

public partial class SaveEditorItems_Control : UserControl, IRestorableModule
{
    public SaveEditorItems_Control() => InitializeComponent();

    void ApplyItem_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SaveEditor_DataModel m)
            m.ApplySelectedItem();
    }

    void ApplyGil_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SaveEditor_DataModel m)
            m.ApplyGil();
    }

    void ApplyKey_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SaveEditor_DataModel m)
            m.ApplyKeyItems();
    }

    void Batch99_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SaveEditor_DataModel m)
            m.RunBatchAction(FfxSaveSection.Items, 5);
    }

    void BatchKey_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SaveEditor_DataModel m)
            m.RunBatchAction(FfxSaveSection.Items, 0);
    }
}

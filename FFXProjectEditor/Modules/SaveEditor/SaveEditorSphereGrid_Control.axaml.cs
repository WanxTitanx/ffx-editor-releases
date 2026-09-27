using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.FfxLib.Save;
using FFXProjectEditor.Modules.SaveEditor;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor;

public partial class SaveEditorSphereGrid_Control : UserControl, IRestorableModule
{
    public SaveEditorSphereGrid_Control() => InitializeComponent();

    void RefreshNode_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SaveEditor_DataModel m)
            m.RefreshSphereNodeFromIndex();
    }

    void ApplyNode_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SaveEditor_DataModel m)
            m.ApplySelectedSphereNode();
    }

    void Batch_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SaveEditor_DataModel m || sender is not Button b || b.Tag is not string tag)
            return;

        if (int.TryParse(tag, out int actionId))
            m.RunBatchAction(FfxSaveSection.SphereGrid, actionId);
    }
}

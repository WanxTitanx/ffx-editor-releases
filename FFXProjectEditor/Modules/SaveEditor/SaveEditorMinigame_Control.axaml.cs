using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.FfxLib.Save;
using FFXProjectEditor.Modules.SaveEditor;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor;

public partial class SaveEditorMinigame_Control : UserControl, IRestorableModule
{
    public SaveEditorMinigame_Control() => InitializeComponent();

    void Apply_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SaveEditor_DataModel m)
            m.ApplyMinigameFields();
    }

    void Batch_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SaveEditor_DataModel m || sender is not Button b || b.Tag is not string tag)
            return;
        if (int.TryParse(tag, out int actionId))
            m.RunBatchAction(FfxSaveSection.Minigame, actionId);
    }
}

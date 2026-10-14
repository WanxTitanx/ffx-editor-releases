using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.FfxLib.Save;
using FFXProjectEditor.Modules.SaveEditor;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor;

public partial class SaveEditorCharacter_Control : UserControl, IRestorableModule
{
    public SaveEditorCharacter_Control()
    {
        InitializeComponent();
    }

    void Button_Apply(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SaveEditor_DataModel model)
            model.ApplySelectedCharacter();
    }

    void Batch_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SaveEditor_DataModel m || sender is not Button b || b.Tag is not string tag)
            return;
        if (int.TryParse(tag, out int actionId))
            m.RunBatchAction(FfxSaveSection.Character, actionId, m.SelectedCharacter?.Index ?? 0);
    }

    void BatchPerChar_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SaveEditor_DataModel m || sender is not Button b || b.Tag is not string tag)
            return;
        if (int.TryParse(tag, out int actionId))
            m.RunBatchAction(FfxSaveSection.Character, actionId, m.CharacterEditor?.SelectedCharacterIndex ?? m.SelectedCharacter?.Index ?? 0);
    }
}

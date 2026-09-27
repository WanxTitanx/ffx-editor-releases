using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.FfxLib.Save;
using FFXProjectEditor.Modules.SaveEditor;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor;

public partial class SaveEditorBlitzball_Control : UserControl, IRestorableModule
{
    public SaveEditorBlitzball_Control() => InitializeComponent();

    void Apply_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SaveEditor_DataModel m)
            m.ApplySelectedBlitzballPlayer();
    }

    void BatchLearn_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SaveEditor_DataModel m)
            m.RunBatchAction(FfxSaveSection.Blitzball, 3);
    }

    void BatchLearnFull_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SaveEditor_DataModel m)
            m.RunBatchAction(FfxSaveSection.Blitzball, 49);
    }

    void BatchMaxLv_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SaveEditor_DataModel m)
            m.RunBatchAction(FfxSaveSection.Blitzball, 48);
    }

    void BatchTechFind_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SaveEditor_DataModel m)
            m.RunBatchAction(FfxSaveSection.Blitzball, 51, m.SelectedBlitzballPlayer?.Index ?? 0);
    }

    void BatchTechFindAll_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SaveEditor_DataModel m)
            m.RunBatchAction(FfxSaveSection.Blitzball, 50);
    }

    void BatchTournament_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SaveEditor_DataModel m)
            m.RunBatchAction(FfxSaveSection.Blitzball, 52);
    }
}

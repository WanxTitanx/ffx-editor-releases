using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.Modules.MonsterAiEditor;

namespace FFXProjectEditor;

public partial class BibleOfSpiraGuide_Window : Window
{
    public BibleOfSpiraGuide_Window()
        : this(null, null, null)
    {
    }

    internal BibleOfSpiraGuide_Window(AiBibleEntry? initialEntry, string? initialSearch, string? contextSummary)
    {
        DataContext = new BibleOfSpiraGuide_DataModel(initialEntry, initialSearch, contextSummary);
        InitializeComponent();
    }

    private void Button_Close(object? sender, RoutedEventArgs e) => Close();
}

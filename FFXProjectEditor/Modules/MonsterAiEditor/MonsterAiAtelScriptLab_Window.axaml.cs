using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules.MonsterAiEditor;

namespace FFXProjectEditor;

internal partial class MonsterAiAtelScriptLab_Window : Window
{
    readonly MonsterAiEditor_DataModel dataModel;

    public MonsterAiAtelScriptLab_Window(MonsterAiEditor_DataModel dataModel)
    {
        this.dataModel = dataModel;
        DataContext = dataModel;
        InitializeComponent();
    }

    private void Button_Compile_Click(object? sender, RoutedEventArgs e)
    {
        dataModel.CompileAtelDryRun();
    }

    private void Button_Apply_Click(object? sender, RoutedEventArgs e)
    {
        dataModel.CompileAtelApply();
    }
}

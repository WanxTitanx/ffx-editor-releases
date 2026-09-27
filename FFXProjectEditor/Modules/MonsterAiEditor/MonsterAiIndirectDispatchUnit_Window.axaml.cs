using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules.MonsterAiEditor;
using FFXProjectEditor.Services;

namespace FFXProjectEditor;

public partial class MonsterAiIndirectDispatchUnit_Window : Window
{
    readonly MonsterAiEditor_DataModel dataModel;

    public MonsterAiIndirectDispatchUnit_Window()
        : this(new MonsterAiEditor_DataModel())
    {
    }

    internal MonsterAiIndirectDispatchUnit_Window(MonsterAiEditor_DataModel dataModel)
    {
        this.dataModel = dataModel;
        DataContext = dataModel.IndirectDispatchEditorVm;
        InitializeComponent();
        Closed += (_, _) => this.dataModel.ClearIndirectDispatchEditor(DataContext as AiIndirectDispatchUnitEditorVm);
    }

    private void Button_Apply(object? sender, RoutedEventArgs e)
    {
        if (dataModel.ApplyPreparedIndirectDispatchEdits(DataContext as AiIndirectDispatchUnitEditorVm))
        {
            AudioStudio_Service.Instance.PlayConfirm();
            Close();
            return;
        }

        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_Close(object? sender, RoutedEventArgs e) => Close();
}

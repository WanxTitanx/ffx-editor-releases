using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules.MonsterAiEditor;
using FFXProjectEditor.Services;

namespace FFXProjectEditor;

public partial class MonsterAiCompositeAction_Window : Window
{
    readonly MonsterAiEditor_DataModel dataModel;

    public MonsterAiCompositeAction_Window()
        : this(new MonsterAiEditor_DataModel())
    {
    }

    internal MonsterAiCompositeAction_Window(MonsterAiEditor_DataModel dataModel)
    {
        this.dataModel = dataModel;
        DataContext = dataModel;
        dataModel.PrepareCompositeActionRecipe();
        InitializeComponent();
    }

    private void Button_ApplyCompositeActionRecipe(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyCompositeActionRecipe();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_AddCompositeStep(object? sender, RoutedEventArgs e)
    {
        dataModel.AddCompositeStep();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_RemoveCompositeStep(object? sender, RoutedEventArgs e)
    {
        dataModel.RemoveSelectedCompositeStep();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_Close(object? sender, RoutedEventArgs e) => Close();
}

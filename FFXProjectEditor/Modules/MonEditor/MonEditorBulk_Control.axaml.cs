using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules.MonEditor;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor;

public partial class MonEditorBulk_Control : UserControl, IRestorableModule
{
    readonly MonEditorBulk_DataModel dataModel;

    public MonEditorBulk_Control(MonEditorSelector_DataModel selectorDM)
    {
        dataModel = new MonEditorBulk_DataModel(selectorDM);
        DataContext = dataModel;
        InitializeComponent();
    }

    private void Button_ApplyAbilitySlot(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyPendingAbilityToSelected();
    }

    private void Button_ClearAbilitySlot(object? sender, RoutedEventArgs e)
    {
        dataModel.ClearPendingAbilitySlotForSelected();
    }

    private void Button_ApplySourceBlocks(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplySourceBlocksToSelected();
    }
}

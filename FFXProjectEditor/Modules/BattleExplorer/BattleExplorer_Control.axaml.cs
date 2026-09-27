using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules.BattleExplorer;
using FFXProjectEditor.Services;
using System;
using static FFXProjectEditor.Modules.BattleExplorer.BattleExplorer_DataModel;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor;

public partial class BattleExplorer_Control : UserControl, IRestorableModule
{
    BattleExplorer_DataModel DataModel;
    public event Action<int>? OpenMonsterRequested;

    public BattleExplorer_Control()
    {
        DataModel = new BattleExplorer_DataModel();
        DataContext = DataModel;
        InitializeComponent();
    }

    private void Filter_Changed(object? sender, Avalonia.Controls.TextChangedEventArgs e)
    {
        DataModel.ApplyFilter();
    }

    private void Button_Refresh(object? sender, RoutedEventArgs e)
    {
        DataModel.Refresh();
        AudioStudio_Service.Instance.PlayNavigation();
    }

    private void Button_RefreshRuntimeProbe(object? sender, RoutedEventArgs e)
    {
        DataModel.RefreshRuntimeProbe();
        AudioStudio_Service.Instance.PlayEditorOpen();
    }

    private void Button_ResetFormation(object? sender, RoutedEventArgs e)
    {
        DataModel.ResetSelectedFormation();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_UndoFormation(object? sender, RoutedEventArgs e)
    {
        DataModel.UndoSelectedFormationEdit();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_SaveFormation(object? sender, RoutedEventArgs e)
    {
        DataModel.SaveSelectedFormation();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_ClearFormationSlot(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is EditableFormationSlotRow row)
        {
            DataModel.ClearFormationSlot(row);
            AudioStudio_Service.Instance.PlayNavigation();
        }
    }

    private void Button_OpenFormationMonster(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is EditableFormationSlotRow row && row.CanOpenSelectedMonster)
        {
            OpenMonsterRequested?.Invoke(row.SelectedMonsterIndex);
            AudioStudio_Service.Instance.PlayConfirm();
        }
    }

    private void Button_AddMonsterToFirstEmptySlot(object? sender, RoutedEventArgs e)
    {
        DataModel.AddSelectedMonsterToFirstEmptySlot();
        AudioStudio_Service.Instance.PlayNavigation();
    }

    private void Button_CopyFormation(object? sender, RoutedEventArgs e)
    {
        DataModel.CopyCurrentFormationToClipboard();
        AudioStudio_Service.Instance.PlayNavigation();
    }

    private void Button_PasteFormation(object? sender, RoutedEventArgs e)
    {
        DataModel.PasteFormationFromClipboard();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_CloneFormation(object? sender, RoutedEventArgs e)
    {
        DataModel.CloneFormationFromSelectedBattle();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_FillEmptyFormationSlots(object? sender, RoutedEventArgs e)
    {
        DataModel.FillEmptyFormationSlots();
        AudioStudio_Service.Instance.PlayNavigation();
    }

    private void ListBox_SelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
        DataModel.LoadBattle((BattleListEntry?)BattleList.SelectedItem);
    }
}

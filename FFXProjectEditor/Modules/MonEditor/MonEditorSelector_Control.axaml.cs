using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules.MonEditor;
using FFXProjectEditor.Services;
using static FFXProjectEditor.Modules.MonEditor.MonEditorSelector_DataModel;
using System;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor;

public partial class MonEditorSelector_Control : UserControl, IRestorableModule
{
    MonEditorSelector_DataModel DataModel;
    DateTime _suppressSelectionFxUntilUtc = DateTime.MinValue;

    public MonEditorSelector_Control() : this(null)
    {
    }

    public MonEditorSelector_Control(int? initialMonsterIndex)
    {
        DataModel = new MonEditorSelector_DataModel();
        DataContext = DataModel;
        InitializeComponent();

        if (initialMonsterIndex.HasValue)
        {
            SelectMonster(initialMonsterIndex.Value);
        }
    }

    private void ListBox_SelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (DateTime.UtcNow >= _suppressSelectionFxUntilUtc)
        {
            AudioStudio_Service.Instance.PlayConfirm();
        }

        DataModel.LoadMonster(MonsterList.SelectedItem as MonsterListEntry, ContentFrame);
    }

    private void Filter_Changed(object? sender, Avalonia.Controls.TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            DataModel.FilterText = textBox.Text ?? string.Empty;
        }

        DataModel.ApplyFilter();
    }

    // Handlers removed due to UI redesign

    public void SelectMonster(int monsterIndex)
    {
        MonEditorSelector_DataModel.MonsterListEntry? entry = DataModel.FindMonster(monsterIndex);
        if (entry == null)
        {
            return;
        }

        MonsterList.SelectedItem = entry;
        MonsterList.ScrollIntoView(entry);
        DataModel.LoadMonster(entry, ContentFrame);
    }

    public int? GetSelectedMonsterIndex()
    {
        return (MonsterList.SelectedItem as MonsterListEntry)?.Index;
    }
}

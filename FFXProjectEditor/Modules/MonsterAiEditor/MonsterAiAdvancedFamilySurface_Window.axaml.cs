using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.Modules.MonsterAiEditor;
using FFXProjectEditor.Services;
using System.Collections.ObjectModel;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor;

public partial class MonsterAiAdvancedFamilySurface_Window : Window
{
    readonly MonsterAiEditor_DataModel dataModel;
    readonly AiAdvancedFamilySurfaceDialogVm editorVm;

    public MonsterAiAdvancedFamilySurface_Window()
        : this(
            new MonsterAiEditor_DataModel(),
            new AiAdvancedFamilySurfaceDialogSnapshot(
                AdvancedFamilySurfaceKeys.ElementalCluster,
                "Design-time family surface",
                "MONSTER AI - DESIGN-TIME",
                "Design-time family summary",
                "Design-time honest summary.",
                "Design-time coverage.",
                "Design-time apply summary.",
                new[]
                {
                    new AiAdvancedFamilySurfaceRowVm(
                        Strings.U_Ai_ExampleBeat,
                        Strings.U_Ai_GuardPayloadSummarized,
                        "detail summary design-time",
                        "0x0000",
                        Strings.U_Ai_OpenNarrowPopup,
                        null),
                }))
    {
    }

    internal MonsterAiAdvancedFamilySurface_Window(
        MonsterAiEditor_DataModel dataModel,
        AiAdvancedFamilySurfaceDialogSnapshot snapshot)
    {
        this.dataModel = dataModel;
        editorVm = new AiAdvancedFamilySurfaceDialogVm(dataModel, snapshot);
        DataContext = editorVm;
        Title = editorVm.WindowTitle;
        InitializeComponent();
    }

    private void Button_OpenFamilyRowEditor(object? sender, RoutedEventArgs e)
    {
        AiIndirectDispatchEditorLaunchContext? focusContext = (sender as Button)?.Tag as AiIndirectDispatchEditorLaunchContext;
        if (focusContext == null)
        {
            AudioStudio_Service.Instance.PlayAlternative();
            return;
        }

        if (string.IsNullOrWhiteSpace(focusContext.UnitId)
            || !dataModel.PrepareIndirectDispatchEditor(focusContext.UnitId, focusContext))
        {
            AudioStudio_Service.Instance.PlayAlternative();
            return;
        }

        Window childWindow = focusContext.FocusKind switch
        {
            AiIndirectDispatchEditorFocusKind.CommandSlot => new MonsterAiIndirectDispatchPayload_Window(dataModel),
            AiIndirectDispatchEditorFocusKind.TargetSlot => new MonsterAiIndirectDispatchTarget_Window(dataModel),
            AiIndirectDispatchEditorFocusKind.NextState => new MonsterAiIndirectDispatchNextState_Window(dataModel),
            _ => new MonsterAiIndirectDispatchUnit_Window(dataModel),
        };

        childWindow.Closed += (_, _) =>
        {
            editorVm.Refresh();
            Title = editorVm.WindowTitle;
        };

        if (TopLevel.GetTopLevel(this) is Window owner)
            childWindow.Show(owner);
        else
            childWindow.Show();

        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_Close(object? sender, RoutedEventArgs e) => Close();
}

internal sealed partial class AiAdvancedFamilySurfaceDialogVm : ObservableObject
{
    readonly MonsterAiEditor_DataModel dataModel;

    public AiAdvancedFamilySurfaceDialogVm(
        MonsterAiEditor_DataModel dataModel,
        AiAdvancedFamilySurfaceDialogSnapshot snapshot)
    {
        this.dataModel = dataModel;
        Rows = new ObservableCollection<AiAdvancedFamilySurfaceRowVm>();
        ApplySnapshot(snapshot);
    }

    public ObservableCollection<AiAdvancedFamilySurfaceRowVm> Rows { get; }
    public string FamilyKey { get; private set; } = string.Empty;

    [ObservableProperty] private string windowTitle = string.Empty;
    [ObservableProperty] private string headerLabel = string.Empty;
    [ObservableProperty] private string summary = string.Empty;
    [ObservableProperty] private string honestSummary = string.Empty;
    [ObservableProperty] private string coverage = string.Empty;
    [ObservableProperty] private string applySummary = string.Empty;

    public void Refresh()
    {
        if (!dataModel.TryBuildAdvancedFamilySurfaceDialog(FamilyKey, out AiAdvancedFamilySurfaceDialogSnapshot? snapshot)
            || snapshot == null)
        {
            return;
        }

        ApplySnapshot(snapshot);
    }

    void ApplySnapshot(AiAdvancedFamilySurfaceDialogSnapshot snapshot)
    {
        FamilyKey = snapshot.FamilyKey;
        WindowTitle = snapshot.WindowTitle;
        HeaderLabel = snapshot.HeaderLabel;
        Summary = snapshot.Summary;
        HonestSummary = snapshot.HonestSummary;
        Coverage = snapshot.Coverage;
        ApplySummary = snapshot.ApplySummary;

        Rows.Clear();
        foreach (AiAdvancedFamilySurfaceRowVm row in snapshot.Rows)
            Rows.Add(row);
    }
}

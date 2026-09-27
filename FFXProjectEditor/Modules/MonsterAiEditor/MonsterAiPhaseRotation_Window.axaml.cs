using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules.MonsterAiEditor;
using FFXProjectEditor.Services;

namespace FFXProjectEditor;

public partial class MonsterAiPhaseRotation_Window : Window
{
    readonly MonsterAiEditor_DataModel dataModel;

    public MonsterAiPhaseRotation_Window()
        : this(new MonsterAiEditor_DataModel())
    {
    }

    internal MonsterAiPhaseRotation_Window(MonsterAiEditor_DataModel dataModel)
    {
        this.dataModel = dataModel;
        DataContext = dataModel;
        dataModel.PrepareCommonPhaseRotationManager();
        InitializeComponent();
    }

    private void Button_RefreshPhaseRotationAudit(object? sender, RoutedEventArgs e)
    {
        dataModel.RefreshPhaseRotationAudit();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_SavePhaseVarAlias(object? sender, RoutedEventArgs e)
    {
        dataModel.SaveSelectedPhaseVarAlias();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_ClearPhaseVarAlias(object? sender, RoutedEventArgs e)
    {
        dataModel.ClearSelectedPhaseVarAlias();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_SavePhaseVarAuthoringIndex(object? sender, RoutedEventArgs e)
    {
        dataModel.SaveSelectedPhaseVarAuthoringIndex();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_ClearPhaseVarAuthoringIndex(object? sender, RoutedEventArgs e)
    {
        dataModel.ClearSelectedPhaseVarAuthoringIndex();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_CreateFreePrivateVarLab(object? sender, RoutedEventArgs e)
    {
        dataModel.CreateFreePrivateVarLab();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_AddPhaseDraftStep(object? sender, RoutedEventArgs e)
    {
        dataModel.AddPhaseDraftStep();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_AddPhaseFinalDraftStep(object? sender, RoutedEventArgs e)
    {
        dataModel.AddPhaseFinalDraftStep();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_RemovePhaseDraftStep(object? sender, RoutedEventArgs e)
    {
        dataModel.RemovePhaseDraftStep();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_AddPhaseVarAdvance(object? sender, RoutedEventArgs e)
    {
        dataModel.AddVarAdvanceToSelectedPhase();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_AddPhaseVarReset(object? sender, RoutedEventArgs e)
    {
        dataModel.AddVarResetToSelectedPhase();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_RemovePhaseVarAdvance(object? sender, RoutedEventArgs e)
    {
        dataModel.RemoveSelectedPhaseVarAdvance();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_ValidatePhaseMultivarCondition(object? sender, RoutedEventArgs e)
    {
        dataModel.ValidatePhaseMultivarCondition();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_AddPhaseConditionClause(object? sender, RoutedEventArgs e)
    {
        dataModel.AddPhaseConditionClause();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_RemovePhaseConditionClause(object? sender, RoutedEventArgs e)
    {
        dataModel.RemoveSelectedPhaseConditionClause();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_ClearPhaseConditionClauses(object? sender, RoutedEventArgs e)
    {
        dataModel.ClearPhaseConditionClauses();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_AssignPhaseConditionToSelectedStep(object? sender, RoutedEventArgs e)
    {
        dataModel.AssignPhaseConditionToSelectedStep();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_ApplySimplePhaseCondition(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplySimpleConditionToSelectedPhaseStep();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_ClearSimplePhaseCondition(object? sender, RoutedEventArgs e)
    {
        dataModel.ClearConditionFromSelectedPhaseStep();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_ApplyPhaseRotationDraft(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyPhaseRotationDraft();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_Close(object? sender, RoutedEventArgs e) => Close();
}

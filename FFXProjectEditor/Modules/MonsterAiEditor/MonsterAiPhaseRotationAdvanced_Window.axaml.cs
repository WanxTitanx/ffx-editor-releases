using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using FFXProjectEditor.Modules.MonsterAiEditor;
using FFXProjectEditor.Services;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor;

public partial class MonsterAiPhaseRotationAdvanced_Window : Window
{
    readonly MonsterAiEditor_DataModel dataModel;

    public MonsterAiPhaseRotationAdvanced_Window()
        : this(new MonsterAiEditor_DataModel())
    {
    }

    internal MonsterAiPhaseRotationAdvanced_Window(MonsterAiEditor_DataModel dataModel)
    {
        this.dataModel = dataModel;
        DataContext = dataModel;
        dataModel.InitializeAdvancedWorkspace();
        dataModel.PrepareAdvancedPhaseRotationManager();
        InitializeComponent();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                dataModel.AdvancedWorkspaceTabIndex = 0;
                this.FindControl<TextBox>("AdvancedRouteSearch")?.Focus();
                e.Handled = true;
            }
            if (e.Key == Key.Escape && !e.Handled) { Close(); e.Handled = true; }
        };
    }

    private void Button_OpenAdvancedNativeCondition(object? sender, RoutedEventArgs e)
        => dataModel.OpenSelectedAdvancedNativeCondition();

    private void Button_ApplyAdvancedNativeCondition(object? sender, RoutedEventArgs e)
    {
        if (dataModel.ApplyAdvancedNativeCondition())
            AudioStudio_Service.Instance.PlayConfirm();
        else
            AudioStudio_Service.Instance.PlayAlternative();
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
        dataModel.AssignPhaseConditionToAdvancedDraft();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_AddAdvancedCommonPhaseDraft(object? sender, RoutedEventArgs e)
    {
        dataModel.AddAdvancedCommonPhaseDraft();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_AddAdvancedFinalPhaseDraft(object? sender, RoutedEventArgs e)
    {
        dataModel.AddAdvancedFinalPhaseDraft();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_RemoveAdvancedSelectedPhaseDraft(object? sender, RoutedEventArgs e)
    {
        dataModel.RemoveAdvancedSelectedPhaseDraft();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_ApplyAdvancedRouteForbiddenRite(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyAdvancedRouteForbiddenRite();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_ApplyAdvancedRouteSecondCast(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyAdvancedRouteSecondCast();
        AudioStudio_Service.Instance.PlayConfirm();
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

    private void Button_ApplyAdvancedFluxThreshold(object? sender, RoutedEventArgs e)
    {
        if (dataModel.ApplyAdvancedFluxThresholdEdit((sender as Button)?.Tag as AiAdvancedFluxThresholdEvidenceVm))
        {
            AudioStudio_Service.Instance.PlayConfirm();
            return;
        }

        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_OpenAdvancedFluxThresholdEditor(object? sender, RoutedEventArgs e)
    {
        AiAdvancedFluxThresholdEvidenceVm? row = (sender as Button)?.Tag as AiAdvancedFluxThresholdEvidenceVm;
        if (row == null)
        {
            AudioStudio_Service.Instance.PlayAlternative();
            return;
        }

        ShowIndirectDispatchWindow(new MonsterAiAdvancedFluxThreshold_Window(dataModel, row));
    }

    private void Button_OpenAdvancedCompanionActivationEditor(object? sender, RoutedEventArgs e)
    {
        AiAdvancedCompanionActivationPackageVm? package =
            (sender as Button)?.Tag as AiAdvancedCompanionActivationPackageVm
            ?? dataModel.SelectedAdvancedCompanionActivationPackage;
        if (package == null)
        {
            AudioStudio_Service.Instance.PlayAlternative();
            return;
        }

        ShowIndirectDispatchWindow(new MonsterAiAdvancedCompanionActivation_Window(dataModel, package));
    }

    private void Button_OpenAdvancedFamilySurfaceEditor(object? sender, RoutedEventArgs e)
    {
        string? familyKey = (sender as Button)?.Tag as string;
        if (!TryOpenAdvancedFamilySurfaceEditor(familyKey))
            AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_SeedPhaseConditionFromAdvancedFocus(object? sender, RoutedEventArgs e)
    {
        dataModel.SeedPhaseConditionFromAdvancedFocus();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_OpenPhaseVariableIndirectDispatchEditor(object? sender, RoutedEventArgs e)
    {
        AiPhaseVarIndirectDispatchLinkVm? unit = (sender as Button)?.Tag as AiPhaseVarIndirectDispatchLinkVm;
        if (unit == null)
        {
            AudioStudio_Service.Instance.PlayAlternative();
            return;
        }

        OpenIndirectDispatchEditor(unit.UnitId);
    }

    private void Button_OpenAdvancedPhaseUnitEditor(object? sender, RoutedEventArgs e)
    {
        object? tag = (sender as Button)?.Tag;
        if (tag is AiIndirectDispatchEditorLaunchContext focusContext)
        {
            switch (focusContext.FocusKind)
            {
                case AiIndirectDispatchEditorFocusKind.CommandSlot:
                    OpenIndirectDispatchPayloadEditor(focusContext);
                    return;
                case AiIndirectDispatchEditorFocusKind.TargetSlot:
                    OpenIndirectDispatchTargetEditor(focusContext);
                    return;
                case AiIndirectDispatchEditorFocusKind.NextState:
                    OpenIndirectDispatchNextStateEditor(focusContext);
                    return;
                default:
                    OpenIndirectDispatchEditor(focusContext);
                    return;
            }
        }

        OpenIndirectDispatchEditor(tag as string);
    }

    private void Button_CloneAdvancedPhaseUnitRoute(object? sender, RoutedEventArgs e)
    {
        bool applied = dataModel.CloneSelectedAdvancedPhaseUnitRoute();
        dataModel.AdvancedWorkspaceFeedback = dataModel.AdvancedPhaseUnitsSummary;
        if (applied)
        {
            AudioStudio_Service.Instance.PlayConfirm();
            return;
        }

        AudioStudio_Service.Instance.PlayAlternative();
    }

    void OpenIndirectDispatchEditor(string? unitId)
    {
        dataModel.ClearActiveFocus();

        if (TryOpenAdvancedFamilySurfaceEditorFromUnit(unitId))
            return;

        if (string.IsNullOrWhiteSpace(unitId) || !dataModel.PrepareIndirectDispatchEditor(unitId))
        {
            AudioStudio_Service.Instance.PlayAlternative();
            return;
        }

        ShowIndirectDispatchWindow(new MonsterAiIndirectDispatchUnit_Window(dataModel));
    }

    void OpenIndirectDispatchEditor(AiIndirectDispatchEditorLaunchContext focusContext)
    {
        if (!TryPrepareFocusedIndirectDispatchEditor(focusContext))
            return;

        ShowIndirectDispatchWindow(new MonsterAiIndirectDispatchUnit_Window(dataModel));
    }

    void OpenIndirectDispatchPayloadEditor(AiIndirectDispatchEditorLaunchContext focusContext)
    {
        if (!TryPrepareFocusedIndirectDispatchEditor(focusContext))
            return;

        ShowIndirectDispatchWindow(new MonsterAiIndirectDispatchPayload_Window(dataModel));
    }

    void OpenIndirectDispatchTargetEditor(AiIndirectDispatchEditorLaunchContext focusContext)
    {
        if (!TryPrepareFocusedIndirectDispatchEditor(focusContext))
            return;

        ShowIndirectDispatchWindow(new MonsterAiIndirectDispatchTarget_Window(dataModel));
    }

    void OpenIndirectDispatchNextStateEditor(AiIndirectDispatchEditorLaunchContext focusContext)
    {
        if (!TryPrepareFocusedIndirectDispatchEditor(focusContext))
            return;

        ShowIndirectDispatchWindow(new MonsterAiIndirectDispatchNextState_Window(dataModel));
    }

    bool TryPrepareFocusedIndirectDispatchEditor(AiIndirectDispatchEditorLaunchContext focusContext)
    {
        if (string.IsNullOrWhiteSpace(focusContext.UnitId)
            || !dataModel.PrepareIndirectDispatchEditor(focusContext.UnitId, focusContext))
        {
            AudioStudio_Service.Instance.PlayAlternative();
            return false;
        }

        dataModel.RememberActiveFocus(focusContext);
        return true;
    }

    private void Button_ClearAdvancedFocus(object? sender, RoutedEventArgs e)
    {
        dataModel.ClearActiveFocus();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    bool TryOpenAdvancedFamilySurfaceEditor(string? familyKey)
    {
        if (string.IsNullOrWhiteSpace(familyKey)
            || !dataModel.TryBuildAdvancedFamilySurfaceDialog(familyKey, out AiAdvancedFamilySurfaceDialogSnapshot? snapshot)
            || snapshot == null)
        {
            return false;
        }

        ShowIndirectDispatchWindow(new MonsterAiAdvancedFamilySurface_Window(dataModel, snapshot));
        return true;
    }

    bool TryOpenAdvancedFamilySurfaceEditorFromUnit(string? unitId)
    {
        if (string.IsNullOrWhiteSpace(unitId)
            || !dataModel.TryBuildAdvancedFamilySurfaceDialogForUnit(unitId, out AiAdvancedFamilySurfaceDialogSnapshot? snapshot)
            || snapshot == null)
        {
            return false;
        }

        ShowIndirectDispatchWindow(new MonsterAiAdvancedFamilySurface_Window(dataModel, snapshot));
        return true;
    }

    void ShowIndirectDispatchWindow(Window window)
    {
        if (TopLevel.GetTopLevel(this) is Window owner)
            _ = window.ShowDialog(owner);
        else
            window.Show();

        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_ApplyAdvancedSeymourHandoff(object? sender, RoutedEventArgs e)
    {
        if (dataModel.ApplyAdvancedSeymourHandoff((sender as Button)?.Tag as AiAdvancedSeymourHandoffRow))
            AudioStudio_Service.Instance.PlayConfirm();
        else
            AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_ApplyAdvancedExpression(object? sender, RoutedEventArgs e)
    {
        if (dataModel.ApplyAdvancedExpression())
            AudioStudio_Service.Instance.PlayConfirm();
        else
            AudioStudio_Service.Instance.PlayAlternative();
    }

    private async void Button_LoadAdvancedSceneEncounter(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await Utils.AvaloniaDialog_Util.OpenFileDialog(this, Strings.AiAdvancedSceneFile,
                fileTypeFilter: new() { new Avalonia.Platform.Storage.FilePickerFileType(Strings.AiAdvancedSceneFile) { Patterns = new[] { "*.bin" } } });
            if (files.Count > 0) dataModel.LoadAdvancedSceneEncounter(files[0]);
        }
        catch (System.Exception ex) { dataModel.AdvancedBattleSceneStatus = ex.Message; }
    }

    private void Button_ApplyAdvancedBattleScene(object? sender, RoutedEventArgs e)
    {
        if (dataModel.ApplyAdvancedBattleScene())
            AudioStudio_Service.Instance.PlayConfirm();
        else
            AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_ApplyAdvancedAnimaOdThreshold(object? sender, RoutedEventArgs e)
    {
        if (dataModel.ApplyAdvancedAnimaOdThresholdEdit((sender as Button)?.Tag as AiAdvancedAnimaOdThresholdEvidenceVm))
        {
            AudioStudio_Service.Instance.PlayConfirm();
            return;
        }
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_ApplyAdvancedOmnisCluster(object? sender, RoutedEventArgs e)
    {
        if (dataModel.ApplyAdvancedOmnisClusterEdit((sender as Button)?.Tag as AiAdvancedOmnisClusterEvidenceVm))
        {
            AudioStudio_Service.Instance.PlayConfirm();
            return;
        }
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_ApplyAdvancedMortibodyAccumulator(object? sender, RoutedEventArgs e)
    {
        if (dataModel.ApplyAdvancedMortibodyAccumulatorEdit((sender as Button)?.Tag as AiAdvancedMortibodyAccumulatorEvidenceVm))
        {
            AudioStudio_Service.Instance.PlayConfirm();
            return;
        }
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_ApplyAdvancedMortiorchisCompanion(object? sender, RoutedEventArgs e)
    {
        if (dataModel.ApplyAdvancedMortiorchisCompanionEdit((sender as Button)?.Tag as AiAdvancedMortiorchisCompanionEvidenceVm))
        {
            AudioStudio_Service.Instance.PlayConfirm();
            return;
        }
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_ApplyAdvancedReactiveSensor(object? sender, RoutedEventArgs e)
    {
        if (dataModel.ApplyAdvancedReactiveSensorEdit((sender as Button)?.Tag as AiAdvancedReactiveSensorEvidenceVm))
        {
            AudioStudio_Service.Instance.PlayConfirm();
            return;
        }
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_ApplyAdvancedRoundScriptedBoss(object? sender, RoutedEventArgs e)
    {
        if (dataModel.ApplyAdvancedRoundScriptedBossEdit((sender as Button)?.Tag as AiAdvancedRoundScriptedBossEvidenceVm))
        {
            AudioStudio_Service.Instance.PlayConfirm();
            return;
        }
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_Close(object? sender, RoutedEventArgs e) => Close();
}

// DESCONTINUADO / NAO EDITAR (2026-06-13):
// Monster AI Editor 2 foi 100% descontinuado por decisao do usuario.
// Nao adicionar features, fixes ou refactors aqui. Use o Monster AI Editor principal.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules.MonsterAiEditor;
using FFXProjectEditor.Services;
using System;

namespace FFXProjectEditor;

public partial class MonsterAiEditor2_Control : UserControl
{
    readonly MonsterAiEditor_DataModel dataModel;
    MonsterAiActionDragPayload? actionDragPayload;
    Control? actionDragHandle;
    Point actionDragStartPoint;
    bool actionDragStarted;

    public MonsterAiEditor2_Control()
    {
        dataModel = new MonsterAiEditor_DataModel();
        dataModel.SetHumanMode(MonsterAiHumanMode.Normal);
        DataContext = dataModel;
        InitializeComponent();
    }

    private void Button_Refresh(object? sender, RoutedEventArgs e)
    {
        dataModel.RefreshFromDisk();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_RefreshRuntime(object? sender, RoutedEventArgs e)
    {
        dataModel.RefreshRuntimeTargets();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_LoadAiDiff(object? sender, RoutedEventArgs e)
    {
        dataModel.LoadAiDiff();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_OpenBibleGuide(object? sender, RoutedEventArgs e)
    {
        BibleOfSpiraGuide_Window window = new(dataModel.SelectedBibleEntry, dataModel.BibleSearchText, dataModel.BibleContextSummary);
        if (TopLevel.GetTopLevel(this) is Window owner)
            window.Show(owner);
        else
            window.Show();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_RestoreVanillaDiff(object? sender, RoutedEventArgs e)
    {
        dataModel.RequestRestoreVanillaFromDiff();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_Save(object? sender, RoutedEventArgs e)
    {
        dataModel.Save();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_InsertAsm(object? sender, RoutedEventArgs e)
    {
        dataModel.InsertAssemblerInstruction();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_RemoveAsm(object? sender, RoutedEventArgs e)
    {
        dataModel.RemoveSelectedAssemblerRow();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_SaveAsm(object? sender, RoutedEventArgs e)
    {
        dataModel.SaveAssembler();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_ValidateAsm(object? sender, RoutedEventArgs e)
    {
        dataModel.ValidateAssembler();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_AutoFixAsm(object? sender, RoutedEventArgs e)
    {
        dataModel.TryAutoFixAssembler();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_InsertTemplate(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyOrInsertTemplate();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_ApplyBehaviorTemplate(object? sender, RoutedEventArgs e)
    {
        BehaviorTemplateVm? template = (sender as Button)?.Tag as BehaviorTemplateVm;
        dataModel.ApplyBehaviorTemplate(template);
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_RemoveAction(object? sender, RoutedEventArgs e)
    {
        dataModel.RemoveActionOrBatch();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_MoveActionUp(object? sender, RoutedEventArgs e)
    {
        dataModel.MoveSelectedAction(up: true);
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_MoveActionDown(object? sender, RoutedEventArgs e)
    {
        dataModel.MoveSelectedAction(up: false);
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_MoveBatchUp(object? sender, RoutedEventArgs e)
    {
        dataModel.MoveBatchSelectionUp();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_MoveBatchDown(object? sender, RoutedEventArgs e)
    {
        dataModel.MoveBatchSelectionDown();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_ClearBatchSelection(object? sender, RoutedEventArgs e)
    {
        dataModel.ClearBatchSelection();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_MoveActionToHook(object? sender, RoutedEventArgs e)
    {
        dataModel.MoveActionOrBatchToHook();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_ChangeAction(object? sender, RoutedEventArgs e)
    {
        dataModel.ChangeActionOrBatchToPickedAbility();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_DuplicateAction(object? sender, RoutedEventArgs e)
    {
        dataModel.DuplicateActionOrBatch();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_ToggleForce(object? sender, RoutedEventArgs e)
    {
        dataModel.ToggleForceActionOrBatch();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_AddAbility(object? sender, RoutedEventArgs e)
    {
        dataModel.AddAbility();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_InsertSecond(object? sender, RoutedEventArgs e)
    {
        dataModel.InsertSecondAbilityOrBatch();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_ChangeTarget(object? sender, RoutedEventArgs e)
    {
        dataModel.ChangeSelectedActionTarget();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_EditBuffStat(object? sender, RoutedEventArgs e)
    {
        dataModel.EditSelectedBuffStat();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_AddSelfBuff(object? sender, RoutedEventArgs e)
    {
        dataModel.AddSelfBuffAction();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_AddForbiddenStatusLab(object? sender, RoutedEventArgs e)
    {
        dataModel.AddForbiddenStatusLabAction();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_AddSetStatField(object? sender, RoutedEventArgs e)
    {
        dataModel.AddSetStatFieldAction();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_RestoreBackup(object? sender, RoutedEventArgs e)
    {
        dataModel.RestoreBackup();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_PresetHaste(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyPresetHaste();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_PresetProtect(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyPresetProtect();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_PresetShell(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyPresetShell();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_PresetReflect(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyPresetReflect();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_PresetRegen(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyPresetRegen();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_PresetNulAll(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyPresetNulAll();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_PresetDefensive(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyPresetDefensive();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_PresetAggressive(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyPresetAggressive();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_PresetEnrage(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyPresetEnrage();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_PresetAlternate(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyPresetAlternate();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_PresetAlternate3(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyPresetAlternate3();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_PresetDuplicate(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyPresetDuplicate();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_PresetMultiCast(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyPresetMultiCast();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_CopyAi(object? sender, RoutedEventArgs e)
    {
        dataModel.CopyAiFromSource();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_RestoreOriginalAi(object? sender, RoutedEventArgs e)
    {
        dataModel.RestoreOriginalAi();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_ApplyLive(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyLivePatch();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_RestoreLive(object? sender, RoutedEventArgs e)
    {
        dataModel.RestoreLivePatch();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void ActionCard_DragHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not AiActionVm vm)
            return;

        PointerPoint point = e.GetCurrentPoint(control);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        actionDragPayload = dataModel.CreateAutomationActionDragPayload(vm);
        if (actionDragPayload == null)
            return;

        actionDragHandle = control;
        actionDragStartPoint = point.Position;
        actionDragStarted = false;
        e.Pointer.Capture(control);
        e.Handled = true;
    }

    private async void ActionCard_DragHandlePointerMoved(object? sender, PointerEventArgs e)
    {
        if (actionDragPayload == null || actionDragStarted || actionDragHandle == null)
            return;

        PointerPoint point = e.GetCurrentPoint(actionDragHandle);
        if (!point.Properties.IsLeftButtonPressed)
        {
            ResetActionDrag(e.Pointer);
            return;
        }

        double dx = point.Position.X - actionDragStartPoint.X;
        double dy = point.Position.Y - actionDragStartPoint.Y;
        if (Math.Abs(dx) < 4 && Math.Abs(dy) < 4)
            return;

        actionDragStarted = true;
        var data = new DataObject();
        data.Set(DataFormats.Text, "ffx/monster-ai-action");
        data.Set("ffx/monster-ai-action", actionDragPayload);

        try
        {
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
        }
        finally
        {
            ResetActionDrag(e.Pointer);
        }
    }

    private void ActionCard_DragHandlePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!actionDragStarted)
            ResetActionDrag(e.Pointer);
    }

    private void ResetActionDrag(IPointer? pointer)
    {
        pointer?.Capture(null);
        actionDragPayload = null;
        actionDragHandle = null;
        actionDragStarted = false;
    }

    private void ActionCard_DragOver(object? sender, DragEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not AiActionVm target)
            return;

        var placement = e.GetPosition(control).Y < control.Bounds.Height / 2
            ? MonsterAiActionDropPlacement.Before
            : MonsterAiActionDropPlacement.After;
        var payload = e.Data.Get("ffx/monster-ai-action") as MonsterAiActionDragPayload ?? actionDragPayload;
        MonsterAiActionDropResult preview = dataModel.PreviewAutomationActionDrop(payload, target, placement);
        e.DragEffects = preview.Accepted && !preview.IsNoOp ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void ActionCard_Drop(object? sender, DragEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not AiActionVm target)
            return;

        var placement = e.GetPosition(control).Y < control.Bounds.Height / 2
            ? MonsterAiActionDropPlacement.Before
            : MonsterAiActionDropPlacement.After;
        var payload = e.Data.Get("ffx/monster-ai-action") as MonsterAiActionDragPayload ?? actionDragPayload;
        MonsterAiActionDropResult result = dataModel.DropAutomationActions(payload, target, placement);
        actionDragPayload = null;
        if (result.Saved)
            AudioStudio_Service.Instance.PlayConfirm();
        else
            AudioStudio_Service.Instance.PlayAlternative();
        e.Handled = true;
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.FfxLib.Ai.Sin;
using FFXProjectEditor.Modules.MonsterAiEditor;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils;

using FFXProjectEditor.Modules.Common;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.VisualTree;
namespace FFXProjectEditor;

public partial class MonsterAiEditor_Control : UserControl, IRestorableModule
{
    readonly MonsterAiEditor_DataModel dataModel;

    /// <summary>F5-L4: abre o painel de script ATEL de alto nível (dry-run sobre o monstro selecionado).</summary>
    private void Button_AtelScriptLab(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var win = new MonsterAiAtelScriptLab_Window(dataModel);
        win.Show();
    }

    public MonsterAiEditor_Control()
        : this(MonsterAiHumanMode.DevKit)
    {
    }

    public static MonsterAiEditor_Control CreateHumanMode() => new(MonsterAiHumanMode.Normal);

    private MonsterAiEditor_Control(MonsterAiHumanMode initialMode)
    {
        dataModel = new MonsterAiEditor_DataModel();
        dataModel.SetHumanMode(initialMode);
        DataContext = dataModel;
        InitializeComponent();
        ConfigureCapabilityBadge();
    }

    private void ConfigureCapabilityBadge()
    {
        AiCapabilityBadge.SetCapability(new FFXProjectEditor.Core.CapabilityDescriptor
        {
            Id = "atel-recipe-phase-rotation",
            Domain = "MonsterAI",
            Title = "Monster AI & Phase Rotation",
            Description = Strings.U_Ai_CapabilityDescription,
            Mode = FFXProjectEditor.Core.CapabilityMode.OfflineWriter,
            Evidence = FFXProjectEditor.Core.EvidenceLevel.Partial,
            Platforms = new[] { FFXProjectEditor.Core.Platform.PC },
            RequiredDependencies = System.Array.Empty<string>(),
            OptionalDependencies = System.Array.Empty<string>(),
            Risks = System.Array.Empty<string>(),
            AllowedOperations = new[] { FFXProjectEditor.Core.AllowedOperation.Read, FFXProjectEditor.Core.AllowedOperation.Edit },
            ProhibitedOperations = System.Array.Empty<FFXProjectEditor.Core.AllowedOperation>(),
            Preconditions = System.Array.Empty<string>(),
            DocumentationLinks = System.Array.Empty<string>(),
            OwnerAgent = "Jarvis"
        });
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
        if (dataModel.Save()) AudioStudio_Service.Instance.PlayConfirm();
    }

    private async void Button_ReviewDiff_Click(object? sender, RoutedEventArgs e)
    {
        AudioStudio_Service.Instance.PlayEditorOpen();

        string monsterName = dataModel.SelectedMonster?.Title ?? "Monster AI";
        string sessionSummary = dataModel.PendingEditSummary ?? Strings.U_Ai_PendingEditSummary;
        string behaviorSummary = dataModel.BehaviorSummary ?? Strings.U_Ai_BehaviorSummary;

        var preview = new Core.OperationPreview
        {
            OperationId = "mon-ai-diff",
            DisplayName = $"Monster AI ({monsterName})",
            FileCount = 1,
            TotalBytes = 1280,
            OverallRisk = Core.RiskLevel.Safe,
            FilePreviewSummaries = new System.Collections.Generic.List<Core.FilePreviewSummary>
            {
                new Core.FilePreviewSummary
                {
                    FileId = monsterName + " (AI)",
                    SourceRelativePath = "monster_*.bin (AiFile)",
                    OutputRelativePath = "monster_*.bin (AiFile)",
                    BeforeHash = "before_snapshot",
                    PredictedAfterHash = "after_snapshot",
                    ByteDiffLines = new[] { "0x0010: [Diff do ATEL Assembly]" },
                    DisassemblyDiffLines = System.Array.Empty<string>(),
                    SemanticDiffLines = new[]
                    {
                        string.Format(Strings.U_Ai_PreviewMonster, monsterName),
                        string.Format(Strings.U_Ai_PreviewEditSummary, sessionSummary),
                        string.Format(Strings.U_Ai_PreviewBehavior, behaviorSummary)
                    },
                    HumanSummary = $"{sessionSummary}"
                }
            }
        };

        var mainWindow = this.GetVisualAncestors().OfType<Main_Window>().FirstOrDefault();
        if (mainWindow != null)
        {
            mainWindow.ShowChangeSetPreview(preview);
        }
        else
        {
            var window = new Window
            {
                Title = string.Format(Strings.U_Ai_ReviewTitle, monsterName),
                Width = 550,
                Height = 350,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new Controls.DiffSummaryView
                {
                    DataContext = preview.FilePreviewSummaries[0]
                }
            };
            var topLevel = TopLevel.GetTopLevel(this) as Window;
            if (topLevel != null) await window.ShowDialog(topLevel);
            else window.Show();
        }
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

    private void Button_AddAbility(object? sender, RoutedEventArgs e)
    {
        dataModel.AddAbility();
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

    private void Button_AddOverdriveGauge(object? sender, RoutedEventArgs e)
    {
        dataModel.AddOverdriveGaugeAction();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_AddOverdriveFinisher(object? sender, RoutedEventArgs e)
    {
        dataModel.AddOverdriveFinisherToSequence();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_RemoveOverdriveFinisher(object? sender, RoutedEventArgs e)
    {
        dataModel.RemoveSelectedOverdriveFinisher();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_MoveOverdriveFinisherUp(object? sender, RoutedEventArgs e)
    {
        dataModel.MoveSelectedOverdriveFinisher(up: true);
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_MoveOverdriveFinisherDown(object? sender, RoutedEventArgs e)
    {
        dataModel.MoveSelectedOverdriveFinisher(up: false);
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_ApplyOverdriveRecipe(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyOverdriveRecipeAction();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_ClearOverdriveLab(object? sender, RoutedEventArgs e)
    {
        dataModel.ClearOverdriveLabAction();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_EditBuffStat(object? sender, RoutedEventArgs e)
    {
        dataModel.EditSelectedBuffStat();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_YunalescaConditionOnTurn(object? sender, RoutedEventArgs e)
    {
        dataModel.AddYunalescaConditionOnTurn();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_YunalescaConditionStart(object? sender, RoutedEventArgs e)
    {
        dataModel.AddYunalescaConditionBattleStart();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_YunalescaConditionAlways(object? sender, RoutedEventArgs e)
    {
        dataModel.AddYunalescaConditionAlways();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_YunalescaConditionHp(object? sender, RoutedEventArgs e)
    {
        dataModel.AddYunalescaConditionHpPercent();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_YunalescaConditionOnHit(object? sender, RoutedEventArgs e)
    {
        dataModel.AddYunalescaConditionOnHit();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_YunalescaConditionAnyHit(object? sender, RoutedEventArgs e)
    {
        dataModel.AddYunalescaConditionAnyHit();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_YunalescaConditionClear(object? sender, RoutedEventArgs e)
    {
        dataModel.ClearYunalescaCondition();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_PrepareTargetRecipe(object? sender, RoutedEventArgs e)
    {
        dataModel.PrepareTargetRecipe();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_PrepareSimpleActionRecipe(object? sender, RoutedEventArgs e)
    {
        dataModel.PrepareSimpleActionRecipe();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_PrepareSimpleRouletteRecipe(object? sender, RoutedEventArgs e)
    {
        dataModel.PrepareSimpleRouletteRecipe();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_PrepareCycleRecipe(object? sender, RoutedEventArgs e)
    {
        dataModel.PrepareCycleRecipe();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_PrepareReactionRecipe(object? sender, RoutedEventArgs e)
    {
        dataModel.PrepareReactionRecipe();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_PrepareTurnStateRecipe(object? sender, RoutedEventArgs e)
    {
        dataModel.PrepareTurnStateRecipe();
        AudioStudio_Service.Instance.PlayAlternative();
    }

    private void Button_PrepareCompositeActionRecipe(object? sender, RoutedEventArgs e)
    {
        MonsterAiCompositeAction_Window window = new(dataModel);
        if (TopLevel.GetTopLevel(this) is Window owner)
            window.Show(owner);
        else
            window.Show();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_OpenPhaseRotationManager(object? sender, RoutedEventArgs e)
    {
        MonsterAiPhaseRotation_Window window = new(dataModel);
        if (TopLevel.GetTopLevel(this) is Window owner)
            window.Show(owner);
        else
            window.Show();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_OpenAdvancedPhaseRotationManager(object? sender, RoutedEventArgs e)
    {
        MonsterAiPhaseRotationAdvanced_Window window = new(dataModel);
        if (TopLevel.GetTopLevel(this) is Window owner)
            window.Show(owner);
        else
            window.Show();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_OpenIndirectDispatchUnitEditor(object? sender, RoutedEventArgs e)
    {
        AiIndirectDispatchUnitVm? unit = (sender as Button)?.Tag as AiIndirectDispatchUnitVm;
        if (unit == null || !dataModel.PrepareIndirectDispatchEditor(unit.UnitId))
        {
            AudioStudio_Service.Instance.PlayAlternative();
            return;
        }

        MonsterAiIndirectDispatchUnit_Window window = new(dataModel);
        if (TopLevel.GetTopLevel(this) is Window owner)
            window.Show(owner);
        else
            window.Show();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_OpenSelectedIndirectDispatchUnitEditor(object? sender, RoutedEventArgs e)
    {
        if (!dataModel.PrepareIndirectDispatchEditorForSelectedAction())
        {
            AudioStudio_Service.Instance.PlayAlternative();
            return;
        }

        MonsterAiIndirectDispatchUnit_Window window = new(dataModel);
        if (TopLevel.GetTopLevel(this) is Window owner)
            window.Show(owner);
        else
            window.Show();
        AudioStudio_Service.Instance.PlayConfirm();
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

    private void Button_PresetNulBlaze(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyPresetNulBlaze();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_PresetNulTide(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyPresetNulTide();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_PresetNulShock(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyPresetNulShock();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_PresetNulFrost(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyPresetNulFrost();
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

    private async void Button_BakeSelectedUni(object? sender, RoutedEventArgs e)
    {
        var preset = SinPostActionPresetCatalog.Find(dataModel.SelectedSinPreset?.Id);
        if (preset == null) return;
        bool confirmed = await AvaloniaDialog_Util.ConfirmYesNoAsync(this, Strings.U_Ai_UniBakeSelected,
            string.Format(Strings.U_Ai_UniBakeConfirm, preset.Id, SinPostActionPresetCatalog.Describe(preset)),
            Strings.U_Ai_Yes, Strings.U_Ai_No);
        if (confirmed && dataModel.ApplySinPostActionPreset(preset.Id)) AudioStudio_Service.Instance.PlayConfirm();
        else AudioStudio_Service.Instance.PlayAlternative();
    }

    private async void Button_BakeUni001(object? sender, RoutedEventArgs e)
    {
        bool confirmed = await AvaloniaDialog_Util.ConfirmYesNoAsync(
            this,
            Strings.U_Ai_BakeUni001Title,
            Strings.U_Ai_ConfirmUni001,
            Strings.U_Ai_Yes, Strings.U_Ai_No);

        if (confirmed)
        {
            dataModel.ApplySinPresetUni001();
            AudioStudio_Service.Instance.PlayConfirm();
        }
        else
        {
            AudioStudio_Service.Instance.PlayAlternative();
        }
    }

    private async void Button_BakeUni002(object? sender, RoutedEventArgs e)
    {
        bool confirmed = await AvaloniaDialog_Util.ConfirmYesNoAsync(
            this,
            Strings.U_Ai_BakeUni002Title,
            Strings.F2_are_you_sure_you_want_to_add_uni_002_del_8c62f473,
            Strings.U_Ai_Yes, Strings.U_Ai_No);

        if (confirmed)
        {
            dataModel.ApplySinPresetUni002();
            AudioStudio_Service.Instance.PlayConfirm();
        }
        else
        {
            AudioStudio_Service.Instance.PlayAlternative();
        }
    }

    private async void Button_BakeUni004(object? sender, RoutedEventArgs e)
    {
        bool confirmed = await AvaloniaDialog_Util.ConfirmYesNoAsync(
            this,
            Strings.U_Ai_BakeUni004Title,
            Strings.U_Ai_ConfirmUni004,
            Strings.U_Ai_Yes, Strings.U_Ai_No);

        if (confirmed)
        {
            dataModel.ApplySinPresetUni004();
            AudioStudio_Service.Instance.PlayConfirm();
        }
        else
        {
            AudioStudio_Service.Instance.PlayAlternative();
        }
    }

    private void Button_ApplyUniformScale(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyUniformScale();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    // ── Placeholders Bikanel UNI-009..014 ────────────────────────────────
    private async void Button_BakeUni009(object? sender, RoutedEventArgs e)
        => await ShowPlaceholderDialog(Strings.U_Ai_Uni009Title, Strings.U_Ai_Uni009Msg);
    private async void Button_BakeUni010(object? sender, RoutedEventArgs e)
        => await ShowPlaceholderDialog(Strings.U_Ai_Uni010Title, Strings.U_Ai_Uni010Msg);
    private async void Button_BakeUni011(object? sender, RoutedEventArgs e)
        => await ShowPlaceholderDialog("UNI-011 · Suga", "Bikanel — Drains HP from the front line. Placeholder, no implementation.");
    private async void Button_BakeUni012(object? sender, RoutedEventArgs e)
        => await ShowPlaceholderDialog(Strings.U_Ai_Uni012Title, Strings.U_Ai_Uni012Msg);
    private async void Button_BakeUni013(object? sender, RoutedEventArgs e)
        => await ShowPlaceholderDialog(Strings.U_Ai_Uni013Title, Strings.U_Ai_Uni013Msg);
    private async void Button_BakeUni014(object? sender, RoutedEventArgs e)
        => await ShowPlaceholderDialog(Strings.U_Ai_Uni014Title, Strings.U_Ai_Uni014Msg);

    private async Task ShowPlaceholderDialog(string title, string message)
    {
        bool confirmed = await AvaloniaDialog_Util.ConfirmYesNoAsync(this, title, message + "\n\n" + Strings.U_Ai_PlaceholderConfirm, Strings.U_Ai_YesEmpty, Strings.U_Ai_No);
        if (confirmed) { dataModel.SinCatalogSummary = string.Format(Strings.U_Ai_PlaceholderApplied, title); AudioStudio_Service.Instance.PlayConfirm(); }
        else { AudioStudio_Service.Instance.PlayAlternative(); }
    }

    private void Button_ScalePlus10(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyUniformScaleMultiplier(1.1f);
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_ScalePlus80(object? sender, RoutedEventArgs e)
    {
        dataModel.ApplyUniformScaleMultiplier(SinScaleOpener.DefaultUniformScale);
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

    private void Button_RenameCommand(object? sender, RoutedEventArgs e)
    {
        dataModel.RenameSelectedCommand();
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_RestoreBackup(object? sender, RoutedEventArgs e)
    {
        dataModel.RestoreBackup();
        AudioStudio_Service.Instance.PlayAlternative();
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
}

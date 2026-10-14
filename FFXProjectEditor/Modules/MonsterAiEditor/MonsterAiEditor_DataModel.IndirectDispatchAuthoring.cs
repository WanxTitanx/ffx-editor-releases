using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.Resources;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;

namespace FFXProjectEditor.Modules.MonsterAiEditor
{
    internal partial class MonsterAiEditor_DataModel
    {
        [ObservableProperty] private AiIndirectDispatchUnitEditorVm? indirectDispatchEditorVm;
        string? indirectDispatchSourcePath;
        byte[]? indirectDispatchSourceBytes;

        void CaptureIndirectDispatchSource()
        {
            indirectDispatchSourcePath = selectedPath;
            indirectDispatchSourceBytes = selectedScript == null ? null : AiScript_File.Write(selectedScript);
        }
        public bool CanEditSelectedIndirectDispatchUnit => FindIndirectDispatchUnitsForSelectedAction().Count > 0;

        public bool PrepareIndirectDispatchEditor(string unitId, AiIndirectDispatchEditorLaunchContext? focusContext = null)
        {
            if (!TryFindLiveIndirectDispatchUnit(unitId, out AiIndirectDispatchUnit? unit, out string error) || unit == null)
            {
                RemoveActionSummary = error;
                return false;
            }

            if (!CanEditIndirectDispatchUnit(unit))
            {
                RemoveActionSummary =
                    $"'rota {unit.UnitIndex}' ainda e leitura tecnica apenas. O MVP atual so libera unidades AuthoringCandidate row-only.";
                return false;
            }

            IndirectDispatchEditorVm = new AiIndirectDispatchUnitEditorVm(new[] { unit }, unit.UnitId, focusContext);
            CaptureIndirectDispatchSource();
            return true;
        }

        public bool PrepareIndirectDispatchEditorForSelectedAction()
        {
            IReadOnlyList<AiIndirectDispatchUnitVm> units = FindIndirectDispatchUnitsForSelectedAction();
            if (units.Count == 0)
            {
                RemoveActionSummary =
                    Strings.F2_the_selected_action_is_not_linked_to_an_4e721895;
                return false;
            }

            string? preferredUnitId = units[0].UnitId;
            IndirectDispatchEditorVm = new AiIndirectDispatchUnitEditorVm(
                units.Select(unit => unit.SourceUnit).ToList(),
                preferredUnitId);
            CaptureIndirectDispatchSource();
            return true;
        }

        public void ClearIndirectDispatchEditor(AiIndirectDispatchUnitEditorVm? expected = null)
        {
            if (expected != null && !ReferenceEquals(expected, IndirectDispatchEditorVm)) return;
            IndirectDispatchEditorVm = null;
            indirectDispatchSourcePath = null;
            indirectDispatchSourceBytes = null;
        }

        public bool ApplyPreparedIndirectDispatchEdits(AiIndirectDispatchUnitEditorVm? expected = null)
        {
            if (expected != null && !ReferenceEquals(expected, IndirectDispatchEditorVm))
            {
                expected.StatusSummary = Strings.AiAdvancedEditorChanged;
                return false;
            }
            if (IndirectDispatchEditorVm == null)
            {
                RemoveActionSummary = "Nenhuma unidade indireta esta armada para edicao.";
                return false;
            }

            if (selectedScript == null || !selectedScript.HasScript || selectedPath == null)
            {
                IndirectDispatchEditorVm.StatusSummary = Strings.F2_the_current_monster_is_not_ready_for_app_fc26d467;
                return false;
            }

            if (HasPendingAuthoringEdits)
            {
                IndirectDispatchEditorVm.StatusSummary = Strings.AiAdvancedPendingEdits;
                return false;
            }

            if (!string.Equals(selectedPath, indirectDispatchSourcePath, StringComparison.Ordinal)
                || indirectDispatchSourceBytes == null
                || !AiScript_File.Write(selectedScript).SequenceEqual(indirectDispatchSourceBytes))
            {
                IndirectDispatchEditorVm.StatusSummary = Strings.AiAdvancedEditorChanged;
                return false;
            }

            if (!IndirectDispatchEditorVm.TryBuildEditRequest(out AiIndirectDispatchEditRequest request, out string error))
            {
                IndirectDispatchEditorVm.StatusSummary = error;
                return false;
            }

            if (!AiIndirectDispatchRowOnlyEditor.TryApplyEdits(AiScript_File.Read(AiScript_File.Write(selectedScript)), request, out AiIndirectDispatchEditResult? result, out error)
                || result == null)
            {
                IndirectDispatchEditorVm.StatusSummary = error;
                return false;
            }

            if (!AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(
                    result.EditedAiFileBytes,
                    selectedScript,
                    selectedScript.OriginalAiFileBytes.Length,
                    out _,
                    out string validationReason))
            {
                error = $"Validacao estrutural bloqueou o apply: {validationReason}";
                IndirectDispatchEditorVm.StatusSummary = error;
                return false;
            }

            try
            {
                AiAdvancedFileWriter.Save(selectedPath, selectedScript.OriginalAiFileBytes, result.EditedAiFileBytes);
            }
            catch (Exception ex)
            {
                error = $"Save row-only abortado no splice: {ex.Message}";
                IndirectDispatchEditorVm.StatusSummary = error;
                return false;
            }

            bool reloaded = ReloadSelectedFromDisk();
            string summary =
                $"✅ Unidade indireta '{IndirectDispatchEditorVm.UnitTitle}' salva em {Path.GetFileName(selectedPath)} " +
                $"({result.Request.Edits.Count} edit(s), {result.ChangedBytes.Count} byte(s) no diff, row-only, .prev.bak criado). " +
                $"{(reloaded ? "Readback ok." : "Readback falhou; use Refresh.")} ⚠️ Confirme o comportamento in-game (RT2).";

            IndirectDispatchEditorVm.StatusSummary = summary;
            RemoveActionSummary = summary;
            AdvancedWorkspaceFeedback = summary;
            AuthoringRecipeSummary =
                "Editor de unidade indireta ativo: row-only, um pacote por vez, sem reescrever hook/switch/call final.";
            return true;
        }

        IReadOnlyList<AiIndirectDispatchUnitVm> FindIndirectDispatchUnitsForSelectedAction()
        {
            AiDetectedAction? action = SelectedAutomationAction?.Action;
            if (action == null)
                return Array.Empty<AiIndirectDispatchUnitVm>();

            return LoadLiveIndirectDispatchUnits()
                .Where(unit => CanEditIndirectDispatchUnit(unit) && unit.Consumers.Any(consumer => consumer.CallOffset == action.CallOffset))
                .Select(unit => new AiIndirectDispatchUnitVm(unit))
                .OrderBy(unit => unit.SourceUnit.UnitIndex)
                .ToList();
        }

        IReadOnlyList<AiIndirectDispatchUnit> LoadLiveIndirectDispatchUnits()
        {
            if (selectedScript == null || !selectedScript.HasScript || string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath))
                return Array.Empty<AiIndirectDispatchUnit>();

            try
            {
                byte[] monsterBin = File.ReadAllBytes(selectedPath);
                return DetectPromotedIndirectDispatchUnits(monsterBin);
            }
            catch
            {
                return Array.Empty<AiIndirectDispatchUnit>();
            }
        }

        bool TryFindLiveIndirectDispatchUnit(string unitId, out AiIndirectDispatchUnit? unit, out string error)
        {
            unit = null;
            if (selectedScript == null || !selectedScript.HasScript || string.IsNullOrWhiteSpace(selectedPath))
            {
                error = Strings.F2_select_a_monster_with_a_real_aifile_befo_68c74df3;
                return false;
            }

            if (!File.Exists(selectedPath))
            {
                error = Strings.F2_the_bin_of_the_current_monster_is_not_ac_1325acbf;
                return false;
            }

            IReadOnlyList<AiIndirectDispatchUnit> units = LoadLiveIndirectDispatchUnits();
            unit = units.FirstOrDefault(candidate => candidate.UnitId.Equals(unitId, StringComparison.OrdinalIgnoreCase));
            if (unit == null)
            {
                error = Strings.F2_i_did_not_find_the_requested_indirect_un_e74908e2;
                return false;
            }

            error = string.Empty;
            return true;
        }

        static bool CanEditIndirectDispatchUnit(AiIndirectDispatchUnit unit) =>
            unit.CapabilityTier == AiIndirectDispatchCapabilityTier.AuthoringCandidate
            && (unit.EditableSlots.Count > 0
                || unit.EditableTargetSlots.Any(slot => slot.CanEdit)
                || unit.EditableNextStateOffset.HasValue);
    }

    internal enum AiIndirectDispatchEditorFocusKind
    {
        Guard,
        CommandSlot,
        TargetSlot,
        NextState,
    }

    internal sealed record AiIndirectDispatchEditorLaunchContext(
        string UnitId,
        AiIndirectDispatchEditorFocusKind FocusKind,
        string RoleKey,
        int InstructionOffset,
        string RoleLabel,
        string VariableName);

    internal sealed partial class AiIndirectDispatchUnitEditorVm : ObservableObject
    {
        readonly IReadOnlyList<AiIndirectDispatchUnit> sourceUnits;
        readonly AiIndirectDispatchEditorLaunchContext? focusContext;
        AiIndirectDispatchUnit sourceUnit;

        public AiIndirectDispatchUnitEditorVm(
            IReadOnlyList<AiIndirectDispatchUnit> sourceUnits,
            string? preferredUnitId = null,
            AiIndirectDispatchEditorLaunchContext? focusContext = null)
        {
            this.sourceUnits = sourceUnits;
            this.focusContext = focusContext;
            sourceUnit = sourceUnits.First();

            foreach (AiIndirectDispatchUnit unit in sourceUnits.OrderBy(unit => unit.UnitIndex))
                AvailableUnits.Add(new AiIndirectDispatchUnitChoiceVm(unit));

            AiIndirectDispatchUnitChoiceVm? initialUnit = AvailableUnits.FirstOrDefault(unit =>
                                                    unit.UnitId.Equals(preferredUnitId, StringComparison.OrdinalIgnoreCase))
                                                ?? AvailableUnits.FirstOrDefault();
            selectedUnit = initialUnit;
            if (initialUnit != null)
            {
                sourceUnit = sourceUnits.First(unit => unit.UnitId.Equals(initialUnit.UnitId, StringComparison.OrdinalIgnoreCase));
                LoadUnit(sourceUnit, initial: true);
            }

            OnPropertyChanged(nameof(SelectedUnit));
        }

        public ObservableCollection<AiIndirectDispatchUnitChoiceVm> AvailableUnits { get; } = new();
        public ObservableCollection<AiIndirectDispatchEditableSlotRow> SlotRows { get; } = new();
        public ObservableCollection<AiIndirectDispatchEditableTargetSlotRow> TargetSlotRows { get; } = new();
        public bool HasMultipleUnits => AvailableUnits.Count > 1;
        public bool HasEditableNextState => sourceUnit.EditableNextStateOffset.HasValue;
        public bool HasTargetSlotRows => TargetSlotRows.Count > 0;

        [ObservableProperty] private AiIndirectDispatchUnitChoiceVm? selectedUnit;
        partial void OnSelectedUnitChanged(AiIndirectDispatchUnitChoiceVm? value)
        {
            if (value == null)
                return;

            AiIndirectDispatchUnit? unit = sourceUnits.FirstOrDefault(candidate =>
                candidate.UnitId.Equals(value.UnitId, StringComparison.OrdinalIgnoreCase));
            if (unit == null)
                return;

            LoadUnit(unit, initial: false);
        }

        [ObservableProperty] private string unitTitle = string.Empty;
        [ObservableProperty] private string hookSummary = string.Empty;
        [ObservableProperty] private string consumerSummary = string.Empty;
        [ObservableProperty] private string offsetSummary = string.Empty;
        [ObservableProperty] private string warningSummary = string.Empty;
        [ObservableProperty] private string focusedContextSummary = string.Empty;
        partial void OnFocusedContextSummaryChanged(string value) => OnPropertyChanged(nameof(HasFocusedContext));
        public bool HasFocusedContext => !string.IsNullOrWhiteSpace(FocusedContextSummary);

        [ObservableProperty] private string nextStateText = "0";
        partial void OnNextStateTextChanged(string value) => RefreshPreview();
        [ObservableProperty] private bool isNextStateFocused;

        [ObservableProperty] private string previewSummary = string.Empty;
        [ObservableProperty] private string statusSummary = string.Empty;
        [ObservableProperty] private bool hasChanges;

        public bool TryBuildEditRequest(out AiIndirectDispatchEditRequest request, out string error)
        {
            var edits = new List<AiIndirectDispatchValueEdit>();

            foreach (AiIndirectDispatchEditableSlotRow row in SlotRows.Where(row => row.HasChanged))
            {
                edits.Add(new AiIndirectDispatchValueEdit(
                    row.RoleKey,
                    row.Label,
                    row.Offset,
                    row.CurrentValue,
                    row.EditedValue));
            }

            foreach (AiIndirectDispatchEditableTargetSlotRow row in TargetSlotRows.Where(row => row.CanEdit && row.HasChanged))
                edits.AddRange(row.BuildEdits());

            if (sourceUnit.EditableNextStateOffset.HasValue && sourceUnit.CurrentNextStateValue.HasValue)
            {
                if (!AiAdvancedNumericInput.TryParse(NextStateText, out ushort nextState))
                {
                    request = new AiIndirectDispatchEditRequest(sourceUnit.UnitId, Array.Empty<AiIndirectDispatchValueEdit>());
                    error = Strings.AiAdvancedInvalidNumber;
                    return false;
                }
                if (nextState != sourceUnit.CurrentNextStateValue.Value)
                {
                    edits.Add(new AiIndirectDispatchValueEdit(
                        "next-state",
                        "next state",
                        sourceUnit.EditableNextStateOffset.Value,
                        sourceUnit.CurrentNextStateValue.Value,
                        nextState));
                }
            }

            if (edits.Count == 0)
            {
                error = Strings.F2_nothing_changed_in_this_indirect_unit_9f028f6b;
                request = new AiIndirectDispatchEditRequest(sourceUnit.UnitId, Array.Empty<AiIndirectDispatchValueEdit>());
                return false;
            }

            request = new AiIndirectDispatchEditRequest(sourceUnit.UnitId, edits);
            error = string.Empty;
            return true;
        }

        void LoadUnit(AiIndirectDispatchUnit unit, bool initial)
        {
            sourceUnit = unit;
            SlotRows.Clear();
            TargetSlotRows.Clear();
            foreach (AiIndirectDispatchEditableSlot slot in unit.EditableSlots)
                SlotRows.Add(new AiIndirectDispatchEditableSlotRow(slot, RefreshPreview));
            foreach (AiIndirectDispatchEditableTargetSlot slot in unit.EditableTargetSlots)
                TargetSlotRows.Add(new AiIndirectDispatchEditableTargetSlotRow(slot, RefreshPreview));

            UnitTitle = $"Editor row-only · rota {unit.UnitIndex}";
            HookSummary = $"{unit.HookKind} · {unit.GuardSummary}";
            ConsumerSummary = string.Join(Environment.NewLine, unit.Consumers.Select(consumer =>
                $"{consumer.Label}: {consumer.CommandVariableName} + {consumer.TargetVariableName} -> 0x{consumer.CallOffset:X4}"));
            OffsetSummary = unit.OffsetSummary;
            WarningSummary =
                "Structural writer current: changes command rows, switches target between literal/copy when the slot fits in a local push, reparameterizes recognized recipes, and modifies next-state. Hook, topology, consumers, and route creation remain out of this pass.";
            NextStateText = unit.CurrentNextStateValue?.ToString() ?? "0";
            StatusSummary = initial
                ? Strings.F2_review_the_slots_below_the_apply_only_pa_c7495243
                : Strings.F2_route_changed_review_the_slots_of_this_r_c391b1af;
            OnPropertyChanged(nameof(HasEditableNextState));
            OnPropertyChanged(nameof(HasTargetSlotRows));
            RefreshPreview();
            ApplyFocusRequest(focusContext, initial);
        }

        void ApplyFocusRequest(AiIndirectDispatchEditorLaunchContext? request, bool initial)
        {
            ClearFocusMarkers();
            if (request == null)
                return;

            string intro = initial ? "Foco vindo do V2" : Strings.F2_inherited_focus_when_changing_the_route_25bc6812;
            if (request.FocusKind == AiIndirectDispatchEditorFocusKind.Guard)
            {
                FocusedContextSummary =
                    $"{intro}: {DescribeFocusSubject(request)} em {UnitTitle}. Esta abertura continua rota-centrica porque o guard segue como leitura estrutural.";
                return;
            }

            if (request.FocusKind == AiIndirectDispatchEditorFocusKind.CommandSlot)
            {
                AiIndirectDispatchEditableSlotRow? row = SlotRows.FirstOrDefault(candidate =>
                    FocusMatches(candidate.RoleKey, candidate.Offset, request));
                if (row != null)
                {
                    row.IsFocused = true;
                    FocusedContextSummary =
                        $"{intro}: {DescribeFocusSubject(request)} em {UnitTitle}. O command slot correspondente ja ficou destacado abaixo.";
                    return;
                }
            }

            if (request.FocusKind == AiIndirectDispatchEditorFocusKind.TargetSlot)
            {
                AiIndirectDispatchEditableTargetSlotRow? row = TargetSlotRows.FirstOrDefault(candidate =>
                    FocusMatches(candidate.RoleKey, candidate.Offset, request));
                if (row != null)
                {
                    row.IsFocused = true;
                    FocusedContextSummary =
                        $"{intro}: {DescribeFocusSubject(request)} em {UnitTitle}. O target slot correspondente ja ficou destacado abaixo.";
                    return;
                }
            }

            if (request.FocusKind == AiIndirectDispatchEditorFocusKind.NextState && HasEditableNextState)
            {
                IsNextStateFocused = true;
                FocusedContextSummary =
                    $"{intro}: {DescribeFocusSubject(request)} em {UnitTitle}. O bloco de next-state ja ficou destacado abaixo.";
                return;
            }

            FocusedContextSummary =
                string.Format(Strings.U_Ai_IdaFocusNoSlot, intro, DescribeFocusSubject(request), UnitTitle);
        }

        void ClearFocusMarkers()
        {
            foreach (AiIndirectDispatchEditableSlotRow row in SlotRows)
                row.IsFocused = false;
            foreach (AiIndirectDispatchEditableTargetSlotRow row in TargetSlotRows)
                row.IsFocused = false;
            IsNextStateFocused = false;
            FocusedContextSummary = string.Empty;
        }

        static bool FocusMatches(string roleKey, int offset, AiIndirectDispatchEditorLaunchContext request)
        {
            if (!string.IsNullOrWhiteSpace(request.RoleKey)
                && roleKey.Equals(request.RoleKey, StringComparison.OrdinalIgnoreCase))
                return true;

            return request.InstructionOffset >= 0 && offset == request.InstructionOffset;
        }

        static string DescribeFocusSubject(AiIndirectDispatchEditorLaunchContext request)
        {
            if (string.IsNullOrWhiteSpace(request.VariableName))
                return request.RoleLabel;

            return $"{request.VariableName} como {request.RoleLabel}";
        }

        void RefreshPreview()
        {
            var sb = new StringBuilder();
            int changeCount = 0;

            foreach (AiIndirectDispatchEditableSlotRow row in SlotRows)
            {
                if (!row.HasChanged)
                    continue;

                changeCount++;
                sb.Append("• ")
                  .Append(row.Label)
                  .Append(": ")
                  .Append(row.CurrentDisplay)
                  .Append(" -> ")
                  .Append(row.EditedDisplay)
                  .Append(" @ ")
                  .Append(row.OffsetHex)
                  .AppendLine();
            }

            foreach (AiIndirectDispatchEditableTargetSlotRow row in TargetSlotRows)
            {
                if (!row.CanEdit || !row.HasChanged)
                    continue;

                changeCount++;
                sb.Append("• ")
                  .Append(row.Label)
                  .Append(": ")
                  .Append(row.CurrentDisplay)
                  .Append(" -> ")
                  .Append(row.EditedDisplaySummary)
                  .Append(" @ ")
                  .Append(row.OffsetHex)
                  .AppendLine();
            }

            if (sourceUnit.EditableNextStateOffset.HasValue && sourceUnit.CurrentNextStateValue.HasValue)
            {
                if (!AiAdvancedNumericInput.TryParse(NextStateText, out ushort nextState))
                {
                    HasChanges = false;
                    PreviewSummary = Strings.AiAdvancedInvalidNumber;
                    return;
                }
                if (nextState != sourceUnit.CurrentNextStateValue.Value)
                {
                    changeCount++;
                    sb.Append("• next state: ")
                      .Append(sourceUnit.CurrentNextStateValue.Value)
                      .Append(" -> ")
                      .Append(nextState)
                      .Append(" @ 0x")
                      .Append(sourceUnit.EditableNextStateOffset.Value.ToString("X4"))
                      .AppendLine();
                }
            }

            HasChanges = changeCount > 0;
            PreviewSummary = changeCount == 0
                ? Strings.F2_no_pending_changes_the_apply_only_modifi_468bc426
                : $"Preview de {changeCount} mudanca(s):{Environment.NewLine}{sb.ToString().TrimEnd()}";
        }


    }

    internal sealed class AiIndirectDispatchUnitChoiceVm
    {
        public AiIndirectDispatchUnitChoiceVm(AiIndirectDispatchUnit unit)
        {
            UnitId = unit.UnitId;
            Title = $"rota {unit.UnitIndex}";
            Summary = string.Join(" / ", unit.EditableSlots.Select(slot => slot.CurrentValueResolved).Take(4));
        }

        public string UnitId { get; }
        public string Title { get; }
        public string Summary { get; }
        public string Display => $"{Title} - {Summary}";
        public override string ToString() => Display;
    }

    internal sealed partial class AiIndirectDispatchEditableSlotRow : ObservableObject
    {
        readonly Action changed;

        public AiIndirectDispatchEditableSlotRow(AiIndirectDispatchEditableSlot slot, Action changed)
        {
            this.changed = changed;
            RoleKey = slot.RoleKey;
            Label = slot.SlotLabel;
            Offset = slot.PushInstructionOffset;
            CurrentValue = slot.CurrentValue;
            CurrentDisplay = slot.CurrentValueResolved;
            CommandOptions = AiCommandId.AllOptions();
            SelectedCommand = AiCommandId.OptionFor(slot.CurrentValue);
        }

        public string RoleKey { get; }
        public string Label { get; }
        public int Offset { get; }
        public ushort CurrentValue { get; }
        public string CurrentDisplay { get; }
        public string OffsetHex => $"0x{Offset:X4}";
        public IReadOnlyList<AiCommandOption> CommandOptions { get; }
        [ObservableProperty] private bool isFocused;

        [ObservableProperty] private AiCommandOption? selectedCommand;
        partial void OnSelectedCommandChanged(AiCommandOption? value)
        {
            OnPropertyChanged(nameof(HasChanged));
            OnPropertyChanged(nameof(EditedValue));
            OnPropertyChanged(nameof(EditedDisplay));
            changed();
        }

        public ushort EditedValue => SelectedCommand?.Operand ?? CurrentValue;
        public string EditedDisplay => SelectedCommand?.Display ?? CurrentDisplay;
        public bool HasChanged => EditedValue != CurrentValue;
    }

    internal sealed class AiIndirectDispatchTargetOption
    {
        public AiIndirectDispatchTargetOption(ushort operand, string label)
        {
            Operand = operand;
            Label = label;
        }

        public ushort Operand { get; }
        public string Label { get; }
        public string Display => $"{Label} [0x{Operand:X4}]";
        public override string ToString() => Display;
    }

    internal sealed class AiIndirectDispatchCopyVariableOption
    {
        public AiIndirectDispatchCopyVariableOption(ushort variableIndex, string variableName)
        {
            VariableIndex = variableIndex;
            VariableName = variableName;
        }

        public ushort VariableIndex { get; }
        public string VariableName { get; }
        public string Display => $"{VariableName} [0x{VariableIndex:X4}]";
        public override string ToString() => Display;
    }

    internal sealed class AiIndirectDispatchTargetEditModeOption
    {
        public AiIndirectDispatchTargetEditModeOption(AiIndirectDispatchTargetSlotSourceKind mode, string label)
        {
            Mode = mode;
            Label = label;
        }

        public AiIndirectDispatchTargetSlotSourceKind Mode { get; }
        public string Label { get; }
        public override string ToString() => Label;
    }

    internal sealed partial class AiIndirectDispatchEditableTargetSlotRow : ObservableObject
    {
        readonly Action changed;
        const byte PushIiOpcode = 0xAE;
        const byte PushVOpcode = 0x9F;

        public AiIndirectDispatchEditableTargetSlotRow(AiIndirectDispatchEditableTargetSlot slot, Action changed)
        {
            this.changed = changed;
            RoleKey = slot.RoleKey;
            Label = slot.SlotLabel;
            VariableName = slot.VariableName;
            Offset = slot.SourceInstructionOffset;
            CurrentValue = slot.CurrentValue;
            CurrentDisplay = slot.CurrentValueResolved;
            DetailSummary = slot.DetailSummary;
            CanEdit = slot.CanEdit;
            SourceKind = slot.SourceKind;
            RecipeKind = slot.RecipeKind;
            EditableOperands = slot.EditableOperands;
            TargetOptions = AiTargetNames.Standard
                .Select(option => new AiIndirectDispatchTargetOption(option.Operand, option.Label))
                .ToList();
            RecipeOptions = new[]
            {
                new AiTargetOption(0, Strings.U_Ai_IdaRandomFrontline, false, AiTargetRecipeKind.FindAliveFrontlineAny),
                new AiTargetOption(0, Strings.U_Ai_IdaLowestHpFrontline, false, AiTargetRecipeKind.FindAliveFrontlineLowestHp),
            };
            CopyVariableOptions = slot.CopyVariableCandidates
                .Select(candidate => new AiIndirectDispatchCopyVariableOption(candidate.VariableIndex, candidate.VariableName))
                .DistinctBy(option => option.VariableIndex)
                .ToList();
            EditModeOptions = BuildEditModeOptions(slot.SourceKind, slot.CanEdit);

            SelectedTarget = slot.SourceKind == AiIndirectDispatchTargetSlotSourceKind.Literal && slot.CanEdit
                ? TargetOptions.FirstOrDefault(option => option.Operand == slot.CurrentValue)
                    ?? new AiIndirectDispatchTargetOption(slot.CurrentValue, slot.CurrentValueResolved)
                : TargetOptions.FirstOrDefault();
            SelectedRecipe = slot.SourceKind == AiIndirectDispatchTargetSlotSourceKind.ComputedRecipe
                ? RecipeOptions.FirstOrDefault(option => option.TargetRecipeKind == slot.RecipeKind)
                : RecipeOptions.FirstOrDefault();
            SelectedCopyVariable = slot.SourceKind == AiIndirectDispatchTargetSlotSourceKind.CopiedValue
                ? CopyVariableOptions.FirstOrDefault(option => option.VariableIndex == slot.CurrentValue)
                    ?? CopyVariableOptions.FirstOrDefault()
                : CopyVariableOptions.FirstOrDefault();
            SelectedEditMode = EditModeOptions.FirstOrDefault(option => option.Mode == slot.SourceKind)
                               ?? EditModeOptions.FirstOrDefault();
        }

        public string RoleKey { get; }
        public string Label { get; }
        public string VariableName { get; }
        public int Offset { get; }
        public ushort CurrentValue { get; }
        public string CurrentDisplay { get; }
        public string DetailSummary { get; }
        public bool CanEdit { get; }
        public AiIndirectDispatchTargetSlotSourceKind SourceKind { get; }
        public AiTargetRecipeKind? RecipeKind { get; }
        public IReadOnlyList<AiIndirectDispatchEditableOperand> EditableOperands { get; }
        public string SourceKindLabel => SourceKind switch
        {
            AiIndirectDispatchTargetSlotSourceKind.Literal => "literal",
            AiIndirectDispatchTargetSlotSourceKind.ComputedRecipe => "recipe",
            AiIndirectDispatchTargetSlotSourceKind.CopiedValue => "copia",
            _ => "desconhecido",
        };
        public string OffsetHex => Offset >= 0 ? $"0x{Offset:X4}" : "-";
        public IReadOnlyList<AiIndirectDispatchTargetOption> TargetOptions { get; }
        public IReadOnlyList<AiTargetOption> RecipeOptions { get; }
        public IReadOnlyList<AiIndirectDispatchCopyVariableOption> CopyVariableOptions { get; }
        public IReadOnlyList<AiIndirectDispatchTargetEditModeOption> EditModeOptions { get; }
        public bool CanChangeMode => EditModeOptions.Count > 1;
        [ObservableProperty] private bool isFocused;
        public bool ShowLiteralEditor => CanEdit && SelectedEditMode?.Mode == AiIndirectDispatchTargetSlotSourceKind.Literal;
        public bool ShowRecipeEditor => CanEdit && SelectedEditMode?.Mode == AiIndirectDispatchTargetSlotSourceKind.ComputedRecipe && RecipeOptions.Count > 0;
        public bool ShowCopyEditor => CanEdit && SelectedEditMode?.Mode == AiIndirectDispatchTargetSlotSourceKind.CopiedValue && CopyVariableOptions.Count > 0;

        [ObservableProperty] private AiIndirectDispatchTargetEditModeOption? selectedEditMode;
        partial void OnSelectedEditModeChanged(AiIndirectDispatchTargetEditModeOption? value)
        {
            if (value?.Mode == AiIndirectDispatchTargetSlotSourceKind.Literal && SelectedTarget == null)
                SelectedTarget = TargetOptions.FirstOrDefault();
            if (value?.Mode == AiIndirectDispatchTargetSlotSourceKind.CopiedValue && SelectedCopyVariable == null)
                SelectedCopyVariable = CopyVariableOptions.FirstOrDefault();
            if (value?.Mode == AiIndirectDispatchTargetSlotSourceKind.ComputedRecipe && SelectedRecipe == null)
                SelectedRecipe = RecipeOptions.FirstOrDefault();

            OnPropertyChanged(nameof(ShowLiteralEditor));
            OnPropertyChanged(nameof(ShowRecipeEditor));
            OnPropertyChanged(nameof(ShowCopyEditor));
            RaiseTargetEditChanged();
        }

        [ObservableProperty] private AiIndirectDispatchTargetOption? selectedTarget;
        partial void OnSelectedTargetChanged(AiIndirectDispatchTargetOption? value)
        {
            RaiseTargetEditChanged();
        }

        [ObservableProperty] private AiTargetOption? selectedRecipe;
        partial void OnSelectedRecipeChanged(AiTargetOption? value) => RaiseTargetEditChanged();

        [ObservableProperty] private AiIndirectDispatchCopyVariableOption? selectedCopyVariable;
        partial void OnSelectedCopyVariableChanged(AiIndirectDispatchCopyVariableOption? value) => RaiseTargetEditChanged();

        void RaiseTargetEditChanged()
        {
            OnPropertyChanged(nameof(HasChanged));
            OnPropertyChanged(nameof(EditedDisplaySummary));
            changed();
        }

        public string EditedDisplaySummary => (SelectedEditMode?.Mode ?? SourceKind) switch
        {
            AiIndirectDispatchTargetSlotSourceKind.Literal => SelectedTarget?.Display ?? CurrentDisplay,
            AiIndirectDispatchTargetSlotSourceKind.ComputedRecipe => SelectedRecipe?.Label ?? CurrentDisplay,
            AiIndirectDispatchTargetSlotSourceKind.CopiedValue => SelectedCopyVariable?.Display ?? CurrentDisplay,
            _ => CurrentDisplay,
        };

        public bool HasChanged => !CanEdit
            ? false
            : (SelectedEditMode?.Mode ?? SourceKind) switch
            {
                AiIndirectDispatchTargetSlotSourceKind.Literal => SourceKind != AiIndirectDispatchTargetSlotSourceKind.Literal
                                                                  || (SelectedTarget?.Operand ?? CurrentValue) != CurrentValue,
                AiIndirectDispatchTargetSlotSourceKind.ComputedRecipe => SourceKind != AiIndirectDispatchTargetSlotSourceKind.ComputedRecipe
                                                                         || SelectedRecipe?.TargetRecipeKind != RecipeKind,
                AiIndirectDispatchTargetSlotSourceKind.CopiedValue => SourceKind != AiIndirectDispatchTargetSlotSourceKind.CopiedValue
                                                                      || (SelectedCopyVariable?.VariableIndex ?? CurrentValue) != CurrentValue,
                _ => false,
            };

        public IReadOnlyList<AiIndirectDispatchValueEdit> BuildEdits()
        {
            if (!CanEdit || !HasChanged)
                return Array.Empty<AiIndirectDispatchValueEdit>();

            AiIndirectDispatchTargetSlotSourceKind desiredMode = SelectedEditMode?.Mode ?? SourceKind;

            if (desiredMode == AiIndirectDispatchTargetSlotSourceKind.Literal)
            {
                AiIndirectDispatchEditableOperand operand = EditableOperands.First();
                ushort newValue = SelectedTarget?.Operand ?? CurrentValue;
                return new[]
                {
                    new AiIndirectDispatchValueEdit(
                        operand.RoleKey,
                        operand.RoleLabel,
                        operand.InstructionOffset,
                        operand.CurrentValue,
                        newValue,
                        operand.Opcode,
                        PushIiOpcode),
                };
            }

            if (desiredMode == AiIndirectDispatchTargetSlotSourceKind.CopiedValue)
            {
                AiIndirectDispatchEditableOperand operand = EditableOperands.First();
                ushort newValue = SelectedCopyVariable?.VariableIndex ?? CurrentValue;
                return new[]
                {
                    new AiIndirectDispatchValueEdit(
                        operand.RoleKey,
                        operand.RoleLabel,
                        operand.InstructionOffset,
                        operand.CurrentValue,
                        newValue,
                        operand.Opcode,
                        PushVOpcode),
                };
            }

            if (desiredMode == AiIndirectDispatchTargetSlotSourceKind.ComputedRecipe && SelectedRecipe != null)
            {
                (ushort group, ushort property, ushort compareValue, ushort selector) = SelectedRecipe.TargetRecipeKind switch
                {
                    AiTargetRecipeKind.FindAliveFrontlineAny => ((ushort)0xFFF2, (ushort)0x0004, (ushort)0x0000, (ushort)0x0000),
                    AiTargetRecipeKind.FindAliveFrontlineLowestHp => ((ushort)0xFFF0, (ushort)0x0000, (ushort)0x0000, (ushort)0x0002),
                    _ => throw new InvalidOperationException($"Recipe de alvo nao suportada: {SelectedRecipe.TargetRecipeKind}"),
                };

                AiIndirectDispatchEditableOperand[] recipeOps = EditableOperands.ToArray();
                return new[]
                {
                    new AiIndirectDispatchValueEdit(recipeOps[0].RoleKey, recipeOps[0].RoleLabel, recipeOps[0].InstructionOffset, recipeOps[0].CurrentValue, group),
                    new AiIndirectDispatchValueEdit(recipeOps[1].RoleKey, recipeOps[1].RoleLabel, recipeOps[1].InstructionOffset, recipeOps[1].CurrentValue, property),
                    new AiIndirectDispatchValueEdit(recipeOps[2].RoleKey, recipeOps[2].RoleLabel, recipeOps[2].InstructionOffset, recipeOps[2].CurrentValue, compareValue),
                    new AiIndirectDispatchValueEdit(recipeOps[3].RoleKey, recipeOps[3].RoleLabel, recipeOps[3].InstructionOffset, recipeOps[3].CurrentValue, selector),
                };
            }

            return Array.Empty<AiIndirectDispatchValueEdit>();
        }

        static IReadOnlyList<AiIndirectDispatchTargetEditModeOption> BuildEditModeOptions(
            AiIndirectDispatchTargetSlotSourceKind sourceKind,
            bool canEdit)
        {
            if (!canEdit)
                return Array.Empty<AiIndirectDispatchTargetEditModeOption>();

            return sourceKind switch
            {
                AiIndirectDispatchTargetSlotSourceKind.Literal or AiIndirectDispatchTargetSlotSourceKind.CopiedValue => new[]
                {
                    new AiIndirectDispatchTargetEditModeOption(AiIndirectDispatchTargetSlotSourceKind.Literal, "Literal"),
                    new AiIndirectDispatchTargetEditModeOption(AiIndirectDispatchTargetSlotSourceKind.CopiedValue, Strings.U_Ai_IdaCopyOfVar),
                },
                AiIndirectDispatchTargetSlotSourceKind.ComputedRecipe => new[]
                {
                    new AiIndirectDispatchTargetEditModeOption(AiIndirectDispatchTargetSlotSourceKind.ComputedRecipe, "Recipe"),
                },
                _ => Array.Empty<AiIndirectDispatchTargetEditModeOption>(),
            };
        }
    }
}

using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Memory;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using Xe.BinaryMapper;

namespace FFXProjectEditor.Modules.MonsterAiEditor
{
    // Real monster-AI disassembler/editor surface: reads the AiFile blob out of each monster_*.bin and
    // disassembles it with the in-repo, RT0-proven codec (FfxLib/Ai/AiScript_File) — no external
    // decompiler dump. Byte-local loose-file operand edits are live here; runtime apply/restore stays
    // explicitly lab-only through the DINPUT8 main-thread probe.
    internal partial class MonsterAiEditor_DataModel : ObservableObject, IDisposable
    {
        public ObservableCollection<MonsterAiRow> Monsters { get; } = new();
        public ObservableCollection<MonsterAiRow> DisplayedMonsters { get; } = new();
        public ObservableCollection<AiWorkerRow> SelectedWorkers { get; } = new();
        public ObservableCollection<AiEditRow> EditableInstructions { get; } = new();
        public ObservableCollection<AiDiffRow> DiffEntries { get; } = new();
        private readonly List<AiEditRow> _allEditRows = new();
        [ObservableProperty] private string instructionFilter = "";
        [ObservableProperty] private string instructionFilterSummary = "";
        partial void OnInstructionFilterChanged(string value) => ApplyInstructionFilter();

        // Build the full operand-row list once, then SHOW only the ones matching the filter — so you can type
        // "Firaga" (or "3049", or "CALLPOPA") and jump straight to it instead of scrolling ~300 instructions.
        void SetEditableRows(IEnumerable<AiInstruction> instructions)
        {
            _allEditRows.Clear();
            foreach (AiInstruction ins in instructions)
                if (ins.HasOperand) _allEditRows.Add(new AiEditRow(ins, RefreshPendingEditPreview));
            ApplyInstructionFilter();
        }

        void ApplyInstructionFilter()
        {
            EditableInstructions.Clear();
            string f = (InstructionFilter ?? "").Trim();
            foreach (AiEditRow r in _allEditRows)
                if (f.Length == 0 || r.MatchesFilter(f))
                    EditableInstructions.Add(r);
            InstructionFilterSummary = _allEditRows.Count == 0 ? ""
                : EditableInstructions.Count == _allEditRows.Count
                    ? string.Format(Strings.U_Ai_InstructionsWithOperand, _allEditRows.Count)
                    : string.Format(Strings.U_Ai_InstructionFilter, EditableInstructions.Count, _allEditRows.Count, f);
        }
        public ObservableCollection<RuntimeMonsterTargetRow> RuntimeTargets { get; } = new();

        AiScriptFile? selectedScript;
        string? selectedPath;
        readonly Dictionary<uint, Dictionary<int, byte>> livePatchBackups = new();

        [ObservableProperty] private string filterText = string.Empty;
        [ObservableProperty] private string loadSummary = Strings.AiEditorLoading;
        [ObservableProperty] private string scopeSummary = Strings.AiEditorScopeIntro;
        [ObservableProperty] private MonsterAiRow? selectedMonster;
        [ObservableProperty] private RuntimeMonsterTargetRow? selectedRuntimeTarget;
        [ObservableProperty] private string selectedHeaderSummary = string.Empty;
        [ObservableProperty] private string selectedDisassembly = string.Empty;
        [ObservableProperty] private string pendingEditSummary = Strings.F2_no_pending_delta_in_the_selected_aifile_11101f07;
        [ObservableProperty] private string pendingEditPreview = string.Empty;
        [ObservableProperty] private string runtimeSummary = Strings.F2_select_a_monster_and_enter_battle_to_cro_cdb60f48;
        [ObservableProperty] private string liveEditSummary = Strings.F2_live_apply_restore_available_in_lab_mode_24e451af;
        [ObservableProperty] private string diffSummary = Strings.F2_select_a_monster_with_an_aifile_to_compa_12e9c8b6;
        [ObservableProperty] private string diffWarning = string.Empty;
        [ObservableProperty] private bool hasDiffEntries;
        [ObservableProperty] private bool hasNoDiffEntries = true;
        [ObservableProperty] private string diffEmptyMessage = Strings.F2_select_a_monster_with_an_aifile_to_compa_12e9c8b6;
        [ObservableProperty] private string diffReferencePath = string.Empty;
        [ObservableProperty] private bool canRestoreVanillaAi;
        [ObservableProperty] private string restoreVanillaDiffButtonText = Strings.U_Ai_RestoreVanilla;
        private bool restoreVanillaDiffArmed;

        // Camada 4 (overhaul de UI): estado do Expander que substituiu a sidebar interna de 280px.
        // Persiste por sessão (default aberto); pode ser amarrado a settings depois se fizer sentido.
        [ObservableProperty] private bool isMonsterListExpanded = true;

        public MonsterAiEditor_DataModel()
        {
            monsterMagicCatalogSubscription = SubscribeToMonsterMagicCatalog(this);
            LoadFromDisk();
            SeedLibrary();
            SeedAutomations();
            SeedBible();
        }

        readonly Action monsterMagicCatalogSubscription;
        bool disposed;

        static Action SubscribeToMonsterMagicCatalog(MonsterAiEditor_DataModel model)
        {
            // Windows can share a model and hubs cache their views. A weak target
            // keeps synchronization while an owner is alive without making the
            // process-wide catalog an owner of every closed editor and buffer.
            var target = new WeakReference<MonsterAiEditor_DataModel>(model);
            Action? handler = null;
            handler = () =>
            {
                if (target.TryGetTarget(out var current) && !current.disposed)
                    current.OnMonsterMagicCatalogChanged();
                else
                    KernelMonsterMagicLiveSync.Changed -= handler;
            };
            KernelMonsterMagicLiveSync.Changed += handler;
            return handler;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            KernelMonsterMagicLiveSync.Changed -= monsterMagicCatalogSubscription;
        }

        void OnMonsterMagicCatalogChanged() => ReloadTemplateCommandOptions();

        partial void OnFilterTextChanged(string value) => ApplyFilter();
        partial void OnSelectedMonsterChanging(MonsterAiRow? oldValue, MonsterAiRow? newValue)
        {
            // Operand edits already live on the row's Script. Keep the separate
            // assembler buffer with that same row when navigating away as well.
            if (oldValue != null)
                oldValue.AssemblerDraft = HasPendingAssemblerEdits ? AssemblerInstructions.ToArray() : null;
        }
        partial void OnSelectedMonsterChanged(MonsterAiRow? value) => UpdateSelected(value);
        partial void OnHasDiffEntriesChanged(bool value) => HasNoDiffEntries = !value;

        public void RefreshFromDisk() => LoadFromDisk();

        void LoadFromDisk()
        {
            Monsters.Clear();
            DisplayedMonsters.Clear();
            SelectedWorkers.Clear();
            SelectedMonster = null;

            if (!Project_Service.Instance.IsProjectLoaded)
            {
                LoadSummary = Strings.AiEditorNoProject;
                return;
            }

            string root = ResolveMonsterAiBattleMonRoot();
            if (!Directory.Exists(root))
            {
                LoadSummary = string.Format(Strings.AiEditorMissingRoot, root);
                return;
            }

            var files = Directory.EnumerateFiles(root, "m*.bin", SearchOption.AllDirectories)
                .Where(p => IsMonsterFileName(Path.GetFileNameWithoutExtension(p)))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);

            int withAi = 0;
            foreach (string path in files)
            {
                var row = MonsterAiRow.TryLoad(path);
                if (row == null) continue;
                Monsters.Add(row);
                if (row.HasScript) withAi++;
            }

            ApplyFilter();
            SelectedMonster = DisplayedMonsters.FirstOrDefault();
            LoadSummary = string.Format(Strings.AiEditorLoaded, Monsters.Count, withAi, root);
            ScopeSummary = string.Format(Strings.AiEditorWriteScope, root);
        }

        string ResolveMonsterAiBattleMonRoot()
        {
            return Project_Service.Instance.Path_BattleMon;
        }

        static bool IsMonsterFileName(string name) =>
            name.Length == 4 && name[0] == 'm' && name.Skip(1).All(char.IsDigit);

        void ApplyFilter()
        {
            DisplayedMonsters.Clear();
            string q = FilterText.Trim();
            foreach (MonsterAiRow row in Monsters)
                if (q.Length == 0 || row.SearchBlob.Contains(q, StringComparison.OrdinalIgnoreCase))
                    DisplayedMonsters.Add(row);
            if (SelectedMonster != null && !DisplayedMonsters.Contains(SelectedMonster))
                SelectedMonster = DisplayedMonsters.FirstOrDefault();
        }

        // Apply the edited operands (already pushed into the model instructions) and write the AiFile back
        // into the monster_*.bin on disk (byte-local: only the AiFile region changes). Loose-file edit —
        // RT2 happens on the game's next load, NOT via the live probe.
        public bool Save()
        {
            if (selectedScript == null || selectedPath == null) return false;
            if (HasPendingAssemblerEdits)
            { LoadSummary = Strings.AiAdvancedPendingEdits; return false; }
            int editableCount = EditableInstructions.Count;
            try
            {
                byte[] newAi = AiScript_File.Write(selectedScript);
                if (!newAi.SequenceEqual(selectedScript.OriginalAiFileBytes))
                {
                    // Validate against an immutable baseline: the editable rows
                    // already mutated selectedScript's instruction objects.
                    var baseline = AiScript_File.Read(selectedScript.OriginalAiFileBytes);
                    var validation = AiValidator.Validate(baseline, selectedScript.Instructions);
                    if (!validation.IsValid && !AiValidator.HasOnlyBaselineUnknownErrors(validation, baseline, selectedScript))
                    {
                        ValidationReport = validation.ToReportString();
                        LoadSummary = string.Format(Strings.U_Ai_StructuralValidationFailed, validation.Errors.First().Message);
                        return false;
                    }
                }
                byte[] monster = File.ReadAllBytes(selectedPath);
                WriteMonsterWithBackup(AiScript_File.SpliceAiFileIntoMonster(monster, newAi));
            }
            catch (Exception ex)
            {
                LoadSummary = string.Format(Strings.U_Ai_SaveAborted, ex.Message);
                return false;
            }
            bool reloaded = ReloadSelectedFromDisk();
            LoadSummary = reloaded
                ? string.Format(Strings.AiEditorSaved, Path.GetFileName(selectedPath), editableCount)
                : string.Format(Strings.U_Ai_ReloadFailed, Path.GetFileName(selectedPath));
            return reloaded;
        }

        public void RefreshRuntimeTargets()
        {
            RuntimeTargets.Clear();
            SelectedRuntimeTarget = null;

            if (selectedMonster == null || selectedScript == null || !selectedMonster.HasScript)
            {
                RuntimeSummary = Strings.U_Ai_SelectMonsterWithAiFile;
                return;
            }

            if (!Process_Service.Instance.IsAlive)
            {
                RuntimeSummary = Strings.F2_ffx_is_closed_open_the_game_and_enter_ba_fccb743b;
                return;
            }

            if (!MemSharp_Service.Instance.IsAvailable())
            {
                RuntimeSummary = Strings.F2_ffx_detected_but_ram_reading_is_not_yet_e3513fa0;
                return;
            }

            if (MemSharp_Service.Instance.Read<byte>(MemoryMap.ADDR_BATTLE_ACTIVE) != 1)
            {
                RuntimeSummary = Strings.F2_no_active_battle_runtime_cross_reference_83bd31f4;
                return;
            }

            if (!TryGetSelectedMonsterNumber(out int monsterNumber))
            {
                RuntimeSummary = string.Format(Strings.U_Ai_CouldNotDeriveIndex, selectedMonster.Id);
                return;
            }

            int listAddress = MemoryBattle_Util.GetMonsterListPointer();
            if (listAddress == 0)
            {
                RuntimeSummary = Strings.F2_runtime_enemy_list_has_not_yet_appeared_3a9d7c93;
                return;
            }

            for (int slot = 0; slot < MemoryBattle_Util.MonsterListCount; slot++)
            {
                RuntimeMonsterTargetRow? row = TryReadRuntimeTarget(listAddress + slot * MemoryMap.SIZE_BATTLE_CHR_ENTRY, slot, monsterNumber);
                if (row != null)
                    RuntimeTargets.Add(row);
            }

            SelectedRuntimeTarget = RuntimeTargets.FirstOrDefault();
            RuntimeSummary = RuntimeTargets.Count == 0
                ? string.Format(Strings.U_Ai_NoRuntimeActor, monsterNumber)
                : string.Format(Strings.U_Ai_RuntimeTargetsFound, RuntimeTargets.Count, monsterNumber);
        }

        public void ApplyLivePatch()
        {
            if (selectedScript == null)
            {
                LiveEditSummary = Strings.F2_no_aifile_selected_to_apply_to_runtime_9ada450a;
                return;
            }

            if (SelectedRuntimeTarget == null)
            {
                LiveEditSummary = Strings.U_Ai_ChooseRuntimeTarget;
                return;
            }

            FfxProbe_Service probe = FfxProbe_Service.Instance;
            if (!probe.IsHooked)
            {
                LiveEditSummary = Strings.F2_dinput8_probe_is_not_hooked_live_edit_re_d62fd9d0;
                return;
            }

            byte[] editedAi = AiScript_File.Write(selectedScript);
            List<ChangedByte> changes = BuildChangedBytes(selectedScript.OriginalAiFileBytes, editedAi);
            if (changes.Count == 0)
            {
                LiveEditSummary = Strings.F2_no_operand_delta_to_apply_edit_at_least_75b87d5c;
                return;
            }

            uint scriptBase = SelectedRuntimeTarget.ScriptChunks;
            if (!livePatchBackups.TryGetValue(scriptBase, out Dictionary<int, byte>? backup))
            {
                backup = new Dictionary<int, byte>();
                livePatchBackups[scriptBase] = backup;
            }

            if (backup.Count == 0)
            {
                RuntimeAiIdentity identity = InspectRuntimeAiIdentity(scriptBase, selectedScript.OriginalAiFileBytes);
                if (!identity.ExactDiskMatch)
                {
                    LiveEditSummary = string.Format(Strings.U_Ai_LiveApplyAborted, SelectedRuntimeTarget.Title, identity.Summary);
                    return;
                }
            }

            foreach (ChangedByte change in changes)
            {
                uint address = scriptBase + (uint)change.Offset;
                if (!backup.ContainsKey(change.Offset))
                {
                    FfxProbe_Service.ProbeResult original = probe.ReadAbsolute(address, 1);
                    if (!original.Ok || original.Data == null || original.Data.Length != 1)
                    {
                        LiveEditSummary = string.Format(Strings.U_Ai_ReadBackupFailed, change.Offset, DescribeProbeFailure(original));
                        return;
                    }

                    backup[change.Offset] = original.Data[0];
                }

                FfxProbe_Service.ProbeResult write = probe.WriteAbsolute(address, new[] { change.Value });
                if (!write.Ok)
                {
                    LiveEditSummary = string.Format(Strings.U_Ai_ApplyByteFailed, change.Offset, DescribeProbeFailure(write));
                    return;
                }
            }

            LiveEditSummary = string.Format(Strings.U_Ai_LiveApplyOk, changes.Count, SelectedRuntimeTarget.Title, scriptBase);
            RefreshRuntimeTargets();
        }

        public void RestoreLivePatch()
        {
            if (SelectedRuntimeTarget == null)
            {
                LiveEditSummary = Strings.U_Ai_ChooseRuntimeTargetRestore;
                return;
            }

            uint scriptBase = SelectedRuntimeTarget.ScriptChunks;
            if (!livePatchBackups.TryGetValue(scriptBase, out Dictionary<int, byte>? backup) || backup.Count == 0)
            {
                LiveEditSummary = string.Format(Strings.U_Ai_NoRuntimeBackup, SelectedRuntimeTarget.Title);
                return;
            }

            FfxProbe_Service probe = FfxProbe_Service.Instance;
            if (!probe.IsHooked)
            {
                LiveEditSummary = "Probe DINPUT8 is not hooked. Without it, live restore cannot run on the main thread.";
                return;
            }

            foreach ((int offset, byte value) in backup.OrderBy(kvp => kvp.Key).Select(kvp => (kvp.Key, kvp.Value)))
            {
                FfxProbe_Service.ProbeResult write = probe.WriteAbsolute(scriptBase + (uint)offset, new[] { value });
                if (!write.Ok)
                {
                    LiveEditSummary = string.Format(Strings.U_Ai_RestoreByteFailed, offset, DescribeProbeFailure(write));
                    return;
                }
            }

            livePatchBackups.Remove(scriptBase);
            LiveEditSummary = string.Format(Strings.U_Ai_LiveRestoreOk, backup.Count, SelectedRuntimeTarget.Title);
            RefreshRuntimeTargets();
        }

        void UpdateSelected(MonsterAiRow? row)
        {
            SelectedWorkers.Clear();
            _allEditRows.Clear();
            EditableInstructions.Clear();
            InstructionFilterSummary = "";
            selectedScript = row?.Script;
            selectedPath = row?.Path;
            RefreshPendingEditPreview();
            RuntimeTargets.Clear();
            SelectedRuntimeTarget = null;
            RuntimeSummary = Strings.U_Ai_SelectMonsterWithAiFile;
            // Keep Phase Rotation window button + summary in sync with monster selection.
            // Without this, CanCreatePhasePrivateVarLab stays stale after switching monsters.
            if (selectedScript != null && selectedScript.HasScript)
            {
                PhasePrivateVarLabSummary = AiScript_File.TryFindFreePrivateVariableSlot(selectedScript, out int privSlot, out string privReason)
                    ? string.Format(Strings.U_Ai_NextPrivVar, privSlot, privReason)
                    : string.Format(Strings.U_Ai_PrivVarUnavailable, privReason);
            }
            else
            {
                PhasePrivateVarLabSummary = Strings.F2_private_var_unavailable_no_aifile_select_f47273cb;
            }
            OnPropertyChanged(nameof(CanCreatePhasePrivateVarLab));
            RebuildAssemblerRows();   // AI Assembler (free-edit) surface — see MonsterAiEditor_DataModel.Assembler.cs
            RebuildTemplateWorkerChoices();   // Behavior Library worker/entrypoint pickers — see .Library.cs
            RebuildAutomationChoices();   // 1-click Automations action list — see .Automations.cs
            RefreshSessionState();     // top-chrome Save/Undo enablement — see .InlineEdit.cs
            if (row == null)
            {
                SelectedHeaderSummary = string.Empty;
                SelectedDisassembly = string.Empty;
                ClearDiff(Strings.F2_select_a_monster_with_an_aifile_to_compa_12e9c8b6);
                RefreshUniformScaleSummary();
                return;
            }

            AiScriptFile? s = row.Script;
            if (s == null || !s.HasScript)
            {
                SelectedHeaderSummary = $"{row.Title}: stub / no AI script.";
                SelectedDisassembly = string.Empty;
                RuntimeTargets.Clear();
                SelectedRuntimeTarget = null;
                RuntimeSummary = Strings.U_Ai_SelectMonsterWithAiFile;
                ClearDiff(string.Format(Strings.U_Ai_StubNoAiFile, row.Id));
                RefreshUniformScaleSummary();
                return;
            }

            string vars = s.Variables.Count == 0 ? "" : "  ·  vars: " + string.Join(", ", s.Variables.Select(v => v.Name));
            string writeTarget = selectedPath == null ? "" : $"  ·  write: {selectedPath}";
            SelectedHeaderSummary =
                $"AiFile 0x{s.OriginalAiFileBytes.Length:X} · code 0x{s.CodeLength:X} @ scriptStart 0x{s.ScriptStart:X} · " +
                $"{s.Workers.Count} workers · {s.Instructions.Count} instructions · {s.Variables.Count} vars" + vars + writeTarget;

            foreach (AiWorker w in s.Workers)
                SelectedWorkers.Add(new AiWorkerRow(w, s.ScriptStart));

            SetEditableRows(s.Instructions);

            SelectedDisassembly = AiScript_File.Disassemble(s);
            RefreshPendingEditPreview();
            RefreshRuntimeTargets();
            LoadAiDiff();
            RefreshUniformScaleSummary();
        }

        public void LoadAiDiff()
        {
            ResetRestoreVanillaConfirmation();
            DiffEntries.Clear();
            ResetHumanDiff();
            HasDiffEntries = false;
            DiffWarning = string.Empty;
            DiffReferencePath = string.Empty;

            MonsterAiRow? currentMonster = SelectedMonster;
            if (currentMonster == null || selectedScript == null || selectedPath == null)
            {
                CanRestoreVanillaAi = false;
                DiffSummary = Strings.F2_select_a_monster_with_an_aifile_to_compa_12e9c8b6;
                DiffEmptyMessage = DiffSummary;
                ClearHumanDiff(DiffSummary);
                return;
            }

            string? referencePath = ReferenceMonsterPath(currentMonster.Id);
            DiffReferencePath = referencePath ?? string.Empty;
            CanRestoreVanillaAi = referencePath != null;
            if (referencePath == null)
            {
                DiffSummary = Strings.U_Ai_IndependentReferenceRequired;
                DiffEmptyMessage = DiffSummary;
                ClearHumanDiff(DiffSummary);
                return;
            }

            try
            {
                byte[]? originalAi = AiScript_File.SliceAiFileFromMonster(File.ReadAllBytes(referencePath));
                if (originalAi == null)
                {
                    DiffSummary = string.Format(Strings.U_Ai_VanillaRefNoAiFile, currentMonster.Id);
                    DiffEmptyMessage = DiffSummary;
                    CanRestoreVanillaAi = false;
                    ClearHumanDiff(DiffSummary);
                    return;
                }

                AiScriptFile original = AiScript_File.Read(originalAi);
                byte[] editedAi = AiScript_File.Write(selectedScript);
                AiScriptFile edited = AiScript_File.Read(editedAi);

                IReadOnlyList<AiDiffEntry> diff = AiScript_Diff.Compare(original, edited);
                foreach (AiDiffEntry entry in diff.Where(entry => entry.Type != AiDiffType.Unchanged))
                {
                    DiffEntries.Add(new AiDiffRow(entry));
                }
                RebuildHumanDiffEntries(DiffEntries.ToList(), original, edited);

                HasDiffEntries = DiffEntries.Count > 0;
                int added = DiffEntries.Count(row => row.Type == AiDiffType.Added);
                int removed = DiffEntries.Count(row => row.Type == AiDiffType.Removed);
                int modified = DiffEntries.Count(row => row.Type == AiDiffType.Modified);

                DiffSummary = HasDiffEntries
                    ? string.Format(Strings.U_Ai_DiffSummary, currentMonster.Id, DiffEntries.Count, added, removed, modified)
                    : string.Format(Strings.U_Ai_IdenticalToVanilla, currentMonster.Id);
                if (!HasDiffEntries && !originalAi.AsSpan().SequenceEqual(editedAi))
                {
                    DiffSummary = Strings.U_Ai_InstructionMatchOnly;
                    ClearHumanDiff(DiffSummary);
                }
                DiffEmptyMessage = HasDiffEntries
                    ? string.Empty
                    : Strings.F2_no_instruction_by_instruction_difference_1fac2e31;

                if (original.OriginalAiFileBytes.Length != edited.OriginalAiFileBytes.Length
                    || original.CodeLength != edited.CodeLength
                    || original.Instructions.Count != edited.Instructions.Count)
                {
                    DiffWarning = Strings.F2_structure_grew_or_changed_size_offset_ba_7b35602e;
                }
            }
            catch (Exception ex)
            {
                CanRestoreVanillaAi = referencePath != null;
                DiffSummary = string.Format(Strings.U_Ai_DiffAborted, ex.GetType().Name, ex.Message);
                DiffEmptyMessage = DiffSummary;
                ClearHumanDiff(DiffSummary);
            }
        }

        public void RequestRestoreVanillaFromDiff()
        {
            if (!CanRestoreVanillaAi)
            {
                DiffSummary = Strings.U_Ai_RestoreUnavailable;
                DiffEmptyMessage = DiffSummary;
                return;
            }

            if (!restoreVanillaDiffArmed)
            {
                restoreVanillaDiffArmed = true;
                RestoreVanillaDiffButtonText = Strings.U_Ai_ConfirmRestore;
                DiffSummary = string.Format(Strings.U_Ai_ConfirmRestoreMsg, SelectedMonster?.Id ?? "m???");
                DiffEmptyMessage = DiffSummary;
                return;
            }

            RestoreOriginalAi();
            string restoreMessage = CopyRestoreSummary;
            ResetRestoreVanillaConfirmation();
            LoadAiDiff();
            DiffSummary = $"{restoreMessage} · {DiffSummary}";
        }

        void ClearDiff(string summary)
        {
            DiffEntries.Clear();
            HasDiffEntries = false;
            CanRestoreVanillaAi = false;
            DiffWarning = string.Empty;
            DiffSummary = summary;
            DiffEmptyMessage = summary;
            ClearHumanDiff(summary);
            ResetRestoreVanillaConfirmation();
        }

        void ResetRestoreVanillaConfirmation()
        {
            restoreVanillaDiffArmed = false;
            RestoreVanillaDiffButtonText = Strings.U_Ai_RestoreVanilla;
        }

        void RefreshPendingEditPreview()
        {
            if (selectedScript == null || !selectedScript.HasScript)
            {
                PendingEditSummary = Strings.F2_no_editable_aifile_selected_186cdc99;
                PendingEditPreview = string.Empty;
                HasPendingEdits = false;
                return;
            }

            try
            {
                byte[] editedAi = AiScript_File.Write(selectedScript);
                List<ChangedByte> changes = BuildChangedBytes(selectedScript.OriginalAiFileBytes, editedAi);
                if (changes.Count == 0)
                {
                    PendingEditSummary = Strings.F2_no_pending_deltas_in_the_selected_aifile_16b8af72;
                    PendingEditPreview = string.Empty;
                    HasPendingEdits = false;
                    return;
                }

                HasPendingEdits = true;
                PendingEditSummary = string.Format(Strings.U_Ai_PendingBytes, changes.Count);
                PendingEditPreview = string.Join(Environment.NewLine,
                    changes.Take(12).Select(change =>
                        $"+0x{change.Offset:X4}: {selectedScript.OriginalAiFileBytes[change.Offset]:X2} -> {change.Value:X2}"));
                if (changes.Count > 12)
                    PendingEditPreview += Environment.NewLine + string.Format(Strings.U_Ai_MoreBytes, changes.Count - 12);
            }
            catch (Exception ex)
            {
                PendingEditSummary = string.Format(Strings.U_Ai_PreviewDeltaFailed, ex.GetType().Name);
                PendingEditPreview = string.Empty;
                HasPendingEdits = false;
            }
        }

        bool TryGetSelectedMonsterNumber(out int monsterNumber)
        {
            monsterNumber = -1;
            if (selectedMonster == null || string.IsNullOrWhiteSpace(selectedMonster.Id) || selectedMonster.Id.Length != 4)
                return false;
            return int.TryParse(selectedMonster.Id[1..], out monsterNumber);
        }

        RuntimeMonsterTargetRow? TryReadRuntimeTarget(int actorAddress, int slot, int monsterNumber)
        {
            byte[] actorBytes = MemSharp_Service.Instance.Read<byte>(actorAddress, MemoryMap.SIZE_BATTLE_CHR_ENTRY, false);
            if (actorBytes == null || actorBytes.Length < MemoryMap.SIZE_BATTLE_CHR_ENTRY)
                return null;

            using MemoryStream stream = new(actorBytes, writable: false);
            MemoryChr actor = BinaryMapping.ReadObject<MemoryChr>(stream);
            if (!actor.Stat_exist_flag || actor.Id <= 0)
                return null;

            int dictionaryId = actor.Id - 0x1000;
            if (dictionaryId != monsterNumber)
                return null;

            uint scriptChunks = unchecked((uint)actor.Ptr_script_chunks);
            RuntimeAiIdentity identity = InspectRuntimeAiIdentity(scriptChunks, selectedScript?.OriginalAiFileBytes);

            return new RuntimeMonsterTargetRow
            {
                Slot = slot,
                ActorAddress = actorAddress,
                RawId = (ushort)actor.Id,
                CurrentHp = actor.Current_hp,
                MaxHp = actor.Max_hp,
                ActionState = actor.Stat_action,
                ScriptChunks = scriptChunks,
                ScriptData = unchecked((uint)actor.Ptr_script_data),
                RuntimeAiIdentitySummary = identity.Summary,
                ExactDiskMatch = identity.ExactDiskMatch,
            };
        }

        RuntimeAiIdentity InspectRuntimeAiIdentity(uint scriptBase, byte[]? diskAi)
        {
            if (scriptBase == 0)
                return new RuntimeAiIdentity(false, Strings.U_Ai_ScrChunksNull);

            if (livePatchBackups.TryGetValue(scriptBase, out Dictionary<int, byte>? backup) && backup.Count > 0)
                return new RuntimeAiIdentity(false, string.Format(Strings.U_Ai_SessionPatchActive, backup.Count));

            if (diskAi == null || diskAi.Length == 0)
                return new RuntimeAiIdentity(false, Strings.U_Ai_DiskAiUnavailable);

            try
            {
                byte[] liveAi = MemSharp_Service.Instance.Read<byte>(unchecked((int)scriptBase), diskAi.Length, false);
                if (liveAi == null || liveAi.Length != diskAi.Length)
                    return new RuntimeAiIdentity(false, string.Format(Strings.U_Ai_CouldNotReadScrChunks, diskAi.Length, scriptBase));

                if (liveAi.SequenceEqual(diskAi))
                    return new RuntimeAiIdentity(true, Strings.U_Ai_LiveEqualsDisk);

                int firstDiff = -1;
                int diffCount = 0;
                for (int i = 0; i < diskAi.Length; i++)
                {
                    if (liveAi[i] == diskAi[i]) continue;
                    diffCount++;
                    if (firstDiff < 0) firstDiff = i;
                }

                return new RuntimeAiIdentity(false, string.Format(Strings.U_Ai_LiveDiverges, diffCount, firstDiff));
            }
            catch (Exception ex)
            {
                return new RuntimeAiIdentity(false, string.Format(Strings.U_Ai_RuntimeCompareFailed, ex.GetType().Name));
            }
        }

        static List<ChangedByte> BuildChangedBytes(byte[] before, byte[] after)
        {
            if (before.Length != after.Length)
                throw new InvalidOperationException($"AiFile length drift: before=0x{before.Length:X}, after=0x{after.Length:X}. Live operand edits must preserve length.");

            List<ChangedByte> changes = new();
            for (int i = 0; i < before.Length; i++)
                if (before[i] != after[i])
                    changes.Add(new ChangedByte(i, after[i]));
            return changes;
        }

        static string DescribeProbeFailure(FfxProbe_Service.ProbeResult result)
        {
            return $"status={result.Status} err=0x{result.Error:X8}";
        }
    }

    internal sealed class MonsterAiRow
    {
        public required string Path { get; init; }
        public required string Id { get; init; }                 // "m001"
        public string MonsterName { get; init; } = "";           // resolved name, e.g. "Mafdet" ("" if not indexed)
        public AiScriptFile? Script { get; init; }
        internal IReadOnlyList<AiAsmRow>? AssemblerDraft { get; set; }
        public bool HasScript => Script?.HasScript ?? false;

        string NameTag => string.IsNullOrEmpty(MonsterName) ? "" : $" · {MonsterName}";
        public string Title => HasScript ? $"{Id}{NameTag}  ({Script!.Workers.Count}w, {Script.Instructions.Count} ops)" : $"{Id}{NameTag}  ({Strings.AiEditorNoAi})";
        public string IndexLabel => Id.ToUpperInvariant();
        public string Summary => HasScript
            ? string.Format(Strings.AiEditorCodeSummary, Script!.CodeLength, Script.Workers.Count)
            : Strings.F2_stub_no_script_3e998903;
        public string SearchBlob => $"{Id} {MonsterName} {Path}";

        public static MonsterAiRow? TryLoad(string path)
        {
            try
            {
                byte[] monster = File.ReadAllBytes(path);
                byte[]? ai = AiScript_File.SliceAiFileFromMonster(monster);
                AiScriptFile? script = ai != null ? AiScript_File.Read(ai) : null;
                string id = System.IO.Path.GetFileNameWithoutExtension(path);
                return new MonsterAiRow
                {
                    Path = path,
                    Id = id,
                    MonsterName = ResolveName(id),
                    Script = script,
                };
            }
            catch { return null; }
        }

        // "m004" -> Monster_Dictionary[4] ("Mafdet"). The monster-file number equals the dictionary id in FFX
        // (same chain Aurora uses: formation dictionaryId -> m{id}.bin). Empty if the id isn't in the dictionary.
        static string ResolveName(string id)
        {
            if (id.Length > 1 && (id[0] == 'm' || id[0] == 'M')
                && int.TryParse(id.Substring(1), out int n)
                && FFXProjectEditor.FfxLib.Dictionaries.Monster_Dictionary.Instance.TryGetValue((short)n, out string? name))
                return name;
            return "";
        }
    }

    // One editable operand-bearing instruction. OperandHex binds TwoWay; setting it pushes the new u16
    // straight into the model instruction (AiScript_File.Write then re-emits it byte-exact). For a PUSHII whose
    // operand is a performCommand id (high nibble 3=character / 4=monster), a friendly command/ability dropdown is
    // shown (SelectedCommand) — it and the hex box are two views of the SAME instr.Operand (editing either updates
    // the other). Derives ObservableObject so the dropdown selection round-trips. The dropdown is ADVISORY: a
    // benign 0x3xxx/0x4xxx literal will also surface it; the hex box stays the source of truth.
    internal sealed class AiEditRow : ObservableObject
    {
        readonly AiInstruction instr;
        readonly Action? onChanged;
        public AiEditRow(AiInstruction i, Action? onChanged = null) { instr = i; this.onChanged = onChanged; }

        public string Label => $"0x{instr.Offset:X4}  {AiScript_File.Mnemonic(instr.Opcode)}";

        // Human-readable decode of the operand (CALLPOPA -> "Battle.<name>", jumps -> label, etc.) so the user
        // isn't staring at a raw hex id. Empty for opcodes with no operand. OpcodeHelp is the opcode's tooltip.
        // For command/ability operands (e.g. PUSHII 0x3049 = Firaga) show the NAME; otherwise the operand gloss.
        public string Meaning => IsCommandOperand && CommandGloss.Length > 0 ? $"⚔ {CommandGloss}" : AiScript_File.OperandGloss(instr);
        public string OpcodeHelp => AiScript_File.OpcodeHelp(instr.Opcode);
        public bool MatchesFilter(string f) =>
            (Label + " " + Meaning + " " + OperandHex).Contains(f, System.StringComparison.OrdinalIgnoreCase);

        public string OperandHex
        {
            get => instr.Operand.ToString("X4");
            set
            {
                string s = (value ?? "").Trim().Replace("0x", "");
                if (ushort.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out ushort v))
                {
                    if (instr.Operand == v) return;
                    instr.Operand = v;
                    onChanged?.Invoke();
                    OnPropertyChanged(nameof(SelectedCommand));
                    OnPropertyChanged(nameof(CommandGloss));
                    OnPropertyChanged(nameof(Meaning));            // the per-row gloss is bound to Meaning, not CommandGloss
                    OnPropertyChanged(nameof(IsCommandOperand));   // hex edit may cross the 0x3xxx/0x4xxx boundary -> show/hide dropdown
                }
            }
        }

        public bool IsCommandOperand => AiCommandId.IsCommandOperand(instr.Operand);
        public IReadOnlyList<AiCommandOption> CommandOptions => AiCommandId.AllOptions();
        public AiCommandOption? SelectedCommand
        {
            get => AiCommandId.OptionFor(instr.Operand);
            set
            {
                if (value == null || instr.Operand == value.Operand) return;
                instr.Operand = value.Operand;
                onChanged?.Invoke();
                OnPropertyChanged(nameof(OperandHex));
                OnPropertyChanged(nameof(SelectedCommand));
                OnPropertyChanged(nameof(CommandGloss));
                OnPropertyChanged(nameof(Meaning));
            }
        }
        public string CommandGloss { get { var d = AiCommandId.Decode(instr.Operand); return d.IsKnown ? d.Name : ""; } }
    }

    internal sealed class AiWorkerRow
    {
        public AiWorkerRow(AiWorker w, int scriptStart)
        {
            Index = w.Index;
            Title = $"Worker {w.Index}  ·  {w.InferredType ?? "?"} (inferred)";
            Summary = $"{w.Entrypoints.Count} entrypoints · {w.JumpTargets.Count} jumps · priv 0x{w.PrivateDataLength:X} · slot ~0x{w.InferredSlot:X2}";
            Entrypoints = string.Join("  ", w.Entrypoints.Select(e => $"0x{scriptStart + e:X}"));
            Jumps = string.Join("  ", w.JumpTargets.Select(j => $"0x{scriptStart + j:X}"));
        }

        public int Index { get; }
        public string Title { get; }
        public string Summary { get; }
        public string Entrypoints { get; }
        public string Jumps { get; }
    }

    internal sealed class AiDiffRow
    {
        static readonly IBrush AddedBrush = new SolidColorBrush(Color.FromRgb(24, 76, 50));
        static readonly IBrush RemovedBrush = new SolidColorBrush(Color.FromRgb(92, 36, 42));
        static readonly IBrush ModifiedBrush = new SolidColorBrush(Color.FromRgb(90, 75, 28));
        static readonly IBrush NeutralBrush = new SolidColorBrush(Color.FromRgb(35, 56, 72));

        public AiDiffRow(AiDiffEntry entry)
        {
            Type = entry.Type;
            WorkerIndex = entry.WorkerIndex;
            Offset = entry.Offset;
            OriginalText = entry.OriginalText ?? "-";
            EditedText = entry.EditedText ?? "-";
        }

        public AiDiffType Type { get; }
        public int WorkerIndex { get; }
        public int Offset { get; }
        public string OriginalText { get; }
        public string EditedText { get; }
        public string WorkerLabel => WorkerIndex < 0 ? "w?" : $"w{WorkerIndex}";
        public string OffsetLabel => $"0x{Offset:X4}";
        public string TypeLabel => Type switch
        {
            AiDiffType.Added => Strings.U_Ai_DiffAdded,
            AiDiffType.Removed => Strings.U_Ai_DiffRemoved,
            AiDiffType.Modified => Strings.U_Ai_DiffModified,
            _ => Strings.U_Ai_DiffUnchanged,
        };
        public IBrush TypeBrush => Type switch
        {
            AiDiffType.Added => AddedBrush,
            AiDiffType.Removed => RemovedBrush,
            AiDiffType.Modified => ModifiedBrush,
            _ => NeutralBrush,
        };
    }

    internal sealed class RuntimeMonsterTargetRow
    {
        public required int Slot { get; init; }
        public required int ActorAddress { get; init; }
        public required ushort RawId { get; init; }
        public required int CurrentHp { get; init; }
        public required int MaxHp { get; init; }
        public required byte ActionState { get; init; }
        public required uint ScriptChunks { get; init; }
        public required uint ScriptData { get; init; }
        public required string RuntimeAiIdentitySummary { get; init; }
        public required bool ExactDiskMatch { get; init; }

        public string Title => $"Enemy slot {Slot:D2} · raw 0x{RawId:X4}";
        public string Summary =>
            $"HP {CurrentHp}/{MaxHp} · Act {ActionState:X2}h · ScrChunks 0x{ScriptChunks:X8} · ScrData 0x{ScriptData:X8} · {(ExactDiskMatch ? "match exato" : "match pendente")} · {RuntimeAiIdentitySummary}";
    }

    readonly record struct ChangedByte(int Offset, byte Value);
    readonly record struct RuntimeAiIdentity(bool ExactDiskMatch, string Summary);
}

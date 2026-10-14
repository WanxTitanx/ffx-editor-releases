using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Battle;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.FfxLib.Memory;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils.Editing;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using Xe.BinaryMapper;

namespace FFXProjectEditor.Modules.BattleExplorer
{
    internal partial class BattleExplorer_DataModel : ObservableObject
    {
        sealed class FormationClipboardSnapshot
        {
            public required string SourceBattleId { get; init; }
            public required string SourceLabel { get; init; }
            public required ushort[] RawMonsterIds { get; init; }
        }

        static FormationClipboardSnapshot? FormationClipboard { get; set; }

        public ObservableCollection<BattleListEntry> LoadedBattles { get; } = new();
        public ObservableCollection<BattleListEntry> DisplayedBattles { get; } = new();
        public ObservableCollection<BattleChunkRow> ChunkRows { get; } = new();
        public ObservableCollection<BattleEncounterReferenceRow> EncounterReferences { get; } = new();
        public ObservableCollection<EditableFormationSlotRow> EditableFormationRows { get; } = new();
        public ObservableCollection<BattleFormationDiffRow> FormationDiffRows { get; } = new();
        public ObservableCollection<BattleRuntimeEnemyRow> RuntimeEnemyRows { get; } = new();
        public ObservableCollection<EncounterOpenerWriteRow> OpenerWrites { get; } = new();
        public ObservableCollection<BattleCompanionActivationRow> CompanionActivationRows { get; } = new();
        public IReadOnlyList<MonsterOption> MonsterOptions { get; }
        public IReadOnlyList<BattleListEntry> CloneSourceBattleOptions => LoadedBattles.Where(entry => entry.BattleFile?.Formation != null).ToList();

        private Dictionary<string, List<EncounterTable_Reference>> EncounterReferenceLookup { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        private BattleListEntry? CurrentBattle { get; set; }
        private BattleEncounterOpener_File? CurrentOpener { get; set; }

        [ObservableProperty] public string filterText = string.Empty;
        [ObservableProperty] public int totalBattleCount;
        [ObservableProperty] public int visibleBattleCount;
        [ObservableProperty] public string loadSummary = "Scanning battle files...";
        [ObservableProperty] public string selectedBattleId = "Select a battle";
        [ObservableProperty] public string selectedBattleLabel = "Pick a battle file to inspect its formation, chunk structure, and encounter-table footprint.";
        [ObservableProperty] public string selectedBattlePath = "-";
        [ObservableProperty] public string selectedStructureSummary = "-";
        [ObservableProperty] public string selectedScriptSummary = "-";
        [ObservableProperty] public string selectedTextSummary = "-";
        [ObservableProperty] public string selectedAreaSummary = "-";
        [ObservableProperty] public string selectedFlagsSummary = "-";
        [ObservableProperty] public string selectedUnknownSummary = "-";
        [ObservableProperty] public string selectedEncounterSummary = "No battle selected.";
        [ObservableProperty] public bool selectedBattleHasParseError;
        [ObservableProperty] public string selectedParseError = string.Empty;
        [ObservableProperty] public string chunkLegendSummary = "The chunk table is still the raw X-ray. Jarvis will now give you a label in PT-BR and a human explanation of what each block does.";
        [ObservableProperty] public bool hasEditableFormation;
        [ObservableProperty] public bool hasPendingFormationChanges;
        [ObservableProperty] public string selectedFormationEditorSummary = "Load a parsed battle file to start editing its initial 8-slot formation.";
        [ObservableProperty] public string selectedFormationDiffSummary = "No pending formation changes.";
        [ObservableProperty] public string selectedFormationSaveSummary = "The editor will only rewrite the proven formation chunk (chunk 2).";
        [ObservableProperty] public string formationClipboardSummary = "No formation copied yet.";
        [ObservableProperty] public string formationCloneSummary = "Copy the current formation, paste a copied one, or clone from another parsed battle.";
        [ObservableProperty] public string formationSlotUsageSummary = "No formation loaded.";
        [ObservableProperty] public bool hasEmptyFormationSlots;
        [ObservableProperty] public bool canQuickAddFormationMonster;
        [ObservableProperty] public bool canCopyFormation;
        [ObservableProperty] public bool canPasteFormation;
        [ObservableProperty] public bool canCloneFormationFromSource;
        [ObservableProperty] private ByteSnapshotEditorSession? formationEditSession;
        [ObservableProperty] private MonsterOption? selectedQuickAddMonster;
        [ObservableProperty] private BattleListEntry? selectedCloneSourceBattle;
        [ObservableProperty] public string runtimeProbeSummary = Strings.F2_open_the_game_force_or_enter_a_battle_an_db4b12a7;
        [ObservableProperty] public string runtimeBattleSummary = "-";
        [ObservableProperty] public string runtimeRoutingSummary = "-";
        [ObservableProperty] public string runtimeFrontlineSummary = "-";
        [ObservableProperty] public string runtimePositionNotes = "P1..P4 are still provisional floats. Jarvis will only call them X/Y/Z/scale when this is proven.";
        [ObservableProperty] public string openerSummary = "Load a parsed battle file to inspect the encounter opener / CTB seed (chunk0 HookStart).";
        [ObservableProperty] public string openerFormationMapping = "-";
        [ObservableProperty] public string openerGuardrail = "read-only · no recognized opener / CTB seed loaded.";
        [ObservableProperty] public string companionActivationSummary = "Load a parsed battle file to inspect hidden companion activation / summon-handoff.";
        [ObservableProperty] public string companionActivationBattleToken = "-";
        [ObservableProperty] public string companionActivationCoverage = "-";
        [ObservableProperty] public string companionActivationGuardrail = Strings.F2_read_only_no_m213_package_loaded_1691d047;

        public BattleExplorer_DataModel()
        {
            MonsterOptions = BuildMonsterOptions();
            SelectedQuickAddMonster = MonsterOptions.FirstOrDefault(option => !option.IsEmpty);
            Refresh();
        }

        public void Refresh()
        {
            LoadedBattles.Clear();
            DisplayedBattles.Clear();
            ChunkRows.Clear();
            EncounterReferences.Clear();
            ClearEditableFormationRows();
            FormationDiffRows.Clear();
            OpenerWrites.Clear();
            CompanionActivationRows.Clear();
            CurrentBattle = null;
            CurrentOpener = null;
            FormationEditSession = null;
            ResetSelection();

            if (!Project_Service.Instance.IsProjectLoaded)
            {
                LoadSummary = "Project root not loaded.";
                return;
            }

            LoadEncounterReferenceLookup();

            if (!Directory.Exists(Project_Service.Instance.Path_Btl))
            {
                LoadSummary = "Battle directory not found in the loaded workspace.";
                return;
            }

            foreach (string battleDirectory in Directory.GetDirectories(Project_Service.Instance.Path_Btl).OrderBy(path => path))
            {
                string? battleId = Path.GetFileName(battleDirectory);
                if (string.IsNullOrWhiteSpace(battleId))
                    continue;

                string battlePath = Project_Service.Instance.GetPathBattle(battleId);
                if (!File.Exists(battlePath))
                    continue;

                List<EncounterTable_Reference> refs = EncounterReferenceLookup.TryGetValue(battleId, out List<EncounterTable_Reference>? values)
                    ? values.OrderBy(value => value.TableIndex).ThenBy(value => value.GroupIndex).ToList()
                    : new List<EncounterTable_Reference>();

                try
                {
                    Battle_File battleFile = Battle_File.Read(battleId, File.ReadAllBytes(battlePath));
                    LoadedBattles.Add(new BattleListEntry
                    {
                        BattleId = battleId,
                        FilePath = battlePath,
                        BattleFile = battleFile,
                        FormationLabel = battleFile.FormationLabel,
                        ReferenceCount = refs.Count,
                        EncounterReferences = refs,
                        ParseError = null
                    });
                }
                catch (Exception ex)
                {
                    LoadedBattles.Add(new BattleListEntry
                    {
                        BattleId = battleId,
                        FilePath = battlePath,
                        BattleFile = null,
                        FormationLabel = "(Parse failed)",
                        ReferenceCount = refs.Count,
                        EncounterReferences = refs,
                        ParseError = ex.Message
                    });
                }
            }

            TotalBattleCount = LoadedBattles.Count;
            ApplyFilter();
            LoadSummary = $"Loaded {TotalBattleCount} battle files. Encounter table refs: {(EncounterReferenceLookup.Count > 0 ? "online" : "not available")}.";
        }

        public void ApplyFilter()
        {
            DisplayedBattles.Clear();

            string normalizedFilter = FilterText.Trim().ToLowerInvariant();
            foreach (BattleListEntry battle in LoadedBattles)
            {
                if (normalizedFilter.Length == 0 || battle.SearchBlob.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase))
                {
                    DisplayedBattles.Add(battle);
                }
            }

            VisibleBattleCount = DisplayedBattles.Count;
            OnPropertyChanged(nameof(CloneSourceBattleOptions));
        }

        public void LoadBattle(BattleListEntry? battle)
        {
            ChunkRows.Clear();
            EncounterReferences.Clear();
            ClearEditableFormationRows();
            FormationDiffRows.Clear();
            CurrentBattle = battle;
            FormationEditSession = null;
            SelectedBattleHasParseError = false;
            SelectedParseError = string.Empty;

            if (battle == null)
            {
                ResetSelection();
                return;
            }

            SelectedBattleId = battle.BattleId;
            SelectedBattleLabel = battle.FormationLabel;
            SelectedBattlePath = battle.FilePath;

            foreach (EncounterTable_Reference encounterReference in battle.EncounterReferences)
            {
                EncounterReferences.Add(new BattleEncounterReferenceRow(encounterReference));
            }

            if (!string.IsNullOrWhiteSpace(battle.ParseError) || battle.BattleFile == null)
            {
                SelectedBattleHasParseError = true;
                SelectedParseError = battle.ParseError ?? "Unknown parse failure.";
                SelectedStructureSummary = "Battle file could not be parsed.";
                SelectedScriptSummary = "-";
                SelectedTextSummary = "-";
                SelectedAreaSummary = "-";
                SelectedFlagsSummary = "-";
                SelectedUnknownSummary = "-";
                SelectedEncounterSummary = battle.ReferenceCount > 0
                    ? $"{battle.ReferenceCount} encounter placements still reference this battle id."
                    : "No encounter table reference was found for this battle id.";
                HasEditableFormation = false;
                HasPendingFormationChanges = false;
                SelectedFormationEditorSummary = "This battle could not be parsed, so the formation editor is offline.";
                SelectedFormationDiffSummary = "No pending formation changes.";
                SelectedFormationSaveSummary = "Fix the parse path before trying to write anything.";
                ChunkLegendSummary = "Parse failed, so the chunk legend remains only as a general reference.";
                OpenerSummary = "Battle parse failed, so the encounter opener / CTB seed reader stayed offline.";
                OpenerFormationMapping = "-";
                OpenerGuardrail = "read-only · parse failure";
                CompanionActivationSummary = "Battle parse failed, so the hidden companion activation reader stayed offline.";
                CompanionActivationBattleToken = "-";
                CompanionActivationCoverage = "-";
                CompanionActivationGuardrail = "read-only · parse failure";
                return;
            }

            Battle_File battleFile = battle.BattleFile;
            SelectedStructureSummary = $"File {battleFile.FileSize:N0} bytes | {battleFile.ChunkCount} chunks | {(battleFile.IsUnsafeFormat ? "unsafe 4-chunk variant" : "standard chunked battle file")}";
            SelectedScriptSummary = $"ATEL {battleFile.ScriptChunkLength:N0} bytes | Worker mapping {battleFile.WorkerMapChunkLength:N0} bytes";
            SelectedTextSummary = $"Primary text {(battleFile.HasPrimaryTextChunk ? $"{battleFile.PrimaryTextChunkLength:N0} bytes" : "missing")} | English text {(battleFile.HasEnglishTextChunk ? $"{battleFile.EnglishTextChunkLength:N0} bytes" : "missing")} | FTCX {(battleFile.HasFtcxChunk ? $"{battleFile.FtcxChunkLength:N0} bytes" : "missing")}";
            SelectedAreaSummary = battleFile.AreaSummary == null
                ? "No battle area/position chunk parsed."
                : $"Areas {battleFile.AreaSummary.AreaCount} | Party positions {battleFile.AreaSummary.PartyPositionCount} | Aeons {battleFile.AreaSummary.AeonPositionCount} | Monsters {battleFile.AreaSummary.MonsterPositionCount} | Extra target structs {battleFile.AreaSummary.UnknownTargetStructCount}";

            if (battleFile.Formation != null)
            {
                SelectedFlagsSummary = $"Voice lines {(battleFile.Formation.CommonVoiceLinesEnabled ? "enabled" : "disabled")} | Underwater {(battleFile.Formation.InWater ? "yes" : "no")}";
                SelectedUnknownSummary = $"Byte01 {FormatByteDebug(battleFile.Formation.UnknownByte01)} | Byte02 {FormatByteDebug(battleFile.Formation.UnknownByte02)} | InWaterByte {FormatByteDebug(battleFile.Formation.InWaterByte)} | Padding {FormatPadding(battleFile.Formation.PaddingBytes)}";

                foreach (Battle_FormationSlot slot in battleFile.Formation.Slots)
                {
                    EditableFormationSlotRow editableRow = new(slot, MonsterOptions);
                    editableRow.PropertyChanged += EditableFormationSlotRow_PropertyChanged;
                    EditableFormationRows.Add(editableRow);
                }

                HasEditableFormation = battleFile.CanWriteFormation;
                SelectedFormationEditorSummary = battleFile.CanWriteFormation
                    ? "Edit monster ids slot by slot. The save path only rewrites chunk 2, preserving ATEL, positions, and the rest of the battle file byte-for-byte. Current proven cap: 8 initial formation slots."
                    : "Formation data parsed, but this file layout is not currently considered safe for writing.";
                if (battleFile.CanWriteFormation)
                {
                    FormationEditSession = new ByteSnapshotEditorSession(
                        CaptureFormationSnapshot,
                        RestoreFormationSnapshot,
                        PersistFormationSnapshot,
                        "formation",
                        CaptureFormationSnapshot());
                }
                RebuildFormationDiff();
            }
            else
            {
                SelectedFlagsSummary = "No formation chunk parsed.";
                SelectedUnknownSummary = "No formation data available.";
                HasEditableFormation = false;
                HasPendingFormationChanges = false;
                SelectedFormationEditorSummary = "No editable formation chunk was found in this battle file.";
                SelectedFormationDiffSummary = "No pending formation changes.";
                SelectedFormationSaveSummary = "This battle is not eligible for formation writing.";
                FormationSlotUsageSummary = "No editable formation loaded.";
                HasEmptyFormationSlots = false;
                CanQuickAddFormationMonster = false;
            }

            foreach (Battle_Chunk chunk in battleFile.Chunks)
            {
                ChunkRows.Add(new BattleChunkRow(chunk));
            }

            SelectedEncounterSummary = battle.ReferenceCount > 0
                ? $"{battle.ReferenceCount} encounter placements reference this battle id."
                : "No encounter table reference was found. This may be a scripted-only or special battle.";
            ChunkLegendSummary = "Chunk table translated in PT-BR: script ATEL, workers, initial formation, areas/positions, texts and FTCX. Use the runtime probe to match this with the live battle.";

            PopulateOpenerData(battle.BattleId, battleFile.OriginalBytes, battleFile);
            PopulateCompanionActivationData(battle.BattleId, battleFile.OriginalBytes);
        }

        public void ResetSelectedFormation()
        {
            FormationEditSession?.Discard();
            SelectedFormationSaveSummary = "Pending formation edits were discarded and the live editor was reset from disk.";
        }

        public void UndoSelectedFormationEdit()
        {
            FormationEditSession?.Undo();
        }

        public void ClearFormationSlot(EditableFormationSlotRow row)
        {
            row.SetEmpty();
        }

        public void AddSelectedMonsterToFirstEmptySlot()
        {
            if (!CanQuickAddFormationMonster || SelectedQuickAddMonster == null)
                return;

            EditableFormationSlotRow? target = EditableFormationRows.FirstOrDefault(row => row.SelectedMonster.IsEmpty);
            if (target == null)
                return;

            target.SelectedMonster = SelectedQuickAddMonster;
        }

        public void FillEmptyFormationSlots()
        {
            if (!CanQuickAddFormationMonster || SelectedQuickAddMonster == null)
                return;

            foreach (EditableFormationSlotRow row in EditableFormationRows.Where(row => row.SelectedMonster.IsEmpty))
            {
                row.SelectedMonster = SelectedQuickAddMonster;
            }
        }

        public void CopyCurrentFormationToClipboard()
        {
            if (!CanCopyFormation || CurrentBattle?.BattleFile?.Formation == null)
                return;

            FormationClipboard = new FormationClipboardSnapshot
            {
                SourceBattleId = CurrentBattle.BattleId,
                SourceLabel = CurrentBattle.FormationLabel,
                RawMonsterIds = EditableFormationRows.Select(row => row.SelectedMonster.RawMonsterId).ToArray()
            };

            UpdateFormationClipboardState();
            SelectedFormationSaveSummary = $"Copied the current editable formation from {CurrentBattle.BattleId} into the local formation clipboard.";
        }

        public void PasteFormationFromClipboard()
        {
            if (!CanPasteFormation || FormationClipboard == null)
                return;

            ApplyFormationRawMonsterIds(FormationClipboard.RawMonsterIds);
            SelectedFormationSaveSummary = $"Pasted formation clipboard from {FormationClipboard.SourceBattleId} into the live editor. Review the diff before saving.";
        }

        public void CloneFormationFromSelectedBattle()
        {
            if (!CanCloneFormationFromSource || SelectedCloneSourceBattle?.BattleFile?.Formation == null)
                return;

            ushort[] sourceFormation = SelectedCloneSourceBattle.BattleFile.Formation.Slots
                .Select(slot => (ushort)(slot.RawMonsterId & 0xFFFF))
                .ToArray();

            ApplyFormationRawMonsterIds(sourceFormation);
            SelectedFormationSaveSummary = $"Cloned the on-disk formation from {SelectedCloneSourceBattle.BattleId} into the live editor. Review the diff before saving.";
        }

        public void SaveSelectedFormation()
        {
            if (!HasPendingFormationChanges)
            {
                SelectedFormationSaveSummary = "Nothing changed. The formation on disk already matches the editor.";
                return;
            }

            FormationEditSession?.Save();
            SelectedFormationSaveSummary = $"Saved formation changes into {CurrentBattle?.BattleId}.bin at {DateTime.Now:HH:mm:ss}.";
        }

        private void EditableFormationSlotRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(EditableFormationSlotRow.SelectedMonster) ||
                e.PropertyName == nameof(EditableFormationSlotRow.IsChanged))
            {
                FormationEditSession?.NotifyPotentialMutation();
                RebuildFormationDiff();
            }
        }

        private void RebuildFormationDiff()
        {
            FormationDiffRows.Clear();

            foreach (EditableFormationSlotRow row in EditableFormationRows.Where(row => row.IsChanged))
            {
                FormationDiffRows.Add(new BattleFormationDiffRow(row));
            }

            HasPendingFormationChanges = FormationDiffRows.Count > 0;
            SelectedFormationDiffSummary = HasPendingFormationChanges
                ? $"{FormationDiffRows.Count} pending slot changes will be written into the formation chunk."
                : "No pending formation changes.";

            if (!HasEditableFormation)
            {
                SelectedFormationSaveSummary = "This battle is not eligible for formation writing.";
            }
            else if (HasPendingFormationChanges)
            {
                SelectedFormationSaveSummary = "Review the before/after diff below, then save when you're ready.";
            }
            else
            {
                SelectedFormationSaveSummary = "Formation matches the file on disk.";
            }

            UpdateFormationSlotUsage();
        }

        private void LoadEncounterReferenceLookup()
        {
            EncounterReferenceLookup = new(StringComparer.OrdinalIgnoreCase);
            string encounterTablePath = Project_Service.Instance.Path_KernelEncounterTable;

            if (!File.Exists(encounterTablePath))
                return;

            try
            {
                EncounterTable_File encounterTableFile = EncounterTable_File.Read(File.ReadAllBytes(encounterTablePath));
                EncounterReferenceLookup = encounterTableFile.BuildReferenceLookup();
            }
            catch
            {
                EncounterReferenceLookup = new(StringComparer.OrdinalIgnoreCase);
            }
        }

        public void RefreshRuntimeProbe()
        {
            RuntimeEnemyRows.Clear();

            if (!Process_Service.Instance.IsAlive || !MemSharp_Service.Instance.IsAvailable())
            {
                RuntimeProbeSummary = Strings.F2_hook_offline_open_ffx_and_leave_the_proc_775d4f8d;
                RuntimeBattleSummary = "-";
                RuntimeRoutingSummary = "-";
                RuntimeFrontlineSummary = "-";
                return;
            }

            bool inBattle = MemSharp_Service.Instance.Read<byte>(MemoryMap.ADDR_BATTLE_ACTIVE) == 1;
            string battleName = MemSharp_Service.Instance.ReadString(MemoryMap.ADDR_BATTLE_NAME, 13).Trim('\0', ' ');
            byte encounterIndex = MemSharp_Service.Instance.Read<byte>(MemoryMap.ADDR_BATTLE_ENCOUNTER_INDEX);
            byte trigger = MemSharp_Service.Instance.Read<byte>(MemoryMap.ADDR_BATTLE_TRIGGER);

            MemoryBtl? battleState = null;
            byte[] btlBytes = MemSharp_Service.Instance.Read<byte>(MemoryMap.ADDR_BTL, 0x2200);
            if (btlBytes != null && btlBytes.Length > 0)
            {
                using MemoryStream stream = new(btlBytes);
                battleState = BinaryMapping.ReadObject<MemoryBtl>(stream);
            }

            string fieldName = battleState == null ? string.Empty : DecodeFieldName(battleState.field_name);
            string guessedBattleId = string.IsNullOrWhiteSpace(fieldName)
                ? "-"
                : $"{fieldName}_{battleState!.formation_idx:00}";

            string selectedVsRuntime = CurrentBattle == null
                ? Strings.U_Be_RuntimeNoBattleSelected
                : string.Equals(CurrentBattle.BattleId, guessedBattleId, StringComparison.OrdinalIgnoreCase)
                    ? string.Format(Strings.U_Be_RuntimeMatchesSelected, CurrentBattle.BattleId)
                    : string.Format(Strings.U_Be_RuntimeMismatch, guessedBattleId, CurrentBattle.BattleId);

            RuntimeProbeSummary = inBattle
                ? string.Format(Strings.U_Be_RuntimeBattleActive, selectedVsRuntime)
                : "Hook online, but no active battle now. Force a fight to start correlating chunk 3.";
            RuntimeBattleSummary = $"InBattle {inBattle} · BattleName '{battleName}' · EncounterIndex {encounterIndex:D3} ({encounterIndex:X2}h) · Trigger {trigger:X2}h";

            if (battleState != null)
            {
                RuntimeRoutingSummary =
                    $"FieldName {fieldName} · FieldIdx {battleState.field_idx} · Battlefield {battleState.battlefield_id:X4}h · Group {battleState.group_idx:D2} · Formation {battleState.formation_idx:D2} · Guess {guessedBattleId}";
                RuntimeFrontlineSummary = $"Frontline {FormatFrontlineSummary()} · Backline raw {FormatByteArray(battleState.backline)}";
            }
            else
            {
                RuntimeRoutingSummary = "BTL struct could not be decoded from memory.";
                RuntimeFrontlineSummary = "-";
            }

            int enemyListAddress = MemSharp_Service.Instance.Read<int>(MemoryMap.POINTER_BATTLE_ENEMY_LIST);
            for (int slot = 0; slot < 11; slot++)
            {
                int enemyAddress = enemyListAddress + slot * MemoryMap.SIZE_BATTLE_CHR_ENTRY;
                short rawMonsterId = MemSharp_Service.Instance.Read<short>(enemyAddress + 14, false);
                if (rawMonsterId <= 0)
                    continue;

                short dictionaryId = (short)(rawMonsterId - 0x1000);
                string monsterName = Monster_Dictionary.Instance.ContainsKey(dictionaryId)
                    ? Monster_Dictionary.Instance[dictionaryId]
                    : "<NOT INDEXED>";

                RuntimeEnemyRows.Add(new BattleRuntimeEnemyRow
                {
                    SlotLabel = $"Enemy {slot:D2}",
                    MonsterLabel = $"m{dictionaryId:D3} - {monsterName}",
                    RawHex = $"{rawMonsterId:X4}h",
                    P1 = MemSharp_Service.Instance.Read<float>(enemyAddress + 928, false),
                    P2 = MemSharp_Service.Instance.Read<float>(enemyAddress + 932, false),
                    P3 = MemSharp_Service.Instance.Read<float>(enemyAddress + 936, false),
                    P4 = MemSharp_Service.Instance.Read<float>(enemyAddress + 940, false)
                });
            }
        }

        private void ClearEditableFormationRows()
        {
            foreach (EditableFormationSlotRow row in EditableFormationRows)
            {
                row.PropertyChanged -= EditableFormationSlotRow_PropertyChanged;
            }

            EditableFormationRows.Clear();
        }

        private void ResetSelection()
        {
            SelectedBattleId = "Select a battle";
            SelectedBattleLabel = "Pick a battle file to inspect its formation, chunk structure, and encounter-table footprint.";
            SelectedBattlePath = "-";
            SelectedStructureSummary = "-";
            SelectedScriptSummary = "-";
            SelectedTextSummary = "-";
            SelectedAreaSummary = "-";
            SelectedFlagsSummary = "-";
            SelectedUnknownSummary = "-";
            SelectedEncounterSummary = "No battle selected.";
            SelectedBattleHasParseError = false;
            SelectedParseError = string.Empty;
            ChunkLegendSummary = Strings.F2_the_chunk_table_is_still_the_raw_x_ray_j_6ac6ab62;
            HasEditableFormation = false;
            HasPendingFormationChanges = false;
            SelectedFormationEditorSummary = "Load a parsed battle file to start editing its initial 8-slot formation.";
            SelectedFormationDiffSummary = "No pending formation changes.";
            SelectedFormationSaveSummary = "The editor will only rewrite the proven formation chunk (chunk 2).";
            SelectedCloneSourceBattle = null;
            FormationEditSession = null;
            CurrentOpener = null;
            OpenerWrites.Clear();
            OpenerSummary = "Load a parsed battle file to inspect the encounter opener / CTB seed (chunk0 HookStart).";
            OpenerFormationMapping = "-";
            OpenerGuardrail = "read-only · no recognized opener / CTB seed loaded.";
            CompanionActivationRows.Clear();
            CompanionActivationSummary = "Load a parsed battle file to inspect hidden companion activation / summon-handoff.";
            CompanionActivationBattleToken = "-";
            CompanionActivationCoverage = "-";
            CompanionActivationGuardrail = "read-only · no m213 package loaded.";
            UpdateFormationClipboardState();
            UpdateFormationSlotUsage();
            ResetRuntimeProbe();
        }

        private void PopulateOpenerData(string battleId, byte[] battleBytes, Battle_File battleFile)
        {
            CurrentOpener = BattleEncounterOpener_File.ReadFromBattleBin(battleId, battleBytes);
            OpenerWrites.Clear();

            if (CurrentOpener.HasRecognizedSeed)
            {
                OpenerSummary = CurrentOpener.HumanSummary;
                OpenerFormationMapping = BuildOpenerFormationMapping(battleFile);
                OpenerGuardrail = "read-only · encounter-driven proof (HookStart, not monster-local CTB). Writer closed.";

                foreach (EncounterOpenerWriteRow write in CurrentOpener.Writes)
                    OpenerWrites.Add(write);

                return;
            }

            OpenerSummary = "No recognizable encounter opener / CTB seed in this battle's chunk0.";
            if (CurrentOpener.Notes.Count > 0)
                OpenerSummary += " " + string.Join(" ", CurrentOpener.Notes);
            OpenerFormationMapping = "-";
            OpenerGuardrail = "read-only · unsupported pattern or no HookStart detected.";
        }

        private static string BuildOpenerFormationMapping(Battle_File battleFile)
        {
            if (battleFile.Formation == null)
                return "-";

            List<string> slotParts = new();
            foreach (Battle_FormationSlot slot in battleFile.Formation.Slots)
            {
                if (!slot.IsEmpty)
                    slotParts.Add($"slot{slot.SlotIndex}=m{slot.DictionaryId:D3}");
            }

            return slotParts.Count > 0
                ? "Formation: " + string.Join(", ", slotParts)
                : "-";
        }

        private void PopulateCompanionActivationData(string battleId, byte[] battleBytes)
        {
            CompanionActivationRows.Clear();

            BattleCompanionActivation_File activation = BattleCompanionActivation_File.ReadFromBattleBin(
                battleId,
                battleBytes,
                ResolveMonsterBinForCompanionActivation);

            CompanionActivationSummary = activation.HumanSummary;
            CompanionActivationBattleToken = activation.BattleTokenLabel;
            CompanionActivationCoverage = activation.ActivationCoverageSummary.Length > 0
                ? activation.ActivationCoverageSummary
                : activation.FormationSummary;

            foreach (BattleCompanionActivationRow row in activation.Rows)
                CompanionActivationRows.Add(row);

            if (activation.HasRecognizedPackage)
            {
                CompanionActivationGuardrail =
                    "read-only · m213 lane narrow · zero spawn refused · stronger reading = companion reveal/handoff · exact native flags of 0x408A not yet proven · no writer";
            }
            else if (activation.Notes.Count > 0)
            {
                CompanionActivationGuardrail = "read-only · " + string.Join(" ", activation.Notes);
            }
            else
            {
                CompanionActivationGuardrail = "read-only · no m213 package recognized in this battle";
            }
        }

        private static byte[]? ResolveMonsterBinForCompanionActivation(int monsterId)
        {
            try
            {
                string path = Project_Service.Instance.GetPathMon(monsterId);
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch
            {
                return null;
            }
        }

        byte[] CaptureFormationSnapshot()
        {
            byte[] bytes = new byte[EditableFormationRows.Count * 2];
            for (int i = 0; i < EditableFormationRows.Count; i++)
            {
                ushort rawMonsterId = EditableFormationRows[i].SelectedMonster.RawMonsterId;
                bytes[i * 2] = (byte)(rawMonsterId & 0xFF);
                bytes[i * 2 + 1] = (byte)((rawMonsterId >> 8) & 0xFF);
            }

            return bytes;
        }

        void RestoreFormationSnapshot(byte[] bytes)
        {
            ApplyFormationRawMonsterIds(ParseFormationSnapshot(bytes));
        }

        void PersistFormationSnapshot(byte[] bytes)
        {
            if (CurrentBattle?.BattleFile == null)
                throw new InvalidOperationException("No parsed battle is currently selected.");

            ushort[] rawMonsterIds = ParseFormationSnapshot(bytes);
            byte[] updatedBytes = CurrentBattle.BattleFile.WriteWithFormationSlots(rawMonsterIds);
            File.WriteAllBytes(CurrentBattle.FilePath, updatedBytes);

            CurrentBattle.BattleFile = Battle_File.Read(CurrentBattle.BattleId, updatedBytes);
            CurrentBattle.FormationLabel = CurrentBattle.BattleFile.FormationLabel;
            CurrentBattle.ParseError = null;
            SelectedBattleLabel = CurrentBattle.FormationLabel;

            if (CurrentBattle.BattleFile.Formation != null)
            {
                RebaseEditableFormationRows(CurrentBattle.BattleFile.Formation);
                SelectedFlagsSummary = $"Voice lines {(CurrentBattle.BattleFile.Formation.CommonVoiceLinesEnabled ? "enabled" : "disabled")} | Underwater {(CurrentBattle.BattleFile.Formation.InWater ? "yes" : "no")}";
                SelectedUnknownSummary = $"Byte01 {FormatByteDebug(CurrentBattle.BattleFile.Formation.UnknownByte01)} | Byte02 {FormatByteDebug(CurrentBattle.BattleFile.Formation.UnknownByte02)} | InWaterByte {FormatByteDebug(CurrentBattle.BattleFile.Formation.InWaterByte)} | Padding {FormatPadding(CurrentBattle.BattleFile.Formation.PaddingBytes)}";
            }

            RebuildFormationDiff();
        }

        private static IReadOnlyList<MonsterOption> BuildMonsterOptions()
        {
            List<MonsterOption> options =
            [
                new MonsterOption(0xFFFF, -1, "(Empty slot)")
            ];

            foreach ((short id, string name) in Monster_Dictionary.Instance.OrderBy(entry => entry.Key))
            {
                options.Add(new MonsterOption((ushort)id, id, $"m{id:D3} - {name}"));
            }

            return options;
        }

        private static string FormatByteDebug(byte value)
        {
            return $"{value:X2}h = {value:D3} ({Convert.ToString(value, 2).PadLeft(8, '0')})";
        }

        private static string FormatPadding(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return "-";

            return string.Join(" ", bytes.Select(value => value.ToString("X2") + "h"));
        }

        partial void OnSelectedQuickAddMonsterChanged(MonsterOption? value)
        {
            UpdateFormationSlotUsage();
        }

        partial void OnSelectedCloneSourceBattleChanged(BattleListEntry? value)
        {
            UpdateFormationClipboardState();
        }

        private void ApplyFormationRawMonsterIds(IReadOnlyList<ushort> rawMonsterIds)
        {
            if (rawMonsterIds.Count != EditableFormationRows.Count)
                throw new InvalidOperationException($"Formation apply expected {EditableFormationRows.Count} slots but received {rawMonsterIds.Count}.");

            for (int i = 0; i < rawMonsterIds.Count; i++)
            {
                EditableFormationSlotRow row = EditableFormationRows[i];
                MonsterOption? match = row.AvailableMonsters.FirstOrDefault(option => option.RawMonsterId == rawMonsterIds[i]);
                row.SelectedMonster = match ?? row.AvailableMonsters.First(option => option.IsEmpty);
            }
        }

        private static ushort[] ParseFormationSnapshot(byte[] bytes)
        {
            if (bytes.Length % 2 != 0)
                throw new InvalidOperationException("Formation snapshot byte length must be even.");

            ushort[] values = new ushort[bytes.Length / 2];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = (ushort)(bytes[i * 2] | (bytes[i * 2 + 1] << 8));
            }

            return values;
        }

        void RebaseEditableFormationRows(Battle_Formation formation)
        {
            for (int i = 0; i < EditableFormationRows.Count && i < formation.Slots.Count; i++)
            {
                EditableFormationRows[i].Rebase(formation.Slots[i].MonsterLabel);
            }
        }

        private void UpdateFormationSlotUsage()
        {
            int totalSlots = EditableFormationRows.Count;
            if (totalSlots == 0)
            {
                FormationSlotUsageSummary = "No formation loaded.";
                HasEmptyFormationSlots = false;
                CanQuickAddFormationMonster = false;
                CanCopyFormation = false;
                UpdateFormationClipboardState();
                return;
            }

            int usedSlots = EditableFormationRows.Count(row => !row.SelectedMonster.IsEmpty);
            int emptySlots = totalSlots - usedSlots;
            FormationSlotUsageSummary = $"Used slots {usedSlots}/{totalSlots} | Empty slots {emptySlots} | Proven initial formation cap: {totalSlots}";
            HasEmptyFormationSlots = emptySlots > 0;
            CanQuickAddFormationMonster = HasEditableFormation
                && HasEmptyFormationSlots
                && SelectedQuickAddMonster != null
                && !SelectedQuickAddMonster.IsEmpty;
            CanCopyFormation = HasEditableFormation && totalSlots == 8;
            UpdateFormationClipboardState();
        }

        private void UpdateFormationClipboardState()
        {
            CanPasteFormation = HasEditableFormation
                && EditableFormationRows.Count == 8
                && FormationClipboard != null
                && FormationClipboard.RawMonsterIds.Length == EditableFormationRows.Count;

            CanCloneFormationFromSource = HasEditableFormation
                && SelectedCloneSourceBattle?.BattleFile?.Formation != null
                && !string.Equals(SelectedCloneSourceBattle.BattleId, CurrentBattle?.BattleId, StringComparison.OrdinalIgnoreCase);

            FormationClipboardSummary = FormationClipboard == null
                ? "No formation copied yet."
                : $"Clipboard: {FormationClipboard.SourceBattleId} {FormationClipboard.SourceLabel}";

            FormationCloneSummary = SelectedCloneSourceBattle?.BattleFile?.Formation == null
                ? "Copy the current formation, paste a copied one, or clone from another parsed battle."
                : string.Equals(SelectedCloneSourceBattle.BattleId, CurrentBattle?.BattleId, StringComparison.OrdinalIgnoreCase)
                    ? "Clone source is the current battle. Pick another battle if you actually want to replace this formation."
                    : $"Clone source ready: {SelectedCloneSourceBattle.BattleId} {SelectedCloneSourceBattle.FormationLabel}";
        }

        private void ResetRuntimeProbe()
        {
            RuntimeEnemyRows.Clear();
            RuntimeProbeSummary = Strings.F2_open_the_game_force_or_enter_a_battle_an_beca10e6;
            RuntimeBattleSummary = "-";
            RuntimeRoutingSummary = "-";
            RuntimeFrontlineSummary = "-";
        }

        private string FormatFrontlineSummary()
        {
            List<string> labels = new();
            for (int slot = 0; slot < 3; slot++)
            {
                sbyte id = MemSharp_Service.Instance.Read<sbyte>(MemoryMap.ADDR_BATTLE_FORMATION_SLOTS + slot);
                string name = Character_Dictionary.Instance.ContainsKey(id)
                    ? Character_Dictionary.Instance[id]
                    : id == -1
                        ? "(empty)"
                        : "<unknown>";
                labels.Add($"{slot}:{id} {name}");
            }

            return string.Join(" | ", labels);
        }

        private static string DecodeFieldName(byte[]? bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return "-";

            return Encoding.UTF8.GetString(bytes).TrimEnd('\0', ' ');
        }

        private static string FormatByteArray(byte[]? bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return "-";

            return string.Join(", ", bytes.Select(value => value.ToString("X2") + "h"));
        }

        private static string FormatChunkLabelPt(int index)
        {
            return index switch
            {
                0 => Strings.U_Be_Chunk0Label,
                1 => Strings.U_Be_Chunk1Label,
                2 => Strings.U_Be_Chunk2Label,
                3 => Strings.U_Be_Chunk3Label,
                4 => Strings.U_Be_Chunk4Label,
                5 => Strings.U_Be_Chunk5Label,
                6 => Strings.U_Be_Chunk6Label,
                _ => $"Chunk {index:00}"
            };
        }

        private static string FormatChunkExplanation(int index)
        {
            return index switch
            {
                0 => Strings.U_Be_Chunk0Explanation,
                1 => Strings.U_Be_Chunk1Explanation,
                2 => Strings.U_Be_Chunk2Explanation,
                3 => Strings.U_Be_Chunk3Explanation,
                4 => Strings.U_Be_Chunk4Explanation,
                5 => Strings.U_Be_Chunk5Explanation,
                6 => Strings.U_Be_Chunk6Explanation,
                _ => Strings.U_Be_ChunkDefaultLabel
            };
        }

        private static string FormatChunkRisk(int index)
        {
            return index switch
            {
                2 => Strings.U_Be_ChunkRisk2,
                3 => Strings.U_Be_ChunkRisk3,
                0 or 1 => Strings.U_Be_ChunkRiskCritical,
                _ => Strings.U_Be_ChunkRiskRead
            };
        }

        public sealed class MonsterOption
        {
            public MonsterOption(ushort rawMonsterId, int dictionaryId, string displayName)
            {
                RawMonsterId = rawMonsterId;
                DictionaryId = dictionaryId;
                DisplayName = displayName;
            }

            public ushort RawMonsterId { get; }
            public int DictionaryId { get; }
            public string DisplayName { get; }
            public bool IsEmpty => RawMonsterId == 0xFFFF;
            public string RawHex => RawMonsterId == 0xFFFF ? "FFFFh" : $"{RawMonsterId:X4}h";
        }

        public sealed class BattleListEntry
        {
            public required string BattleId { get; set; }
            public required string FilePath { get; set; }
            public required string FormationLabel { get; set; }
            public required int ReferenceCount { get; set; }
            public required List<EncounterTable_Reference> EncounterReferences { get; set; }
            public required Battle_File? BattleFile { get; set; }
            public string? ParseError { get; set; }

            public string MetadataLabel => ParseError != null
                ? "Parse warning"
                : $"{ReferenceCount} encounter refs";

            public string SearchBlob => $"{BattleId} {FormationLabel}".ToLowerInvariant();
        }

        public sealed class BattleChunkRow
        {
            public BattleChunkRow(Battle_Chunk chunk)
            {
                Index = chunk.Index;
                Label = chunk.Label;
                LabelPt = FormatChunkLabelPt(chunk.Index);
                Explanation = FormatChunkExplanation(chunk.Index);
                Risk = FormatChunkRisk(chunk.Index);
                Offset = chunk.Offset == 0 ? "-" : $"{chunk.Offset:X8}h";
                Length = $"{chunk.Length:N0} bytes";
                Range = chunk.IsPresent ? $"{chunk.Offset:X8}h..{(chunk.Offset + chunk.Length - 1):X8}h" : "(empty)";
            }

            public int Index { get; }
            public string Label { get; }
            public string LabelPt { get; }
            public string Explanation { get; }
            public string Risk { get; }
            public string Offset { get; }
            public string Length { get; }
            public string Range { get; }
        }

        public sealed class BattleRuntimeEnemyRow
        {
            public required string SlotLabel { get; init; }
            public required string MonsterLabel { get; init; }
            public required string RawHex { get; init; }
            public required float P1 { get; init; }
            public required float P2 { get; init; }
            public required float P3 { get; init; }
            public required float P4 { get; init; }
            public string P1Label => P1.ToString("0.000");
            public string P2Label => P2.ToString("0.000");
            public string P3Label => P3.ToString("0.000");
            public string P4Label => P4.ToString("0.000");
        }

        public sealed partial class EditableFormationSlotRow : ObservableObject
        {
            ushort originalRawMonsterId;
            string originalMonsterLabel;

            public EditableFormationSlotRow(Battle_FormationSlot slot, IReadOnlyList<MonsterOption> availableMonsters)
            {
                SlotIndex = slot.SlotIndex;
                SlotLabel = slot.SlotLabel;
                originalRawMonsterId = (ushort)(slot.RawMonsterId & 0xFFFF);
                originalMonsterLabel = slot.MonsterLabel;
                AvailableMonsters = availableMonsters;
                SelectedMonster = ResolveInitialSelection(slot, availableMonsters);
            }

            public int SlotIndex { get; }
            public string SlotLabel { get; }
            public ushort OriginalRawMonsterId => originalRawMonsterId;
            public string OriginalMonsterLabel => originalMonsterLabel;
            public IReadOnlyList<MonsterOption> AvailableMonsters { get; }

            [ObservableProperty]
            private MonsterOption selectedMonster;

            public string OriginalRawHex => OriginalRawMonsterId == 0xFFFF ? "FFFFh" : $"{OriginalRawMonsterId:X4}h";
            public string CurrentRawHex => SelectedMonster.RawHex;
            public string CurrentMonsterLabel => SelectedMonster.DisplayName;
            public bool IsChanged => SelectedMonster.RawMonsterId != OriginalRawMonsterId;
            public bool CanOpenSelectedMonster => !SelectedMonster.IsEmpty && SelectedMonster.DictionaryId >= 0;
            public int SelectedMonsterIndex => SelectedMonster.DictionaryId;

            public void SetEmpty()
            {
                SelectedMonster = AvailableMonsters.First(option => option.IsEmpty);
            }

            public void Rebase(string savedMonsterLabel)
            {
                originalRawMonsterId = SelectedMonster.RawMonsterId;
                originalMonsterLabel = savedMonsterLabel;
                OnPropertyChanged(nameof(OriginalRawMonsterId));
                OnPropertyChanged(nameof(OriginalMonsterLabel));
                OnPropertyChanged(nameof(OriginalRawHex));
                OnPropertyChanged(nameof(IsChanged));
            }

            partial void OnSelectedMonsterChanged(MonsterOption value)
            {
                OnPropertyChanged(nameof(CurrentRawHex));
                OnPropertyChanged(nameof(CurrentMonsterLabel));
                OnPropertyChanged(nameof(IsChanged));
                OnPropertyChanged(nameof(CanOpenSelectedMonster));
                OnPropertyChanged(nameof(SelectedMonsterIndex));
            }

            private static MonsterOption ResolveInitialSelection(Battle_FormationSlot slot, IReadOnlyList<MonsterOption> availableMonsters)
            {
                if (slot.IsEmpty)
                    return availableMonsters.First(option => option.IsEmpty);

                MonsterOption? exact = availableMonsters.FirstOrDefault(option => option.RawMonsterId == (ushort)slot.RawMonsterId);
                if (exact != null)
                    return exact;

                MonsterOption? byDictionaryId = availableMonsters.FirstOrDefault(option => option.DictionaryId == slot.DictionaryId);
                if (byDictionaryId != null)
                    return byDictionaryId;

                return availableMonsters.First(option => option.IsEmpty);
            }
        }

        public sealed class BattleFormationDiffRow
        {
            public BattleFormationDiffRow(EditableFormationSlotRow row)
            {
                SlotLabel = row.SlotLabel;
                BeforeRawHex = row.OriginalRawHex;
                BeforeMonsterLabel = row.OriginalMonsterLabel;
                AfterRawHex = row.CurrentRawHex;
                AfterMonsterLabel = row.CurrentMonsterLabel;
            }

            public string SlotLabel { get; }
            public string BeforeRawHex { get; }
            public string BeforeMonsterLabel { get; }
            public string AfterRawHex { get; }
            public string AfterMonsterLabel { get; }
        }

        public sealed class BattleEncounterReferenceRow
        {
            public BattleEncounterReferenceRow(EncounterTable_Reference reference)
            {
                Map = reference.Map;
                TableLabel = $"Table {reference.TableIndex:000} / Id {reference.TableId:000}";
                GroupLabel = $"Group {reference.GroupIndex:00}";
                BattlefieldLabel = $"Battlefield {reference.Battlefield}";
                DangerLabel = $"Danger {reference.Danger}";
                WeightLabel = reference.TotalWeight > 0 ? $"Weight {reference.Weight}/{reference.TotalWeight}" : $"Weight {reference.Weight}";
            }

            public string Map { get; }
            public string TableLabel { get; }
            public string GroupLabel { get; }
            public string BattlefieldLabel { get; }
            public string DangerLabel { get; }
            public string WeightLabel { get; }
        }
    }
}

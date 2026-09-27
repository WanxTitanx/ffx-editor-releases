using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Battle;
using FFXProjectEditor.FfxLib.BattleMap;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.FfxLib.Monster;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.LiveBattleLab
{
    internal partial class LiveBattleLab_DataModel
    {
        const int SandboxMaxEnemySlots = 8;
        const string SandboxBackupSuffix = ".sandbox.bak";
        const string SandboxBattleBackupSuffix = ".sandbox.btl.bak";

        EncounterTable_File? sandboxEncounterTable;
        string? sandboxEncounterTablePath;
        string? sandboxProjectPath;

        public ObservableCollection<BattleSandboxMonsterChoice> SandboxMonsterChoices { get; } = new();
        public ObservableCollection<BattleSandboxSlot> SandboxSlots { get; } = new();
        public ObservableCollection<BattleSandboxRouteChoice> SandboxRouteChoices { get; } = new();
        public ObservableCollection<BattleSandboxPreviewDot> SandboxPreviewDots { get; } = new();

        public IReadOnlyList<BattleSandboxAiPresetChoice> SandboxAiPresetChoices { get; } =
        [
            new BattleSandboxAiPresetChoice(BattleSandboxAiPreset.Original, "Original"),
            new BattleSandboxAiPresetChoice(BattleSandboxAiPreset.Aggressive, "Agressivo"),
            new BattleSandboxAiPresetChoice(BattleSandboxAiPreset.Defensive, "Defensivo"),
            new BattleSandboxAiPresetChoice(BattleSandboxAiPreset.Enrage, "Enrage"),
        ];

        public IReadOnlyList<BattleSandboxLayoutPlanChoice> SandboxLayoutPlanChoices { get; } =
        [
            new BattleSandboxLayoutPlanChoice(BattleSandboxLayoutPlan.CompactRows, Strings.U_Llb_LayoutCompactRows),
            new BattleSandboxLayoutPlanChoice(BattleSandboxLayoutPlan.WideRows, Strings.U_Llb_LayoutWideRows),
            new BattleSandboxLayoutPlanChoice(BattleSandboxLayoutPlan.BossAndAdds, Strings.U_Llb_LayoutBossAdds),
            new BattleSandboxLayoutPlanChoice(BattleSandboxLayoutPlan.NarrowLane, Strings.U_Llb_LayoutNarrowLane),
            new BattleSandboxLayoutPlanChoice(BattleSandboxLayoutPlan.Ring, Strings.U_Llb_LayoutRing),
        ];

        [ObservableProperty] private BattleSandboxRouteChoice? selectedSandboxRouteChoice;
        [ObservableProperty] private BattleSandboxLayoutPlanChoice? selectedSandboxLayoutPlan;
        [ObservableProperty] private string sandboxPlannerSummary =
            "Offline plan: choose monsters, route, and spread. Force Battle still requires DINPUT8 probe.";
        [ObservableProperty] private string sandboxCameraRecommendation =
            "Suggested camera appears after there is an active monster.";
        [ObservableProperty] private string sandboxRouteSummary =
            "Manual route still not resolved for a battleId.";
        [ObservableProperty] private string sandboxStatus =
            "Build the test package, prepare the route, and fire with connected probe.";
        [ObservableProperty] private string sandboxLastSession =
            "No sandbox session sent yet.";

        public bool SandboxCanPrepareRoute =>
            Project_Service.Instance.IsProjectLoaded &&
            ActiveSandboxSlots().Count > 0 &&
            TryParseForceInputs(out _, out _, out _, out _);

        public bool SandboxCanForceBattle =>
            SandboxCanPrepareRoute &&
            HookAvailable &&
            !InBattle &&
            FfxProbe_Service.Instance.IsHooked;

        void SeedBattleSandbox()
        {
            RefreshSandboxProjectDataIfNeeded(force: true);

            if (SandboxSlots.Count == 0)
            {
                for (int i = 1; i <= SandboxMaxEnemySlots; i++)
                {
                    SandboxSlots.Add(new BattleSandboxSlot(
                        i,
                        SandboxMonsterChoices,
                        SandboxAiPresetChoices,
                        ReadMonsterHp,
                        NotifySandboxSlotChanged));
                }
            }

            BattleSandboxAiPresetChoice? originalPreset = SandboxAiPresetChoices.FirstOrDefault();
            foreach (BattleSandboxSlot slot in SandboxSlots)
            {
                slot.SelectedMonster = BattleSandboxMonsterChoice.Empty;
                slot.SelectedAiPreset = originalPreset;
            }

            SandboxSlots[0].SelectedMonster = SandboxMonsterChoices.FirstOrDefault(choice => choice.Id == 14)
                                              ?? SandboxMonsterChoices.FirstOrDefault(choice => !choice.IsEmpty)
                                              ?? BattleSandboxMonsterChoice.Empty;

            SelectedSandboxLayoutPlan = SandboxLayoutPlanChoices.FirstOrDefault();
            SelectedSandboxRouteChoice = SandboxRouteChoices.FirstOrDefault(choice => !choice.IsManual)
                                         ?? SandboxRouteChoices.FirstOrDefault();

            RefreshSandboxHpFields();
            RefreshSandboxRouteSummary();
            RefreshSandboxPlanner();
            NotifySandboxAvailability();
        }

        void RefreshSandboxProjectDataIfNeeded(bool force = false)
        {
            string? projectPath = Project_Service.Instance.ProjectPath;
            if (!force && string.Equals(sandboxProjectPath, projectPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            sandboxProjectPath = projectPath;
            LoadSandboxMonsterChoices();
            LoadSandboxRouteChoices();

            foreach (BattleSandboxSlot slot in SandboxSlots)
            {
                slot.MonsterChoices = SandboxMonsterChoices;
            }
        }

        void LoadSandboxMonsterChoices()
        {
            SandboxMonsterChoices.Clear();
            SandboxMonsterChoices.Add(BattleSandboxMonsterChoice.Empty);

            if (!Project_Service.Instance.IsProjectLoaded || !Directory.Exists(Project_Service.Instance.Path_Mon))
            {
                return;
            }

            foreach (string path in Directory.EnumerateFiles(Project_Service.Instance.Path_Mon, "m*.bin", SearchOption.AllDirectories)
                         .Where(IsMonsterPath)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                int id = int.Parse(Path.GetFileNameWithoutExtension(path)[1..], CultureInfo.InvariantCulture);
                string name = Monster_Dictionary.Instance.TryGetValue((short)id, out string? n) ? n : "<Custom>";
                SandboxMonsterChoices.Add(new BattleSandboxMonsterChoice(id, name, path));
            }
        }

        void LoadSandboxRouteChoices()
        {
            SandboxRouteChoices.Clear();
            SandboxRouteChoices.Add(BattleSandboxRouteChoice.Manual);

            if (!TryGetSandboxEncounterTable(out EncounterTable_File? encounterTable, out _))
            {
                return;
            }

            foreach (EncounterTable_Entry table in encounterTable.Tables.OrderBy(t => t.TableIndex))
            {
                foreach (EncounterTable_Group group in table.Groups.OrderBy(g => g.GroupIndex))
                {
                    foreach (EncounterTable_Formation formation in group.Formations.OrderBy(f => f.FormationId))
                    {
                        SandboxRouteChoices.Add(new BattleSandboxRouteChoice(
                            table.TableIndex,
                            group.GroupIndex,
                            formation.FormationId,
                            table.Map,
                            formation.BattleId,
                            group.Battlefield,
                            group.Danger,
                            formation.Weight));
                    }
                }
            }
        }

        partial void OnSelectedSandboxRouteChoiceChanged(BattleSandboxRouteChoice? value)
        {
            if (value is { IsManual: false })
            {
                ForceFieldInput = value.FieldIndex.ToString(CultureInfo.InvariantCulture);
                ForceGroupInput = value.GroupIndex.ToString(CultureInfo.InvariantCulture);
                ForceFormationInput = value.FormationId.ToString(CultureInfo.InvariantCulture);
            }

            RefreshSandboxRouteSummary();
            RefreshSandboxPlanner();
            NotifySandboxAvailability();
        }

        partial void OnSelectedSandboxLayoutPlanChanged(BattleSandboxLayoutPlanChoice? value)
        {
            RefreshSandboxPlanner();
            NotifySandboxAvailability();
        }

        partial void OnHookAvailableChanged(bool value) => NotifySandboxAvailability();
        partial void OnInBattleChanged(bool value) => NotifySandboxAvailability();
        partial void OnForceFieldInputChanged(string value)
        {
            RefreshSandboxRouteSummary();
            RefreshSandboxPlanner();
            NotifySandboxAvailability();
        }

        partial void OnForceGroupInputChanged(string value)
        {
            RefreshSandboxRouteSummary();
            RefreshSandboxPlanner();
            NotifySandboxAvailability();
        }

        partial void OnForceFormationInputChanged(string value)
        {
            RefreshSandboxRouteSummary();
            RefreshSandboxPlanner();
            NotifySandboxAvailability();
        }

        void NotifySandboxSlotChanged()
        {
            RefreshSandboxPlanner();
            NotifySandboxAvailability();
        }

        void NotifySandboxAvailability()
        {
            OnPropertyChanged(nameof(SandboxCanPrepareRoute));
            OnPropertyChanged(nameof(SandboxCanForceBattle));
        }

        void RefreshSandboxHpFields()
        {
            foreach (BattleSandboxSlot slot in SandboxSlots)
            {
                slot.RefreshHpFromMonster();
            }
        }

        static string ReadMonsterHp(BattleSandboxMonsterChoice? choice)
        {
            if (choice == null || choice.IsEmpty || string.IsNullOrWhiteSpace(choice.Path) || !File.Exists(choice.Path))
            {
                return "";
            }

            try
            {
                Monster_File monster = Monster_File.Read(File.ReadAllBytes(choice.Path));
                return monster.StatSheetFile?.Hp.ToString(CultureInfo.InvariantCulture) ?? "";
            }
            catch
            {
                return "";
            }
        }

        public void RecalculateSandboxPlan()
        {
            RefreshSandboxRouteSummary();
            RefreshSandboxPlanner();
            int activeCount = ActiveSandboxSlots().Count;
            SandboxStatus = activeCount == 0
                ? "Empty plan: choose at least one monster."
                : $"Plano recalculado para {activeCount}/8 monstro(s). {SandboxCameraRecommendation}";
        }

        public void ApplySandboxChanges()
        {
            List<string> notes = new();
            foreach (BattleSandboxSlot slot in ActiveSandboxSlots())
            {
                ApplySandboxSlot(slot.SelectedMonster, slot.HpText, slot.SelectedAiPreset, slot.ShortLabel, notes);
            }

            SandboxStatus = notes.Count == 0
                ? "Nothing applied: choose at least one monster or change HP/AI."
                : "Sandbox aplicado: " + string.Join(" · ", notes);
            RefreshSandboxHpFields();
            RefreshSandboxPlanner();
        }

        public void PrepareSandboxRoute()
        {
            bool ok = TryPrepareSandboxRoute(out string message, out _);
            SandboxStatus = ok ? message : Strings.U_Lbl_RouteNotPrepared + message;
            RefreshSandboxRouteSummary();
            RefreshSandboxPlanner();
        }

        public void ForceSandboxBattle()
        {
            if (!SandboxCanForceBattle)
            {
                SandboxStatus = !FfxProbe_Service.Instance.IsHooked
                    ? "Battle Sandbox requires the DINPUT8 probe to be alive. Without probe, you can only build/apply files offline."
                    : "Battle Sandbox is not armed: check active monsters, route field/group/formation, and whether an active battle already exists.";
                return;
            }

            ApplySandboxChanges();

            if (!TryPrepareSandboxRoute(out string prepareMessage, out BattleSandboxRouteResolution? route))
            {
                SandboxStatus = "Force abortado: " + prepareMessage;
                return;
            }

            if (!TryParseForceInputs(out ushort field, out byte group, out byte formation, out string error))
            {
                SandboxStatus = string.Format(Strings.U_Llb_ForceAborted, error);
                return;
            }

            ExecuteEncounterRoll(field, group, formation, "battle sandbox authoring loop");
            string slots = string.Join(", ", ActiveSandboxSlots().Select(slot => $"{slot.ShortLabel}:{slot.SelectedMonster!.IdLabel}"));
            SandboxStatus = prepareMessage;
            SandboxLastSession =
                string.Format(Strings.U_Llb_LastSession,
                    route?.RouteLabel ?? string.Format(Strings.U_Llb_FieldGroupFormation, field, group, formation),
                    slots);
        }

        bool TryPrepareSandboxRoute(out string message, out BattleSandboxRouteResolution? route)
        {
            route = null;
            List<BattleSandboxSlot> activeSlots = ActiveSandboxSlots();
            if (activeSlots.Count == 0)
            {
                message = "choose at least one monster.";
                return false;
            }

            if (activeSlots.Count > SandboxMaxEnemySlots)
            {
                message = "the practical limit of this screen is 8 formation slots.";
                return false;
            }

            if (!TryResolveSandboxRoute(out route, out string routeError))
            {
                message = routeError;
                return false;
            }

            if (!File.Exists(route.BattlePath))
            {
                message = string.Format(Strings.U_Llb_BattleIdNotFound, route.BattleId, route.BattlePath);
                return false;
            }

            try
            {
                byte[] original = File.ReadAllBytes(route.BattlePath);
                byte[] working = original;
                int growSteps = 0;

                while (CountLiveFormationSlots(working) < activeSlots.Count)
                {
                    if (!BattleArenaAuthor.CanAdd(working, out string reason))
                    {
                        message = string.Format(Strings.U_Llb_ArenaCannotFit, activeSlots.Count, reason);
                        return false;
                    }

                    working = BattleArenaAuthor.AddMonsterCloneLast(working);
                    growSteps++;
                    if (growSteps > SandboxMaxEnemySlots)
                    {
                        message = Strings.U_Llb_GrowAborted;
                        return false;
                    }
                }

                int anchorCount = CountMonsterLiveAnchors(working);
                if (anchorCount < activeSlots.Count)
                {
                    message = string.Format(Strings.U_Llb_ArenaHasAnchors, anchorCount, activeSlots.Count);
                    return false;
                }

                ushort[] rawSlots = Enumerable.Repeat((ushort)0xFFFF, SandboxMaxEnemySlots).ToArray();
                for (int i = 0; i < activeSlots.Count; i++)
                {
                    rawSlots[i] = BuildFormationRawId(activeSlots[i].SelectedMonster!);
                }

                working = Battle_File.Read(route.BattleId, working).WriteWithFormationSlots(rawSlots);

                if (anchorCount > 0)
                {
                    List<(float X, float Y, float Z)> coords = ReadMonsterLiveCoords(route.BattleId, working);
                    if (coords.Count >= activeSlots.Count)
                    {
                        IReadOnlyList<(float X, float Y, float Z)> planned = BuildPlannedBattleCoords(activeSlots.Count, SelectedSandboxLayoutPlan?.Plan ?? BattleSandboxLayoutPlan.CompactRows);
                        for (int i = 0; i < planned.Count; i++)
                        {
                            coords[i] = planned[i];
                        }

                        working = Battle_File.Read(route.BattleId, working)
                            .WriteWithMonsterPositions(0, BattleArena_AnchorRole.MonsterLive, coords);
                    }
                }

                if (!working.SequenceEqual(original))
                {
                    string backup = route.BattlePath + SandboxBattleBackupSuffix;
                    if (!File.Exists(backup))
                    {
                        File.Copy(route.BattlePath, backup, overwrite: false);
                    }

                    File.WriteAllBytes(route.BattlePath, working);
                }

                string backupNote = File.Exists(route.BattlePath + SandboxBattleBackupSuffix)
                    ? $"backup {Path.GetFileName(route.BattlePath + SandboxBattleBackupSuffix)}"
                    : "sem diff novo";
                message =
                    $"Route preparado em {route.BattleId}: {activeSlots.Count}/8 slot(s), spread {SelectedSandboxLayoutPlan?.Label ?? "2 linhas compactas"}, {backupNote}. " +
                    $"{SandboxCameraRecommendation}";
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        void ApplySandboxSlot(
            BattleSandboxMonsterChoice? choice,
            string hpText,
            BattleSandboxAiPresetChoice? preset,
            string slot,
            List<string> notes)
        {
            if (choice == null || choice.IsEmpty || string.IsNullOrWhiteSpace(choice.Path) || !File.Exists(choice.Path))
            {
                return;
            }

            try
            {
                byte[] original = File.ReadAllBytes(choice.Path);
                byte[] current = original;
                Monster_File monster = Monster_File.Read(current);

                if (uint.TryParse((hpText ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint hp)
                    && monster.StatSheetFile != null
                    && monster.StatSheetFile.Hp != hp)
                {
                    monster.StatSheetFile.Hp = hp;
                    current = monster.Write();
                }

                BattleSandboxAiPreset aiPreset = preset?.Preset ?? BattleSandboxAiPreset.Original;
                if (aiPreset != BattleSandboxAiPreset.Original)
                {
                    current = ApplyQuickAiPreset(current, aiPreset, out string aiNote);
                    notes.Add($"{slot}:{choice.IdLabel} {aiNote}");
                }
                else if (!current.SequenceEqual(original))
                {
                    notes.Add($"{slot}:{choice.IdLabel} HP {hp}");
                }

                if (!current.SequenceEqual(original))
                {
                    string backup = choice.Path + SandboxBackupSuffix;
                    if (!File.Exists(backup))
                    {
                        File.Copy(choice.Path, backup, overwrite: false);
                    }

                    File.WriteAllBytes(choice.Path, current);
                }
            }
            catch (Exception ex)
            {
                notes.Add($"{slot}:{choice.IdLabel} falhou ({ex.Message})");
            }
        }

        byte[] ApplyQuickAiPreset(byte[] monsterBytes, BattleSandboxAiPreset preset, out string note)
        {
            note = preset.ToString();
            byte[]? aiBytes = AiScript_File.SliceAiFileFromMonster(monsterBytes);
            if (aiBytes == null)
            {
                note = "sem AiFile";
                return monsterBytes;
            }

            AiScriptFile script = AiScript_File.Read(aiBytes);
            if (!script.HasScript)
            {
                note = "AI stub";
                return monsterBytes;
            }

            if (!AiWorkerMapping.TryResolveCombatOnTurn(monsterBytes, script, out AiEventHook hook, out string why))
            {
                note = string.Format(Strings.U_Llb_OnTurnNotResolved, why);
                return monsterBytes;
            }

            byte[] newAi = preset switch
            {
                BattleSandboxAiPreset.Aggressive => BuildAggressiveAi(script, hook, out note),
                BattleSandboxAiPreset.Defensive => BuildDefensiveAi(script, hook, out note),
                BattleSandboxAiPreset.Enrage => BuildEnrageAi(script, hook, out note),
                _ => script.OriginalAiFileBytes,
            };

            if (newAi.SequenceEqual(script.OriginalAiFileBytes))
            {
                return monsterBytes;
            }

            AiValidationReport validation = AiValidator.ValidateRebuilt(newAi, script.OriginalAiFileBytes.Length);
            if (!validation.IsValid)
            {
                note = Strings.U_Lbl_ValidationBlocked + validation.Errors.FirstOrDefault()?.Message;
                return monsterBytes;
            }

            return AiScript_File.SpliceAiFileIntoMonsterGrow(monsterBytes, newAi);
        }

        static byte[] BuildAggressiveAi(AiScriptFile script, AiEventHook hook, out string note)
        {
            AiDetectedAction? action = AiAutomation.DetectActions(script)
                .FirstOrDefault(a => a.Kind == AiActionKind.Command && a.Removable);
            if (action == null)
            {
                note = Strings.U_Llb_AggressiveNoSimple;
                return script.OriginalAiFileBytes;
            }

            note = Strings.U_Llb_AggressiveNote;
            return AiAutomation.AddAbilityFromActionTemplate(script, action, action.CommandOperand, random: false, k: 0, hook.WorkerIndex, hook.EntrypointIndex);
        }

        static byte[] BuildDefensiveAi(AiScriptFile script, AiEventHook hook, out string note)
        {
            byte[] protect = AiAutomation.AddSelfBuff(script, 0x31, random: false, k: 0, hook.WorkerIndex, hook.EntrypointIndex);
            AiScriptFile afterProtect = AiScript_File.Read(protect);
            byte[] haste = AiAutomation.AddSelfBuff(afterProtect, 0x38, random: false, k: 0, hook.WorkerIndex, hook.EntrypointIndex);
            note = Strings.U_Llb_DefensiveNote;
            return haste;
        }

        static byte[] BuildEnrageAi(AiScriptFile script, AiEventHook hook, out string note)
        {
            AiCommandOption? haste = AiCommandId.AllOptions()
                .FirstOrDefault(o => o.Name.Contains("Haste", StringComparison.OrdinalIgnoreCase));
            AiSnippet? snippet = AiSnippetLibrary.ById("guard-hp-below-pct-force-cmd");
            if (haste == null || snippet == null)
            {
                note = Strings.U_Llb_EnrageNoSnippet;
                return script.OriginalAiFileBytes;
            }

            var (guard, action) = snippet.ExpandGuarded(new AiSnippetArgs(haste.Operand, 50, 0));
            note = Strings.U_Llb_EnrageNote;
            return AiScript_File.AppendGuardedAction(script, hook.WorkerIndex, hook.EntrypointIndex, guard, action);
        }

        List<BattleSandboxSlot> ActiveSandboxSlots() =>
            SandboxSlots.Where(slot => slot.SelectedMonster is { IsEmpty: false }).OrderBy(slot => slot.Index).ToList();

        void RefreshSandboxRouteSummary()
        {
            if (TryResolveSandboxRoute(out BattleSandboxRouteResolution? route, out string error))
            {
                SandboxRouteSummary =
                    $"{route.BattleId} · map {route.Map} · field {route.FieldIndex:D3} / group {route.GroupIndex:D2} / formation {route.FormationId:D2} · " +
                    $"battlefield {route.Battlefield:X4}h · danger {route.Danger:D3}.";
            }
            else
            {
                SandboxRouteSummary = "Route manual: " + error;
            }
        }

        void RefreshSandboxPlanner()
        {
            List<BattleSandboxSlot> active = ActiveSandboxSlots();
            SandboxPreviewDots.Clear();

            if (active.Count == 0)
            {
                SandboxPlannerSummary = Strings.U_Lbl_NoEnemies;
                SandboxCameraRecommendation = "Suggested camera appears after there is an active monster.";
                return;
            }

            BattleSandboxLayoutPlan plan = SelectedSandboxLayoutPlan?.Plan ?? BattleSandboxLayoutPlan.CompactRows;
            IReadOnlyList<(float X, float Y, float Z)> coords = BuildPlannedBattleCoords(active.Count, plan);
            for (int i = 0; i < active.Count; i++)
            {
                (float x, float y, float z) = coords[i];
                SandboxPreviewDots.Add(new BattleSandboxPreviewDot(
                    active[i].ShortLabel,
                    active[i].SelectedMonster?.IdLabel ?? "-",
                    $"X {x:0.0} · Z {z:0.0}"));
            }

            float minX = coords.Min(p => p.X);
            float maxX = coords.Max(p => p.X);
            float minZ = coords.Min(p => p.Z);
            float maxZ = coords.Max(p => p.Z);
            float span = Math.Max(maxX - minX, maxZ - minZ);
            float centerX = coords.Average(p => p.X);
            float centerZ = coords.Average(p => p.Z);
            float distance = Clamp(14f + span * 1.35f + active.Count * 0.6f, 16f, 44f);
            float height = Clamp(7f + active.Count * 0.55f, 8f, 15f);

            SandboxPlannerSummary =
                string.Format(Strings.U_Llb_PlannerSummary, active.Count,
                    SelectedSandboxLayoutPlan?.Label ?? Strings.U_Llb_LayoutCompactRows, centerX, centerZ, span / 2f);
            SandboxCameraRecommendation =
                string.Format(Strings.U_Llb_CameraRecommendation, centerX, centerZ, distance, height);
        }

        bool TryResolveSandboxRoute(out BattleSandboxRouteResolution? route, out string error)
        {
            route = null;
            if (!TryParseForceInputs(out ushort field, out byte group, out byte formation, out error))
            {
                return false;
            }

            if (!TryGetSandboxEncounterTable(out EncounterTable_File? encounterTable, out error))
            {
                return false;
            }

            EncounterTable_Entry? table = encounterTable.Tables.FirstOrDefault(t => t.TableIndex == field);
            if (table == null)
            {
                error = string.Format(Strings.U_Llb_FieldTableMissing, field);
                return false;
            }

            EncounterTable_Group? groupRow = table.Groups.FirstOrDefault(g => g.GroupIndex == group);
            if (groupRow == null)
            {
                error = string.Format(Strings.U_Llb_GroupMissing, group, field);
                return false;
            }

            EncounterTable_Formation? formationRow = groupRow.Formations.FirstOrDefault(f => f.FormationId == formation);
            if (formationRow == null)
            {
                error = string.Format(Strings.U_Llb_FormationMissing, formation, field, group);
                return false;
            }

            string battlePath = Project_Service.Instance.GetPathBattle(formationRow.BattleId);
            route = new BattleSandboxRouteResolution(
                field,
                group,
                formation,
                table.Map,
                formationRow.BattleId,
                battlePath,
                groupRow.Battlefield,
                groupRow.Danger,
                formationRow.Weight);
            error = string.Empty;
            return true;
        }

        bool TryGetSandboxEncounterTable(out EncounterTable_File? encounterTable, out string error)
        {
            encounterTable = null;
            if (!Project_Service.Instance.IsProjectLoaded)
            {
                error = Strings.F2_load_the_workspace_master_first_d8b7d870;
                return false;
            }

            string path = Project_Service.Instance.Path_KernelEncounterTable;
            if (!File.Exists(path))
            {
                error = string.Format(Strings.U_Llb_BtlBinNotFound, path);
                return false;
            }

            try
            {
                if (sandboxEncounterTable == null || !string.Equals(sandboxEncounterTablePath, path, StringComparison.OrdinalIgnoreCase))
                {
                    sandboxEncounterTable = EncounterTable_File.Read(File.ReadAllBytes(path));
                    sandboxEncounterTablePath = path;
                }

                encounterTable = sandboxEncounterTable;
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                sandboxEncounterTable = null;
                error = $"falha lendo btl.bin: {ex.Message}";
                return false;
            }
        }

        static int CountLiveFormationSlots(byte[] battleBytes)
        {
            Battle_File battle = Battle_File.Read(string.Empty, battleBytes);
            return battle.Formation?.Slots.Count(slot => !slot.IsEmpty) ?? 0;
        }

        static int CountMonsterLiveAnchors(byte[] battleBytes) =>
            BattleArenaPositionWriter.LocateAnchorArray(battleBytes, 0, BattleArena_AnchorRole.MonsterLive)?.Count ?? 0;

        static List<(float X, float Y, float Z)> ReadMonsterLiveCoords(string battleId, byte[] battleBytes)
        {
            BattleArenaAnchors_File anchors = BattleArenaAnchors_File.ReadFromBattleBin(battleId, battleBytes);
            BattleArena_AnchorGroup? group = anchors.Areas.Count > 0 ? anchors.Areas[0][BattleArena_AnchorRole.MonsterLive] : null;
            return group?.Anchors.Select(anchor => (anchor.X, anchor.Y, anchor.Z)).ToList() ?? new List<(float X, float Y, float Z)>();
        }

        static ushort BuildFormationRawId(BattleSandboxMonsterChoice choice) =>
            (ushort)(0x1000 | (choice.Id & 0x0FFF));

        static IReadOnlyList<(float X, float Y, float Z)> BuildPlannedBattleCoords(int count, BattleSandboxLayoutPlan plan)
        {
            List<(float X, float Y, float Z)> coords = new();
            if (count <= 0)
            {
                return coords;
            }

            switch (plan)
            {
                case BattleSandboxLayoutPlan.WideRows:
                    AddRows(coords, count, spacingX: 3.4f, spacingZ: 3.2f);
                    break;
                case BattleSandboxLayoutPlan.BossAndAdds:
                    coords.Add((0f, 0f, 0.8f));
                    if (count > 1)
                    {
                        AddArc(coords, count - 1, radius: 4.6f, startDegrees: 210f, endDegrees: 330f);
                    }
                    break;
                case BattleSandboxLayoutPlan.NarrowLane:
                    AddRows(coords, count, spacingX: 1.8f, spacingZ: 2.8f);
                    break;
                case BattleSandboxLayoutPlan.Ring:
                    if (count == 1)
                    {
                        coords.Add((0f, 0f, 0f));
                    }
                    else
                    {
                        float radius = count <= 4 ? 3.2f : 4.7f;
                        for (int i = 0; i < count; i++)
                        {
                            double angle = (-90d + i * 360d / count) * Math.PI / 180d;
                            coords.Add(((float)(Math.Cos(angle) * radius), 0f, (float)(Math.Sin(angle) * radius)));
                        }
                    }
                    break;
                default:
                    AddRows(coords, count, spacingX: 2.7f, spacingZ: 2.6f);
                    break;
            }

            return coords.Take(count).ToList();
        }

        static void AddRows(List<(float X, float Y, float Z)> coords, int count, float spacingX, float spacingZ)
        {
            int firstRow = count <= 4 ? count : (count + 1) / 2;
            int secondRow = count - firstRow;
            AddCenteredRow(coords, firstRow, z: secondRow > 0 ? -spacingZ * 0.5f : 0f, spacingX);
            if (secondRow > 0)
            {
                AddCenteredRow(coords, secondRow, z: spacingZ * 0.5f, spacingX);
            }
        }

        static void AddCenteredRow(List<(float X, float Y, float Z)> coords, int count, float z, float spacingX)
        {
            float startX = -(count - 1) * spacingX * 0.5f;
            for (int i = 0; i < count; i++)
            {
                coords.Add((startX + i * spacingX, 0f, z));
            }
        }

        static void AddArc(List<(float X, float Y, float Z)> coords, int count, float radius, float startDegrees, float endDegrees)
        {
            if (count <= 0)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0.5f : i / (float)(count - 1);
                double angle = (startDegrees + (endDegrees - startDegrees) * t) * Math.PI / 180d;
                coords.Add(((float)(Math.Cos(angle) * radius), 0f, (float)(Math.Sin(angle) * radius)));
            }
        }

        static float Clamp(float value, float min, float max) => Math.Min(max, Math.Max(min, value));

        static bool IsMonsterPath(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            return name.Length == 4 && (name[0] == 'm' || name[0] == 'M') && name.Skip(1).All(char.IsDigit);
        }
    }

    internal enum BattleSandboxAiPreset
    {
        Original,
        Aggressive,
        Defensive,
        Enrage
    }

    internal enum BattleSandboxLayoutPlan
    {
        CompactRows,
        WideRows,
        BossAndAdds,
        NarrowLane,
        Ring
    }

    internal sealed partial class BattleSandboxSlot : ObservableObject
    {
        readonly Func<BattleSandboxMonsterChoice?, string> readHp;
        readonly Action changed;

        public BattleSandboxSlot(
            int index,
            ObservableCollection<BattleSandboxMonsterChoice> monsterChoices,
            IReadOnlyList<BattleSandboxAiPresetChoice> aiPresetChoices,
            Func<BattleSandboxMonsterChoice?, string> readHp,
            Action changed)
        {
            Index = index;
            MonsterChoices = monsterChoices;
            AiPresetChoices = aiPresetChoices;
            this.readHp = readHp;
            this.changed = changed;
        }

        public int Index { get; }
        public string ShortLabel => $"#{Index}";
        public string HeaderLabel => $"Slot {Index:D2}";
        public IReadOnlyList<BattleSandboxAiPresetChoice> AiPresetChoices { get; }

        [ObservableProperty] private ObservableCollection<BattleSandboxMonsterChoice> monsterChoices;
        [ObservableProperty] private BattleSandboxMonsterChoice? selectedMonster;
        [ObservableProperty] private string hpText = "";
        [ObservableProperty] private BattleSandboxAiPresetChoice? selectedAiPreset;

        public bool HasMonster => SelectedMonster is { IsEmpty: false };

        public void RefreshHpFromMonster()
        {
            HpText = readHp(SelectedMonster);
        }

        partial void OnSelectedMonsterChanged(BattleSandboxMonsterChoice? value)
        {
            HpText = readHp(value);
            OnPropertyChanged(nameof(HasMonster));
            changed();
        }

        partial void OnHpTextChanged(string value) => changed();
        partial void OnSelectedAiPresetChanged(BattleSandboxAiPresetChoice? value) => changed();
    }

    internal sealed record BattleSandboxAiPresetChoice(BattleSandboxAiPreset Preset, string Label)
    {
        public override string ToString() => Label;
    }

    internal sealed record BattleSandboxLayoutPlanChoice(BattleSandboxLayoutPlan Plan, string Label)
    {
        public override string ToString() => Label;
    }

    internal sealed record BattleSandboxPreviewDot(string SlotLabel, string MonsterLabel, string PositionLabel);

    internal sealed class BattleSandboxMonsterChoice
    {
        public static readonly BattleSandboxMonsterChoice Empty = new(-1, Strings.F2_none_6eef6648, "");

        public BattleSandboxMonsterChoice(int id, string name, string path)
        {
            Id = id;
            Name = name;
            Path = path;
        }

        public int Id { get; }
        public string Name { get; }
        public string Path { get; }
        public bool IsEmpty => Id < 0;
        public string IdLabel => IsEmpty ? Strings.F2_none_6eef6648 : $"m{Id:D3}";
        public string Label => IsEmpty ? Strings.F2_none_6eef6648 : $"{IdLabel} · {Name}";
        public override string ToString() => Label;
    }

    internal sealed class BattleSandboxRouteChoice
    {
        public static readonly BattleSandboxRouteChoice Manual = new();

        BattleSandboxRouteChoice()
        {
            IsManual = true;
            Label = Strings.U_Lbl_ManualRoute;
            Map = "-";
            BattleId = "-";
        }

        public BattleSandboxRouteChoice(int fieldIndex, int groupIndex, int formationId, string map, string battleId, int battlefield, int danger, int weight)
        {
            FieldIndex = fieldIndex;
            GroupIndex = groupIndex;
            FormationId = formationId;
            Map = map;
            BattleId = battleId;
            Battlefield = battlefield;
            Danger = danger;
            Weight = weight;
            Label = $"{map}_{formationId:00} · field {fieldIndex:D3} / group {groupIndex:D2} · {battleId}";
        }

        public bool IsManual { get; }
        public int FieldIndex { get; }
        public int GroupIndex { get; }
        public int FormationId { get; }
        public string Map { get; } = "";
        public string BattleId { get; } = "";
        public int Battlefield { get; }
        public int Danger { get; }
        public int Weight { get; }
        public string Label { get; }
        public override string ToString() => Label;
    }

    internal sealed record BattleSandboxRouteResolution(
        int FieldIndex,
        int GroupIndex,
        int FormationId,
        string Map,
        string BattleId,
        string BattlePath,
        int Battlefield,
        int Danger,
        int Weight)
    {
        public string RouteLabel => $"{BattleId} · field {FieldIndex:D3} / group {GroupIndex:D2} / formation {FormationId:D2}";
    }
}

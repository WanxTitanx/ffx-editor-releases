using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Ai.Sin;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MonsterAiEditor
{
    // SIN catalog surface: browsable/read-only "demonic recipe book" for future authoring.
    // A selected entry explains intent, primitives, risk and gates; it deliberately does not write AI bytes.
    // The ApplySinPreset* methods below are the one-click bakes (writer gated by confirmation dialog).
    internal partial class MonsterAiEditor_DataModel
    {
        public ObservableCollection<AiSinPresetEntry> DisplayedSinPresets { get; } = new();

        // ── Helpers de construção de instrução ATEL ──────────────────────────────────────
        static AiInstruction Op(byte opcode, ushort operand) => new()
        {
            Offset = -1, Opcode = opcode,
            HasOperand = AiScript_File.IsOperandBearing(opcode),
            Operand = operand,
            OperandKind = AiScript_File.OperandKindOf(opcode),
        };

        static AiInstruction Op0(byte opcode) => new()
        {
            Offset = -1, Opcode = opcode,
            HasOperand = false, Operand = 0,
            OperandKind = AiOperandKind.None,
        };

        /// <summary>Bake UNI-001 "Opening Veil" no monstro selecionado.
        /// Abertura defensiva: Haste+Protect+Shell em si, guardado por var privada (executa 1 vez).
        /// Usa forcePerformCommand (0x705A) para aplicar os 3 buffs no primeiro turno.</summary>
        public bool ApplySinPresetUni001()
        {
            if (selectedScript == null || !selectedScript.HasScript || selectedPath == null)
            { SinCatalogSummary = Strings.F2_select_a_monster_with_a_real_aifile_befo_b163f007; return false; }

            byte[] monsterBytes = File.ReadAllBytes(selectedPath);
            if (!AiWorkerMapping.TryResolveCombatOnTurn(monsterBytes, selectedScript, out AiEventHook hook, out string resolveErr))
            { SinCatalogSummary = $"UNI-001 abortado: {resolveErr}"; return false; }

            if (!TryEnsureFreePrivateVarSlot(out int varSlot, out string varReason, hook.WorkerIndex))
            { SinCatalogSummary = $"UNI-001 abortado: {varReason}"; return false; }

            // Build guard: var == 0 (one-shot — executa só na primeira passagem)
            var guard = new List<AiInstruction>
            {
                Op(0x9F, (ushort)varSlot),  // PUSHV var
                Op(0xAE, 0),                // PUSHII 0
                Op0(0x06),                  // EQ
            };

            // Build action: forcePerformCommand × 3 + var++
            var action = new List<AiInstruction>
            {
                // forcePerformCommand(Haste, Self)
                Op(0xAE, 0xFFF3), Op(0xAE, 0x3036), Op(0xD8, 0x705A),
                // forcePerformCommand(Protect, Self)
                Op(0xAE, 0xFFF3), Op(0xAE, 0x303B), Op(0xD8, 0x705A),
                // forcePerformCommand(Shell, Self)
                Op(0xAE, 0xFFF3), Op(0xAE, 0x303A), Op(0xD8, 0x705A),
                // var++ (latch — nunca mais executa)
                Op(0x9F, (ushort)varSlot),  // PUSHV var
                Op(0xAE, 1),                // PUSHII 1
                Op0(0x14),                  // ADD
                Op(0xA0, (ushort)varSlot),  // POPV var
            };

            // Append guarded action + validar + salvar (mesmo padrão do SaveNewAi)
            byte[] newAi;
            try { newAi = AiScript_File.AppendGuardedAction(selectedScript, hook.WorkerIndex, hook.EntrypointIndex, guard, action); }
            catch (Exception ex) { SinCatalogSummary = string.Format(Strings.U_Ai_SinUni001AppendAborted, ex.Message); return false; }

            if (!SaveNewAi(newAi, out string saveErr))
            { SinCatalogSummary = string.Format(Strings.U_Ai_SinUni001Aborted, saveErr); return false; }

            SinCatalogSummary = string.Format(Strings.U_Ai_SinUni001Applied, Path.GetFileName(selectedPath)) +
                                string.Format(Strings.U_Ai_SinOneShotVar, varSlot);
            return true;
        }

        /// <summary>Bake UNI-002 "Counter March" no monstro selecionado.
        /// Contra-ataca quem bateu por último (LastAttacker) com Delay Attack, sempre que o onHit dispara.
        /// Idioma provado no Skoll m014 (SIN): PUSHII 1 (guard sempre) + PUSHII LastAttacker (0xFFEF)
        /// + PUSHII 0x3006 (Delay Attack) + CALLPOPA performCommand (0x700B). Hook no entrypoint 3 (onHit).
        /// </summary>
        public bool ApplySinPresetUni002()
        {
            if (selectedScript == null || !selectedScript.HasScript || selectedPath == null)
            { SinCatalogSummary = Strings.F2_select_a_monster_with_a_real_aifile_befo_88cdaae1; return false; }

            // 1. Resolver CombatHandler onHit
            byte[] monsterBytes = File.ReadAllBytes(selectedPath);
            if (!AiWorkerMapping.TryResolveCombatOnHit(monsterBytes, selectedScript, out AiEventHook hook, out string resolveErr))
            { SinCatalogSummary = string.Format(Strings.U_Ai_SinUni002Aborted, resolveErr); return false; }

            // 2. Build guard: PUSHII 1 (sempre dispara no onHit — validado in-game)
            var guard = new List<AiInstruction>
            {
                Op(0xAE, 1),                  // PUSHII 1
            };

            // 3. Build action: performCommand(Delay Attack, LastAttacker)
            var action = new List<AiInstruction>
            {
                Op(0xAE, 0xFFEF),             // PUSHII LastAttacker (0xFFEF)
                Op(0xAE, 0x3006),             // PUSHII Delay Attack (0x3006)
                Op(0xD8, 0x700B),             // CALLPOPA performCommand
            };

            // 4. Append guarded action + validar + salvar
            byte[] newAi;
            try { newAi = AiScript_File.AppendGuardedAction(selectedScript, hook.WorkerIndex, hook.EntrypointIndex, guard, action); }
            catch (Exception ex) { SinCatalogSummary = string.Format(Strings.U_Ai_SinUni002AppendAborted, ex.Message); return false; }

            AiValidationReport check = AiValidator.ValidateRebuilt(newAi, selectedScript.OriginalAiFileBytes.Length);
            if (!check.IsValid)
            { SinCatalogSummary = string.Format(Strings.U_Ai_SinUni002Blocked, check.Errors.FirstOrDefault()?.Message); return false; }

            if (!SaveNewAi(newAi, out string saveErr))
            { SinCatalogSummary = string.Format(Strings.U_Ai_SinUni002Aborted, saveErr); return false; }

            SinCatalogSummary = string.Format(Strings.U_Ai_SinUni002Applied, Path.GetFileName(selectedPath)) +
                                string.Format(Strings.U_Ai_SinUni002Detail, hook.WorkerIndex, hook.EntrypointIndex);
            return true;
        }

        /// <summary>Bake UNI-004 "Ward Stack" no monstro selecionado.
        /// Abertura defensiva: skill 0x610B (monmagic2[267] = Shell+Regen+NulBlaze+NulShock) em Self, guardado por var privada (executa 1 vez).
        /// Hook no onHit conforme validado no m020 por Halyson.</summary>
        public bool ApplySinPresetUni004()
        {
            if (selectedScript == null || !selectedScript.HasScript || selectedPath == null)
            { SinCatalogSummary = Strings.F2_select_a_monster_with_a_real_aifile_befo_6d1b567a; return false; }

            byte[] monsterBytes = File.ReadAllBytes(selectedPath);
            if (!AiWorkerMapping.TryResolveCombatOnHit(monsterBytes, selectedScript, out AiEventHook hook, out string resolveErr))
            { SinCatalogSummary = string.Format(Strings.U_Ai_SinUni004OnHitAborted, resolveErr); return false; }

            if (!TryEnsureFreePrivateVarSlot(out int varSlot, out string varReason, hook.WorkerIndex))
            { SinCatalogSummary = string.Format(Strings.U_Ai_SinUni004Aborted, varReason); return false; }

            var guard = new List<AiInstruction>
            {
                Op(0x9F, (ushort)varSlot),  // PUSHV var
                Op(0xAE, 0),                // PUSHII 0
                Op0(0x06),                  // EQ
            };

            var action = new List<AiInstruction>
            {
                Op(0xAE, 0xFFF3), Op(0xAE, 0x610B), Op(0xD8, 0x705A),
                Op(0x9F, (ushort)varSlot),  // PUSHV var
                Op(0xAE, 1),                // PUSHII 1
                Op0(0x14),                  // ADD
                Op(0xA0, (ushort)varSlot),  // POPV var
            };

            byte[] newAi;
            try { newAi = AiScript_File.AppendGuardedAction(selectedScript, hook.WorkerIndex, hook.EntrypointIndex, guard, action); }
            catch (Exception ex) { SinCatalogSummary = string.Format(Strings.U_Ai_SinUni004AppendAborted, ex.Message); return false; }

            AiValidationReport check = AiValidator.ValidateRebuilt(newAi, selectedScript.OriginalAiFileBytes.Length);
            if (!check.IsValid)
            { SinCatalogSummary = string.Format(Strings.U_Ai_SinUni004Blocked, check.Errors.FirstOrDefault()?.Message); return false; }

            if (!SaveNewAi(newAi, out string saveErr))
            { SinCatalogSummary = string.Format(Strings.U_Ai_SinUni004Aborted, saveErr); return false; }

            SinCatalogSummary = string.Format(Strings.U_Ai_SinUni004Applied, Path.GetFileName(selectedPath)) +
                                string.Format(Strings.U_Ai_SinOneShotVarOnHit, varSlot);
            return true;
        }

        public bool ApplySinPresetUni003()
        {
            if (selectedScript == null || !selectedScript.HasScript || selectedPath == null)
            { SinCatalogSummary = Strings.F2_select_a_monster_with_a_real_aifile_befo_d6d61afc; return false; }

            byte[] monsterBytes = File.ReadAllBytes(selectedPath);
            if (!AiWorkerMapping.TryResolveCombatOnTurn(monsterBytes, selectedScript, out AiEventHook hook, out string resolveErr))
            { SinCatalogSummary = $"UNI-003 abortado: {resolveErr}"; return false; }

            if (!TryEnsureFreePrivateVarSlot(out int varSlot, out string varReason, hook.WorkerIndex))
            { SinCatalogSummary = $"UNI-003 abortado: {varReason}"; return false; }

            var guard = new List<AiInstruction>
            {
                Op(0x9F, (ushort)varSlot),
                Op(0xAE, 0),
                Op0(0x06),
                Op(0xAE, 0xFFF3), Op(0xAE, 0x0119), Op(0xB5, 0x700F),
                Op0(0x02),
            };

            var action = new List<AiInstruction>
            {
                Op(0xAE, 0xFFF3), Op(0xAE, 0x3036), Op(0xD8, 0x705A),
                Op(0xAE, 0xFFF2), Op(0xAE, 0x0004), Op(0xAE, 0), Op(0xAE, 0),
                Op(0xB5, 0x7010), Op(0xAE, 0x3038), Op(0xD8, 0x705A),
                Op(0x9F, (ushort)varSlot),
                Op(0xAE, 1),
                Op0(0x14),
                Op(0xA0, (ushort)varSlot),
            };

            byte[] newAi;
            try { newAi = AiScript_File.AppendGuardedAction(selectedScript, hook.WorkerIndex, hook.EntrypointIndex, guard, action); }
            catch (Exception ex) { SinCatalogSummary = $"UNI-003 abortado no append: {ex.Message}"; return false; }

            if (!SaveNewAi(newAi, out string saveErr))
            { SinCatalogSummary = $"UNI-003 abortado: {saveErr}"; return false; }

            SinCatalogSummary = string.Format(Strings.U_Ai_SinUni003Applied, Path.GetFileName(selectedPath)) +
                                $"quando HP < 50% (NearDeath), one-shot via var[{varSlot}]. ⚠️ Confirme in-game (RT2).";
            return true;
        }

        /// <summary>TryFindFreePrivateVariableSlot, mas se falhar por qualquer motivo
        /// (sem private storage OU slots esgotados), cresce o worker-alvo do hook em +8 bytes
        /// e tenta de novo. Caso <paramref name="targetWorkerIdx"/> seja inválido, cresce o
        /// worker com maior privLen atual (preserva slots já acessíveis no maior buffer); se
        /// nenhum worker declara private storage, cai no worker[0].</summary>
        bool TryEnsureFreePrivateVarSlot(out int slot, out string reason,
            int targetWorkerIdx = -1, bool showErrors = false)
        {
            try
            {
                if (selectedScript == null) { slot = -1; reason = Strings.F2_no_aifile_67fdc2bf; return false; }

                if (AiScript_File.TryFindFreePrivateVariableSlot(selectedScript, out slot, out reason))
                    return true;

                if (selectedScript.Workers.Count == 0)
                { reason = Strings.F2_no_worker_in_script_1c0c0fec; return false; }

                byte[] aiRaw = selectedScript.OriginalAiFileBytes;

                // Escolhe qual worker crescer. Prioridade:
                //   1. worker-alvo do hook (targetWorkerIdx), se informado e válido
                //   2. worker com maior privLen atual (preserva slots já acessíveis)
                //   3. fallback: worker[0]
                int growIdx;
                if (targetWorkerIdx >= 0 && targetWorkerIdx < selectedScript.Workers.Count)
                {
                    growIdx = targetWorkerIdx;
                }
                else
                {
                    growIdx = 0;
                    int bestPriv = -1;
                    for (int k = 0; k < selectedScript.Workers.Count; k++)
                    {
                        if (selectedScript.Workers[k].PrivateDataLength > bestPriv)
                        {
                            bestPriv = selectedScript.Workers[k].PrivateDataLength;
                            growIdx = k;
                        }
                    }
                }

                int desc = selectedScript.Workers[growIdx].DescriptorOffset;
                int curLen = aiRaw[desc + 0x10] | (aiRaw[desc + 0x11] << 8);
                int newLen = curLen + 8;
                aiRaw[desc + 0x10] = (byte)(newLen & 0xFF);
                aiRaw[desc + 0x11] = (byte)((newLen >> 8) & 0xFF);
                selectedScript = AiScript_File.Read(aiRaw);

                return AiScript_File.TryFindFreePrivateVariableSlot(selectedScript, out slot, out reason);
            }
            catch (Exception ex)
            {
                slot = -1;
                reason = $"Erro ao crescer worker: {ex.Message}";
                if (showErrors) PhasePrivateVarLabSummary = reason;
                return false;
            }
        }

        public IReadOnlyList<string> SinTierFilters { get; } = new[]
        {
            Strings.F2_all_6a720856,
            "Universal",
            "Boss",
            "Bake-ready",
            "Tier A",
            "Tier B",
            "Tier C / LAB",
        };

        public IReadOnlyList<string> SinThreatFilters { get; } = new[]
        {
            Strings.F2_any_t_8ec0efdf,
            "T1",
            "T2",
            "T3",
            "T4",
            "T5",
            "T6",
            "T7",
            "T8",
            "T9",
            "T10",
            "T1-T3",
            "T4-T6",
            "T7-T8",
            "T9-T10",
        };

        public IReadOnlyList<string> SinSortModes { get; } = new[]
        {
            "UNI-001 -> UNI-999",
            "Threat 1 -> 10",
            "Threat 10 -> 1",
            "Busca: relevancia",
        };

        [ObservableProperty] private string sinSearchText = "";
        [ObservableProperty] private string selectedSinTierFilter = Strings.F2_all_6a720856;
        [ObservableProperty] private string selectedSinThreatFilter = Strings.F2_any_t_8ec0efdf;
        [ObservableProperty] private string selectedSinSortMode = "UNI-001 -> UNI-999";
        [ObservableProperty] private AiSinPresetEntry? selectedSinPreset;
        [ObservableProperty] private string sinCatalogSummary = "";

        public bool HasSinPresetSelection => SelectedSinPreset != null;

        void SeedSinCatalog()
        {
            RefreshSinPresets();
            SelectedSinPreset ??= DisplayedSinPresets.FirstOrDefault();
        }

        partial void OnSinSearchTextChanged(string value) => RefreshSinPresets();
        partial void OnSelectedSinTierFilterChanged(string value) => RefreshSinPresets();
        partial void OnSelectedSinThreatFilterChanged(string value) => RefreshSinPresets();
        partial void OnSelectedSinSortModeChanged(string value) => RefreshSinPresets();

        partial void OnSelectedSinPresetChanged(AiSinPresetEntry? value)
        {
            OnPropertyChanged(nameof(HasSinPresetSelection));
            OnPropertyChanged(nameof(CanBakeSelectedUni));
        }

        void RefreshSinPresets()
        {
            AiSinPresetEntry? previous = SelectedSinPreset;
            DisplayedSinPresets.Clear();
            IEnumerable<AiSinPresetEntry> editorDrafts = BikanelSinPrototypeCatalog.All
                .Concat(CalmLandsSinPrototypeCatalog.All)
                .Concat(StolenFaythSinPrototypeCatalog.All)
                .Concat(GagazetSinPrototypeCatalog.All)
                .Concat(ZanarkandSinPrototypeCatalog.All)
                .Concat(EvraeSinPrototypeCatalog.All);

            try
            {
                foreach (AiSinPresetEntry entry in AiSinPresetCatalog.SearchWithAdditional(
                             editorDrafts,
                             SinSearchText, SelectedSinTierFilter, SelectedSinThreatFilter, SelectedSinSortMode))
                    DisplayedSinPresets.Add(entry);
            }
            catch (System.Exception ex)
            {
                SinCatalogSummary = string.Format(Strings.U_Ai_SinCatalogUnavailable, ex.Message);
                SelectedSinPreset = null;
                return;
            }

            if (previous != null && DisplayedSinPresets.Contains(previous))
                SelectedSinPreset = previous;
            else
                SelectedSinPreset = DisplayedSinPresets.FirstOrDefault();

            string suffix = string.IsNullOrWhiteSpace(SinSearchText)
                ? ""
                : $" para \"{SinSearchText.Trim()}\"";

            int uni = AiSinPresetCatalog.Universal.Count + editorDrafts.Count(entry => entry.IsUniversal);
            int boss = AiSinPresetCatalog.BossPresets.Count + editorDrafts.Count(entry => entry.Scope == AiSinPresetScope.Boss);

            SinCatalogSummary = DisplayedSinPresets.Count == 0
                ? string.Format(Strings.U_Ai_SinNoResults, suffix)
                : string.Format(Strings.U_Ai_SinVisible, DisplayedSinPresets.Count, suffix, uni) +
                  (boss > 0 ? $" + {boss} BOSS" : "") +
                  $" · {SelectedSinThreatFilter} · {SelectedSinSortMode}.";
        }
    }
}

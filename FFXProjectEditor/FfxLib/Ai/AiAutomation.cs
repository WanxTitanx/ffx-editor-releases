using System;
using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Ai
{
    // 1-CLICK AUTOMATION CORE — dependency-free logic behind the Monster AI Editor's layperson buttons
    // ("Adicionar habilidade", "Tirar ação"). Lives in FfxLib (next to AiCommandId / AiSnippetLibrary) so it is
    // pure, unit/gate-testable and shared by BOTH the editor VM and the AiScriptLab --ai3 corpus gate.
    //
    // It assembles NOTHING new at the byte level: every output is a composition of already-proven primitives —
    //   * AddAbility   -> AiScript_File.AppendGuardedAction when creating a new hook-level behaviour.
    //   * InsertCommandAfterAction -> local insertion after a selected action; optional chance wraps only the new action.
    //   * RemoveAction -> drop a stack-neutral [target][command][call] triplet, then AiScript_File.Rebuild (RT0).
    // The editor resolves the actual CombatHandler.onTurn hook from the monster WorkerFile before calling these
    // primitives. The generated structure is offline-proven; the final gameplay effect remains RT2-pending.
    public static partial class AiAutomation
    {
        // proven idiom opcodes / calls (mirror AiSnippetLibrary — single source of the corpus-faithful sequence)
        const byte PUSHII = 0xAE, PUSHV = 0x9F, CALLPOPA = 0xD8, CALL = 0xB5, MOD = 0x18, EQ = 0x06, NE = 0x07, LT = 0x0B;
        // Native stack [A(base), B(top)]: LT(0x0B) = A < B, LE(0x0F) = A <= B.
        // Keep swapped-LT/LE emission for >/>=. Direct GT/GTE also exist (0x0A/0x0E).
        // Evidence: FFX_EVENTVM_OPS_2026-09-17 section 7, interpreter @0x864180.
        const byte LE = 0x0F, ADD = 0x14, MUL = 0x16, DIV = 0x17, LOR = 0x01, LAND = 0x02;
        const byte POPI0 = 0x59, PUSHI0 = 0x67;
        public const ushort ForcePerformCommand = 0x705A, PerformCommand = 0x700B;
        const ushort GetRandomValue = 0x00A9, SelfTarget = 0xFFF3, ReadChrProperty = 0x700F, WriteChrProperty = 0x7018, SetStatField = 0x70AB;
        const ushort UsedCommand = 0x7019, ReadMoveProperty = 0x701A, MoveFieldDamageType = 0x0001;
        const ushort RemoveCommand = 0x7038, RunBtlSceneA = 0x703C, RunBtlSceneB = 0x7097;
        const ushort ChosenCommand = 0x7014, CountChrOverlap = 0x701E, DereferenceCharacter = 0x7021, CurrentEncounter = 0x7024, CamReq = 0x703F, DereferenceEnemy = 0x70A1;
        const ushort TalkCommand = 0x3105, Special1Command = 0x6001;
        const ushort StatVisibleCamField = 0x00D8, TonberryPositionToMoveToField = 0x0056;
        const ushort RoundLandingCommand = 0x4019, RoundCrawlCommand = 0x401A, RoundSonicBoomCommand = 0x4016;
        const ushort RoundAeonPunishCommand = 0x4097, RoundFinisherCommandA = 0x40AB, RoundFinisherCommandB = 0x40DF;
        const ushort TonberryPressureCommand = 0x4061, TonberryCounterCommand = 0x4060, TonberryAdvanceCommand = 0x4081, TonberryFallbackCommandA = 0x4062, TonberryFallbackCommandB = 0x410F;
        const ushort StealCommand = 0x3016, CopycatCommand = 0x3028, EscapeCommand = 0x3003, FleeCommand = 0x3018;
        const ushort OpenCommand = 0x4037, Mimic1Command = 0x40A2, SetCommandDisabled = 0x703B;
        const ushort TargetableField = 0x0052, VisibleOnCtbField = 0x0053, MustBeKilledForBattleEndField = 0x008E, CtbIconNumberField = 0x0103;
        public const ushort DamageTypePhysical = 0x0001;
        public const ushort DamageTypeMagical = 0x0002;
        const ushort FindMatchingChr = 0x7010, FrontlineChars = 0xFFF2, MatchingGroup = 0xFFF0;
        // Native readChrProperty: 0 -> actor+5D0 (HP), 2 -> actor+594 (maxHP).
        // FFX.exe SHA-256 78ce3439...5ced; cases 7B2E31/7B2E53, accessors
        // 79ADE0/79AE00. 119/11B are predicates and must not enter HP arithmetic.
        public const ushort CurrentHpField = 0x0000, MaximumHpField = 0x0002;
        const ushort ChrFieldHp = CurrentHpField, ChrFieldMaxHp = MaximumHpField, ChrFieldLastDamageTakenHp = 0x00A6, ChrFieldTurnsTaken = 0x0114;
        public const ushort ChrFieldIsAlive = 0x0004;
        public const ushort LastAttackerTarget = 0xFFEF;
        const ushort SelectorAny = 0x0000, SelectorLowest = 0x0002;
        public const ushort OverdriveModeField = 0x0012;
        public const ushort OverdriveCurrentField = 0x0013;
        public const ushort OverdriveMaxField = 0x0014;
        public const ushort ShowOverdriveBarField = 0x0089;
        const int WdEntrypoints = 0x08, WdJumps = 0x0A, WdEntryTable = 0x20, WdJumpTable = 0x24;
        static readonly HashSet<ushort> OverdriveActorFields = new()
        {
            OverdriveModeField,
            OverdriveCurrentField,
            OverdriveMaxField,
            ShowOverdriveBarField,
        };

        /// <summary>Self/owner status fields the editor can grant via writeChrProperty(7018). These ids come from
        /// AiChrPropertyNames; the write shape is structure-proven, while in-game behaviour remains RT2.</summary>
        public static IReadOnlyList<AiBuffPreset> SelfBuffPresets { get; } = new[]
        {
            new AiBuffPreset("Haste", 0x38),
            new AiBuffPreset("Protect", 0x31),
            new AiBuffPreset("Shell", 0x30),
            new AiBuffPreset("Reflect", 0x32),
            new AiBuffPreset("Regen", 0x37),
            new AiBuffPreset("NulBlaze", 0x34),
            new AiBuffPreset("NulTide", 0x33),
            new AiBuffPreset("NulShock", 0x35),
            new AiBuffPreset("NulFrost", 0x36),
        };

        // ── ➖ detect / remove actions (commands + buffs/status + stat tunes) ──────────────────────────────────
        /// <summary>Scan the script for EVERY editable action statement — not just commands (#6). Three kinds are
        /// recognised by their CALLPOPA target (the proven corpus idioms in AiSnippetLibrary):
        ///   • Command  — CALLPOPA 700B/705A, preceded by a PUSHII command id ([target][command][call]);
        ///   • Buff     — CALLPOPA 7018 writeChrProperty ([chr][field][value][call]) = a status buff/debuff;
        ///   • Stat     — CALLPOPA 70AB setStatField ([field][value][call]) = a stat/motion tune.
        /// Removable iff EVERY arg push feeding the call is a single-item push — then dropping the whole
        /// [pushes…][call] run is stack-neutral. For a Command, the user-renamed label (AiCommandLabels, #7) wins
        /// over the dictionary name. Ascending offset order, kinds interleaved.</summary>
        public static IReadOnlyList<AiDetectedAction> DetectActions(AiScriptFile script)
        {
            var found = new List<AiDetectedAction>();
            IReadOnlyList<AiInstruction> ins = script.Instructions;
            IReadOnlyDictionary<int, IReadOnlyList<int>> ownerMap = AiScript_File.InstructionOwners(script);
            HashSet<int> controlFlowTargets = ControlFlowTargetOffsets(script);
            bool rebuildSafe = CanRebuildSafely(script);
            for (int i = 1; i < ins.Count; i++)
            {
                AiInstruction call = ins[i];
                if (call.Opcode != CALLPOPA) continue;

                if (call.Operand == PerformCommand || call.Operand == ForcePerformCommand)
                {
                    AiInstruction cmdPush = ins[i - 1];
                    AiInstruction? tgtPush = i >= 2 ? ins[i - 2] : null;
                    bool commandIsLiteral = cmdPush.Opcode == PUSHII && AiCommandId.IsCommandOperand(cmdPush.Operand);
                    bool commandIsIndirect = false;
                    ushort commandOperand = 0;
                    IReadOnlyList<ushort>? commandCandidates = null;
                    string name;
                    if (commandIsLiteral)
                    {
                        commandOperand = cmdPush.Operand;
                        AiCommandDecode dec = AiCommandId.Decode(commandOperand);
                        string baseName = dec.IsKnown ? dec.Name : (AiStackModel.CallDisplayName(commandOperand) != null ? $"{AiStackModel.CallDisplayName(commandOperand)} (0x{commandOperand:X4})" : $"comando 0x{commandOperand:X4}");
                        name = AiCommandLabels.Get(commandOperand) ?? baseName;
                    }
                    else if (cmdPush.Opcode == PUSHV && TryResolveIndirectCommandCandidates(ins, i - 1, cmdPush.Operand, out List<ushort> candidates))
                    {
                        commandIsIndirect = true;
                        commandCandidates = candidates;
                        if (candidates.Count == 1) commandOperand = candidates[0];
                        name = DescribeIndirectCommandCandidates(candidates);
                    }
                    else continue;

                    bool removable = false;
                    var offsets = tgtPush != null && commandIsIndirect
                        ? new List<int> { tgtPush.Offset, cmdPush.Offset, call.Offset }
                        : new List<int> { cmdPush.Offset, call.Offset };
                    int targetPushOffset = tgtPush?.Offset ?? -1;
                    ushort targetOperand = tgtPush?.Operand ?? 0;
                    byte targetOpcode = tgtPush?.Opcode ?? (byte)0;
                    if (commandIsLiteral && rebuildSafe && tgtPush != null && SinglePush.Contains(tgtPush.Opcode))
                    {
                        removable = true;
                        offsets.Insert(0, tgtPush.Offset);
                    }
                    else if (commandIsLiteral && rebuildSafe && TryGetComputedTargetRecipeBeforeCommand(ins, i - 1, out List<int> targetOffsets, out int computedTargetOffset))
                    {
                        List<int> computedOffsets = targetOffsets.Concat(offsets).ToList();
                        if (!computedOffsets.Any(controlFlowTargets.Contains))
                        {
                            removable = true;
                            offsets = computedOffsets;
                            targetPushOffset = computedTargetOffset;
                            targetOperand = FindMatchingChr;
                            targetOpcode = CALL;
                        }
                    }
                    // Target slot (#8): the push before the command id. Swappable only when it is a PUSHII literal.
                    bool tgtLiteral = tgtPush != null && tgtPush.Opcode == PUSHII;
                    found.Add(new AiDetectedAction(
                        Kind: AiActionKind.Command, AbilityName: name,
                        CommandOperand: commandOperand, PerformOperand: call.Operand,
                        ForcePerform: call.Operand == ForcePerformCommand,
                        CallOffset: call.Offset, CmdPushOffset: cmdPush.Offset,
                        WorkerIndex: OwningWorkerIndex(script, call.Offset, ownerMap),
                        Removable: removable, RemoveOffsets: offsets, FieldId: 0, FieldValue: 0,
                        TargetPushOffset: targetPushOffset, TargetOperand: targetOperand,
                        TargetIsLiteral: tgtLiteral, TargetOpcode: targetOpcode,
                        CmdPushOpcode: cmdPush.Opcode, CommandIsLiteral: commandIsLiteral,
                        CommandCandidates: commandCandidates));
                }
                else if (call.Operand == WriteChrProperty && i >= 3)
                {
                    // [chr][field][value] writeChrProperty — a status buff/debuff (arity 3).
                    AiInstruction chr = ins[i - 3], field = ins[i - 2], val = ins[i - 1];
                    bool removable = rebuildSafe && SinglePush.Contains(chr.Opcode) && SinglePush.Contains(field.Opcode) && SinglePush.Contains(val.Opcode);
                    bool fieldLit = field.Opcode == PUSHII, valLit = val.Opcode == PUSHII;
                    ushort fieldId = fieldLit ? field.Operand : (ushort)0, value = valLit ? val.Operand : (ushort)0;
                    var offsets = removable
                        ? new List<int> { chr.Offset, field.Offset, val.Offset, call.Offset }
                        : new List<int> { field.Offset, val.Offset, call.Offset };
                    found.Add(new AiDetectedAction(
                        Kind: AiActionKind.Buff, AbilityName: BuffName(chr, fieldLit, fieldId, valLit, value),
                        CommandOperand: 0, PerformOperand: call.Operand, ForcePerform: false,
                        CallOffset: call.Offset, CmdPushOffset: field.Offset,
                        WorkerIndex: OwningWorkerIndex(script, call.Offset, ownerMap),
                        Removable: removable, RemoveOffsets: offsets, FieldId: fieldId, FieldValue: value,
                        TargetPushOffset: chr.Offset, TargetOperand: chr.Operand,
                        TargetIsLiteral: chr.Opcode == PUSHII, TargetOpcode: chr.Opcode));
                }
                else if (call.Operand == SetStatField && i >= 2)
                {
                    // [field][value] setStatField — a stat/motion tune (arity 2).
                    AiInstruction field = ins[i - 2], val = ins[i - 1];
                    bool removable = rebuildSafe && SinglePush.Contains(field.Opcode) && SinglePush.Contains(val.Opcode);
                    bool fieldLit = field.Opcode == PUSHII, valLit = val.Opcode == PUSHII;
                    ushort fieldId = fieldLit ? field.Operand : (ushort)0, value = valLit ? val.Operand : (ushort)0;
                    var offsets = removable
                        ? new List<int> { field.Offset, val.Offset, call.Offset }
                        : new List<int> { val.Offset, call.Offset };
                    // setStatField shares the chr-property field space (FFXDataParser "btlActorProperty"), so the same
                    // name map applies — "stat 0xDA" becomes "stat_round", etc.
                    string sname = fieldLit ? (StatusFieldName(fieldId) ?? $"campo 0x{fieldId:X2}") : "calculated field";
                    string name = fieldLit
                        ? $"{sname}{(valLit ? $" = {value}" : "")}"
                        : "stat (calculated field)";
                    found.Add(new AiDetectedAction(
                        Kind: AiActionKind.Stat, AbilityName: name,
                        CommandOperand: 0, PerformOperand: call.Operand, ForcePerform: false,
                        CallOffset: call.Offset, CmdPushOffset: field.Offset,
                        WorkerIndex: OwningWorkerIndex(script, call.Offset, ownerMap),
                        Removable: removable, RemoveOffsets: offsets, FieldId: fieldId, FieldValue: value));
                }
            }
            return found;
        }

        static HashSet<int> ControlFlowTargetOffsets(AiScriptFile script)
        {
            var targets = new HashSet<int>();
            foreach (AiWorker worker in script.Workers)
            {
                foreach (int entry in worker.Entrypoints)
                    targets.Add(script.ScriptStart + entry);
                foreach (int jump in worker.JumpTargets)
                    targets.Add(script.ScriptStart + jump);
            }
            return targets;
        }

        public static bool CanRebuildSafely(AiScriptFile script) =>
            UnrebuildableControlTargets(script).Count == 0;

        public static IReadOnlyList<int> UnrebuildableControlTargets(AiScriptFile script)
        {
            var instructionStarts = script.Instructions
                .Select(i => i.Offset - script.ScriptStart)
                .ToHashSet();
            var bad = new SortedSet<int>();
            foreach (AiWorker worker in script.Workers)
            {
                foreach (int target in worker.Entrypoints)
                    if (!IsRebuildTargetSafe(target, script.CodeLength, instructionStarts)) bad.Add(target);
                foreach (int target in worker.JumpTargets)
                    if (!IsRebuildTargetSafe(target, script.CodeLength, instructionStarts)) bad.Add(target);
            }
            return bad.ToList();
        }

        static bool IsRebuildTargetSafe(int codeRelativeTarget, int codeLength, HashSet<int> instructionStarts)
        {
            if (codeRelativeTarget < 0 || codeRelativeTarget > codeLength) return false;
            return codeRelativeTarget == codeLength || instructionStarts.Contains(codeRelativeTarget);
        }

        static bool TryGetComputedTargetRecipeBeforeCommand(
            IReadOnlyList<AiInstruction> ins,
            int cmdIndex,
            out List<int> removeOffsets,
            out int computedTargetOffset)
        {
            removeOffsets = new List<int>();
            computedTargetOffset = -1;

            static bool Is(AiInstruction i, byte opcode, ushort operand) =>
                i.Opcode == opcode && i.Operand == operand;

            // Exact editor-generated "one alive frontline target" recipe:
            // FrontlineChars, IsAlive, 0, Any, CALL findMatchingChr -> target on stack.
            if (cmdIndex >= 5)
            {
                int start = cmdIndex - 5;
                if (Is(ins[start + 0], PUSHII, FrontlineChars)
                    && Is(ins[start + 1], PUSHII, ChrFieldIsAlive)
                    && Is(ins[start + 2], PUSHII, 0)
                    && Is(ins[start + 3], PUSHII, SelectorAny)
                    && Is(ins[start + 4], CALL, FindMatchingChr))
                {
                    removeOffsets = ins.Skip(start).Take(5).Select(x => x.Offset).ToList();
                    computedTargetOffset = ins[start + 4].Offset;
                    return true;
                }
            }

            // Exact editor-generated "alive frontline with lowest HP" recipe:
            // seed MatchingGroup with alive actors, then select lowest HP from that group.
            if (cmdIndex >= 10)
            {
                int start = cmdIndex - 10;
                if (Is(ins[start + 0], PUSHII, FrontlineChars)
                    && Is(ins[start + 1], PUSHII, ChrFieldIsAlive)
                    && Is(ins[start + 2], PUSHII, 0)
                    && Is(ins[start + 3], PUSHII, SelectorAny)
                    && Is(ins[start + 4], CALLPOPA, FindMatchingChr)
                    && Is(ins[start + 5], PUSHII, MatchingGroup)
                    && Is(ins[start + 6], PUSHII, ChrFieldHp)
                    && Is(ins[start + 7], PUSHII, 0)
                    && Is(ins[start + 8], PUSHII, SelectorLowest)
                    && Is(ins[start + 9], CALL, FindMatchingChr))
                {
                    removeOffsets = ins.Skip(start).Take(10).Select(x => x.Offset).ToList();
                    computedTargetOffset = ins[start + 9].Offset;
                    return true;
                }
            }

            return false;
        }

        static bool TryResolveIndirectCommandCandidates(
            IReadOnlyList<AiInstruction> ins,
            int cmdIndex,
            ushort varIndex,
            out List<ushort> candidates)
        {
            var found = new HashSet<ushort>();
            CollectIndirectCommandCandidates(ins, cmdIndex, varIndex, found, new HashSet<ushort>(), 0);
            candidates = found.OrderBy(x => x).ToList();
            return candidates.Count > 0;
        }

        static void CollectIndirectCommandCandidates(
            IReadOnlyList<AiInstruction> ins,
            int beforeIndex,
            ushort varIndex,
            HashSet<ushort> candidates,
            HashSet<ushort> seenVars,
            int depth)
        {
            if (depth > 4 || !seenVars.Add(varIndex)) return;
            for (int idx = beforeIndex - 1; idx >= 1; idx--)
            {
                AiInstruction store = ins[idx];
                if (store.Opcode != 0xA0 || store.Operand != varIndex) continue; // POPV same var

                AiInstruction source = ins[idx - 1];
                if (source.Opcode == PUSHII && AiCommandId.IsCommandOperand(source.Operand))
                {
                    candidates.Add(source.Operand);
                }
                else if (source.Opcode == PUSHV)
                {
                    CollectIndirectCommandCandidates(ins, idx - 1, source.Operand, candidates, new HashSet<ushort>(seenVars), depth + 1);
                }
            }
        }

        static string DescribeIndirectCommandCandidates(IReadOnlyList<ushort> candidates)
        {
            if (candidates.Count == 1)
                return $"{CommandDisplayName(candidates[0])} (via var)";

            IEnumerable<string> names = candidates.Take(3).Select(CommandDisplayName);
            string suffix = candidates.Count > 3 ? $" +{candidates.Count - 3}" : string.Empty;
            return $"comando via var ({candidates.Count} candidatos: {string.Join(", ", names)}{suffix})";
        }

        static string CommandDisplayName(ushort operand)
        {
            AiCommandDecode dec = AiCommandId.Decode(operand);
            string baseName = dec.IsKnown ? dec.Name : (AiStackModel.CallDisplayName(operand) != null ? $"{AiStackModel.CallDisplayName(operand)} (0x{operand:X4})" : $"comando 0x{operand:X4}");
            return AiCommandLabels.Get(operand) ?? baseName;
        }

        /// <summary>Back-compat command-only view of <see cref="DetectActions"/> (the surface the --ai3 gate and the
        /// command-specific operations — change/toggle — consume). Identical to the pre-#6 behaviour.</summary>

public static byte[] AddSelfBuff(AiScriptFile script, ushort fieldId, bool random, int k, out int workerIndex, out int entrypointIndex)
        {
            AiWorker worker = PickCombatWorker(script)
                ?? throw new InvalidOperationException("This monster does not have a worker with an entrypoint to hook the buff.");
            workerIndex = worker.Index;
            entrypointIndex = PickMainEntrypoint(script, worker);
            return AddSelfBuff(script, fieldId, random, k, workerIndex, entrypointIndex);
        }

        /// <summary>Attribution for labels. Uses CFG reachability first, with the old physical range rule only as fallback.</summary>
        public static int OwningWorkerIndex(AiScriptFile script, int aiFileOffset)
        {
            IReadOnlyDictionary<int, IReadOnlyList<int>> owners = AiScript_File.InstructionOwners(script);
            return OwningWorkerIndex(script, aiFileOffset, owners);
        }

        static int OwningWorkerIndex(
            AiScriptFile script,
            int aiFileOffset,
            IReadOnlyDictionary<int, IReadOnlyList<int>> owners)
        {
            if (owners.TryGetValue(aiFileOffset, out IReadOnlyList<int>? exact) && exact.Count == 1)
                return exact[0];

            int codeRel = aiFileOffset - script.ScriptStart;
            int owner = -1, bestStart = -1;
            foreach (AiWorker w in script.Workers)
            {
                if (w.Entrypoints.Count == 0) continue;
                int start = w.Entrypoints.Min();
                if (start <= codeRel && start > bestStart) { bestStart = start; owner = w.Index; }
            }
            return owner;
        }

        static bool TryBuildStrictSeymourDispatchUnits(AiScriptFile script, out List<AiIndirectDispatchUnit> units)
        {
            units = new List<AiIndirectDispatchUnit>();
            if (!script.HasScript || script.Instructions.Count == 0)
                return false;

            if (!TryFindVariableIndexByName(script, "priv0020", out ushort phaseVar)
                || !TryFindVariableIndexByName(script, "priv0024", out ushort normalCmdVar)
                || !TryFindVariableIndexByName(script, "priv0028", out ushort aeonCmdVar)
                || !TryFindVariableIndexByName(script, "priv002C", out ushort multiCmdVar)
                || !TryFindVariableIndexByName(script, "priv0030", out ushort pairCmdVar)
                || !TryFindVariableIndexByName(script, "priv0014", out ushort singleTargetVar)
                || !TryFindVariableIndexByName(script, "priv0018", out ushort multiTargetOneVar)
                || !TryFindVariableIndexByName(script, "priv001C", out ushort multiTargetTwoVar)
                || !TryFindVariableIndexByName(script, "battleVar0014", out ushort multiGateVar))
            {
                return false;
            }

            IReadOnlyList<AiInstruction> instructions = script.Instructions;
            var rows = new List<StrictSeymourDispatchRow>();
            for (int i = 0; i <= instructions.Count - 10; i++)
            {
                if (!TryReadStrictSeymourDispatchRow(
                        instructions,
                        i,
                        normalCmdVar,
                        aeonCmdVar,
                        multiCmdVar,
                        pairCmdVar,
                        phaseVar,
                        out StrictSeymourDispatchRow row))
                {
                    continue;
                }

                rows.Add(row with { PhaseIndex = rows.Count });
                i += 9;
            }

            if (rows.Count < 4)
                return false;

            int normalCall = FindIndirectPerformCallOffset(script, singleTargetVar, normalCmdVar);
            int aeonCall = FindIndirectPerformCallOffset(script, singleTargetVar, aeonCmdVar);
            int multiOneCall = FindIndirectPerformCallOffset(script, multiTargetOneVar, multiCmdVar);
            int multiTwoCall = FindIndirectPerformCallOffset(script, multiTargetTwoVar, pairCmdVar);
            int multiGateOffset = FindLiteralCompareOffset(script, multiGateVar, 255);

            var nativeGuards = AiNativeConditionWriter.Detect(script)
                .Where(guard => guard.UsesSwitchRegister && guard.VariableIndex == phaseVar)
                .ToArray();

            foreach (StrictSeymourDispatchRow row in rows)
            {
                var payloadWrites = new List<AiIndirectDispatchWrite>
                {
                    new(
                        normalCmdVar,
                        VarName(script, normalCmdVar),
                        "slot cmd normal",
                        $"{CommandDisplayName(row.NormalCastCommand)} [0x{row.NormalCastCommand:X4}]",
                        row.NormalCommandOffset),
                    new(
                        aeonCmdVar,
                        VarName(script, aeonCmdVar),
                        "slot cmd contra aeon",
                        $"{CommandDisplayName(row.AeonCastCommand)} [0x{row.AeonCastCommand:X4}]",
                        row.AeonCommandOffset),
                    new(
                        multiCmdVar,
                        VarName(script, multiCmdVar),
                        "slot cmd multi #1",
                        $"{CommandDisplayName(row.MultiCastCommand)} [0x{row.MultiCastCommand:X4}]",
                        row.MultiCommandOffset),
                    new(
                        pairCmdVar,
                        VarName(script, pairCmdVar),
                        "slot cmd multi #2",
                        $"{CommandDisplayName(row.PairCastCommand)} [0x{row.PairCastCommand:X4}]",
                        row.PairCommandOffset),
                    new(
                        phaseVar,
                        VarName(script, phaseVar),
                        "next state",
                        row.NextState.ToString(),
                        row.NextStateOffset),
                };

                var editableSlots = new List<AiIndirectDispatchEditableSlot>
                {
                    new(
                        "command.normal",
                        "slot cmd normal",
                        row.NormalCommandOffset,
                        row.NormalCastCommand,
                        $"{CommandDisplayName(row.NormalCastCommand)} [0x{row.NormalCastCommand:X4}]"),
                    new(
                        "command.aeon",
                        "slot cmd contra aeon",
                        row.AeonCommandOffset,
                        row.AeonCastCommand,
                        $"{CommandDisplayName(row.AeonCastCommand)} [0x{row.AeonCastCommand:X4}]"),
                    new(
                        "command.multi1",
                        "slot cmd multi #1",
                        row.MultiCommandOffset,
                        row.MultiCastCommand,
                        $"{CommandDisplayName(row.MultiCastCommand)} [0x{row.MultiCastCommand:X4}]"),
                    new(
                        "command.multi2",
                        "slot cmd multi #2",
                        row.PairCommandOffset,
                        row.PairCastCommand,
                        $"{CommandDisplayName(row.PairCastCommand)} [0x{row.PairCastCommand:X4}]"),
                };

                List<AiIndirectDispatchEditableTargetSlot> editableTargetSlots = BuildEditableTargetSlots(
                    script,
                    (singleTargetVar, "target.single", "slot alvo do cast unitario"),
                    (multiTargetOneVar, "target.multi1", "slot alvo do multi #1"),
                    (multiTargetTwoVar, "target.multi2", "slot alvo do multi #2"));

                var consumers = new List<AiIndirectDispatchConsumer>();
                AddIndirectDispatchConsumer(consumers, script, "cast unitario normal", normalCmdVar, singleTargetVar, normalCall);
                AddIndirectDispatchConsumer(consumers, script, "cast unitario contra aeon", aeonCmdVar, singleTargetVar, aeonCall);
                AddIndirectDispatchConsumer(consumers, script, "multi-cast #1", multiCmdVar, multiTargetOneVar, multiOneCall);
                AddIndirectDispatchConsumer(consumers, script, "multi-cast #2", pairCmdVar, multiTargetTwoVar, multiTwoCall);

                var companionEffects = new List<string>
                {
                    "payload principal = writes de slot + next state; os performCommand finais continuam os mesmos",
                    "estrategia segura futura = mutar rows/slots (row-only), sem reescrever o CALLPOPA final",
                    $"{VarName(script, singleTargetVar)} / {VarName(script, multiTargetOneVar)} / {VarName(script, multiTargetTwoVar)} continuam decidindo os alvos nos consumers",
                };

                if (multiGateOffset >= 0)
                    companionEffects.Add($"{VarName(script, multiGateVar)} == 255 continua sendo o gate estrutural do multi-cast (0x{multiGateOffset:X4})");

                units.Add(new AiIndirectDispatchUnit(
                    $"dispatch-row-{row.PhaseIndex}",
                    row.PhaseIndex,
                    "onTurn real",
                    nativeGuards.Any(guard => guard.BranchTargetOffset == row.StartOffset)
                        ? string.Join(" | ", nativeGuards.Where(guard => guard.BranchTargetOffset == row.StartOffset)
                            .Select(guard => $"{guard.VariableName} {AiVarConditionBuilder.OperatorLabel(guard.Operator)} {guard.Value}"))
                        : Strings.AiAdvancedUnknownNativeCondition,
                    $"{VarName(script, phaseVar)} <- {row.NextState}",
                    AiIndirectDispatchCapabilityTier.AuthoringCandidate,
                    "candidato row-only",
                    payloadWrites,
                    consumers,
                    companionEffects,
                    $"0x{row.StartOffset:X4}..0x{row.EndOffset:X4}",
                    "Leitura forte o bastante para preview autoral do row pack; writer row-only ja cobre command slots, next-state e target literal quando o slot nasce de PUSHII.",
                    editableSlots,
                    editableTargetSlots,
                    row.NextStateOffset,
                    row.NextState));
            }

            return units.Count > 0;
        }
    }
}

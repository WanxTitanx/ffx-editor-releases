using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai
{
    // F7.1: deteccao de acoes/branches extraida do god file AiAutomation.cs.
    public static partial class AiAutomation
    {
public static IReadOnlyList<AiDetectedAction> DetectCommandActions(AiScriptFile script)
            => DetectActions(script).Where(a => a.Kind == AiActionKind.Command).ToList();

        public static IReadOnlyList<AiDetectedBranchAction> DetectBranchSensitiveActions(byte[] monsterBin, AiScriptFile script)
        {
            ArgumentNullException.ThrowIfNull(monsterBin);
            ArgumentNullException.ThrowIfNull(script);

            const int MaxVisitedStatesPerHook = 4000;
            const int MaxPendingStatesPerHook = 2048;
            const int MaxActionsPerHook = 256;

            var found = new List<AiDetectedBranchAction>();
            if (!script.HasScript || script.Workers.Count == 0 || script.Instructions.Count == 0)
                return found;

            IReadOnlyList<AiInstruction> ins = script.Instructions;
            var seenActions = new HashSet<string>(StringComparer.Ordinal);

            foreach ((AiEventHook hook, string hookKind) in ResolveReadableHooks(monsterBin, script))
            {
                if (hook.WorkerIndex < 0 || hook.WorkerIndex >= script.Workers.Count) continue;
                AiWorker worker = script.Workers[hook.WorkerIndex];
                if (hook.EntrypointIndex < 0 || hook.EntrypointIndex >= worker.Entrypoints.Count) continue;

                var nodes = new Dictionary<int, (AiInstruction Instruction, int Next, int Index)>();
                for (int i = 0; i < ins.Count; i++)
                {
                    AiInstruction instruction = ins[i];
                    int relative = instruction.Offset - script.ScriptStart;
                    int next = i + 1 < ins.Count
                        ? ins[i + 1].Offset - script.ScriptStart
                        : script.CodeLength;
                    nodes[relative] = (instruction, next, i);
                }

                var pending = new Stack<BranchTraversalState>();
                var visited = new HashSet<string>(StringComparer.Ordinal);
                int actionsForHook = 0;
                pending.Push(new BranchTraversalState(
                    worker.Entrypoints[hook.EntrypointIndex],
                    new Dictionary<ushort, BranchValue>(),
                    string.Empty,
                    $"w{hook.WorkerIndex}_e{hook.EntrypointIndex}",
                    null,
                    false));

                while (pending.Count > 0)
                {
                    if (visited.Count >= MaxVisitedStatesPerHook || actionsForHook >= MaxActionsPerHook)
                        break;

                    BranchTraversalState state = pending.Pop();
                    int safety = 0;
                    while (safety++ < 4000 && nodes.TryGetValue(state.Relative, out var node))
                    {
                        if (visited.Count >= MaxVisitedStatesPerHook || actionsForHook >= MaxActionsPerHook)
                            break;

                        string stateKey = BuildBranchStateKey(state);
                        if (!visited.Add(stateKey))
                            break;

                        AiInstruction instruction = node.Instruction;
                        int absolute = script.ScriptStart + state.Relative;

                        if (instruction.Opcode == 0xA0)
                        {
                            if (TryResolveAssignedValue(script, node.Index, state, out BranchValue value))
                                state.Vars[instruction.Operand] = value;
                            else
                                state.Vars.Remove(instruction.Operand);
                            state.Relative = node.Next;
                            continue;
                        }

                        if (instruction.Opcode == 0x2C)
                        {
                            state.SwitchVar = node.Index > 0 && ins[node.Index - 1].Opcode == PUSHV
                                ? ins[node.Index - 1].Operand
                                : null;
                            state.Relative = node.Next;
                            continue;
                        }

                        if (instruction.Opcode == 0xB0)
                        {
                            if (TryJumpTarget(worker, instruction, out int target))
                            {
                                BranchTraversalState nextState = state.CloneFor(target, state.PathId + $"->j{instruction.Operand:X2}");
                                if (pending.Count < MaxPendingStatesPerHook)
                                    pending.Push(nextState);
                            }
                            break;
                        }

                        if (instruction.Opcode is 0xD6 or 0xD7)
                        {
                            string guard = DescribeBranchGuard(script, node.Index, state);
                            bool random = state.Random || GuardLooksRandom(script, node.Index);
                            if (TryJumpTarget(worker, instruction, out int target))
                            {
                                BranchTraversalState jumpState = instruction.Opcode == 0xD6
                                    ? state.CloneFor(target, state.PathId + $"->t{instruction.Operand:X2}", guard, random)
                                    : state.CloneFor(target, state.PathId + $"->f{instruction.Operand:X2}");
                                if (pending.Count < MaxPendingStatesPerHook)
                                    pending.Push(jumpState);
                            }

                            if (node.Next < script.CodeLength)
                            {
                                BranchTraversalState nextState = instruction.Opcode == 0xD7
                                    ? state.CloneFor(node.Next, state.PathId + "->next", guard, random)
                                    : state.CloneFor(node.Next, state.PathId + "->next");
                                if (pending.Count < MaxPendingStatesPerHook)
                                    pending.Push(nextState);
                            }
                            break;
                        }

                        if (instruction.Opcode == CALLPOPA && (instruction.Operand == PerformCommand || instruction.Operand == ForcePerformCommand))
                        {
                            if (TryCreateBranchAction(monsterBin, script, hookKind, hook, state, node.Index, absolute, instruction, out AiDetectedBranchAction? action))
                            {
                                string actionKey = $"{action.HookKind}|{action.CallOffset:X4}|{action.GuardSummary}|{action.TargetSummary}|{action.CommandSummary}";
                                if (seenActions.Add(actionKey))
                                {
                                    found.Add(action);
                                    actionsForHook++;
                                }
                            }
                            state.Relative = node.Next;
                            continue;
                        }

                        if (instruction.Opcode is 0x3C or 0x40)
                            break;

                        if (node.Next >= script.CodeLength)
                            break;
                        state.Relative = node.Next;
                    }
                }
            }

            return found
                .OrderBy(a => a.WorkerIndex)
                .ThenBy(a => a.EntrypointIndex)
                .ThenBy(a => a.CallOffset)
                .ThenBy(a => a.PathId, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Builds a structured, preview-only model for indirect dispatch units. The first supported slice is the
        /// Seymour-style "write rows into vars, then consume them later via PUSHV ... performCommand" table.
        /// </summary>
        public static IReadOnlyList<AiIndirectDispatchUnit> DetectIndirectDispatchUnits(byte[] monsterBin, AiScriptFile script)
        {
            ArgumentNullException.ThrowIfNull(monsterBin);
            ArgumentNullException.ThrowIfNull(script);

            if (TryBuildStrictSeymourDispatchUnits(script, out List<AiIndirectDispatchUnit> units))
                return units;

            if (TryBuildGenericSwitchDispatchUnits(script, out units))
                return units;

            if (TryBuildSupportIndirectPayloadPickerUnits(script, out units))
                return units;

            if (TryBuildReactiveSensorPreviewUnits(script, out units))
                return units;

            if (TryBuildSpecializedPreviewUnits(monsterBin, script, out units))
                return units;

            if (TryBuildRoundScriptedBossPreviewUnits(monsterBin, script, out units))
                return units;

            if (TryBuildTonberryCameraRoutingPreviewUnits(monsterBin, script, out units))
                return units;

            if (TryBuildEncounterKeyedAppearDisablePreviewUnits(monsterBin, script, out units))
                return units;

            return Array.Empty<AiIndirectDispatchUnit>();
        }

        static IEnumerable<(AiEventHook Hook, string Label)> ResolveReadableHooks(byte[] monsterBin, AiScriptFile script)
        {
            var seen = new HashSet<(int Worker, int Entry)>();

            if (AiWorkerMapping.TryResolveCombatOnTurn(monsterBin, script, out AiEventHook onTurn, out _)
                && seen.Add((onTurn.WorkerIndex, onTurn.EntrypointIndex)))
                yield return (onTurn, "onTurn real");

            if (AiWorkerMapping.TryResolveCombatOnHit(monsterBin, script, out AiEventHook onHit, out _)
                && seen.Add((onHit.WorkerIndex, onHit.EntrypointIndex)))
                yield return (onHit, "onHit real");

            for (int wi = 0; wi < script.Workers.Count; wi++)
            {
                AiWorker worker = script.Workers[wi];
                for (int ei = 0; ei < worker.Entrypoints.Count; ei++)
                {
                    if (seen.Contains((wi, ei))) continue;
                    yield return (new AiEventHook(wi, ei), ei == 0 ? "init/evento auxiliar" : "evento auxiliar");
                }
            }
        }

        static string BuildBranchStateKey(BranchTraversalState state)
        {
            string vars = string.Join(";", state.Vars
                .OrderBy(k => k.Key)
                .Select(k => $"{k.Key:X4}:{(int)k.Value.Kind}:{k.Value.Literal:X4}:{k.Value.Summary}:{k.Value.Provenance}"));
            return $"{state.Relative:X4}|{state.GuardSummary}|{state.SwitchVar?.ToString("X4") ?? "--"}|{state.Random}|{vars}";
        }

        static bool TryCreateBranchAction(
            byte[] monsterBin,
            AiScriptFile script,
            string hookKind,
            AiEventHook hook,
            BranchTraversalState state,
            int instructionIndex,
            int absoluteOffset,
            AiInstruction call,
            out AiDetectedBranchAction? action)
        {
            action = null;
            if (instructionIndex < 1) return false;

            AiInstruction cmdPush = script.Instructions[instructionIndex - 1];
            AiInstruction? targetPush = instructionIndex >= 2 ? script.Instructions[instructionIndex - 2] : null;

            bool commandResolved = TryResolveCommandSummary(
                script,
                instructionIndex - 1,
                cmdPush,
                state,
                out string commandSummary,
                out string commandProvenance,
                out bool commandIndirect);
            string targetSummary = "target calculated by the script";
            string targetProvenance = string.Empty;
            bool targetIndirect = false;
            bool targetResolved = targetPush != null
                && TryResolveTargetSummary(
                    script,
                    instructionIndex - 2,
                    targetPush,
                    state,
                    out targetSummary,
                    out targetProvenance,
                    out targetIndirect);
            if (!commandResolved)
                return false;

            string confidence = !commandIndirect && !targetIndirect
                ? "direto"
                : commandResolved && targetResolved
                    ? "indireto resolvido"
                    : "indireto parcial";

            string guardSummary = string.IsNullOrWhiteSpace(state.GuardSummary) ? "direta" : state.GuardSummary;
            var provisional = new AiDetectedBranchAction(
                hook.WorkerIndex,
                hook.EntrypointIndex,
                absoluteOffset,
                hookKind,
                confidence,
                guardSummary,
                targetSummary,
                commandSummary,
                targetProvenance,
                commandProvenance,
                state.PathId,
                Array.Empty<string>());

            action = provisional with { HighLevelHints = BuildHighLevelHints(monsterBin, script, provisional) };
            return true;
        }

        static bool TryResolveCommandSummary(
            AiScriptFile script,
            int cmdIndex,
            AiInstruction cmdPush,
            BranchTraversalState state,
            out string summary,
            out string provenance,
            out bool indirect)
        {
            indirect = false;
            summary = string.Empty;
            provenance = string.Empty;
            if (cmdPush.Opcode == PUSHII && AiCommandId.IsCommandOperand(cmdPush.Operand))
            {
                summary = $"{CommandDisplayName(cmdPush.Operand)} [0x{cmdPush.Operand:X4}]";
                provenance = "literal direto";
                return true;
            }

            if (cmdPush.Opcode == PUSHV)
            {
                indirect = true;
                if (state.Vars.TryGetValue(cmdPush.Operand, out BranchValue value) && value.Kind == BranchValueKind.CommandLiteral)
                {
                    summary = value.Summary;
                    provenance = DescribeVarResolution(script, cmdPush.Operand, value);
                    return true;
                }

                if (TryResolveIndirectCommandCandidates(script.Instructions, cmdIndex, cmdPush.Operand, out List<ushort> candidates))
                {
                    summary = DescribeIndirectCommandCandidates(candidates);
                    provenance = $"{VarName(script, cmdPush.Operand)} -> candidatos no script";
                    return true;
                }

                summary = $"comando via {VarName(script, cmdPush.Operand)}";
                provenance = $"{VarName(script, cmdPush.Operand)} -> sem literal fechado";
                return true;
            }

            return false;
        }

        static bool TryResolveTargetSummary(
            AiScriptFile script,
            int targetIndex,
            AiInstruction targetPush,
            BranchTraversalState state,
            out string summary,
            out string provenance,
            out bool indirect)
        {
            indirect = false;
            summary = string.Empty;
            provenance = string.Empty;
            if (targetPush.Opcode == PUSHII)
            {
                summary = DescribeTargetOperand(targetPush.Operand);
                provenance = "literal direto";
                return true;
            }

            if (targetPush.Opcode == PUSHV)
            {
                indirect = true;
                if (state.Vars.TryGetValue(targetPush.Operand, out BranchValue value))
                {
                    summary = value.Summary;
                    provenance = DescribeVarResolution(script, targetPush.Operand, value);
                    return true;
                }

                summary = $"alvo via {VarName(script, targetPush.Operand)}";
                provenance = $"{VarName(script, targetPush.Operand)} -> sem recipe fechada";
                return true;
            }

            if ((targetPush.Opcode == CALL || targetPush.Opcode == CALLPOPA) && targetPush.Operand == FindMatchingChr)
            {
                indirect = true;
                summary = DescribeFindMatchingRecipe(script.Instructions, targetIndex);
                provenance = "findMatchingChr inline";
                return true;
            }

            return false;
        }

        static bool TryResolveAssignedValue(AiScriptFile script, int storeIndex, BranchTraversalState state, out BranchValue value)
        {
            value = default;
            if (storeIndex <= 0) return false;
            AiInstruction source = script.Instructions[storeIndex - 1];

            if (source.Opcode == PUSHII)
            {
                if (AiCommandId.IsCommandOperand(source.Operand))
                {
                    value = new BranchValue(
                        BranchValueKind.CommandLiteral,
                        source.Operand,
                        $"{CommandDisplayName(source.Operand)} [0x{source.Operand:X4}]",
                        $"literal 0x{source.Operand:X4}");
                    return true;
                }

                string targetName = DescribeTargetOperand(source.Operand);
                if (!targetName.StartsWith("valor ", StringComparison.Ordinal))
                {
                    value = new BranchValue(
                        BranchValueKind.TargetLiteral,
                        source.Operand,
                        targetName,
                        $"literal {targetName}");
                    return true;
                }

                value = new BranchValue(
                    BranchValueKind.ScalarLiteral,
                    source.Operand,
                    source.Operand.ToString(),
                    $"literal {source.Operand}");
                return true;
            }

            if (source.Opcode == PUSHV && state.Vars.TryGetValue(source.Operand, out BranchValue copied))
            {
                string sourceVar = VarName(script, source.Operand);
                value = copied with { Provenance = string.IsNullOrWhiteSpace(copied.Provenance) ? sourceVar : $"{sourceVar} <- {copied.Provenance}" };
                return true;
            }

            if ((source.Opcode == CALL || source.Opcode == CALLPOPA) && source.Operand == FindMatchingChr)
            {
                value = new BranchValue(
                    BranchValueKind.TargetRecipe,
                    0,
                    DescribeFindMatchingRecipe(script.Instructions, storeIndex - 1),
                    "findMatchingChr");
                return true;
            }

            return false;
        }

        static bool TryJumpTarget(AiWorker worker, AiInstruction instruction, out int target)
        {
            target = 0;
            if (instruction.Operand >= worker.JumpTargets.Count) return false;
            target = worker.JumpTargets[instruction.Operand];
            return true;
        }

        static string DescribeBranchGuard(AiScriptFile script, int branchIndex, BranchTraversalState state)
        {
            IReadOnlyList<AiInstruction> ins = script.Instructions;
            if (branchIndex <= 0) return state.GuardSummary;

            if (AiVarConditionBuilder.TryReadImmediateComparison(ins, branchIndex - 3, out AiVarConditionClause clause))
            {
                string varName = VarName(script, clause.VariableIndex);
                string op = AiVarConditionBuilder.OperatorLabel(clause.Operator);
                return $"{varName} {op} {clause.Value}";
            }

            // PUSHII then PUSHY puts the switch value on top, so express the
            // decoded relation from the variable's side, including non-EQ cases.
            if (branchIndex >= 4 && state.SwitchVar.HasValue
                && ins[branchIndex - 2].Opcode == 0x29
                && ins[branchIndex - 3].Opcode == PUSHII
                && AiVarConditionBuilder.TryGetComparisonOperator(ins[branchIndex - 1].Opcode, true, out AiVarCompareOperator switchOp))
            {
                string varName = VarName(script, state.SwitchVar.Value);
                string op = AiVarConditionBuilder.OperatorLabel(switchOp);
                int value = unchecked((short)ins[branchIndex - 3].Operand);
                return $"{varName} {op} {value}";
            }

            if (GuardLooksRandom(script, branchIndex))
                return "random guard";

            return string.IsNullOrWhiteSpace(state.GuardSummary)
                ? "condicao do script"
                : string.Empty;
        }

        static bool GuardLooksRandom(AiScriptFile script, int branchIndex)
        {
            for (int i = branchIndex - 1; i >= Math.Max(0, branchIndex - 8); i--)
            {
                AiInstruction previous = script.Instructions[i];
                if ((previous.Opcode == CALL || previous.Opcode == CALLPOPA) && previous.Operand == GetRandomValue)
                    return true;
            }
            return false;
        }

        static string DescribeFindMatchingRecipe(IReadOnlyList<AiInstruction> ins, int callIndex)
        {
            if (callIndex >= 4
                && ins[callIndex].Operand == FindMatchingChr
                && ins[callIndex - 1].Opcode == PUSHII
                && ins[callIndex - 2].Opcode == PUSHII
                && ins[callIndex - 3].Opcode == PUSHII
                && ins[callIndex - 4].Opcode == PUSHII)
            {
                ushort group = ins[callIndex - 4].Operand;
                ushort property = ins[callIndex - 3].Operand;
                ushort selector = ins[callIndex - 1].Operand;
                if (group == FrontlineChars && property == ChrFieldIsAlive && selector == SelectorAny)
                    return "alvo vivo da linha de frente";
                if (group == MatchingGroup && property == ChrFieldHp && selector == SelectorLowest)
                    return "alvo vivo com menor HP";
                if (group == LastAttackerTarget)
                    return "ultimo atacante";
                string groupName = AiTargetNames.Get(group) ?? $"grupo 0x{group:X4}";
                return $"findMatchingChr({groupName})";
            }

            return "alvo calculado por findMatchingChr";
        }

        static string DescribeTargetOperand(ushort operand)
        {
            if (AiTargetNames.Get(operand) is string known
                && !known.StartsWith("MonsterType=", StringComparison.OrdinalIgnoreCase))
                return known;

            if (operand >= 0x1000 && operand < 0x1FFF)
            {
                short monsterId = (short)(operand - 0x1000);
                return $"MonsterType=m{monsterId:D3}";
            }

            if (operand >= 0x0013 && operand < 0x00FF)
                return $"Monster#{operand - 0x0013}";

            return $"valor 0x{operand:X4}";
        }

        static string VarName(AiScriptFile script, ushort operand) =>
            operand < script.Variables.Count
                ? script.Variables[operand].Name
                : $"var[{operand}]";

        static string DescribeVarResolution(AiScriptFile script, ushort varIndex, BranchValue value)
        {
            string head = VarName(script, varIndex);
            return string.IsNullOrWhiteSpace(value.Provenance) ? head : $"{head} <- {value.Provenance}";
        }

        static IReadOnlyList<string> BuildHighLevelHints(byte[] monsterBin, AiScriptFile script, AiDetectedBranchAction action)
        {
            var hints = new List<string>();
            if (action.HookKind.Contains("evento auxiliar", StringComparison.OrdinalIgnoreCase))
                hints.Add("entrypoint auxiliar contextual");

            if (action.HookKind == "onTurn real"
                && action.TargetSummary.Contains("Self", StringComparison.OrdinalIgnoreCase)
                && action.GuardSummary.Contains("== 0", StringComparison.OrdinalIgnoreCase))
                hints.Add("opener one-shot");

            if (action.TargetSummary.Contains("Seymour", StringComparison.OrdinalIgnoreCase)
                || action.TargetSummary.Contains("m124", StringComparison.OrdinalIgnoreCase))
                hints.Add("ally support");

            if (action.CommandSummary.Contains("Blizzara", StringComparison.OrdinalIgnoreCase)
                || action.CommandSummary.Contains("Thundara", StringComparison.OrdinalIgnoreCase)
                || action.CommandSummary.Contains("Watera", StringComparison.OrdinalIgnoreCase)
                || action.CommandSummary.Contains("Fira", StringComparison.OrdinalIgnoreCase)
                || action.CommandSummary.Contains("Blizzaga", StringComparison.OrdinalIgnoreCase)
                || action.CommandSummary.Contains("Thundaga", StringComparison.OrdinalIgnoreCase)
                || action.CommandSummary.Contains("Waterga", StringComparison.OrdinalIgnoreCase)
                || action.CommandSummary.Contains("Firaga", StringComparison.OrdinalIgnoreCase))
                hints.Add("rotacao elemental");

            if (action.TargetSummary.Contains("Aeon", StringComparison.OrdinalIgnoreCase))
                hints.Add("variante contra aeon");

            if (action.CommandSummary.Contains("Multi-", StringComparison.OrdinalIgnoreCase))
                hints.Add("multi-cast fase 2");

            if (LooksLikeContextualScriptedCut(script, action))
                hints.Add("corte contextual/scriptado");

            if (LooksLikePresentationHandoff(script, action))
                hints.Add("apresentacao/handoff");

            if (LooksLikeTalkOrchestration(script, action))
                hints.Add("trigger-command talk orchestration");

            if (LooksLikeDamageGatedTransition(script, action))
                hints.Add("damage-gated transition");

            if (LooksLikePhaseStateMachine(script, action))
            {
                hints.Add("phase-state machine");
                hints.Add("handoff de fase");
                hints.Add("apresentacao/handoff");
            }

            return hints
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        static bool LooksLikeTalkOrchestration(AiScriptFile script, AiDetectedBranchAction action) =>
            action.HookKind.Contains("evento auxiliar", StringComparison.OrdinalIgnoreCase)
            && HasNearbyCall(script, action.CallOffset, UsedCommand, 20, 4)
            && HasNearbyLiteral(script, action.CallOffset, TalkCommand, 20, 4)
            && HasNearbyCall(script, action.CallOffset, RunBtlSceneA, 20, 4)
            && (action.TargetSummary.Contains("LastAttacker", StringComparison.OrdinalIgnoreCase)
                || action.TargetProvenance.Contains("LastAttacker", StringComparison.OrdinalIgnoreCase));

        static bool LooksLikeDamageGatedTransition(AiScriptFile script, AiDetectedBranchAction action) =>
            action.HookKind.Equals("onHit real", StringComparison.OrdinalIgnoreCase)
            && action.CommandSummary.Contains("Special 1", StringComparison.OrdinalIgnoreCase)
            && (action.PathId.Contains("w0_e4", StringComparison.OrdinalIgnoreCase)
                || HasNearbyCall(script, action.CallOffset, ReadChrProperty, 48, 4)
                || HasNearbyLiteral(script, action.CallOffset, ChrFieldLastDamageTakenHp, 48, 4));

        static bool LooksLikeContextualScriptedCut(AiScriptFile script, AiDetectedBranchAction action) =>
            HasNearbyCall(script, action.CallOffset, RunBtlSceneA, 24, 4);

        static bool LooksLikePresentationHandoff(AiScriptFile script, AiDetectedBranchAction action) =>
            HasNearbyCall(script, action.CallOffset, RunBtlSceneB, 24, 4);

        static bool LooksLikePhaseStateMachine(AiScriptFile script, AiDetectedBranchAction action)
        {
            if (action.CommandSummary.Contains("Special 1", StringComparison.OrdinalIgnoreCase)
                && action.HookKind.Contains("evento auxiliar", StringComparison.OrdinalIgnoreCase))
                return HasNearbyCall(script, action.CallOffset, RemoveCommand, 24, 2)
                    || HasNearbyLiteral(script, action.CallOffset, TalkCommand, 24, 2)
                    || HasNearbyCall(script, action.CallOffset, RunBtlSceneB, 24, 2);

            return action.CommandSummary.Contains("Multi-", StringComparison.OrdinalIgnoreCase)
                && (action.CommandProvenance.Contains("priv002C", StringComparison.OrdinalIgnoreCase)
                    || action.CommandProvenance.Contains("priv0030", StringComparison.OrdinalIgnoreCase));
        }

        static bool HasNearbyCall(AiScriptFile script, int callOffset, ushort operand, int lookBackInstructions, int lookAheadInstructions)
        {
            foreach (AiInstruction instruction in EnumerateInstructionWindow(script, callOffset, lookBackInstructions, lookAheadInstructions))
            {
                if ((instruction.Opcode == CALL || instruction.Opcode == CALLPOPA) && instruction.Operand == operand)
                    return true;
            }

            return false;
        }

        static bool HasNearbyLiteral(AiScriptFile script, int callOffset, ushort operand, int lookBackInstructions, int lookAheadInstructions) =>
            EnumerateInstructionWindow(script, callOffset, lookBackInstructions, lookAheadInstructions)
                .Any(instruction => instruction.Opcode == PUSHII && instruction.Operand == operand);

        static IEnumerable<AiInstruction> EnumerateInstructionWindow(
            AiScriptFile script,
            int callOffset,
            int lookBackInstructions,
            int lookAheadInstructions)
        {
            IReadOnlyList<AiInstruction> instructions = script.Instructions;
            int index = -1;
            for (int i = 0; i < instructions.Count; i++)
            {
                if (instructions[i].Offset == callOffset)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
                yield break;

            int start = Math.Max(0, index - lookBackInstructions);
            int end = Math.Min(instructions.Count - 1, index + lookAheadInstructions);
            for (int i = start; i <= end; i++)
                yield return instructions[i];
        }

        /// <summary>Friendly name of a writeChrProperty buff: the known status preset or the raw
        /// field hex, "(em si)" when the chr ref is the self sentinel, and the value ("tirar" when 0).</summary>
        static string BuffName(AiInstruction chr, bool fieldLit, ushort fieldId, bool valLit, ushort value)
        {
            string status = fieldLit ? (StatusFieldName(fieldId) ?? $"campo 0x{fieldId:X2}") : "campo calculado";
            string self = chr.Opcode == PUSHII && chr.Operand == SelfTarget ? " (em si)" : "";
            string val = valLit ? (value == 0 ? " — tirar" : $" = {value}") : "";
            return $"{status}{self}{val}";
        }

        /// <summary>Friendly writeChrProperty field id -> name. Prefers the short self-status preset label,
        /// then the full chr-property name map (AiChrPropertyNames,
        /// cross-referenced from the public ATEL RE — 222 fields). Null only for a field absent from both.</summary>
        static string? StatusFieldName(ushort fieldId)
        {
            foreach (AiBuffPreset p in SelfBuffPresets) if (p.FieldId == fieldId) return p.Name;
            return AiChrPropertyNames.Get(fieldId);
        }

        /// <summary>The instruction list with a detected action's bytes dropped — feed to AiScript_File.Rebuild
        /// (after AiValidator.Validate catches the rare dangle where a jump/entrypoint targets the dropped range).</summary>

    }
}

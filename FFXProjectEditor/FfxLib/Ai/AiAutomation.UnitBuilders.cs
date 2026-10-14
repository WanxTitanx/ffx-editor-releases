using System;
using System.Collections.Generic;
using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Ai
{
    // F7.1: unit builders de preview/edição extraídos do god file AiAutomation.cs.
    public static partial class AiAutomation
    {
static bool TryBuildGenericSwitchDispatchUnits(AiScriptFile script, out List<AiIndirectDispatchUnit> units)
        {
            units = new List<AiIndirectDispatchUnit>();

            List<GenericIndirectDispatchConsumer> consumers = FindGenericIndirectDispatchConsumers(script);
            if (consumers.Count < 2)
                return false;

            List<(ushort CommandVariableIndex, ushort TargetVariableIndex)> slotPairs = consumers
                .OrderBy(consumer => consumer.CallOffset)
                .Select(consumer => (consumer.CommandVariableIndex, consumer.TargetVariableIndex))
                .Distinct()
                .ToList();
            List<ushort> commandVars = slotPairs.Select(pair => pair.CommandVariableIndex).Distinct().ToList();
            List<ushort> targetVars = slotPairs.Select(pair => pair.TargetVariableIndex).Distinct().ToList();
            List<GenericIndirectDispatchRouteCluster> clusters = FindGenericRouteClusters(script, commandVars, targetVars);
            if (clusters.Count < 2)
                return false;

            GenericIndirectDispatchRouteCluster[]? family = clusters
                .GroupBy(cluster => new GenericRouteFamilyKey(
                    cluster.NextStateVariableIndex,
                    string.Join(",", cluster.CommandWrites.Select(write => write.VariableIndex).OrderBy(x => x))))
                .Where(group =>
                    group.Count() >= 2
                    && group.All(cluster => cluster.CommandWrites.Count == commandVars.Count)
                    && group.All(cluster =>
                        cluster.CommandWrites.Select(write => write.VariableIndex).OrderBy(x => x).SequenceEqual(commandVars)))
                .OrderByDescending(group => group.Count())
                .ThenByDescending(group => group.First().CommandWrites.Count)
                .Select(group => group.OrderBy(cluster => cluster.StartOffset).ToArray())
                .FirstOrDefault();

            if (family == null || family.Length < 2)
                return false;

            ushort phaseVar = family[0].NextStateVariableIndex;
            bool hasSwitchGuard = HasDirectSwitchOnVariable(script, phaseVar);

            Dictionary<ushort, int> commandSlotIndexByVar = commandVars
                .Select((variableIndex, index) => (variableIndex, index))
                .ToDictionary(x => x.variableIndex, x => x.index);
            Dictionary<ushort, int> targetSlotIndexByVar = targetVars
                .Select((variableIndex, index) => (variableIndex, index))
                .ToDictionary(x => x.variableIndex, x => x.index);
            List<(GenericIndirectDispatchConsumer Consumer, int SlotIndex)> orderedConsumers = consumers
                .OrderBy(consumer => consumer.CallOffset)
                .Select(consumer =>
                {
                    int slotIndex = commandSlotIndexByVar.TryGetValue(consumer.CommandVariableIndex, out int commandSlotIndex)
                        ? commandSlotIndex
                        : targetSlotIndexByVar.GetValueOrDefault(consumer.TargetVariableIndex, 0);
                    return (consumer, slotIndex);
                })
                .ToList();

            var targetSlotSpecs = targetVars
                .Select((variableIndex, slotIndex) => (
                    variableIndex,
                    $"target.slot{slotIndex + 1}",
                    DescribeGenericTargetSlotLabel(variableIndex, slotIndex)))
                .ToArray();

            List<AiIndirectDispatchEditableTargetSlot> editableTargetSlots = BuildEditableTargetSlots(script, targetSlotSpecs);

            for (int routeIndex = 0; routeIndex < family.Length; routeIndex++)
            {
                GenericIndirectDispatchRouteCluster route = family[routeIndex];

                var payloadWrites = new List<AiIndirectDispatchWrite>();
                var editableSlots = new List<AiIndirectDispatchEditableSlot>();
                foreach ((ushort commandVariableIndex, int slotIndex) in commandVars.Select((variableIndex, index) => (variableIndex, index)))
                {
                    GenericIndirectDispatchCommandWrite? write = route.CommandWrites.FirstOrDefault(w => w.VariableIndex == commandVariableIndex);
                    if (write == null)
                        continue;

                    string slotLabel = DescribeGenericCommandSlotLabel(commandVariableIndex, slotIndex);
                    string valueSummary = $"{CommandDisplayName(write.CommandOperand)} [0x{write.CommandOperand:X4}]";

                    payloadWrites.Add(new AiIndirectDispatchWrite(
                        write.VariableIndex,
                        VarName(script, write.VariableIndex),
                        slotLabel,
                        valueSummary,
                        write.Offset));

                    editableSlots.Add(new AiIndirectDispatchEditableSlot(
                        $"command.slot{slotIndex + 1}",
                        slotLabel,
                        write.Offset,
                        write.CommandOperand,
                        valueSummary));
                }

                payloadWrites.Add(new AiIndirectDispatchWrite(
                    phaseVar,
                    VarName(script, phaseVar),
                    "next state",
                    route.NextState.ToString(),
                    route.NextStateOffset));

                var payloadConsumers = orderedConsumers
                    .Select(x => new AiIndirectDispatchConsumer(
                        DescribeGenericConsumerLabel(x.SlotIndex, x.Consumer.ForcePerform),
                        x.Consumer.CommandVariableIndex,
                        VarName(script, x.Consumer.CommandVariableIndex),
                        x.Consumer.TargetVariableIndex,
                        VarName(script, x.Consumer.TargetVariableIndex),
                        x.Consumer.CallOffset))
                    .ToList();

                var companionEffects = new List<string>
                {
                    "familia indireta detectada por shape estrutural: command vars consumidos por performCommand indireto + row packs literais + next-state.",
                    "This read does not depend on the priv0024/0028/002C/0030 names; it starts from the consumers and the row pack found in the script.",
                };

                units.Add(new AiIndirectDispatchUnit(
                    $"dispatch-generic-{phaseVar:X4}-{routeIndex}",
                    routeIndex,
                    hasSwitchGuard ? "onTurn real (switch estrutural)" : "onTurn real (row pack estrutural)",
                    hasSwitchGuard
                        ? $"{VarName(script, phaseVar)} == {routeIndex} (switch estrutural inferido)"
                        : $"rota {routeIndex} de {VarName(script, phaseVar)} (guard inferido pelo row pack)",
                    $"{VarName(script, phaseVar)} <- {route.NextState}",
                    AiIndirectDispatchCapabilityTier.AuthoringCandidate,
                    "candidato estrutural",
                    payloadWrites,
                    payloadConsumers,
                    companionEffects,
                    $"0x{route.StartOffset:X4}..0x{route.EndOffset:X4}",
                    "Generic structural read: good for Seymour's indirect sibling families. Still requires RT2 before being offered as a universal writer.",
                    editableSlots,
                    editableTargetSlots,
                    route.NextStateOffset,
                    route.NextState));
            }

            return units.Count >= 2;
        }

static bool TryBuildSupportIndirectPayloadPickerUnits(AiScriptFile script, out List<AiIndirectDispatchUnit> units)
        {
            units = new List<AiIndirectDispatchUnit>();

            List<GenericIndirectDispatchConsumer> consumers = FindGenericIndirectDispatchConsumers(script);
            if (consumers.Count == 0)
                return false;

            foreach (IGrouping<(ushort CommandVariableIndex, ushort TargetVariableIndex), GenericIndirectDispatchConsumer> family in consumers
                         .GroupBy(consumer => (consumer.CommandVariableIndex, consumer.TargetVariableIndex))
                         .OrderBy(group => group.Min(consumer => consumer.CallOffset)))
            {
                if (family.Count() > 3)
                    continue;

                List<SupportIndirectPayloadPickerCluster> clusters = FindSupportIndirectPayloadPickerClusters(
                    script,
                    family.Key.CommandVariableIndex,
                    family.Key.TargetVariableIndex,
                    family.ToList());
                if (clusters.Count == 0)
                    continue;

                List<AiIndirectDispatchEditableTargetSlot> editableTargetSlots = BuildEditableTargetSlots(
                    script,
                    (family.Key.TargetVariableIndex, "target.picker", "slot alvo calculado"));

                for (int unitIndex = 0; unitIndex < clusters.Count; unitIndex++)
                {
                    SupportIndirectPayloadPickerCluster cluster = clusters[unitIndex];
                    List<GenericIndirectDispatchCommandWrite> orderedWrites = cluster.CommandWrites
                        .OrderBy(write => write.Offset)
                        .ToList();

                    var payloadWrites = orderedWrites
                        .Select((write, slotIndex) => new AiIndirectDispatchWrite(
                            write.VariableIndex,
                            VarName(script, write.VariableIndex),
                            orderedWrites.Count == 1 ? "payload indireto" : $"payload opcao #{slotIndex + 1}",
                            $"{CommandDisplayName(write.CommandOperand)} [0x{write.CommandOperand:X4}]",
                            write.Offset))
                        .ToList();

                    var editableSlots = orderedWrites
                        .Select((write, slotIndex) => new AiIndirectDispatchEditableSlot(
                            $"command.slot{slotIndex + 1}",
                            orderedWrites.Count == 1 ? "payload indireto" : $"payload opcao #{slotIndex + 1}",
                            write.Offset,
                            write.CommandOperand,
                            $"{CommandDisplayName(write.CommandOperand)} [0x{write.CommandOperand:X4}]"))
                        .ToList();

                    var unitConsumers = new List<AiIndirectDispatchConsumer>
                    {
                        new(
                            cluster.ForcePerform ? "dispatch imediato contextual" : "dispatch contextual",
                            cluster.CommandVariableIndex,
                            VarName(script, cluster.CommandVariableIndex),
                            cluster.TargetVariableIndex,
                            VarName(script, cluster.TargetVariableIndex),
                            cluster.ConsumerCallOffset),
                    };

                    var notes = new List<string>
                    {
                        $"payload indireto real em {VarName(script, cluster.CommandVariableIndex)}.",
                        $"{VarName(script, cluster.TargetVariableIndex)} recebe alvo calculado antes do performCommand indireto.",
                        "Lightweight family: V2 can touch up payload and target without offering a structural clone or heavy route panel.",
                    };

                    if (cluster.IsRandomized)
                        notes.Add("Local selector with RNG before dispatch.");
                    else if (cluster.UsesSwitch)
                        notes.Add("Local selector via switch/cases before dispatch.");

                    units.Add(new AiIndirectDispatchUnit(
                        $"dispatch-support-{cluster.CommandVariableIndex:X4}-{cluster.TargetVariableIndex:X4}-{unitIndex}",
                        unitIndex,
                        "onTurn real (payload picker leve)",
                        cluster.GuardSummary,
                        $"{VarName(script, cluster.TargetVariableIndex)} -> performCommand indireto",
                        AiIndirectDispatchCapabilityTier.AuthoringCandidate,
                        "candidato payload picker",
                        payloadWrites,
                        unitConsumers,
                        notes,
                        $"0x{cluster.StartOffset:X4}..0x{cluster.EndOffset:X4}",
                        "Lightweight structural read: indirect payload + calculated target. V2 can edit the local trim without faking next-state / universal cloning.",
                        editableSlots,
                        editableTargetSlots,
                        null,
                        null));
                }
            }

            return units.Count > 0;
        }

        static bool TryBuildReactiveSensorPreviewUnits(AiScriptFile script, out List<AiIndirectDispatchUnit> units)
        {
            units = new List<AiIndirectDispatchUnit>();
            List<ReactiveSensorPattern> patterns = DetectReactiveSensorPatterns(script);
            if (patterns.Count == 0)
                return false;

            foreach (IGrouping<(string PropertyName, ushort StateVarIndex), ReactiveSensorPattern> group in patterns
                         .GroupBy(pattern => (pattern.PropertyName, pattern.StateVarIndex))
                         .OrderBy(group => group.Min(pattern => pattern.UsedCommandOffset)))
            {
                ReactiveSensorPattern first = group
                    .OrderBy(pattern => pattern.UsedCommandOffset)
                    .First();
                bool sceneStateOnlyGroup = group.All(pattern => pattern.UsesSceneStateOnly);
                int unitIndex = units.Count;
                string hookKind = group.Any(pattern => pattern.SceneCallOperand == RunBtlSceneA || pattern.SceneCallOperand == RunBtlSceneB)
                    ? "evento auxiliar (sensor reativo)"
                    : "sensor reativo";
                string guardSummary =
                    $"{VarName(script, first.UsedCommandVarIndex)} -> readMoveProperty.{first.PropertyName} " +
                    $"e reage em {VarName(script, first.StateVarIndex)}.";

                string nextStateSummary = string.Join(
                    " · ",
                    group
                        .Select(pattern => pattern.SceneStateValue.HasValue
                            ? $"{VarName(script, pattern.SceneStateVarIndex)} <- {pattern.SceneStateValue.Value}"
                            : string.Empty)
                        .Where(text => !string.IsNullOrWhiteSpace(text))
                        .Distinct(StringComparer.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(nextStateSummary))
                    nextStateSummary = $"{VarName(script, first.StateVarIndex)} acumula flags reativas.";

                var payloadWrites = new List<AiIndirectDispatchWrite>
                {
                    BuildPreviewNamedWrite(
                        script,
                        VarName(script, first.StateVarIndex),
                        sceneStateOnlyGroup ? "estado reativo" : "flag reativa",
                        sceneStateOnlyGroup
                            ? string.Join(
                                " / ",
                                group
                                    .Where(pattern => pattern.SceneStateValue.HasValue)
                                    .Select(pattern => pattern.SceneStateValue!.Value.ToString())
                                    .Distinct(StringComparer.OrdinalIgnoreCase))
                            : string.Join(
                                " / ",
                                group
                                    .Select(pattern => $"OR 0x{pattern.OrLiteral:X4}")
                                    .Distinct(StringComparer.OrdinalIgnoreCase)),
                        sceneStateOnlyGroup && first.SceneStateOffset >= 0 ? first.SceneStateOffset : first.OrWriteOffset),
                    BuildPreviewNamedWrite(
                        script,
                        VarName(script, first.PropertyVarIndex),
                        $"ultimo {first.PropertyName}",
                        $"lido de {VarName(script, first.UsedCommandVarIndex)}",
                        first.PropertyWriteOffset),
                };

                if (first.SceneStateVarIndex != 0xFFFF
                    && (!sceneStateOnlyGroup || first.SceneStateVarIndex != first.StateVarIndex))
                {
                    string sceneValues = string.Join(
                        " / ",
                        group
                            .Where(pattern => pattern.SceneStateValue.HasValue)
                            .Select(pattern => pattern.SceneStateValue!.Value.ToString())
                            .Distinct(StringComparer.OrdinalIgnoreCase));
                    if (!string.IsNullOrWhiteSpace(sceneValues))
                    {
                        payloadWrites.Add(BuildPreviewNamedWrite(
                            script,
                            VarName(script, first.SceneStateVarIndex),
                            "slot de cena",
                            sceneValues,
                            first.SceneStateOffset));
                    }
                }

                ushort targetIndex = PreviewTargetVariableIndex(unitIndex, 0);
                var targetSlots = new List<AiIndirectDispatchEditableTargetSlot>
                {
                    BuildPreviewTargetSlot(
                        unitIndex,
                        0,
                        "alvo observado",
                        "ultimo command observado",
                        $"sensor usa {VarName(script, first.UsedCommandVarIndex)} como entrada antes de classificar {first.PropertyName}.",
                        first.UsedCommandOffset,
                        AiIndirectDispatchTargetSlotSourceKind.ComputedRecipe),
                };

                var consumers = new List<AiIndirectDispatchConsumer>
                {
                    new(
                        group.Any(pattern => pattern.SceneCallOperand == RunBtlSceneB) ? "runBtlSceneB reativo" : "runBtlSceneA reativo",
                        first.PropertyVarIndex,
                        VarName(script, first.PropertyVarIndex),
                        targetIndex,
                        $"preview.target1",
                        group.Select(pattern => pattern.SceneCallOffset).Where(offset => offset >= 0).DefaultIfEmpty(first.UsedCommandOffset).Min()),
                };

                List<string> notes = new()
                {
                    sceneStateOnlyGroup
                        ? "family complexa lida como sensor reativo: usedCommand -> readMoveProperty -> scene-state, sem performCommand authoring."
                        : "family complexa lida como sensor reativo: usedCommand -> readMoveProperty -> flags/scene, sem performCommand authoring.",
                    $"property observada: {first.PropertyName}.",
                };

                if (group.Any(pattern => pattern.SceneCallOperand == RunBtlSceneA))
                    notes.Add("runBtlSceneA participa da reacao.");
                if (group.Any(pattern => pattern.SceneCallOperand == RunBtlSceneB))
                    notes.Add("runBtlSceneB participa da reacao.");
                notes.AddRange(DescribeReactiveAftermath(group, script));

                units.Add(new AiIndirectDispatchUnit(
                    $"preview-reactive-{group.Key.PropertyName}-{group.Key.StateVarIndex:X4}",
                    unitIndex,
                    hookKind,
                    guardSummary,
                    nextStateSummary,
                    AiIndirectDispatchCapabilityTier.PreviewReadOnly,
                    "preview sensor reativo",
                    payloadWrites,
                    consumers,
                    notes
                        .Where(text => !string.IsNullOrWhiteSpace(text))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    BuildOffsetSummary(group.Select(pattern => pattern.UsedCommandOffset)
                        .Concat(group.Select(pattern => pattern.SceneCallOffset).Where(offset => offset >= 0))),
                    "Preview read-only: a familia reage a golpes/elementos do command usado. O V2 mostra o pacote sem vender clone/topology writer.",
                    Array.Empty<AiIndirectDispatchEditableSlot>(),
                    targetSlots,
                    null,
                    null));
            }

            return units.Count > 0;
        }

        static bool TryBuildSpecializedPreviewUnits(byte[] monsterBin, AiScriptFile script, out List<AiIndirectDispatchUnit> units)
        {
            units = new List<AiIndirectDispatchUnit>();
            IReadOnlyList<AiDetectedBranchAction> branches = DetectBranchSensitiveActions(monsterBin, script);

            if (TryBuildOmnisPreviewUnits(script, branches, out List<AiIndirectDispatchUnit> omnisUnits))
            {
                units.AddRange(omnisUnits);
                return true;
            }

            if (TryBuildFluxPreviewUnits(script, branches, out List<AiIndirectDispatchUnit> fluxUnits))
            {
                units.AddRange(fluxUnits);
                return true;
            }

            if (TryBuildMortibodyPreviewUnits(script, branches, out List<AiIndirectDispatchUnit> mortibodyUnits))
            {
                units.AddRange(mortibodyUnits);
                return true;
            }

            if (TryBuildMortiorchisPreviewUnits(script, branches, out List<AiIndirectDispatchUnit> mortiorchisUnits))
            {
                units.AddRange(mortiorchisUnits);
                return true;
            }

            return false;
        }

        static bool TryBuildRoundScriptedBossPreviewUnits(
            byte[] monsterBin,
            AiScriptFile script,
            out List<AiIndirectDispatchUnit> units)
        {
            units = new List<AiIndirectDispatchUnit>();
            if (!script.HasScript || script.Instructions.Count == 0)
                return false;

            if (script.Workers.Count(worker => string.Equals(worker.InferredType, "CombatHandler", StringComparison.OrdinalIgnoreCase)) < 2
                || !script.Workers.Any(worker => string.Equals(worker.InferredType, "CameraHandler", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            bool hasCoreCommandSet =
                HasLiteralCommandSite(script, RoundLandingCommand)
                && HasLiteralCommandSite(script, RoundCrawlCommand)
                && HasLiteralCommandSite(script, RoundSonicBoomCommand)
                && HasLiteralCommandSite(script, RoundAeonPunishCommand)
                && HasLiteralCommandSite(script, RoundFinisherCommandA)
                && HasLiteralCommandSite(script, RoundFinisherCommandB);
            bool hasPresentationRoute =
                HasCallOperand(script, CALL, ChosenCommand)
                && HasCallOperand(script, CALL, CurrentEncounter)
                && HasCallOperand(script, CALLPOPA, CamReq)
                && CountChrPropertyWrites(script, StatVisibleCamField) >= 4;
            bool hasForcedLanding = HasLiteralDispatchCommand(script, ForcePerformCommand, RoundLandingCommand);
            if (!hasCoreCommandSet || !hasPresentationRoute || !hasForcedLanding)
                return false;

            IReadOnlyList<AiDetectedBranchAction> branches = DetectBranchSensitiveActions(monsterBin, script);
            List<AiDetectedBranchAction> landingActions = FilterDistinctBranchActions(branches, RoundLandingCommand);
            List<AiDetectedBranchAction> crawlActions = FilterDistinctBranchActions(branches, RoundCrawlCommand);
            List<AiDetectedBranchAction> sonicActions = FilterDistinctBranchActions(branches, RoundSonicBoomCommand);
            List<AiDetectedBranchAction> aeonPunishActions = FilterDistinctBranchActions(branches, RoundAeonPunishCommand);
            List<AiDetectedBranchAction> finisherActions = FilterDistinctBranchActions(branches, RoundFinisherCommandA, RoundFinisherCommandB);

            bool finisherDual =
                finisherActions.Any(action => BranchMatchesAnyCommand(action, RoundFinisherCommandA))
                && finisherActions.Any(action => BranchMatchesAnyCommand(action, RoundFinisherCommandB));
            if (landingActions.Count == 0
                || crawlActions.Count == 0
                || sonicActions.Count == 0
                || aeonPunishActions.Count == 0
                || finisherActions.Count == 0
                || !finisherDual)
            {
                return false;
            }

            int landingUnitIndex = units.Count;
            units.Add(BuildPreviewActionBundleUnit(
                script,
                "preview-round-landing",
                landingUnitIndex,
                "round-scripted-boss (landing / recovery)",
                "priv0008, priv0014 e um HP gate seguram o pouso/recovery direto em vez de montar row pack indireto.",
                "priv0008 <- 1 e priv0014 <- 1 armam o beat de landing antes do proximo round.",
                "preview round-scripted-boss",
                landingActions,
                new[]
                {
                    BuildPreviewNamedWrite(script, "priv0008", "latch local", "vira 1 quando o boss entra no beat de landing/recovery", landingActions[0].CallOffset),
                    BuildPreviewNamedWrite(script, "priv0014", "gate de reprise", "vira 1 no ramo com forcePerformCommand do landing", landingActions[^1].CallOffset),
                },
                new[]
                {
                    "0x4019 aparece como performCommand e tambem como forcePerformCommand; o reader mostra ambos, mas nao promove writer.",
                    "o pacote continua preso ao round-scripted-boss camera-heavy; nada aqui vira Seymour row pack.",
                }));

            int crawlUnitIndex = units.Count;
            units.Add(BuildPreviewActionBundleUnit(
                script,
                "preview-round-crawl",
                crawlUnitIndex,
                "round-scripted-boss (crawl de abertura)",
                "findMatchingChr escolhe alvo contextual antes do 0x401A e o script ainda acopla btlSetTexAnime local.",
                "priv0000 recebe o alvo direto do crawl; sem next-state authoring exposto.",
                "preview round-scripted-boss",
                crawlActions,
                new[]
                {
                    BuildPreviewNamedWrite(script, "priv0000", "alvo direto", "resultado de findMatchingChr antes do Crawl", crawlActions[0].CallOffset),
                },
                new[]
                {
                    "0x401A e um beat direto/contextual do opener, nao um consumer indireto sobre slots Seymour-style.",
                }));

            int sonicUnitIndex = units.Count;
            units.Add(BuildPreviewActionBundleUnit(
                script,
                "preview-round-sonic-boom",
                sonicUnitIndex,
                "round-scripted-boss (sonic boom de round)",
                "priv000C funciona como contador/local fork antes do 0x4016.",
                "priv000C reseta ou incrementa antes do pacote Sonic Boom / finisher.",
                "preview round-scripted-boss",
                sonicActions,
                new[]
                {
                    BuildPreviewNamedWrite(script, "priv000C", "contador local de beat", "reseta para 0 ou prepara o fork seguinte antes do Sonic Boom", sonicActions[0].CallOffset),
                },
                new[]
                {
                    "Sonic Boom continua tratado como cast direto dependente do contador local, sem writer generico de state machine.",
                }));

            int aeonUnitIndex = units.Count;
            units.Add(BuildPreviewActionBundleUnit(
                script,
                "preview-round-aeon-punish",
                aeonUnitIndex,
                "round-scripted-boss (anti-aeon contextual)",
                "countChrOverlap(Aeons) e battleVar0008 trocam o beat direto para 0x4097 sem consumer indireto.",
                "battleVar0008 <- 2 e o alvo contextual em priv0000 empurram o punish antes do pacote final.",
                "preview round-scripted-boss",
                aeonPunishActions,
                new[]
                {
                    BuildPreviewNamedWrite(script, "battleVar0008", "gate anti-aeon", "valor 2 abre o beat contextual antes do pacote final", aeonPunishActions[0].CallOffset),
                    BuildPreviewNamedWrite(script, "priv0000", "alvo contextual", "resultado de findMatchingChr no ramo contra aeon/frontline", aeonPunishActions[0].CallOffset),
                },
                new[]
                {
                    "0x4097 continua contextual ao overlap de aeon/frontline; o V2 so mostra o pacote direto e o gate relevante.",
                }));

            int finisherUnitIndex = units.Count;
            units.Add(BuildPreviewActionBundleUnit(
                script,
                "preview-round-finisher",
                finisherUnitIndex,
                "round-scripted-boss (finisher + camera choreography)",
                "priv000C e battleVar0008 escolhem o fork direto entre 0x40AB e 0x40DF, enquanto a CameraHandler usa chosenCommand / CurrentEncounter / camReq.",
                "battleVar00F4 e battleVar0288 roteiam encounter/camera; stat_visible_cam e camReq seguram a apresentacao do beat final.",
                "preview round-scripted-boss",
                finisherActions,
                new[]
                {
                    BuildPreviewNamedWrite(script, "priv000C", "contador local de beat", "3/4 decidem o fork entre 0x40AB e 0x40DF", finisherActions[0].CallOffset),
                    BuildPreviewNamedWrite(script, "battleVar00F4", "router de encounter", "CurrentEncounter alimenta o switch de apresentacao/camera", finisherActions[0].CallOffset),
                    BuildPreviewNamedWrite(script, "battleVar0288", "router de camera/alvo", "chosenCommand + dereferenceEnemy/Character alimentam o camReq contextual", finisherActions[0].CallOffset),
                    BuildPreviewPseudoWrite(finisherUnitIndex, 0, "presentation gate", "camReq + stat_visible_cam orquestram o beat visual", finisherActions[0].CallOffset),
                },
                new[]
                {
                    "o round-scripted-boss tem worker de camera proprio; isso e parte da familia, nao detalhe descartavel.",
                    "0x40AB e 0x40DF continuam tratados como bundle direto de finisher, sem promotion row-only.",
                }));

            return units.Count == 5;
        }

        static bool TryBuildTonberryCameraRoutingPreviewUnits(
            byte[] monsterBin,
            AiScriptFile script,
            out List<AiIndirectDispatchUnit> units)
        {
            units = new List<AiIndirectDispatchUnit>();
            if (!script.HasScript || script.Instructions.Count == 0)
                return false;

            int combatHandlers = script.Workers.Count(worker => string.Equals(worker.InferredType, "CombatHandler", StringComparison.OrdinalIgnoreCase));
            int motionHandlers = script.Workers.Count(worker => string.Equals(worker.InferredType, "MotionHandler", StringComparison.OrdinalIgnoreCase));
            int cameraHandlers = script.Workers.Count(worker => string.Equals(worker.InferredType, "CameraHandler", StringComparison.OrdinalIgnoreCase));
            if (combatHandlers != 1 || motionHandlers < 2 || cameraHandlers != 1)
                return false;

            bool hasCombatShape =
                HasLiteralCommandSite(script, TonberryPressureCommand)
                && HasLiteralCommandSite(script, TonberryAdvanceCommand)
                && HasLiteralCommandSite(script, TonberryCounterCommand)
                && (HasLiteralCommandSite(script, TonberryFallbackCommandA) || HasLiteralCommandSite(script, TonberryFallbackCommandB))
                && CountChrPropertyWrites(script, TonberryPositionToMoveToField) >= 4;
            bool hasCameraShape =
                HasCallOperand(script, CALL, CurrentEncounter)
                && HasCallOperand(script, CALL, CountChrOverlap)
                && HasCallOperand(script, CALL, DereferenceEnemy)
                && HasCallOperand(script, CALL, DereferenceCharacter)
                && HasCallOperand(script, CALLPOPA, CamReq)
                && CountChrPropertyWrites(script, StatVisibleCamField) >= 6;
            bool leaksRoundScriptedBoss =
                HasCallOperand(script, CALL, ChosenCommand)
                || HasLiteralDispatchCommand(script, ForcePerformCommand, RoundLandingCommand)
                || HasLiteralCommandSite(script, RoundLandingCommand)
                || HasLiteralCommandSite(script, RoundCrawlCommand)
                || HasLiteralCommandSite(script, RoundSonicBoomCommand)
                || HasLiteralCommandSite(script, RoundAeonPunishCommand)
                || HasLiteralCommandSite(script, RoundFinisherCommandA)
                || HasLiteralCommandSite(script, RoundFinisherCommandB);
            if (!hasCombatShape || !hasCameraShape || leaksRoundScriptedBoss)
                return false;

            IReadOnlyList<AiDetectedBranchAction> branches = DetectBranchSensitiveActions(monsterBin, script);
            List<AiDetectedBranchAction> pressureActions = FilterDistinctBranchActions(
                branches,
                TonberryPressureCommand,
                TonberryFallbackCommandA,
                TonberryFallbackCommandB);
            List<AiDetectedBranchAction> advanceActions = FilterDistinctBranchActions(branches, TonberryAdvanceCommand);
            List<AiDetectedBranchAction> counterActions = FilterDistinctBranchActions(branches, TonberryCounterCommand);
            if (pressureActions.Count < 2 || advanceActions.Count < 4 || counterActions.Count == 0)
                return false;

            int pressureUnitIndex = units.Count;
            units.Add(BuildPreviewActionBundleUnit(
                script,
                "preview-tonberry-direct-pressure",
                pressureUnitIndex,
                "tonberry-camera-routing-boss (pressure direta)",
                "findMatchingChr escolhe o frontline/last-attacker e o pacote direto troca entre 0x4061 e o fallback do sibling.",
                "priv0000 segura o alvo contextual; o fallback muda entre 0x4062 (m223) e 0x410F (m224) sem abrir row pack indireto.",
                "preview tonberry camera routing",
                pressureActions,
                new[]
                {
                    BuildPreviewNamedWrite(script, "priv0000", "alvo contextual", "resultado de findMatchingChr nos ramos de pressao/fallback", pressureActions[0].CallOffset),
                    BuildPreviewNamedWrite(script, "priv0008", "etapa local", "0/1 escolhe se o script segue no pacote de pressao ou na caminhada", pressureActions[0].CallOffset),
                },
                new[]
                {
                    "Os siblings compartilham o mesmo shape de pressao direta, mas o fallback final diverge: 0x4062 em m223 e 0x410F em m224.",
                    "Isto continua pacote direto/contextual; nao e consumer indireto generic switch.",
                }));

            int advanceUnitIndex = units.Count;
            units.Add(BuildPreviewActionBundleUnit(
                script,
                "preview-tonberry-position-cycle",
                advanceUnitIndex,
                "tonberry-camera-routing-boss (ciclo de aproximacao)",
                "priv0008 sobe 1/2/3/4 e cada degrau grava PositionToMoveTo antes do 0x4081.",
                "priv000C arma a janela local enquanto o boss avanca entre os pontos 1..4.",
                "preview tonberry camera routing",
                advanceActions,
                new[]
                {
                    BuildPreviewNamedWrite(script, "priv0008", "etapa da trilha", "1/2/3/4 decidem o PositionToMoveTo antes do 0x4081", advanceActions[0].CallOffset),
                    BuildPreviewNamedWrite(script, "priv000C", "janela local", "1 marca que a caminhada/cast atual ainda esta no pacote de aproximacao", advanceActions[0].CallOffset),
                    BuildPreviewPseudoWrite(advanceUnitIndex, 0, "PositionToMoveTo", "Self.?PositionToMoveTo <- 1/2/3/4 antes de cada 0x4081", advanceActions[0].CallOffset),
                },
                new[]
                {
                    "O V2 trata os quatro passos como um ciclo unico de aproximacao/reposicionamento, nao como quatro rotas editaveis separadas.",
                    "0x4081 aparece acoplado ao campo PositionToMoveTo em vez de nascer de tabela Seymour-style.",
                }));

            int counterUnitIndex = units.Count;
            units.Add(BuildPreviewActionBundleUnit(
                script,
                "preview-tonberry-counter-window",
                counterUnitIndex,
                "tonberry-camera-routing-boss (retaliacao)",
                "isCounterattackAllowed + priv000C + priv0008>=4 seguram a janela de retaliacao no LastAttacker.",
                "priv000C reseta para 0 quando a janela fecha; 0x4060 marca o counter base observado nos dois siblings.",
                "preview tonberry camera routing",
                counterActions,
                new[]
                {
                    BuildPreviewNamedWrite(script, "priv000C", "janela de retaliacao", "1 arma o counter; 0 fecha a janela quando a checagem falha", counterActions[0].CallOffset),
                    BuildPreviewNamedWrite(script, "priv0008", "progresso da trilha", "4+ abre a retaliacao no pacote de counter", counterActions[0].CallOffset),
                },
                new[]
                {
                    "LastAttacker continua receita contextual do counter; o preview explica a janela sem vender popup de target/clone.",
                    "m224 ainda reusa 0x4061 numa variante do ramo de counter quando o overlap muda; o 0x4060 continua o anchor comum mais seguro.",
                }));

            int encounterOffset = FindFirstCallOffset(script, CALL, CurrentEncounter);
            int dereferenceEnemyOffset = FindFirstCallOffset(script, CALL, DereferenceEnemy);
            int dereferenceCharacterOffset = FindFirstCallOffset(script, CALL, DereferenceCharacter);
            int camReqOffset = FindFirstCallOffset(script, CALLPOPA, CamReq);
            int visibleCamOffset = FindFirstChrPropertyWriteOffset(script, StatVisibleCamField);
            int cameraUnitIndex = units.Count;
            units.Add(new AiIndirectDispatchUnit(
                "preview-tonberry-camera-routing",
                cameraUnitIndex,
                "tonberry-camera-routing-boss (roteador de camera)",
                "CurrentEncounter + countChrOverlap escolhem quem vira router vivo da camera antes do bloco visual.",
                "battleVar0288 alimenta dereferenceCharacter; stat_visible_cam e camReq seguram a apresentacao contextual do pacote.",
                AiIndirectDispatchCapabilityTier.PreviewReadOnly,
                "preview tonberry camera routing",
                new[]
                {
                    BuildPreviewNamedWrite(script, "battleVar0288", "router de actor/camera", "recebe o actor vindo de dereferenceEnemy antes do dereferenceCharacter / camReq", dereferenceEnemyOffset),
                    BuildPreviewPseudoWrite(cameraUnitIndex, 0, "presentation gate", "stat_visible_cam limpa atores 20/21/22 conforme o router atual", visibleCamOffset),
                    BuildPreviewPseudoWrite(cameraUnitIndex, 1, "camera request", "camReq fecha a coreografia contextual por encounter", camReqOffset),
                },
                Array.Empty<AiIndirectDispatchConsumer>(),
                new[]
                {
                    "CameraHandler propria da familia: o preview existe para dissecar routing/apresentacao, nao para vender writer de camera ou encounter.",
                    "battleVar0288 nasce de dereferenceEnemy(ActiveActors/TargetActors) e depois passa por dereferenceCharacter antes do bloco visual.",
                    "Os mesmos actors 20/21/22 entram no stat_visible_cam conforme o encounter e o router contextual.",
                },
                BuildOffsetSummary(new[] { encounterOffset, dereferenceEnemyOffset, dereferenceCharacterOffset, visibleCamOffset, camReqOffset }.Where(offset => offset >= 0)),
                "Preview read-only: pacote camera-heavy/contextual. O V2 explica routing de actor e apresentacao sem fingir writer de camera.",
                Array.Empty<AiIndirectDispatchEditableSlot>(),
                Array.Empty<AiIndirectDispatchEditableTargetSlot>(),
                null,
                null));

            return units.Count == 4;
        }

        static bool TryBuildEncounterKeyedAppearDisablePreviewUnits(
            byte[] monsterBin,
            AiScriptFile script,
            out List<AiIndirectDispatchUnit> units)
        {
            units = new List<AiIndirectDispatchUnit>();
            if (!script.HasScript || script.Instructions.Count == 0)
                return false;

            bool hasStealTrigger =
                HasCallOperand(script, CALL, UsedCommand)
                && script.Instructions.Any(instruction => instruction.Opcode == PUSHII && instruction.Operand == StealCommand);
            bool hasEncounterRouter = HasCallOperand(script, CALL, CurrentEncounter);
            bool hasOpen = HasLiteralDispatchCommand(script, PerformCommand, OpenCommand);
            bool hasMimicReveal = HasLiteralDispatchCommand(script, PerformCommand, Mimic1Command);
            bool hasVisibilityFlip =
                CountChrPropertyWrites(script, TargetableField) >= 2
                && CountChrPropertyWrites(script, VisibleOnCtbField) >= 2
                && CountChrPropertyWrites(script, CtbIconNumberField) >= 2
                && CountChrPropertyWrites(script, MustBeKilledForBattleEndField) >= 2;
            bool hasCommandDisable =
                HasCallOperand(script, CALLPOPA, SetCommandDisabled)
                && script.Instructions.Any(instruction => instruction.Opcode == PUSHII && instruction.Operand == CopycatCommand)
                && script.Instructions.Any(instruction => instruction.Opcode == PUSHII && instruction.Operand == EscapeCommand)
                && script.Instructions.Any(instruction => instruction.Opcode == PUSHII && instruction.Operand == FleeCommand);
            if (!hasStealTrigger || !hasEncounterRouter || !hasOpen || !hasMimicReveal || !hasVisibilityFlip || !hasCommandDisable)
                return false;

            IReadOnlyList<AiDetectedBranchAction> branches = DetectBranchSensitiveActions(monsterBin, script);
            List<AiDetectedBranchAction> openActions = FilterDistinctBranchActions(branches, OpenCommand);
            List<AiDetectedBranchAction> mimicActions = FilterDistinctBranchActions(branches, Mimic1Command);
            if (openActions.Count == 0 || mimicActions.Count == 0)
                return false;

            int encounterOffset = FindFirstCallOffset(script, CALL, CurrentEncounter);
            int selfUntargetableOffset = FindFirstChrPropertyWriteOffset(script, TargetableField);
            int visibleOffset = FindFirstChrPropertyWriteOffset(script, VisibleOnCtbField);
            int ctbOffset = FindFirstChrPropertyWriteOffset(script, CtbIconNumberField);
            int battleEndOffset = FindFirstChrPropertyWriteOffset(script, MustBeKilledForBattleEndField);
            int disableOffset = FindFirstCallOffset(script, CALLPOPA, SetCommandDisabled);

            int openUnitIndex = units.Count;
            units.Add(BuildPreviewActionBundleUnit(
                script,
                "preview-encounter-open",
                openUnitIndex,
                "onHit real (steal trigger)",
                "usedCommand == Steal + CurrentBattle escolhem o opening/contexto antes do reveal encounter-keyed.",
                "battleVar0024 <- 1 | priv000C <- CurrentBattle antes do pacote de Open.",
                "preview encounter-keyed appear-disable",
                openActions,
                new AiIndirectDispatchWrite[]
                {
                    BuildPreviewNamedWrite(script, "battleVar0024", "latch de abertura", "1", openActions[0].CallOffset),
                    BuildPreviewNamedWrite(script, "priv000C", "router de encounter", "CurrentBattle seleciona o alvo contextual do reveal", encounterOffset >= 0 ? encounterOffset : openActions[0].CallOffset),
                },
                new[]
                {
                    "O host abre a si mesmo com 0x4037 antes de transferir a cena para o ator encounter-specific.",
                    "O pacote continua reativo/contextual ao Steal; nao e generic switch nem painel pesado Seymour-style.",
                }));

            int handoffUnitIndex = units.Count;
            units.Add(BuildPreviewActionBundleUnit(
                script,
                "preview-encounter-appear-disable",
                handoffUnitIndex,
                "onHit real (reveal / disable)",
                "CurrentBattle escolhe o actor de reveal e o pacote troca flags de presenca/CTB enquanto desliga o host.",
                "self.Targetable <- 0 | actor contextual ganha CTB/Targetable/VisibleOnCTB/MustBeKilledForBattleEnd | Copycat/Escape/Flee ficam desabilitados.",
                "preview encounter-keyed appear-disable",
                mimicActions,
                new AiIndirectDispatchWrite[]
                {
                    BuildPreviewNamedWrite(script, "priv000C", "router de encounter", "CurrentBattle decide se o reveal vai para 0x10FB / 0x10FC / 0x10FD / 0x1100", encounterOffset >= 0 ? encounterOffset : mimicActions[0].CallOffset),
                    BuildPreviewNamedWrite(script, "battleVar0024", "latch de reveal", "1", mimicActions[0].CallOffset),
                    BuildPreviewPseudoWrite(handoffUnitIndex, 0, "self.Targetable", "host fica intangivel apos o reveal", selfUntargetableOffset >= 0 ? selfUntargetableOffset : mimicActions[0].CallOffset),
                    BuildPreviewPseudoWrite(handoffUnitIndex, 1, "CTBIconNumber / VisibleOnCTB", "ator contextual entra no CTB visivel", ctbOffset >= 0 ? ctbOffset : (visibleOffset >= 0 ? visibleOffset : mimicActions[0].CallOffset)),
                    BuildPreviewPseudoWrite(handoffUnitIndex, 2, "battle-end flag", "ator contextual vira obrigatorio para encerrar a batalha", battleEndOffset >= 0 ? battleEndOffset : mimicActions[0].CallOffset),
                    BuildPreviewPseudoWrite(handoffUnitIndex, 3, "command disable", "Copycat / Escape / Flee ficam travados enquanto o reveal contextual assume", disableOffset >= 0 ? disableOffset : mimicActions[0].CallOffset),
                },
                new[]
                {
                    "0x40A2 continua pacote direto de reveal/handoff; o V2 mostra o actor encounter-keyed sem promover writer de topology.",
                    "O lane atual fecha como pilot do m211, mas a familia foi nomeada por shape para aceitar siblings futuros com o mesmo handoff.",
                }));

            return units.Count == 2;
        }

        static bool TryBuildOmnisPreviewUnits(
            AiScriptFile script,
            IReadOnlyList<AiDetectedBranchAction> branches,
            out List<AiIndirectDispatchUnit> units)
        {
            units = new List<AiIndirectDispatchUnit>();
            ushort[] elementalCommands = { 0x3045, 0x3046, 0x3047, 0x3048, 0x3049, 0x304A, 0x304B, 0x304C };
            List<AiDetectedBranchAction> elementalActions = FilterDistinctBranchActions(branches, elementalCommands);
            if (elementalActions.Count < 4 || CountSelfPropertyWrites(script, "Absorb", "Null", "Resist", "Weak") < 12)
                return false;

            List<AiDetectedBranchAction> dispelActions = FilterDistinctBranchActions(branches, 0x303D);
            List<AiDetectedBranchAction> ultimaActions = FilterDistinctBranchActions(branches, 0x60F0);

            units.Add(BuildPreviewActionBundleUnit(
                script,
                "preview-omnis-elemental",
                0,
                "onTurn real (orquestrador elemental)",
                "battleVar0004 + priv0018/priv001C/priv0020/priv0024 governam alinhamento dos Mortiphasms e a barragem elemental direta.",
                "priv0028 alterna o pacote entre barragem, Dispel break e Ultima break.",
                "preview complexa elemental",
                elementalActions,
                new[]
                {
                    BuildPreviewNamedWrite(script, "battleVar0004", "contador de ataques", "marca quantos ataques ja forcarm a rotacao especial", elementalActions[0].CallOffset),
                    BuildPreviewNamedWrite(script, "priv0018", "disco #1", "estado/alinhamento elemental observado", elementalActions[0].CallOffset),
                    BuildPreviewNamedWrite(script, "priv001C", "disco #2", "estado/alinhamento elemental observado", elementalActions[0].CallOffset),
                    BuildPreviewNamedWrite(script, "priv0020", "disco #3", "estado/alinhamento elemental observado", elementalActions[0].CallOffset),
                    BuildPreviewNamedWrite(script, "priv0024", "disco #4", "estado/alinhamento elemental observado", elementalActions[0].CallOffset),
                },
                new[]
                {
                    "os Mortiphasms escrevem Absorb/Null/Resist/Weak direto no Seymour em vez de montar um row pack indireto.",
                    "1-2 discos alinhados puxam -ra; 3-4 discos alinhados puxam -ga.",
                }));

            if (dispelActions.Count > 0)
            {
                units.Add(BuildPreviewActionBundleUnit(
                    script,
                    "preview-omnis-dispel-break",
                    units.Count,
                    "onTurn real (quebra defensiva)",
                    "priv0028 prepara a quebra apos o threshold de ataques/HP.",
                    "priv0028 <- 2 · DEF <- 100.",
                    "preview complexa elemental",
                    dispelActions,
                    new[]
                    {
                        BuildPreviewNamedWrite(script, "priv0028", "estado da estrategia", "2", dispelActions[0].CallOffset),
                        BuildPreviewPseudoWrite(units.Count, 0, "auto-buff", "DEF <- 100", dispelActions[0].CallOffset),
                    },
                    new[]
                    {
                        "Dispel abre a janela antes da Ultima opaca e reduz a defesa base do Seymour.",
                    }));
            }

            if (ultimaActions.Count > 0)
            {
                units.Add(BuildPreviewActionBundleUnit(
                    script,
                    "preview-omnis-ultima-break",
                    units.Count,
                    "onTurn real (ultima opaca)",
                    "priv0028 >= 2 leva ao pacote opaco de Ultima/cena.",
                    "priv0028 <- 3 · battleVar0018 <- 1 · DEF <- 150 · runBtlSceneB.",
                    "preview complexa elemental",
                    ultimaActions,
                    new[]
                    {
                        BuildPreviewNamedWrite(script, "priv0028", "estado da estrategia", "3", ultimaActions[0].CallOffset),
                        BuildPreviewNamedWrite(script, "battleVar0018", "cooldown/flag de quebra", "1", ultimaActions[0].CallOffset),
                        BuildPreviewPseudoWrite(units.Count, 1, "auto-buff", "DEF <- 150", ultimaActions[0].CallOffset),
                    },
                    new[]
                    {
                        "0x60F0 = comando de cena (Ultima trigger, namespace 0x6 via DispatchNativeCall) - nao e opcode VM.",
                        "o detector expoe o bundle como UltimaSceneTrigger; operando u16 do CALL nativo.",
                    }));
            }

            return units.Count > 0;
        }

        static bool TryBuildFluxPreviewUnits(
            AiScriptFile script,
            IReadOnlyList<AiDetectedBranchAction> branches,
            out List<AiIndirectDispatchUnit> units)
        {
            units = new List<AiIndirectDispatchUnit>();
            bool hostCoupling = HasCallOperand(script, CALLPOPA, 0x7052);
            List<AiDetectedBranchAction> lanceActions = FilterDistinctBranchActions(branches, 0x6078);
            List<AiDetectedBranchAction> dispelActions = FilterDistinctBranchActions(branches, 0x303D);
            List<AiDetectedBranchAction> selfBuffActions = FilterDistinctBranchActions(branches, 0x303B, 0x303C, 0x6079);
            List<AiDetectedBranchAction> banishActions = FilterDistinctBranchActions(branches, 0x6050);

            if (!hostCoupling || (lanceActions.Count == 0 && dispelActions.Count == 0 && selfBuffActions.Count == 0))
                return false;

            if (lanceActions.Count > 0)
            {
                units.Add(BuildPreviewActionBundleUnit(
                    script,
                    "preview-flux-lance-cycle",
                    units.Count,
                    "onTurn real (host + companheiro)",
                    "battleVar0014/0018/001C/0020 e priv0000/priv0018/priv001C formam o ciclo do Flux acoplado ao Mortiorchis.",
                    "battleVar0014 avanca o ciclo de Lance/Dispel/Cross Cleave.",
                    "preview acoplada host/companheiro",
                    lanceActions,
                    new[]
                    {
                        BuildPreviewNamedWrite(script, "battleVar0014", "estado do ciclo", "2 / 4", lanceActions[0].CallOffset),
                        BuildPreviewNamedWrite(script, "priv0000", "gate local", "zero = branch direta; >0 = branch adiada", lanceActions[0].CallOffset),
                    },
                    new[]
                    {
                        "attachActor/Host real: Seymour Flux e Mortiorchis funcionam como dupla acoplada.",
                        Strings.U_Ai_UnitBuilderLanceNote,
                    }));
            }

            if (dispelActions.Count > 0)
            {
                units.Add(BuildPreviewActionBundleUnit(
                    script,
                    "preview-flux-dispel-cross",
                    units.Count,
                    "onTurn real (janela de limpeza)",
                    "battleVar0014 leva ao beat party-wide de Dispel antes do follow-up do companheiro.",
                    "battleVar0014 <- 6.",
                    "preview acoplada host/companheiro",
                    dispelActions,
                    new[]
                    {
                        BuildPreviewNamedWrite(script, "battleVar0014", "estado do ciclo", "6", dispelActions[0].CallOffset),
                    },
                    new[]
                    {
                        "Cross Cleave vive no Mortiorchis; o V2 mostra o beat do host e preserva a nota do follow-up acoplado.",
                    }));
            }

            if (selfBuffActions.Count > 0)
            {
                units.Add(BuildPreviewActionBundleUnit(
                    script,
                    "preview-flux-self-buff",
                    units.Count,
                    "onTurn real (threshold HP)",
                    "abaixo de 75%/50% o Flux entra no pacote de Protect/Reflect/Flare com branch direta.",
                    "battleVar0018/001C ajustam o rhythm do pacote de Reflect/Flare.",
                    "preview acoplada host/companheiro",
                    selfBuffActions,
                    new[]
                    {
                        BuildPreviewNamedWrite(script, "battleVar0018", "flag sub-50%", "1 apos o pacote mais agressivo", selfBuffActions[0].CallOffset),
                        BuildPreviewNamedWrite(script, "battleVar001C", "flag contextual", "usado para recast/espera", selfBuffActions[0].CallOffset),
                    },
                    new[]
                    {
                        "Protect/Reflect sao casts diretos do host; o detector nao os deforma em falsa tabela de rota.",
                    }));
            }

            if (banishActions.Count > 0)
            {
                units.Add(BuildPreviewActionBundleUnit(
                    script,
                    "preview-flux-anti-aeon",
                    units.Count,
                    "evento auxiliar (anti-aeon)",
                    "branch contra aeon usa o proprio alvo encontrado pelo script e executa Banish direto.",
                    "cena B associada ao pacote anti-aeon.",
                    "preview acoplada host/companheiro",
                    banishActions,
                    new[]
                    {
                        BuildPreviewNamedWrite(script, "priv0004", "alvo anti-aeon", "resultado de findMatchingChr", banishActions[0].CallOffset),
                    },
                    new[]
                    {
                        "o Seymour ainda bane aeons aqui; a UI passa a mostrar o bundle como pacote contextual e nao como rota editavel.",
                    }));
            }

            return units.Count > 0;
        }

        static bool TryBuildMortiorchisPreviewUnits(
            AiScriptFile script,
            IReadOnlyList<AiDetectedBranchAction> branches,
            out List<AiIndirectDispatchUnit> units)
        {
            units = new List<AiIndirectDispatchUnit>();
            List<AiDetectedBranchAction> bodyActions = FilterDistinctBranchActions(branches, 0x608C);
            List<AiDetectedBranchAction> absorptionActions = FilterDistinctBranchActions(branches, 0x60A9);
            bool hasUsedCommandGate = HasLiteralCommandSite(script, 0x3006) && HasLiteralCommandSite(script, 0x3007) && HasPopVarWithLiteral(script, "battleVar0020", 255);
            if (bodyActions.Count == 0 && absorptionActions.Count == 0)
                return false;

            if (bodyActions.Count > 0)
            {
                units.Add(BuildPreviewActionBundleUnit(
                    script,
                    "preview-mortiorchis-body-handoff",
                    units.Count,
                    "evento auxiliar (companheiro)",
                    "o Mortiorchis sincroniza CurrentTurnDelay e reapresenta o corpo com pacote direto.",
                    "CurrentTurnDelay copiado do host antes do handoff visual.",
                    "preview companheiro acoplado",
                    bodyActions,
                    new[]
                    {
                        BuildPreviewPseudoWrite(units.Count, 0, "delay compartilhado", "CurrentTurnDelay <- host", bodyActions[0].CallOffset),
                    },
                    new[]
                    {
                        "0x608C continua tratado como pacote opaco de cena/acao do companheiro.",
                    }));
            }

            if (absorptionActions.Count > 0)
            {
                units.Add(BuildPreviewActionBundleUnit(
                    script,
                    "preview-mortiorchis-absorption",
                    units.Count,
                    "evento auxiliar (companheiro)",
                    hasUsedCommandGate
                        ? "usedCommand 3006/3007 prepara Mortibsorption e marca battleVar0020 = 255."
                        : "companheiro decide entre performCommand e forcePerformCommand para Mortibsorption.",
                    hasUsedCommandGate
                        ? "battleVar0020 <- 255 antes do pacote de corpo."
                        : "pacote contextual do companheiro sem next-state authoring exposto.",
                    "preview companheiro acoplado",
                    absorptionActions,
                    new[]
                    {
                        BuildPreviewNamedWrite(script, "battleVar0020", "gate de reacao", hasUsedCommandGate ? "255" : "gate contextual do pacote", absorptionActions[0].CallOffset),
                    },
                    new[]
                    {
                        "Mortibsorption existe em performCommand e forcePerformCommand; o V2 mostra ambos sem tentar fundir tudo em writer arbitrario.",
                    }));
            }

            return units.Count > 0;
        }

        static bool TryBuildMortibodyPreviewUnits(
            AiScriptFile script,
            IReadOnlyList<AiDetectedBranchAction> branches,
            out List<AiIndirectDispatchUnit> units)
        {
            units = new List<AiIndirectDispatchUnit>();
            List<AiDetectedBranchAction> directSupport = FilterDistinctBranchActions(branches, 0x605E, 0x6076, 0x302C, 0x6039, 0x603A, 0x603B, 0x603C);
            List<AiDetectedBranchAction> absorptionActions = FilterDistinctBranchActions(branches, 0x60A9);
            int statusReads = CountNearbyStatusReads(script, "Shell", "Haste", "Nul");
            if (directSupport.Count == 0 || statusReads < 6)
                return false;

            units.Add(BuildPreviewActionBundleUnit(
                script,
                "preview-mortibody-support-switch",
                units.Count,
                "onTurn real (suporte por acumulador)",
                "priv0010/priv0014/priv0018/priv001C acumulam leitura de Shell/Haste/Nul* antes do switch principal.",
                "battleVar001C governa o pacote direto de suporte/ofensiva.",
                "preview suporte por acumulador",
                directSupport,
                new[]
                {
                    BuildPreviewNamedWrite(script, "priv0010", "score party #1", "acumulador de status/cleanse", directSupport[0].CallOffset),
                    BuildPreviewNamedWrite(script, "priv0014", "score party #2", "acumulador de status/cleanse", directSupport[0].CallOffset),
                    BuildPreviewNamedWrite(script, "priv0018", "score party #3", "acumulador de status/cleanse", directSupport[0].CallOffset),
                    BuildPreviewNamedWrite(script, "priv001C", "score party #4", "acumulador de status/cleanse", directSupport[0].CallOffset),
                    BuildPreviewNamedWrite(script, "battleVar001C", "selector do switch", "decide entre suporte elemental, Cura, Shattering Claw ou Desperado", directSupport[0].CallOffset),
                },
                new[]
                {
                    "o Mortibody nao e uma simples tabela de rota: ele pontua status da party e so depois dispara o pacote direto.",
                }));

            if (absorptionActions.Count > 0)
            {
                units.Add(BuildPreviewActionBundleUnit(
                    script,
                    "preview-mortibody-absorption",
                    units.Count,
                    "evento auxiliar (companheiro)",
                    "pacote de Mortibsorption vive como follow-up separado do switch principal.",
                    "perform/forcePerform contextual para Mortibsorption.",
                    "preview suporte por acumulador",
                    absorptionActions,
                    new[]
                    {
                        BuildPreviewPseudoWrite(units.Count, 1, "follow-up", "Mortibsorption perform/forcePerform", absorptionActions[0].CallOffset),
                    },
                    new[]
                    {
                        "o V2 isola o cleanup/follow-up do Mortibody em preview read-only para nao prometer row-only onde nao ha slot real.",
                    }));
            }

            return units.Count > 0;
        }

        static List<ReactiveSensorPattern> DetectReactiveSensorPatterns(AiScriptFile script)
        {
            IReadOnlyList<AiInstruction> instructions = script.Instructions;
            var patterns = new List<ReactiveSensorPattern>();
            for (int i = 0; i + 5 < instructions.Count; i++)
            {
                if (instructions[i].Opcode != CALL || instructions[i].Operand != UsedCommand)
                    continue;
                if (instructions[i + 1].Opcode != 0xA0
                    || instructions[i + 2].Opcode != PUSHV
                    || instructions[i + 2].Operand != instructions[i + 1].Operand
                    || instructions[i + 3].Opcode != PUSHII
                    || instructions[i + 4].Opcode != CALL
                    || instructions[i + 4].Operand != ReadMoveProperty
                    || instructions[i + 5].Opcode != 0xA0)
                {
                    continue;
                }

                ushort usedCommandVar = instructions[i + 1].Operand;
                ushort propertyId = instructions[i + 3].Operand;
                ushort propertyVar = instructions[i + 5].Operand;
                string propertyName = AiMovePropertyNames.Get(propertyId) ?? $"moveProperty 0x{propertyId:X4}";
                int sceneStateOffset = -1;
                ushort sceneStateVar = 0xFFFF;
                ushort? sceneStateValue = null;
                int sceneCallOffset = -1;
                ushort sceneCallOperand = 0;
                ushort stateVar = 0xFFFF;
                ushort orLiteral = 0;
                int orWriteOffset = -1;

                for (int j = i + 6; j + 3 < instructions.Count && j <= i + 32; j++)
                {
                    if (sceneStateValue == null
                        && instructions[j].Opcode == PUSHII
                        && instructions[j + 1].Opcode == 0xA0)
                    {
                        sceneStateOffset = instructions[j].Offset;
                        sceneStateVar = instructions[j + 1].Operand;
                        sceneStateValue = instructions[j].Operand;
                    }

                    if (instructions[j].Opcode == PUSHV
                        && instructions[j + 1].Opcode == PUSHII
                        && instructions[j + 2].Opcode == 0x03
                        && instructions[j + 3].Opcode == 0xA0
                        && instructions[j + 3].Operand == instructions[j].Operand)
                    {
                        stateVar = instructions[j].Operand;
                        orLiteral = instructions[j + 1].Operand;
                        orWriteOffset = instructions[j + 3].Offset;
                    }

                    if (instructions[j].Opcode == CALLPOPA
                        && (instructions[j].Operand == RunBtlSceneA || instructions[j].Operand == RunBtlSceneB))
                    {
                        sceneCallOffset = instructions[j].Offset;
                        sceneCallOperand = instructions[j].Operand;
                    }
                }

                bool usesSceneStateOnly = false;
                if (stateVar == 0xFFFF || orWriteOffset < 0)
                {
                    if (sceneStateVar == 0xFFFF || !sceneStateValue.HasValue || sceneStateOffset < 0 || sceneCallOffset < 0)
                        continue;

                    stateVar = sceneStateVar;
                    orLiteral = sceneStateValue.Value;
                    orWriteOffset = sceneStateOffset;
                    usesSceneStateOnly = true;
                }

                patterns.Add(new ReactiveSensorPattern(
                    instructions[i].Offset,
                    usedCommandVar,
                    propertyVar,
                    propertyName,
                    instructions[i + 5].Offset,
                    stateVar,
                    orLiteral,
                    orWriteOffset,
                    sceneStateVar,
                    sceneStateValue,
                    sceneStateOffset,
                    sceneCallOffset,
                    sceneCallOperand,
                    usesSceneStateOnly));
            }

            return patterns
                .OrderBy(pattern => pattern.UsedCommandOffset)
                .ToList();
        }

        static List<AiDetectedBranchAction> FilterDistinctBranchActions(
            IReadOnlyList<AiDetectedBranchAction> branches,
            params ushort[] commandIds)
        {
            return branches
                .Where(action => BranchMatchesAnyCommand(action, commandIds))
                .GroupBy(action => action.CallOffset)
                .Select(group => group.First())
                .OrderBy(action => action.CallOffset)
                .ToList();
        }

        static bool BranchMatchesAnyCommand(AiDetectedBranchAction action, params ushort[] commandIds) =>
            commandIds.Any(commandId => action.CommandSummary.Contains($"0x{commandId:X4}", StringComparison.OrdinalIgnoreCase));

        static int CountSelfPropertyWrites(AiScriptFile script, params string[] nameSnippets)
        {
            IReadOnlyList<AiInstruction> instructions = script.Instructions;
            int count = 0;
            for (int i = 2; i < instructions.Count; i++)
            {
                if (instructions[i].Opcode != CALLPOPA || instructions[i].Operand != WriteChrProperty)
                    continue;
                if (instructions[i - 2].Opcode != PUSHII || instructions[i - 1].Opcode != PUSHII)
                {
                    continue;
                }

                string fieldName = AiChrPropertyNames.Get(instructions[i - 2].Operand) ?? $"field 0x{instructions[i - 2].Operand:X4}";
                if (nameSnippets.Any(snippet => fieldName.Contains(snippet, StringComparison.OrdinalIgnoreCase)))
                    count++;
            }

            return count;
        }

        static int CountNearbyStatusReads(AiScriptFile script, params string[] nameSnippets)
        {
            int count = 0;
            foreach (AiInstruction instruction in script.Instructions)
            {
                if (instruction.Opcode != CALL || instruction.Operand != ReadChrProperty)
                    continue;
                count++;
            }

            return count;
        }

        static bool HasCallOperand(AiScriptFile script, byte opcode, ushort operand) =>
            script.Instructions.Any(instruction => instruction.Opcode == opcode && instruction.Operand == operand);

        static bool HasLiteralCommandSite(AiScriptFile script, ushort commandId) =>
            script.Instructions.Any(instruction => instruction.Opcode == PUSHII && instruction.Operand == commandId);

        static bool HasLiteralDispatchCommand(AiScriptFile script, ushort callOperand, ushort commandId)
        {
            IReadOnlyList<AiInstruction> instructions = script.Instructions;
            for (int i = 1; i < instructions.Count; i++)
            {
                if (instructions[i].Opcode == CALLPOPA
                    && instructions[i].Operand == callOperand
                    && instructions[i - 1].Opcode == PUSHII
                    && instructions[i - 1].Operand == commandId)
                {
                    return true;
                }
            }

            return false;
        }

        static int CountChrPropertyWrites(AiScriptFile script, ushort fieldId)
        {
            int count = 0;
            IReadOnlyList<AiInstruction> instructions = script.Instructions;
            for (int i = 2; i < instructions.Count; i++)
            {
                if (instructions[i].Opcode == CALLPOPA
                    && instructions[i].Operand == WriteChrProperty
                    && instructions[i - 2].Opcode == PUSHII
                    && instructions[i - 2].Operand == fieldId)
                {
                    count++;
                }
            }

            return count;
        }

        static int FindFirstCallOffset(AiScriptFile script, byte opcode, ushort operand)
        {
            foreach (AiInstruction instruction in script.Instructions)
            {
                if (instruction.Opcode == opcode && instruction.Operand == operand)
                    return instruction.Offset;
            }

            return -1;
        }

        static int FindFirstChrPropertyWriteOffset(AiScriptFile script, ushort fieldId)
        {
            IReadOnlyList<AiInstruction> instructions = script.Instructions;
            for (int i = 2; i < instructions.Count; i++)
            {
                if (instructions[i].Opcode == CALLPOPA
                    && instructions[i].Operand == WriteChrProperty
                    && instructions[i - 2].Opcode == PUSHII
                    && instructions[i - 2].Operand == fieldId)
                {
                    return instructions[i].Offset;
                }
            }

            return -1;
        }

        static bool HasPopVarWithLiteral(AiScriptFile script, string variableName, ushort literal)
        {
            if (!TryFindVariableIndexByName(script, variableName, out ushort variableIndex))
                return false;

            IReadOnlyList<AiInstruction> instructions = script.Instructions;
            for (int i = 0; i + 1 < instructions.Count; i++)
            {
                if (instructions[i].Opcode == PUSHII
                    && instructions[i].Operand == literal
                    && instructions[i + 1].Opcode == 0xA0
                    && instructions[i + 1].Operand == variableIndex)
                {
                    return true;
                }
            }

            return false;
        }

        static AiIndirectDispatchUnit BuildPreviewActionBundleUnit(
            AiScriptFile script,
            string unitId,
            int unitIndex,
            string hookKind,
            string guardSummary,
            string nextStateSummary,
            string capabilityLabel,
            IReadOnlyList<AiDetectedBranchAction> actions,
            IReadOnlyList<AiIndirectDispatchWrite> realWrites,
            IReadOnlyList<string> extraNotes)
        {
            var payloadWrites = new List<AiIndirectDispatchWrite>();
            if (realWrites != null)
                payloadWrites.AddRange(realWrites.Where(write => write.Offset >= 0));

            var targetSlots = new List<AiIndirectDispatchEditableTargetSlot>();
            var consumers = new List<AiIndirectDispatchConsumer>();
            int slotIndex = 0;
            foreach (AiDetectedBranchAction action in actions.OrderBy(candidate => candidate.CallOffset))
            {
                ushort commandVarIndex = PreviewCommandVariableIndex(unitIndex, slotIndex);
                string commandVarName = $"preview.cmd{slotIndex + 1}";
                payloadWrites.Add(new AiIndirectDispatchWrite(
                    commandVarIndex,
                    commandVarName,
                    $"payload #{slotIndex + 1}",
                    action.CommandSummary,
                    action.CallOffset));

                ushort targetVarIndex = PreviewTargetVariableIndex(unitIndex, slotIndex);
                string targetVarName = $"preview.target{slotIndex + 1}";
                targetSlots.Add(BuildPreviewTargetSlot(
                    unitIndex,
                    slotIndex,
                    $"alvo #{slotIndex + 1}",
                    action.TargetSummary,
                    BuildPreviewTargetDetail(action),
                    action.CallOffset,
                    InferPreviewTargetKind(action),
                    targetVarIndex,
                    targetVarName));

                consumers.Add(new AiIndirectDispatchConsumer(
                    $"cast direto #{slotIndex + 1}",
                    commandVarIndex,
                    commandVarName,
                    targetVarIndex,
                    targetVarName,
                    action.CallOffset));
                slotIndex++;
            }

            List<string> notes = new();
            if (extraNotes != null)
                notes.AddRange(extraNotes);
            notes.AddRange(actions.SelectMany(action => action.HighLevelHints));
            notes.AddRange(DescribeAftermathNotes(script, actions.Select(action => action.CallOffset)));

            string warningSummary =
                "Preview read-only: pacote complexo direto/reativo. O V2 mapeia payload, alvo e follow-up sem fingir row-only authoring.";

            return new AiIndirectDispatchUnit(
                unitId,
                unitIndex,
                hookKind,
                guardSummary,
                nextStateSummary,
                AiIndirectDispatchCapabilityTier.PreviewReadOnly,
                capabilityLabel,
                payloadWrites,
                consumers,
                notes
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                BuildOffsetSummary(actions.Select(action => action.CallOffset)),
                warningSummary,
                Array.Empty<AiIndirectDispatchEditableSlot>(),
                targetSlots,
                null,
                null);
        }

        static AiIndirectDispatchWrite BuildPreviewNamedWrite(
            AiScriptFile script,
            string variableName,
            string roleSummary,
            string valueSummary,
            int offset)
        {
            if (TryFindVariableIndexByName(script, variableName, out ushort index))
                return new AiIndirectDispatchWrite(index, VarName(script, index), roleSummary, valueSummary, offset);

            return new AiIndirectDispatchWrite(
                PreviewNamedVariableIndex(variableName),
                variableName,
                roleSummary,
                valueSummary,
                offset);
        }

        static AiIndirectDispatchWrite BuildPreviewPseudoWrite(
            int unitIndex,
            int slotIndex,
            string roleSummary,
            string valueSummary,
            int offset) =>
            new(
                PreviewPseudoVariableIndex(0xC000, unitIndex, slotIndex),
                $"preview.note{slotIndex + 1}",
                roleSummary,
                valueSummary,
                offset);

    }
}

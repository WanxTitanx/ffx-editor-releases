using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai
{
    public sealed record AiIndirectDispatchRouteCloneRequest(
        string SourceUnitId,
        bool ChainSourceToClone = true);

    public sealed record AiIndirectDispatchRouteCloneResult(
        string SourceUnitId,
        string NewUnitId,
        int NewRouteIndex,
        byte[] EditedAiFileBytes,
        IReadOnlyList<AiIndirectDispatchByteChange> ChangedBytes,
        string Summary);

    public static partial class AiAutomation
    {
        const byte EqOpcode = 0x06;
        const byte CaseOpcode = 0x29;
        const byte JumpOpcode = 0xB0;
        const byte PopConditionalJumpOpcode = 0xD6;
        const byte PushLiteralOpcode = 0xAE;

        public static bool TryCloneAndChainIndirectDispatchRoute(
            AiScriptFile script,
            AiIndirectDispatchRouteCloneRequest request,
            out AiIndirectDispatchRouteCloneResult? result,
            out string error)
        {
            result = null;
            error = string.Empty;

            ArgumentNullException.ThrowIfNull(script);
            ArgumentNullException.ThrowIfNull(request);

            if (!script.HasScript || script.Instructions.Count == 0)
            {
                error = "AiFile invalido para clonagem estrutural de rota.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(request.SourceUnitId))
            {
                error = "Structural cloning requires a source unitId.";
                return false;
            }

            if (!TryBuildStrictSeymourRouteClonePlan(script, request, out IndirectDispatchRouteClonePlan? plan, out error)
                && !TryBuildGenericRouteClonePlan(script, request, out plan, out error))
            {
                return false;
            }

            if (plan == null)
            {
                error = "Structural cloning failed to build a valid plan.";
                return false;
            }

            if (!TryBuildClonedRouteAiFile(script, plan, out byte[] editedAiFile, out List<AiIndirectDispatchByteChange> changedBytes, out error))
                return false;

            result = new AiIndirectDispatchRouteCloneResult(
                plan.SourceUnitId,
                plan.NewUnitId,
                plan.NewRouteIndex,
                editedAiFile,
                changedBytes,
                plan.Summary);
            return true;
        }

        static bool TryBuildStrictSeymourRouteClonePlan(
            AiScriptFile script,
            AiIndirectDispatchRouteCloneRequest request,
            out IndirectDispatchRouteClonePlan? plan,
            out string error)
        {
            plan = null;
            error = string.Empty;

            if (!TryFindVariableIndexByName(script, "priv0020", out ushort phaseVar)
                || !TryFindVariableIndexByName(script, "priv0024", out ushort normalCmdVar)
                || !TryFindVariableIndexByName(script, "priv0028", out ushort aeonCmdVar)
                || !TryFindVariableIndexByName(script, "priv002C", out ushort multiCmdVar)
                || !TryFindVariableIndexByName(script, "priv0030", out ushort pairCmdVar))
            {
                error = "Strict Seymour shape not found for structural cloning.";
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

            if (rows.Count < 2)
            {
                error = "Not enough routes in the Seymour shape to clone.";
                return false;
            }

            StrictSeymourDispatchRow? sourceRow = rows.FirstOrDefault(row =>
                string.Equals($"dispatch-row-{row.PhaseIndex}", request.SourceUnitId, StringComparison.OrdinalIgnoreCase));
            if (sourceRow == null)
            {
                error = "The requested route does not belong to the detected strict Seymour shape.";
                return false;
            }

            if (!TryBuildRouteClonePlan(
                    script,
                    request,
                    sourceUnitId: request.SourceUnitId,
                    sourceUnitIndex: sourceRow.PhaseIndex,
                    sourceNextStateOffset: sourceRow.NextStateOffset,
                    sourceOldNextState: sourceRow.NextState,
                    sourceBlockStartOffset: sourceRow.StartOffset,
                    sourceBlockEndOffset: sourceRow.EndOffset,
                    allRouteStartOffsets: rows.Select(row => row.StartOffset).ToArray(),
                    newUnitIdFactory: _ => $"dispatch-row-{rows.Count}",
                    summaryFactory: newRouteIndex =>
                        $"Rota {sourceRow.PhaseIndex} clonada e encadeada para rota {newRouteIndex} (shape Seymour estrito).",
                    out plan,
                    out error))
            {
                return false;
            }

            return true;
        }

        static bool TryBuildGenericRouteClonePlan(
            AiScriptFile script,
            AiIndirectDispatchRouteCloneRequest request,
            out IndirectDispatchRouteClonePlan? plan,
            out string error)
        {
            plan = null;
            error = string.Empty;

            List<GenericIndirectDispatchConsumer> consumers = FindGenericIndirectDispatchConsumers(script);
            if (consumers.Count < 2)
            {
                error = "shape generico sem consumers suficientes para clonagem estrutural.";
                return false;
            }

            List<(ushort CommandVariableIndex, ushort TargetVariableIndex)> slotPairs = consumers
                .OrderBy(consumer => consumer.CallOffset)
                .Select(consumer => (consumer.CommandVariableIndex, consumer.TargetVariableIndex))
                .Distinct()
                .ToList();
            List<ushort> commandVars = slotPairs.Select(pair => pair.CommandVariableIndex).Distinct().ToList();
            List<ushort> targetVars = slotPairs.Select(pair => pair.TargetVariableIndex).Distinct().ToList();
            List<GenericIndirectDispatchRouteCluster> clusters = FindGenericRouteClusters(script, commandVars, targetVars);
            if (clusters.Count < 2)
            {
                error = "shape generico sem route clusters suficientes para clonagem.";
                return false;
            }

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
            {
                error = "familia generica de dispatch nao encontrada para clonagem estrutural.";
                return false;
            }

            ushort phaseVar = family[0].NextStateVariableIndex;
            List<AiIndirectDispatchUnit> detectedUnits = new();
            if (!TryBuildGenericSwitchDispatchUnits(script, out detectedUnits))
            {
                error = "The generic read fell outside before cloning.";
                return false;
            }

            AiIndirectDispatchUnit? sourceUnit = detectedUnits.FirstOrDefault(unit =>
                unit.UnitId.Equals(request.SourceUnitId, StringComparison.OrdinalIgnoreCase));
            if (sourceUnit == null)
            {
                error = "The requested route does not belong to the detected generic family.";
                return false;
            }

            if (sourceUnit.UnitIndex < 0 || sourceUnit.UnitIndex >= family.Length)
            {
                error = "The generic route index went outside the range of the detected family.";
                return false;
            }

            GenericIndirectDispatchRouteCluster sourceRoute = family[sourceUnit.UnitIndex];
            if (!TryBuildRouteClonePlan(
                    script,
                    request,
                    sourceUnitId: request.SourceUnitId,
                    sourceUnitIndex: sourceUnit.UnitIndex,
                    sourceNextStateOffset: sourceRoute.NextStateOffset,
                    sourceOldNextState: sourceRoute.NextState,
                    sourceBlockStartOffset: sourceRoute.StartOffset,
                    sourceBlockEndOffset: sourceRoute.EndOffset,
                    allRouteStartOffsets: family.Select(route => route.StartOffset).ToArray(),
                    newUnitIdFactory: newRouteIndex => $"dispatch-generic-{phaseVar:X4}-{newRouteIndex}",
                    summaryFactory: newRouteIndex =>
                        $"Rota {sourceUnit.UnitIndex} clonada e encadeada para rota {newRouteIndex} (shape estrutural generico).",
                    out plan,
                    out error))
            {
                return false;
            }

            return true;
        }

        static bool TryBuildRouteClonePlan(
            AiScriptFile script,
            AiIndirectDispatchRouteCloneRequest request,
            string sourceUnitId,
            int sourceUnitIndex,
            int sourceNextStateOffset,
            ushort sourceOldNextState,
            int sourceBlockStartOffset,
            int sourceBlockEndOffset,
            IReadOnlyList<int> allRouteStartOffsets,
            Func<int, string> newUnitIdFactory,
            Func<int, string> summaryFactory,
            out IndirectDispatchRouteClonePlan? plan,
            out string error)
        {
            plan = null;
            error = string.Empty;

            IReadOnlyList<AiInstruction> instructions = script.Instructions;
            int sourceStartIndex = FindInstructionIndexByOffset(instructions, sourceBlockStartOffset);
            int sourceEndIndex = FindInstructionIndexByOffset(instructions, sourceBlockEndOffset);
            if (sourceStartIndex < 0 || sourceEndIndex < 0 || sourceEndIndex + 1 >= instructions.Count)
            {
                error = "Could not locate the raw route block in the script.";
                return false;
            }

            AiInstruction trailerJump = instructions[sourceEndIndex + 1];
            if (trailerJump.Opcode != JumpOpcode)
            {
                error = "The route does not end with the expected JMP to return to the common section.";
                return false;
            }

            if (!TryResolveRouteSwitchCaseContext(
                    script,
                    allRouteStartOffsets,
                    sourceBlockEndOffset,
                    out int workerIndex,
                    out int caseInsertionIndex,
                    out int newRouteIndex,
                    out int oldJumpCount))
            {
                error = "Could not close the structural context of this family's switch/case.";
                return false;
            }

            int sourceNextStateInstructionIndex = FindInstructionIndexByOffset(instructions, sourceNextStateOffset);
            if (sourceNextStateInstructionIndex < 0)
            {
                error = "The next-state PUSHII was not found again.";
                return false;
            }

            var clonedBlockInstructions = new List<AiInstruction>();
            for (int i = sourceStartIndex; i <= sourceEndIndex + 1; i++)
                clonedBlockInstructions.Add(CloneInstructionForInsert(instructions[i]));

            plan = new IndirectDispatchRouteClonePlan(
                SourceUnitId: sourceUnitId,
                SourceUnitIndex: sourceUnitIndex,
                NewUnitId: newUnitIdFactory(newRouteIndex),
                NewRouteIndex: newRouteIndex,
                WorkerIndex: workerIndex,
                CaseInsertionIndex: caseInsertionIndex,
                SourceNextStateInstructionIndex: sourceNextStateInstructionIndex,
                SourceOldNextState: sourceOldNextState,
                SourceNewNextState: request.ChainSourceToClone ? (ushort)newRouteIndex : sourceOldNextState,
                NewJumpSlotIndex: oldJumpCount,
                ClonedBlockInstructions: clonedBlockInstructions,
                Summary: summaryFactory(newRouteIndex));
            return true;
        }

        static bool TryBuildClonedRouteAiFile(
            AiScriptFile script,
            IndirectDispatchRouteClonePlan plan,
            out byte[] editedAiFile,
            out List<AiIndirectDispatchByteChange> changedBytes,
            out string error)
        {
            editedAiFile = Array.Empty<byte>();
            changedBytes = new List<AiIndirectDispatchByteChange>();
            error = string.Empty;

            IReadOnlyList<AiInstruction> instructions = script.Instructions;
            var newInstructions = new List<AiInstruction>(instructions.Count + 4 + plan.ClonedBlockInstructions.Count);
            IReadOnlyList<AiInstruction> newCaseInstructions = BuildNewCaseInstructions(plan.NewRouteIndex, plan.NewJumpSlotIndex);

            for (int i = 0; i < instructions.Count; i++)
            {
                if (i == plan.CaseInsertionIndex)
                    newInstructions.AddRange(newCaseInstructions);

                AiInstruction current = instructions[i];
                if (i == plan.SourceNextStateInstructionIndex)
                {
                    newInstructions.Add(new AiInstruction
                    {
                        Offset = current.Offset,
                        Opcode = current.Opcode,
                        HasOperand = current.HasOperand,
                        Operand = plan.SourceNewNextState,
                        OperandKind = current.OperandKind,
                    });
                    continue;
                }

                newInstructions.Add(current);
            }

            newInstructions.AddRange(plan.ClonedBlockInstructions);

            byte[] rebuilt = AiScript_File.Rebuild(script, newInstructions);
            AiScriptFile rebuiltScript = AiScript_File.Read(rebuilt);
            int clonedBlockLength = plan.ClonedBlockInstructions.Sum(instruction => instruction.Length);
            int cloneStartRelative = rebuiltScript.CodeLength - clonedBlockLength;

            editedAiFile = AiScript_File.GrowWorkerJumpTable(rebuiltScript, plan.WorkerIndex, new[] { cloneStartRelative });
            changedBytes = BuildByteChanges(script.OriginalAiFileBytes, editedAiFile);
            return true;
        }

        static IReadOnlyList<AiInstruction> BuildNewCaseInstructions(int newRouteIndex, int jumpSlotIndex)
        {
            return new AiInstruction[]
            {
                NewInsertedInstruction(PushLiteralOpcode, (ushort)newRouteIndex, AiOperandKind.Immediate),
                NewInsertedInstruction(CaseOpcode, 0, AiOperandKind.None),
                NewInsertedInstruction(EqOpcode, 0, AiOperandKind.None),
                NewInsertedInstruction(PopConditionalJumpOpcode, (ushort)jumpSlotIndex, AiOperandKind.JumpIndex),
            };
        }

        static bool TryResolveRouteSwitchCaseContext(
            AiScriptFile script,
            IReadOnlyList<int> routeStartOffsets,
            int sourceBlockEndOffset,
            out int workerIndex,
            out int caseInsertionIndex,
            out int newRouteIndex,
            out int oldJumpCount)
        {
            workerIndex = -1;
            caseInsertionIndex = -1;
            newRouteIndex = -1;
            oldJumpCount = 0;

            IReadOnlyList<AiInstruction> instructions = script.Instructions;
            int sourceEndIndex = FindInstructionIndexByOffset(instructions, sourceBlockEndOffset);
            if (sourceEndIndex < 0 || sourceEndIndex + 1 >= instructions.Count)
                return false;

            AiInstruction exitJump = instructions[sourceEndIndex + 1];
            if (exitJump.Opcode != JumpOpcode)
                return false;

            int[] routeStartRelative = routeStartOffsets
                .Select(offset => offset - script.ScriptStart)
                .ToArray();
            var routeStartSet = routeStartRelative.ToHashSet();

            AiWorker? owner = script.Workers.FirstOrDefault(worker =>
                exitJump.Operand < worker.JumpTargets.Count
                && routeStartRelative.All(target => worker.JumpTargets.Contains(target)));
            if (owner == null)
                return false;

            workerIndex = owner.Index;
            oldJumpCount = owner.JumpTargets.Count;
            int exitTargetRelative = owner.JumpTargets[exitJump.Operand];
            int exitTargetAbsolute = script.ScriptStart + exitTargetRelative;

            var matchedCases = new List<(int Index, int Literal, int TargetRelative)>();
            for (int i = 3; i < instructions.Count; i++)
            {
                AiInstruction jump = instructions[i];
                if (jump.Opcode != PopConditionalJumpOpcode || jump.Operand >= owner.JumpTargets.Count)
                    continue;
                if (instructions[i - 1].Opcode != EqOpcode
                    || instructions[i - 2].Opcode != CaseOpcode
                    || instructions[i - 3].Opcode != PushLiteralOpcode)
                {
                    continue;
                }

                int targetRelative = owner.JumpTargets[jump.Operand];
                if (!routeStartSet.Contains(targetRelative))
                    continue;
                if (jump.Offset >= exitTargetAbsolute)
                    continue;

                matchedCases.Add((i, instructions[i - 3].Operand, targetRelative));
            }

            List<(int Index, int Literal, int TargetRelative)> tailCases = matchedCases
                .TakeLast(routeStartSet.Count)
                .ToList();
            if (tailCases.Count != routeStartSet.Count || tailCases.Select(caseRow => caseRow.TargetRelative).Distinct().Count() != routeStartSet.Count)
                return false;

            caseInsertionIndex = tailCases.Max(caseRow => caseRow.Index) + 1;
            newRouteIndex = tailCases.Max(caseRow => caseRow.Literal) + 1;
            return true;
        }

        static AiInstruction CloneInstructionForInsert(AiInstruction instruction)
        {
            return new AiInstruction
            {
                Offset = -1,
                Opcode = instruction.Opcode,
                HasOperand = instruction.HasOperand,
                Operand = instruction.Operand,
                OperandKind = instruction.OperandKind,
            };
        }

        static AiInstruction NewInsertedInstruction(byte opcode, ushort operand, AiOperandKind kind)
        {
            bool hasOperand = kind != AiOperandKind.None;
            return new AiInstruction
            {
                Offset = -1,
                Opcode = opcode,
                HasOperand = hasOperand,
                Operand = hasOperand ? operand : (ushort)0,
                OperandKind = kind,
            };
        }

        static int FindInstructionIndexByOffset(IReadOnlyList<AiInstruction> instructions, int offset)
        {
            for (int i = 0; i < instructions.Count; i++)
            {
                if (instructions[i].Offset == offset)
                    return i;
            }

            return -1;
        }

        static List<AiIndirectDispatchByteChange> BuildByteChanges(byte[] before, byte[] after)
        {
            int len = Math.Min(before.Length, after.Length);
            var changes = new List<AiIndirectDispatchByteChange>();
            for (int i = 0; i < len; i++)
            {
                if (before[i] != after[i])
                    changes.Add(new AiIndirectDispatchByteChange(i, before[i], after[i]));
            }

            return changes;
        }

        sealed record IndirectDispatchRouteClonePlan(
            string SourceUnitId,
            int SourceUnitIndex,
            string NewUnitId,
            int NewRouteIndex,
            int WorkerIndex,
            int CaseInsertionIndex,
            int SourceNextStateInstructionIndex,
            ushort SourceOldNextState,
            ushort SourceNewNextState,
            int NewJumpSlotIndex,
            IReadOnlyList<AiInstruction> ClonedBlockInstructions,
            string Summary);
    }
}

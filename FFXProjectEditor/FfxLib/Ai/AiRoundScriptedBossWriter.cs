using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai
{
    public sealed record AiRoundScriptedBossDescriptor(
        string BeatName,
        string RoleLabel,
        ushort CurrentCommand,
        int CommandInstructionOffset,
        int? LandingRepriseOffset,
        ushort? CurrentLandingReprise)
    {
        public string CurrentCommandSummary =>
            AiCommandId.Decode(CurrentCommand).Name + " [0x" + CurrentCommand.ToString("X4") + "]";

        public bool HasLandingReprise =>
            BeatName.Equals("preview-round-landing", StringComparison.OrdinalIgnoreCase)
            && LandingRepriseOffset.HasValue
            && CurrentLandingReprise.HasValue;
    }

    public sealed record AiRoundScriptedBossPatchRequest(
        string BeatName,
        int CommandInstructionOffset,
        ushort OldCommand,
        ushort NewCommand,
        int? LandingRepriseOffset,
        ushort? OldLandingReprise,
        ushort? NewLandingReprise);

    public sealed record AiRoundScriptedBossByteChange(
        int Offset,
        byte OldValue,
        byte NewValue);

    public sealed record AiRoundScriptedBossEditResult(
        AiRoundScriptedBossPatchRequest Request,
        byte[] EditedAiFileBytes,
        IReadOnlyList<AiRoundScriptedBossByteChange> ChangedBytes);

    public static class AiRoundScriptedBossWriter
    {
        const byte PushIiOpcode = 0xAE;
        const byte PopVariableOpcode = 0xA0;
        const byte CallOpcode = 0xB5;
        const byte CallPopAOpcode = 0xD8;
        const ushort ForcePerformCommand = 0x705A;
        const ushort ReadChrPropertyFuncId = 0x700F;

        static readonly ushort[] ValidCommandOperands =
        {
            0x4016, 0x4019, 0x401A, 0x4097, 0x40AB, 0x40DF, 0x6050, 0x608C, 0x60A9,
        };

        public static bool TryBuildDescriptors(
            AiScriptFile script,
            out IReadOnlyList<AiRoundScriptedBossDescriptor> descriptors,
            out string error)
        {
            descriptors = Array.Empty<AiRoundScriptedBossDescriptor>();
            error = string.Empty;

            if (script == null || !script.HasScript)
            {
                error = "AiFile invalido para detectar shape do round-scripted-boss.";
                return false;
            }

            var built = new List<AiRoundScriptedBossDescriptor>();

            if (!TryBuildLandingDescriptor(script, out AiRoundScriptedBossDescriptor? landing, out error)
                || landing == null)
            {
                return false;
            }
            built.Add(landing);

            if (TryBuildSingleBeatDescriptor(script, "preview-round-crawl", 0x401A, "Crawl direto do round opener", out AiRoundScriptedBossDescriptor? crawl, out error)
                && crawl != null)
            {
                built.Add(crawl);
            }

            if (TryBuildSingleBeatDescriptor(script, "preview-round-sonic-boom", 0x4016, "Sonic Boom do round", out AiRoundScriptedBossDescriptor? sonic, out error)
                && sonic != null)
            {
                built.Add(sonic);
            }

            if (TryBuildSingleBeatDescriptor(script, "preview-round-aeon-punish", 0x4097, "Anti-aeon contextual", out AiRoundScriptedBossDescriptor? aeon, out error)
                && aeon != null)
            {
                built.Add(aeon);
            }

            if (TryBuildFinisherDescriptor(script, out AiRoundScriptedBossDescriptor? finisher, out error)
                && finisher != null)
            {
                built.Add(finisher);
            }

            if (built.Count < 2)
            {
                error = "The round-scripted-boss needs at least 2 beats with proven direct command PUSHII.";
                descriptors = Array.Empty<AiRoundScriptedBossDescriptor>();
                return false;
            }

            descriptors = built;
            return true;
        }

        public static bool TryApplyPatch(
            AiScriptFile script,
            AiRoundScriptedBossPatchRequest request,
            out AiRoundScriptedBossEditResult? result,
            out string error)
        {
            result = null;
            error = string.Empty;

            if (script == null || !script.HasScript)
            {
                error = "AiFile invalido para patch do round-scripted-boss.";
                return false;
            }

            if (request == null
                || string.IsNullOrWhiteSpace(request.BeatName)
                || request.CommandInstructionOffset < 0)
            {
                error = "Patch do round-scripted-boss veio sem offset ou beatName.";
                return false;
            }

            if (!ValidCommandOperands.Contains(request.NewCommand))
            {
                error =
                    $"Comando 0x{request.NewCommand:X4} nao esta na lista branca de PUSHII do round-scripted-boss writer.";
                return false;
            }

            if (request.NewCommand == request.OldCommand
                && (!request.LandingRepriseOffset.HasValue
                    || request.OldLandingReprise == request.NewLandingReprise))
            {
                error = "No byte changed in this round-scripted-boss patch.";
                return false;
            }

            var touched = new List<(AiInstruction Instruction, ushort OriginalOperand, int Offset, int OriginalLength)>();

            try
            {
                if (!TryPatchPushIiOperand(
                        script,
                        request.CommandInstructionOffset,
                        request.OldCommand,
                        request.NewCommand,
                        touched,
                        out error))
                {
                    return false;
                }

                if (request.LandingRepriseOffset.HasValue
                    && request.OldLandingReprise.HasValue
                    && request.NewLandingReprise.HasValue)
                {
                    if (!TryPatchPushIiOperand(
                            script,
                            request.LandingRepriseOffset.Value,
                            request.OldLandingReprise.Value,
                            request.NewLandingReprise.Value,
                            touched,
                            out error))
                    {
                        return false;
                    }
                }

                byte[] editedAi = AiScript_File.Write(script);
                List<AiRoundScriptedBossByteChange> changes = BuildByteChanges(script.OriginalAiFileBytes, editedAi);
                HashSet<int> allowedOffsets = BuildAllowedOffsets(request);
                List<AiRoundScriptedBossByteChange> forbidden = changes
                    .Where(change => !allowedOffsets.Contains(change.Offset))
                    .Take(8)
                    .ToList();

                if (forbidden.Count > 0)
                {
                    error =
                        "O patch raw do round-scripted-boss abortou: apareceram bytes fora dos PUSHII autorizados. " +
                        string.Join("; ", forbidden.Select(change =>
                            $"+0x{change.Offset:X4} {change.OldValue:X2}->{change.NewValue:X2}"));
                    return false;
                }

                result = new AiRoundScriptedBossEditResult(request, editedAi, changes);
                return true;
            }
            finally
            {
                if (result == null)
                {
                    foreach ((AiInstruction instruction, ushort originalOperand, int _, int __) in touched)
                        instruction.Operand = originalOperand;
                }
            }
        }

        static bool TryBuildLandingDescriptor(
            AiScriptFile script,
            out AiRoundScriptedBossDescriptor? descriptor,
            out string error)
        {
            descriptor = null;
            error = string.Empty;

            if (!TryFindFirstLiteralCommandPush(script, 0x4019, out int landingOffset, out ushort landingOperand))
            {
                error = "Did not find PUSHII 0x4019 of landing in round-scripted-boss.";
                return false;
            }

            int? repriseOffset = null;
            ushort? repriseValue = null;
            if (TryFindForcePerformLandingReprise(script, 0x4019, out int reprise))
            {
                AiInstruction? repriseInstruction = FindInstructionByOffset(script.Instructions, reprise);
                if (repriseInstruction != null && IsPushIi(repriseInstruction))
                {
                    repriseOffset = reprise;
                    repriseValue = repriseInstruction.Operand;
                }
            }

            descriptor = new AiRoundScriptedBossDescriptor(
                "preview-round-landing",
                "Landing / recovery direto",
                landingOperand,
                landingOffset,
                repriseOffset,
                repriseValue);
            return true;
        }

        static bool TryBuildFinisherDescriptor(
            AiScriptFile script,
            out AiRoundScriptedBossDescriptor? descriptor,
            out string error)
        {
            descriptor = null;
            error = string.Empty;

            if (TryFindFirstLiteralCommandPush(script, 0x40AB, out int finisherAOffset, out ushort finisherAOp)
                && TryFindFirstLiteralCommandPush(script, 0x40DF, out int finisherBOffset, out ushort finisherBOp))
            {
                descriptor = new AiRoundScriptedBossDescriptor(
                    "preview-round-finisher",
                    "Finisher duplo (0x40AB + 0x40DF)",
                    finisherAOp,
                    finisherAOffset,
                    finisherBOffset,
                    finisherBOp);
                return true;
            }

            if (TryFindFirstLiteralCommandPush(script, 0x40AB, out int finisherOffset, out ushort finisherOp))
            {
                descriptor = new AiRoundScriptedBossDescriptor(
                    "preview-round-finisher",
                    "Finisher unico 0x40AB",
                    finisherOp,
                    finisherOffset,
                    null,
                    null);
                return true;
            }

            error = "Did not find PUSHII 0x40AB or 0x40DF of finisher in round-scripted-boss.";
            return false;
        }

        static bool TryBuildSingleBeatDescriptor(
            AiScriptFile script,
            string beatName,
            ushort commandOperand,
            string roleLabel,
            out AiRoundScriptedBossDescriptor? descriptor,
            out string error)
        {
            descriptor = null;
            error = string.Empty;

            if (!TryFindFirstLiteralCommandPush(script, commandOperand, out int offset, out ushort currentOperand))
            {
                error = $"Nao achei PUSHII 0x{commandOperand:X4} para o beat {beatName}.";
                return false;
            }

            descriptor = new AiRoundScriptedBossDescriptor(
                beatName,
                roleLabel,
                currentOperand,
                offset,
                null,
                null);
            return true;
        }

        static bool TryFindFirstLiteralCommandPush(
            AiScriptFile script,
            ushort commandOperand,
            out int pushOffset,
            out ushort pushOperand)
        {
            pushOffset = -1;
            pushOperand = 0;

            IReadOnlyList<AiInstruction> instructions = script.Instructions;
            for (int i = 0; i < instructions.Count; i++)
            {
                AiInstruction instruction = instructions[i];
                if (!IsPushIi(instruction) || instruction.Operand != commandOperand)
                    continue;

                pushOffset = instruction.Offset;
                pushOperand = instruction.Operand;
                return true;
            }

            return false;
        }

        static bool TryFindForcePerformLandingReprise(
            AiScriptFile script,
            ushort commandOperand,
            out int pushOffset)
        {
            pushOffset = -1;

            IReadOnlyList<AiInstruction> instructions = script.Instructions;
            for (int i = 0; i < instructions.Count; i++)
            {
                AiInstruction instruction = instructions[i];
                if (!IsPushIi(instruction) || instruction.Operand != commandOperand)
                    continue;

                if (i + 1 >= instructions.Count)
                    continue;

                AiInstruction next = instructions[i + 1];
                if (next.Opcode != CallOpcode || !next.HasOperand || next.Operand != ForcePerformCommand)
                    continue;

                pushOffset = instruction.Offset;
                return true;
            }

            return false;
        }

        static bool IsFollowedByPerformCall(IReadOnlyList<AiInstruction> instructions, int pushIndex)
        {
            if (pushIndex + 1 >= instructions.Count)
                return false;

            AiInstruction next = instructions[pushIndex + 1];
            return next.Opcode == CallOpcode
                && next.HasOperand
                && (next.Operand == ForcePerformCommand || IsChrPropertyCall(next.Operand));
        }

        static bool IsChrPropertyCall(ushort operand) => operand == ReadChrPropertyFuncId;

        static bool IsPushIi(AiInstruction instruction) =>
            instruction.Opcode == PushIiOpcode && instruction.HasOperand;

        static AiInstruction? FindInstructionByOffset(IReadOnlyList<AiInstruction> instructions, int offset)
        {
            for (int i = 0; i < instructions.Count; i++)
            {
                if (instructions[i].Offset == offset)
                    return instructions[i];
            }

            return null;
        }

        static bool TryPatchPushIiOperand(
            AiScriptFile script,
            int instructionOffset,
            ushort oldValue,
            ushort newValue,
            List<(AiInstruction Instruction, ushort OriginalOperand, int Offset, int OriginalLength)> touched,
            out string error)
        {
            error = string.Empty;

            AiInstruction? instruction = FindInstructionByOffset(script.Instructions, instructionOffset);
            if (instruction == null)
            {
                error = $"Nao achei a instrucao 0x{instructionOffset:X4} no patch do round-scripted-boss.";
                return false;
            }

            if (!IsPushIi(instruction))
            {
                error = $"Offset 0x{instructionOffset:X4} deixou de ser um PUSHII editavel do round-scripted-boss.";
                return false;
            }

            if (instruction.Operand != oldValue)
            {
                error =
                    $"Offset 0x{instructionOffset:X4} mudou desde a leitura " +
                    $"(esperado 0x{oldValue:X4}, atual 0x{instruction.Operand:X4}).";
                return false;
            }

            touched.Add((instruction, instruction.Operand, instructionOffset, instruction.Length));
            instruction.Operand = newValue;
            return true;
        }

        static HashSet<int> BuildAllowedOffsets(AiRoundScriptedBossPatchRequest request)
        {
            var offsets = new HashSet<int>
            {
                request.CommandInstructionOffset + 1,
                request.CommandInstructionOffset + 2,
            };

            if (request.LandingRepriseOffset.HasValue)
            {
                offsets.Add(request.LandingRepriseOffset.Value + 1);
                offsets.Add(request.LandingRepriseOffset.Value + 2);
            }

            return offsets;
        }

        static List<AiRoundScriptedBossByteChange> BuildByteChanges(byte[] before, byte[] after)
        {
            int len = Math.Min(before.Length, after.Length);
            var changes = new List<AiRoundScriptedBossByteChange>();
            for (int i = 0; i < len; i++)
            {
                if (before[i] != after[i])
                    changes.Add(new AiRoundScriptedBossByteChange(i, before[i], after[i]));
            }

            return changes;
        }
    }
}
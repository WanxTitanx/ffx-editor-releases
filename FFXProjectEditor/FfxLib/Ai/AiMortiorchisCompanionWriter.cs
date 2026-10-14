using FFXProjectEditor.Resources;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai
{
    public sealed record AiMortiorchisCompanionDescriptor(
        string BeatName,
        string RoleLabel,
        int CommandInstructionOffset,
        ushort CurrentCommand,
        int? GateWriteOffset,
        ushort? CurrentGateValue)
    {
        public string CurrentCommandSummary =>
            AiCommandId.Decode(CurrentCommand).Name + " [0x" + CurrentCommand.ToString("X4") + "]";
    }

    public sealed record AiMortiorchisCompanionPatchRequest(
        string BeatName,
        int CommandInstructionOffset,
        ushort OldCommand,
        ushort NewCommand,
        int? GateWriteOffset,
        ushort? OldGateValue,
        ushort? NewGateValue);

    public sealed record AiMortiorchisCompanionByteChange(int Offset, byte OldValue, byte NewValue);

    public sealed record AiMortiorchisCompanionEditResult(
        AiMortiorchisCompanionPatchRequest Request,
        byte[] EditedAiFileBytes,
        IReadOnlyList<AiMortiorchisCompanionByteChange> ChangedBytes);

    public static class AiMortiorchisCompanionWriter
    {
        const byte PushIiOpcode = 0xAE;
        const byte CallOpcode = 0xB5;
        const ushort ForcePerformCommand = 0x705A;

        static readonly ushort[] ValidMortiorchisCommands = { 0x608C, 0x60A9 };

        public static bool TryBuildDescriptors(
            AiScriptFile script,
            out IReadOnlyList<AiMortiorchisCompanionDescriptor> descriptors,
            out string error)
        {
            descriptors = Array.Empty<AiMortiorchisCompanionDescriptor>();
            error = string.Empty;

            if (script == null || !script.HasScript)
            {
                error = "AiFile invalido para detectar shape do Mortiorchis.";
                return false;
            }

            var built = new List<AiMortiorchisCompanionDescriptor>();

            foreach (AiInstruction push in AiDirectCommandSites.Enumerate(script, ValidMortiorchisCommands))
            {
                string cmdName = AiCommandId.Decode(push.Operand).Name;
                built.Add(new AiMortiorchisCompanionDescriptor(
                    "mortiorchis-call-" + (push.Offset + 3).ToString("X4"),
                    cmdName + " · 0x" + (push.Offset + 3).ToString("X4"),
                    push.Offset, push.Operand, null, null));
            }

            if (built.Count < 1)
            {
                error = "Mortiorchis requires at least 1 PUSHII of command 0x608C or 0x60A9 proven.";
                descriptors = Array.Empty<AiMortiorchisCompanionDescriptor>();
                return false;
            }

            descriptors = built;
            return true;
        }

        public static bool TryApplyPatch(
            AiScriptFile script,
            AiMortiorchisCompanionPatchRequest request,
            out AiMortiorchisCompanionEditResult? result,
            out string error)
        {
            result = null;
            error = string.Empty;

            if (script == null || !script.HasScript)
            {
                error = "AiFile invalido para patch do Mortiorchis.";
                return false;
            }

            if (request == null || request.CommandInstructionOffset < 0)
            {
                error = "Patch do Mortiorchis veio sem offset.";
                return false;
            }

            if (!TryBuildDescriptors(script, out var liveDescriptors, out error)
                || !liveDescriptors.Any(row => row.BeatName == request.BeatName
                    && row.CommandInstructionOffset == request.CommandInstructionOffset
                    && row.CurrentCommand == request.OldCommand))
            {
                error = Strings.AiAdvancedCommandChanged;
                return false;
            }
            if (request.GateWriteOffset.HasValue || request.OldGateValue.HasValue || request.NewGateValue.HasValue)
            {
                error = Strings.AiAdvancedStateUnsupported;
                return false;
            }

            if (!ValidMortiorchisCommands.Contains(request.NewCommand))
            {
                error = $"Comando 0x{request.NewCommand:X4} nao esta na lista branca do Mortiorchis writer.";
                return false;
            }

            if (request.NewCommand == request.OldCommand)
            {
                error = "No byte changed in this Mortiorchis patch.";
                return false;
            }

            var touched = new List<(AiInstruction Instruction, ushort OriginalOperand)>();
            bool success = false;

            try
            {
                if (!TryPatchPushIiOperand(script, request.CommandInstructionOffset, request.OldCommand, request.NewCommand, touched, out error))
                    return false;

                byte[] editedAi = AiScript_File.Write(script);
                List<AiMortiorchisCompanionByteChange> changed = BuildByteChanges(script.OriginalAiFileBytes, editedAi);
                HashSet<int> allowedOffsets = new()
                {
                    request.CommandInstructionOffset + 1,
                    request.CommandInstructionOffset + 2,
                };
                List<AiMortiorchisCompanionByteChange> forbidden = changed
                    .Where(c => !allowedOffsets.Contains(c.Offset))
                    .Take(8)
                    .ToList();

                if (forbidden.Count > 0)
                {
                    error = "O patch raw do Mortiorchis abortou: apareceram bytes fora dos PUSHII autorizados. " +
                        string.Join("; ", forbidden.Select(c => $"+0x{c.Offset:X4} {c.OldValue:X2}->{c.NewValue:X2}"));
                    return false;
                }

                result = new AiMortiorchisCompanionEditResult(request, editedAi, changed);
                success = true;
                return true;
            }
            finally
            {
                if (!success)
                {
                    foreach ((AiInstruction instruction, ushort originalOperand) in touched)
                        instruction.Operand = originalOperand;
                }
            }
        }

        static bool TryPatchPushIiOperand(
            AiScriptFile script,
            int instructionOffset,
            ushort oldValue,
            ushort newValue,
            List<(AiInstruction Instruction, ushort OriginalOperand)> touched,
            out string error)
        {
            error = string.Empty;
            AiInstruction? instruction = script.Instructions.FirstOrDefault(c => c.Offset == instructionOffset);
            if (instruction == null)
            {
                error = $"Nao achei a instrucao 0x{instructionOffset:X4} no patch do Mortiorchis.";
                return false;
            }
            if (instruction.Opcode != PushIiOpcode || !instruction.HasOperand)
            {
                error = $"Offset 0x{instructionOffset:X4} deixou de ser um PUSHII editavel do Mortiorchis.";
                return false;
            }
            if (instruction.Operand != oldValue)
            {
                error = $"Offset 0x{instructionOffset:X4} mudou desde a leitura (esperado 0x{oldValue:X4}, atual 0x{instruction.Operand:X4}).";
                return false;
            }
            touched.Add((instruction, instruction.Operand));
            instruction.Operand = newValue;
            return true;
        }

        static List<AiMortiorchisCompanionByteChange> BuildByteChanges(byte[] before, byte[] after)
        {
            int len = Math.Min(before.Length, after.Length);
            var changes = new List<AiMortiorchisCompanionByteChange>();
            for (int i = 0; i < len; i++)
            {
                if (before[i] != after[i])
                    changes.Add(new AiMortiorchisCompanionByteChange(i, before[i], after[i]));
            }
            return changes;
        }
    }
}
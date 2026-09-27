using FFXProjectEditor.Resources;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai
{
    public sealed record AiOmnisClusterDescriptor(
        string BeatName,
        string RoleLabel,
        int CommandInstructionOffset,
        ushort CurrentCommand,
        int? StateWriteOffset,
        ushort? CurrentStateValue)
    {
        public string CurrentCommandSummary =>
            AiCommandId.Decode(CurrentCommand).Name + " [0x" + CurrentCommand.ToString("X4") + "]";
    }

    public sealed record AiOmnisClusterPatchRequest(
        string BeatName,
        int CommandInstructionOffset,
        ushort OldCommand,
        ushort NewCommand,
        int? StateWriteOffset,
        ushort? OldStateValue,
        ushort? NewStateValue);

    public sealed record AiOmnisClusterByteChange(int Offset, byte OldValue, byte NewValue);

    public sealed record AiOmnisClusterEditResult(
        AiOmnisClusterPatchRequest Request,
        byte[] EditedAiFileBytes,
        IReadOnlyList<AiOmnisClusterByteChange> ChangedBytes);

    public static class AiOmnisClusterWriter
    {
        const byte PushIiOpcode = 0xAE;
        const byte CallOpcode = 0xB5;
        const ushort ForcePerformCommand = 0x705A;

        static readonly ushort[] ValidOmnisCommands =
        {
            0x303D, 0x3045, 0x3046, 0x3047, 0x3048, 0x3049, 0x304A, 0x304B, 0x304C, 0x60F0,
        };

        public static bool TryBuildDescriptors(
            AiScriptFile script,
            out IReadOnlyList<AiOmnisClusterDescriptor> descriptors,
            out string error)
        {
            descriptors = Array.Empty<AiOmnisClusterDescriptor>();
            error = string.Empty;

            if (script == null || !script.HasScript)
            {
                error = "AiFile invalido para detectar shape do elemental cluster do Omnis.";
                return false;
            }

            var built = new List<AiOmnisClusterDescriptor>();

            foreach (AiInstruction push in AiDirectCommandSites.Enumerate(script, ValidOmnisCommands))
            {
                string cmdName = AiCommandId.Decode(push.Operand).Name;
                built.Add(new AiOmnisClusterDescriptor(
                    "omnis-call-" + (push.Offset + 3).ToString("X4"),
                    cmdName + " · 0x" + (push.Offset + 3).ToString("X4"),
                    push.Offset, push.Operand, null, null));
            }

            if (built.Count < 3)
            {
                error = "The elemental cluster of Omnis requires at least 3 PUSHII of elemental command proven.";
                descriptors = Array.Empty<AiOmnisClusterDescriptor>();
                return false;
            }

            descriptors = built;
            return true;
        }

        public static bool TryApplyPatch(
            AiScriptFile script,
            AiOmnisClusterPatchRequest request,
            out AiOmnisClusterEditResult? result,
            out string error)
        {
            result = null;
            error = string.Empty;

            if (script == null || !script.HasScript)
            {
                error = "AiFile invalido para patch do elemental cluster do Omnis.";
                return false;
            }

            if (request == null || request.CommandInstructionOffset < 0)
            {
                error = "Patch do Omnis veio sem offset ou beatName.";
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
            if (request.StateWriteOffset.HasValue || request.OldStateValue.HasValue || request.NewStateValue.HasValue)
            {
                error = Strings.AiAdvancedStateUnsupported;
                return false;
            }

            if (!ValidOmnisCommands.Contains(request.NewCommand))
            {
                error = $"Comando 0x{request.NewCommand:X4} nao esta na lista branca do Omnis cluster writer.";
                return false;
            }

            if (request.NewCommand == request.OldCommand)
            {
                error = "No byte changed in this Omnis patch.";
                return false;
            }

            var touched = new List<(AiInstruction Instruction, ushort OriginalOperand)>();
            bool success = false;

            try
            {
                if (!TryPatchPushIiOperand(script, request.CommandInstructionOffset, request.OldCommand, request.NewCommand, touched, out error))
                    return false;

                byte[] editedAi = AiScript_File.Write(script);
                List<AiOmnisClusterByteChange> changed = BuildByteChanges(script.OriginalAiFileBytes, editedAi);
                HashSet<int> allowedOffsets = BuildAllowedOffsets(request);
                List<AiOmnisClusterByteChange> forbidden = changed
                    .Where(c => !allowedOffsets.Contains(c.Offset))
                    .Take(8)
                    .ToList();

                if (forbidden.Count > 0)
                {
                    error = "O patch raw do Omnis abortou: apareceram bytes fora dos PUSHII autorizados. " +
                        string.Join("; ", forbidden.Select(c => $"+0x{c.Offset:X4} {c.OldValue:X2}->{c.NewValue:X2}"));
                    return false;
                }

                result = new AiOmnisClusterEditResult(request, editedAi, changed);
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
                error = $"Nao achei a instrucao 0x{instructionOffset:X4} no patch do Omnis.";
                return false;
            }
            if (instruction.Opcode != PushIiOpcode || !instruction.HasOperand)
            {
                error = $"Offset 0x{instructionOffset:X4} deixou de ser um PUSHII editavel do Omnis.";
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

        static HashSet<int> BuildAllowedOffsets(AiOmnisClusterPatchRequest request)
        {
            var offsets = new HashSet<int>
            {
                request.CommandInstructionOffset + 1,
                request.CommandInstructionOffset + 2,
            };
            if (request.StateWriteOffset.HasValue)
            {
                offsets.Add(request.StateWriteOffset.Value + 1);
                offsets.Add(request.StateWriteOffset.Value + 2);
            }
            return offsets;
        }

        static List<AiOmnisClusterByteChange> BuildByteChanges(byte[] before, byte[] after)
        {
            int len = Math.Min(before.Length, after.Length);
            var changes = new List<AiOmnisClusterByteChange>();
            for (int i = 0; i < len; i++)
            {
                if (before[i] != after[i])
                    changes.Add(new AiOmnisClusterByteChange(i, before[i], after[i]));
            }
            return changes;
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai
{
    public enum AiDiffType
    {
        Unchanged,
        Modified,
        Added,
        Removed,
    }

    public sealed record AiDiffEntry(
        int WorkerIndex,
        int Offset,
        AiDiffType Type,
        string? OriginalText,
        string? EditedText);

    public static class AiScript_Diff
    {
        public static IReadOnlyList<AiDiffEntry> Compare(AiScriptFile original, AiScriptFile edited)
        {
            ArgumentNullException.ThrowIfNull(original);
            ArgumentNullException.ThrowIfNull(edited);

            Dictionary<DiffKey, AiInstruction> originalMap = BuildInstructionMap(original);
            Dictionary<DiffKey, AiInstruction> editedMap = BuildInstructionMap(edited);
            List<AiDiffEntry> entries = new();

            foreach (DiffKey key in originalMap.Keys.Union(editedMap.Keys).OrderBy(k => k.WorkerIndex).ThenBy(k => k.Offset))
            {
                bool hasOriginal = originalMap.TryGetValue(key, out AiInstruction? originalInstruction);
                bool hasEdited = editedMap.TryGetValue(key, out AiInstruction? editedInstruction);

                if (hasOriginal && hasEdited)
                {
                    bool same = InstructionsEqual(originalInstruction!, editedInstruction!);
                    entries.Add(new AiDiffEntry(
                        key.WorkerIndex,
                        key.Offset,
                        same ? AiDiffType.Unchanged : AiDiffType.Modified,
                        AiScript_File.Format(originalInstruction!),
                        AiScript_File.Format(editedInstruction!)));
                    continue;
                }

                if (hasOriginal)
                {
                    entries.Add(new AiDiffEntry(
                        key.WorkerIndex,
                        key.Offset,
                        AiDiffType.Removed,
                        AiScript_File.Format(originalInstruction!),
                        null));
                    continue;
                }

                entries.Add(new AiDiffEntry(
                    key.WorkerIndex,
                    key.Offset,
                    AiDiffType.Added,
                    null,
                    AiScript_File.Format(editedInstruction!)));
            }

            return entries;
        }

        private static Dictionary<DiffKey, AiInstruction> BuildInstructionMap(AiScriptFile script)
        {
            Dictionary<int, IReadOnlyList<int>> reachableOwners = AiScript_File.InstructionOwners(script)
                .ToDictionary(pair => pair.Key, pair => pair.Value);

            Dictionary<DiffKey, AiInstruction> map = new();
            foreach (AiInstruction instruction in script.Instructions)
            {
                IReadOnlyList<int> owners = reachableOwners.TryGetValue(instruction.Offset, out IReadOnlyList<int>? exactOwners)
                    ? exactOwners
                    : InferPhysicalOwners(script, instruction.Offset);

                if (owners.Count == 0)
                {
                    owners = new[] { -1 };
                }

                foreach (int owner in owners.Distinct())
                {
                    map[new DiffKey(owner, instruction.Offset)] = instruction;
                }
            }

            return map;
        }

        private static IReadOnlyList<int> InferPhysicalOwners(AiScriptFile script, int offset)
        {
            var ranges = script.Workers
                .Where(worker => worker.Entrypoints.Count > 0)
                .Select(worker => (Start: script.ScriptStart + worker.Entrypoints.Min(), Worker: worker))
                .OrderBy(pair => pair.Start)
                .ToList();

            if (ranges.Count == 0)
            {
                return Array.Empty<int>();
            }

            AiWorker? current = null;
            foreach ((int start, AiWorker worker) in ranges)
            {
                if (offset < start)
                {
                    break;
                }

                current = worker;
            }

            return current == null ? Array.Empty<int>() : new[] { current.Index };
        }

        private static bool InstructionsEqual(AiInstruction left, AiInstruction right)
        {
            byte[] leftBytes = left.Emit();
            byte[] rightBytes = right.Emit();
            return leftBytes.AsSpan().SequenceEqual(rightBytes);
        }

        private readonly record struct DiffKey(int WorkerIndex, int Offset);
    }
}

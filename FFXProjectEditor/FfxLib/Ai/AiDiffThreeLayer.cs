using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai
{
    // P2 three-layer ATEL diff (read-only) — see docs/ai/P2_IR_SEMANTICA_2026-07-31.md (L5, L1 subset).
    //
    // Compute() disassembles both sides through the real codec (AiScript_File.Read), then produces:
    //   • ByteDiff         — raw byte changes over the instruction region (header/data sections excluded),
    //                        lines like "offset: 00->01" ("--" when a side has no byte at that offset);
    //   • DisassemblyDiff  — AiScript_Diff.Compare entries (same WorkerIndex+Offset keying), filtered to
    //                        Modified/Added/Removed, texts via AiScript_File.Format;
    //   • SemanticDiff     — AiSemanticScript before/after compared per (WorkerIndex, Offset): lists nodes
    //                        whose Meaning or Evidence changed (added/removed nodes listed too).
    // The codec and AiScript_Diff are untouched: this file only consumes their public APIs.

    /// <summary>Three-layer diff of an ATEL AiFile. Each layer is a list of human-readable lines;
    /// all three are empty when before == after.</summary>
    public sealed record ThreeLayerDiff(
        IReadOnlyList<string> ByteDiff,
        IReadOnlyList<string> DisassemblyDiff,
        IReadOnlyList<string> SemanticDiff)
    {
        public bool IsEmpty => ByteDiff.Count == 0 && DisassemblyDiff.Count == 0 && SemanticDiff.Count == 0;
    }

    public static class AiDiffThreeLayer
    {
        /// <summary>Compute the three-layer diff between two AiFile blobs.
        /// <paramref name="hasExtraInfo"/> is accepted for signature parity with the Core writer
        /// adapters (AbilityCommandAdapter.DetectExtraInfo) but is ignored: the ATEL AiFile format
        /// has no entry-size variant, so reading is unconditional via AiScript_File.Read.</summary>
        public static ThreeLayerDiff Compute(byte[] beforeBytes, byte[] afterBytes, bool hasExtraInfo)
        {
            ArgumentNullException.ThrowIfNull(beforeBytes);
            ArgumentNullException.ThrowIfNull(afterBytes);
            _ = hasExtraInfo;

            AiScriptFile before = AiScript_File.Read(beforeBytes);
            AiScriptFile after = AiScript_File.Read(afterBytes);

            return new ThreeLayerDiff(
                ComputeByteDiff(beforeBytes, afterBytes, before, after),
                ComputeDisassemblyDiff(before, after),
                ComputeSemanticDiff(before, after));
        }

        /// <summary>Raw byte changes restricted to the union of the two instruction regions
        /// [ScriptStart .. ScriptStart+CodeLength). Lines: "{offset:X}: {before:X2}->{after:X2}"
        /// with "--" when a side has no byte at the offset (region grow/shrink).</summary>
        static IReadOnlyList<string> ComputeByteDiff(
            byte[] beforeBytes, byte[] afterBytes, AiScriptFile before, AiScriptFile after)
        {
            int lo = Math.Min(before.ScriptStart, after.ScriptStart);
            int hi = Math.Max(
                before.ScriptStart + before.CodeLength,
                after.ScriptStart + after.CodeLength);
            hi = Math.Min(hi, Math.Max(beforeBytes.Length, afterBytes.Length));

            var lines = new List<string>();
            for (int i = lo; i < hi; i++)
            {
                string b = i < beforeBytes.Length ? beforeBytes[i].ToString("X2") : "--";
                string a = i < afterBytes.Length ? afterBytes[i].ToString("X2") : "--";
                if (b != a)
                    lines.Add($"{i:X}: {b}->{a}");
            }
            return lines;
        }

        /// <summary>Disassembly layer: AiScript_Diff.Compare over the two codec models, keeping only
        /// entries that changed, formatted with the codec's own one-line Format().</summary>
        static IReadOnlyList<string> ComputeDisassemblyDiff(AiScriptFile before, AiScriptFile after)
        {
            var lines = new List<string>();
            foreach (AiDiffEntry entry in AiScript_Diff.Compare(before, after))
            {
                if (entry.Type == AiDiffType.Unchanged)
                    continue;

                string line = entry.Type switch
                {
                    AiDiffType.Modified => $"W{entry.WorkerIndex} @0x{entry.Offset:X}: [Modified] {entry.OriginalText} -> {entry.EditedText}",
                    AiDiffType.Added => $"W{entry.WorkerIndex} @0x{entry.Offset:X}: [Added] {entry.EditedText}",
                    AiDiffType.Removed => $"W{entry.WorkerIndex} @0x{entry.Offset:X}: [Removed] {entry.OriginalText}",
                    _ => string.Empty,
                };
                if (line.Length > 0)
                    lines.Add(line);
            }
            return lines;
        }


        /// <summary>Semantic layer: builds the read-only IR over both sides and compares nodes by
        /// (WorkerIndex, Offset) — the same key AiScript_Diff uses, so the layers line up.
        /// A node is reported when its Meaning or Evidence changed; nodes present on only one
        /// side are reported as (added)/(removed).</summary>
        static IReadOnlyList<string> ComputeSemanticDiff(AiScriptFile before, AiScriptFile after)
        {
            AiSemanticScript beforeIr = AiSemanticScript.Build(before);
            AiSemanticScript afterIr = AiSemanticScript.Build(after);

            Dictionary<SemanticKey, AiInstructionNode> beforeMap =
                beforeIr.Nodes.ToDictionary(n => new SemanticKey(n.WorkerIndex, n.Offset));
            Dictionary<SemanticKey, AiInstructionNode> afterMap =
                afterIr.Nodes.ToDictionary(n => new SemanticKey(n.WorkerIndex, n.Offset));

            var lines = new List<string>();
            foreach (SemanticKey key in beforeMap.Keys
                         .Union(afterMap.Keys)
                         .OrderBy(k => k.WorkerIndex)
                         .ThenBy(k => k.Offset))
            {
                bool hasBefore = beforeMap.TryGetValue(key, out AiInstructionNode? b);
                bool hasAfter = afterMap.TryGetValue(key, out AiInstructionNode? a);

                if (hasBefore && hasAfter)
                {
                    if (!string.Equals(b!.Meaning, a!.Meaning, StringComparison.Ordinal))
                        lines.Add($"W{key.WorkerIndex} @0x{key.Offset:X}: meaning \"{b.Meaning}\" -> \"{a.Meaning}\"");
                    if (b!.Evidence != a!.Evidence)
                        lines.Add($"W{key.WorkerIndex} @0x{key.Offset:X}: evidence {b.Evidence} -> {a.Evidence}");
                }
                else if (hasBefore)
                {
                    lines.Add($"W{key.WorkerIndex} @0x{key.Offset:X}: (removed) meaning \"{b!.Meaning}\"");
                }
                else
                {
                    lines.Add($"W{key.WorkerIndex} @0x{key.Offset:X}: (added) meaning \"{a!.Meaning}\"");
                }
            }
            return lines;
        }

        readonly record struct SemanticKey(int WorkerIndex, int Offset);
    }
}

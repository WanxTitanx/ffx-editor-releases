using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Ai
{
    // AI pre-flight validator: answers "is this edited AI safe to save?" BEFORE the editor calls Rebuild/Save.
    //
    // It front-runs every way AiScript_File.Rebuild can throw or the in-game loader can choke, and reports each
    // problem with a precise, human message + offset INSTEAD of the opaque Rebuild exception:
    //   - unknown opcode (VM has no handler) / HasOperand-vs-0x80 mismatch (mis-sizes the stream);
    //   - dangling branch/entrypoint (a target lands on a removed instruction) — names WHICH branch + where;
    //   - out-of-range operand (jump index >= worker jumpCount, var index >= vars, pool index past pool,
    //     unknown CALL namespace) — the first two are fatal-ish, the rest are warnings;
    //   - RT0 self-check (a no-edit Write reproduces the original);
    //   - Rebuild dry-run + a re-read of the rebuilt blob (code walk closes on codeLength, no unknown opcodes);
    //   - grow / shrink classification + the 16-pad note + the "RT2 in-game recommended for a grow" advisory.
    //
    // READ-ONLY by design (only System.* + the codec): it never mutates the source, the instruction list, or any
    // worker. IsValid is true iff there are zero Error findings (Warnings/Infos never block). The verdict is built
    // to MATCH Rebuild+Splice reality — no false PASS that then throws, no false FAIL that would have worked.
    // See docs/ai/FFX_AI_ASSEMBLER_PRODUCTIZED_2026-06-05.md.

    public enum AiValidationSeverity { Info, Warning, Error }

    public sealed record AiValidationFinding(
        AiValidationSeverity Severity,
        string Message,
        int Offset = -1);   // AiFile-relative byte offset when meaningful, else -1

    public sealed class AiValidationReport
    {
        public IReadOnlyList<AiValidationFinding> Findings { get; }
        public AiValidationReport(IReadOnlyList<AiValidationFinding> findings) => Findings = findings;

        public IEnumerable<AiValidationFinding> Errors => Findings.Where(f => f.Severity == AiValidationSeverity.Error);
        public IEnumerable<AiValidationFinding> Warnings => Findings.Where(f => f.Severity == AiValidationSeverity.Warning);
        public IEnumerable<AiValidationFinding> Infos => Findings.Where(f => f.Severity == AiValidationSeverity.Info);

        public bool IsValid => !Errors.Any();   // Warnings / Infos do NOT block saving
        public int ErrorCount => Errors.Count();
        public int WarningCount => Warnings.Count();

        public string ToReportString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== AI Validation: {(IsValid ? Strings.U_Ai_ValReportPass : Strings.U_Ai_ValReportFail)} ===");
            sb.AppendLine(string.Format(Strings.U_Ai_ValReportCounts, ErrorCount, WarningCount, Infos.Count()));
            foreach (AiValidationFinding f in Errors.Concat(Warnings).Concat(Infos))
            {
                char tag = f.Severity switch { AiValidationSeverity.Error => 'E', AiValidationSeverity.Warning => 'W', _ => 'i' };
                sb.Append($"[{tag}] {f.Message}");
                if (f.Offset >= 0) sb.Append($"  @0x{f.Offset:X}");
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }
    }

    public static class AiValidator
    {
        // Known FUNCSPACE namespaces (call func-id high nibble) — cross-checked vs FFXDataParser / IDA.
        static readonly HashSet<int> KnownNamespaces = new() { 0x0, 0x1, 0x4, 0x5, 0x6, 0x7, 0x8, 0x9, 0xB, 0xC, 0xD };

        /// <summary>Validate the source's current (unedited) instruction list — the RT0 self-check path.</summary>
        public static AiValidationReport Validate(AiScriptFile source) => Validate(source, source.Instructions);

        /// <summary>PRIMARY: validate a proposed edited instruction list before Rebuild/Save.</summary>
        public static AiValidationReport Validate(AiScriptFile source, IReadOnlyList<AiInstruction> proposed)
        {
            var f = new List<AiValidationFinding>();
            if (source == null) { f.Add(new(AiValidationSeverity.Error, "source AiScriptFile is null.")); return new(f); }
            if (proposed == null) { f.Add(new(AiValidationSeverity.Error, "instruction list is null.")); return new(f); }

            if (!source.HasScript && proposed.Count == 0)
            {
                f.Add(new(AiValidationSeverity.Info, "stub / no script — nothing to validate."));
                return new AiValidationReport(f);
            }

            int scriptStart = source.ScriptStart;
            int oldCodeLen = source.CodeLength;

            // --- 1. opcode + operand-flag check; build the old->new code-relative offset map (mirrors Rebuild). ---
            var oldToNew = new Dictionary<int, int>();
            var newStarts = new HashSet<int>();
            int rel = 0;
            bool fatalEncoding = false;
            foreach (AiInstruction i in proposed)
            {
                newStarts.Add(rel);
                if (i.Offset >= 0) oldToNew[i.Offset - scriptStart] = rel;
                if (!AiScript_File.IsKnownOpcode(i.Opcode))
                {
                    f.Add(new(AiValidationSeverity.Error,
                        string.Format(Strings.U_Ai_ValUnknownOpcode, i.Opcode, AiScript_File.Mnemonic(i.Opcode), rel),
                        scriptStart + rel));
                    fatalEncoding = true;
                }
                if (i.HasOperand != AiScript_File.IsOperandBearing(i.Opcode))
                {
                    f.Add(new(AiValidationSeverity.Error,
                        string.Format(Strings.U_Ai_ValHasOperandMismatch, i.Opcode, i.HasOperand),
                        scriptStart + rel));
                    fatalEncoding = true;
                }
                rel += i.Length;
            }
            int newCodeLen = rel;

            // --- 2. dangling entrypoint / jump pre-check (replicates Rebuild.Remap WITHOUT throwing). ---
            bool Resolvable(int codeRelTarget) => oldToNew.ContainsKey(codeRelTarget) || codeRelTarget == oldCodeLen;
            foreach (AiWorker w in source.Workers)
            {
                for (int ei = 0; ei < w.Entrypoints.Count; ei++)
                {
                    int t = w.Entrypoints[ei];
                    if (!Resolvable(t))
                        f.Add(new(AiValidationSeverity.Error,
                            string.Format(Strings.U_Ai_ValDanglingEntrypoint, w.Index, ei, t),
                            scriptStart + t));
                }
                for (int ji = 0; ji < w.JumpTargets.Count; ji++)
                {
                    int t = w.JumpTargets[ji];
                    if (!Resolvable(t))
                        f.Add(new(AiValidationSeverity.Error,
                            string.Format(Strings.U_Ai_ValDanglingJump, w.Index, ji, t),
                            scriptStart + t));
                }
            }

            // --- 3. operand-range sanity (owner-by-range over the PROPOSED layout). ---
            var owners = BuildOwnerRanges(source, oldToNew, newCodeLen);
            IReadOnlyDictionary<int, IReadOnlyList<int>> sourceOwners = AiScript_File.InstructionOwners(source);
            int pos = 0;
            foreach (AiInstruction i in proposed)
            {
                int here = pos;
                pos += i.Length;
                if (!i.HasOperand) continue;
                // Derive the operand kind from the OPCODE — the editor's AiAsmRow.ToInstruction historically left
                // AiInstruction.OperandKind unset (None), which would silently disable the jump-index-OOB Error on
                // the real save path. OperandKindOf is authoritative regardless of how the caller built the row.
                switch (AiScript_File.OperandKindOf(i.Opcode))
                {
                    case AiOperandKind.JumpIndex:
                    {
                        IReadOnlyList<AiWorker> exactOwners = i.Offset >= 0
                            && sourceOwners.TryGetValue(i.Offset, out IReadOnlyList<int>? ownerIds)
                            ? ownerIds.Select(index => source.Workers.First(w => w.Index == index)).ToArray()
                            : Array.Empty<AiWorker>();
                        if (exactOwners.Count > 0)
                        {
                            AiWorker[] invalidOwners = exactOwners.Where(owner => i.Operand >= owner.JumpTargets.Count).ToArray();
                            if (invalidOwners.Length > 0)
                                f.Add(new(AiValidationSeverity.Error,
                                    string.Format(Strings.U_Ai_ValJumpIndexExceeds, here, i.Operand,
                                        string.Join(", ", invalidOwners.Select(owner => $"{owner.Index} ({owner.JumpTargets.Count})"))),
                                    scriptStart + here));
                        }
                        else
                        {
                            AiWorker? owner = OwnerOf(owners, here);
                            if (owner == null)
                                f.Add(new(AiValidationSeverity.Warning,
                                    string.Format(Strings.U_Ai_ValBranchNoOwner, here), scriptStart + here));
                            else if (i.Operand >= owner.JumpTargets.Count)
                                f.Add(new(AiValidationSeverity.Error,
                                    string.Format(Strings.U_Ai_ValJumpIndexOob, here, i.Operand, owner.JumpTargets.Count, owner.Index),
                                    scriptStart + here));
                        }
                        break;
                    }
                    case AiOperandKind.VarLoad:
                    case AiOperandKind.VarStore:
                        if (source.Variables.Count > 0 && i.Operand >= source.Variables.Count)
                            f.Add(new(AiValidationSeverity.Warning,
                                string.Format(Strings.U_Ai_ValVarIndexOob, i.Operand, source.Variables.Count, here),
                                scriptStart + here));
                        break;
                    case AiOperandKind.FloatConst:
                        {
                            int n = FloatPoolCount(source);
                            if (n >= 0 && i.Operand >= n)
                                f.Add(new(AiValidationSeverity.Warning,
                                    string.Format(Strings.U_Ai_ValFloatPoolOob, i.Operand, n, here),
                                    scriptStart + here));
                            break;
                        }
                    case AiOperandKind.IntConst:
                        {
                            int n = IntPoolCount(source);
                            if (n >= 0 && i.Operand >= n)
                                f.Add(new(AiValidationSeverity.Warning,
                                    string.Format(Strings.U_Ai_ValIntPoolOob, i.Operand, n, here),
                                    scriptStart + here));
                            break;
                        }
                    case AiOperandKind.FuncId:
                        int ns = i.Operand >> 12;
                        if (!KnownNamespaces.Contains(ns))
                            f.Add(new(AiValidationSeverity.Warning,
                                string.Format(Strings.U_Ai_ValUnknownNamespace, i.Operand, ns),
                                scriptStart + here));
                        break;
                }
            }

            // --- 4. RT0 self-check (always). For a no-edit list, a no-edit Write must reproduce the original. ---
            if (IsNoEdit(source, proposed))
            {
                bool rt0 = AiScript_File.Write(source).AsSpan().SequenceEqual(source.OriginalAiFileBytes);
                f.Add(rt0
                    ? new(AiValidationSeverity.Info, "RT0 ok: rebuild without edit is byte-identical to the original.")
                    : new(AiValidationSeverity.Error, "RT0 FAILED: a Write without edit does not reproduce the original AiFile — codec/model desync, DO NOT save."));
            }

            // --- 5. Rebuild dry-run + re-read the rebuilt blob (skip if we already have fatal errors). ---
            if (!f.Any(x => x.Severity == AiValidationSeverity.Error) && !fatalEncoding)
            {
                byte[]? rebuilt = null;
                try { rebuilt = AiScript_File.Rebuild(source, proposed); }
                catch (Exception ex)
                {
                    f.Add(new(AiValidationSeverity.Error, string.Format(Strings.U_Ai_ValRebuildRejected, ex.Message)));
                }
                if (rebuilt != null)
                {
                    try
                    {
                        AiScriptFile rr = AiScript_File.Read(rebuilt);
                        if (!rr.CodeWalkClosedExactly)
                            f.Add(new(AiValidationSeverity.Error,
                                string.Format(Strings.U_Ai_ValWalkNotClosed, rr.CodeLength)));
                        if (rr.UnknownOpcodes.Count > 0)
                            f.Add(new(AiValidationSeverity.Error,
                                string.Format(Strings.U_Ai_ValUnknownOpcodesRebuilt, string.Join(" ", rr.UnknownOpcodes.Select(b => b.ToString("X2"))))));
                        if (rr.CodeWalkClosedExactly && rr.UnknownOpcodes.Count == 0)
                        {
                            f.Add(new(AiValidationSeverity.Info,
                                string.Format(Strings.U_Ai_ValRebuiltReadsClean, rr.CodeLength, rr.Instructions.Count, rr.Workers.Count)));
                            AnalyzeStackDelta(source, rr, f);   // LEVEL 1: balança de pilha (só o que a EDIÇÃO introduziu)
                        }

                        // --- 6. grow / shrink classification ---
                        int oldLen = source.OriginalAiFileBytes.Length;
                        int delta = rebuilt.Length - oldLen;
                        if (delta == 0)
                            f.Add(new(AiValidationSeverity.Info, "edit keeps the size — verbatim splice (byte-safe), RT0 family."));
                        else
                        {
                            string verb = delta > 0 ? $"GROW +{delta} B" : $"SHRINK {delta} B";
                            f.Add(new(AiValidationSeverity.Warning,
                                string.Format(Strings.U_Ai_ValGrowShrinkWarning, verb)));
                            int padded = (rebuilt.Length + 0xF) & ~0xF;
                            f.Add(new(AiValidationSeverity.Info, string.Format(Strings.U_Ai_ValWorkerFileAligned, padded)));
                        }
                    }
                    catch (Exception ex)
                    {
                        f.Add(new(AiValidationSeverity.Error, string.Format(Strings.U_Ai_ValRebuiltBlobNoRead, ex.GetType().Name, ex.Message)));
                    }
                }
            }

            return new AiValidationReport(f);
        }

        /// <summary>SECONDARY: validate an already-rebuilt blob (re-read it; run the rebuilt-byte checks + grow/shrink).</summary>
        public static AiValidationReport ValidateRebuilt(byte[] rebuiltAiFile, int originalLength = -1)
        {
            var f = new List<AiValidationFinding>();
            if (rebuiltAiFile == null) { f.Add(new(AiValidationSeverity.Error, "rebuilt AiFile is null.")); return new(f); }
            AiScriptFile rr;
            try { rr = AiScript_File.Read(rebuiltAiFile); }
            catch (Exception ex) { f.Add(new(AiValidationSeverity.Error, string.Format(Strings.U_Ai_ValRebuiltNoRead, ex.Message))); return new(f); }

            if (!rr.CodeWalkClosedExactly)
                f.Add(new(AiValidationSeverity.Error, string.Format(Strings.U_Ai_ValWalkNotClosed2, rr.CodeLength)));
            if (rr.UnknownOpcodes.Count > 0)
                f.Add(new(AiValidationSeverity.Error, string.Format(Strings.U_Ai_ValUnknownOpcodes, string.Join(" ", rr.UnknownOpcodes.Select(b => b.ToString("X2"))))));

            // dangling check against the rebuilt blob's own instruction starts.
            var starts = new HashSet<int>(rr.Instructions.Select(i => i.Offset - rr.ScriptStart));
            bool Resolvable(int t) => starts.Contains(t) || t == rr.CodeLength;
            foreach (AiWorker w in rr.Workers)
            {
                foreach (int t in w.Entrypoints) if (!Resolvable(t))
                    f.Add(new(AiValidationSeverity.Error, string.Format(Strings.U_Ai_ValEntrypointNotInstr, w.Index, t), rr.ScriptStart + t));
                foreach (int t in w.JumpTargets) if (!Resolvable(t))
                    f.Add(new(AiValidationSeverity.Error, string.Format(Strings.U_Ai_ValJumpNotInstr, w.Index, t), rr.ScriptStart + t));
            }

            if (originalLength >= 0)
            {
                int delta = rebuiltAiFile.Length - originalLength;
                if (delta == 0) f.Add(new(AiValidationSeverity.Info, "same size as original (byte-safe)."));
                else f.Add(new(AiValidationSeverity.Warning, string.Format(Strings.U_Ai_ValGrowShrinkShort, (delta > 0 ? "GROW +" : "SHRINK ") + delta + " B")));
            }
            if (f.All(x => x.Severity != AiValidationSeverity.Error))
                f.Add(new(AiValidationSeverity.Info, string.Format(Strings.U_Ai_ValBlobReadsClean, rr.CodeLength, rr.Instructions.Count, rr.Workers.Count)));
            return new AiValidationReport(f);
        }

        public static bool HasOnlyBaselineUnknownErrors(
            AiValidationReport report,
            AiScriptFile baseline,
            AiScriptFile rebuilt)
        {
            ArgumentNullException.ThrowIfNull(report);
            ArgumentNullException.ThrowIfNull(baseline);
            ArgumentNullException.ThrowIfNull(rebuilt);

            List<AiValidationFinding> errors = report.Errors.ToList();
            return errors.Count > 0
                   && errors.All(IsUnknownOnlyValidationError)
                   && baseline.CodeWalkClosedExactly
                   && rebuilt.CodeWalkClosedExactly
                   && baseline.Workers.Count == rebuilt.Workers.Count
                   && baseline.UnknownOpcodes.SequenceEqual(rebuilt.UnknownOpcodes);
        }

        public static bool TryValidateRebuiltAllowingBaselineUnknowns(
            byte[] rebuiltAiFile,
            AiScriptFile baseline,
            out AiValidationReport report,
            out string reason) =>
            TryValidateRebuiltAllowingBaselineUnknowns(
                rebuiltAiFile,
                baseline,
                baseline?.OriginalAiFileBytes?.Length ?? -1,
                out report,
                out reason);

        public static bool TryValidateRebuiltAllowingBaselineUnknowns(
            byte[] rebuiltAiFile,
            AiScriptFile baseline,
            int originalLength,
            out AiValidationReport report,
            out string reason)
        {
            ArgumentNullException.ThrowIfNull(baseline);

            report = ValidateRebuilt(rebuiltAiFile, originalLength);
            if (report.IsValid)
            {
                reason = string.Empty;
                return true;
            }

            try
            {
                AiScriptFile rebuilt = AiScript_File.Read(rebuiltAiFile);
                if (HasOnlyBaselineUnknownErrors(report, baseline, rebuilt))
                {
                    reason = $"baseline unknowns preserved [{string.Join(" ", rebuilt.UnknownOpcodes.Select(b => b.ToString("X2")))}]";
                    return true;
                }
            }
            catch (Exception ex)
            {
                reason = string.Format(Strings.U_Ai_ValRebuiltNoRead, ex.Message);
                return false;
            }

            reason = string.Join(" | ", report.Errors.Select(error => error.Message));
            return false;
        }

        static bool IsUnknownOnlyValidationError(AiValidationFinding finding) =>
            finding.Message.StartsWith("unknown opcode", StringComparison.Ordinal)
            || finding.Message.StartsWith("unknown opcodes", StringComparison.Ordinal);

        // ---- LEVEL 1: stack-balance checker (the "Mini-corretora") ----

        // Simulate the VM stack depth through an instruction stream, resetting to 0 at each worker entrypoint / jump
        // target (block starts assume an empty stack — the statement-level norm), tracking depth with the proven
        // net/arity model (AiStackModel). Returns (underflowCount, firstUnderflowCodeRel). HONEST: it stops asserting
        // within a block past a call/opcode whose effect isn't modelled, so it never guesses.
        //
        // KEY: FFXDataParser's declared arg counts don't always equal the VM's real pop count (e.g. setSelfFloating
        // 0x7029 = parser says 2, the corpus pushes 1) — so the ABSOLUTE underflow count of a CLEAN file may be > 0
        // purely from RE-data noise. We therefore never report the absolute count; the caller diffs original-vs-edited
        // so those artifacts cancel and only EDIT-INTRODUCED imbalance surfaces.
        static (int count, int firstRel) SimulateUnderflows(AiScriptFile s)
        {
            if (s.Instructions.Count == 0) return (0, -1);
            var blockStarts = new HashSet<int>();
            foreach (AiWorker w in s.Workers)
            {
                foreach (int e in w.Entrypoints) blockStarts.Add(e);
                foreach (int j in w.JumpTargets) blockStarts.Add(j);
            }
            int depth = 0, underflows = 0, firstRel = -1;
            bool confident = true, firstSeen = false;
            foreach (AiInstruction i in s.Instructions)
            {
                int rel = i.Offset - s.ScriptStart;
                if (firstSeen && blockStarts.Contains(rel)) { depth = 0; confident = true; }
                firstSeen = true;

                int? delta;
                if (i.Opcode == 0xB5 || i.Opcode == 0xD8)
                {
                    int? args = AiStackModel.CallArgCount(i.Operand);
                    delta = args == null ? (int?)null : (i.Opcode == 0xB5 ? 1 - args.Value : -args.Value);
                }
                else delta = AiStackModel.NetNonCall(i.Opcode);

                if (delta == null) { confident = false; continue; }
                if (!confident) continue;
                depth += delta.Value;
                if (depth < 0) { underflows++; if (firstRel < 0) firstRel = rel; depth = 0; }
            }
            return (underflows, firstRel);
        }

        // LEVEL 1 surface: report ONLY the stack imbalance the EDIT introduced (original-vs-edited diff cancels the
        // RE-arity noise). newU > origU => the edit removed a needed PUSH / unbalanced a statement.
        static void AnalyzeStackDelta(AiScriptFile original, AiScriptFile edited, List<AiValidationFinding> f)
        {
            (int origU, _) = SimulateUnderflows(original);
            (int newU, int firstRel) = SimulateUnderflows(edited);
            if (newU > origU)
                f.Add(new(AiValidationSeverity.Error,
                    string.Format(Strings.U_Ai_ValStackImbalance, newU - origU, firstRel),
                    firstRel >= 0 ? original.ScriptStart + firstRel : -1));
            else
                f.Add(new(AiValidationSeverity.Info, "stack balance: the edit did not introduce a new imbalance."));
        }

        // ---- helpers ----

        // True when `proposed` is the unedited source list (reference-equal, or same length + same (op,operand) seq).
        static bool IsNoEdit(AiScriptFile source, IReadOnlyList<AiInstruction> proposed)
        {
            if (ReferenceEquals(proposed, source.Instructions)) return true;
            if (proposed.Count != source.Instructions.Count) return false;
            for (int i = 0; i < proposed.Count; i++)
            {
                AiInstruction a = proposed[i], b = source.Instructions[i];
                if (a.Offset < 0 || a.Opcode != b.Opcode || (a.HasOperand && a.Operand != b.Operand)) return false;
            }
            return true;
        }

        // Worker ranges over the PROPOSED layout: each worker owns [start, nextStart) where start = its remapped
        // min-entrypoint (new code-relative offset). Mirrors AiScript_File.Disassemble's owner-by-range.
        static List<(int start, AiWorker w)> BuildOwnerRanges(AiScriptFile source, Dictionary<int, int> oldToNew, int newCodeLen)
        {
            var ranges = new List<(int start, AiWorker w)>();
            foreach (AiWorker w in source.Workers)
            {
                if (w.Entrypoints.Count == 0) continue;
                int oldMin = w.Entrypoints.Min();
                int newMin = oldToNew.TryGetValue(oldMin, out int nm) ? nm : (oldMin == source.CodeLength ? newCodeLen : oldMin);
                ranges.Add((newMin, w));
            }
            ranges.Sort((a, b) => a.start.CompareTo(b.start));
            return ranges;
        }

        static AiWorker? OwnerOf(List<(int start, AiWorker w)> ranges, int off)
        {
            AiWorker? cur = null;
            foreach ((int start, AiWorker w) in ranges) { if (off >= start) cur = w; else break; }
            return cur;
        }

        // Conservative pool counts (mirror the codec's bounds; -1 => can't derive -> skip the check).
        static int FloatPoolCount(AiScriptFile s)
        {
            if (s.FloatPoolOffset < 0) return -1;
            int end = s.OriginalAiFileBytes.Length;
            return Math.Max(0, (end - s.FloatPoolOffset) / 4);
        }

        static int IntPoolCount(AiScriptFile s)
        {
            if (s.IntPoolOffset < 0) return -1;
            int end = s.FloatPoolOffset > s.IntPoolOffset ? s.FloatPoolOffset : s.OriginalAiFileBytes.Length;
            return Math.Max(0, (end - s.IntPoolOffset) / 4);
        }
    }
}

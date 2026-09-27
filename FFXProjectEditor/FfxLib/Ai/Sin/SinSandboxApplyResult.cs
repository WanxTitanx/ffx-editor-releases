using System.Collections.Generic;
using System.Text;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    // SIN Chain Builder — Gate 5 (backup/apply sandbox) structured result. SANDBOX-ONLY.
    //
    // The read-only, inspectable answer to one sandbox-apply attempt. It records EXACTLY what happened (and what
    // did NOT): whether the recipe was eligible, whether a backup was created, whether the original was touched
    // (always NO), whether the sandbox copy was written, whether the post-apply diff was the expected conservative
    // block, whether the restore came back byte-identical — plus the hashes that PROVE each claim. Public apply and
    // RT2 are hard-wired NO. ToReportString() renders the PENANCE output template verbatim.

    public enum SinSandboxOutcome
    {
        /// <summary>Recipe not eligible for a sandbox apply — nothing was copied/backed-up/written.</summary>
        Blocked,
        /// <summary>Eligible, but the corpus fixture was absent — the real apply was skipped (harness not exercised).</summary>
        Skipped,
        /// <summary>Eligible, but the real codec emit failed — nothing was written (honest non-apply).</summary>
        EmitFailed,
        /// <summary>Eligible + emitted, but the post-apply diff was NOT the expected conservative block — refused, nothing committed.</summary>
        DiffRejected,
        /// <summary>Applied to the sandbox copy, verified, and restored byte-identical. The original was never touched.</summary>
        Applied,
    }

    public sealed class SinSandboxApplyResult
    {
        public required string RecipeId { get; init; }
        public required string RecipeDisplayName { get; init; }
        public required SinSandboxOutcome Outcome { get; init; }
        public required string ValidationSummary { get; init; }
        public required string AeonPreviewLabel { get; init; }   // "expected" / "warning/blocker" / "unexpected"

        public string? BlockReason { get; init; }

        public string? SandboxTarget { get; init; }
        public string? FixtureSource { get; init; }
        public int WorkerIndex { get; init; } = -1;
        public int EntrypointIndex { get; init; } = -1;
        public string? WorkerResolution { get; init; }

        public bool BackupCreated { get; init; }
        public bool AppliedToOriginal { get; init; }            // ALWAYS false — invariant
        public bool AppliedToSandbox { get; init; }
        public bool UnexpectedDiff { get; init; }
        public bool RestoreByteIdentical { get; init; }
        public bool PublicApplyAllowed { get; init; }           // ALWAYS false — invariant
        public bool Rt2 { get; init; }                          // ALWAYS false — invariant

        // diff shape (real AiScript_Diff against the loaded monster copy)
        public int ExpectedAddedRows { get; init; }
        public int ActualAddedRows { get; init; }
        public int ActualModifiedRows { get; init; }
        public int ActualRemovedRows { get; init; }
        public bool ReParsedClean { get; init; }
        public bool AiScriptLabClean { get; init; }

        /// <summary>The full edited monster image (in memory) the session committed to the sandbox copy. Carried so an
        /// RT2 pilot layer can stage the SAME proven bytes without re-emitting (PENANCE handoff contract). Never a
        /// write authorization by itself — whoever consumes it owns its gates.</summary>
        public byte[]? EditedMonsterBytes { get; init; }

        // hashes (proof)
        public string? OriginalSha256Before { get; init; }      // pristine reference copy, before apply
        public string? OriginalSha256After { get; init; }       // pristine reference copy, after the whole session
        public string? CorpusSha256Before { get; init; }        // the real read-only source on disk, before
        public string? CorpusSha256After { get; init; }         // the real read-only source on disk, after
        public string? SandboxSha256PostApply { get; init; }
        public string? SandboxSha256PostRestore { get; init; }
        public string? BackupSha256 { get; init; }

        public IReadOnlyList<string> Notes { get; init; } = new List<string>();

        public string ToReportString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Recipe: {RecipeId} · {RecipeDisplayName}");
            sb.AppendLine("Gate: sandbox-apply");
            sb.AppendLine($"Validation: {ValidationSummary}");
            sb.AppendLine($"AEON preview: {AeonPreviewLabel}");

            if (Outcome == SinSandboxOutcome.Applied)
            {
                sb.AppendLine($"Sandbox target: {SandboxTarget}");
                sb.AppendLine($"Worker resolution: {WorkerResolution}");
                sb.AppendLine($"Backup created: {YesNo(BackupCreated)}");
                sb.AppendLine($"Applied to original: {YesNo(AppliedToOriginal)}");
                sb.AppendLine($"Applied to sandbox: {YesNo(AppliedToSandbox)}");
                sb.AppendLine($"Unexpected diff: {YesNo(UnexpectedDiff)}");
                sb.AppendLine($"Diff rows: +{ActualAddedRows} ~{ActualModifiedRows} -{ActualRemovedRows} (expected +{ExpectedAddedRows} ~0 -0)");
                sb.AppendLine($"Re-parsed clean: {YesNo(ReParsedClean)}");
                sb.AppendLine($"AiScriptLab clean: {YesNo(AiScriptLabClean)}");
                sb.AppendLine($"Restore byte-identical: {YesNo(RestoreByteIdentical)}");
                sb.AppendLine($"Public apply allowed: {YesNo(PublicApplyAllowed)}");
                sb.AppendLine($"RT2: {YesNo(Rt2)}");
                sb.AppendLine($"  corpus source SHA  before/after : {CorpusSha256Before} / {CorpusSha256After}");
                sb.AppendLine($"  original copy SHA  before/after : {OriginalSha256Before} / {OriginalSha256After}");
                sb.AppendLine($"  sandbox SHA post-apply/restore  : {SandboxSha256PostApply} / {SandboxSha256PostRestore}");
                sb.AppendLine($"  backup  SHA (pre-apply image)   : {BackupSha256}");
            }
            else
            {
                string verb = Outcome switch
                {
                    SinSandboxOutcome.Blocked => "Sandbox apply: BLOCKED",
                    SinSandboxOutcome.Skipped => "Sandbox apply: SKIPPED (corpus fixture absent)",
                    SinSandboxOutcome.EmitFailed => "Sandbox apply: NOT APPLIED (emit failed)",
                    SinSandboxOutcome.DiffRejected => "Sandbox apply: REJECTED (unexpected diff)",
                    _ => "Sandbox apply: NOT APPLIED",
                };
                sb.AppendLine(verb);
                sb.AppendLine($"Reason: {BlockReason}");
                sb.AppendLine($"Applied to sandbox: {YesNo(AppliedToSandbox)}");
                sb.AppendLine($"Public apply allowed: {YesNo(PublicApplyAllowed)}");
            }

            foreach (string n in Notes)
                sb.AppendLine($"  note: {n}");

            return sb.ToString().TrimEnd();
        }

        static string YesNo(bool b) => b ? "YES" : "NO";
    }
}

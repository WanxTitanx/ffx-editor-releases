using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.FfxLib.Ai;

namespace FFXProjectEditor.Core.Writers
{
    /// <summary>
    /// L1 of docs/ai/P2_INTEGRACAO_PIPELINE_2026-07-31.md: read-only/preview adapter for the ATEL
    /// monster-AI script (the AiFile partition inside monster_*.bin).
    ///
    /// This adapter does NOT write. StageAsync refuses any edit (InvalidOperationException,
    /// marker "preview-only") and only supports the copy-only path (Edits.Count == 0 → source
    /// copied to staging byte-for-byte, predicted after-hash == before-hash — the same semantics
    /// OperationExecutorV2 relies on for no-edit operations). Real ATEL writes land in L2
    /// (AtelPhaseRotationAdapter, byte-local recipes first — docs/ai/P2_RECEITAS_ATEL_2026-07-31.md §3).
    ///
    /// The preview contract lives in two static helpers:
    ///   • ComputePreviewDiff   — three-layer diff (bytes / disassembly / semantic IR) via
    ///                            AiDiffThreeLayer, for the P2 "simulate → review diff" flow;
    ///   • ValidateMonsterBinHash — SHA-256 precondition over the WHOLE monster_*.bin, because
    ///                            Monster_File.Write re-emits the container and relocates the
    ///                            AiFile/WorkerFile pointers (P2_INTEGRACAO §2.2).
    /// </summary>
    public sealed class AtelScriptAdapter : IWriterAdapter
    {
        public string CapabilityId => "atel-script";
        public string DisplayName => "ATEL Script (preview)";
        public RiskLevel Risk => RiskLevel.Safe;

        /// <summary>Marker every preview-only rejection message carries (asserted by tests).</summary>
        public const string PreviewOnlyMarker = "preview-only";
        /// <summary>Fixed validation error for any non-empty edit set.</summary>
        public const string PreviewOnlyValidationError = "preview-only adapter: no edits allowed";

        public string ComputeBeforeHash(string sourcePath)
        {
            ArgumentNullException.ThrowIfNull(sourcePath);
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(sourcePath);
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }

        public async Task<string> StageAsync(
            string sourcePath,
            string stagingPath,
            IReadOnlyDictionary<string, object> edits,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(edits);

            // Preview-only contract: refuse ANY edit BEFORE touching the staging path, so a rejected
            // operation can never leave partial staging artifacts behind (tested: nothing written).
            if (edits.Count > 0)
            {
                throw new InvalidOperationException(
                    "AtelScriptAdapter is " + PreviewOnlyMarker + ": no edits are allowed "
                    + "(Edits.Count = " + edits.Count + "). Use the AtelPhaseRotationAdapter "
                    + "(P2 L2, byte-local recipes) for actual ATEL writes.");
            }

            // Copy-only path (same semantics OperationExecutorV2 uses for no-edit operations):
            // the source is staged unchanged and the predicted after-hash equals the before-hash.
            byte[] bytes = await File.ReadAllBytesAsync(sourcePath, ct);
            string? dir = Path.GetDirectoryName(stagingPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(stagingPath, bytes, ct);
            return ComputeSha256(bytes);
        }

        public FileDiffSummary DescribeChanges(IReadOnlyDictionary<string, object> edits)
        {
            // Preview adapter never changes anything → empty summary, always (invalid edits are
            // rejected by ValidateEdits before a plan is built, so nothing can reach StageAsync).
            return new FileDiffSummary
            {
                FieldsChanged = 0,
                ChangedFieldNames = Array.Empty<string>(),
                HumanSummary = string.Empty,
            };
        }

        public IReadOnlyList<string> ValidateEdits(IReadOnlyDictionary<string, object> edits)
        {
            if (edits.Count > 0)
                return new[] { PreviewOnlyValidationError };
            return Array.Empty<string>();
        }

        /// <summary>
        /// Three-layer preview diff (bytes / disassembly / semantic IR) between two AiFile blobs.
        /// Delegates to AiDiffThreeLayer.Compute with hasExtraInfo: false — the ATEL AiFile format
        /// has no entry-size variant (documented in AiDiffThreeLayer), so the flag is irrelevant here.
        /// Usable by the P2 "simulate → review diff" flow (docs/ai/P2_INTEGRACAO_PIPELINE_2026-07-31.md §3).
        /// </summary>
        public static ThreeLayerDiff? ComputePreviewDiff(byte[] beforeBytes, byte[] afterBytes)
            => AiDiffThreeLayer.Compute(beforeBytes, afterBytes, hasExtraInfo: false);

        /// <summary>
        /// SHA-256 precondition over the WHOLE monster_*.bin (not just the AiFile partition):
        /// Monster_File.Write re-emits the container and relocates the AiFile/WorkerFile pointers,
        /// so the bin is the hash unit the L2 phase-rotation adapter must snapshot (P2_INTEGRACAO §2.2).
        /// Returns null when the file hashes to expectedHash; otherwise a descriptive error message.
        /// Comparison is case-insensitive (hashes are hex; adapters emit lowercase).
        /// </summary>
        public static string? ValidateMonsterBinHash(string monsterBinPath, string expectedHash)
        {
            ArgumentNullException.ThrowIfNull(expectedHash);
            string actual = ComputeSha256(File.ReadAllBytes(monsterBinPath));
            if (string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
                return null;

            return "Monster bin hash mismatch: expected " + expectedHash
                   + " but " + monsterBinPath + " hashes to " + actual
                   + " (Monster_File.Write relocates AiFile pointers, so the whole bin is the precondition unit).";
        }

        static string ComputeSha256(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
        }
    }
}

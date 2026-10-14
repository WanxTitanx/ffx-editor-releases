using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FFXProjectEditor.Core
{
    /// <summary>
    /// Adapts an existing FfxLib writer to the OperationPlan pipeline.
    /// Each adapter knows how to:
    /// 1. Compute the BeforeHash of the source file
    /// 2. Produce the modified bytes (staged)
    /// 3. Compute the PredictedAfterHash
    /// 4. Describe what changed (diff summary)
    /// </summary>
    public interface IWriterAdapter
    {
        /// <summary>Stable ID matching CapabilityDescriptor.Id.</summary>
        string CapabilityId { get; }

        /// <summary>Human-readable name for logs/UI.</summary>
        string DisplayName { get; }

        /// <summary>
        /// Compute SHA-256 of the source file before any edit.
        /// </summary>
        string ComputeBeforeHash(string sourcePath);

        /// <summary>
        /// Produce the modified bytes and write them to the staging path.
        /// Returns the predicted after-hash.
        /// </summary>
        Task<string> StageAsync(
            string sourcePath,
            string stagingPath,
            IReadOnlyDictionary<string, object> edits,
            CancellationToken ct = default);

        /// <summary>
        /// Describe what changed for the UI diff preview.
        /// </summary>
        FileDiffSummary DescribeChanges(IReadOnlyDictionary<string, object> edits);

        /// <summary>
        /// Validate that the edits are safe to apply.
        /// Returns errors (empty = safe).
        /// </summary>
        IReadOnlyList<string> ValidateEdits(IReadOnlyDictionary<string, object> edits);

        /// <summary>Risk level for this operation type.</summary>
        RiskLevel Risk { get; }
    }
}

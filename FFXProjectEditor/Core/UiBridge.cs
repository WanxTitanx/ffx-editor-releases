using System;
using System.Collections.Generic;

namespace FFXProjectEditor.Core
{
    /// <summary>
    /// Groups a batch of operations by domain for the confirmation dialog.
    /// </summary>
    public sealed record ChangeSetSummary
    {
        public required string Domain { get; init; }
        public required int OperationCount { get; init; }
        public required RiskLevel MaxRisk { get; init; }
        public required IReadOnlyList<string> FileNames { get; init; }
    }

    /// <summary>
    /// Read-only preview of an OperationPlan for the confirmation dialog.
    /// </summary>
    public sealed record OperationPlanPreview
    {
        public required string OperationId { get; init; }
        public required string DisplayName { get; init; }
        public required int FileCount { get; init; }
        public required long TotalBytes { get; init; }
        public required RiskLevel OverallRisk { get; init; }
        public required IReadOnlyList<ChangeSetSummary> ChangeSets { get; init; }
        public required IReadOnlyList<string> Preconditions { get; init; }
    }

    /// <summary>
    /// Real-time progress during operation execution.
    /// </summary>
    public sealed record OperationProgress
    {
        public required int Percentage { get; init; }
        public required string CurrentFile { get; init; }
        public required int FilesCompleted { get; init; }
        public required int TotalFiles { get; init; }
        public required string Status { get; init; }
    }

    /// <summary>
    /// Post-execution receipt proving what was written.
    /// </summary>
    public sealed record CommitReceipt
    {
        public required string ReceiptId { get; init; }
        public required DateTimeOffset Timestamp { get; init; }
        public required int FilesWritten { get; init; }
        public required long BytesWritten { get; init; }
        public required IReadOnlyList<string> BackupPaths { get; init; }
        public required bool Success { get; init; }
    }

    /// <summary>
    /// A single validation issue found on a file.
    /// </summary>
    public enum ValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed record ValidationIssue
    {
        public required string FileId { get; init; }
        public required ValidationSeverity Severity { get; init; }
        public required string Message { get; init; }
    }

    /// <summary>
    /// Raised when a running operation reports progress.
    /// </summary>
    public delegate void OperationProgressChanged(object? sender, OperationProgress progress);

    /// <summary>
    /// Raised when a running operation completes and produces a receipt.
    /// </summary>
    public delegate void OperationCompleted(object? sender, CommitReceipt receipt);
}

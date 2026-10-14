using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.Core
{
    public enum FileOperationKind
    {
        CreateNew,
        Overwrite,
        Patch,
        Delete
    }

    public enum RiskLevel
    {
        Safe,
        Moderate,
        High
    }

    public sealed record FileDiffSummary
    {
        public required int FieldsChanged { get; init; }
        public required IReadOnlyList<string> ChangedFieldNames { get; init; }
        public required string HumanSummary { get; init; }
    }

    public sealed record FileOperation
    {
        public required string Id { get; init; }
        public required FileOperationKind Kind { get; init; }
        public required string SourceRelativePath { get; init; }
        public required string OutputRelativePath { get; init; }
        public required string BeforeHash { get; init; }
        public required string PredictedAfterHash { get; init; }
        public required long EstimatedBytes { get; init; }
        public required string Description { get; init; }
        public required FileDiffSummary Diff { get; init; }
        public required RiskLevel Risk { get; init; }
        /// <summary>Field edits transported from the EditSession to the executor.
        /// Empty = copy-only operation (no adapter mutation).</summary>
        public required IReadOnlyDictionary<string, object> Edits { get; init; }
    }

    public sealed record OperationPlan
    {
        public required string OperationId { get; init; }
        public required string DisplayName { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
        public required string SourceRoot { get; init; }
        public required string OutputRoot { get; init; }
        public required string StagingRoot { get; init; }
        public required string BackupRoot { get; init; }
        public required IReadOnlyList<FileOperation> Operations { get; init; }
        public required IReadOnlyList<string> Preconditions { get; init; }
        public required string OwnerCapabilityId { get; init; }

        public int FileCount => Operations.Count;
        public long TotalBytes => Operations.Sum(o => o.EstimatedBytes);
    }

    public sealed record ValidationResult
    {
        public required string FileId { get; init; }
        public required bool Passed { get; init; }
        public required string? ActualHash { get; init; }
        public required string? ExpectedHash { get; init; }
        public required string? Message { get; init; }
    }

    public sealed record OperationResult
    {
        public required bool Success { get; init; }
        public required string ReceiptId { get; init; }
        public required int FilesWritten { get; init; }
        public required long BytesWritten { get; init; }
        public required IReadOnlyList<ValidationResult> Validations { get; init; }
        public required IReadOnlyList<string> BackupPaths { get; init; }
        public required string? ErrorMessage { get; init; }
        public required string? RecoveryInstructions { get; init; }
    }
}

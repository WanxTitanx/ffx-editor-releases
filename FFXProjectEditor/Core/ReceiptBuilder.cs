using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Core
{
    public static class ReceiptBuilder
    {
        public static CommitReceipt BuildReceipt(OperationPlan plan, OperationResult result) => new()
        {
            ReceiptId = result.ReceiptId,
            Timestamp = DateTimeOffset.UtcNow,
            FilesWritten = result.FilesWritten,
            BytesWritten = result.BytesWritten,
            BackupPaths = result.BackupPaths,
            Success = result.Success
        };

        public static CommitReceipt BuildReceiptFromException(OperationPlan plan, Exception ex) => new()
        {
            ReceiptId = plan.OperationId,
            Timestamp = DateTimeOffset.UtcNow,
            FilesWritten = 0,
            BytesWritten = 0,
            BackupPaths = Array.Empty<string>(),
            Success = false
        };

        public static OperationPlanPreview ConvertToPreview(OperationPlan plan)
        {
            var changeSets = GroupByDomain(plan.Operations);
            var maxRisk = plan.Operations.Count > 0
                ? plan.Operations.Max(o => o.Risk)
                : RiskLevel.Safe;
            return new OperationPlanPreview
            {
                OperationId = plan.OperationId,
                DisplayName = plan.DisplayName,
                FileCount = plan.FileCount,
                TotalBytes = plan.TotalBytes,
                OverallRisk = maxRisk,
                ChangeSets = changeSets,
                Preconditions = plan.Preconditions
            };
        }

        public static IReadOnlyList<ChangeSetSummary> GroupByDomain(IReadOnlyList<FileOperation> operations) =>
            operations
                .GroupBy(o => GetDomain(o.SourceRelativePath))
                .Select(g => new ChangeSetSummary
                {
                    Domain = g.Key,
                    OperationCount = g.Count(),
                    MaxRisk = g.Max(o => o.Risk),
                    FileNames = g.Select(o => Path.GetFileName(o.SourceRelativePath)).ToList()
                })
                .ToList();

        private static string GetDomain(string relativePath)
        {
            var trimmed = relativePath.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var idx = trimmed.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
            return idx > 0 ? trimmed.Substring(0, idx) : "root";
        }
    }
}

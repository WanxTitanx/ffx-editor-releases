using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FFXProjectEditor.Core
{
    public sealed class OperationExecutor
    {
        public OperationResult Execute(OperationPlan plan)
        {
            var validations = new List<ValidationResult>();
            var backupPaths = new List<string>();
            var filesWritten = 0;
            long bytesWritten = 0;

            // Phase 1: Validate preconditions
            var pathResult = PathGuard.ValidateOutputPath(plan.SourceRoot, plan.OutputRoot);
            if (!pathResult.IsValid)
            {
                return Fail(plan, "Path validation failed", pathResult.Errors);
            }

            // Phase 2: Ensure directories exist
            Directory.CreateDirectory(plan.StagingRoot);
            Directory.CreateDirectory(plan.BackupRoot);

            try
            {
                foreach (var op in plan.Operations)
                {
                    var sourcePath = Path.Combine(plan.SourceRoot, op.SourceRelativePath);
                    var outputPath = Path.Combine(plan.OutputRoot, op.OutputRelativePath);
                    var stagingPath = Path.Combine(plan.StagingRoot, op.Id);

                    // Verify source hash
                    if (File.Exists(sourcePath))
                    {
                        var actualHash = ComputeSha256(sourcePath);
                        if (actualHash != op.BeforeHash)
                        {
                            validations.Add(new ValidationResult
                            {
                                FileId = op.Id,
                                Passed = false,
                                ActualHash = actualHash,
                                ExpectedHash = op.BeforeHash,
                                Message = "Source file changed since plan was created"
                            });
                            continue;
                        }
                    }

                    // Backup original if overwriting
                    if (op.Kind == FileOperationKind.Overwrite && File.Exists(outputPath))
                    {
                        var backupPath = Path.Combine(plan.BackupRoot, op.Id + ".bak");
                        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                        File.Copy(outputPath, backupPath, overwrite: true);
                        backupPaths.Add(backupPath);
                    }

                    // Stage the write
                    Directory.CreateDirectory(Path.GetDirectoryName(stagingPath)!);

                    if (op.Kind == FileOperationKind.Delete)
                    {
                        if (File.Exists(outputPath))
                            File.Delete(outputPath);
                    }
                    else
                    {
                        // The actual write bytes come from the caller's adapter
                        // For now, validate that staging succeeded
                        if (!File.Exists(stagingPath))
                        {
                            validations.Add(new ValidationResult
                            {
                                FileId = op.Id,
                                Passed = false,
                                ActualHash = null,
                                ExpectedHash = op.PredictedAfterHash,
                                Message = "Staging file not found — writer adapter must create it"
                            });
                            continue;
                        }

                        // Verify staged hash
                        var stagedHash = ComputeSha256(stagingPath);
                        validations.Add(new ValidationResult
                        {
                            FileId = op.Id,
                            Passed = stagedHash == op.PredictedAfterHash,
                            ActualHash = stagedHash,
                            ExpectedHash = op.PredictedAfterHash,
                            Message = stagedHash == op.PredictedAfterHash ? "OK" : "Hash mismatch after staging"
                        });

                        // Promote from staging to output
                        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                        File.Copy(stagingPath, outputPath, overwrite: true);
                        filesWritten++;
                        bytesWritten += new FileInfo(outputPath).Length;
                    }
                }

                return new OperationResult
                {
                    Success = validations.All(v => v.Passed),
                    ReceiptId = Guid.NewGuid().ToString("N"),
                    FilesWritten = filesWritten,
                    BytesWritten = bytesWritten,
                    Validations = validations,
                    BackupPaths = backupPaths,
                    ErrorMessage = null,
                    RecoveryInstructions = backupPaths.Count > 0
                        ? $"Backups available at: {string.Join(", ", backupPaths)}"
                        : null
                };
            }
            catch (Exception ex)
            {
                return Fail(plan, ex.Message, new[] { ex.ToString() });
            }
        }

        private static OperationResult Fail(OperationPlan plan, string message, IEnumerable<string> details)
        {
            return new OperationResult
            {
                Success = false,
                ReceiptId = Guid.NewGuid().ToString("N"),
                FilesWritten = 0,
                BytesWritten = 0,
                Validations = Array.Empty<ValidationResult>(),
                BackupPaths = Array.Empty<string>(),
                ErrorMessage = message,
                RecoveryInstructions = string.Join("\n", details)
            };
        }

        public static string ComputeSha256(string filePath)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha.ComputeHash(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}

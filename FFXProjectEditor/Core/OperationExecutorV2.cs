using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.Core.LLM;

namespace FFXProjectEditor.Core
{
    public sealed class OperationExecutorV2
    {
        private readonly WriterAdapterCatalog _catalog;

        /// <summary>Serializes the promote (write) phase across all executor instances — two concurrent
        /// operations targeting the same output must not interleave (P3-L3 concurrency contract).</summary>
        private static readonly object PromoteGate = new();

        public OperationExecutorV2(WriterAdapterCatalog catalog)
        {
            _catalog = catalog;
        }

        public async Task<OperationResult> ExecuteAsync(OperationPlan plan, CancellationToken ct = default)
        {
            var validations = new List<ValidationResult>();
            var backupPaths = new List<string>();
            int filesWritten = 0;
            long bytesWritten = 0;

            var pathResult = PathGuard.ValidateOutputPath(plan.SourceRoot, plan.OutputRoot);
            if (!pathResult.IsValid)
                return Fail(plan, "Path validation failed", pathResult.Errors);

            Directory.CreateDirectory(plan.StagingRoot);
            Directory.CreateDirectory(plan.BackupRoot);

            try
            {
                ct.ThrowIfCancellationRequested(); // checkpoint: cancel BEFORE stage

                // ---- Pass 1: Stage + Verify ALL operations (nothing touches the output) ----
                var staged = new List<(FileOperation Op, string StagingPath, string OutputPath, string StagedHash)>();
                foreach (var op in plan.Operations)
                {
                    ct.ThrowIfCancellationRequested(); // checkpoint: cancel BETWEEN ops

                    var sourcePath = Path.Combine(plan.SourceRoot, op.SourceRelativePath);
                    var outputPath = Path.Combine(plan.OutputRoot, op.OutputRelativePath);
                    var stagingPath = Path.Combine(plan.StagingRoot, op.Id);

                    if (File.Exists(sourcePath))
                    {
                        var actualHash = ComputeSha256(sourcePath);
                        if (actualHash != op.BeforeHash)
                        {
                            validations.Add(new ValidationResult
                            {
                                FileId = op.Id, Passed = false,
                                ActualHash = actualHash, ExpectedHash = op.BeforeHash,
                                Message = "Source changed since plan creation"
                            });
                            continue;
                        }
                    }

                    var adapter = _catalog.Get(plan.OwnerCapabilityId);
                    if (adapter != null)
                    {
                        string stagedHash;
                        try
                        {
                            stagedHash = await adapter.StageAsync(sourcePath, stagingPath, op.Edits, ct).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            throw; // cancellation propagates to the outer handler
                        }
                        catch (Exception ex)
                        {
                            validations.Add(new ValidationResult
                            {
                                FileId = op.Id, Passed = false,
                                ActualHash = null, ExpectedHash = op.PredictedAfterHash,
                                Message = "Stage failed: " + LlmRedactor.Redact(ex.Message)
                            });
                            continue;
                        }

                        // Verify: an edit must actually change the bytes (no silent no-op).
                        bool editsApplied = op.Edits.Count > 0 && stagedHash != op.BeforeHash;
                        bool predictedMatches = op.PredictedAfterHash is "pending-stage" or "" or null
                            || stagedHash == op.PredictedAfterHash;
                        bool passed = editsApplied || stagedHash == op.BeforeHash; // copy-only ops pass
                        validations.Add(new ValidationResult
                        {
                            FileId = op.Id, Passed = passed && predictedMatches,
                            ActualHash = stagedHash, ExpectedHash = op.PredictedAfterHash,
                            Message = !passed ? "Edit produced no byte change (silent no-op)"
                                    : !predictedMatches ? "Staged hash does not match prediction"
                                    : "OK"
                        });

                        if (passed && predictedMatches)
                            staged.Add((op, stagingPath, outputPath, stagedHash));
                    }
                    else
                    {
                        // Copy-only path (no adapter registered): plain byte-identical copy,
                        // no mutation — original executor contract preserved.
                        Directory.CreateDirectory(Path.GetDirectoryName(stagingPath)!);
                        if (File.Exists(sourcePath))
                            File.Copy(sourcePath, stagingPath, overwrite: true);
                        var stagedHash = File.Exists(stagingPath) ? ComputeSha256(stagingPath) : "";
                        bool copyOk = stagedHash == op.BeforeHash;
                        validations.Add(new ValidationResult
                        {
                            FileId = op.Id,
                            Passed = copyOk,
                            ActualHash = stagedHash, ExpectedHash = op.BeforeHash,
                            Message = copyOk ? "OK (copy-only)" : "Copy mismatch"
                        });
                        if (copyOk)
                            staged.Add((op, stagingPath, outputPath, stagedHash));
                    }
                }

                if (validations.Count == 0)
                    return Fail(plan, "No operations were executable", Array.Empty<string>());

                bool allPassed = validations.All(v => v.Passed);
                if (!allPassed)
                {
                    return new OperationResult
                    {
                        Success = false,
                        ReceiptId = Guid.NewGuid().ToString("N"),
                        FilesWritten = 0, BytesWritten = 0,
                        Validations = validations, BackupPaths = Array.Empty<string>(),
                        ErrorMessage = "Validation failed — nothing was written to the output",
                        RecoveryInstructions = "No output files were touched; staging remains for diagnosis at " + plan.StagingRoot
                    };
                }

                // ---- Pass 2: Backup + Promote ALL (only after every validation passed) ----
                // Concurrency contract (P3-L3): promote is serialized across executor instances.
                lock (PromoteGate)
                {
                    ct.ThrowIfCancellationRequested(); // checkpoint: cancel BEFORE promote
                    WriteJournal(plan.BackupRoot, "promoting", plan, Array.Empty<string>(), null);
                    var promoted = new List<(string OutputPath, string? BackupPath)>();

                    try
                    {
                        foreach (var (op, stagingPath, outputPath, _) in staged)
                        {
                            ct.ThrowIfCancellationRequested(); // checkpoint: cancel DURING promote

                            string? backupPath = null;
                            if (op.Kind == FileOperationKind.Overwrite && File.Exists(outputPath))
                            {
                                backupPath = Path.Combine(plan.BackupRoot, op.Id + ".bak");
                                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                                File.Copy(outputPath, backupPath, overwrite: true);
                                backupPaths.Add(backupPath);
                            }

                            if (File.Exists(stagingPath))
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                                File.Copy(stagingPath, outputPath, overwrite: true);
                                filesWritten++;
                                bytesWritten += new FileInfo(outputPath).Length;
                            }
                            promoted.Add((outputPath, backupPath));
                        }

                        WriteJournal(plan.BackupRoot, "success", plan, backupPaths, null);
                        return new OperationResult
                        {
                            Success = true,
                            ReceiptId = Guid.NewGuid().ToString("N"),
                            FilesWritten = filesWritten, BytesWritten = bytesWritten,
                            Validations = validations, BackupPaths = backupPaths,
                            ErrorMessage = null,
                            RecoveryInstructions = backupPaths.Count > 0
                                ? $"Backups at: {string.Join(", ", backupPaths)}" : null
                        };
                    }
                    catch (OperationCanceledException)
                    {
                        // Recovery from partial promote: restore every already-promoted output.
                        RollbackPromoted(promoted);
                        WriteJournal(plan.BackupRoot, "cancelled", plan, backupPaths,
                            "Operation cancelled during promote; outputs restored");
                        return Fail(plan, "Operation cancelled during promote — outputs restored",
                            new[] { "Journal: " + Path.Combine(plan.BackupRoot, "journal.json") });
                    }
                    catch (Exception ex)
                    {
                        RollbackPromoted(promoted);
                        WriteJournal(plan.BackupRoot, "failed", plan, backupPaths,
                            LlmRedactor.Redact(ex.Message));
                        return Fail(plan, "Promote failed — outputs restored",
                            new[] { LlmRedactor.Redact(ex.Message),
                                    "Journal: " + Path.Combine(plan.BackupRoot, "journal.json") });
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return Fail(plan, "Operation cancelled — nothing was written", Array.Empty<string>());
            }
            catch (Exception ex)
            {
                return Fail(plan, LlmRedactor.Redact(ex.Message),
                    new[] { LlmRedactor.Redact(ex.ToString()) });
            }
        }

        private static OperationResult Fail(OperationPlan plan, string msg, IEnumerable<string> details)
        {
            // P3-L3 secret contract: failure surfaces are redacted — never a raw secret/stack trace.
            return new OperationResult
            {
                Success = false, ReceiptId = Guid.NewGuid().ToString("N"),
                FilesWritten = 0, BytesWritten = 0,
                Validations = Array.Empty<ValidationResult>(),
                BackupPaths = Array.Empty<string>(),
                ErrorMessage = LlmRedactor.Redact(msg),
                RecoveryInstructions = LlmRedactor.Redact(string.Join("\n", details))
            };
        }

        /// <summary>Restore every already-promoted output from its backup (or delete outputs that were
        /// created by this promote). Best-effort: a failing restore never masks the original error.</summary>
        static void RollbackPromoted(IReadOnlyList<(string OutputPath, string? BackupPath)> promoted)
        {
            foreach (var (output, backup) in promoted)
            {
                try
                {
                    if (backup != null && File.Exists(backup))
                        File.Copy(backup, output, overwrite: true);
                    else if (File.Exists(output))
                        File.Delete(output); // created by this promote (no prior file) — undo
                }
                catch
                {
                    // best-effort rollback; the journal still records the failure
                }
            }
        }

        /// <summary>Best-effort journal (manifest) of the promote phase — recovery contract (P3-L3).
        /// States: promoting → success | cancelled | failed. Never throws (journal must not fail the op).</summary>
        static void WriteJournal(string backupRoot, string state, OperationPlan plan,
            IReadOnlyList<string> backups, string? error)
        {
            try
            {
                Directory.CreateDirectory(backupRoot);
                var journal = new
                {
                    operationId = plan.OperationId,
                    displayName = plan.DisplayName,
                    state,
                    timestamp = DateTimeOffset.UtcNow.ToString("O"),
                    files = plan.Operations.Select(o => o.OutputRelativePath).ToArray(),
                    backups,
                    error = LlmRedactor.Redact(error),
                };
                File.WriteAllText(Path.Combine(backupRoot, "journal.json"),
                    JsonSerializer.Serialize(journal, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // journal is best-effort; never fails the operation
            }
        }

        public static string ComputeSha256(string filePath)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }
    }
}

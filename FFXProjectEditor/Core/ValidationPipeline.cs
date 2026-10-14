using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FFXProjectEditor.Core
{
    public static class ValidationPipeline
    {
        public static ValidationResult[] ValidateBeforeCommit(
            OperationPlan plan, string stagingRoot)
        {
            var results = new List<ValidationResult>();
            string normStaging = Path.GetFullPath(stagingRoot);

            // Global: staging root must not be inside source root
            var pathCheck = PathGuard.ValidateOutputPath(plan.SourceRoot, normStaging);
            if (!pathCheck.IsValid)
            {
                results.Add(new ValidationResult
                {
                    FileId = "_staging_root",
                    Passed = false, ActualHash = null, ExpectedHash = null,
                    Message = $"Staging containment: {string.Join("; ", pathCheck.Errors)}"
                });
                return results.ToArray(); // critical — short-circuit
            }

            using var sha = SHA256.Create();

            foreach (var op in plan.Operations)
            {
                // 1) Source path containment
                var srcPath = PathGuard.ValidateSourcePath(plan.SourceRoot, op.SourceRelativePath);
                if (!srcPath.IsValid)
                {
                    results.Add(Fail(op.Id, "source_containment",
                        $"Path blocked: {string.Join("; ", srcPath.Errors)}"));
                    return results.ToArray(); // critical — traversal = abort
                }

                string srcFull = Path.GetFullPath(
                    Path.Combine(plan.SourceRoot, op.SourceRelativePath));

                // 2) Source file exists + SHA-256 matches BeforeHash
                if (!File.Exists(srcFull))
                {
                    results.Add(Fail(op.Id, "source_missing",
                        $"Source file missing: {srcFull}"));
                    return results.ToArray(); // critical
                }

                string srcHash = HashFile(sha, srcFull);
                if (!string.Equals(srcHash, op.BeforeHash, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(Fail(op.Id, "source_hash_mismatch",
                        $"Source changed since plan: expected {op.BeforeHash}, got {srcHash}"));
                    return results.ToArray(); // critical
                }

                // 3) Staging file exists
                string stagingFile = Path.Combine(normStaging, op.OutputRelativePath);
                if (!File.Exists(stagingFile))
                {
                    results.Add(Fail(op.Id, "staging_missing",
                        $"Staging file not found: {stagingFile}"));
                    return results.ToArray(); // critical
                }

                // 4) No reparse points in staging path
                var info = new FileInfo(stagingFile);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    results.Add(Fail(op.Id, "staging_reparse",
                        "Staging file is a reparse point (symlink/junction)."));
                    return results.ToArray(); // critical
                }

                // 5) Staging hash matches PredictedAfterHash
                string stagingHash = HashFile(sha, stagingFile);
                bool hashOk = string.Equals(
                    stagingHash, op.PredictedAfterHash, StringComparison.OrdinalIgnoreCase);

                results.Add(new ValidationResult
                {
                    FileId = op.Id,
                    Passed = hashOk,
                    ActualHash = stagingHash,
                    ExpectedHash = op.PredictedAfterHash,
                    Message = hashOk ? null :
                        $"Hash mismatch: predicted {op.PredictedAfterHash}, got {stagingHash}"
                });

                // Non-critical: hash mismatch is reported but does NOT short-circuit
                // (caller decides policy via Passed flags)
            }

            return results.ToArray();
        }

        private static ValidationResult Fail(string fileId, string tag, string msg) =>
            new()
            {
                FileId = fileId,
                Passed = false,
                ActualHash = null,
                ExpectedHash = null,
                Message = $"[{tag}] {msg}"
            };

        private static string HashFile(SHA256 sha, string path)
        {
            using var fs = File.OpenRead(path);
            byte[] hash = sha.ComputeHash(fs);
            var sb = new StringBuilder(64);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}

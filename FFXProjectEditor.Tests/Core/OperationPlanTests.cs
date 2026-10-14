using System;
using FFXProjectEditor.Tests.Infrastructure;
using System.Collections.Generic;
using System.IO;
using FFXProjectEditor.Core;
using Xunit;

namespace FFXProjectEditor.Tests.Core
{
    public class OperationPlanTests : IDisposable
    {
        private readonly string _sourceDir;
        private readonly string _outputDir;
        private readonly string _stagingDir;
        private readonly string _backupDir;

        public OperationPlanTests()
        {
            var id = Guid.NewGuid().ToString("N")[..8];
            _sourceDir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", $"op_src_{id}");
            _outputDir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", $"op_out_{id}");
            _stagingDir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", $"op_stg_{id}");
            _backupDir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", $"op_bak_{id}");
            Directory.CreateDirectory(_sourceDir);
        }

        public void Dispose()
        {
            foreach (var dir in new[] { _sourceDir, _outputDir, _stagingDir, _backupDir })
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
        }

        [Fact]
        public void Execute_ValidateFails_WhenOutputInsideSource()
        {
            var plan = new OperationPlan
            {
                OperationId = "test-1",
                DisplayName = "Test",
                CreatedAt = DateTimeOffset.UtcNow,
                SourceRoot = _sourceDir,
                OutputRoot = Path.Combine(_sourceDir, "sub"), // inside source!
                StagingRoot = _stagingDir,
                BackupRoot = _backupDir,
                Operations = Array.Empty<FileOperation>(),
                Preconditions = Array.Empty<string>(),
                OwnerCapabilityId = "test"
            };

            var executor = new OperationExecutor();
            var result = executor.Execute(plan);

            Assert.False(result.Success);
            Assert.Contains("Path", result.ErrorMessage!);
        }

        [Fact]
        public void Execute_EmptyPlan_ReturnsSuccess()
        {
            var plan = new OperationPlan
            {
                OperationId = "test-2",
                DisplayName = "Empty",
                CreatedAt = DateTimeOffset.UtcNow,
                SourceRoot = _sourceDir,
                OutputRoot = _outputDir,
                StagingRoot = _stagingDir,
                BackupRoot = _backupDir,
                Operations = Array.Empty<FileOperation>(),
                Preconditions = Array.Empty<string>(),
                OwnerCapabilityId = "test"
            };

            var executor = new OperationExecutor();
            var result = executor.Execute(plan);

            Assert.True(result.Success);
            Assert.Equal(0, result.FilesWritten);
        }

        [Fact]
        public void ComputeSha256_ReturnsDeterministicHash()
        {
            var file = Path.Combine(_sourceDir, "test.bin");
            File.WriteAllBytes(file, new byte[] { 1, 2, 3, 4 });

            var hash1 = OperationExecutor.ComputeSha256(file);
            var hash2 = OperationExecutor.ComputeSha256(file);

            Assert.Equal(hash1, hash2);
            Assert.Equal(64, hash1.Length); // SHA-256 hex = 64 chars
        }

        [Fact]
        public void ComputeSha256_DifferentFiles_DifferentHashes()
        {
            var file1 = Path.Combine(_sourceDir, "a.bin");
            var file2 = Path.Combine(_sourceDir, "b.bin");
            File.WriteAllBytes(file1, new byte[] { 1, 2, 3 });
            File.WriteAllBytes(file2, new byte[] { 4, 5, 6 });

            Assert.NotEqual(
                OperationExecutor.ComputeSha256(file1),
                OperationExecutor.ComputeSha256(file2));
        }

        [Fact]
        public void OperationPlan_FileCount_ReturnsCorrect()
        {
            var plan = new OperationPlan
            {
                OperationId = "test-3",
                DisplayName = "Count",
                CreatedAt = DateTimeOffset.UtcNow,
                SourceRoot = _sourceDir,
                OutputRoot = _outputDir,
                StagingRoot = _stagingDir,
                BackupRoot = _backupDir,
                Operations = new[]
                {
                    CreateDummyOp("op-1"),
                    CreateDummyOp("op-2"),
                    CreateDummyOp("op-3")
                },
                Preconditions = Array.Empty<string>(),
                OwnerCapabilityId = "test"
            };

            Assert.Equal(3, plan.FileCount);
        }

        private static FileOperation CreateDummyOp(string id) => new()
        {
            Id = id,
            Kind = FileOperationKind.Overwrite,
            SourceRelativePath = "data.bin",
            OutputRelativePath = "data.bin",
            BeforeHash = "abc123",
            PredictedAfterHash = "def456",
            EstimatedBytes = 100,
            Description = "test",
            Diff = new FileDiffSummary
            {
                FieldsChanged = 1,
                ChangedFieldNames = new[] { "test" },
                HumanSummary = "test"
            },
            Risk = RiskLevel.Safe,
            Edits = new Dictionary<string, object>()
        };
    }
}

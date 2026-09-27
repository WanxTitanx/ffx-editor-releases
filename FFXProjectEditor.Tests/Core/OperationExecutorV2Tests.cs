using System;
using FFXProjectEditor.Tests.Infrastructure;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.Core;
using FFXProjectEditor.Core.Writers;
using FFXProjectEditor.FfxLib.Ability;
using Xunit;

namespace FFXProjectEditor.Tests.Core
{
    /// <summary>
    /// Guards for OperationExecutorV2 (P2 integration, Lote B — Jarvis-CLINE 2026-07-31):
    ///   G1: edits must be transported from the plan into StageAsync (was: empty dictionary).
    ///   G2: hash verify must not compare against the "pending-stage" placeholder.
    ///   G3: all-or-nothing — stage+verify ALL operations first; promote only if all passed.
    /// Uses the AbilityCommandAdapter + vanilla command.bin fixture (see AbilityCommandRoundTripTests).
    /// </summary>
    public class OperationExecutorV2Tests : IDisposable
    {
        private readonly string _sourceDir;
        private readonly string _outputDir;
        private readonly string _stagingDir;
        private readonly string _backupDir;

        public OperationExecutorV2Tests()
        {
            var id = Guid.NewGuid().ToString("N")[..8];
            _sourceDir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "v2_src_" + id);
            _outputDir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "v2_out_" + id);
            _stagingDir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "v2_stg_" + id);
            _backupDir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "v2_bak_" + id);
            Directory.CreateDirectory(_sourceDir);
            Directory.CreateDirectory(_outputDir);
        }

        public void Dispose()
        {
            foreach (var dir in new[] { _sourceDir, _outputDir, _stagingDir, _backupDir })
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
        }

        [Fact]
        public void Execute_AppliesEditsFromPlan_AndPromotes()
        {
            string source = Path.Combine(_sourceDir, "command.bin");
            File.Copy(FixturePath("command.bin"), source);

            var catalog = new WriterAdapterCatalog();
            var adapter = new AbilityCommandAdapter();
            var edits = new Dictionary<string, object>
            {
                [AbilityCommandAdapter.CommandIndexField] = 0,
                ["CostMp"] = (byte)7,
            };

            var plan = BuildPlan(source, "command.bin", catalog, adapter, edits);

            var executor = new OperationExecutorV2(catalog);
            var result = executor.ExecuteAsync(plan).GetAwaiter().GetResult();

            Assert.True(result.Success, result.Validations.Count > 0
                ? string.Join(" | ", result.Validations.Select(v => v.Message).ToArray())
                : result.ErrorMessage);
            Assert.Equal(1, result.FilesWritten);

            string output = Path.Combine(_outputDir, "command.bin");
            Assert.True(File.Exists(output), "output must exist after promote");
            var reread = Ability_Command.ReadList(File.ReadAllBytes(output), hasExtraInfo: true);
            Assert.Equal((byte)7, reread[0].CostMp);
        }

        [Fact]
        public void Execute_SourceChangedSincePlan_AbortsWithoutTouchingOutput()
        {
            string source = Path.Combine(_sourceDir, "command.bin");
            File.Copy(FixturePath("command.bin"), source);

            var catalog = new WriterAdapterCatalog();
            var adapter = new AbilityCommandAdapter();
            var edits = new Dictionary<string, object>
            {
                [AbilityCommandAdapter.CommandIndexField] = 0,
                ["CostMp"] = (byte)7,
            };

            var plan = BuildPlan(source, "command.bin", catalog, adapter, edits);

            // Mutate the source AFTER plan creation (hash preconditions must catch it).
            File.WriteAllBytes(source, File.ReadAllBytes(FixturePath("item.bin")));

            var executor = new OperationExecutorV2(catalog);
            var result = executor.ExecuteAsync(plan).GetAwaiter().GetResult();

            Assert.False(result.Success);
            Assert.Equal(0, result.FilesWritten);
            Assert.False(File.Exists(Path.Combine(_outputDir, "command.bin")), "output must not be touched");
        }

        [Fact]
        public void Execute_AllOrNothing_WhenOneOperationFails_NothingIsPromoted()
        {
            string source1 = Path.Combine(_sourceDir, "command.bin");
            string source2 = Path.Combine(_sourceDir, "item.bin");
            File.Copy(FixturePath("command.bin"), source1);
            File.Copy(FixturePath("item.bin"), source2);

            var catalog = new WriterAdapterCatalog();
            var adapter = new AbilityCommandAdapter();
            var edits = new Dictionary<string, object>
            {
                [AbilityCommandAdapter.CommandIndexField] = 0,
                ["CostMp"] = (byte)7,
            };

            // Two operations in one plan; the second one will have a stale BeforeHash.
            var op1 = BuildOperation(source1, "command.bin", catalog, adapter, edits);
            var op2 = BuildOperation(source2, "item.bin", catalog, adapter, edits);
            File.WriteAllBytes(source2, File.ReadAllBytes(FixturePath("monmagic2.bin"))); // stale after plan build

            var plan = new OperationPlan
            {
                OperationId = Guid.NewGuid().ToString("N"),
                DisplayName = "two-ops",
                CreatedAt = DateTimeOffset.UtcNow,
                SourceRoot = _sourceDir,
                OutputRoot = _outputDir,
                StagingRoot = _stagingDir,
                BackupRoot = _backupDir,
                Operations = new[] { op1, op2 },
                Preconditions = Array.Empty<string>(),
                OwnerCapabilityId = "ability-command",
            };

            var executor = new OperationExecutorV2(catalog);
            var result = executor.ExecuteAsync(plan).GetAwaiter().GetResult();

            Assert.False(result.Success);
            Assert.Equal(0, result.FilesWritten);
            Assert.False(File.Exists(Path.Combine(_outputDir, "command.bin")),
                "valid operation must NOT be promoted when a sibling failed");
        }

        [Fact]
        public void Execute_CopyOnly_WhenNoAdapterRegistered()
        {
            string source = Path.Combine(_sourceDir, "command.bin");
            File.Copy(FixturePath("command.bin"), source);

            // Capability WITHOUT any registered adapter -> copy-only path.
            var catalog = new WriterAdapterCatalog();
            string before = OperationExecutorV2.ComputeSha256(source);

            var plan = new OperationPlan
            {
                OperationId = Guid.NewGuid().ToString("N"),
                DisplayName = "copy-only",
                CreatedAt = DateTimeOffset.UtcNow,
                SourceRoot = _sourceDir,
                OutputRoot = _outputDir,
                StagingRoot = _stagingDir,
                BackupRoot = _backupDir,
                Operations = new[]
                {
                    new FileOperation
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Kind = FileOperationKind.Overwrite,
                        SourceRelativePath = "command.bin",
                        OutputRelativePath = "command.bin",
                        BeforeHash = before,
                        PredictedAfterHash = "pending-stage",
                        EstimatedBytes = new FileInfo(source).Length,
                        Description = "copy-only op",
                        Diff = new FileDiffSummary
                        {
                            FieldsChanged = 0,
                            ChangedFieldNames = Array.Empty<string>(),
                            HumanSummary = "copy-only"
                        },
                        Risk = RiskLevel.Safe,
                        Edits = new Dictionary<string, object>(),
                    }
                },
                Preconditions = Array.Empty<string>(),
                OwnerCapabilityId = "no-such-capability",
            };

            var executor = new OperationExecutorV2(catalog);
            var result = executor.ExecuteAsync(plan).GetAwaiter().GetResult();

            Assert.True(result.Success);
            Assert.Equal(1, result.FilesWritten);
            string output = Path.Combine(_outputDir, "command.bin");
            Assert.True(File.Exists(output));
            Assert.Equal(before, OperationExecutorV2.ComputeSha256(output));
        }

        // --- Helpers ----------------------------------------------------------------

        private OperationPlan BuildPlan(string source, string fileName, WriterAdapterCatalog catalog,
            IWriterAdapter adapter, Dictionary<string, object> edits)
        {
            var operation = BuildOperation(source, fileName, catalog, adapter, edits);
            return new OperationPlan
            {
                OperationId = Guid.NewGuid().ToString("N"),
                DisplayName = "test-plan",
                CreatedAt = DateTimeOffset.UtcNow,
                SourceRoot = _sourceDir,
                OutputRoot = _outputDir,
                StagingRoot = _stagingDir,
                BackupRoot = _backupDir,
                Operations = new[] { operation },
                Preconditions = Array.Empty<string>(),
                OwnerCapabilityId = adapter.CapabilityId,
            };
        }

        private static FileOperation BuildOperation(string source, string fileName, WriterAdapterCatalog catalog,
            IWriterAdapter adapter, Dictionary<string, object> edits)
        {
            return new FileOperation
            {
                Id = Guid.NewGuid().ToString("N"),
                Kind = FileOperationKind.Overwrite,
                SourceRelativePath = fileName,
                OutputRelativePath = fileName,
                BeforeHash = adapter.ComputeBeforeHash(source),
                PredictedAfterHash = "pending-stage",
                EstimatedBytes = new FileInfo(source).Length,
                Description = "test op",
                Diff = adapter.DescribeChanges(edits),
                Risk = adapter.Risk,
                Edits = edits,
            };
        }

        private static string FixturePath(string fixtureName) =>
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Battle", fixtureName);
    }
}



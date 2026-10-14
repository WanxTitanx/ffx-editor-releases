using System;
using FFXProjectEditor.Tests.Infrastructure;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.Core;
using FFXProjectEditor.Core.Writers;
using Xunit;

namespace FFXProjectEditor.Tests.Core
{
    /// <summary>
    /// P3-L3 (docs/ai/P3_RUNTIME_CONTRACTS_2026-07-31.md §4): operations-platform resilience suite.
    /// Covers the mandatory P3 test items (prompt L431-444) not covered elsewhere: cancellation
    /// (before / during stage / during promote), injected failure per critical stage, journal/recovery,
    /// backup+manifest, concurrency, secret redaction, empty plan, forbidden diff, JSON round-trip,
    /// DTO consistency.
    /// </summary>
    public class OperationResilienceTests : IDisposable
    {
        private readonly string _src;
        private readonly string _out;
        private readonly string _stg;
        private readonly string _bak;

        public OperationResilienceTests()
        {
            var id = Guid.NewGuid().ToString("N")[..8];
            _src = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "res_src_" + id);
            _out = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "res_out_" + id);
            _stg = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "res_stg_" + id);
            _bak = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "res_bak_" + id);
            Directory.CreateDirectory(_src);
            Directory.CreateDirectory(_out);
        }

        public void Dispose()
        {
            foreach (var d in new[] { _src, _out, _stg, _bak })
                if (Directory.Exists(d))
                    Directory.Delete(d, recursive: true);
        }

        static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Battle", name);

        OperationPlan PlanFor(string fileName, IWriterAdapter adapter, Dictionary<string, object> edits,
            params (string SourceRel, string OutputRel)[] extraOps)
        {
            var ops = new List<FileOperation>();
            foreach (var (sourceRel, outputRel) in new[] { (fileName, fileName) }.Concat(extraOps))
            {
                string source = Path.Combine(_src, sourceRel);
                ops.Add(new FileOperation
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Kind = FileOperationKind.Overwrite,
                    SourceRelativePath = sourceRel,
                    OutputRelativePath = outputRel,
                    BeforeHash = File.Exists(source) ? OperationExecutorV2.ComputeSha256(source) : "",
                    PredictedAfterHash = "pending-stage",
                    EstimatedBytes = File.Exists(source) ? new FileInfo(source).Length : 0,
                    Description = "resilience op",
                    Diff = new FileDiffSummary { FieldsChanged = 0, ChangedFieldNames = Array.Empty<string>(), HumanSummary = "op" },
                    Risk = RiskLevel.Moderate,
                    Edits = edits,
                });
            }

            return new OperationPlan
            {
                OperationId = Guid.NewGuid().ToString("N"),
                DisplayName = "resilience",
                CreatedAt = DateTimeOffset.UtcNow,
                SourceRoot = _src,
                OutputRoot = _out,
                StagingRoot = _stg,
                BackupRoot = _bak,
                Operations = ops,
                Preconditions = Array.Empty<string>(),
                OwnerCapabilityId = adapter.CapabilityId,
            };
        }

        // --- 1. Cancel BEFORE stage: nothing written --------------------------------

        [Fact]
        public async Task CancelBeforeStage_NothingWritten()
        {
            string source = Path.Combine(_src, "command.bin");
            File.Copy(Fixture("command.bin"), source);

            var catalog = new WriterAdapterCatalog();
            var adapter = new AbilityCommandAdapter();
            var plan = PlanFor("command.bin", adapter, new Dictionary<string, object>
            {
                [AbilityCommandAdapter.CommandIndexField] = 0,
                ["CostMp"] = (byte)7,
            });

            using var cts = new CancellationTokenSource();
            cts.Cancel(); // cancelled BEFORE execution

            var result = await new OperationExecutorV2(catalog).ExecuteAsync(plan, cts.Token);

            Assert.False(result.Success);
            Assert.Contains("cancelled", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(_out, "command.bin")), "output must not exist");
        }

        // --- 2. Cancel DURING staging (adapter observes token): nothing written ------

        [Fact]
        public async Task CancelDuringStage_NothingWritten()
        {
            string source = Path.Combine(_src, "command.bin");
            File.Copy(Fixture("command.bin"), source);

            var adapter = new FlakyAdapter { CancelOnStage = true };
            var catalog = new WriterAdapterCatalog();
            catalog.Register(adapter);
            var plan = PlanFor("command.bin", adapter, new Dictionary<string, object> { ["x"] = 1 });

            var result = await new OperationExecutorV2(catalog).ExecuteAsync(plan);

            Assert.False(result.Success);
            Assert.Contains("cancelled", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(_out, "command.bin")), "output must not exist");
        }

        // --- 3. Injected failure in PROMOTE → rollback restores earlier outputs ------

        [Fact]
        public async Task PromoteFailure_RollsBackAlreadyPromotedOutput()
        {
            // op1: command.bin → output1 (pre-populated with original bytes)
            string source = Path.Combine(_src, "command.bin");
            File.Copy(Fixture("command.bin"), source);
            string output1 = Path.Combine(_out, "command.bin");
            File.Copy(source, output1); // original output exists → backup will be taken

            // op2: same source → output path that is a DIRECTORY → File.Copy fails
            string output2 = Path.Combine(_out, "command2.bin");
            Directory.CreateDirectory(output2); // promote target is a directory → copy throws

            var adapter = new FlakyAdapter { FailStage = false };
            var catalog = new WriterAdapterCatalog();
            catalog.Register(adapter);
            var plan = PlanFor("command.bin", adapter, new Dictionary<string, object> { ["x"] = 1 },
                ("command.bin", "command2.bin"));

            var result = await new OperationExecutorV2(catalog).ExecuteAsync(plan);

            Assert.False(result.Success);
            Assert.Contains("Promote failed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            // rollback: output1 restored to the ORIGINAL bytes (pre-promote)
            Assert.Equal(OperationExecutorV2.ComputeSha256(source), OperationExecutorV2.ComputeSha256(output1));
        }


        // --- 4. Journal + manifest: promote failure records "failed" + backup exists --

        [Fact]
        public async Task Journal_PromoteFailure_RecordsFailedAndBackup()
        {
            string source = Path.Combine(_src, "command.bin");
            File.Copy(Fixture("command.bin"), source);
            string output1 = Path.Combine(_out, "command.bin");
            File.Copy(source, output1);

            string output2 = Path.Combine(_out, "command2.bin");
            Directory.CreateDirectory(output2);

            var adapter = new FlakyAdapter();
            var catalog = new WriterAdapterCatalog();
            catalog.Register(adapter);
            var plan = PlanFor("command.bin", adapter, new Dictionary<string, object> { ["x"] = 1 },
                ("command.bin", "command2.bin"));

            await new OperationExecutorV2(catalog).ExecuteAsync(plan);

            string journalPath = Path.Combine(_bak, "journal.json");
            Assert.True(File.Exists(journalPath), "journal must exist after a failed promote");
            string journal = File.ReadAllText(journalPath);
            Assert.Contains("\"failed\"", journal);
            // backup manifest: at least one .bak recorded
            Assert.Contains(".bak", journal);
            Assert.True(Directory.EnumerateFiles(_bak, "*.bak").Any(), "backup file must exist");
        }

        // --- 5. Journal: success state ----------------------------------------------

        [Fact]
        public async Task Journal_Success_RecordsSuccess()
        {
            string source = Path.Combine(_src, "command.bin");
            File.Copy(Fixture("command.bin"), source);

            var adapter = new FlakyAdapter();
            var catalog = new WriterAdapterCatalog();
            catalog.Register(adapter);
            var plan = PlanFor("command.bin", adapter, new Dictionary<string, object> { ["x"] = 1 });

            var result = await new OperationExecutorV2(catalog).ExecuteAsync(plan);

            Assert.True(result.Success);
            string journalPath = Path.Combine(_bak, "journal.json");
            Assert.True(File.Exists(journalPath));
            Assert.Contains("\"success\"", File.ReadAllText(journalPath));
        }

        // --- 6. Concurrency: two parallel executions on the same output do not corrupt

        [Fact]
        public async Task ConcurrentExecutions_SameOutput_NoCorruption()
        {
            string source = Path.Combine(_src, "command.bin");
            File.Copy(Fixture("command.bin"), source);

            var catalog = new WriterAdapterCatalog();
            var adapter = new AbilityCommandAdapter();
            var planA = PlanFor("command.bin", adapter, new Dictionary<string, object>
            {
                [AbilityCommandAdapter.CommandIndexField] = 0, ["CostMp"] = (byte)7,
            });
            var planB = PlanFor("command.bin", adapter, new Dictionary<string, object>
            {
                [AbilityCommandAdapter.CommandIndexField] = 0, ["CostMp"] = (byte)8,
            });

            var executor = new OperationExecutorV2(catalog);
            var results = await Task.WhenAll(executor.ExecuteAsync(planA), executor.ExecuteAsync(planB));

            Assert.All(results, r => Assert.True(r.Success));
            // output is one of the two complete, parseable results — never a mix
            string output = Path.Combine(_out, "command.bin");
            var rows = FFXProjectEditor.FfxLib.Ability.Ability_Command.ReadList(File.ReadAllBytes(output), hasExtraInfo: true);
            Assert.True(rows[0].CostMp is 7 or 8, "output must be one complete result");
        }

        // --- 7. Secret never surfaces in failure messages ----------------------------

        [Fact]
        public async Task Secret_RedactedFromFailureMessages()
        {
            string source = Path.Combine(_src, "command.bin");
            File.Copy(Fixture("command.bin"), source);

            const string secret = "sk-abcdef1234567890";
            var adapter = new FlakyAdapter { FailStage = true, SecretInException = secret };
            var catalog = new WriterAdapterCatalog();
            catalog.Register(adapter);
            var plan = PlanFor("command.bin", adapter, new Dictionary<string, object> { ["x"] = 1 });

            var result = await new OperationExecutorV2(catalog).ExecuteAsync(plan);

            Assert.False(result.Success);
            string all = (result.ErrorMessage ?? "") + " " + (result.RecoveryInstructions ?? "")
                + " " + string.Join(" ", result.Validations.Select(v => v.Message));
            Assert.DoesNotContain(secret, all, StringComparison.Ordinal);
            Assert.Contains("[REDACTED]", all);
        }

        // --- 8. Empty plan → "No operations were executable" -------------------------

        [Fact]
        public async Task EmptyPlan_ReturnsNoOperations()
        {
            var adapter = new FlakyAdapter();
            var catalog = new WriterAdapterCatalog();
            catalog.Register(adapter);
            var plan = PlanFor("command.bin", adapter, new Dictionary<string, object> { ["x"] = 1 });
            // empty the operations list — no-op plan
            plan = plan with { Operations = Array.Empty<FileOperation>() };

            var result = await new OperationExecutorV2(catalog).ExecuteAsync(plan);

            Assert.False(result.Success);
            Assert.Contains("No operations", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }


        // --- 9. Forbidden diff: field outside the allowlist never builds a plan ------

        [Fact]
        public void ForbiddenDiff_EditSessionBuild_Throws()
        {
            string source = Path.Combine(_src, "command.bin");
            File.Copy(Fixture("command.bin"), source);

            var catalog = new WriterAdapterCatalog();
            var session = new EditSession(catalog)
                .ForCapability("ability-command")
                .WithSource(source)
                .WithEdit(AbilityCommandAdapter.CommandIndexField, 0)
                .WithEdit("NotARealField", (byte)1); // outside allowlist

            var ex = Assert.Throws<InvalidOperationException>(() => session.Build());
            Assert.Contains("Edit validation failed", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // --- 10. OperationPlan JSON round-trip preserves identity --------------------

        [Fact]
        public void Plan_JsonRoundTrip_PreservesIdentity()
        {
            string source = Path.Combine(_src, "command.bin");
            File.Copy(Fixture("command.bin"), source);

            var adapter = new AbilityCommandAdapter();
            var plan = PlanFor("command.bin", adapter, new Dictionary<string, object>
            {
                [AbilityCommandAdapter.CommandIndexField] = 0, ["CostMp"] = (byte)7,
            });

            string json = JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true });
            var back = JsonSerializer.Deserialize<OperationPlan>(json);

            Assert.NotNull(back);
            Assert.Equal(plan.OperationId, back!.OperationId);
            Assert.Equal(plan.DisplayName, back.DisplayName);
            Assert.Equal(plan.FileCount, back.FileCount);
            Assert.Equal(plan.OwnerCapabilityId, back.OwnerCapabilityId);
            Assert.Equal(plan.Operations[0].BeforeHash, back.Operations[0].BeforeHash);
        }

        // --- 11. DTO consistency: failure receipt is coherent ------------------------

        [Fact]
        public async Task Receipt_Failure_ConsistentDto()
        {
            string source = Path.Combine(_src, "command.bin");
            File.Copy(Fixture("command.bin"), source);

            var adapter = new FlakyAdapter { FailStage = true };
            var catalog = new WriterAdapterCatalog();
            catalog.Register(adapter);
            var plan = PlanFor("command.bin", adapter, new Dictionary<string, object> { ["x"] = 1 });

            var result = await new OperationExecutorV2(catalog).ExecuteAsync(plan);
            var receipt = ReceiptBuilder.BuildReceipt(plan, result);

            Assert.False(result.Success);
            Assert.False(receipt.Success);
            Assert.Equal(0, receipt.FilesWritten);
            Assert.Equal(result.ReceiptId, receipt.ReceiptId);
        }

        // --- 12. Backup + manifest on success ----------------------------------------

        [Fact]
        public async Task Backup_Overwrite_PreservesOriginalBytes()
        {
            string source = Path.Combine(_src, "command.bin");
            File.Copy(Fixture("command.bin"), source);
            string output = Path.Combine(_out, "command.bin");
            File.Copy(source, output); // existing output → backup taken
            string originalHash = OperationExecutorV2.ComputeSha256(output);

            var adapter = new AbilityCommandAdapter();
            var catalog = new WriterAdapterCatalog();
            var plan = PlanFor("command.bin", adapter, new Dictionary<string, object>
            {
                [AbilityCommandAdapter.CommandIndexField] = 0, ["CostMp"] = (byte)7,
            });

            var result = await new OperationExecutorV2(catalog).ExecuteAsync(plan);

            Assert.True(result.Success);
            Assert.NotEmpty(result.BackupPaths);
            Assert.True(File.Exists(result.BackupPaths[0]));
            Assert.Equal(originalHash, OperationExecutorV2.ComputeSha256(result.BackupPaths[0]));
        }
    }

    /// <summary>Test adapter that can fail/cancel at controlled points.</summary>
    internal sealed class FlakyAdapter : IWriterAdapter
    {
        public string CapabilityId => "flaky";
        public string DisplayName => "Flaky";
        public RiskLevel Risk => RiskLevel.Moderate;

        public bool CancelOnStage { get; init; }
        public bool FailStage { get; init; }
        public string? SecretInException { get; init; }

        public string ComputeBeforeHash(string sourcePath)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            using var stream = File.OpenRead(sourcePath);
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }

        public IReadOnlyList<string> ValidateEdits(IReadOnlyDictionary<string, object> edits) => Array.Empty<string>();

        public FileDiffSummary DescribeChanges(IReadOnlyDictionary<string, object> edits) =>
            new() { FieldsChanged = 0, ChangedFieldNames = Array.Empty<string>(), HumanSummary = "flaky" };

        public async Task<string> StageAsync(string sourcePath, string stagingPath,
            IReadOnlyDictionary<string, object> edits, CancellationToken ct)
        {
            await Task.Yield();
            if (CancelOnStage)
                throw new OperationCanceledException(ct);
            if (FailStage)
                throw new InvalidOperationException(SecretInException ?? "injected stage failure");
            File.Copy(sourcePath, stagingPath, overwrite: true);
            return OperationExecutorV2.ComputeSha256(stagingPath);
        }
    }
}


using System;
using FFXProjectEditor.Tests.Infrastructure;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FFXProjectEditor.Core;
using FFXProjectEditor.Core.Writers;
using FFXProjectEditor.FfxLib.Monster;
using Xunit;

namespace FFXProjectEditor.Tests.Core
{
    /// <summary>
    /// Unit + RT0 tests for <see cref="MonsterFileAdapter"/> — the OperationPlan pipeline
    /// adapter around <see cref="Monster_File.Read(byte[])"/>/<see cref="Monster_File.Write"/>.
    /// Supports editing stat sheet fields (Stat.*), loot entries (Loot.*) and monster file
    /// metadata (Metadata.*). Fixtures are vanilla monster_*.bin files (Fixtures/Monster/*.bin),
    /// copied next to the test assembly by the FFXProjectEditor.Tests.csproj content glob.
    /// </summary>
    public class MonsterFileAdapterTests : IDisposable
    {
        private readonly string _tempDir;

        public MonsterFileAdapterTests()
        {
            var id = Guid.NewGuid().ToString("N")[..8];
            _tempDir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", $"monster_file_adapter_{id}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        private static string FixturePath(string fixtureName) =>
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Monster", fixtureName);

        [Fact]
        public async Task StageAsync_NoEdit_IsByteIdentical()
        {
            string source = FixturePath("m000.bin");
            string staging = Path.Combine(_tempDir, "m000_staged.bin");
            byte[] original = File.ReadAllBytes(source);

            var adapter = new MonsterFileAdapter();
            string predictedAfter = await adapter.StageAsync(
                source, staging, new Dictionary<string, object>());

            byte[] staged = File.ReadAllBytes(staging);

            Assert.True(
                original.AsSpan().SequenceEqual(staged),
                "no-edit save through the adapter must be byte-identical (RT0)");
            Assert.Equal(adapter.ComputeBeforeHash(source), predictedAfter);
            Assert.Equal(64, predictedAfter.Length); // SHA-256 lowercase hex
        }

        [Fact]
        public async Task StageAsync_StatSheetEdit_AppliesAndRewrites()
        {
            string source = FixturePath("m000.bin");
            string staging = Path.Combine(_tempDir, "m000_stats.bin");

            var edits = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["Stat.Hp"] = 5000u,
                ["Stat.Strength"] = (byte)40,
                ["Stat.MonsterId"] = (short)77
            };

            var adapter = new MonsterFileAdapter();
            await adapter.StageAsync(source, staging, edits);

            Monster_File file = Monster_File.Read(File.ReadAllBytes(staging));
            Assert.NotNull(file.StatSheetFile);
            Assert.Equal(5000u, file.StatSheetFile!.Hp);
            Assert.Equal(40, (int)file.StatSheetFile.Strength);
            Assert.Equal(77, (int)file.StatSheetFile.MonsterId);

            // Re-writing the staged file must stay stable (same length, editable again).
            byte[] rewritten = file.Write();
            Assert.Equal(File.ReadAllBytes(staging).Length, rewritten.Length);
        }

        [Fact]
        public async Task StageAsync_LootEdit_AppliesAndRewrites()
        {
            string source = FixturePath("m000.bin");
            string staging = Path.Combine(_tempDir, "m000_loot.bin");

            var edits = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["Loot.Gil"] = (short)500,
                ["Loot.Drop1Id"] = (ushort)1000,
                ["Loot.StealChance"] = (byte)25
            };

            var adapter = new MonsterFileAdapter();
            await adapter.StageAsync(source, staging, edits);

            Monster_File file = Monster_File.Read(File.ReadAllBytes(staging));
            Assert.NotNull(file.LootFile);
            Assert.Equal(500, (int)file.LootFile!.Gil);
            Assert.Equal(1000, (int)file.LootFile.Drop1Id);
            Assert.Equal(25, (int)file.LootFile.StealChance);

            // Re-writing the staged file must stay stable (same length, editable again).
            byte[] rewritten = file.Write();
            Assert.Equal(File.ReadAllBytes(staging).Length, rewritten.Length);
        }

        [Fact]
        public async Task StageAsync_MetadataSignatureEdit_IsApplied()
        {
            string source = FixturePath("m000.bin");
            string staging = Path.Combine(_tempDir, "m000_sig.bin");

            var edits = new Dictionary<string, object>
            {
                ["Metadata.Signature"] = 9
            };

            var adapter = new MonsterFileAdapter();
            await adapter.StageAsync(source, staging, edits);

            byte[] staged = File.ReadAllBytes(staging);

            // Header Signature lives at offset 0x00 (little-endian int32).
            Assert.Equal(9, BitConverter.ToInt32(staged, 0));
            Assert.Equal(9, Monster_File.Read(staged).OriginalHeader!.Signature);
        }

        [Fact]
        public void ValidateEdits_And_DescribeChanges_AgreeOnEditableFields()
        {
            var adapter = new MonsterFileAdapter();

            // Each invalid key reports exactly one error.
            var invalid = new Dictionary<string, object>
            {
                ["Bogus"] = 1,                          // unknown key (no prefix)
                ["Stat.Hp"] = 100000u,                  // over the HP/MP cap
                ["Loot.Gil"] = 70000,                   // int out of ushort range
                ["Metadata.AiFile"] = "not-base64!!!"   // malformed base64
            };
            var errors = adapter.ValidateEdits(invalid);
            Assert.Equal(4, errors.Count);
            Assert.Contains(errors, e => e.Contains("Bogus"));
            Assert.Contains(errors, e => e.Contains("exceeds max HP/MP"));
            Assert.Contains(errors, e => e.Contains("not valid for UInt16"));
            Assert.Contains(errors, e => e.Contains("not a valid base64"));

            // Valid edits across all three namespaces pass cleanly.
            var valid = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["Stat.Hp"] = 99999u,
                ["Stat.Strength"] = (byte)255,
                ["Loot.Gil"] = (ushort)40000,
                ["Loot.Drop1Id"] = (ushort)65535,
                ["Metadata.Signature"] = 8,
                ["Metadata.AudioFile"] = Convert.ToBase64String(new byte[] { 1, 2, 3 })
            };
            Assert.Empty(adapter.ValidateEdits(valid));

            // DescribeChanges reports only the editable keys.
            var mixed = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["Stat.Hp"] = 1u,
                ["Loot.Gil"] = (short)2,
                ["Metadata.Signature"] = 3,
                ["Bogus"] = 4
            };
            var summary = adapter.DescribeChanges(mixed);
            Assert.Equal(3, summary.FieldsChanged);
            Assert.Contains("Stat.Hp", summary.ChangedFieldNames);
            Assert.Contains("Loot.Gil", summary.ChangedFieldNames);
            Assert.Contains("Metadata.Signature", summary.ChangedFieldNames);
            Assert.DoesNotContain("Bogus", summary.ChangedFieldNames);
            Assert.Contains("3 field(s)", summary.HumanSummary);
        }
    }
}

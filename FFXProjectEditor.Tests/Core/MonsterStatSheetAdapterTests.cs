using System;
using FFXProjectEditor.Tests.Infrastructure;
using System.Collections.Generic;
using System.IO;
using FFXProjectEditor.Core;
using FFXProjectEditor.Core.Writers;
using Xunit;

namespace FFXProjectEditor.Tests.Core
{
    /// <summary>
    /// Unit tests for <see cref="MonsterStatSheetAdapter"/> (the OperationPlan pipeline
    /// adapter around Monster_StatSheet.WriteSingle()).
    /// </summary>
    public class MonsterStatSheetAdapterTests : IDisposable
    {
        private readonly string _tempDir;

        public MonsterStatSheetAdapterTests()
        {
            var id = Guid.NewGuid().ToString("N")[..8];
            _tempDir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", $"monster_adapter_{id}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        [Fact]
        public void ComputeBeforeHash_ReturnsDeterministicHash()
        {
            var file = Path.Combine(_tempDir, "stats.bin");
            File.WriteAllBytes(file, new byte[] { 1, 2, 3, 4 });

            var adapter = new MonsterStatSheetAdapter();
            var hash1 = adapter.ComputeBeforeHash(file);
            var hash2 = adapter.ComputeBeforeHash(file);

            Assert.Equal(hash1, hash2); // deterministic across calls
            Assert.Equal(64, hash1.Length); // SHA-256 hex = 64 chars
            Assert.Equal(hash1, hash1.ToLowerInvariant());

            // Pin the exact value: SHA-256 (lowercase hex) of the bytes 01 02 03 04.
            // This proves the hash is the SHA-256 of the file content, not just stable.
            Assert.Equal("9f64a747e1b97f131fabb6b447296c9b6f0201e79fb3c5356e6c77e89b6a806a", hash1);
        }

        [Fact]
        public void DescribeChanges_ReturnsCorrectFields()
        {
            var edits = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["Hp"] = 5000u,
                ["Strength"] = (byte)40,
                ["NotAField"] = 1
            };

            var summary = new MonsterStatSheetAdapter().DescribeChanges(edits);

            Assert.Equal(2, summary.FieldsChanged);
            Assert.Contains("Hp", summary.ChangedFieldNames);
            Assert.Contains("Strength", summary.ChangedFieldNames);
            Assert.DoesNotContain("NotAField", summary.ChangedFieldNames);
            Assert.Contains("2 field(s)", summary.HumanSummary);
            Assert.Contains("Hp", summary.HumanSummary);
        }

        [Fact]
        public void ValidateEdits_RejectsUnknownField()
        {
            var edits = new Dictionary<string, object>
            {
                ["BogusField"] = 1
            };

            var errors = new MonsterStatSheetAdapter().ValidateEdits(edits);

            var error = Assert.Single(errors);
            Assert.Contains("BogusField", error);
            Assert.Contains("not editable", error);
        }

        [Fact]
        public void ValidateEdits_RejectsOversizedHp()
        {
            // The adapter's HP/MP cap check only fires for uint values (> 99999).
            var edits = new Dictionary<string, object>
            {
                ["Hp"] = 100000u
            };

            var errors = new MonsterStatSheetAdapter().ValidateEdits(edits);

            var error = Assert.Single(errors);
            Assert.Contains("exceeds max HP/MP", error);
        }

        [Fact]
        public void ValidateEdits_AcceptsValidStats()
        {
            var edits = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["Hp"] = 99999u,
                ["Mp"] = 999u,
                ["HpOverkill"] = 5000u,
                ["Strength"] = (byte)255,
                ["Defense"] = (byte)200,
                ["Magic"] = (byte)100,
                ["MagicDefense"] = (byte)100,
                ["Agility"] = (byte)90,
                ["Luck"] = (byte)80,
                ["Evasion"] = (byte)70,
                ["Accuracy"] = (byte)60,
                ["PoisonDamage"] = (byte)50,
                ["MonsterId"] = (short)1,
                ["ModelId"] = (short)2,
                ["CtbIconId"] = (byte)3,
                ["DoomCount"] = (sbyte)4
            };

            var errors = new MonsterStatSheetAdapter().ValidateEdits(edits);

            Assert.Empty(errors);
        }
    }
}

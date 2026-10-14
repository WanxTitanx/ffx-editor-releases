using System;
using FFXProjectEditor.Tests.Infrastructure;
using System.IO;
using FFXProjectEditor.Core;
using FFXProjectEditor.Core.Writers;
using Xunit;

namespace FFXProjectEditor.Tests.Core
{
    public class MonsterEditSessionIntegrationTests : IDisposable
    {
        private readonly string _tempDir;

        public MonsterEditSessionIntegrationTests()
        {
            _tempDir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", $"monster_int_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        [Fact]
        public void Adapter_ComputeBeforeHash_ReturnsDeterministic()
        {
            var file = Path.Combine(_tempDir, "monster.bin");
            File.WriteAllBytes(file, new byte[] { 0x4D, 0x4F, 0x4E, 0x00 });
            var adapter = new MonsterStatSheetAdapter();

            var h1 = adapter.ComputeBeforeHash(file);
            var h2 = adapter.ComputeBeforeHash(file);

            Assert.Equal(h1, h2);
            Assert.Equal(64, h1.Length);
        }

        [Fact]
        public void Adapter_DescribeChanges_ReturnsFields()
        {
            var adapter = new MonsterStatSheetAdapter();
            var edits = new System.Collections.Generic.Dictionary<string, object>
            {
                ["Hp"] = 99999u,
                ["Strength"] = (byte)255
            };

            var diff = adapter.DescribeChanges(edits);
            Assert.Equal(2, diff.FieldsChanged);
            Assert.Contains("Hp", diff.ChangedFieldNames);
        }

        [Fact]
        public void Adapter_ValidateEdits_RejectsUnknownField()
        {
            var adapter = new MonsterStatSheetAdapter();
            var edits = new System.Collections.Generic.Dictionary<string, object>
            {
                ["_internal"] = "nope"
            };

            var errors = adapter.ValidateEdits(edits);
            Assert.NotEmpty(errors);
        }

        [Fact]
        public void Adapter_ValidateEdits_RejectsOversizedHp()
        {
            var adapter = new MonsterStatSheetAdapter();
            var edits = new System.Collections.Generic.Dictionary<string, object>
            {
                ["Hp"] = 999999u
            };

            var errors = adapter.ValidateEdits(edits);
            Assert.NotEmpty(errors);
        }

        [Fact]
        public void Adapter_Risk_IsModerate()
        {
            var adapter = new MonsterStatSheetAdapter();
            Assert.Equal(RiskLevel.Moderate, adapter.Risk);
        }
    }
}

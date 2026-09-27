using System;
using FFXProjectEditor.Tests.Infrastructure;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using FFXProjectEditor.Core;
using FFXProjectEditor.Core.Writers;
using Xunit;

namespace FFXProjectEditor.Tests.Core
{
    /// <summary>
    /// Integration tests for TreasureAdapter against the IWriterAdapter contract.
    /// Each test creates a temp directory with a synthetic takara.bin so no
    /// game installation is required.
    /// </summary>
    public class TreasureEditSessionIntegrationTests : IDisposable
    {
        private readonly string _tempDir;

        public TreasureEditSessionIntegrationTests()
        {
            _tempDir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", $"ffx-treasure-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        /// <summary>
        /// Build a minimal valid takara.bin: 0x14 header + 498 entries of 4 bytes.
        /// Same shape as TreasureRoundTripTests.BuildKnownTakaraBin().
        /// </summary>
        private static byte[] BuildMinimalTakaraBin()
        {
            const int headerLen = 0x14;
            const int entryCount = 0x01F2; // 498
            const int entrySize = 4;
            const int dataLen = entryCount * entrySize; // 0x07C8
            byte[] bytes = new byte[headerLen + dataLen]; // 0x07DC

            // Header
            bytes[0x00] = 0x01;
            bytes[0x08] = 0x00; bytes[0x09] = 0x00; // MinIndex
            bytes[0x0A] = 0xF1; bytes[0x0B] = 0x01; // MaxIndex = 0x01F1
            bytes[0x0C] = 0x04; bytes[0x0D] = 0x00; // EntryLength
            bytes[0x0E] = 0xC8; bytes[0x0F] = 0x07; // TotalDataLength = 0x07C8
            bytes[0x10] = 0x14; bytes[0x11] = 0x00; bytes[0x12] = 0x00; bytes[0x13] = 0x00;

            // Fill entries with recognizable pattern
            for (int i = 0; i < entryCount; i++)
            {
                int off = headerLen + (i * entrySize);
                bytes[off + 0] = (byte)(0xA0 + i);          // Kind
                bytes[off + 1] = (byte)((i * 5) + 1);       // Quantity
                bytes[off + 2] = (byte)(0x00 + (i * 3));     // Type low
                bytes[off + 3] = (byte)(0x80 + ((i * 3) >> 8)); // Type high
            }

            return bytes;
        }

        private string WriteTempTakaraBin()
        {
            string path = Path.Combine(_tempDir, "takara.bin");
            File.WriteAllBytes(path, BuildMinimalTakaraBin());
            return path;
        }

        // ---------------------------------------------------------------
        // 1. ComputeBeforeHash returns a 64-char lowercase hex SHA-256
        // ---------------------------------------------------------------

        [Fact]
        public void TreasureAdapter_ComputeBeforeHash_ReturnsSha256()
        {
            var adapter = new TreasureAdapter();
            string path = WriteTempTakaraBin();

            string hash = adapter.ComputeBeforeHash(path);

            // SHA-256 hex = exactly 64 lowercase hex characters
            Assert.Equal(64, hash.Length);
            Assert.Matches("^[0-9a-f]{64}$", hash);

            // Hash must be deterministic
            string hash2 = adapter.ComputeBeforeHash(path);
            Assert.Equal(hash, hash2);
        }

        // ---------------------------------------------------------------
        // 2. DescribeChanges returns correct field names for Kind/Quantity edits
        // ---------------------------------------------------------------

        [Fact]
        public void TreasureAdapter_DescribeChanges_ReturnsCorrectFields()
        {
            var adapter = new TreasureAdapter();
            var edits = new Dictionary<string, object>
            {
                { "Kind", (byte)0x42 },
                { "Quantity", (byte)0x0A }
            };

            FileDiffSummary summary = adapter.DescribeChanges(edits);

            Assert.Equal(2, summary.FieldsChanged);
            Assert.Contains("Kind", summary.ChangedFieldNames);
            Assert.Contains("Quantity", summary.ChangedFieldNames);
            Assert.Contains("2 field(s)", summary.HumanSummary);
        }

        // ---------------------------------------------------------------
        // 3. ValidateEdits rejects unknown fields (non-editable)
        // ---------------------------------------------------------------

        [Fact]
        public void TreasureAdapter_ValidateEdits_RejectsUnknownField()
        {
            var adapter = new TreasureAdapter();
            var edits = new Dictionary<string, object>
            {
                { "_internal", "something" },  // underscore-prefixed — skipped, not an error
                { "BogusField", 123 }           // real unknown — should error
            };

            IReadOnlyList<string> errors = adapter.ValidateEdits(edits);

            Assert.Single(errors);
            Assert.Contains("BogusField", errors[0]);
            Assert.Contains("not editable", errors[0]);
        }

        // ---------------------------------------------------------------
        // 4. ValidateEdits accepts valid editable fields
        // ---------------------------------------------------------------

        [Fact]
        public void TreasureAdapter_ValidateEdits_AcceptsValidFields()
        {
            var adapter = new TreasureAdapter();
            var edits = new Dictionary<string, object>
            {
                { "Kind", (byte)0xFF },
                { "Quantity", (byte)0x01 },
                { "Type", (ushort)0x1234 }
            };

            IReadOnlyList<string> errors = adapter.ValidateEdits(edits);

            Assert.Empty(errors);
        }

        // ---------------------------------------------------------------
        // 5. Risk property is Safe
        // ---------------------------------------------------------------

        [Fact]
        public void TreasureAdapter_Risk_IsSafe()
        {
            var adapter = new TreasureAdapter();

            Assert.Equal(RiskLevel.Safe, adapter.Risk);
        }
    }
}

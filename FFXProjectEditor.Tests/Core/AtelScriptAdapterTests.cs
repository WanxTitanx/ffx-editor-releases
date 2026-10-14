using System;
using FFXProjectEditor.Tests.Infrastructure;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FFXProjectEditor.Core;
using FFXProjectEditor.Core.Writers;
using FFXProjectEditor.FfxLib.Ai;
using Xunit;

namespace FFXProjectEditor.Tests.Core
{
    /// <summary>
    /// L1 of docs/ai/P2_INTEGRACAO_PIPELINE_2026-07-31.md: read-only/preview ATEL adapter contract.
    ///
    /// Fixtures: Fixtures/Monster/m000.bin + m001.bin (vanilla monster_*.bin); the AiFile partition
    /// is sliced via AiScript_File.SliceAiFileFromMonster (the codec's own API). m001 was verified by
    /// hand: AiFile = [0x30..0x930), 3 workers, script 837 bytes starting at 0x178 whose first PUSHII
    /// (AE 9E 00) carries operand 0x009E (unknown command) — used for the byte-local edit test.
    /// </summary>
    public class AtelScriptAdapterTests
    {
        private static readonly AtelScriptAdapter Adapter = new();

        public static TheoryData<string> MonsterFixtures => new()
        {
            { "m000.bin" },
            { "m001.bin" },
        };

        static byte[] MonsterFixtureBytes(string name) =>
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Monster", name));

        static byte[] SliceAiFile(string monsterFixtureName)
        {
            byte[]? ai = AiScript_File.SliceAiFileFromMonster(MonsterFixtureBytes(monsterFixtureName));
            Assert.NotNull(ai);
            return ai!;
        }

        /// <summary>Copy a fixture into a fresh temp dir and return the temp path (isolated per test).</summary>
        static string TempSource(string fixtureName)
        {
            string dir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "AtelScriptAdapterTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, fixtureName);
            File.WriteAllBytes(path, MonsterFixtureBytes(fixtureName));
            return path;
        }

        static string Sha256Of(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
        }

        // --- (a) ComputeBeforeHash is stable --------------------------------------------

        [Theory]
        [MemberData(nameof(MonsterFixtures))]
        public void ComputeBeforeHash_IsStableAndMatchesRawSha256(string fixtureName)
        {
            string source = TempSource(fixtureName);

            string first = Adapter.ComputeBeforeHash(source);
            string second = Adapter.ComputeBeforeHash(source);

            Assert.Equal(64, first.Length);
            Assert.Equal(first, second);
            Assert.Equal(Sha256Of(MonsterFixtureBytes(fixtureName)), first);
        }

        // --- (b) StageAsync with edits -> InvalidOperationException, nothing staged ------

        [Theory]
        [MemberData(nameof(MonsterFixtures))]
        public async Task StageAsync_WithEdits_ThrowsInvalidOperationAndWritesNothing(string fixtureName)
        {
            string source = TempSource(fixtureName);
            string stagingDir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "AtelScriptAdapterTests_" + Guid.NewGuid().ToString("N"));
            string stagingPath = Path.Combine(stagingDir, "staged.bin");
            var edits = new Dictionary<string, object> { ["Operand"] = (ushort)1 };

            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Adapter.StageAsync(source, stagingPath, edits));

            Assert.Contains("preview-only", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(stagingPath),
                "staging must not be created when an edit is refused (preview-only)");
        }

        // --- (c) StageAsync copy-only -> staging exists, hash == before ------------------

        [Theory]
        [MemberData(nameof(MonsterFixtures))]
        public async Task StageAsync_CopyOnly_StagesIdenticalBytes(string fixtureName)
        {
            string source = TempSource(fixtureName);
            string stagingDir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "AtelScriptAdapterTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingDir);
            string stagingPath = Path.Combine(stagingDir, "staged.bin");

            string stagedHash = await Adapter.StageAsync(source, stagingPath, new Dictionary<string, object>());

            Assert.True(File.Exists(stagingPath), "copy-only staging must produce a staging file");
            Assert.Equal(Adapter.ComputeBeforeHash(source), stagedHash);
            Assert.Equal(Sha256Of(File.ReadAllBytes(stagingPath)), stagedHash);
        }

        // --- DescribeChanges / ValidateEdits (empty preview contract) --------------------

        [Fact]
        public void DescribeChanges_AlwaysReturnsEmptySummary()
        {
            var edits = new Dictionary<string, object> { ["Operand"] = (ushort)1 };

            FileDiffSummary summary = Adapter.DescribeChanges(edits);

            Assert.Equal(0, summary.FieldsChanged);
            Assert.Empty(summary.ChangedFieldNames);
            Assert.Equal(string.Empty, summary.HumanSummary);
        }

        [Fact]
        public void ValidateEdits_RejectsAnyField_AndAcceptsEmpty()
        {
            var edits = new Dictionary<string, object> { ["Operand"] = (ushort)1 };

            IReadOnlyList<string> errors = Adapter.ValidateEdits(edits);

            Assert.Single(errors);
            Assert.Equal(AtelScriptAdapter.PreviewOnlyValidationError, errors[0]);

            Assert.Empty(Adapter.ValidateEdits(new Dictionary<string, object>()));
        }

        // --- (d) ComputePreviewDiff: byte-local operand edit -----------------------------

        [Fact]
        public void ComputePreviewDiff_RealFixtureOperandEdit_AllThreeLayersReport()
        {
            // m001: first PUSHII is AE 9E 00 (operand 0x009E, unknown command). Editing it to
            // 0x3049 (Firaga — known in AiCommandId) is byte-local (3 bytes, same length), changes
            // the raw bytes, the disassembly text AND the semantic Meaning/Evidence, so all three
            // layers must report.
            byte[] before = SliceAiFile("m001.bin");
            Assert.True(AiScript_File.RoundTripsByteIdentical(before),
                "fixture m001 deve ser RT0 byte-idêntica (pré-condição do fluxo de preview)");

            AiScriptFile script = AiScript_File.Read(before);
            AiInstruction pushii = script.Instructions.First(i => i.Opcode == 0xAE);
            Assert.Equal((ushort)0x009E, pushii.Operand);
            pushii.Operand = 0x3049; // Firaga: mesmo comprimento (3 bytes), byte-local
            byte[] after = AiScript_File.Write(script);

            ThreeLayerDiff? diff = AtelScriptAdapter.ComputePreviewDiff(before, after);

            Assert.NotNull(diff);
            Assert.NotEmpty(diff!.ByteDiff);
            Assert.Contains(diff.ByteDiff, line => line.Contains("9E->49", StringComparison.Ordinal));
            Assert.NotEmpty(diff.DisassemblyDiff);
            Assert.Contains(diff.DisassemblyDiff, line => line.Contains("[Modified]", StringComparison.Ordinal));
            Assert.NotEmpty(diff.SemanticDiff);
            Assert.Contains(diff.SemanticDiff, line => line.Contains("meaning", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void ComputePreviewDiff_RealFixtureOperandEdit_DoesNotTouchTheFile()
        {
            // Preview must be pure: computing the diff never creates or alters any file.
            string tempDir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "AtelScriptAdapterTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string[] filesBefore = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);

            byte[] before = SliceAiFile("m000.bin");
            AiScriptFile script = AiScript_File.Read(before);
            AiInstruction call = script.Instructions.First(i => i.Opcode == 0xD8);
            call.Operand = 0x60; // CALLPOPA 0x5F -> 0x60: byte-local
            byte[] after = AiScript_File.Write(script);
            AtelScriptAdapter.ComputePreviewDiff(before, after);

            Assert.Equal(filesBefore, Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories));
        }

        // --- (e) ComputePreviewDiff before == after -> all layers empty ------------------

        [Theory]
        [MemberData(nameof(MonsterFixtures))]
        public void ComputePreviewDiff_BeforeEqualsAfter_AllLayersEmpty(string fixtureName)
        {
            byte[] ai = SliceAiFile(fixtureName);

            ThreeLayerDiff? diff = AtelScriptAdapter.ComputePreviewDiff(ai, ai);

            Assert.NotNull(diff);
            Assert.True(diff!.IsEmpty);
            Assert.Empty(diff.ByteDiff);
            Assert.Empty(diff.DisassemblyDiff);
            Assert.Empty(diff.SemanticDiff);
        }

        // --- (f) ValidateMonsterBinHash --------------------------------------------------

        [Theory]
        [MemberData(nameof(MonsterFixtures))]
        public void ValidateMonsterBinHash_CorrectHash_ReturnsNull(string fixtureName)
        {
            string monsterBinPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Monster", fixtureName);
            string expected = Sha256Of(MonsterFixtureBytes(fixtureName));

            Assert.Null(AtelScriptAdapter.ValidateMonsterBinHash(monsterBinPath, expected));
        }

        [Theory]
        [MemberData(nameof(MonsterFixtures))]
        public void ValidateMonsterBinHash_WrongHash_ReturnsErrorMessage(string fixtureName)
        {
            string monsterBinPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Monster", fixtureName);
            const string wrongHash = "0000000000000000000000000000000000000000000000000000000000000000";

            string? error = AtelScriptAdapter.ValidateMonsterBinHash(monsterBinPath, wrongHash);

            Assert.NotNull(error);
            Assert.Contains("hash mismatch", error, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(monsterBinPath, error, StringComparison.Ordinal);
        }
    }
}

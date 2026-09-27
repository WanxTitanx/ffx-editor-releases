using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FFXProjectEditor.Core;
using FFXProjectEditor.Core.LLM;
using FFXProjectEditor.Core.Writers;
using Xunit;

namespace FFXProjectEditor.Tests.Core.Writers
{
    /// <summary>
    /// BytePatchAdapter (Jarvis-UI 2026-09-15): the ONLY adapter that consumes the
    /// generic {offset, newBytesBase64, beforeHash} edit shape emitted by
    /// ProposalToPlanMapper.BuildEdits for profile-1 ByteReplace proposals.
    /// Covers splice correctness, bounds, validation and byte preservation.
    /// </summary>
    public class BytePatchAdapterTests : IDisposable
    {
        readonly string _dir = Path.Combine(Path.GetTempPath(), "bytepatch-" + Guid.NewGuid().ToString("N"));
        readonly BytePatchAdapter _adapter = new();

        public BytePatchAdapterTests()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllBytes(Path.Combine(_dir, "src.bin"),
                Enumerable.Range(0, 256).Select(i => (byte)i).ToArray());
        }

        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        string Src => Path.Combine(_dir, "src.bin");
        string Stage => Path.Combine(_dir, "staged", "src.bin");

        static Dictionary<string, object> Edits(long offset, byte[] payload, string beforeHash = "x") => new()
        {
            [ProposalToPlanMapper.EditKeyOffset] = offset,
            [ProposalToPlanMapper.EditKeyNewBytesBase64] = Convert.ToBase64String(payload),
            [ProposalToPlanMapper.EditKeyBeforeHash] = beforeHash,
        };

        // ── splice feliz ────────────────────────────────────────────────────

        [Fact]
        public async Task StageAsync_SplicesPayload_PreservesUnrelatedBytes()
        {
            byte[] payload = { 0xDE, 0xAD, 0xBE, 0xEF };

            string hash = await _adapter.StageAsync(Src, Stage, Edits(0x40, payload));

            byte[] staged = File.ReadAllBytes(Stage);
            Assert.Equal(256, staged.Length);
            Assert.Equal(payload, staged.Skip(0x40).Take(4).ToArray());
            // Todo byte fora do range preservado.
            Assert.Equal(Enumerable.Range(0, 0x40).Select(i => (byte)i), staged.Take(0x40));
            Assert.Equal(Enumerable.Range(0x44, 256 - 0x44).Select(i => (byte)i), staged.Skip(0x44));
            // Hash devolvido é o do arquivo staged real.
            Assert.Equal(Convert.ToHexString(SHA256.HashData(staged)).ToLowerInvariant(), hash);
            // Fonte intocada.
            Assert.Equal(0x40, File.ReadAllBytes(Src)[0x40]);
        }

        [Fact]
        public async Task StageAsync_PatchAtFileEnd_Succeeds()
        {
            await _adapter.StageAsync(Src, Stage, Edits(252, new byte[] { 1, 2, 3, 4 }));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(Stage).Skip(252));
        }

        // ── bounds / malformed ──────────────────────────────────────────────

        [Fact]
        public async Task StageAsync_NegativeOffset_Throws()
        {
            await Assert.ThrowsAsync<InvalidDataException>(
                () => _adapter.StageAsync(Src, Stage, Edits(-1, new byte[] { 1 })));
            Assert.False(File.Exists(Stage));
        }

        [Fact]
        public async Task StageAsync_OutOfRange_Throws()
        {
            await Assert.ThrowsAsync<InvalidDataException>(
                () => _adapter.StageAsync(Src, Stage, Edits(254, new byte[] { 1, 2, 3, 4 })));
            Assert.False(File.Exists(Stage));
        }

        [Fact]
        public async Task StageAsync_OffsetBeyondFile_Throws()
        {
            await Assert.ThrowsAsync<InvalidDataException>(
                () => _adapter.StageAsync(Src, Stage, Edits(4096, new byte[] { 1 })));
        }

        [Fact]
        public async Task StageAsync_MissingOffset_Throws()
        {
            var edits = new Dictionary<string, object>
            {
                [ProposalToPlanMapper.EditKeyNewBytesBase64] = Convert.ToBase64String(new byte[] { 1 }),
            };
            await Assert.ThrowsAsync<InvalidDataException>(() => _adapter.StageAsync(Src, Stage, edits));
        }

        [Fact]
        public async Task StageAsync_InvalidBase64_Throws()
        {
            var edits = new Dictionary<string, object>
            {
                [ProposalToPlanMapper.EditKeyOffset] = 0L,
                [ProposalToPlanMapper.EditKeyNewBytesBase64] = "!!!not-base64!!!",
            };
            await Assert.ThrowsAsync<InvalidDataException>(() => _adapter.StageAsync(Src, Stage, edits));
        }

        // ── ValidateEdits ───────────────────────────────────────────────────

        [Fact]
        public void ValidateEdits_Valid_ReturnsEmpty()
        {
            Assert.Empty(_adapter.ValidateEdits(Edits(0, new byte[] { 1 })));
        }

        [Fact]
        public void ValidateEdits_NegativeOffset_Flagged()
        {
            var errors = _adapter.ValidateEdits(Edits(-4, new byte[] { 1 }));
            Assert.Contains(errors, e => e.Contains("offset"));
        }

        [Fact]
        public void ValidateEdits_EmptyPayload_Flagged()
        {
            var errors = _adapter.ValidateEdits(Edits(0, Array.Empty<byte>()));
            Assert.Contains(errors, e => e.Contains("empty"));
        }

        [Fact]
        public void ValidateEdits_BadBase64_Flagged()
        {
            var edits = new Dictionary<string, object>
            {
                [ProposalToPlanMapper.EditKeyOffset] = 0L,
                [ProposalToPlanMapper.EditKeyNewBytesBase64] = "@@@",
            };
            Assert.NotEmpty(_adapter.ValidateEdits(edits));
        }

        // ── contrato / catálogo ─────────────────────────────────────────────

        [Fact]
        public void DescribeChanges_ReportsOffsetAndLength()
        {
            var d = _adapter.DescribeChanges(Edits(0x540, new byte[] { 1, 2 }));
            Assert.Contains("0x540", d.HumanSummary);
            Assert.Equal(2, d.FieldsChanged);
        }

        [Fact]
        public void Catalog_ResolvesBytePatchAdapter()
        {
            var catalog = new WriterAdapterCatalog();
            var adapter = catalog.Get("byte-patch");
            Assert.NotNull(adapter);
            Assert.IsType<BytePatchAdapter>(adapter);
        }

        [Fact]
        public void CapabilityCatalog_BytePatch_IsExecutable()
        {
            var entry = LlmCapabilityCatalog.Entries.First(e => e.RecipeId == "byte-patch-t1");
            Assert.Equal("byte-patch", entry.CapabilityId);
            Assert.True(LlmCapabilityCatalog.TryGetDescriptor("byte-patch", out var d));
            Assert.NotNull(d);
        }

        // ── e2e: proposal → BuildEdits → StageAsync ─────────────────────────

        [Fact]
        public async Task EndToEnd_MapperEdits_FeedAdapter()
        {
            // Simula a trilha real: PatchProposal(ByteReplace) → BuildEdits → StageAsync.
            byte[] payload = BitConverter.GetBytes(500u); // hp = 500, little-endian
            var proposal = new PatchProposal
            {
                ProposalId = "p-e2e",
                CapabilityId = "byte-patch",
                RecipeId = "byte-patch-t1",
                Operation = PatchOperationKind.ByteReplace,
                Target = new PatchTarget
                {
                    RelativePath = "src.bin",
                    FileVersion = "pc-hd",
                    Offset = 0x10,
                    Length = 4,
                    NewBytesBase64 = Convert.ToBase64String(payload),
                },
                BeforeHash = "x",
                AfterHash = "y",
                SemanticChange = "hp",
                Justification = "test",
                Diff = new PatchDiff
                {
                    BeforeBytesHex = "01000000",
                    AfterBytesHex = Convert.ToHexString(payload).ToLowerInvariant(),
                    ChangedFields = new[] { "hp" },
                    HumanSummary = "hp 1→500",
                },
                Verifications = new[] { new VerificationCheck { Kind = "hash-after", Required = true } },
                Provider = "test",
                ModelId = "m",
                PromptTemplateVersion = "v1",
                CreatedAt = DateTimeOffset.UtcNow,
            };

            var edits = ProposalToPlanMapper.BuildEdits(proposal, "x");
            string hash = await _adapter.StageAsync(Src, Stage, edits);

            byte[] staged = File.ReadAllBytes(Stage);
            Assert.Equal(500u, BitConverter.ToUInt32(staged, 0x10));
            Assert.Equal(64, Convert.FromHexString(hash).Length == 32 ? 64 : 0);
        }
    }
}

using System;
using FFXProjectEditor.Tests.Infrastructure;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.Core;
using FFXProjectEditor.Core.LLM;
using Xunit;

namespace FFXProjectEditor.Tests.Core
{
    /// <summary>
    /// L3 P2-B (Jarvis-CLINE 2026-07-31): ProposalToPlanMapper — PatchProposal →
    /// FileOperation(Kind=Patch) → OperationPlan (reuso total do pipeline RT0).
    /// O mapper nunca executa: só mapeia; o executor roda depois, all-or-nothing.
    /// </summary>
    public class ProposalToPlanMapperTests : IDisposable
    {
        private readonly string _sourceDir;
        private readonly string _outputDir;

        public ProposalToPlanMapperTests()
        {
            var id = Guid.NewGuid().ToString("N")[..8];
            _sourceDir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "mapper_src_" + id);
            _outputDir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "mapper_out_" + id);
            Directory.CreateDirectory(_sourceDir);
            Directory.CreateDirectory(_outputDir);
            // Arquivo real de 16 bytes — mesma base do LlmGuardTests.
            File.WriteAllBytes(Path.Combine(_sourceDir, "magic_0021.dll"),
                new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 });
        }

        public void Dispose()
        {
            if (Directory.Exists(_sourceDir))
                Directory.Delete(_sourceDir, recursive: true);
            if (Directory.Exists(_outputDir))
                Directory.Delete(_outputDir, recursive: true);
        }

        // (a) Proposta válida (mesmo padrão do LlmGuardTests) → plano não-nulo,
        //     Edits transportados, PredictedAfterHash == AfterHash.
        [Fact]
        public void MapToPlan_ValidProposal_ReturnsPlan_WithEditsAndHashes()
        {
            var proposal = ValidProposal();
            string realHash = ComputeSha256(Path.Combine(_sourceDir, "magic_0021.dll"));
            var catalog = CatalogWithFakeAdapter();

            var plan = ProposalToPlanMapper.MapToPlan(
                proposal, DefaultSettings(), Capability(), _sourceDir, _outputDir, catalog, realHash,
                out var guardResult);

            Assert.True(guardResult.Allowed, guardResult.Message);
            Assert.NotNull(plan);
            Assert.Equal("ppp-sclmove", plan.OwnerCapabilityId);
            Assert.Equal(Capability().Preconditions, plan.Preconditions);
            Assert.Equal(_sourceDir, plan.SourceRoot);
            Assert.Equal(_outputDir, plan.OutputRoot);
            Assert.Equal(1, plan.FileCount);

            var op = Assert.Single(plan.Operations);
            Assert.Equal(FileOperationKind.Patch, op.Kind);
            Assert.Equal("magic_0021.dll", op.SourceRelativePath);
            Assert.Equal("magic_0021.dll", op.OutputRelativePath);
            Assert.Equal(realHash, op.BeforeHash);
            Assert.Equal(proposal.AfterHash, op.PredictedAfterHash);
            Assert.Equal((long)proposal.Target.Length, op.EstimatedBytes);
            Assert.Equal(proposal.Diff.ChangedFields.Count, op.Diff.FieldsChanged);
            Assert.Equal(proposal.Diff.ChangedFields, op.Diff.ChangedFieldNames);
            Assert.Equal(proposal.Diff.HumanSummary, op.Diff.HumanSummary);
            Assert.Equal(RiskLevel.Moderate, op.Risk);

            // Contrato de Edits ppp-sclmove-t3: offset / newBytesBase64 / beforeHash.
            Assert.Equal(3, op.Edits.Count);
            Assert.Equal(proposal.Target.Offset, Assert.IsType<long>(op.Edits[ProposalToPlanMapper.EditKeyOffset]));
            Assert.Equal(proposal.Target.NewBytesBase64, Assert.IsType<string>(op.Edits[ProposalToPlanMapper.EditKeyNewBytesBase64]));
            Assert.Equal(realHash, Assert.IsType<string>(op.Edits[ProposalToPlanMapper.EditKeyBeforeHash]));
        }

        // (b) Opt-in off → guardResult.OptInDisabled e plano null.
        [Fact]
        public void MapToPlan_OptInDisabled_Rejects()
        {
            var proposal = ValidProposal();
            string realHash = ComputeSha256(Path.Combine(_sourceDir, "magic_0021.dll"));

            var plan = ProposalToPlanMapper.MapToPlan(
                proposal, DefaultSettings() with { IsEnabled = false }, Capability(),
                _sourceDir, _outputDir, CatalogWithFakeAdapter(), realHash,
                out var guardResult);

            Assert.Null(plan);
            Assert.False(guardResult.Allowed);
            Assert.Equal(LlmRejectionReason.OptInDisabled, guardResult.Reason);
        }

        // (c) Receita desconhecida → null.
        [Fact]
        public void MapToPlan_UnknownRecipe_Rejects()
        {
            var proposal = ValidProposal() with { RecipeId = "not-a-proven-recipe" };
            string realHash = ComputeSha256(Path.Combine(_sourceDir, "magic_0021.dll"));

            var plan = ProposalToPlanMapper.MapToPlan(
                proposal, DefaultSettings(), Capability(),
                _sourceDir, _outputDir, CatalogWithFakeAdapter(), realHash,
                out var guardResult);

            Assert.Null(plan);
            Assert.False(guardResult.Allowed);
            Assert.Equal(LlmRejectionReason.UnknownRecipe, guardResult.Reason);
        }

        // (d) Capability sem adapter no catálogo → MissingPrecondition (etapa b do mapper).
        [Fact]
        public void MapToPlan_CapabilityWithoutAdapter_RejectsMissingPrecondition()
        {
            var proposal = ValidProposal();
            string realHash = ComputeSha256(Path.Combine(_sourceDir, "magic_0021.dll"));

            // Catálogo com os built-ins do repo — nenhum registra "ppp-sclmove".
            var plan = ProposalToPlanMapper.MapToPlan(
                proposal, DefaultSettings(), Capability(),
                _sourceDir, _outputDir, new WriterAdapterCatalog(), realHash,
                out var guardResult);

            Assert.Null(plan);
            Assert.False(guardResult.Allowed);
            Assert.Equal(LlmRejectionReason.MissingPrecondition, guardResult.Reason);
            Assert.Contains(proposal.CapabilityId, guardResult.Message ?? string.Empty);
        }

        // (e) BeforeHash divergente do arquivo real → null (guard rejeita antes do mapper).
        [Fact]
        public void MapToPlan_BeforeHashMismatch_Rejects()
        {
            var proposal = ValidProposal(); // BeforeHash = hash real do arquivo
            string divergentHash = "0".PadLeft(64, '0');

            var plan = ProposalToPlanMapper.MapToPlan(
                proposal, DefaultSettings(), Capability(),
                _sourceDir, _outputDir, CatalogWithFakeAdapter(), divergentHash,
                out var guardResult);

            Assert.Null(plan);
            Assert.False(guardResult.Allowed);
        }

        // (f) LlmHistoryEntry nunca carrega payload — por construção, mas testado:
        //     superfície exata do contrato L3 e ausência de conteúdo sensível no JSON.
        [Fact]
        public void HistoryEntry_NeverCarriesPayload()
        {
            var proposal = ValidProposal();
            var entry = new LlmHistoryEntry
            {
                ProposalId = proposal.ProposalId,
                CapabilityId = proposal.CapabilityId,
                RecipeId = proposal.RecipeId,
                Provider = proposal.Provider,
                ModelId = proposal.ModelId,
                PromptTemplateVersion = proposal.PromptTemplateVersion,
                Decision = ReviewDecision.Approved,
                Timestamp = DateTimeOffset.UtcNow,
            };

            // 1) Superfície do record: exatamente os 8 campos do contrato L3.
            var props = typeof(LlmHistoryEntry).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();
            var expected = new[]
            {
                "CapabilityId", "Decision", "ModelId", "PromptTemplateVersion",
                "ProposalId", "Provider", "RecipeId", "Timestamp",
            }.OrderBy(n => n).ToArray();
            Assert.Equal(expected, props);

            // Nenhum nome de campo sugere payload/chave/conteúdo sensível.
            var sensitive = new[] { "payload", "bytes", "key", "secret", "hash", "diff", "justification", "target" };
            Assert.DoesNotContain(props, n => sensitive.Any(s => n.Contains(s, StringComparison.OrdinalIgnoreCase)));

            // 2) Serializado, o histórico não vaza conteúdo da proposta.
            var json = JsonSerializer.Serialize(entry);
            Assert.DoesNotContain(proposal.Justification, json);
            Assert.DoesNotContain(proposal.Diff.HumanSummary, json);
            Assert.DoesNotContain(proposal.Target.NewBytesBase64, json);
            Assert.DoesNotContain(proposal.BeforeHash, json);
            Assert.DoesNotContain(proposal.Diff.BeforeBytesHex, json);
        }

        // --- Helpers ----------------------------------------------------------------

        private static LlmSessionSettings DefaultSettings() => new()
        {
            IsEnabled = true,
            Provider = "local",
            Endpoint = new Uri("http://127.0.0.1:11434/v1"),
            ModelId = "test-model",
            AllowedDataKinds = new[] { LlmDataKind.RecipeDescription },
            CapabilityFlags = LlmCapabilityFlag.ByteReplace,
            MaxResponseBytes = 4096,
            MaxProposalBytes = 32,
            RequestTimeout = TimeSpan.FromSeconds(10),
        };

        private static WriterAdapterCatalog CatalogWithFakeAdapter()
        {
            var catalog = new WriterAdapterCatalog();
            catalog.Register(new FakeBytePatchAdapter());
            return catalog;
        }

        private PatchProposal ValidProposal()
        {
            string filePath = Path.Combine(_sourceDir, "magic_0021.dll");
            string hash = ComputeSha256(filePath);
            return new PatchProposal
            {
                ProposalId = Guid.NewGuid().ToString("N"),
                CapabilityId = "ppp-sclmove",
                RecipeId = "ppp-sclmove-t3",
                Operation = PatchOperationKind.ByteReplace,
                Target = new PatchTarget
                {
                    RelativePath = "magic_0021.dll",
                    FileVersion = "ps2-hd",
                    Offset = 16,
                    Length = 16,
                    NewBytesBase64 = Convert.ToBase64String(new byte[16]),
                },
                BeforeHash = hash,
                AfterHash = "0".PadLeft(64, '0'),
                SemanticChange = "pppSclMove runtime window",
                Justification = "justificativa-secreta-do-modelo",
                Diff = new PatchDiff
                {
                    BeforeBytesHex = "DEADBEEF",
                    AfterBytesHex = "00FF00FF",
                    ChangedFields = new[] { "vx" },
                    HumanSummary = "resumo-secreto-do-modelo",
                },
                Verifications = new List<VerificationCheck>
                {
                    new() { Kind = "hash-after", Required = true, Expected = "0".PadLeft(64, '0') },
                    new() { Kind = "round-trip", Required = true },
                },
                Provider = "local",
                ModelId = "test-model",
                PromptTemplateVersion = "v1",
                CreatedAt = DateTimeOffset.UtcNow,
            };
        }

        private static CapabilityDescriptor Capability() => new()
        {
            Id = "ppp-sclmove",
            Domain = "magic",
            Title = "PPP SclMove",
            Description = "test",
            Mode = CapabilityMode.OfflineWriter,
            Evidence = EvidenceLevel.Partial,
            Platforms = new[] { Platform.PS2 },
            RequiredDependencies = Array.Empty<string>(),
            OptionalDependencies = Array.Empty<string>(),
            Risks = Array.Empty<string>(),
            AllowedOperations = new[] { AllowedOperation.Patch },
            ProhibitedOperations = Array.Empty<AllowedOperation>(),
            Preconditions = Array.Empty<string>(),
            DocumentationLinks = Array.Empty<string>(),
            OwnerAgent = "test",
        };

        private static string ComputeSha256(string filePath)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }
    }

    /// <summary>
    /// Fake do futuro BytePatchAdapter genérico do perfil 1 (receita ppp-sclmove-t3):
    /// consome exatamente o contrato de Edits do mapper (offset/newBytesBase64/beforeHash).
    /// O mapper não executa nem valida edits — o executor faz isso via StageAsync.
    /// </summary>
    internal sealed class FakeBytePatchAdapter : IWriterAdapter
    {
        public string CapabilityId => "ppp-sclmove";
        public string DisplayName => "PPP SclMove BytePatch (fake)";
        public RiskLevel Risk => RiskLevel.Moderate;

        public string ComputeBeforeHash(string sourcePath)
            => OperationExecutorV2.ComputeSha256(sourcePath);

        public Task<string> StageAsync(
            string sourcePath,
            string stagingPath,
            IReadOnlyDictionary<string, object> edits,
            CancellationToken ct = default)
        {
            if (!edits.TryGetValue(ProposalToPlanMapper.EditKeyOffset, out var offsetObj) || offsetObj is not long offset)
                throw new ArgumentException("Missing/invalid 'offset' edit.");
            if (!edits.TryGetValue(ProposalToPlanMapper.EditKeyNewBytesBase64, out var b64Obj) || b64Obj is not string b64)
                throw new ArgumentException("Missing/invalid 'newBytesBase64' edit.");
            if (!edits.TryGetValue(ProposalToPlanMapper.EditKeyBeforeHash, out var hashObj) || hashObj is not string beforeHash)
                throw new ArgumentException("Missing/invalid 'beforeHash' edit.");

            ct.ThrowIfCancellationRequested();
            var bytes = File.ReadAllBytes(sourcePath);
            var patch = Convert.FromBase64String(b64);
            if (offset < 0 || offset + patch.Length > bytes.Length)
                throw new ArgumentException("Patch window out of bounds.");
            Array.Copy(patch, 0, bytes, offset, patch.Length);
            File.WriteAllBytes(stagingPath, bytes);
            return Task.FromResult(OperationExecutorV2.ComputeSha256(stagingPath));
        }

        public FileDiffSummary DescribeChanges(IReadOnlyDictionary<string, object> edits)
            => new()
            {
                FieldsChanged = 1,
                ChangedFieldNames = new[] { "runtime-window" },
                HumanSummary = "Byte replace (perfil 1)",
            };

        public IReadOnlyList<string> ValidateEdits(IReadOnlyDictionary<string, object> edits)
        {
            var errors = new List<string>();
            if (!edits.TryGetValue(ProposalToPlanMapper.EditKeyOffset, out var offsetObj) || offsetObj is not long)
                errors.Add("Missing/invalid 'offset'.");
            if (!edits.TryGetValue(ProposalToPlanMapper.EditKeyNewBytesBase64, out var b64Obj) || b64Obj is not string)
                errors.Add("Missing/invalid 'newBytesBase64'.");
            if (!edits.TryGetValue(ProposalToPlanMapper.EditKeyBeforeHash, out var hashObj) || hashObj is not string)
                errors.Add("Missing/invalid 'beforeHash'.");
            return errors;
        }
    }
}

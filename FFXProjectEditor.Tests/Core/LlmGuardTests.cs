using System;
using FFXProjectEditor.Tests.Infrastructure;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using FFXProjectEditor.Core;
using FFXProjectEditor.Core.LLM;
using Xunit;

namespace FFXProjectEditor.Tests.Core
{
    /// <summary>
    /// L1 P2-B (Jarvis-CLINE 2026-07-31): regras compiladas do contrato LLM.
    /// Prompt injection nunca altera estas regras — todas as decisões são código.
    /// </summary>
    public class LlmGuardTests : IDisposable
    {
        private readonly string _sourceDir;

        public LlmGuardTests()
        {
            _sourceDir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "llm_guard_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_sourceDir);
            File.WriteAllBytes(Path.Combine(_sourceDir, "magic_0021.dll"), new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 });
        }

        public void Dispose()
        {
            if (Directory.Exists(_sourceDir))
                Directory.Delete(_sourceDir, recursive: true);
        }

        [Fact]
        public void Evaluate_OptInDisabled_Rejects()
        {
            var settings = DefaultSettings() with { IsEnabled = false };
            var proposal = ValidProposal();

            var result = LlmGuard.EvaluatePreconditions(proposal, settings, Capability(), _sourceDir, proposal.BeforeHash);

            Assert.False(result.Allowed);
            Assert.Equal(LlmRejectionReason.OptInDisabled, result.Reason);
        }

        [Fact]
        public void Evaluate_UnknownRecipe_Rejects()
        {
            var settings = DefaultSettings();
            var proposal = ValidProposal() with { RecipeId = "not-a-proven-recipe" };

            var result = LlmGuard.EvaluatePreconditions(proposal, settings, Capability(), _sourceDir, proposal.BeforeHash);

            Assert.False(result.Allowed);
            Assert.Equal(LlmRejectionReason.UnknownRecipe, result.Reason);
        }

        [Fact]
        public void Evaluate_CapabilityMismatch_Rejects()
        {
            var settings = DefaultSettings();
            var proposal = ValidProposal() with { CapabilityId = "other-capability" };

            var result = LlmGuard.EvaluatePreconditions(proposal, settings, Capability(), _sourceDir, proposal.BeforeHash);

            Assert.False(result.Allowed);
            Assert.Equal(LlmRejectionReason.MissingPrecondition, result.Reason);
        }

        [Fact]
        public void Evaluate_InsertWithoutFlag_Rejects()
        {
            var settings = DefaultSettings(); // CapabilityFlags = ByteReplace (sem Insert)
            var proposal = ValidProposal() with { Operation = PatchOperationKind.Insert };

            var result = LlmGuard.EvaluatePreconditions(proposal, settings, Capability(), _sourceDir, proposal.BeforeHash);

            Assert.False(result.Allowed);
            Assert.Equal(LlmRejectionReason.OperationNotAllowed, result.Reason);
        }

        [Fact]
        public void Evaluate_DeleteWithoutFlag_Rejects()
        {
            var settings = DefaultSettings();
            var proposal = ValidProposal() with { Operation = PatchOperationKind.Delete };

            var result = LlmGuard.EvaluatePreconditions(proposal, settings, Capability(), _sourceDir, proposal.BeforeHash);

            Assert.False(result.Allowed);
            Assert.Equal(LlmRejectionReason.OperationNotAllowed, result.Reason);
        }

        [Fact]
        public void Evaluate_MissingRequiredVerification_Rejects()
        {
            var settings = DefaultSettings();
            var proposal = ValidProposal() with
            {
                Verifications = new List<VerificationCheck>
                {
                    new() { Kind = "hash-after", Required = false, Expected = "x" },
                }
            };

            var result = LlmGuard.EvaluatePreconditions(proposal, settings, Capability(), _sourceDir, proposal.BeforeHash);

            Assert.False(result.Allowed);
            Assert.Equal(LlmRejectionReason.MissingPrecondition, result.Reason);
        }

        [Fact]
        public void Evaluate_OverMaxProposalBytes_Rejects()
        {
            var settings = DefaultSettings(); // MaxProposalBytes = 32
            var valid = ValidProposal();
            var proposal = valid with
            {
                Target = valid.Target with { Length = 64, NewBytesBase64 = Convert.ToBase64String(new byte[64]) }
            };

            var result = LlmGuard.EvaluatePreconditions(proposal, settings, Capability(), _sourceDir, proposal.BeforeHash);

            Assert.False(result.Allowed);
            Assert.Equal(LlmRejectionReason.LimitsExceeded, result.Reason);
        }

        [Fact]
        public void Evaluate_BeforeHashMismatch_Rejects()
        {
            var settings = DefaultSettings();
            var proposal = ValidProposal() with { BeforeHash = "0".PadLeft(64, '0') };
            string realHash = ComputeSha256(Path.Combine(_sourceDir, "magic_0021.dll"));

            var result = LlmGuard.EvaluatePreconditions(proposal, settings, Capability(), _sourceDir, realHash);

            Assert.False(result.Allowed);
            Assert.Equal(LlmRejectionReason.BeforeHashMismatch, result.Reason);
        }

        [Fact]
        public void Evaluate_PathTraversal_Rejects()
        {
            var settings = DefaultSettings();
            var proposal = ValidProposal() with
            {
                Target = ValidProposal().Target with { RelativePath = @"..\..\windows\system32\evil.dll" }
            };

            var result = LlmGuard.EvaluatePreconditions(proposal, settings, Capability(), _sourceDir, proposal.BeforeHash);

            Assert.False(result.Allowed);
            Assert.Equal(LlmRejectionReason.MalformedPayload, result.Reason);
        }

        [Fact]
        public void Evaluate_BadBase64_Rejects()
        {
            var settings = DefaultSettings();
            var proposal = ValidProposal() with
            {
                Target = ValidProposal().Target with { NewBytesBase64 = "!!!not-base64!!!" }
            };

            var result = LlmGuard.EvaluatePreconditions(proposal, settings, Capability(), _sourceDir, proposal.BeforeHash);

            Assert.False(result.Allowed);
            Assert.Equal(LlmRejectionReason.MalformedPayload, result.Reason);
        }

        [Fact]
        public void Evaluate_ValidByteReplace_Allows()
        {
            var settings = DefaultSettings();
            var proposal = ValidProposal();

            var result = LlmGuard.EvaluatePreconditions(proposal, settings, Capability(), _sourceDir, proposal.BeforeHash);

            Assert.True(result.Allowed, result.Message);
            Assert.Equal(LlmRejectionReason.None, result.Reason);
        }

        [Fact]
        public void Envelope_RemoteHost_RequiresConfirmation()
        {
            var local = new LlmRequestEnvelope
            {
                Provider = "local",
                Endpoint = new Uri("http://127.0.0.1:11434/v1"),
                ModelId = "m",
                PayloadPreview = "preview",
                DataKinds = new[] { LlmDataKind.RecipeDescription },
                MaxResponseBytes = 1024,
                Timeout = TimeSpan.FromSeconds(10),
            };
            var remote = local with { Endpoint = new Uri("https://api.openai.com/v1") };

            Assert.False(local.RequiresRemoteConfirmation);
            Assert.True(remote.RequiresRemoteConfirmation);
        }

        [Fact]
        public void FailureMessage_NoSecret()
        {
            using var cred = LlmCredential.FromUserInput("sk-super-secret-key-1234567890");
            string msg = LlmRedactor.Redact("falhou ao chamar o modelo com sk-super-secret-key-1234567890");

            Assert.DoesNotContain("sk-super-secret-key-1234567890", msg);
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
                Justification = "model text",
                Diff = new PatchDiff
                {
                    BeforeBytesHex = "00",
                    AfterBytesHex = "01",
                    ChangedFields = new[] { "vx" },
                    HumanSummary = "test",
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
}


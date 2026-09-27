using System;
using System.Text;
using FFXProjectEditor.Core.LLM;
using Xunit;

namespace FFXProjectEditor.Tests.Core
{
    /// <summary>
    /// L1 P2-B (Jarvis-CLINE 2026-07-31): redação central do subsistema LLM.
    /// Toda string de saída (preview, log, justificativa, exceção) passa por aqui.
    /// </summary>
    public class LlmRedactorTests
    {
        [Fact]
        public void Redact_ApiKeyPattern_Masked()
        {
            string result = LlmRedactor.Redact("chave usada: sk-abcdef1234567890xyz no request");

            Assert.DoesNotContain("sk-abcdef1234567890xyz", result);
            Assert.Contains("[REDACTED]", result);
        }

        [Fact]
        public void Redact_BearerToken_Masked()
        {
            string result = LlmRedactor.Redact("Authorization: Bearer abcdef1234567890.abcdef");

            Assert.DoesNotContain("Bearer abcdef1234567890", result);
        }

        [Fact]
        public void Preview_NeverContainsSessionKey()
        {
            using var cred = LlmCredential.FromUserInput("sk-test-1234567890abcdef");
            byte[] payload = Encoding.UTF8.GetBytes("prompt with sk-test-1234567890abcdef");

            var preview = LlmRedactor.BuildPreview(payload, maxBytes: 64);
            var guardMsg = LlmRedactor.Redact("falhou ao chamar o modelo");

            Assert.DoesNotContain("sk-test-1234567890abcdef", preview);
            Assert.DoesNotContain("sk-test-1234567890abcdef", guardMsg);
        }

        [Fact]
        public void Preview_TruncatedToMaxBytes()
        {
            byte[] payload = Encoding.UTF8.GetBytes(new string('A', 200));

            var preview = LlmRedactor.BuildPreview(payload, maxBytes: 32);

            Assert.True(preview.Length <= 64, "preview must be truncated (base64 + ellipsis)");
        }

        [Fact]
        public void ModelJustification_RedactedBeforeStore()
        {
            string modelText = "Troquei o valor porque sk-live-9999999999 é a chave do servidor de produção.";

            string stored = LlmRedactor.Redact(modelText);

            Assert.DoesNotContain("sk-live-9999999999", stored);
            Assert.Contains("[REDACTED]", stored);
        }

        [Fact]
        public void ContainsSecret_DetectsApiKey()
        {
            Assert.True(LlmRedactor.ContainsSecret("key=sk-abc1234567890xyz"));
            Assert.False(LlmRedactor.ContainsSecret("nenhum segredo aqui"));
        }
    }
}

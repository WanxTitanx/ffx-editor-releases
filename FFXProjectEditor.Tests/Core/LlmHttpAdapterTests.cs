using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.Core.LLM;
using Xunit;

namespace FFXProjectEditor.Tests.Core
{
    /// <summary>
    /// L2 P2-B (Jarvis-MAGIC 2026-07-31): adapter HTTP do contrato LLM (testes 15-18 + extras).
    /// Zero rede real — todo request passa por DelegatingHandler fake.
    /// </summary>
    public class LlmHttpAdapterTests
    {
        private const string SessionKey = "sk-test-1234567890abcdef";

        /// <summary>PatchProposal mínimo VÁLIDO para o parse estrito (todos os required preenchidos).</summary>
        private const string ValidProposalJson = """
            {
              "ProposalId": "proposal-test-0001",
              "CapabilityId": "ppp-sclmove",
              "RecipeId": "ppp-sclmove-t3",
              "Operation": 0,
              "Target": {
                "RelativePath": "magic_0021.dll",
                "FileVersion": "ps2-hd",
                "Offset": 16,
                "Length": 3,
                "NewBytesBase64": "AAAA"
              },
              "BeforeHash": "0000000000000000000000000000000000000000000000000000000000000000",
              "AfterHash": "1111111111111111111111111111111111111111111111111111111111111111",
              "SemanticChange": "pppSclMove runtime window",
              "Justification": "model text",
              "Diff": {
                "BeforeBytesHex": "000102",
                "AfterBytesHex": "010203",
                "ChangedFields": ["vx", "vy"],
                "HumanSummary": "test"
              },
              "Verifications": [
                { "Kind": "hash-after", "Required": true, "Expected": "1111111111111111111111111111111111111111111111111111111111111111" },
                { "Kind": "round-trip", "Required": true }
              ],
              "Provider": "local",
              "ModelId": "test-model",
              "PromptTemplateVersion": "v1",
              "CreatedAt": "2026-07-31T12:00:00Z"
            }
            """;

        [Fact]
        public async Task Adapter_Timeout_ReturnsRedactedFailure()
        {
            using var cred = LlmCredential.FromUserInput(SessionKey);
            var handler = new FakeHandler(async (_, ct) =>
            {
                // Handler lento: encerra limpo quando o token do adapter dispara (timeout).
                try { await Task.Delay(TimeSpan.FromSeconds(15), ct); }
                catch (OperationCanceledException) { /* esperado */ }
                return new HttpResponseMessage();
            });
            using var http = new HttpClient(handler);
            var adapter = new LlmHttpAdapter(http);
            var envelope = Envelope() with { Timeout = TimeSpan.FromMilliseconds(75) };

            var result = await adapter.RequestAsync(envelope, cred, remoteConfirmed: false, CancellationToken.None);

            Assert.Equal(LlmRejectionReason.Timeout, result.Reason);
            Assert.Null(result.Proposal);
            Assert.DoesNotContain(SessionKey, result.Message);
        }

        [Fact]
        public async Task Adapter_Cancellation_ReturnsCancelled()
        {
            using var cred = LlmCredential.FromUserInput(SessionKey);
            var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage()));
            using var http = new HttpClient(handler);
            var adapter = new LlmHttpAdapter(http);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var result = await adapter.RequestAsync(Envelope(), cred, remoteConfirmed: false, cts.Token);

            Assert.Equal(LlmRejectionReason.Cancelled, result.Reason);
            Assert.Null(result.Proposal);
        }

        [Fact]
        public async Task Adapter_OversizedResponse_Rejects()
        {
            using var cred = LlmCredential.FromUserInput(SessionKey);
            string bigBody = new('x', 10_000);
            var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage
            {
                Content = new StringContent(bigBody, Encoding.UTF8, "application/json"),
            }));
            using var http = new HttpClient(handler);
            var adapter = new LlmHttpAdapter(http);
            var envelope = Envelope() with { MaxResponseBytes = 128 };

            var result = await adapter.RequestAsync(envelope, cred, remoteConfirmed: false, CancellationToken.None);

            Assert.Equal(LlmRejectionReason.LimitsExceeded, result.Reason);
            Assert.Null(result.Proposal);
        }

        [Fact]
        public async Task Adapter_UnknownJsonField_Rejects()
        {
            using var cred = LlmCredential.FromUserInput(SessionKey);
            // T1: campo extra no JSON (tentativa de prompt injection) → parse estrito rejeita.
            string injected = ValidProposalJson.Replace(
                "\"ProposalId\"", "\"evilField\":\"prompt-injection\",\"ProposalId\"", StringComparison.Ordinal);
            var handler = OkJsonHandler(injected);
            using var http = new HttpClient(handler);
            var adapter = new LlmHttpAdapter(http);

            var result = await adapter.RequestAsync(Envelope(), cred, remoteConfirmed: false, CancellationToken.None);

            Assert.Equal(LlmRejectionReason.MalformedPayload, result.Reason);
            Assert.Null(result.Proposal);
        }

        [Fact]
        public async Task Adapter_RemoteHost_WithoutConfirmation_Rejects()
        {
            using var cred = LlmCredential.FromUserInput(SessionKey);
            var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage()));
            using var http = new HttpClient(handler);
            var adapter = new LlmHttpAdapter(http);
            var envelope = Envelope(new Uri("https://api.example.com/v1")); // não-loopback

            var result = await adapter.RequestAsync(envelope, cred, remoteConfirmed: false, CancellationToken.None);

            Assert.Equal(LlmRejectionReason.RemoteEndpointNotConfirmed, result.Reason);
            Assert.Null(result.Proposal);
            Assert.Null(handler.LastRequest); // recusa ANTES de tocar a rede
        }

        [Fact]
        public async Task Adapter_FailureMessage_NeverContainsSessionKey()
        {
            using var cred = LlmCredential.FromUserInput(SessionKey);
            // T4: handler lança exceção com a chave no texto — a mensagem final deve estar redigida.
            var handler = new FakeHandler((_, _) =>
                throw new HttpRequestException($"rede indisponível: tentativa com chave {SessionKey} falhou"));
            using var http = new HttpClient(handler);
            var adapter = new LlmHttpAdapter(http);

            var result = await adapter.RequestAsync(Envelope(), cred, remoteConfirmed: false, CancellationToken.None);

            Assert.Equal(LlmRejectionReason.VerificationFailed, result.Reason);
            Assert.Null(result.Proposal);
            Assert.DoesNotContain(SessionKey, result.Message);
            Assert.DoesNotContain("Bearer " + SessionKey, result.Message);
            Assert.Contains("[REDACTED]", result.Message); // redação efetivamente aplicada
        }


        [Fact]
        public async Task Adapter_ValidProposal_ParsesWithBearerHeaderOnly()
        {
            using var cred = LlmCredential.FromUserInput(SessionKey);
            var handler = OkJsonHandler(ValidProposalJson);
            using var http = new HttpClient(handler);
            var adapter = new LlmHttpAdapter(http);
            // Endpoint remoto + confirmação: cobre também o caminho da flag remoteConfirmed.
            var envelope = Envelope(new Uri("https://api.example.com/v1"));

            var result = await adapter.RequestAsync(envelope, cred, remoteConfirmed: true, CancellationToken.None);

            Assert.Equal(LlmRejectionReason.None, result.Reason);
            Assert.NotNull(result.Proposal);
            Assert.Equal("ppp-sclmove-t3", result.Proposal!.RecipeId);
            Assert.Equal(PatchOperationKind.ByteReplace, result.Proposal.Operation);
            Assert.Equal("AAAA", result.Proposal.Target.NewBytesBase64);

            var sent = handler.LastRequest;
            Assert.NotNull(sent);
            Assert.NotNull(sent!.Headers.Authorization);
            Assert.Equal("Bearer", sent.Headers.Authorization!.Scheme);
            Assert.Equal(SessionKey, sent.Headers.Authorization.Parameter);

            string body = handler.LastRequestBody ?? string.Empty;
            Assert.DoesNotContain(SessionKey, body); // chave NUNCA no body
        }

        [Fact]
        public async Task Adapter_InvalidBase64_Rejects()
        {
            using var cred = LlmCredential.FromUserInput(SessionKey);
            string broken = ValidProposalJson.Replace("\"AAAA\"", "\"###not-base64###\"", StringComparison.Ordinal);
            var handler = OkJsonHandler(broken);
            using var http = new HttpClient(handler);
            var adapter = new LlmHttpAdapter(http);

            var result = await adapter.RequestAsync(Envelope(), cred, remoteConfirmed: false, CancellationToken.None);

            Assert.Equal(LlmRejectionReason.MalformedPayload, result.Reason);
            Assert.Null(result.Proposal);
        }

        // ── Response modes (Jarvis-UI 2026-09-15) ─────────────────────────────────────────────
        // Real OpenAI-compatible endpoints wrap the model text in choices[0].message.content.
        // Chat mode surfaces that text; Proposal mode parses the content as strict PatchProposal.

        private static string WrapInOpenAiChat(string content) =>
            "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" +
            JsonSerializer.Serialize(content) + "}}]}";

        [Fact]
        public async Task Adapter_ChatMode_ReturnsTextContent()
        {
            using var cred = LlmCredential.FromUserInput(SessionKey);
            var handler = OkJsonHandler(WrapInOpenAiChat("Olá, Tidus!"));
            using var http = new HttpClient(handler);
            var adapter = new LlmHttpAdapter(http);
            var envelope = Envelope() with { Mode = LlmResponseMode.Chat };

            var result = await adapter.RequestAsync(envelope, cred, remoteConfirmed: false, CancellationToken.None);

            Assert.Equal(LlmRejectionReason.None, result.Reason);
            Assert.Null(result.Proposal);
            Assert.Equal("Olá, Tidus!", result.Message);
        }

        [Fact]
        public async Task Adapter_ChatMode_NonWrapperBody_FallsBackToRawText()
        {
            using var cred = LlmCredential.FromUserInput(SessionKey);
            var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage
            {
                Content = new StringContent("resposta em texto puro", Encoding.UTF8, "text/plain"),
            }));
            using var http = new HttpClient(handler);
            var adapter = new LlmHttpAdapter(http);
            var envelope = Envelope() with { Mode = LlmResponseMode.Chat };

            var result = await adapter.RequestAsync(envelope, cred, remoteConfirmed: false, CancellationToken.None);

            Assert.Equal(LlmRejectionReason.None, result.Reason);
            Assert.Equal("resposta em texto puro", result.Message);
        }

        [Fact]
        public async Task Adapter_ProposalMode_UnwrapsChatWrapper()
        {
            using var cred = LlmCredential.FromUserInput(SessionKey);
            // Endpoint OpenAI-compatible real: a proposal JSON viajando dentro do content.
            var handler = OkJsonHandler(WrapInOpenAiChat(ValidProposalJson));
            using var http = new HttpClient(handler);
            var adapter = new LlmHttpAdapter(http);

            var result = await adapter.RequestAsync(Envelope(), cred, remoteConfirmed: false, CancellationToken.None);

            Assert.Equal(LlmRejectionReason.None, result.Reason);
            Assert.NotNull(result.Proposal);
            Assert.Equal("ppp-sclmove-t3", result.Proposal!.RecipeId);
        }

        [Fact]
        public async Task Adapter_ProposalMode_WrapperWithPlainText_RejectsMalformed()
        {
            using var cred = LlmCredential.FromUserInput(SessionKey);
            // Chat comum em modo Proposal: o content é texto livre → gate honesto (MalformedPayload).
            var handler = OkJsonHandler(WrapInOpenAiChat("não sei fazer isso"));
            using var http = new HttpClient(handler);
            var adapter = new LlmHttpAdapter(http);

            var result = await adapter.RequestAsync(Envelope(), cred, remoteConfirmed: false, CancellationToken.None);

            Assert.Equal(LlmRejectionReason.MalformedPayload, result.Reason);
            Assert.Null(result.Proposal);
        }

        [Fact]
        public async Task Adapter_Envelope_DefaultsToProposalMode()
        {
            var envelope = Envelope();
            Assert.Equal(LlmResponseMode.Proposal, envelope.Mode);
        }

        // ── Agent mode / tool calling (Jarvis-UI 2026-09-15) ────────────────────────────────
        // O endpoint responde finish_reason="tool_calls" com message.tool_calls[]; o adapter
        // devolve as chamadas sem parsear content. No request, Messages+Tools serializam no
        // shape OpenAI (assistant echo carrega tool_calls; tool result carrega tool_call_id).

        private const string ToolCallResponseJson = """
            {
              "choices": [{
                "finish_reason": "tool_calls",
                "index": 0,
                "message": {
                  "role": "assistant",
                  "content": null,
                  "tool_calls": [{
                    "id": "call_abc123",
                    "type": "function",
                    "function": { "name": "read_bytes", "arguments": "{\"path\":\"kernel.bin\",\"offset\":0,\"length\":64}" }
                  }]
                }
              }]
            }
            """;

        [Fact]
        public async Task Adapter_AgentMode_ParsesToolCalls()
        {
            using var cred = LlmCredential.FromUserInput(SessionKey);
            var handler = OkJsonHandler(ToolCallResponseJson);
            using var http = new HttpClient(handler);
            var adapter = new LlmHttpAdapter(http);
            var envelope = Envelope() with
            {
                Mode = LlmResponseMode.Agent,
                Messages = new[] { LlmChatMessage.System("sys"), LlmChatMessage.User("read kernel.bin header") },
                Tools = new[] { new LlmToolSpec { Name = "read_bytes", Description = "read", ParametersJson = "{}" } },
            };

            var result = await adapter.RequestAsync(envelope, cred, remoteConfirmed: false, CancellationToken.None);

            Assert.Equal(LlmRejectionReason.None, result.Reason);
            Assert.Equal("tool_calls", result.FinishReason);
            Assert.NotNull(result.ToolCalls);
            var call = Assert.Single(result.ToolCalls!);
            Assert.Equal("call_abc123", call.Id);
            Assert.Equal("read_bytes", call.Name);
            Assert.Contains("kernel.bin", call.ArgumentsJson);
        }

        [Fact]
        public async Task Adapter_AgentMode_DataWrappedEnvelope_ParsesToolCalls()
        {
            // cline.bot / OpenCodex upstreams wrap the OpenAI body in {"data":{...},"success":true}.
            using var cred = LlmCredential.FromUserInput(SessionKey);
            var wrapped = "{\"success\":true,\"data\":" + ToolCallResponseJson + "}";
            var handler = OkJsonHandler(wrapped);
            using var http = new HttpClient(handler);
            var adapter = new LlmHttpAdapter(http);
            var envelope = Envelope() with
            {
                Mode = LlmResponseMode.Agent,
                Messages = new[] { LlmChatMessage.User("x") },
            };

            var result = await adapter.RequestAsync(envelope, cred, remoteConfirmed: false, CancellationToken.None);

            Assert.Equal(LlmRejectionReason.None, result.Reason);
            Assert.Equal("tool_calls", result.FinishReason);
            Assert.NotNull(result.ToolCalls);
            Assert.Equal("read_bytes", result.ToolCalls![0].Name);
        }

        [Fact]
        public async Task Adapter_AgentMode_SerializesMessagesAndTools()
        {
            using var cred = LlmCredential.FromUserInput(SessionKey);
            var handler = OkJsonHandler(WrapInOpenAiChat("done"));
            using var http = new HttpClient(handler);
            var adapter = new LlmHttpAdapter(http);
            var envelope = Envelope() with
            {
                Mode = LlmResponseMode.Agent,
                Messages = new LlmChatMessage[]
                {
                    LlmChatMessage.System("sys prompt"),
                    LlmChatMessage.User("u1"),
                    LlmChatMessage.AssistantToolCalls(null, new[]
                    {
                        new LlmToolCall { Id = "call_1", Name = "read_bytes", ArgumentsJson = "{\"path\":\"a.bin\"}" },
                    }),
                    LlmChatMessage.Tool("call_1", "00 11 22"),
                },
                Tools = new[]
                {
                    new LlmToolSpec
                    {
                        Name = "read_bytes", Description = "Read bytes",
                        ParametersJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}}}",
                    },
                },
            };

            var result = await adapter.RequestAsync(envelope, cred, remoteConfirmed: false, CancellationToken.None);

            Assert.Equal(LlmRejectionReason.None, result.Reason);
            using var doc = JsonDocument.Parse(handler.LastRequestBody!);
            var root = doc.RootElement;
            var messages = root.GetProperty("messages");
            Assert.Equal(4, messages.GetArrayLength());
            Assert.Equal("system", messages[0].GetProperty("role").GetString());
            Assert.Equal("call_1", messages[2].GetProperty("tool_calls")[0].GetProperty("id").GetString());
            Assert.Equal("read_bytes", messages[2].GetProperty("tool_calls")[0].GetProperty("function").GetProperty("name").GetString());
            Assert.Equal("tool", messages[3].GetProperty("role").GetString());
            Assert.Equal("call_1", messages[3].GetProperty("tool_call_id").GetString());
            var tools = root.GetProperty("tools");
            Assert.Equal("function", tools[0].GetProperty("type").GetString());
            Assert.Equal("read_bytes", tools[0].GetProperty("function").GetProperty("name").GetString());
            Assert.Equal(JsonValueKind.Object, tools[0].GetProperty("function").GetProperty("parameters").ValueKind);
        }

        [Fact]
        public async Task Adapter_AgentMode_StopWithContent_ReturnsText()
        {
            using var cred = LlmCredential.FromUserInput(SessionKey);
            var handler = OkJsonHandler(WrapInOpenAiChat("resposta final do agente"));
            using var http = new HttpClient(handler);
            var adapter = new LlmHttpAdapter(http);
            var envelope = Envelope() with
            {
                Mode = LlmResponseMode.Agent,
                Messages = new[] { LlmChatMessage.User("hi") },
            };

            var result = await adapter.RequestAsync(envelope, cred, remoteConfirmed: false, CancellationToken.None);

            Assert.Equal(LlmRejectionReason.None, result.Reason);
            Assert.Equal("resposta final do agente", result.Message);
            Assert.Null(result.ToolCalls);
        }


        // --- Helpers ----------------------------------------------------------------

        private static FakeHandler OkJsonHandler(string json) =>
            new((_, _) => Task.FromResult(new HttpResponseMessage
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            }));

        private static LlmRequestEnvelope Envelope(Uri? endpoint = null) => new()
        {
            Provider = "local",
            Endpoint = endpoint ?? new Uri("http://127.0.0.1:11434/v1"),
            ModelId = "test-model",
            PayloadPreview = "prompt aprovado (preview redigido)",
            DataKinds = new[] { LlmDataKind.RecipeDescription },
            MaxResponseBytes = 4096,
            Timeout = TimeSpan.FromSeconds(10),
        };

        /// <summary>DelegatingHandler fake — nenhuma chamada de rede real acontece nos testes.
        /// Captura request E body DENTRO do SendAsync (o adapter descarta o request após a
        /// chamada — ler o Content depois causaria ObjectDisposedException).</summary>
        private sealed class FakeHandler : DelegatingHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

            public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
                => _send = send;

            public HttpRequestMessage? LastRequest { get; private set; }
            public string? LastRequestBody { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                LastRequestBody = request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken);
                return await _send(request, cancellationToken);
            }
        }
    }
}


using FFXProjectEditor.Core.LLM;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.AiAssistant
{
    /// <summary>
    /// Sessão BYOK (bring-your-own-key) do Assistente IA (lane Jarvis-MAGIC-IA, 2026-08-16).
    ///
    /// Segurança por construção, espelhando o contrato P2-B (`docs/ai/P2B_CONTRATO_LLM_2026-07-31.md`):
    ///  - a key do usuário vive SÓ em memória (LlmCredential); Dispose() zera o buffer;
    ///  - a key entra somente no header Authorization: Bearer, nunca em body/log/exceção (LlmRedactor);
    ///  - endpoint não-loopback exige confirmação humana (host + payload preview) ANTES do request;
    ///  - o adapter espera que o endpoint devolva um <see cref="PatchProposal"/> JSON (parse estrito) — um
    ///    chat API "cru" devolve wrapper e vira MalformedPayload (gate honesto até existir gateway/template
    ///    que produza o proposal; faz parte da expansão de receitas, MINOR seguinte).
    ///
    /// Nada aqui persiste nem grava arquivos de jogo. A escrita real só acontece após receita comprovada
    /// + LlmGuard + aprovação humana (pipeline OperationExecutorV2), fora deste painel.
    /// </summary>
    internal sealed class AiAssistantSession : IDisposable
    {
        const int DefaultMaxResponseBytes = 24 * 1024;   // 24 KB / response
        // Gateways (cline.bot/OpenCodex) pad each response with provider_metadata
        // (costs, routing, attempt lists) that easily dwarfs the model payload —
        // agent rounds need a larger but still bounded ceiling.
        const int AgentMaxResponseBytes = 128 * 1024;    // 128 KB / agent round
        const int DefaultMaxProposalBytes = 2 * 1024;    // 2 KB / proposal
        static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(60);
        // Agent rounds ride reasoning models that think before emitting tool_calls — a single
        // round routinely exceeds the chat timeout. The loop itself is bounded by MaxRounds.
        static readonly TimeSpan AgentRoundTimeout = TimeSpan.FromSeconds(180);

        readonly HttpClient _http;
        readonly LlmHttpAdapter _adapter;
        readonly LlmCredential _credential;
        LlmSessionSettings _settings;

        public bool HasKey => _credential.HasKey;
        public string Provider => _settings.Provider;
        public string ModelId => _settings.ModelId;
        public Uri Endpoint => _settings.Endpoint;

        /// <summary>Session settings — o runner de Agent os repassa ao AgentToolContext (guard).</summary>
        public LlmSessionSettings Settings => _settings;

        public AiAssistantSession(LlmSessionSettings settings, string apiKey)
        {
            _settings = settings;
            _credential = LlmCredential.FromUserInput(apiKey);
            _http = new HttpClient();
            _adapter = new LlmHttpAdapter(_http);
        }

        /// <summary>Monta config padrão do perfil 1 (ByteReplace, opt-in habilitado só após conectar).</summary>
        public static LlmSessionSettings BuildSettings(string provider, string endpoint, string modelId)
        {
            var uri = new Uri(endpoint, UriKind.Absolute);
            return new LlmSessionSettings
            {
                IsEnabled = true, // só existe após o usuário conectar com a própria key
                Provider = string.IsNullOrWhiteSpace(provider) ? "openai-compatible" : provider,
                Endpoint = uri,
                ModelId = modelId,
                AllowedDataKinds = new[] { LlmDataKind.RecipeDescription, LlmDataKind.DiffContext },
                CapabilityFlags = LlmCapabilityFlag.ByteReplace, // perfil 1: só troca byte-local de receita comprovada
                MaxResponseBytes = DefaultMaxResponseBytes,
                MaxProposalBytes = DefaultMaxProposalBytes,
                RequestTimeout = DefaultRequestTimeout,
                RedactLogs = true,
            };
        }

        /// <summary>Endpoint não-loopback => confirmação humana obrigatória antes do request.</summary>
        public bool RequiresRemoteConfirmation => _settings.Endpoint.IsLoopback == false;

        /// <summary>Monta o envelope de uma chamada. PayloadPreview é redigido (LlmRedactor).</summary>
        public LlmRequestEnvelope BuildEnvelope(string commandText, LlmResponseMode mode)
        {
            // Chat = pergunta direta, resposta em texto (read-only — nada é aplicável).
            // Proposal = coerção do schema PatchProposal (contrato P2-B inalterado).
            string prompt = mode == LlmResponseMode.Chat
                ? "You are the assistant inside the FFX Project Editor (a Final Fantasy X HD modding tool). " +
                  "Answer plainly and concisely. User command: " + commandText
                : "Você é o assistente de edição do FFX Project Editor. " +
                  "Responda EXCLUSIVAMENTE com um JSON do schema PatchProposal " +
                  "(Campos: proposalId, capabilityId, recipeId, operation, target{relativePath,fileVersion,offset,length,newBytesBase64}, " +
                  "beforeHash, afterHash, semanticChange, justification, diff{beforeBytesHex,afterBytesHex,changedFields,humanSummary}, " +
                  "verifications[{kind,required,expected}], provider, modelId, promptTemplateVersion, createdAt). " +
                  "Nunca invente bytes executáveis: devolva offsets e bytes dentro de uma receita comprovada. Comando do usuário: " + commandText;

            return new LlmRequestEnvelope
            {
                Provider = _settings.Provider,
                Endpoint = _settings.Endpoint,
                ModelId = _settings.ModelId,
                PayloadPreview = LlmRedactor.BuildPreview(System.Text.Encoding.UTF8.GetBytes(prompt), 4096),
                DataKinds = _settings.AllowedDataKinds,
                MaxResponseBytes = _settings.MaxResponseBytes,
                Timeout = _settings.RequestTimeout,
                Mode = mode,
            };
        }

        /// <summary>Envia a chamada ao modelo e devolve o resultado (message sempre redigida).</summary>
        public Task<LlmResult> SendAsync(string commandText, LlmResponseMode mode, bool remoteConfirmed, CancellationToken ct)
            => _adapter.RequestAsync(BuildEnvelope(commandText, mode), _credential, remoteConfirmed, ct);

        /// <summary>Envelope multi-turno do modo Agent: histórico Messages + tool specs.</summary>
        public LlmRequestEnvelope BuildAgentEnvelope(
            System.Collections.Generic.IReadOnlyList<LlmChatMessage> messages,
            System.Collections.Generic.IReadOnlyList<LlmToolSpec> tools)
        {
            return new LlmRequestEnvelope
            {
                Provider = _settings.Provider,
                Endpoint = _settings.Endpoint,
                ModelId = _settings.ModelId,
                // PayloadPreview segue exigido pelo contrato — no Agent é só o resumo redigido
                // do turno (o body real sai de Messages, não deste campo).
                PayloadPreview = LlmRedactor.BuildPreview(
                    System.Text.Encoding.UTF8.GetBytes($"agent turn: {messages.Count} messages"), 256),
                DataKinds = _settings.AllowedDataKinds,
                MaxResponseBytes = AgentMaxResponseBytes,
                Timeout = AgentRoundTimeout,
                Mode = LlmResponseMode.Agent,
                Messages = messages,
                Tools = tools,
            };
        }

        /// <summary>Um round do loop agente — devolve texto final ou ToolCalls p/ executar.</summary>
        public Task<LlmResult> SendAgentAsync(
            System.Collections.Generic.IReadOnlyList<LlmChatMessage> messages,
            System.Collections.Generic.IReadOnlyList<LlmToolSpec> tools,
            bool remoteConfirmed, CancellationToken ct)
            => _adapter.RequestAsync(BuildAgentEnvelope(messages, tools), _credential, remoteConfirmed, ct);

        public void Dispose()
        {
            _credential.Dispose(); // zera a key da memória
            _http.Dispose();
        }
    }
}

using FFXProjectEditor.Resources;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.Core;
using FFXProjectEditor.Core.LLM;
using FFXProjectEditor.Diagnostics;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.AiAssistant
{
    /// <summary>Tipo semântico de uma linha da saída — dirige glifo e cor no DataTemplate.</summary>
    internal enum AiResultKind { Info, Chat, Proposal, Error }

    /// <summary>
    /// Uma linha da área de saída (título + detalhe + tipo visual). Nunca carrega payload/chave.
    /// Glyph/TitleBrush resolvem tokens do StudioTheme com fallback estático para testes
    /// (Application.Current é null fora do app).
    /// </summary>
    internal sealed class AiAssistantResult
    {
        public required string Title { get; init; }
        public required string Detail { get; init; }
        public AiResultKind Kind { get; init; } = AiResultKind.Info;

        public string Glyph => Kind switch
        {
            AiResultKind.Chat => "💬",
            AiResultKind.Proposal => "🧩",
            AiResultKind.Error => "⚠",
            _ => "●",
        };

        public IBrush TitleBrush => Kind switch
        {
            AiResultKind.Chat => Res("AccentCoolBrush", "#4A90D9"),
            AiResultKind.Proposal => Res("AccentWarmBrush", "#E8943A"),
            AiResultKind.Error => Res("DangerBrush", "#D95A6A"),
            _ => Res("TextMutedBrush", "#8BA3B5"),
        };

        internal static IBrush Res(string key, string fallback)
        {
            if (Application.Current?.TryGetResource(key, null, out object? v) == true && v is IBrush b)
                return b;
            return new SolidColorBrush(Color.Parse(fallback));
        }
    }

    /// <summary>Linha do histórico de auditoria — envelope UI do LlmHistoryEntry (core fica limpo).</summary>
    internal sealed class AiAssistantHistoryRow
    {
        public required LlmHistoryEntry Entry { get; init; }
        public string ProposalId => Entry.ProposalId;
        public string CapabilityId => Entry.CapabilityId;
        public string RecipeId => Entry.RecipeId;
        public string TimestampText => Entry.Timestamp.ToLocalTime().ToString("dd/MM HH:mm:ss");
        public string SubtitleText => $"{Entry.CapabilityId} / {Entry.RecipeId}";
        public string DecisionText => Entry.Decision.ToString();
        public IBrush DecisionBrush => Entry.Decision switch
        {
            ReviewDecision.Approved => AiAssistantResult.Res("SuccessBrush", "#4CAF8A"),
            ReviewDecision.Rejected => AiAssistantResult.Res("DangerBrush", "#D95A6A"),
            ReviewDecision.Expired => AiAssistantResult.Res("TextMutedBrush", "#8BA3B5"),
            _ => AiAssistantResult.Res("WarningBrush", "#D4A84A"),
        };
    }

    /// <summary>Estado semântico da sessão — dirige a cor da pill de status no card.</summary>
    internal enum SessionStatusKind { Idle, Connected, Failed }

    /// <summary>
    /// Preset de provider (todos OpenAI-compatible — o adapter não muda o shape do request;
    /// Provider é metadado de envelope/auditoria). Endpoint/Model null = preset "custom",
    /// que não sobrescreve os campos do usuário.
    /// </summary>
    internal sealed record ProviderPreset(string Id, string Label, string? Endpoint, string? Model);

    /// <summary>
    /// DataModel do Assistente IA (BYOK). Gerencia a sessão em memória (AiAssistantSession),
    /// o envio de comandos via LlmHttpAdapter (contrato P2-B + modo Chat read-only) e o
    /// histórico de auditoria. A key vive só em memória e é zerada em ClearSession();
    /// provider/endpoint/model/mode persistem em ai-assistant.json (nunca a key).
    /// </summary>
    internal partial class AiAssistant_DataModel : ObservableObject
    {
        const string Area = "AiAssistant";

        // Test seam: tests apontam para um arquivo temporário. Produção usa LocalAppData
        // (mesma convenção de last-project.txt / game-root.txt).
        internal static string? SettingsPathOverride;
        static string SettingsFilePath => SettingsPathOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FFXProjectEditor", "ai-assistant.json");

        sealed class PersistedSettings
        {
            public string? Provider { get; set; }
            public string? Endpoint { get; set; }
            public string? ModelId { get; set; }
            public bool ChatMode { get; set; } = true; // legado — fallback quando Mode ausente
            public string? Mode { get; set; }        // "chat" | "proposal" | "agent"
        }

        readonly ObservableCollection<AiAssistantResult> resultLines = new();
        readonly ObservableCollection<AiAssistantHistoryRow> history = new();
        AiAssistantSession? session;
        CancellationTokenSource? sendCts;

        public ObservableCollection<AiAssistantResult> ResultLines => resultLines;
        public ObservableCollection<AiAssistantHistoryRow> History => history;

        [ObservableProperty] string provider = "openai-compatible";
        [ObservableProperty] string endpoint = "http://localhost:11434/v1/chat/completions";
        [ObservableProperty] string modelId = "deepseek-v4-flash";
        [ObservableProperty] string commandText = "";
        [ObservableProperty] string sessionStatus = AiAssistantLabels.SessionNotConnected;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SessionStatusBrush))]
        SessionStatusKind sessionStatusKind = SessionStatusKind.Idle;
        // CanSend precisa de NotifyPropertyChangedFor — sem isso o botão Send ficava
        // desabilitado para sempre após Connect (bug real: o módulo não enviava nada).
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanSend))]
        bool isSessionActive;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanSend))]
        bool isSending;
        [ObservableProperty] bool needsRemoteConfirm;
        [ObservableProperty] bool remoteConfirmed;
        // Modo 3-state: 0=Chat, 1=Proposal, 2=Agent. Proxies bool dirigem as pills;
        // ChatMode/IsProposalMode ficam por compat com persistência antiga e testes.
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ChatMode), nameof(IsChatMode), nameof(IsProposalMode), nameof(IsAgentMode))]
        int modeIndex = 0; // default Chat: funciona com qualquer endpoint OpenAI-compatible
        [ObservableProperty] int selectedProviderIndex = -1;

        /// <summary>Proposta validada pelo guard aguardando decisão humana (Agent/Proposal).</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasPendingProposal), nameof(PendingProposalSummary), nameof(PendingProposalDetail))]
        PatchProposal? pendingProposal;

        /// <summary>
        /// Confirmação humana final antes da escrita — o code-behind injeta o dialog
        /// (ConfirmYesNoAsync com o plano). Null = nunca confirma (headless/testes
        /// devem injetar um stub que devolve true/false explicitamente).
        /// </summary>
        public Func<OperationPlan, Task<bool>>? ConfirmPlan { get; set; }

        /// <summary>
        /// Presets do dropdown de Provider — os endpoints OpenAI-compatible mais usados
        /// (o adapter sempre posta o shape OpenAI; Provider é metadado de envelope/auditoria).
        /// Selecionar aplica Endpoint+Model; o último ("custom") só troca o Provider id
        /// e preserva os campos editados à mão.
        /// </summary>
        public IReadOnlyList<ProviderPreset> ProviderPresets { get; } = new List<ProviderPreset>
        {
            new("openai",            "OpenAI",              "https://api.openai.com/v1/chat/completions",                       "gpt-4o-mini"),
            new("google-gemini",     "Google Gemini",       "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions", "gemini-2.5-flash"),
            new("anthropic",         "Anthropic (Claude)",  "https://api.anthropic.com/v1/chat/completions",                    "claude-sonnet-4-5"),
            new("deepseek",          "DeepSeek",            "https://api.deepseek.com/v1/chat/completions",                     "deepseek-chat"),
            new("openrouter",        "OpenRouter",          "https://openrouter.ai/api/v1/chat/completions",                    "openai/gpt-4o-mini"),
            new("groq",              "Groq",                "https://api.groq.com/openai/v1/chat/completions",                  "llama-3.3-70b-versatile"),
            new("xai",               "xAI (Grok)",          "https://api.x.ai/v1/chat/completions",                             "grok-3-mini"),
            new("mistral",           "Mistral",             "https://api.mistral.ai/v1/chat/completions",                       "mistral-small-latest"),
            new("together",          "Together AI",         "https://api.together.xyz/v1/chat/completions",                     "meta-llama/Llama-3.3-70B-Instruct-Turbo"),
            new("nvidia-nim",        "NVIDIA NIM",          "https://integrate.api.nvidia.com/v1/chat/completions",             "meta/llama-3.3-70b-instruct"),
            new("perplexity",        "Perplexity",          "https://api.perplexity.ai/chat/completions",                       "sonar"),
            new("cerebras",          "Cerebras",            "https://api.cerebras.ai/v1/chat/completions",                      "llama-3.3-70b"),
            new("verboo",            "Verboo",              "https://code.verboo.ai/router/v1/chat/completions",                "deepseek-v4-flash"),
            new("ollama",            "Ollama (local)",      "http://localhost:11434/v1/chat/completions",                       "qwen3-vl:8b"),
            new("lmstudio",          "LM Studio (local)",   "http://localhost:1234/v1/chat/completions",                        "local-model"),
            new("openai-compatible", Strings.AiAssistant_ProviderCustom, null, null),
        };

        /// <summary>Proxy legado: true=Chat; false=Proposal (preserva semântica antiga).</summary>
        public bool ChatMode
        {
            get => ModeIndex == 0;
            set { if (value) ModeIndex = 0; else if (ModeIndex == 0) ModeIndex = 1; }
        }

        /// <summary>Pill "Chat" (0).</summary>
        public bool IsChatMode { get => ModeIndex == 0; set { if (value) ModeIndex = 0; } }

        /// <summary>Pill "Proposal" (1).</summary>
        public bool IsProposalMode { get => ModeIndex == 1; set { if (value) ModeIndex = 1; } }

        /// <summary>Pill "Agent" (2) — loop com tools locais read-only + propostas.</summary>
        public bool IsAgentMode { get => ModeIndex == 2; set { if (value) ModeIndex = 2; } }

        /// <summary>Proposta pendente visível no card de revisão.</summary>
        public bool HasPendingProposal => PendingProposal is not null;

        public string PendingProposalSummary => PendingProposal is { } p
            ? $"{p.CapabilityId} · {p.RecipeId} · {p.Target.RelativePath} @ 0x{p.Target.Offset:X} len {p.Target.Length}"
            : string.Empty;

        public string PendingProposalDetail => PendingProposal is { } p
            ? $"{p.Diff.HumanSummary}\n{p.SemanticChange}"
            : string.Empty;

        // Key do usuário (BYOK) — SÓ em memória, nunca persistida/logada. Zerada em TakeApiKey().
        [ObservableProperty] string apiKeyText = "";

        public bool CanSend => IsSessionActive && !IsSending;

        /// <summary>Host remoto exibido no painel de confirmação (o gate mostra o que confirma).</summary>
        public string RemoteHost => Endpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri)
            ? uri.Host : Endpoint;

        /// <summary>Aviso do gate remoto com o host real embutido.</summary>
        public string RemoteHostWarning => string.Format(Strings.AiAssistant_RemoteHostWarning, RemoteHost);

        /// <summary>Fundo da pill de status: idle=panel, conectado=verde, falha=vermelho.</summary>
        public IBrush SessionStatusBrush => SessionStatusKind switch
        {
            SessionStatusKind.Connected => AiAssistantResult.Res("SuccessBrush", "#4CAF8A"),
            SessionStatusKind.Failed => AiAssistantResult.Res("DangerBrush", "#D95A6A"),
            _ => AiAssistantResult.Res("PanelAltBrush", "#15212C"),
        };

        public AiAssistant_DataModel()
        {
            LoadPersisted();
            // Sync do índice sem disparar o apply (senão clobberava endpoint/model carregados).
            selectedProviderIndex = IndexOfProvider(Provider);
        }

        int IndexOfProvider(string id)
        {
            for (int i = 0; i < ProviderPresets.Count; i++)
                if (ProviderPresets[i].Id == id) return i;
            return ProviderPresets.Count - 1; // desconhecido => "custom"
        }

        /// <summary>Usuário escolheu um preset: aplica endpoint+model (custom preserva).</summary>
        partial void OnSelectedProviderIndexChanged(int value)
        {
            if (value < 0 || value >= ProviderPresets.Count) return;
            var p = ProviderPresets[value];
            Provider = p.Id;
            if (p.Endpoint is { } ep) Endpoint = ep;
            if (p.Model is { } m) ModelId = m;
        }

        /// <summary>Consome a key digitada (devolve e zera a propriedade) para conectar a sessão.</summary>
        public string TakeApiKey()
        {
            string key = ApiKeyText ?? string.Empty;
            ApiKeyText = string.Empty; // zera do campo o mais cedo possível
            return key;
        }

        /// <summary>Conecta a sessão com a key do usuário (BYOK). Repõe a sessão anterior (zera key antiga).</summary>
        public void ConnectSession(string apiKey)
        {
            try
            {
                string key = (apiKey ?? string.Empty).Trim();
                if (key.Length == 0)
                {
                    SessionStatus = AiAssistantLabels.SessionNoKey;
                    DebugLog.Warn(Area, "connect ignored: empty key");
                    return;
                }

                session?.Dispose(); // zera key anterior se houver
                session = new AiAssistantSession(AiAssistantSession.BuildSettings(Provider, Endpoint, ModelId), key);

                IsSessionActive = session.HasKey;
                NeedsRemoteConfirm = session.RequiresRemoteConfirmation;
                RemoteConfirmed = false;
                SessionStatus = IsSessionActive ? AiAssistantLabels.SessionConnected : AiAssistantLabels.SessionFailed;
                SessionStatusKind = IsSessionActive ? SessionStatusKind.Connected : SessionStatusKind.Failed;

                ResultAdd(AiAssistantLabels.TitleResult,
                    $"Provider={Provider} · Model={ModelId} · Endpoint={Endpoint}" +
                    (NeedsRemoteConfirm ? Strings.U_Aia_RemoteSuffix : Strings.U_Aia_LoopbackSuffix));
                DebugLog.Info(Area, $"session connected provider={Provider} model={ModelId} remote={NeedsRemoteConfirm}");
            }
            catch (Exception ex)
            {
                SessionStatus = AiAssistantLabels.SessionFailed;
                SessionStatusKind = SessionStatusKind.Failed;
                ResultAdd(AiAssistantLabels.TitleResult, LlmRedactor.Redact(ex.Message), AiResultKind.Error);
                DebugLog.Error(Area, "connect session failed", ex);
            }
        }

        /// <summary>Encerra a sessão e zera a key da memória.</summary>
        public void ClearSession()
        {
            sendCts?.Cancel();
            session?.Dispose();
            session = null;
            PendingProposal = null; // sem sessão não há settings pra validar/aprovar
            IsSessionActive = false;
            NeedsRemoteConfirm = false;
            RemoteConfirmed = false;
            SessionStatus = AiAssistantLabels.SessionCleared;
            SessionStatusKind = SessionStatusKind.Idle;
            DebugLog.Info(Area, "session cleared; key zeroized");
        }

        /// <summary>Cancela o envio em voo (o adapter responde Reason=Cancelled).</summary>
        public void CancelSend() => sendCts?.Cancel();

        /// <summary>Envia o comando atual para o modelo (modo Chat ou Proposal). Não escreve arquivos.</summary>
        public async Task SendCommandAsync()
        {
            if (session is null || !IsSessionActive || IsSending)
            {
                if (!IsSending) SessionStatus = AiAssistantLabels.SessionNotConnected;
                return;
            }

            string cmd = CommandText.Trim();
            if (cmd.Length == 0) return;

            if (NeedsRemoteConfirm && !RemoteConfirmed)
            {
                ResultAdd(AiAssistantLabels.TitleRemoteConfirm, AiAssistantLabels.RemoteConfirmNeeded, AiResultKind.Error);
                return;
            }

            sendCts?.Cancel();
            sendCts = new CancellationTokenSource();
            CancellationToken ct = sendCts.Token;
            IsSending = true;
            var mode = ModeIndex switch
            {
                2 => LlmResponseMode.Agent,
                1 => LlmResponseMode.Proposal,
                _ => LlmResponseMode.Chat,
            };
            DebugLog.Info(Area, $"send command to {Endpoint} mode={mode} (remoteConfirmed={RemoteConfirmed})");
            try
            {
                if (mode == LlmResponseMode.Agent)
                {
                    await RunAgentAsync(cmd, ct);
                    return;
                }

                LlmResult result = await session.SendAsync(cmd, mode, RemoteConfirmed, ct);

                if (result.Reason != LlmRejectionReason.None)
                {
                    // Rejeição tipada: título = razão localizada (RejectReason saiu do limbo —
                    // era código morto até aqui), detalhe = mensagem redigida do adapter.
                    string reason = AiAssistantLabels.RejectReason(result.Reason);
                    ResultAdd(string.IsNullOrEmpty(reason) ? AiAssistantLabels.TitleResult : reason,
                        result.Message, AiResultKind.Error);
                }
                else if (!string.IsNullOrEmpty(result.Message))
                {
                    ResultAdd(AiAssistantLabels.TitleResult, result.Message,
                        mode == LlmResponseMode.Chat ? AiResultKind.Chat : AiResultKind.Info);
                }

                if (result.Proposal is { } p)
                {
                    PendingProposal = p;
                    ResultAdd(AiAssistantLabels.TitleProposal, PendingProposalSummary + " :: " + p.Diff.HumanSummary,
                        AiResultKind.Proposal);
                    AddHistoryRow(p, ReviewDecision.Pending);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                ResultAdd(AiAssistantLabels.TitleResult, Strings.AiAssistant_Cancelled, AiResultKind.Info);
            }
            catch (Exception ex)
            {
                ResultAdd(AiAssistantLabels.TitleResult, LlmRedactor.Redact(ex.Message), AiResultKind.Error);
                DebugLog.Error(Area, "send command failed", ex);
            }
            finally
            {
                IsSending = false;
            }
        }

        void ResultAdd(string title, string detail, AiResultKind kind = AiResultKind.Info)
        {
            resultLines.Insert(0, new AiAssistantResult { Title = title, Detail = detail, Kind = kind });
            while (resultLines.Count > 200) resultLines.RemoveAt(resultLines.Count - 1);
        }

        // ── Modo Agent ─────────────────────────────────────────────────────────

        /// <summary>
        /// Loop do agente: modelo → tool_calls → tools locais read-only (PathGuard) →
        /// submit_proposal → PendingProposal. Nenhuma escrita aqui — a aplicação real
        /// só acontece em <see cref="ApproveProposalAsync"/> via OperationExecutorV2.
        /// </summary>
        async Task RunAgentAsync(string cmd, CancellationToken ct)
        {
            var project = Project_Service.Instance;
            string? workspace = project.ProjectPath;
            if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace))
            {
                ResultAdd(AiAssistantLabels.TitleResult, Strings.AiAssistant_AgentNeedsWorkspace, AiResultKind.Error);
                return;
            }

            var ctx = new AgentToolContext
            {
                WorkspaceRoot = workspace,
                OutputRoot = project.Path_OutputRoot,
                Settings = session!.Settings,
            };
            var runner = new AiAgentRunner();
            runner.Row += ResultAdd;
            runner.ProposalPending += OnProposalAccepted;
            await runner.RunAsync(
                (msgs, tools, c) => session!.SendAgentAsync(msgs, tools, RemoteConfirmed, c),
                ctx, cmd, ct);
        }

        void OnProposalAccepted(PatchProposal p)
        {
            PendingProposal = p;
            ResultAdd(AiAssistantLabels.TitleProposal,
                PendingProposalSummary + "\n" + p.Diff.HumanSummary, AiResultKind.Proposal);
            AddHistoryRow(p, ReviewDecision.Pending);
            DebugLog.Info(Area, $"proposal {p.ProposalId} accepted by guard — awaiting human review");
        }

        // ── Revisão humana: Approve → plan → confirmação → executor → receipt ────

        /// <summary>
        /// Aprova a proposta pendente: LlmGuard → ProposalToPlanMapper → confirmação
        /// humana (ConfirmPlan, injetado pela view) → OperationExecutorV2 → receipt.
        /// A escrita só acontece após a confirmação do plano pelo usuário.
        /// </summary>
        public async Task ApproveProposalAsync()
        {
            if (PendingProposal is not { } p || session is null) return;

            var project = Project_Service.Instance;
            string? source = project.ProjectPath;
            string? output = project.Path_OutputRoot;
            if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source))
            {
                ResultAdd(AiAssistantLabels.TitleResult, Strings.AiAssistant_AgentNeedsWorkspace, AiResultKind.Error);
                return;
            }
            if (string.IsNullOrWhiteSpace(output))
            {
                ResultAdd(AiAssistantLabels.TitleResult, Strings.AiAssistant_AgentNeedsOutput, AiResultKind.Error);
                return;
            }
            if (!LlmCapabilityCatalog.TryGetDescriptor(p.CapabilityId, out var capability) || capability is null)
            {
                ResultAdd(AiAssistantLabels.TitleProposal,
                    $"rejected: unknown capability '{p.CapabilityId}'", AiResultKind.Error);
                MarkDecision(p, ReviewDecision.Rejected);
                PendingProposal = null;
                return;
            }

            string full = Path.GetFullPath(Path.Combine(source, p.Target.RelativePath));
            if (!File.Exists(full))
            {
                ResultAdd(AiAssistantLabels.TitleProposal,
                    $"rejected: target file gone: {p.Target.RelativePath}", AiResultKind.Error);
                MarkDecision(p, ReviewDecision.Rejected);
                PendingProposal = null;
                return;
            }
            string actualBefore = OperationExecutorV2.ComputeSha256(full);

            var catalog = new WriterAdapterCatalog();
            var plan = ProposalToPlanMapper.MapToPlan(
                p, session.Settings, capability, source, output, catalog, actualBefore, out var guard);
            if (plan is null)
            {
                ResultAdd(AiAssistantLabels.TitleProposal,
                    $"rejected by guard: {guard.Reason} — {guard.Message}", AiResultKind.Error);
                MarkDecision(p, ReviewDecision.Rejected);
                PendingProposal = null;
                return;
            }

            // Última porta humana: a view mostra o plano (arquivo/offset/diff/destino)
            // e só devolve true se o usuário confirmar explicitamente.
            bool confirmed = ConfirmPlan is not null && await ConfirmPlan(plan);
            if (!confirmed)
            {
                ResultAdd(AiAssistantLabels.TitleProposal, Strings.AiAssistant_ApplyCancelled, AiResultKind.Info);
                return; // proposta segue pendente — o usuário pode aprovar depois
            }

            IsSending = true;
            try
            {
                var result = await new OperationExecutorV2(catalog).ExecuteAsync(plan);
                if (result.Success)
                {
                    ResultAdd(AiAssistantLabels.TitleProposal,
                        string.Format(Strings.AiAssistant_AppliedReceipt, result.ReceiptId,
                            result.FilesWritten, result.BytesWritten, plan.OutputRoot),
                        AiResultKind.Proposal);
                    MarkDecision(p, ReviewDecision.Approved);
                }
                else
                {
                    ResultAdd(AiAssistantLabels.TitleProposal,
                        $"apply failed: {result.ErrorMessage}\n{result.RecoveryInstructions}",
                        AiResultKind.Error);
                    MarkDecision(p, ReviewDecision.Rejected);
                }
            }
            finally
            {
                IsSending = false;
                PendingProposal = null;
            }
        }

        /// <summary>Rejeita a proposta pendente — nada é escrito, decisão registrada.</summary>
        public void RejectProposal()
        {
            if (PendingProposal is not { } p) return;
            MarkDecision(p, ReviewDecision.Rejected);
            ResultAdd(AiAssistantLabels.TitleProposal,
                string.Format(Strings.AiAssistant_ProposalRejected, p.ProposalId), AiResultKind.Info);
            PendingProposal = null;
            DebugLog.Info(Area, $"proposal {p.ProposalId} rejected by user");
        }

        void MarkDecision(PatchProposal p, ReviewDecision decision)
        {
            for (int i = 0; i < history.Count; i++)
            {
                if (history[i].Entry.ProposalId != p.ProposalId) continue;
                history[i] = new AiAssistantHistoryRow { Entry = history[i].Entry with { Decision = decision } };
                return;
            }
            AddHistoryRow(p, decision); // proposta sem linha (não deveria) — registra mesmo assim
        }

        void AddHistoryRow(PatchProposal p, ReviewDecision decision)
        {
            // Auditoria local (nunca carrega payload/chave — regra do LlmHistoryEntry).
            history.Add(new AiAssistantHistoryRow
            {
                Entry = new LlmHistoryEntry
                {
                    ProposalId = p.ProposalId,
                    CapabilityId = p.CapabilityId,
                    RecipeId = p.RecipeId,
                    Provider = p.Provider,
                    ModelId = p.ModelId,
                    PromptTemplateVersion = p.PromptTemplateVersion,
                    Decision = decision,
                    Timestamp = DateTimeOffset.UtcNow,
                }
            });
        }

        // ── Persistência de config (provider/endpoint/model/mode) — a key NUNCA entra ──

        void LoadPersisted()
        {
            try
            {
                if (!File.Exists(SettingsFilePath)) return;
                var s = JsonSerializer.Deserialize<PersistedSettings>(File.ReadAllText(SettingsFilePath));
                if (s is null) return;
                if (!string.IsNullOrWhiteSpace(s.Provider)) Provider = s.Provider;
                if (!string.IsNullOrWhiteSpace(s.Endpoint)) Endpoint = s.Endpoint;
                if (!string.IsNullOrWhiteSpace(s.ModelId)) ModelId = s.ModelId;
                ModeIndex = s.Mode switch
                {
                    "agent" => 2,
                    "proposal" => 1,
                    "chat" => 0,
                    _ => s.ChatMode ? 0 : 1, // arquivos antigos: só ChatMode existia
                };
            }
            catch (Exception ex)
            {
                // Arquivo corrompido/ilegível → defaults; nunca derruba o painel por preferência.
                DebugLog.Warn(Area, $"settings load failed ({ex.GetType().Name}) — using defaults");
            }
        }

        void PersistSettings()
        {
            try
            {
                string path = SettingsFilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(new PersistedSettings
                {
                    Provider = Provider,
                    Endpoint = Endpoint,
                    ModelId = ModelId,
                    ChatMode = ChatMode, // legado — arquivos antigos ainda leem
                    Mode = ModeIndex switch { 2 => "agent", 1 => "proposal", _ => "chat" },
                }));
            }
            catch (Exception ex)
            {
                DebugLog.Warn(Area, $"settings save failed ({ex.GetType().Name})");
            }
        }

        partial void OnProviderChanged(string value)
        {
            PersistSettings();
            // Provider escrito programaticamente (load/teste) reflete no dropdown.
            int idx = IndexOfProvider(value);
            if (idx != SelectedProviderIndex)
            {
                selectedProviderIndex = idx;
                OnPropertyChanged(nameof(SelectedProviderIndex));
            }
        }
        partial void OnEndpointChanged(string value)
        {
            PersistSettings();
            OnPropertyChanged(nameof(RemoteHost));
            OnPropertyChanged(nameof(RemoteHostWarning));
        }
        partial void OnModelIdChanged(string value) => PersistSettings();
        partial void OnModeIndexChanged(int value) => PersistSettings();
    }
}

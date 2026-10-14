using FFXProjectEditor.Core;
using FFXProjectEditor.Core.LLM;
using FFXProjectEditor.Diagnostics;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.AiAssistant
{
    /// <summary>Um round do loop: envia o histórico + specs e devolve texto final ou tool calls.</summary>
    internal delegate Task<LlmResult> AgentRound(
        IReadOnlyList<LlmChatMessage> messages,
        IReadOnlyList<LlmToolSpec> tools,
        CancellationToken ct);

    /// <summary>
    /// Agent loop (Jarvis-UI, 2026-09-15): multi-turn chat/completions com tools OpenAI.
    /// Cada round o modelo responde (stop) ou pede tool_calls — executadas LOCALMENTE
    /// (read-only + submit_proposal) e re-enviadas como role="tool". Caps duros: rounds,
    /// tool calls e bytes de saída por comando (budgets do AgentToolContext) contêm loops
    /// e prompt-injection.
    ///
    /// O runner nunca escreve arquivos de jogo. submit_proposal só enfileira um
    /// PatchProposal validado pra revisão humana; a escrita real segue
    /// LlmGuard → ProposalToPlanMapper → aprovação humana → OperationExecutorV2.
    ///
    /// Testabilidade: o loop depende do delegate <see cref="AgentRound"/> (a sessão real
    /// pluga SendAgentAsync); testes alimentam respostas roteirizadas sem rede.
    /// </summary>
    internal sealed class AiAgentRunner
    {
        const int MaxRounds = 8;

        readonly AgentToolRegistry _tools = new();

        /// <summary>UI sink — linhas title/detail/kind no painel de saída.</summary>
        public event Action<string, string, AiResultKind>? Row;

        /// <summary>Uma proposta validada pelo guard está pronta pra revisão humana.</summary>
        public event Action<PatchProposal>? ProposalPending;

        /// <summary>Última proposta aceita pelo guard (pra auditoria no histórico).</summary>
        public PatchProposal? LastAcceptedProposal { get; private set; }

        /// <summary>Executa um comando do usuário pelo loop agente.</summary>
        public async Task RunAsync(
            AgentRound round,
            AgentToolContext ctx,
            string command,
            CancellationToken ct)
        {
            // submit_proposal aprova no guard DENTRO do executor da tool — o runner
            // repõe o evento pra UI enfileirar a revisão humana (Approve/Reject).
            ctx.ProposalAccepted += p =>
            {
                LastAcceptedProposal = p;
                ProposalPending?.Invoke(p);
            };

            var messages = new List<LlmChatMessage>
            {
                LlmChatMessage.System(BuildSystemPrompt(ctx)),
                LlmChatMessage.User(command),
            };

            for (int r = 0; r < MaxRounds; r++)
            {
                ct.ThrowIfCancellationRequested();
                LlmResult result = await round(messages, _tools.Specs, ct);

                if (result.Reason != LlmRejectionReason.None)
                {
                    Row?.Invoke("Agent", result.Message, AiResultKind.Error);
                    return;
                }

                if (result.ToolCalls is { Count: > 0 } calls)
                {
                    // Replay exato do turno assistant (a API exige o echo antes dos results).
                    messages.Add(LlmChatMessage.AssistantToolCalls(result.AssistantContent, calls));
                    foreach (var call in calls)
                    {
                        ct.ThrowIfCancellationRequested();
                        Row?.Invoke($"⚙ {call.Name}", call.ArgumentsJson, AiResultKind.Info);
                        string output = _tools.Execute(call, ctx);
                        Row?.Invoke($"⚙ {call.Name} →", output, AiResultKind.Info);
                        messages.Add(LlmChatMessage.Tool(call.Id, output));
                    }
                    continue;
                }

                Row?.Invoke("Agent", result.Message, AiResultKind.Chat);
                return;
            }

            Row?.Invoke("Agent",
                "stopped: max agent rounds reached — narrow the command or continue manually",
                AiResultKind.Error);
        }


        /// <summary>
        /// System prompt do agente: papel, ferramentas, layout real do workspace
        /// (Path_* conhecidos do Project_Service) e o contexto do editor
        /// (módulo/arquivo/registro aberto via EditorContextHub).
        /// </summary>
        static string BuildSystemPrompt(AgentToolContext ctx)
        {
            // Project_Service.Instance.Path_* combina com ProjectPath — fora de workspace
            // carregado isso quebra; os hints de layout são convenções relativas e seguras.
            var project = Project_Service.Instance;
            string gameRoot = "(unset)";
            try { gameRoot = project.Path_GameInstallRoot ?? "(unset)"; } catch { }

            var ec = EditorContextHub.Current;
            string context = $"module={ec.ModuleId}"
                + (ec.FilePath is { } f ? $", file={f}" : "")
                + (ec.RecordLabel is { } r ? $", record={r}" : "")
                + (ec.RecordSummary is { } s ? $", recordDetail={s}" : "");

            return
                "You are the AGENT inside the FFX Project Editor, a Final Fantasy X HD modding tool. " +
                "You can inspect the user's extracted game workspace with the provided tools and submit " +
                "byte-level patch proposals for human review.\n\n" +
                "RULES:\n" +
                "- All file paths are RELATIVE to the workspace root. Never use absolute paths.\n" +
                "- Investigate before proposing: use describe_file / read_bytes to find real offsets and " +
                "current bytes, and ALWAYS call sha256_file on the target before submit_proposal " +
                "(beforeHash must equal the real hash or the guard rejects it).\n" +
                "- Only propose recipes where executable=true in list_recipes; capabilityId must match the " +
                "recipe's capability. operation is always 0 (ByteReplace); target.offset/length and " +
                "newBytesBase64 must be real — never invent bytes. Send afterHash as \"pending-stage\" " +
                "(the real post-patch hash is computed locally — you cannot predict SHA-256).\n" +
                "- submit_proposal never writes: a human approves or rejects the diff. If a proposal is " +
                "rejected by the guard, read the reason, fix it, and retry.\n" +
                "- Keep answers short; when done, summarize what you found and what you proposed.\n\n" +
                $"WORKSPACE ROOT: {ctx.WorkspaceRoot}\n" +
                "Known layout (relative): monsters=jppc/battle/mon (m###.bin), " +
                "kernel=jppc/battle/kernel, battle=jppc/battle/btl, events=jppc/event/obj\n" +
                $"Game install: {gameRoot} | Output: {ctx.OutputRoot ?? "(auto)"}\n" +
                $"Editor context now: {context}";
        }
    }
}

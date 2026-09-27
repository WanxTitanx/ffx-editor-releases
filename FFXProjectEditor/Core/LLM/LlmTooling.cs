using System.Collections.Generic;

namespace FFXProjectEditor.Core.LLM;

/// <summary>
/// Tool surface do modo Agent (Jarvis-UI, 2026-09-15). O endpoint continua sendo
/// OpenAI-compatible chat/completions — a diferença é que o request carrega
/// <c>tools</c> e a resposta pode vir com <c>tool_calls</c> em vez de content.
/// O loop agente executa as chamadas LOCALMENTE (read-only + submit_proposal)
/// e devolve os resultados como mensagens <c>role="tool"</c>.
///
/// INVARIANTE: nenhum tool escreve arquivo — a única saída "mutável" é
/// <c>submit_proposal</c>, que produz um <see cref="PatchProposal"/> validado
/// pelo <see cref="LlmGuard"/> e fica pendente de aprovação humana. O caminho de
/// escrita real continua sendo ProposalToPlanMapper → OperationExecutorV2,
/// exatamente como o Proposal mode manual.
/// </summary>
public sealed record LlmToolSpec
{
    public required string Name { get; init; }
    public required string Description { get; init; }

    /// <summary>JSON Schema (raw) do objeto de parâmetros da tool.</summary>
    public required string ParametersJson { get; init; }
}

/// <summary>Uma chamada de tool pedida pelo modelo (choices[0].message.tool_calls[i]).</summary>
public sealed record LlmToolCall
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Arguments como JSON cru — o executor faz o parse (args nunca viram código).</summary>
    public required string ArgumentsJson { get; init; }
}

/// <summary>
/// Uma mensagem do histórico multi-turno do modo Agent. Serializa no shape
/// OpenAI: system/user → {role, content}; assistant → {role, content, tool_calls?};
/// tool → {role:"tool", tool_call_id, content}.
/// </summary>
public sealed record LlmChatMessage
{
    public required string Role { get; init; }
    public string? Content { get; init; }
    public string? ToolCallId { get; init; }
    public IReadOnlyList<LlmToolCall>? ToolCalls { get; init; }

    public static LlmChatMessage System(string content) => new() { Role = "system", Content = content };
    public static LlmChatMessage User(string content) => new() { Role = "user", Content = content };

    /// <summary>Echo do turno assistant que pediu tools (a API exige o replay exato).</summary>
    public static LlmChatMessage AssistantToolCalls(string? content, IReadOnlyList<LlmToolCall> calls) =>
        new() { Role = "assistant", Content = content, ToolCalls = calls };

    /// <summary>Resultado de um tool executado localmente.</summary>
    public static LlmChatMessage Tool(string callId, string content) =>
        new() { Role = "tool", ToolCallId = callId, Content = content };
}

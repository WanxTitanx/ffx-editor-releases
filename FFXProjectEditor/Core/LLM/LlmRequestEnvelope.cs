using System;
using System.Collections.Generic;

namespace FFXProjectEditor.Core.LLM;

/// <summary>O que o usuário aprovou enviar. Nunca: chaves, paths fora do workspace, saves pessoais.</summary>
public enum LlmDataKind
{
    None = 0,
    RecipeDescription,
    DiffContext,
    DumpBytes,
    ScriptBytes
}

/// <summary>
/// Como interpretar a resposta do endpoint (Jarvis-UI, 2026-09-15). <see cref="Proposal"/> mantém o
/// contrato P2-B estrito (o content/corpo DEVE ser um PatchProposal JSON). <see cref="Chat"/> devolve
/// o texto do modelo em <see cref="LlmResult.Message"/> — read-only, nunca aplicável.
/// </summary>
public enum LlmResponseMode
{
    Proposal = 0,
    Chat = 1,

    /// <summary>Multi-turno com tools — o adapter devolve ToolCalls p/ o loop agente executar.</summary>
    Agent = 2,
}

/// <summary>
/// Envelope de uma chamada ao modelo. PayloadPreview é SEMPRE redigido e truncado.
/// Endpoint não-loopback exige confirmação humana com Host + PayloadPreview.
/// </summary>
public sealed record LlmRequestEnvelope
{
    public required string Provider { get; init; }   // ex.: "openai-compatible" | "local"
    public required Uri Endpoint { get; init; }
    public required string ModelId { get; init; }
    public required string PayloadPreview { get; init; } // redigido (LlmRedactor) + truncado
    public required IReadOnlyList<LlmDataKind> DataKinds { get; init; }
    public required int MaxResponseBytes { get; init; }
    public required TimeSpan Timeout { get; init; }

    /// <summary>Modo de resposta esperado. Default Proposal preserva o contrato P2-B original.</summary>
    public LlmResponseMode Mode { get; init; } = LlmResponseMode.Proposal;

    /// <summary>
    /// Histórico multi-turno (modo Agent). Quando setado, o adapter serializa ESTA lista
    /// em vez da mensagem única derivada de PayloadPreview (single-shot Chat/Proposal).
    /// </summary>
    public IReadOnlyList<LlmChatMessage>? Messages { get; init; }

    /// <summary>Tool specs anunciadas no request (modo Agent). Null = sem tools.</summary>
    public IReadOnlyList<LlmToolSpec>? Tools { get; init; }

    /// <summary>Host não-local => confirmação humana obrigatória antes do request.</summary>
    public bool RequiresRemoteConfirmation => !Endpoint.IsLoopback;
}

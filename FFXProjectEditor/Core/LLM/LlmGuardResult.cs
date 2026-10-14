using System.Collections.Generic;

namespace FFXProjectEditor.Core.LLM;

public enum LlmRejectionReason
{
    None = 0,
    OptInDisabled,              // settings.IsEnabled == false
    UnknownRecipe,              // RecipeId fora do catálogo de receitas comprovadas
    MissingPrecondition,        // verificação obrigatória ausente / capability errada
    OperationNotAllowed,        // Insert/Delete/Grow sem flag; ou fora do perfil 1
    LimitsExceeded,             // MaxProposalBytes / MaxResponseBytes / tamanho de alvo
    BeforeHashMismatch,         // arquivo real != BeforeHash da proposta
    MalformedPayload,           // caminho inválido, base64 quebrado, campo extra no JSON
    RemoteEndpointNotConfirmed, // host não-loopback sem confirmação humana
    VerificationFailed,         // hash-after/round-trip/decompile falhou
    RoundTripFailed,
    RegressionFailed,
    Cancelled,
    Timeout,
    SecretDetected              // texto do modelo continha padrão de chave (ou chave da sessão)
}

public sealed record LlmGuardResult
{
    public required bool Allowed { get; init; }
    public LlmRejectionReason Reason { get; init; } = LlmRejectionReason.None;
    public required IReadOnlyList<string> FailedChecks { get; init; }
    public string? Message { get; init; } // SEMPRE redigido; nunca contém chave/payload bruto
}

/// <summary>Falha de chamada ao modelo: mensagem acionável, sem segredo, sem stack cru.</summary>
public sealed record LlmFailure
{
    public required LlmRejectionReason Reason { get; init; }
    public required string Message { get; init; }
    public string? EndpointHost { get; init; } // só o host, para diagnóstico
}

using System;

namespace FFXProjectEditor.Core.LLM;

/// <summary>
/// Entrada do histórico LOCAL de decisões do assistente LLM (contrato L3 —
/// docs/ai/P2B_CONTRATO_LLM_2026-07-31.md §L3).
///
/// REGRA DE OURO: este record NUNCA carrega payload. Sem NewBytesBase64, sem
/// hashes de conteúdo, sem Justification/Diff do modelo, sem caminhos absolutos
/// e sem chave/segredo (LlmCredential nunca sai da memória). A proposta original
/// é reconstruível apenas re-executando a receita — o histórico guarda só a
/// decisão de auditoria (quem pediu, o que foi decidido, quando).
/// </summary>
public sealed record LlmHistoryEntry
{
    public required string ProposalId { get; init; }
    public required string CapabilityId { get; init; }
    public required string RecipeId { get; init; }
    public required string Provider { get; init; }
    public required string ModelId { get; init; }
    public required string PromptTemplateVersion { get; init; }
    public required ReviewDecision Decision { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}

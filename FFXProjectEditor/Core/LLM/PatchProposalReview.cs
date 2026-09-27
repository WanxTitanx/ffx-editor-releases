using System;

namespace FFXProjectEditor.Core.LLM;

public enum ReviewDecision { Pending = 0, Approved = 1, Rejected = 2, Expired = 3 }

/// <summary>
/// Aprovação humana OBRIGATÓRIA antes de qualquer aplicação. Expira:
/// proposta aprovada não pode ser aplicada depois de ExpiresAt sem nova revisão.
/// </summary>
public sealed record PatchProposalReview
{
    public required string ProposalId { get; init; }
    public required ReviewDecision Decision { get; init; }
    public required DateTimeOffset ReviewedAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public string? Notes { get; init; }

    public bool IsValidNow(DateTimeOffset now) =>
        Decision == ReviewDecision.Approved && now < ExpiresAt;
}

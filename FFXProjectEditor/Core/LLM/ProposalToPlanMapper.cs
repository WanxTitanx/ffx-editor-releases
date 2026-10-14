using FFXProjectEditor.Resources;
using System;
using System.Collections.Generic;
using System.IO;

namespace FFXProjectEditor.Core.LLM;

/// <summary>
/// L3 do contrato LLM (docs/ai/P2B_CONTRATO_LLM_2026-07-31.md §L3): converte uma
/// <see cref="PatchProposal"/> em um <see cref="OperationPlan"/> do pipeline comprovado (RT0).
/// O mapper NÃO executa nada — só mapeia; o chamador roda o OperationExecutorV2.
/// Nenhuma etapa do subsistema LLM escreve arquivo (a escrita só existe no pipeline
/// OperationPlan já comprovado).
///
/// Fluxo: (a) LlmGuard.EvaluatePreconditions — rejeição → null + motivo em
/// <paramref name="guardResult"/>; (b) adapter do catálogo via proposal.CapabilityId —
/// ausente → MissingPrecondition; (c) FileOperation(Kind=Patch, BeforeHash,
/// PredictedAfterHash=AfterHash, Risk=adapter.Risk, Edits tipados); (d) OperationPlan
/// com OwnerCapabilityId = proposal.CapabilityId e Preconditions = capability.Preconditions.
///
/// CONTRATO DE EDITS — receita "ppp-sclmove-t3" (perfil 1, ByteReplace):
///   "offset"         (long)   byte offset absoluto no arquivo (== PatchTarget.Offset)
///   "newBytesBase64" (string) payload do formato em base64 (decodificado == PatchTarget.Length)
///   "beforeHash"     (string) SHA-256 hex lowercase do arquivo atual (== actualBeforeHash)
/// O adapter ATEL do perfil 1 é um BytePatchAdapter genérico que ainda não existe;
/// o mapper funciona com QUALQUER IWriterAdapter que aceite essas três chaves.
/// A validação prática acontece no executor: StageAsync com edits inválidos falha o
/// estágio e o pipeline aborta all-or-nothing sem tocar o output (OperationExecutorV2).
/// </summary>
public static class ProposalToPlanMapper
{
    /// <summary>Chave de edit: byte offset absoluto (long) — receita ppp-sclmove-t3.</summary>
    public const string EditKeyOffset = "offset";

    /// <summary>Chave de edit: payload do formato em base64 (string) — receita ppp-sclmove-t3.</summary>
    public const string EditKeyNewBytesBase64 = "newBytesBase64";

    /// <summary>Chave de edit: SHA-256 hex lowercase do arquivo atual (string) — receita ppp-sclmove-t3.</summary>
    public const string EditKeyBeforeHash = "beforeHash";

    /// <summary>
    /// Mapeia uma proposta aprovada para um plano executável. Retorna null quando o
    /// guard ou o catálogo rejeita — nesse caso <paramref name="guardResult"/> carrega
    /// o motivo exato (Allowed=false + Reason + Message redigida).
    /// </summary>
    public static OperationPlan? MapToPlan(
        PatchProposal proposal,
        LlmSessionSettings settings,
        CapabilityDescriptor capability,
        string sourceRoot,
        string outputRoot,
        WriterAdapterCatalog catalog,
        string actualBeforeHash,
        out LlmGuardResult guardResult)
    {
        if (proposal is null) throw new ArgumentNullException(nameof(proposal));
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        if (capability is null) throw new ArgumentNullException(nameof(capability));
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));

        // (a) Regras compiladas: opt-in, receita comprovada, capability, operação,
        //     path guard, before-hash contra o arquivo real, limites, verificação obrigatória.
        guardResult = LlmGuard.EvaluatePreconditions(proposal, settings, capability, sourceRoot, actualBeforeHash);
        if (!guardResult.Allowed)
            return null;

        // (b) Adapter do catálogo — sem adapter registrado não há como produzir os
        //     bytes modificados; rejeita antes de montar qualquer plano.
        var adapter = catalog.Get(proposal.CapabilityId);
        if (adapter is null)
        {
            guardResult = new LlmGuardResult
            {
                Allowed = false,
                Reason = LlmRejectionReason.MissingPrecondition,
                FailedChecks = new[] { "adapter-registered" },
                Message = string.Format(Strings.U_Llm_NoAdapter, proposal.CapabilityId)
            };
            return null;
        }

        // (c) FileOperation(Kind=Patch) — só descreve o patch; nada é executado aqui.
        var opId = Guid.NewGuid().ToString("N");
        var operation = new FileOperation
        {
            Id = opId,
            Kind = FileOperationKind.Patch,
            SourceRelativePath = proposal.Target.RelativePath,
            OutputRelativePath = proposal.Target.RelativePath,
            BeforeHash = actualBeforeHash,
            PredictedAfterHash = proposal.AfterHash,
            EstimatedBytes = proposal.Target.Length,
            Description = $"{adapter.DisplayName} ({proposal.CapabilityId}): receita {proposal.RecipeId}, proposta {proposal.ProposalId}",
            Diff = new FileDiffSummary
            {
                FieldsChanged = proposal.Diff.ChangedFields.Count,
                ChangedFieldNames = proposal.Diff.ChangedFields,
                HumanSummary = proposal.Diff.HumanSummary,
            },
            Risk = adapter.Risk,
            Edits = BuildEdits(proposal, actualBeforeHash),
        };

        // (d) OperationPlan com OwnerCapabilityId e Preconditions da capacidade ativa.
        return new OperationPlan
        {
            OperationId = opId,
            DisplayName = $"{adapter.DisplayName} [{proposal.RecipeId}] — {Path.GetFileName(proposal.Target.RelativePath)}",
            CreatedAt = DateTimeOffset.UtcNow,
            SourceRoot = sourceRoot,
            OutputRoot = outputRoot,
            StagingRoot = Path.Combine(sourceRoot, ".staging", opId),
            BackupRoot = Path.Combine(sourceRoot, ".backup", opId),
            Operations = new[] { operation },
            Preconditions = capability.Preconditions,
            OwnerCapabilityId = proposal.CapabilityId,
        };
    }

    /// <summary>
    /// Monta o dicionário de Edits do contrato ppp-sclmove-t3 (perfil 1):
    /// "offset" (long), "newBytesBase64" (string), "beforeHash" (string).
    /// Qualquer IWriterAdapter que aceite essas chaves (ex.: o futuro BytePatchAdapter
    /// genérico do perfil 1) consome este dicionário direto no StageAsync.
    /// </summary>
    public static IReadOnlyDictionary<string, object> BuildEdits(PatchProposal proposal, string actualBeforeHash)
    {
        if (proposal is null) throw new ArgumentNullException(nameof(proposal));

        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [EditKeyOffset] = proposal.Target.Offset,
            [EditKeyNewBytesBase64] = proposal.Target.NewBytesBase64,
            [EditKeyBeforeHash] = actualBeforeHash,
        };
    }
}

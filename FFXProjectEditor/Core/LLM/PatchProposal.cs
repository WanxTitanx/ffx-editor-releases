using System;
using System.Collections.Generic;

namespace FFXProjectEditor.Core.LLM;

/// <summary>
/// Perfil 1 (único habilitado por padrão): troca byte-local de receita comprovada.
/// Insert/Delete/Grow exigem flag de capacidade + evidência independente por formato.
/// </summary>
public enum PatchOperationKind
{
    ByteReplace = 0, // única operação do perfil 1
    Insert = 1,      // exige LlmCapabilityFlag.Insert + evidência independente
    Delete = 2,      // exige LlmCapabilityFlag.Delete + evidência independente
    Grow = 3         // exige LlmCapabilityFlag.Grow + evidência independente
}

/// <summary>Alvo byte-local dentro do workspace (nunca caminho absoluto livre).</summary>
public sealed record PatchTarget
{
    public required string RelativePath { get; init; } // validado por PathGuard (anti-traversal)
    public required string FileVersion { get; init; }  // ex.: "ps2-jppc-hd" | versão detectada
    public required long Offset { get; init; }         // byte offset absoluto no arquivo
    public required int Length { get; init; }          // deve casar com NewBytesBase64 decodificado
    public required string NewBytesBase64 { get; init; } // dados do formato; nunca código executável
}

/// <summary>Diff legível para revisão humana.</summary>
public sealed record PatchDiff
{
    public required string BeforeBytesHex { get; init; }
    public required string AfterBytesHex { get; init; }
    public required IReadOnlyList<string> ChangedFields { get; init; }
    public required string HumanSummary { get; init; }
}

/// <summary>Verificação exigida antes de a proposta ser aplicável.</summary>
public sealed record VerificationCheck
{
    public required string Kind { get; init; } // "hash-after" | "round-trip" | "decompile" | "regression" | "size-limit"
    public required bool Required { get; init; }
    public string? Expected { get; init; }     // ex.: SHA-256 after esperado
}

/// <summary>
/// Saída tipada do assistente LLM. NUNCA contém bytes executáveis livres:
/// o alvo é sempre {arquivo, offset, length, bytes} dentro de uma receita comprovada.
/// Prompt injection não pode adicionar campos (parse estrito) nem mudar regras.
/// </summary>
public sealed record PatchProposal
{
    public required string ProposalId { get; init; }
    public required string CapabilityId { get; init; }   // CapabilityDescriptor.Id
    public required string RecipeId { get; init; }       // receita comprovada (ex.: "ppp-sclmove-t3")
    public required PatchOperationKind Operation { get; init; }
    public required PatchTarget Target { get; init; }
    public required string BeforeHash { get; init; }     // SHA-256 esperado do arquivo atual
    public required string AfterHash { get; init; }      // SHA-256 predito pós-aplicação
    public required string SemanticChange { get; init; } // campo/registro/família alterado
    public required string Justification { get; init; }  // texto do modelo — REDIGIDO antes de armazenar
    public required PatchDiff Diff { get; init; }
    public required IReadOnlyList<VerificationCheck> Verifications { get; init; }
    public required string Provider { get; init; }
    public required string ModelId { get; init; }
    public required string PromptTemplateVersion { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Core
{
    // P2 — preview de operação consumível pela UI (docs/ai/P2_INTEGRACAO_PIPELINE_2026-07-31.md §3).
    //
    // O fluxo de produto P2 é coberto ponta a ponta por componentes existentes:
    //   SIMULAR  → OperationPreview.BuildFromPlan / BuildFromFiles (este arquivo): expõe as 3 camadas
    //              de diff (bytes / disassembly / semântica) POR ARQUIVO, sem abrir editor;
    //   REVISAR  → a UI consome FilePreviewSummaries (hashes + linhas de diff + resumo humano);
    //   APLICAR  → OperationExecutorV2.ExecuteAsync (stage+verify → backup+promote);
    //   RECEIPT  → ReceiptBuilder.BuildReceipt.
    //
    // O diff de 3 camadas é domínio-específico (ATEL AiFile): quem o produz é o provider injetado
    // (tipicamente AtelScriptAdapter.ComputePreviewDiff, que delega a AiDiffThreeLayer.Compute).
    // Para domínios sem codec de 3 camadas (ex.: command.bin), o provider retorna null ou lança —
    // o preview então carrega a nota "preview indisponível" em HumanSummary, com as 3 listas vazias.

    /// <summary>
    /// Resumo de preview de UM arquivo do plano: as 3 camadas de diff expostas por arquivo
    /// (bytes / disassembly / semântica) + hashes + resumo humano. Quando o diff de 3 camadas
    /// não está disponível (provider null/ausente, staged ainda não existe, domínio sem codec),
    /// as listas vêm vazias e <see cref="HumanSummary"/> carrega
    /// <see cref="OperationPreview.PreviewUnavailableNote"/>.
    /// </summary>
    public sealed record FilePreviewSummary
    {
        public required string FileId { get; init; }
        public required string SourceRelativePath { get; init; }
        public required string OutputRelativePath { get; init; }
        public required string BeforeHash { get; init; }
        public required string PredictedAfterHash { get; init; }
        public required IReadOnlyList<string> ByteDiffLines { get; init; }
        public required IReadOnlyList<string> DisassemblyDiffLines { get; init; }
        public required IReadOnlyList<string> SemanticDiffLines { get; init; }
        public required string HumanSummary { get; init; }
    }

    /// <summary>
    /// Preview consumível da operação inteira (padrão UiBridge: sealed record + required).
    /// Read-only por construção: este arquivo nunca cria nem altera arquivos — apenas LÊ os bytes
    /// de source/staged quando o provider (ou BuildFromFiles) precisa computar o diff.
    /// </summary>
    public sealed record OperationPreview
    {
        /// <summary>Nota padrão quando o diff de 3 camadas não pode ser produzido.</summary>
        public static string PreviewUnavailableNote => Strings.U_Lbl_PreviewUnavailable;

        public required string OperationId { get; init; }
        public required string DisplayName { get; init; }
        public required int FileCount { get; init; }
        public required long TotalBytes { get; init; }
        public required RiskLevel OverallRisk { get; init; }
        public required IReadOnlyList<FilePreviewSummary> FilePreviewSummaries { get; init; }

        /// <summary>
        /// SIMULAR: constrói o preview do plano inteiro, expondo as 3 camadas de diff por arquivo.
        /// Para cada FileOperation, o provider é chamado com (sourcePath, stagedPath):
        ///   • sourcePath = SourceRoot/SourceRelativePath;
        ///   • stagedPath = StagingRoot/op.Id — a MESMA convenção de staging que o
        ///     OperationExecutorV2 usa no Pass 1, então um preview gerado após o stage reflete
        ///     exatamente o que seria promovido.
        /// Null-safe por contrato: provider null, retorno null OU exceção do provider → camadas
        /// vazias + nota "preview indisponível" em HumanSummary. Uma falha num arquivo nunca
        /// derruba o preview dos demais (nem o preview inteiro).
        /// </summary>
        public static OperationPreview BuildFromPlan(
            OperationPlan plan,
            Func<string, string, ThreeLayerDiff?>? threeLayerDiffProvider = null)
        {
            ArgumentNullException.ThrowIfNull(plan);

            var summaries = new List<FilePreviewSummary>(plan.Operations.Count);
            foreach (FileOperation op in plan.Operations)
            {
                string sourcePath = Path.Combine(plan.SourceRoot, op.SourceRelativePath);
                string stagedPath = Path.Combine(plan.StagingRoot, op.Id);

                ThreeLayerDiff? diff = null;
                string? unavailableNote = null;
                try
                {
                    diff = threeLayerDiffProvider?.Invoke(sourcePath, stagedPath);
                }
                catch (Exception ex)
                {
                    unavailableNote = PreviewUnavailableNote + " (" + ex.GetType().Name + ": " + ex.Message + ")";
                }

                if (diff is null && unavailableNote is null)
                    unavailableNote = PreviewUnavailableNote;

                summaries.Add(new FilePreviewSummary
                {
                    FileId = op.Id,
                    SourceRelativePath = op.SourceRelativePath,
                    OutputRelativePath = op.OutputRelativePath,
                    BeforeHash = op.BeforeHash,
                    PredictedAfterHash = op.PredictedAfterHash,
                    ByteDiffLines = diff?.ByteDiff ?? Array.Empty<string>(),
                    DisassemblyDiffLines = diff?.DisassemblyDiff ?? Array.Empty<string>(),
                    SemanticDiffLines = diff?.SemanticDiff ?? Array.Empty<string>(),
                    HumanSummary = unavailableNote
                        ?? (string.IsNullOrEmpty(op.Diff.HumanSummary) ? Summarize(diff!) : op.Diff.HumanSummary)
                });
            }

            return new OperationPreview
            {
                OperationId = plan.OperationId,
                DisplayName = plan.DisplayName,
                FileCount = plan.FileCount,
                TotalBytes = plan.TotalBytes,
                OverallRisk = plan.Operations.Count > 0 ? plan.Operations.Max(o => o.Risk) : RiskLevel.Safe,
                FilePreviewSummaries = summaries
            };
        }

        /// <summary>
        /// Convenience para um par (source, staged): lê os DOIS arquivos e computa o diff de 3 camadas
        /// direto via AiDiffThreeLayer.Compute com hasExtraInfo: false — o formato AiFile ATEL não tem
        /// variante de entry-size (documentado em AiDiffThreeLayer; o flag existe por paridade de
        /// assinatura com os adapters de AbilityCommand e é ignorado pelo compute).
        /// Assume que ambos os arquivos existem (FileNotFoundException propaga — é um contrato do
        /// chamador). FileId = nome do source; os paths são expostos como recebidos.
        /// </summary>
        public static FilePreviewSummary BuildFromFiles(string sourcePath, string stagedPath)
        {
            ArgumentNullException.ThrowIfNull(sourcePath);
            ArgumentNullException.ThrowIfNull(stagedPath);

            byte[] beforeBytes = File.ReadAllBytes(sourcePath);
            byte[] afterBytes = File.ReadAllBytes(stagedPath);

            ThreeLayerDiff diff = AiDiffThreeLayer.Compute(beforeBytes, afterBytes, hasExtraInfo: false);

            return new FilePreviewSummary
            {
                FileId = Path.GetFileName(sourcePath),
                SourceRelativePath = sourcePath,
                OutputRelativePath = stagedPath,
                BeforeHash = ComputeSha256(beforeBytes),
                PredictedAfterHash = ComputeSha256(afterBytes),
                ByteDiffLines = diff.ByteDiff,
                DisassemblyDiffLines = diff.DisassemblyDiff,
                SemanticDiffLines = diff.SemanticDiff,
                HumanSummary = diff.IsEmpty ? Strings.U_Lbl_NoChangesBeforeAfter : Summarize(diff)
            };
        }

        static string Summarize(ThreeLayerDiff diff)
            => string.Format(Strings.U_Lbl_ThreeLayerDiff, diff.ByteDiff.Count)
               + string.Format(Strings.U_Lbl_DisassemblyLines, diff.DisassemblyDiff.Count)
               + string.Format(Strings.U_Lbl_SemanticLines, diff.SemanticDiff.Count);

        static string ComputeSha256(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
        }
    }
}


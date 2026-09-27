using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Ai
{
    /// <summary>Camada 1 de um diff de 3 camadas: mudança byte-a-byte.</summary>
    public sealed record AiByteChange(int Offset, byte OldValue, byte NewValue);

    /// <summary>
    /// Receipt formal de uma aplicação de receita ATEL (F1 do plano — gaps G-3/G-9 de
    /// P2_CENSO_ATEL_2026-07-31.md): hash-precondição SHA-256 do blob original, antes/depois em
    /// 3 camadas (bytes + instruções + resumo), validade do validator, splice round-trip e
    /// restauração byte-idêntica. Serialização JSON fica no chamador (FfxLib é dependency-free).
    /// </summary>
    public sealed record AiReceipt(
        string Recipe,
        string MonsterId,
        string Timestamp,
        string BeforeSha256,
        string AfterSha256,
        bool PreconditionOk,
        IReadOnlyList<AiByteChange> ByteChanges,
        IReadOnlyList<string> InstructionChanges,
        string Summary,
        bool ValidatorOk,
        string ValidatorNote,
        bool SpliceRoundTripOk,
        bool RestoreByteIdentity,
        bool Verdict)
    {
        public bool IsPureByteLocal =>
            ByteChanges.Count > 0
            && ByteChanges.All(c => c.Offset >= 0)
            && SpliceRoundTripOk
            && RestoreByteIdentity;

        public string ChangedBytesSummary =>
            ByteChanges.Count == 0
                ? Strings.U_Ai_PatchNoByteChanges
                : string.Join("; ", ByteChanges.Take(8).Select(c => $"+0x{c.Offset:X4} {c.OldValue:X2}->{c.NewValue:X2}")
                    + (ByteChanges.Count > 8 ? string.Format(Strings.U_Ai_PatchMore, ByteChanges.Count - 8) : ""));

        public string RecoveryNote =>
            RestoreByteIdentity
                ? string.Format(Strings.U_Ai_PatchRecoveryOk, MonsterId, AfterSha256, BeforeSha256)
                : Strings.U_Ai_PatchRecoveryNotProven;
    }

    /// <summary>
    /// Blindagem do fluxo de edição ATEL (F1 do plano):
    /// hash-precondição SHA-256 (G-1/G-9), diff em 3 camadas (G-3) e validação de pureza byte-local.
    /// Dependency-free (sem System.Text.Json) — serialização fica no host (AiScriptLab/UI).
    /// </summary>
    public static class AiPatchGuard
    {
        /// <summary>SHA-256 hex lowercase do blob (pré-condição de aplicação).</summary>
        public static string ComputeSha256(byte[] blob)
        {
            if (blob == null || blob.Length == 0) return string.Empty;
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(blob)).ToLowerInvariant();
        }

        /// <summary>Camada 1: mudanças byte-a-byte entre dois blobs (comparação posicional).</summary>
        public static IReadOnlyList<AiByteChange> DiffBytes(byte[] before, byte[] after)
        {
            if (before == null || after == null) return Array.Empty<AiByteChange>();
            int len = Math.Min(before.Length, after.Length);
            var changes = new List<AiByteChange>();
            for (int i = 0; i < len; i++)
                if (before[i] != after[i])
                    changes.Add(new AiByteChange(i, before[i], after[i]));
            if (before.Length != after.Length)
                changes.Add(new AiByteChange(Math.Min(before.Length, after.Length), 0, 0)); // marcador de tamanho
            return changes;
        }

        /// <summary>Camada 2: diff em nível de instrução via AiScript_Diff (somente linhas alteradas).</summary>
        public static IReadOnlyList<string> DiffInstructions(AiScriptFile before, AiScriptFile after)
        {
            if (before == null || after == null) return Array.Empty<string>();
            return AiScript_Diff.Compare(before, after)
                .Where(d => d.Type != AiDiffType.Unchanged)
                .Select(d => d.ToString())
                .ToList();
        }

        /// <summary>Camada 3: resumo legível (via OperandGloss quando possível).</summary>
        public static string SummarizeChanges(AiScriptFile before, AiScriptFile after)
        {
            IReadOnlyList<string> lines = DiffInstructions(before, after);
            if (lines.Count == 0) return Strings.U_Ai_PatchNoInstructionChanges;
            return string.Join(" | ", lines.Take(6)) + (lines.Count > 6 ? $" | +{lines.Count - 6} mais" : "");
        }

        /// <summary>
        /// Verifica a pré-condição de hash: o blob atual precisa bater com o hash esperado
        /// (capturado no momento da leitura/edição). Retorna o hash real em <paramref name="actual"/>.
        /// </summary>
        public static bool TryVerifyPrecondition(byte[] currentBlob, string expectedSha256, out string actual)
        {
            actual = ComputeSha256(currentBlob);
            return !string.IsNullOrEmpty(expectedSha256) && string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Monta o receipt completo de uma aplicação de receita (F1.3).</summary>
        public static AiReceipt BuildReceipt(
            string recipe,
            string monsterId,
            byte[] originalAiBytes,
            byte[] editedAiBytes,
            AiScriptFile originalScript,
            AiScriptFile editedScript,
            bool validatorOk,
            string validatorNote,
            bool spliceRoundTripOk,
            bool restoreByteIdentity)
        {
            string beforeHash = ComputeSha256(originalAiBytes);
            string afterHash = ComputeSha256(editedAiBytes);
            bool verdict = validatorOk && spliceRoundTripOk && restoreByteIdentity;

            return new AiReceipt(
                recipe,
                monsterId,
                DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                beforeHash,
                afterHash,
                true,
                DiffBytes(originalAiBytes, editedAiBytes),
                DiffInstructions(originalScript, editedScript),
                SummarizeChanges(originalScript, editedScript),
                validatorOk,
                validatorNote,
                spliceRoundTripOk,
                restoreByteIdentity,
                verdict);
        }

        /// <summary>Helper de leitura de hash para logs (evita expor blob inteiro).</summary>
        public static string ShortHash(string sha256) =>
            string.IsNullOrEmpty(sha256) ? "(vazio)" : sha256.Substring(0, 12) + "…";
    }
}

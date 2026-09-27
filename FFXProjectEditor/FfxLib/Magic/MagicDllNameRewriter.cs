using FFXProjectEditor.Diagnostics;
using System;
using System.IO;
using System.Text;

namespace FFXProjectEditor.FfxLib.Magic
{
    /// <summary>
    /// Reescritor das strings UTF-16LE de "magic_&lt;id&gt;" / "magic_&lt;id&gt;.dll" embutidas no .data
    /// de uma magic DLL. Provado por RE (2026-08-04, Jarvis-MAGIC): as DLLs vanilla magic_0140 e
    /// magic_0141 diferem em exatamente 3 bytes no .data — todos eles são o dígito final do id
    /// dentro de 3 strings UTF-16LE (offsets 0x07E3C6, 0x07E4B2, 0x07E4F2 em magic_0140.dll). O id
    /// NÃO existe como u32 LE em nenhum lugar do .data — só como string UTF-16.
    ///
    /// Uso: ao clonar magic_&lt;oldId&gt;.dll → magic_&lt;newId&gt;.dll, chamar RewriteMagicNameStrings
    /// com os working bytes + oldId + newId para alinhar as strings internas com o nome do arquivo
    /// do clone. Sem isso, o clone roda (o jogo carrega pelo nome do arquivo), mas as strings internas
    /// de debug/símbolos mostram o id antigo — sujidade.
    ///
    /// Seguro (mesma-length): exige que oldId e newId tenham o mesmo nº de dígitos decimais (ex.:
    /// 140 → 0140, ou 140 → 9999). Caso contrário, lança — pq mudar o tamanho da string deslocaria
    /// offsets subsequentes no .data. Use zero-pad (magic_0140) para garantir.
    /// </summary>
    internal static class MagicDllNameRewriter
    {
        /// <summary>
        /// Reescreve todas as ocorrências das strings UTF-16LE "magic_&lt;oldId&gt;" e "magic_&lt;oldId&gt;.dll"
        /// no .data para o newId. Retorna os bytes modificados (clone dos working bytes).
        /// Lança se oldId/newId têm nº de dígitos diferente (length-mismatch safety).
        /// </summary>
        public static byte[] RewriteMagicNameStrings(byte[] workingBytes, int oldId, int newId)
        {
            if (workingBytes == null || workingBytes.Length == 0)
                throw new ArgumentException("Working bytes vazios.", nameof(workingBytes));

            string oldStr = $"magic_{oldId:D4}";
            string newStr = $"magic_{newId:D4}";
            // Verificação de length (defensiva — D4 garante 4 dígitos, mas valida anyway).
            if (oldStr.Length != newStr.Length)
                throw new InvalidOperationException(
                    $"Rewrite length mismatch: '{oldStr}' ({oldStr.Length}) vs '{newStr}' ({newStr.Length}). " +
                    "Use 4-digit zero-padded ids (magic_0140) to ensure the same size.");

            byte[] oldU16 = Encoding.Unicode.GetBytes(oldStr);
            byte[] newU16 = Encoding.Unicode.GetBytes(newStr);
            // Versão com ".dll" (string mais longa — também reescreve)
            string oldStrDll = oldStr + ".dll";
            string newStrDll = newStr + ".dll";
            byte[] oldU16Dll = Encoding.Unicode.GetBytes(oldStrDll);
            byte[] newU16Dll = Encoding.Unicode.GetBytes(newStrDll);

            byte[] result = (byte[])workingBytes.Clone();
            int replaced = 0;

            // Substitui "magic_<id>.dll" PRIMEIRO (mais específico, mais longo) — evita match parcial.
            replaced += ReplaceAll(result, oldU16Dll, newU16Dll);
            // Depois "magic_<id>" (curto).
            replaced += ReplaceAll(result, oldU16, newU16);

            if (replaced == 0)
            {
                throw new InvalidDataException(
                    $"The source DLL does not contain the expected internal identity '{oldStr}'. " +
                    "The clone was refused because its internal magic name could not be rewritten safely.");
            }

            DebugLog.Info("Magic.Rewriter",
                $"RewriteMagicNameStrings: oldId={oldId} → newId={newId}; {replaced} ocorrência(s) reescrita(s) " +
                $"(magic_{oldId:D4}+magic_{oldId:D4}.dll → magic_{newId:D4}+magic_{newId:D4}.dll).");

            return result;
        }

        /// <summary>Conta as ocorrências da string UTF-16LE (debug/preview).</summary>
        public static int CountMagicNameStrings(byte[] bytes, int id)
        {
            string str = $"magic_{id:D4}";
            byte[] needle = Encoding.Unicode.GetBytes(str);
            return CountOccurrences(bytes, needle);
        }

        /// <summary>
        /// Substitui todas as ocorrências não-sobrepostas de needle por replacement, in-place,
        /// ASSUMINDO que needle.Length == replacement.Length (mesmo tamanho — não desloca offsets).
        /// </summary>
        private static int ReplaceAll(byte[] buffer, byte[] needle, byte[] replacement)
        {
            if (needle.Length != replacement.Length)
                throw new InvalidOperationException(
                    $"ReplaceAll exige needle/replacement mesmo tamanho: {needle.Length} vs {replacement.Length}.");

            int count = 0;
            int idx = 0;
            while ((idx = IndexOf(buffer, needle, idx)) >= 0)
            {
                Buffer.BlockCopy(replacement, 0, buffer, idx, replacement.Length);
                count++;
                idx += replacement.Length;
            }
            return count;
        }

        private static int CountOccurrences(byte[] haystack, byte[] needle)
        {
            int count = 0, idx = 0;
            while ((idx = IndexOf(haystack, needle, idx)) >= 0)
            {
                count++;
                idx += needle.Length;
            }
            return count;
        }

        /// <summary>IndexOf manual (Span.IndexOf não disponível em net8.0 sem using extra).</summary>
        private static int IndexOf(byte[] haystack, byte[] needle, int start)
        {
            int max = haystack.Length - needle.Length;
            for (int i = start; i <= max; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }
    }
}

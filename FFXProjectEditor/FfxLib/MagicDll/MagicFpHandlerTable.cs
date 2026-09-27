using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FFXProjectEditor.FfxLib.MagicDll
{
    /// <summary>
    /// Tabela de handlers LOCAIS de um efeito, extraída do fp.h do Yonishi
    /// (yonishi_data/dat_ov/mag_XXXX/par/fp.h). O <c>handler_table_index</c> do slot
    /// é índice NESTA tabela (pppProgTbl_*), nunca um índice global do EXE
    /// (PARSER_SPEC.md §5.3 — regra de ouro do family_schema.py).
    ///
    /// Método de extração (espelho do summarize_c3.py): ler o fp.h (shift_jis),
    /// localizar a linha <c>pppProgTbl...={</c> e numerar os <c>PPMPN("...")</c> em
    /// ordem até o <c>};</c> — o índice da linha é o handler_table_index.
    /// </summary>
    public sealed class MagicFpHandlerTable
    {
        /// <summary>Id do efeito (21 para mag_0021).</summary>
        public int EffectMagicId { get; }

        /// <summary>Caminho do fp.h lido.</summary>
        public string SourcePath { get; }

        /// <summary>Índice local → opcode (ordem das PPMPN na pppProgTbl).</summary>
        public IReadOnlyDictionary<int, string> Names { get; }

        /// <summary>Quantidade de handlers na tabela.</summary>
        public int Count => Names.Count;

        public MagicFpHandlerTable(int effectMagicId, string sourcePath, IReadOnlyDictionary<int, string> names)
        {
            EffectMagicId = effectMagicId;
            SourcePath = sourcePath;
            Names = names;
        }

        /// <summary>Resolve o opcode de um handler_table_index local. Null quando fora da tabela.</summary>
        public string? Resolve(int handlerTableIndex)
        {
            return Names.TryGetValue(handlerTableIndex, out string? name) ? name : null;
        }

        /// <summary>
        /// Tenta carregar o fp.h de um efeito a partir do diretório canônico do Yonishi:
        /// &lt;dir&gt;\mag_XXXX\par\fp.h (XXXX com zero-pad de 4). Fallbacks: fp.h direto
        /// no diretório e &lt;dir&gt;\mag_XXXX\fp.h. Retorna null quando nada é encontrado.
        /// </summary>
        public static MagicFpHandlerTable? TryLoadFromMagicDirectory(string directory, int magicId)
        {
            string magDir = $"mag_{magicId:D4}";
            string[] candidates =
            {
                Path.Combine(directory, magDir, "par", "fp.h"),
                Path.Combine(directory, magDir, "fp.h"),
                Path.Combine(directory, "fp.h"),
            };
            foreach (string candidate in candidates)
            {
                if (!File.Exists(candidate))
                    continue;
                MagicFpHandlerTable? table = TryLoadFromFile(candidate, magicId);
                if (table != null)
                    return table;
            }
            return null;
        }

        /// <summary>Carrega um fp.h específico. Retorna null quando o arquivo não tem pppProgTbl.</summary>
        public static MagicFpHandlerTable? TryLoadFromFile(string fpPath, int magicId)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(fpPath);
                return TryParse(bytes, fpPath, magicId);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static MagicFpHandlerTable? TryParse(byte[] bytes, string path, int magicId)
        {
            string text = DecodeText(bytes);
            var names = new Dictionary<int, string>();
            int index = 0;
            bool inTable = false;

            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.Trim();
                if (!inTable)
                {
                    // "pppProgTbl_FP[]={" — a tabela começa na linha que contém ambos.
                    if (line.Contains("pppProgTbl", StringComparison.Ordinal) &&
                        line.Contains("={", StringComparison.Ordinal))
                        inTable = true;
                    continue;
                }

                if (line.StartsWith("};", StringComparison.Ordinal))
                    break;

                const string marker = "PPMPN(\"";
                int markerIndex = line.IndexOf(marker, StringComparison.Ordinal);
                if (markerIndex >= 0)
                {
                    int start = markerIndex + marker.Length;
                    int end = line.IndexOf('"', start);
                    if (end > start)
                    {
                        names[index] = line[start..end];
                        index++;
                    }
                }
            }

            return names.Count > 0 ? new MagicFpHandlerTable(magicId, path, names) : null;
        }

        /// <summary>
        /// Extrai a pppProgTbl_FP (tabela de nomes de handlers) do .rdata da PRÓPRIA DLL.
        /// PROVA (2026-08-02): a ordem física das strings "ppp*" no .rdata é o
        /// handler_table_index — 0021 .rdata == fp.h do Yonishi (37/37, ordem idêntica);
        /// 0098 .rdata tem 39 vs 35 do Yonishi (a DLL do PC é MAIS completa: KeZCrct,
        /// KeZCrctShp, DrawMdl3, DrawShapeX). Universal: toda DLL do jogo tem a tabela
        /// (amostra: 0021/0098/0117/0086/0087/0412/0620/0676/0715/0383/0300/0357).
        /// Fonte de verdade do runtime: sub_10001680 registra &amp;off_100050F8 (a tabela
        /// de nomes) via host_context+2844 — a primeira string é "pppAccele".
        /// </summary>
        /// <returns>null quando o .rdata não existe ou não tem nomes suficientes.</returns>
        public static MagicFpHandlerTable? TryExtractFromDll(MagicDllFile dll)
        {
            MagicPeSection? rdata = dll.Sections.FirstOrDefault(s => s.Name == ".rdata");
            if (rdata == null || rdata.RawPtr < 0 || rdata.RawSize <= 0)
                return null;

            int start = rdata.RawPtr;
            int end = Math.Min(start + rdata.RawSize, dll.FileBytes.Length);
            if (start >= end)
                return null;

            byte[] blob = dll.FileBytes;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var names = new List<string>();
            int i = start;
            while (i < end - 3)
            {
                if (blob[i] == (byte)'p' && blob[i + 1] == (byte)'p' && blob[i + 2] == (byte)'p')
                {
                    int j = i + 3;
                    int nameStart = j;
                    while (j < end && (IsAsciiLetterOrDigit(blob[j]) || blob[j] == (byte)'_'))
                        j++;
                    int len = j - nameStart;
                    if (len >= 2)
                    {
                        string s = "ppp" + Encoding.ASCII.GetString(blob, nameStart, len);
                        if (seen.Add(s))
                            names.Add(s);
                    }
                    i = j;
                }
                else
                {
                    i++;
                }
            }

            if (names.Count < 2)
                return null;

            var table = new Dictionary<int, string>();
            for (int idx = 0; idx < names.Count; idx++)
                table[idx] = names[idx];

            return new MagicFpHandlerTable(
                dll.MagicId,
                dll.SourcePath + " [.rdata pppProgTbl_FP]",
                table);
        }

        private static bool IsAsciiLetterOrDigit(byte b)
        {
            return (b >= (byte)'a' && b <= (byte)'z')
                || (b >= (byte)'A' && b <= (byte)'Z')
                || (b >= (byte)'0' && b <= (byte)'9');
        }

        /// <summary>
        /// Decodifica o fp.h: tenta UTF-8 estrito (os arquivos reais são ASCII/UTF-8);
        /// em falha, cai para shift_jis (encoding original do part.exe). Nunca lança.
        /// </summary>
        private static string DecodeText(byte[] bytes)
        {
            try
            {
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                    .GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return GetShiftJis().GetString(bytes);
            }
        }

        private static Encoding GetShiftJis()
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding("shift_jis");
            }
            catch (Exception)
            {
                // Sem suporte a code pages no runtime — UTF-8 é um fallback aceitável
                // para nomes ASCII (todos os opcodes PPP são ASCII).
                return Encoding.UTF8;
            }
        }
    }
}

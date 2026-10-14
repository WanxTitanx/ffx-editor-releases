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

        /// <summary>Number of native tables found. Multiple tables expose only agreeing indices.</summary>
        public int NativeTableCount { get; }

        internal IReadOnlyDictionary<uint, IReadOnlyDictionary<int, string>> NativeTables { get; private set; }
            = new Dictionary<uint, IReadOnlyDictionary<int, string>>();

        public MagicFpHandlerTable(int effectMagicId, string sourcePath, IReadOnlyDictionary<int, string> names,
            int nativeTableCount = 0)
        {
            EffectMagicId = effectMagicId;
            SourcePath = sourcePath;
            Names = names;
            NativeTableCount = nativeTableCount;
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
        /// Reads native 40-byte PPP entries in runtime index order. Shared string storage does
        /// not define that order. If a DLL registers multiple tables, only indices with the
        /// same name in every table are safe until their resource bindings are known.
        /// Legacy string-only images retain the historical fallback when no native table exists.
        /// </summary>
        public static MagicFpHandlerTable? TryExtractFromDll(MagicDllFile dll)
        {
            MagicFpHandlerTable? native = TryExtractNativeEntries(dll);
            if (native != null) return native;
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

        private static MagicFpHandlerTable? TryExtractNativeEntries(MagicDllFile dll)
        {
            const int EntrySize = 40;
            byte[] bytes = dll.FileBytes;
            if (bytes.Length < 64) return null;
            int pe = BitConverter.ToInt32(bytes, 0x3C);
            if (pe < 0 || pe > bytes.Length - 56) return null;
            uint imageBase = BitConverter.ToUInt32(bytes, pe + 24 + 28);

            int? Offset(uint address, string? sectionName, int width = 1)
            {
                if (address < imageBase) return null;
                long rva = (long)address - imageBase;
                foreach (var section in dll.Sections)
                {
                    if ((sectionName != null && section.Name != sectionName) ||
                        section.RawPtr < 0 || section.RawSize < width)
                        continue;
                    long relative = rva - section.VirtualAddress;
                    long raw = section.RawPtr + relative;
                    if (relative >= 0 && relative <= (long)section.RawSize - width &&
                        raw >= 0 && raw <= (long)bytes.Length - width) return (int)raw;
                }
                return null;
            }

            string? Name(int at)
            {
                if (at < 0 || at > bytes.Length - 4) return null;
                uint address = BitConverter.ToUInt32(bytes, at);
                int? pointer = Offset(address, ".rdata", 6);
                if (pointer is not int start || bytes[start] != 'p' ||
                    bytes[start + 1] != 'p' || bytes[start + 2] != 'p') return null;
                int length = 3;
                while (length < 80 && Offset(address, ".rdata", length + 1) != null &&
                    (IsAsciiLetterOrDigit(bytes[start + length]) || bytes[start + length] == '_'))
                    length++;
                if (length < 5 || Offset(address, ".rdata", length + 1) == null ||
                    bytes[start + length] != 0) return null;
                return Encoding.ASCII.GetString(bytes, start, length);
            }

            bool Functions(int at)
            {
                if (at < 0 || at > bytes.Length - EntrySize) return false;
                for (int offset = 4; offset < EntrySize; offset += 4)
                {
                    uint value = BitConverter.ToUInt32(bytes, at + offset);
                    if (value != 0 && Offset(value, ".text") == null) return false;
                }
                // Host-provided handlers may legitimately have nine null DLL callbacks.
                return true;
            }

            // Tables can be contiguous with no null separator. Native registration reads their
            // addresses from relocated pointer arrays (0021: VA 0x10006FDC), not necessarily
            // immediate operands in .text. Preserve these independently referenced starts.
            var referencedStarts = new HashSet<int>();
            int optional = pe + 24;
            if (optional <= bytes.Length - 144 && BitConverter.ToUInt32(bytes, optional + 92) > 5)
            {
                uint relocationRva = BitConverter.ToUInt32(bytes, optional + 136);
                uint relocationSize = BitConverter.ToUInt32(bytes, optional + 140);
                ulong relocationVa = (ulong)imageBase + relocationRva;
                if (relocationRva != 0 && relocationSize is >= 8 and <= int.MaxValue &&
                    relocationVa <= uint.MaxValue &&
                    Offset((uint)relocationVa, null, (int)relocationSize) is int relocations)
                {
                    int end = relocations + (int)relocationSize;
                    for (int block = relocations; block <= end - 8;)
                    {
                        uint page = BitConverter.ToUInt32(bytes, block);
                        uint size = BitConverter.ToUInt32(bytes, block + 4);
                        if (size < 8 || size % 4 != 0 || size > end - block) break;
                        int blockEnd = block + (int)size;
                        for (int item = block + 8; item < blockEnd; item += 2)
                        {
                            ushort relocation = BitConverter.ToUInt16(bytes, item);
                            if (relocation >> 12 != 3) continue; // IMAGE_REL_BASED_HIGHLOW
                            ulong operandVa = (ulong)imageBase + page + (uint)(relocation & 0xFFF);
                            if (operandVa > uint.MaxValue ||
                                Offset((uint)operandVa, null, 4) is not int operand) continue;
                            uint target = BitConverter.ToUInt32(bytes, operand);
                            if (Offset(target, null, EntrySize) is int table &&
                                Name(table) != null && Functions(table)) referencedStarts.Add(table);
                        }
                        block = blockEnd;
                    }
                }
            }

            var candidates = new List<(int Start, Dictionary<int, string> Names)>();
            foreach (var section in dll.Sections.Where(s => s.Name is ".data" or ".rdata"))
            {
                if (section.RawPtr < 0 || section.RawSize < 2 * EntrySize) continue;
                int end = (int)Math.Min(bytes.Length, (long)section.RawPtr + section.RawSize);
                for (int at = section.RawPtr; at <= end - EntrySize; at += 4)
                {
                    bool referenced = referencedStarts.Contains(at);
                    if (Name(at) == null || !Functions(at)) continue;
                    if (!referenced && (at > end - 2 * EntrySize ||
                        Name(at + EntrySize) == null || !Functions(at + EntrySize))) continue;
                    if (at - EntrySize >= section.RawPtr &&
                        Name(at - EntrySize) != null && Functions(at - EntrySize) && !referenced) continue;
                    var names = new Dictionary<int, string>();
                    int entry = at;
                    while (entry <= end - EntrySize && names.Count < 256 &&
                        (entry == at || !referencedStarts.Contains(entry)) &&
                        Name(entry) is string name && Functions(entry))
                    {
                        names.Add(names.Count, name);
                        entry += EntrySize;
                    }
                    if ((names.Count >= 2 || referenced && names.Count == 1) && names.Count < 256)
                        candidates.Add((at, names));
                }
            }
            if (candidates.Count == 0) return null;
            var nativeTables = new Dictionary<uint, IReadOnlyDictionary<int, string>>();
            foreach (var entry in candidates)
            {
                var section = dll.Sections.First(s => entry.Start >= s.RawPtr &&
                    (long)entry.Start + EntrySize <= (long)s.RawPtr + s.RawSize);
                long address = (long)imageBase + section.VirtualAddress + entry.Start - section.RawPtr;
                if (address is < 0 or > uint.MaxValue || !nativeTables.TryAdd((uint)address, entry.Names))
                    return null;
            }
            var candidate = candidates[0];
            if (candidates.Count == 1)
                return new MagicFpHandlerTable(dll.MagicId,
                    $"{dll.SourcePath} [native PPP entries @file+0x{candidate.Start:X}]",
                    candidate.Names, 1) { NativeTables = nativeTables };

            // Multiple PPP instances can have different local indices (e.g. magic_0244.dll).
            // Never replace their conflicts with the order of pooled strings or an old fp.h.
            var consensus = candidate.Names.Where(pair => candidates.All(table =>
                table.Names.TryGetValue(pair.Key, out var name) && name == pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            return new MagicFpHandlerTable(dll.MagicId,
                $"{dll.SourcePath} [native PPP entries: {candidates.Count} tables; consensus only]",
                consensus, candidates.Count) { NativeTables = nativeTables };
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

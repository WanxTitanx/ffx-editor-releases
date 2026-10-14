using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FFXProjectEditor.FfxLib.MagicDll
{
    // ── Shared PE decoder with explicit byte ownership ──
    // Path loading and internal captured-byte loading use the same decoder and offsets.
    // MAINT: snapshots own their array; a logical source label is never reopened by the decoder.
    /// <summary>
    /// Arquivo de magic DLL (PE32) com a seção .data exposta para o parser de roots PPP.
    /// É o ponto de entrada do pipeline: <see cref="Load(string)"/> faz o parse do PE
    /// (MS-DOS header → PE header → section table), localiza a seção .data e expõe o
    /// payload bruto + conversão RVA → offset.
    ///
    /// Convenções do domínio (PARSER_SPEC.md §1): todos os inteiros são little-endian;
    /// "abs" = offset dentro do payload da seção .data (RAW do PE) — não é RVA, não é VA.
    /// </summary>
    public sealed class MagicDllFile
    {
        private const ushort DosMagic = 0x5A4D;       // "MZ"
        private const uint PeMagic = 0x00004550;      // "PE\0\0"

        /// <summary>Caminho original do arquivo lido.</summary>
        public string SourcePath { get; }

        /// <summary>Nome do arquivo com extensão. Ex.: "magic_0021.dll".</summary>
        public string DllName { get; }

        /// <summary>Id do efeito derivado do nome (0021 → 21). -1 quando o nome não casa o padrão.</summary>
        public int MagicId { get; }

        /// <summary>Cópia integral dos bytes do arquivo (leitura). Nunca escrita pelo parser.</summary>
        public byte[] FileBytes { get; }

        /// <summary>Payload da seção .data (raw). É sobre estes bytes que os roots PPP são localizados.</summary>
        public byte[] Data { get; }

        /// <summary>PointerToRawData da seção .data.</summary>
        public int DataSectionRawPtr { get; }

        /// <summary>SizeOfRawData (clampado ao tamanho do arquivo) da seção .data.</summary>
        public int DataSectionSize { get; }

        /// <summary>RVA base da seção .data (para conversões RVA ↔ offset).</summary>
        public int DataSectionRva { get; }

        /// <summary>Todas as seções do PE (para RvaToOffset e diagnóstico).</summary>
        public IReadOnlyList<MagicPeSection> Sections { get; }

        /// <summary>Roots PPP encontrados na seção .data (tipicamente 1 por DLL).</summary>
        public IReadOnlyList<MagicDllRoot> Roots { get; }

        public MagicDllFile(
            string sourcePath,
            byte[] fileBytes,
            byte[] data,
            int dataSectionRawPtr,
            int dataSectionSize,
            int dataSectionRva,
            IReadOnlyList<MagicPeSection> sections,
            IReadOnlyList<MagicDllRoot> roots)
        {
            SourcePath = sourcePath;
            DllName = Path.GetFileName(sourcePath);
            MagicId = ParseMagicIdFromName(sourcePath);
            FileBytes = fileBytes;
            Data = data;
            DataSectionRawPtr = dataSectionRawPtr;
            DataSectionSize = dataSectionSize;
            DataSectionRva = dataSectionRva;
            Sections = sections;
            Roots = roots;
        }

        /// <summary>
        /// Serializa o arquivo de volta (sem edição). O parser é read-only por design:
        /// retorna os bytes originais preservados (round-trip byte-idêntico).
        /// Writers futuros devem ser classes separadas com âncora hash-gated (T3).
        /// </summary>
        public byte[] Serialize() => (byte[])FileBytes.Clone();

        /// <summary>
        /// Converte um RVA da imagem em offset dentro do arquivo (PointerToRawData).
        /// Retorna -1 quando o RVA não pertence a nenhuma seção ou cai fora do raw.
        /// </summary>
        public int RvaToOffset(int rva)
        {
            foreach (MagicPeSection section in Sections)
            {
                int span = Math.Max(section.VirtualSize, section.RawSize);
                if (rva < section.VirtualAddress || rva >= section.VirtualAddress + span)
                    continue;
                int offset = section.RawPtr + (rva - section.VirtualAddress);
                return offset >= 0 && offset < FileBytes.Length ? offset : -1;
            }
            return -1;
        }

        /// <summary>
        /// Carrega um magic DLL: lê os bytes, faz o parse do PE32 e localiza a seção .data.
        /// Lança <see cref="InvalidDataException"/> (ou IOException) em arquivo inválido.
        /// </summary>
        public static MagicDllFile Load(string path) => Load(path, []);
        /// <summary>Overload de <see cref="Load(string)"/> que já anexa os roots parseados.</summary>
        public static MagicDllFile Load(string path, IReadOnlyList<MagicDllRoot> roots) =>
            DecodeOwned(path, File.ReadAllBytes(path), roots);

        internal static MagicDllFile LoadSnapshot(string logicalPath, ReadOnlySpan<byte> bytes) =>
            DecodeOwned(logicalPath, bytes.ToArray(), Array.Empty<MagicDllRoot>());

        private static MagicDllFile DecodeOwned(string path, byte[] bytes, IReadOnlyList<MagicDllRoot> roots)
        {
            // --- MS-DOS header ---
            if (bytes.Length < 64)
                throw new InvalidDataException("File too small for the MS-DOS header.");
            if (BitConverter.ToUInt16(bytes, 0) != DosMagic)
                throw new InvalidDataException("MZ signature missing — not a valid PE.");

            // --- PE header ---
            int peOffset = BitConverter.ToInt32(bytes, 0x3C);
            if (peOffset <= 0 || peOffset + 24 > bytes.Length)
                throw new InvalidDataException("Invalid PE header offset (e_lfanew out of file).");
            if (BitConverter.ToUInt32(bytes, peOffset) != PeMagic)
                throw new InvalidDataException("PE signature missing.");

            int coffOffset = peOffset + 4;
            int numSections = BitConverter.ToUInt16(bytes, coffOffset + 2);
            if (numSections <= 0 || numSections > 96)
                throw new InvalidDataException($"Invalid section count: {numSections}.");
            int optionalHeaderSize = BitConverter.ToUInt16(bytes, coffOffset + 16);
            int sectionTableOffset = coffOffset + 20 + optionalHeaderSize;
            if (sectionTableOffset + numSections * 40 > bytes.Length)
                throw new InvalidDataException("Section table out of file bounds.");

            // --- Section table ---
            var sections = new List<MagicPeSection>(numSections);
            for (int i = 0; i < numSections; i++)
            {
                int so = sectionTableOffset + i * 40;
                string name = Encoding.ASCII.GetString(bytes, so, 8).TrimEnd('\0');
                int virtualSize = BitConverter.ToInt32(bytes, so + 8);
                int virtualAddress = BitConverter.ToInt32(bytes, so + 12);
                int rawSize = BitConverter.ToInt32(bytes, so + 16);
                int rawPtr = BitConverter.ToInt32(bytes, so + 20);
                sections.Add(new MagicPeSection(name, virtualSize, virtualAddress, rawSize, rawPtr));
            }

            // --- Localiza a seção .data (aceita ".data" ou o nome alternativo "DATA") ---
            MagicPeSection dataSection = sections.FirstOrDefault(
                s => s.Name == ".data" || s.Name == "DATA")
                ?? throw new InvalidDataException(".data section not found in the PE.");

            if (dataSection.RawPtr < 0 || dataSection.RawPtr >= bytes.Length)
                throw new InvalidDataException(".data section PointerToRawData out of file.");

            int dataSize = Math.Min(dataSection.RawSize, bytes.Length - dataSection.RawPtr);
            byte[] data = dataSize > 0 ? bytes[dataSection.RawPtr..(dataSection.RawPtr + dataSize)] : [];

            return new MagicDllFile(
                path, bytes, data,
                dataSection.RawPtr, dataSize, dataSection.VirtualAddress,
                sections, roots);
        }

        /// <summary>
        /// Extrai o id do efeito do nome do arquivo: "magic_0021.dll" → 21; "magic_249.dll" → 249.
        /// Retorna -1 quando o nome não casa o padrão magic_XXXX.
        /// </summary>
        public static int ParseMagicIdFromName(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            int underscoreIdx = name.LastIndexOf('_');
            if (underscoreIdx >= 0 && int.TryParse(
                    name.AsSpan(underscoreIdx + 1),
                    System.Globalization.NumberStyles.Integer,
                    null, out int id))
                return id;
            return -1;
        }
    }
}

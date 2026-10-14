using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace FFXProjectEditor.FfxLib.Magic
{
    /// <summary>
    /// Represents a parsed magic DLL with its raw PE sections.
    /// </summary>
    ///
    /// Magic DLL structure (CORRECTED 2026-09-13, FFX-STRUCTURES audit F73/X9):
    ///   - PE header (standard Windows DLL)
    ///   - .text section: real x86 code (thunks forwarding to FFX.exe handlers) — NOT EgoVM
    ///     bytecode. Legacy evidence: `C3 CC CC CC` / `53 56 8B F1` prologue patterns
    ///     (FFX_STRUCTURE_COMPLETE §11.20; audit `FFX_STRUCTURE_AUDIT_KERNEL_BATTLE_MAGIC_2026-09-13.md` F73).
    ///   - .data section: EgoVM bytecode region + WD3/PPP containers + handler tables.
    ///     WHY: the previous model labeled .text as "EgoVmBytecode" — a confirmed structural
    ///     error that fed x86 bytes to the EgoVM disassembler. The proven .data parsers live in
    ///     FfxLib/MagicDll (MagicDllParser/PppParser, canonized on magic_0021/0098); locating the
    ///     exact bytecode region start inside .data is tracked as coverage gap P3.
    [DebuggerDisplay("{DllName,nq} — .text {TextSection.Length}B, .data {DataSection.Length}B")]
    public sealed class MagicDllFile
    {
        public string Path { get; }            // Original DLL path
        public string DllName { get; }         // e.g., "magic_0086"
        public int MagicId { get; }            // e.g., 86
        public byte[] TextSection { get; }     // x86 code (thunks to FFX.exe handlers)
        public byte[] DataSection { get; }     // EgoVM bytecode region + WD3/PPP + tables
        public byte[] RawSections { get; }     // Raw section data with offsets

        public MagicDllFile(string path, int magicId, byte[] textSection,
                            byte[] dataSection, byte[] rawWithOffsets)
        {
            Path = path;
            DllName = System.IO.Path.GetFileNameWithoutExtension(path);
            MagicId = magicId;
            TextSection = textSection;
            DataSection = dataSection;
            RawSections = rawWithOffsets;
        }
    }

    /// <summary>
    /// Reads a magic DLL file and extracts its raw .text/.data sections.
    /// Uses PE header parsing to locate the sections. Section semantics: see MagicDllFile.
    /// </summary>
    public static class MagicDllReader
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct ImageDosHeader
        {
            public ushort e_magic;       // MZ
            public ushort e_cblp;
            public ushort e_cp;
            public ushort e_crlc;
            public ushort e_cparhdr;
            public ushort e_minalloc;
            public ushort e_maxalloc;
            public ushort e_ss;
            public ushort e_sp;
            public ushort e_csum;
            public ushort e_ip;
            public ushort e_cs;
            public ushort e_lfarlc;
            public ushort e_ovno;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public ushort[] e_res1;
            public ushort e_oemid;
            public ushort e_oeminfo;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
            public ushort[] e_res2;
            public int e_lfanew;         // PE header offset
        }

        // Minimal PE header parsing: read section table directly.
        private const ushort DOS_MAGIC = 0x5A4D;  // MZ
        private const uint PE_MAGIC = 0x00004550;  // PE\0\0

        public static MagicDllFile Read(string dllPath)
        {
            var bytes = File.ReadAllBytes(dllPath);
            int magicId = ParseMagicIdFromName(dllPath);

            // Parse PE headers
            int peOffset;
            if (bytes.Length < 64)
                throw new InvalidDataException("File too small for DOS header");
            peOffset = BitConverter.ToInt32(bytes, 0x3C);
            if (peOffset + 24 > bytes.Length)
                throw new InvalidDataException("Invalid PE offset");

            // Verify PE signature
            if (BitConverter.ToUInt32(bytes, peOffset) != PE_MAGIC)
                throw new InvalidDataException("Invalid PE signature");

            // Read section table (after file header + optional header)
            int fileHeaderOffset = peOffset + 4;
            int machine = BitConverter.ToUInt16(bytes, fileHeaderOffset);
            int numSections = BitConverter.ToUInt16(bytes, fileHeaderOffset + 2);
            int optHeaderSize = BitConverter.ToUInt16(bytes, fileHeaderOffset + 16);

            int sectionTableOffset = fileHeaderOffset + 20 + optHeaderSize;
            if (sectionTableOffset + numSections * 40 > bytes.Length)
                throw new InvalidDataException("Invalid section table");

            byte[] textData = null;
            byte[] dataData = null;

            for (int i = 0; i < numSections; i++)
            {
                int so = sectionTableOffset + i * 40;
                string name = System.Text.Encoding.ASCII.GetString(bytes, so, 8).TrimEnd('\0');

                int virtualSize = BitConverter.ToInt32(bytes, so + 8);
                int virtualAddr = BitConverter.ToInt32(bytes, so + 12);
                int rawSize = BitConverter.ToInt32(bytes, so + 16);
                int rawPtr = BitConverter.ToInt32(bytes, so + 20);

                if (rawPtr + rawSize > bytes.Length)
                    rawSize = bytes.Length - rawPtr;

                var sectionBytes = rawSize > 0 ? bytes[rawPtr..(rawPtr + rawSize)] : [];

                if (name is ".text" or "CODE")
                    textData = sectionBytes;
                else if (name is ".data" or "DATA")
                    dataData = sectionBytes;
            }

            if (textData == null)
                throw new InvalidDataException("No .text section found");
            if (dataData == null)
                throw new InvalidDataException("No .data section found");

            return new MagicDllFile(dllPath, magicId, textData, dataData, bytes);
        }

        /// <summary>
        /// Parse magic ID from DLL name. Supports "magic_0086" -> 86, "magic_249" -> 249.
        /// </summary>
        public static int ParseMagicIdFromName(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            // Expect format: magic_XXXX
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

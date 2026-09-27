using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ps3;

namespace FFXProjectEditor.FfxLib.Magic
{
    // ──────────────────────────────────────────────────────────────
    //  PppParser — Parse WD3 containers + PPP dispatch tables + streams
    //
    //  Three levels of parsing:
    //    Level 1 — WD3 container: header (48B) + stream headers (32B ea)
    //              + raw stream data. Reuses Wd3StreamParser for low-level.
    //    Level 2 — Stream records: 16-byte sprite draw records within streams.
    //    Level 3 — Dispatch table: 40-byte entries mapping opcodes→handlers.
    //
    //  Source: FFX.exe RE (0x7170F0), magic_0086/0087, PPP_HANDLER_MASTER_20260716.md
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Top-level parse result combining WD3 structure, stream sprite records,
    /// and optionally a dispatch table.
    /// </summary>
    public sealed record PppParseResult(
        Wd3Blob? Wd3,
        IReadOnlyList<PppStreamRecord> StreamRecords,
        IReadOnlyList<PppDispatchEntry>? DispatchTable,
        IReadOnlyList<string> Errors);

    /// <summary>
    /// A single 16-byte PPP record found within a WD3 stream.
    /// These are the sprite draw commands / alpha records that the runtime
    /// constructs during relocation.
    /// </summary>
    public sealed record PppStreamRecord(
        int StreamIndex,
        int RecordOffset,
        byte OpcodeByte,      // 0x1D = DrawShape, or alpha selector
        byte TypeTag,         // 0x08 = sprite/quad
        ushort Count,         // Number of sprites / entries
        byte ColorR, byte ColorG, byte ColorB, byte ColorA,
        byte AlphaByte,       // Alpha selector (0x41-0x88)
        byte F4,              // Unknown
        ushort F5,            // Unknown
        uint TextureRef,      // Texture reference
        byte[] RawPayload,    // Remaining bytes after the 16-byte header
        string? OpcodeName);  // Resolved opcode name (if known)

    /// <summary>
    /// A single 40-byte PPP dispatch table entry from pppSysProgTbl.
    /// </summary>
    public sealed record PppDispatchEntry(
        int Index,
        string Name,           // Opcode name string (from .rdata)
        uint Slot1Func,        // +4: mode_specific_func_0
        uint Slot2Func,        // +8: mode_specific_func_1
        uint Slot3Func,        // +C: mode_specific_func_2
        PppOpcode? ResolvedOpcode); // Mapped to known opcode, if applicable

    /// <summary>
    /// Parses PPP (WD3 container + stream records + dispatch table) from
    /// raw DLL bytes or standalone data section blobs.
    /// </summary>
    public static class PppParser
    {
        // ──────────────────────────────────────────────────────────
        //  Constants
        // ──────────────────────────────────────────────────────────

        /// <summary>Size of each sprite draw record header (before per-vertex data).</summary>
        public const int SpriteRecordHeaderSize = 16;

        /// <summary>Opcode byte for DrawShape (most common sprite record).</summary>
        public const byte OpcodeDrawShape = 0x1D;

        /// <summary>Type tag for sprite/quad records.</summary>
        public const byte TypeTagSprite = 0x08;

        /// <summary>Size of each dispatch table entry (confirmed 40 bytes).</summary>
        public const int DispatchEntrySize = 40;

        /// <summary>Maximum dispatch entries (opcode count).</summary>
        public const int MaxDispatchEntries = 274;

        // ──────────────────────────────────────────────────────────
        //  Level 1: WD3 container parsing
        // ──────────────────────────────────────────────────────────

        /// <summary>
        /// Parse a WD3 container from raw bytes at the given offset.
        /// Delegates to <see cref="Wd3StreamParser"/> for the low-level
        /// container format.
        /// </summary>
        /// <param name="data">Raw DLL or .data section bytes.</param>
        /// <param name="offset">Offset where the WD3 blob starts.</param>
        /// <returns>A parsed <see cref="Wd3Blob"/>.</returns>
        /// <exception cref="Wd3ParseException">On invalid or truncated data.</exception>
        public static Wd3Blob ParseWd3Container(byte[] data, int offset = 0)
        {
            ArgumentNullException.ThrowIfNull(data);
            return Wd3StreamParser.Parse(data, offset);
        }

        /// <summary>
        /// Try to find a WD3 container anywhere in the byte array by searching
        /// for the "WD3\x01" magic sequence.
        /// </summary>
        /// <param name="data">Raw bytes to search.</param>
        /// <param name="wd3">The parsed WD3 blob, if found.</param>
        /// <returns>True if a valid WD3 container was found and parsed.</returns>
        public static bool TryFindWd3Container(byte[] data, out Wd3Blob wd3)
        {
            wd3 = default;

            if (data == null || data.Length < 16)
                return false;

            for (int i = 0; i <= data.Length - 16; i++)
            {
                // Quick check for WD3 magic
                if (data[i] != (byte)'W' || data[i + 1] != (byte)'D' ||
                    data[i + 2] != (byte)'3' || data[i + 3] != 0x01)
                    continue;

                try
                {
                    wd3 = Wd3StreamParser.Parse(data, i);
                    return true;
                }
                catch (Wd3ParseException)
                {
                    // False positive — keep searching
                    continue;
                }
            }

            return false;
        }

        // ──────────────────────────────────────────────────────────
        //  Level 2: Stream sprite-draw record parsing
        // ──────────────────────────────────────────────────────────

        /// <summary>
        /// Scan a WD3 stream's raw data for 16-byte sprite draw records.
        /// Each stream may contain overlapping views of rendering primitives.
        /// Records are stride-16 entries starting at offset 0 within the stream.
        /// </summary>
        /// <param name="streamData">Raw bytes of one WD3 stream.</param>
        /// <param name="streamIndex">Index of this stream (for reference).</param>
        /// <returns>List of parsed sprite draw records.</returns>
        public static List<PppStreamRecord> ParseStreamRecords(byte[] streamData, int streamIndex = 0)
        {
            var records = new List<PppStreamRecord>();

            if (streamData == null || streamData.Length < SpriteRecordHeaderSize)
                return records;

            for (int offset = 0; offset <= streamData.Length - SpriteRecordHeaderSize; offset += 16)
            {
                byte opcodeByte = streamData[offset];
                byte typeTag = streamData[offset + 1];
                ushort count = BitConverter.ToUInt16(streamData, offset + 2);
                byte cr = streamData[offset + 4];
                byte cg = streamData[offset + 5];
                byte cb = streamData[offset + 6];
                byte ca = streamData[offset + 7];
                byte alphaByte = streamData[offset + 8];
                byte f4 = streamData[offset + 9];
                ushort f5 = BitConverter.ToUInt16(streamData, offset + 10);
                uint textureRef = BitConverter.ToUInt32(streamData, offset + 12);

                // Extract remaining payload (max 64 bytes to be reasonable)
                int remaining = streamData.Length - (offset + SpriteRecordHeaderSize);
                int payloadLen = Math.Min(remaining, 64);
                byte[] payload = payloadLen > 0
                    ? streamData.Skip(offset + SpriteRecordHeaderSize).Take(payloadLen).ToArray()
                    : [];

                // Resolve opcode name
                string? opcodeName = opcodeByte switch
                {
                    OpcodeDrawShape => "pppDrawShape",
                    _ when alphaByte is >= 0x41 and <= 0x88 => $"alpha_0x{alphaByte:X2}",
                    _ => null
                };

                records.Add(new PppStreamRecord(
                    streamIndex, offset,
                    opcodeByte, typeTag, count,
                    cr, cg, cb, ca,
                    alphaByte, f4, f5, textureRef,
                    payload, opcodeName));
            }

            return records;
        }

        /// <summary>
        /// Parse sprite draw records from all streams in a WD3 blob.
        /// </summary>
        public static List<PppStreamRecord> ParseAllStreamRecords(Wd3Blob wd3)
        {
            var all = new List<PppStreamRecord>();
            for (int i = 0; i < wd3.Streams.Length; i++)
            {
                var records = ParseStreamRecords(wd3.Streams[i].Data, i);
                all.AddRange(records);
            }
            return all;
        }

        // ──────────────────────────────────────────────────────────
        //  Level 3: Dispatch table (40-byte entries)
        // ──────────────────────────────────────────────────────────

        /// <summary>
        /// Parse a PPP dispatch table from raw bytes.
        /// The dispatch table is an array of 40-byte entries in the DLL's
        /// .data section (typically 36-274 entries).
        /// </summary>
        /// <param name="data">Raw DLL bytes.</param>
        /// <param name="tableOffset">File offset of the dispatch table.</param>
        /// <param name="entryCount">Number of entries (36 local or 274 total).</param>
        /// <returns>List of dispatch entries.</returns>
        public static List<PppDispatchEntry> ParseDispatchTable(
            byte[] data, int tableOffset, int entryCount = 36)
        {
            var entries = new List<PppDispatchEntry>();
            entryCount = Math.Min(entryCount, MaxDispatchEntries);

            for (int i = 0; i < entryCount; i++)
            {
                int entryOff = tableOffset + i * DispatchEntrySize;
                if (entryOff + DispatchEntrySize > data.Length)
                    break;

                // +0: name pointer (string in .rdata)
                uint namePtr = BitConverter.ToUInt32(data, entryOff);
                string name = ExtractDllString(data, namePtr) ?? $"entry_{i}";

                // +4: slot 1 (mode_specific_func_0)
                uint slot1 = BitConverter.ToUInt32(data, entryOff + 4);
                // +8: slot 2 (mode_specific_func_1)
                uint slot2 = BitConverter.ToUInt32(data, entryOff + 8);
                // +C: slot 3 (mode_specific_func_2)
                uint slot3 = BitConverter.ToUInt32(data, entryOff + 12);

                // Try to resolve to known opcode
                PppOpcode? resolved = ResolveOpcodeByName(name);

                entries.Add(new PppDispatchEntry(i, name, slot1, slot2, slot3, resolved));
            }

            return entries;
        }

        // ──────────────────────────────────────────────────────────
        //  Top-level parse
        // ──────────────────────────────────────────────────────────

        /// <summary>
        /// Full PPP parse: WD3 container + all stream records + optional
        /// dispatch table at a known offset.
        /// </summary>
        /// <param name="data">Raw DLL or .data bytes.</param>
        /// <param name="wd3Offset">Offset of the WD3 blob (negative = auto-detect).</param>
        /// <param name="dispatchTableOffset">
        /// Offset of the 40-byte dispatch table (-1 = omit dispatch parse).
        /// </param>
        /// <param name="dispatchEntryCount">Number of dispatch entries to parse.</param>
        /// <returns>Complete parse result.</returns>
        public static PppParseResult Parse(
            byte[] data,
            int wd3Offset = -1,
            int dispatchTableOffset = -1,
            int dispatchEntryCount = 36)
        {
            var errors = new List<string>();

            // --- Level 1: WD3 ---
            Wd3Blob? wd3 = null;
            if (wd3Offset >= 0)
            {
                try
                {
                    wd3 = Wd3StreamParser.Parse(data, wd3Offset);
                }
                catch (Exception ex)
                {
                    errors.Add($"WD3 parse error at +0x{wd3Offset:X}: {ex.Message}");
                }
            }
            else
            {
                if (!TryFindWd3Container(data, out var found))
                    errors.Add("No WD3 container found in data (auto-detect failed).");
                else
                    wd3 = found;
            }

            // --- Level 2: Stream records ---
            var streamRecords = wd3 is not null
                ? ParseAllStreamRecords(wd3.Value)
                : new List<PppStreamRecord>();

            // --- Level 3: Dispatch table ---
            List<PppDispatchEntry>? dispatch = null;
            if (dispatchTableOffset >= 0)
            {
                try
                {
                    dispatch = ParseDispatchTable(data, dispatchTableOffset, dispatchEntryCount);
                }
                catch (Exception ex)
                {
                    errors.Add($"Dispatch table parse error: {ex.Message}");
                }
            }

            return new PppParseResult(wd3, streamRecords, dispatch, errors);
        }

        // ──────────────────────────────────────────────────────────
        //  Instruction extraction
        // ──────────────────────────────────────────────────────────

        /// <summary>
        /// Convert parsed stream records to PPP instructions for the instruction
        /// list model (bridging stream records to PppInstruction).
        /// </summary>
        public static List<PppInstruction> StreamRecordsToInstructions(
            IReadOnlyList<PppStreamRecord> records)
        {
            var instructions = new List<PppInstruction>();

            foreach (var rec in records)
            {
                // Build raw operands from the record fields
                var operands = new byte[SpriteRecordHeaderSize + rec.RawPayload.Length];
                operands[0] = rec.OpcodeByte;
                operands[1] = rec.TypeTag;
                BitConverter.GetBytes(rec.Count).CopyTo(operands, 2);
                operands[4] = rec.ColorR;
                operands[5] = rec.ColorG;
                operands[6] = rec.ColorB;
                operands[7] = rec.ColorA;
                operands[8] = rec.AlphaByte;
                operands[9] = rec.F4;
                BitConverter.GetBytes(rec.F5).CopyTo(operands, 10);
                BitConverter.GetBytes(rec.TextureRef).CopyTo(operands, 12);

                if (rec.RawPayload.Length > 0)
                    Array.Copy(rec.RawPayload, 0, operands, SpriteRecordHeaderSize, rec.RawPayload.Length);

                // Map opcode byte to known PppOpcode if possible
                PppOpcode opcode = rec.OpcodeByte switch
                {
                    OpcodeDrawShape => PppOpcode.PppDrawShape,
                    _ => (PppOpcode)(0x0000 + rec.OpcodeByte)
                };

                instructions.Add(new PppInstruction(
                    opcode, operands, operands.Length,
                    PppSlotType.ModeFunc2, rec.RecordOffset));
            }

            return instructions;
        }

        // ──────────────────────────────────────────────────────────
        //  Helpers
        // ──────────────────────────────────────────────────────────

        /// <summary>
        /// Try to read a null-terminated ASCII string from a DLL byte array,
        /// given a relative virtual address (RVA).
        /// </summary>
        static string? ExtractDllString(byte[] data, uint rva)
        {
            // Guard: RVA should be within reasonable DLL bounds
            if (rva == 0 || rva >= data.Length)
                return null;

            int maxLen = Math.Min(256, data.Length - (int)rva);
            int end = (int)rva;
            while (end < (int)rva + maxLen && data[end] != 0)
                end++;

            int len = end - (int)rva;
            if (len <= 0)
                return null;

            try
            {
                return System.Text.Encoding.ASCII.GetString(data, (int)rva, len);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Try to resolve a Yonishi opcode name string to a known PppOpcode.
        /// </summary>
        static PppOpcode? ResolveOpcodeByName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            // Strip "ppp" prefix if present for lookup
            string lookupKey = name.StartsWith("ppp", StringComparison.OrdinalIgnoreCase)
                ? name
                : $"ppp{name}";

            foreach (PppOpcode op in Enum.GetValues<PppOpcode>())
            {
                string enumName = op.ToString();
                if (string.Equals(enumName, lookupKey, StringComparison.OrdinalIgnoreCase))
                    return op;
            }

            return null;
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  Helper extensions
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Extension methods for interpreting raw WD3 stream data.
    /// </summary>
    public static class Wd3StreamExtensions
    {
        /// <summary>
        /// Parse sprite draw records from a single stream.
        /// </summary>
        public static List<PppStreamRecord> ParsePppRecords(this Wd3Stream stream, int streamIndex = 0)
            => PppParser.ParseStreamRecords(stream.Data, streamIndex);

        /// <summary>
        /// Parse sprite draw records from all streams in a WD3 blob.
        /// </summary>
        public static List<PppStreamRecord> ParseAllPppRecords(this Wd3Blob wd3)
            => PppParser.ParseAllStreamRecords(wd3);
    }
}

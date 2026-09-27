using System;
using System.Collections.Generic;
using System.IO;

namespace FFXProjectEditor.FfxLib.Text
{
    public sealed partial class TextTable_File
    {
        /// <summary>Field-string (8-byte entry) writer: rebuilds the string area from the (possibly edited)
        /// regular/simplified scripts and repoints every offset, deduplicating shared strings (regular ==
        /// simplified, or repeats). Mirrors the proven NameDescription rebuild pattern. A no-edit Read->Write
        /// is byte-identical when the source lays its pool out in first-seen entry order with shared-string
        /// dedup (the <c>--textstr-rt0</c> gate proves it on help_txt.bin and unlocks the write family).</summary>
        /// <param name="decoder">Field-string decoder (JP/US).</param>
        /// <param name="includeTrailingPadding">When true (default, used by the standalone field-string writer
        /// and the <c>--textstr-rt0</c> gate), re-emit the captured trailing alignment padding so a no-edit
        /// save stays byte-faithful. When false, emit ONLY the entry table + string pool (no trailing padding);
        /// used when this table is a CHUNK inside a larger container (e.g. the Event EV01 text chunks) whose
        /// own writer owns the inter-chunk alignment padding and would otherwise double-pad. Additive: the
        /// default keeps every existing caller and the gate untouched.</param>
        public byte[] Write(Dictionary<byte, char> decoder, bool includeTrailingPadding = true)
        {
            ArgumentNullException.ThrowIfNull(decoder);

            int headerLength = EntryCount * 0x08;
            byte[] entryTable = new byte[headerLength];
            List<byte> stringBytes = new();
            Dictionary<string, ushort> pool = new(StringComparer.Ordinal);

            ushort Append(byte[] scriptBytes)
            {
                string key = Convert.ToHexString(scriptBytes);
                if (pool.TryGetValue(key, out ushort existing))
                    return existing;

                int absolute = headerLength + stringBytes.Count;
                if (absolute > ushort.MaxValue)
                    throw new InvalidDataException("Field-string table string pool exceeded 64KB.");

                ushort offset = (ushort)absolute;
                stringBytes.AddRange(scriptBytes);
                stringBytes.Add(0);
                pool[key] = offset;
                return offset;
            }

            foreach (TextTable_Entry entry in Entries)
            {
                byte[] regularScript = TextBinary_Util.ResolveTextBytes(entry.RegularText, entry.RegularScriptBytes, decoder);
                byte[] simplifiedScript = TextBinary_Util.ResolveTextBytes(entry.SimplifiedText, entry.SimplifiedScriptBytes, decoder);

                ushort regularOffset = Append(regularScript);
                ushort simplifiedOffset = Append(simplifiedScript);

                int b = entry.Index * 0x08;
                TextBinary_Util.WriteUInt16(entryTable, b, regularOffset);
                entryTable[b + 2] = entry.RegularFlags;
                entryTable[b + 3] = entry.RegularChoices;
                TextBinary_Util.WriteUInt16(entryTable, b + 4, simplifiedOffset);
                entryTable[b + 6] = entry.SimplifiedFlags;
                entryTable[b + 7] = entry.SimplifiedChoices;
            }

            using MemoryStream stream = new();
            stream.Write(entryTable, 0, entryTable.Length);
            stream.Write(stringBytes.ToArray(), 0, stringBytes.Count);
            // Re-emit the trailing alignment padding (to a 4-byte boundary in the proven samples).
            // The header has no total-length field, so this is preserved from Read to stay byte-faithful.
            // A container caller (Event writer) passes includeTrailingPadding:false and re-pads itself.
            if (includeTrailingPadding && TrailingPaddingBytes.Length > 0)
                stream.Write(TrailingPaddingBytes, 0, TrailingPaddingBytes.Length);
            return stream.ToArray();
        }
    }
}

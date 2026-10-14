using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Text
{
    public sealed class PointerScriptTable_File
    {
        const int ProvenEntryCount = 52;
        const int Bank0Start = 0x00;
        const int Bank0End = 0x0F;
        const int Bank1Start = 0x10;
        const int Bank1End = 0x1F;
        const int PivotSlotIndex = 0x20;
        const int ReservedGapStart = 0x21;
        const int ReservedGapEnd = 0x23;
        const int Bank3Start = 0x24;
        const int Bank3End = 0x2F;
        const int ReservedTailStart = 0x30;
        const int ReservedTailEnd = 0x33;

        public required int FileSize { get; init; }
        public required int HeaderLength { get; init; }
        public required string VariantLabel { get; init; }
        public required IReadOnlyList<PointerScriptTable_Entry> Entries { get; init; }

        // Verbatim clone of the source file. The preserve-only Write re-stamps only the
        // fixed 4-byte pointer header from this buffer, so a no-edit save is byte-identical.
        public required byte[] OriginalBytes { get; init; }

        public int EntryCount => Entries.Count;
        public int PopulatedEntryCount => Entries.Count(entry => !entry.IsEmpty);
        public int EmptyEntryCount => Entries.Count(entry => entry.IsEmpty);
        public int DistinctOffsetCount => Entries.Where(entry => !entry.IsEmpty).Select(entry => entry.Offset).Distinct().Count();
        public int Bank0PopulatedCount => CountPopulated(Bank0Start, Bank0End);
        public int Bank1PopulatedCount => CountPopulated(Bank1Start, Bank1End);
        public int PivotPopulatedCount => CountPopulated(PivotSlotIndex, PivotSlotIndex);
        public int Bank3PopulatedCount => CountPopulated(Bank3Start, Bank3End);
        public string BankTopologySummary =>
            $"B0 {Bank0PopulatedCount} · B1 {Bank1PopulatedCount} · pivot {PivotPopulatedCount} · B3 {Bank3PopulatedCount}";

        public string Summary =>
            $"{VariantLabel} · header {HeaderLength:X4}h · {EntryCount} 4-byte pointers · {PopulatedEntryCount} populated slot(s) · {EmptyEntryCount} empty slot(s) · {DistinctOffsetCount} distinct script offset(s) · {BankTopologySummary}.";

        public static PointerScriptTable_File Read(byte[] bytes, Dictionary<byte, char> decoder)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            ArgumentNullException.ThrowIfNull(decoder);

            PointerScriptTableLayout layout = ValidateLayout(bytes);
            List<PointerScriptTable_Entry> entries = new(layout.Offsets.Length);
            Dictionary<int, int> firstSeenByOffset = new();

            for (int i = 0; i < layout.Offsets.Length; i++)
            {
                int offset = layout.Offsets[i];
                if (offset == 0)
                {
                    entries.Add(new PointerScriptTable_Entry
                    {
                        Index = i,
                        Offset = 0,
                        IsEmpty = true,
                        SharedWithIndex = null,
                        ScriptBytes = Array.Empty<byte>(),
                        Text = string.Empty
                    });
                    continue;
                }

                byte[] scriptBytes = TextBinary_Util.ReadNullTerminatedScript(bytes, offset);
                string text = DecodeScriptForDisplay(scriptBytes, decoder);
                int? sharedWith = firstSeenByOffset.TryGetValue(offset, out int firstIndex) ? firstIndex : null;
                firstSeenByOffset.TryAdd(offset, i);

                entries.Add(new PointerScriptTable_Entry
                {
                    Index = i,
                    Offset = offset,
                    IsEmpty = false,
                    SharedWithIndex = sharedWith,
                    ScriptBytes = scriptBytes,
                    Text = text
                });
            }

            return new PointerScriptTable_File
            {
                FileSize = bytes.Length,
                HeaderLength = layout.HeaderLength,
                VariantLabel = layout.VariantLabel,
                Entries = entries,
                OriginalBytes = bytes.ToArray()
            };
        }

        // Preserve-only writer (KeyItem_File pattern): clone the original buffer and re-stamp
        // ONLY the fixed 4-byte pointer header (one little-endian int32 per slot, at slot*4).
        // The script text bodies + any padding/header tail are preserved verbatim, so a
        // no-edit save (Read -> Write) is byte-identical by construction.
        public static byte[] Write(PointerScriptTable_File table)
        {
            ArgumentNullException.ThrowIfNull(table);

            byte[] output = table.OriginalBytes.ToArray();

            int headerEnd = table.Entries.Count * 0x04;
            if (headerEnd > output.Length)
                throw new InvalidOperationException(
                    $"Pointer-script header ({headerEnd:X}h) runs past the source buffer ({output.Length:X}h).");

            foreach (PointerScriptTable_Entry entry in table.Entries)
            {
                if (entry.Index < 0 || entry.Index >= table.Entries.Count)
                    throw new InvalidOperationException($"Pointer-script slot index {entry.Index} is outside the table range.");

                int pointerOffset = entry.Index * 0x04;
                int value = entry.IsEmpty ? 0 : entry.Offset;
                WriteInt32(output, pointerOffset, value);
            }

            return output;
        }

        static void WriteInt32(byte[] bytes, int offset, int value)
        {
            if (offset < 0 || offset + 4 > bytes.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            bytes[offset] = (byte)(value & 0xFF);
            bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
            bytes[offset + 2] = (byte)((value >> 16) & 0xFF);
            bytes[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        public static bool MatchesProvenLayout(byte[] bytes)
        {
            if (bytes == null)
                return false;

            try
            {
                ValidateLayout(bytes);
                return true;
            }
            catch
            {
                return false;
            }
        }

        static PointerScriptTableLayout ValidateLayout(byte[] bytes)
        {
            if (bytes.Length < 0x10)
                throw new InvalidDataException("Pointer-script table is too small.");

            int headerLength = TextBinary_Util.ReadInt32(bytes, 0x00);
            if (headerLength <= 0 || headerLength % 0x04 != 0)
                throw new InvalidDataException($"Pointer-script table has an invalid 4-byte pointer header length: {headerLength:X}h.");

            if (headerLength > bytes.Length)
                throw new InvalidDataException("Pointer-script table header runs past EOF.");

            int entryCount = headerLength / 0x04;
            if (entryCount != ProvenEntryCount)
            {
                throw new InvalidDataException(
                    $"Pointer-script table matched the 4-byte pointer shape, but only the proven {ProvenEntryCount}-slot variant is currently accepted (got {entryCount}).");
            }

            int[] offsets = new int[entryCount];
            for (int i = 0; i < entryCount; i++)
            {
                int offset = TextBinary_Util.ReadInt32(bytes, i * 0x04);
                offsets[i] = offset;

                if (offset == 0)
                    continue;

                if (offset < headerLength || offset >= bytes.Length)
                    throw new InvalidDataException($"Pointer-script slot {i:X2}h points outside the script area: {offset:X4}h.");

                if (TextBinary_Util.FindNullTerminator(bytes, offset) < 0)
                    throw new InvalidDataException($"Pointer-script slot {i:X2}h is missing a terminating NULL byte.");
            }

            if (offsets[0] != headerLength)
                throw new InvalidDataException($"Pointer-script first slot must resolve to the header end {headerLength:X4}h, got {offsets[0]:X4}h.");

            EnsureZeroRange(offsets, ReservedGapStart, ReservedGapEnd, "reserved gap");
            EnsureZeroRange(offsets, ReservedTailStart, ReservedTailEnd, "reserved tail");

            ValidateMonotonicBank(offsets, Bank0Start, Bank0End, "bank B0");
            ValidateMonotonicBank(offsets, Bank1Start, Bank1End, "bank B1");
            ValidateMonotonicBank(offsets, Bank3Start, Bank3End, "bank B3");
            ValidateCrossBankOrdering(offsets);

            return new PointerScriptTableLayout
            {
                HeaderLength = headerLength,
                Offsets = offsets,
                VariantLabel = "POINTER SCRIPT TABLE · 52-SLOT FOUR-BANK VARIANT · PROVEN"
            };
        }

        int CountPopulated(int startIndex, int endIndex)
        {
            int count = 0;
            for (int i = startIndex; i <= endIndex; i++)
            {
                if (!Entries[i].IsEmpty)
                    count++;
            }

            return count;
        }

        static void EnsureZeroRange(int[] offsets, int startIndex, int endIndex, string label)
        {
            for (int i = startIndex; i <= endIndex; i++)
            {
                if (offsets[i] != 0)
                    throw new InvalidDataException($"Pointer-script {label} slot {i:X2}h must stay zero in the proven four-bank variant.");
            }
        }

        static void ValidateMonotonicBank(int[] offsets, int startIndex, int endIndex, string label)
        {
            int? previousNonZeroOffset = null;
            for (int i = startIndex; i <= endIndex; i++)
            {
                int offset = offsets[i];
                if (offset == 0)
                    continue;

                if (previousNonZeroOffset.HasValue && offset < previousNonZeroOffset.Value)
                    throw new InvalidDataException($"Pointer-script {label} slot {i:X2}h breaks non-decreasing offset order.");

                previousNonZeroOffset = offset;
            }
        }

        static void ValidateCrossBankOrdering(int[] offsets)
        {
            int? bank0Last = GetLastNonZeroOffset(offsets, Bank0Start, Bank0End);
            int? bank1First = GetFirstNonZeroOffset(offsets, Bank1Start, Bank1End);
            int? bank1Last = GetLastNonZeroOffset(offsets, Bank1Start, Bank1End);
            int? pivotOffset = offsets[PivotSlotIndex] == 0 ? null : offsets[PivotSlotIndex];
            int? bank3First = GetFirstNonZeroOffset(offsets, Bank3Start, Bank3End);

            if (bank0Last.HasValue && pivotOffset.HasValue && bank0Last.Value >= pivotOffset.Value)
                throw new InvalidDataException("Pointer-script pivot slot must stay after bank B0 in the proven four-bank variant.");

            if (pivotOffset.HasValue && bank1First.HasValue && pivotOffset.Value >= bank1First.Value)
                throw new InvalidDataException("Pointer-script pivot slot must stay before bank B1 in the proven four-bank variant.");

            if (bank1Last.HasValue && bank3First.HasValue && bank1Last.Value >= bank3First.Value)
                throw new InvalidDataException("Pointer-script bank B3 must stay after bank B1 in the proven four-bank variant.");
        }

        static int? GetFirstNonZeroOffset(int[] offsets, int startIndex, int endIndex)
        {
            for (int i = startIndex; i <= endIndex; i++)
            {
                if (offsets[i] != 0)
                    return offsets[i];
            }

            return null;
        }

        static int? GetLastNonZeroOffset(int[] offsets, int startIndex, int endIndex)
        {
            for (int i = endIndex; i >= startIndex; i--)
            {
                if (offsets[i] != 0)
                    return offsets[i];
            }

            return null;
        }

        static string DecodeScriptForDisplay(byte[] scriptBytes, Dictionary<byte, char> decoder)
        {
            try
            {
                return TextBinary_Util.DecodeScriptToString(scriptBytes, decoder, true);
            }
            catch (Exception ex)
            {
                return $"<DECODE-ERROR:{ex.Message}>";
            }
        }

        sealed class PointerScriptTableLayout
        {
            public required int HeaderLength { get; init; }
            public required int[] Offsets { get; init; }
            public required string VariantLabel { get; init; }
        }
    }

    public sealed class PointerScriptTable_Entry
    {
        public required int Index { get; init; }
        public required int Offset { get; init; }
        public required bool IsEmpty { get; init; }
        public required int? SharedWithIndex { get; init; }
        public required byte[] ScriptBytes { get; init; }
        public required string Text { get; init; }

        public string IndexLabel => $"Script {Index:X2}h";
        public string Title =>
            IsEmpty
                ? "(Empty Slot)"
                : string.IsNullOrWhiteSpace(Text) ? "(Empty Script)" : Text;
        public string Summary
        {
            get
            {
                if (IsEmpty)
                    return "Null pointer slot inside the fixed 52-entry script table.";

                if (SharedWithIndex.HasValue)
                    return $"Offset {Offset:X4}h · shared with slot {SharedWithIndex.Value:X2}h.";

                return $"Offset {Offset:X4}h · {ScriptBytes.Length} script byte(s).";
            }
        }

        public string SearchBlob =>
            IsEmpty
                ? $"empty {Index:X2}"
                : $"{Index:X2} {Offset:X4} {Text} {Summary}";
    }
}

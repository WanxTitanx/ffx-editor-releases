using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Customization;
using FFXProjectEditor.FfxLib.Dictionaries;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Shop
{
    public enum ShopTableKind
    {
        Item,
        Gear
    }

    internal static class ShopTable_File
    {
        const int HeaderLength = 0x14;
        const int EntryLength = 0x22;
        const int SlotCount = 0x10;
        const ushort ExpectedMinIndex = 0x0000;
        const ushort ExpectedMaxIndex = 0x002E;
        const int ExpectedEntryCount = 0x2F;

        public static ShopTable Read(byte[] bytes, ShopTableKind kind, ShopGearCatalog? gearCatalog = null)
        {
            ArgumentNullException.ThrowIfNull(bytes);

            IndexedFixedTableHeader header = Customization_File.ReadHeader(bytes);
            ValidateHeader(header, bytes.Length, kind);

            List<ShopEntry> entries = new(header.EntryCount);
            for (int i = 0; i < header.EntryCount; i++)
            {
                int index = header.MinIndex + i;
                int offset = HeaderLength + (i * header.EntryLength);
                if (offset < 0 || offset + header.EntryLength > bytes.Length)
                    throw new InvalidDataException($"{GetKindLabel(kind)} entry {index:D2} extends past EOF at offset 0x{offset:X4}.");

                byte[] raw = bytes.Skip(offset).Take(header.EntryLength).ToArray();
                ushort unusedPriceWord = ReadUInt16(raw, 0x00);

                List<ShopSlotEntry> slots = new(SlotCount);
                for (int slotIndex = 0; slotIndex < SlotCount; slotIndex++)
                {
                    ushort rawValue = ReadUInt16(raw, 0x02 + (slotIndex * 0x02));
                    slots.Add(BuildSlotEntry(kind, slotIndex, rawValue, gearCatalog));
                }

                entries.Add(new ShopEntry
                {
                    Index = index,
                    RawBytes = raw,
                    UnusedPriceWord = unusedPriceWord,
                    Slots = slots
                });
            }

            return new ShopTable
            {
                Kind = kind,
                OriginalBytes = bytes.ToArray(),
                Header = header,
                Entries = entries
            };
        }

        public static byte[] Write(ShopTable table)
        {
            ArgumentNullException.ThrowIfNull(table);
            ValidateWriterShape(table);
            ValidateWriterValues(table);

            byte[] bytes = table.OriginalBytes.ToArray();
            for (int i = 0; i < table.Entries.Count; i++)
            {
                ShopEntry entry = table.Entries[i];
                int offset = HeaderLength + (i * table.Header.EntryLength);

                for (int slotIndex = 0; slotIndex < SlotCount; slotIndex++)
                {
                    WriteUInt16(bytes, offset + 0x02 + (slotIndex * 0x02), entry.Slots[slotIndex].RawValue);
                }
            }

            return bytes;
        }

        // Preserve-only RT0 path (no value-domain guard): clone OriginalBytes + re-stamp the 16 slots per
        // entry in place -> a no-edit save is byte-identical even when the source has non-"strong" slot values.
        public static byte[] WriteIdentity(ShopTable table)
        {
            ArgumentNullException.ThrowIfNull(table);
            byte[] bytes = table.OriginalBytes.ToArray();
            for (int i = 0; i < table.Entries.Count; i++)
            {
                int offset = HeaderLength + (i * table.Header.EntryLength);
                for (int slotIndex = 0; slotIndex < SlotCount; slotIndex++)
                    WriteUInt16(bytes, offset + 0x02 + (slotIndex * 0x02), table.Entries[i].Slots[slotIndex].RawValue);
            }
            return bytes;
        }

        internal static void AssertSlotOnlyDiff(ShopTable table, byte[] candidateBytes)
        {
            ArgumentNullException.ThrowIfNull(table);
            ArgumentNullException.ThrowIfNull(candidateBytes);

            if (candidateBytes.Length != table.OriginalBytes.Length)
                throw new InvalidDataException("Guarded shop writer changed the file length, which is outside the proven slot-only slice.");

            for (int offset = 0; offset < candidateBytes.Length; offset++)
            {
                if (candidateBytes[offset] == table.OriginalBytes[offset])
                    continue;

                if (!IsSlotPayloadOffset(table, offset))
                    throw new InvalidDataException($"Guarded shop writer attempted to touch offset 0x{offset:X4}, which is outside the proven 16-slot payload slice.");
            }
        }

        internal static ShopSlotEntry BuildSlotEntry(ShopTableKind kind, int slotIndex, ushort rawValue, ShopGearCatalog? gearCatalog)
        {
            return new ShopSlotEntry
            {
                SlotIndex = slotIndex,
                RawValue = rawValue,
                DisplayLabel = ResolveDisplayLabel(kind, rawValue, gearCatalog),
                CandidateSummary = ResolveCandidateSummary(kind, rawValue, gearCatalog),
                ConfidenceLabel = ResolveConfidenceLabel(kind, rawValue, gearCatalog)
            };
        }

        static void ValidateHeader(IndexedFixedTableHeader header, int totalFileLength, ShopTableKind kind)
        {
            string label = GetKindLabel(kind);
            if (header.MinIndex != ExpectedMinIndex || header.MaxIndex != ExpectedMaxIndex || header.EntryCount != ExpectedEntryCount)
            {
                throw new InvalidDataException(
                    $"{label} uses unexpected index range {header.MinIndex}..{header.MaxIndex} ({header.EntryCount} entries); expected {ExpectedMinIndex}..{ExpectedMaxIndex} ({ExpectedEntryCount} entries).");
            }

            if (header.EntryLength != EntryLength)
                throw new InvalidDataException($"{label} uses unexpected entry length 0x{header.EntryLength:X2}; expected 0x{EntryLength:X2}.");

            int expectedDataLength = header.EntryCount * header.EntryLength;
            if (header.TotalDataLength != expectedDataLength)
                throw new InvalidDataException($"{label} declares data length 0x{header.TotalDataLength:X4}, but {header.EntryCount} rows of 0x{header.EntryLength:X2} require 0x{expectedDataLength:X4}.");

            if (HeaderLength + header.TotalDataLength > totalFileLength)
                throw new InvalidDataException($"{label} declares a data section that extends past EOF.");
        }

        static string ResolveDisplayLabel(ShopTableKind kind, ushort rawValue, ShopGearCatalog? gearCatalog)
        {
            if (rawValue == 0)
                return "<Zero / empty slot candidate>";

            if (kind == ShopTableKind.Gear)
            {
                if (gearCatalog != null && gearCatalog.EntriesByIndex.TryGetValue(rawValue, out ShopGearCatalogEntry? catalogEntry))
                    return $"Catalog {rawValue:X4}h · {catalogEntry.DisplayLabel}";

                return $"Buyable gear catalog candidate {rawValue:X4}h";
            }

            if (TryResolveItemGameIndex(rawValue, out ushort gameIndex, out string? gameIndexLabel))
                return $"Encoded item game-index {gameIndex:X3}h · {gameIndexLabel}";

            if (Item_Dictionary.Instance.TryGetValue(rawValue, out string? directItemLabel))
                return $"Direct item-id candidate · {directItemLabel}";

            return $"{rawValue:X4}h";
        }

        static string ResolveCandidateSummary(ShopTableKind kind, ushort rawValue, ShopGearCatalog? gearCatalog)
        {
            if (rawValue == 0)
            {
                return "External parser behavior skips zero-valued slots, so this editor treats the value as an empty-slot candidate only. No stronger semantic is claimed yet.";
            }

            if (kind == ShopTableKind.Gear)
            {
                if (gearCatalog != null && gearCatalog.EntriesByIndex.TryGetValue(rawValue, out ShopGearCatalogEntry? catalogEntry))
                {
                    return $"Raw ushort {rawValue:X4}h lands inside the local shop_arms.bin catalog as {catalogEntry.Summary}. {catalogEntry.DetailSummary}. This strengthens the catalog-index mapping, but it still does not justify a production writer or a fake-safe gear naming surface yet.";
                }

                return $"Raw ushort {rawValue:X4}h is treated only as a buyable-gear catalog candidate. The external parser hints at BUYABLE_GEAR[idx], but this editor still lacks a trusted local name/model table, so the slot stays research-only and read-only.";
            }

            if (TryResolveItemGameIndex(rawValue, out ushort gameIndex, out string? gameIndexLabel))
            {
                return $"Raw ushort {rawValue:X4}h carries game category Items with low index {gameIndex:X3}h and resolves as '{gameIndexLabel}'. Current real-file samples fit encoded item game indices much better than raw item ids, so the writer remains closed until that contract survives broader samples and deliberate edit tests.";
            }

            if (Item_Dictionary.Instance.TryGetValue(rawValue, out string? directItemLabel))
            {
                return $"Raw ushort {rawValue:X4}h resolves directly in Item_Dictionary as '{directItemLabel}', but the current real-file sample no longer points there as the strongest fit. Candidate wording stays deliberate until broader evidence proves whether any category-0 variant matters.";
            }

            return $"Raw ushort {rawValue:X4}h only. Slot semantics are still not proven hard enough for a writer or for production-friendly labeling.";
        }

        static string ResolveConfidenceLabel(ShopTableKind kind, ushort rawValue, ShopGearCatalog? gearCatalog)
        {
            if (rawValue == 0)
                return "Empty-slot candidate";

            if (kind == ShopTableKind.Gear)
            {
                if (gearCatalog != null && gearCatalog.EntriesByIndex.ContainsKey(rawValue))
                    return "Strong catalog candidate";

                return "Catalog candidate";
            }

            if (TryResolveItemGameIndex(rawValue, out _, out _))
                return "Strong encoded-index candidate";

            if (Item_Dictionary.Instance.TryGetValue(rawValue, out _))
                return "Weak direct-id fallback";

            return "Opaque raw";
        }

        internal static bool TryResolveItemGameIndex(ushort rawValue, out ushort index, out string? label)
        {
            index = 0;
            label = null;

            try
            {
                byte category = FfxCommon_Util.GetGameCategory(rawValue);
                if (category != (byte)GameCategory_Enum.Items)
                    return false;

                index = FfxCommon_Util.GetGameIndex(rawValue);
                label = FfxCommon_Util.GetGameIndexName(category, index);
                return !string.IsNullOrWhiteSpace(label) && !string.Equals(label, "<NOT_INDEXED>", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsAllowedEditableValue(ShopTableKind kind, ushort rawValue, ShopGearCatalog? gearCatalog)
        {
            if (rawValue == 0)
                return true;

            if (kind == ShopTableKind.Item)
                return TryResolveItemGameIndex(rawValue, out _, out _);

            return gearCatalog != null && gearCatalog.EntriesByIndex.ContainsKey(rawValue);
        }

        static ushort ReadUInt16(byte[] bytes, int offset)
        {
            if (offset < 0 || offset + 2 > bytes.Length)
                return 0;

            return (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
        }

        static void WriteUInt16(byte[] bytes, int offset, ushort value)
        {
            if (offset < 0 || offset + 2 > bytes.Length)
                return;

            bytes[offset] = (byte)(value & 0x00FF);
            bytes[offset + 1] = (byte)((value & 0xFF00) >> 8);
        }

        static void ValidateWriterShape(ShopTable table)
        {
            if (table.Header.EntryLength != EntryLength)
                throw new InvalidDataException($"Guarded shop writer expected fixed entry length 0x{EntryLength:X2} but received 0x{table.Header.EntryLength:X2}.");

            if (table.Entries.Count != table.Header.EntryCount)
                throw new InvalidDataException($"Guarded shop writer expected {table.Header.EntryCount} rows but received {table.Entries.Count}.");

            for (int entryIndex = 0; entryIndex < table.Entries.Count; entryIndex++)
            {
                ShopEntry entry = table.Entries[entryIndex];
                int expectedIndex = table.Header.MinIndex + entryIndex;
                if (entry.Index != expectedIndex)
                    throw new InvalidDataException($"Guarded shop writer expected row index {expectedIndex} but received {entry.Index}.");

                if (entry.Slots.Count != SlotCount)
                    throw new InvalidDataException($"Guarded shop writer expected {SlotCount} slots for row {entry.Index} but received {entry.Slots.Count}.");
            }
        }

        static void ValidateWriterValues(ShopTable table)
        {
            foreach (ShopEntry entry in table.Entries)
            {
                foreach (ShopSlotEntry slot in entry.Slots)
                {
                    if (slot.RawValue == 0)
                        continue;

                    bool isAllowed = table.Kind switch
                    {
                        ShopTableKind.Item => string.Equals(slot.ConfidenceLabel, "Strong encoded-index candidate", StringComparison.Ordinal),
                        ShopTableKind.Gear => string.Equals(slot.ConfidenceLabel, "Strong catalog candidate", StringComparison.Ordinal),
                        _ => false
                    };

                    if (!isAllowed)
                    {
                        throw new InvalidDataException(
                            $"Guarded shop writer rejected row {entry.Index:D2} slot {slot.SlotIndex:D2} raw value 0x{slot.RawValue:X4} because it is outside the proven {table.Kind} slot domain.");
                    }
                }
            }
        }

        static bool IsSlotPayloadOffset(ShopTable table, int offset)
        {
            int dataStart = HeaderLength;
            int dataEnd = HeaderLength + (table.Header.EntryCount * table.Header.EntryLength);
            if (offset < dataStart || offset >= dataEnd)
                return false;

            int relative = offset - HeaderLength;
            int offsetWithinEntry = relative % table.Header.EntryLength;
            return offsetWithinEntry >= 0x02 && offsetWithinEntry < 0x22;
        }

        static string GetKindLabel(ShopTableKind kind)
        {
            return kind == ShopTableKind.Item ? "item_shop.bin" : "arms_shop.bin";
        }
    }

    internal sealed class ShopTable
    {
        public required ShopTableKind Kind { get; init; }
        public required byte[] OriginalBytes { get; init; }
        public required IndexedFixedTableHeader Header { get; init; }
        public required IReadOnlyList<ShopEntry> Entries { get; init; }
    }

    internal sealed class ShopEntry
    {
        public required int Index { get; init; }
        public required byte[] RawBytes { get; init; }
        public required ushort UnusedPriceWord { get; init; }
        public required IReadOnlyList<ShopSlotEntry> Slots { get; init; }
    }

    internal sealed class ShopSlotEntry
    {
        public required int SlotIndex { get; init; }
        public required ushort RawValue { get; init; }
        public required string DisplayLabel { get; init; }
        public required string CandidateSummary { get; init; }
        public required string ConfidenceLabel { get; init; }
    }
}

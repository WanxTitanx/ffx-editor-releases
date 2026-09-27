using FFXProjectEditor.FfxLib.Customization;
using FFXProjectEditor.FfxLib.Dictionaries;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Treasure
{
    internal static class BukiGetTreasureCatalog_File
    {
        const int HeaderLength = 0x14;
        const int EntryLength = 0x10;
        const int ExpectedMinIndex = 0;
        const int ExpectedMaxIndex = 85;
        const int ExpectedDataLength = 0x560;

        public static BukiGetTreasureCatalog Read(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);

            IndexedFixedTableHeader header = Customization_File.ReadHeader(bytes);
            ValidateHeader(header, bytes.Length);

            Dictionary<int, BukiGetTreasureEntry> entriesByIndex = new(header.EntryCount);
            for (int i = 0; i < header.EntryCount; i++)
            {
                int index = header.MinIndex + i;
                int entryOffset = HeaderLength + (i * header.EntryLength);
                ushort[] words = ReadWords(bytes, entryOffset, header.EntryLength);
                byte flags = bytes[entryOffset + 0x00];
                byte owner = bytes[entryOffset + 0x01];
                byte gearType = bytes[entryOffset + 0x02];
                byte unknown03 = bytes[entryOffset + 0x03];
                byte damageFormula = bytes[entryOffset + 0x04];
                byte power = bytes[entryOffset + 0x05];
                byte critBonus = bytes[entryOffset + 0x06];
                byte slotCount = bytes[entryOffset + 0x07];
                string abilitySummary = BuildAbilitySummary(words.Skip(4));

                BukiGetTreasureEntry entry = new()
                {
                    Index = index,
                    RawWords = words,
                    Flags = flags,
                    Owner = owner,
                    GearType = gearType,
                    Unknown03 = unknown03,
                    DamageFormula = damageFormula,
                    Power = power,
                    CritBonus = critBonus,
                    SlotCount = slotCount,
                    ObservedSlotCount = slotCount,
                    AbilitySummary = abilitySummary,
                    DisplayLabel = $"buki_get #{index:D4} · {BuildOwnerLabel(owner)} {BuildGearTypeLabel(gearType)}",
                    Summary = BuildSummary(index, owner, gearType, slotCount, abilitySummary),
                    DetailSummary = BuildDetailSummary(words, flags, owner, gearType, unknown03, damageFormula, power, critBonus, slotCount, abilitySummary)
                };

                entriesByIndex[index] = entry;
            }

            return new BukiGetTreasureCatalog
            {
                OriginalBytes = bytes.ToArray(),
                Header = header,
                EntriesByIndex = entriesByIndex
            };
        }

        // Preserve-only writer (PROVEN pattern, mirrors KeyItem_File.Write):
        // clone the original bytes and re-stamp ONLY the clearly-fixed scalar fields
        // each entry carries (the 8 little-endian RawWords). RawWords is the exact LE
        // decode of the original entry block, so a no-edit save is byte-identical by
        // construction; everything outside the data section is preserved verbatim.
        public static byte[] Write(BukiGetTreasureCatalog catalog)
        {
            ArgumentNullException.ThrowIfNull(catalog);

            ValidateHeader(catalog.Header, catalog.OriginalBytes.Length);
            ValidateWriteShape(catalog.Header, catalog.EntriesByIndex.Count);

            byte[] output = catalog.OriginalBytes.ToArray();

            for (int i = 0; i < catalog.Header.EntryCount; i++)
            {
                int index = catalog.Header.MinIndex + i;
                if (!catalog.EntriesByIndex.TryGetValue(index, out BukiGetTreasureEntry? entry))
                    throw new InvalidOperationException($"buki_get.bin writer is missing entry {index}.");

                ValidateEntryWordCount(entry.RawWords, catalog.Header.EntryLength, index);

                int entryOffset = HeaderLength + (i * catalog.Header.EntryLength);
                WriteWords(output, entryOffset, entry.RawWords);
            }

            return output;
        }

        static void ValidateWriteShape(IndexedFixedTableHeader header, int entryCount)
        {
            if (entryCount != header.EntryCount)
            {
                throw new InvalidOperationException(
                    $"buki_get.bin writer only supports preserving the existing entry count. Expected {header.EntryCount}, got {entryCount}.");
            }
        }

        static void ValidateEntryWordCount(IReadOnlyList<ushort> words, int entryLength, int index)
        {
            if (words.Count != entryLength / 2)
                throw new InvalidOperationException(
                    $"buki_get.bin entry {index} expected {entryLength / 2} words, found {words.Count}.");
        }

        static void WriteWords(byte[] bytes, int entryOffset, IReadOnlyList<ushort> words)
        {
            for (int i = 0; i < words.Count; i++)
            {
                int offset = entryOffset + (i * 2);
                if (offset < 0 || offset + 1 >= bytes.Length)
                    throw new InvalidOperationException($"buki_get.bin write at offset 0x{offset:X4} extends past EOF.");

                bytes[offset] = (byte)(words[i] & 0xFF);
                bytes[offset + 1] = (byte)((words[i] >> 8) & 0xFF);
            }
        }

        static void ValidateHeader(IndexedFixedTableHeader header, int totalFileLength)
        {
            if (header.MinIndex != ExpectedMinIndex
                || header.MaxIndex != ExpectedMaxIndex
                || header.EntryLength != EntryLength
                || header.TotalDataLength != ExpectedDataLength)
            {
                throw new InvalidDataException(
                    $"buki_get.bin shape mismatch. Expected min=0 max=85 entryLen=0x10 total=0x560; got min={header.MinIndex} max={header.MaxIndex} entryLen=0x{header.EntryLength:X2} total=0x{header.TotalDataLength:X4}.");
            }

            if (HeaderLength + header.TotalDataLength > totalFileLength)
                throw new InvalidDataException("buki_get.bin declares a data section that extends past EOF.");
        }

        static ushort[] ReadWords(byte[] bytes, int entryOffset, int entryLength)
        {
            if (entryOffset < 0 || entryOffset + entryLength > bytes.Length)
                throw new InvalidDataException($"buki_get.bin entry at offset 0x{entryOffset:X4} extends past EOF.");

            ushort[] words = new ushort[entryLength / 2];
            for (int i = 0; i < words.Length; i++)
            {
                int offset = entryOffset + (i * 2);
                words[i] = (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
            }

            return words;
        }

        static string BuildSummary(int index, byte owner, byte gearType, byte slotCount, string abilitySummary)
        {
            string lead = $"buki_get #{index:D4} · {BuildOwnerLabel(owner)} {BuildGearTypeLabel(gearType)} · slots {slotCount}";
            return string.IsNullOrWhiteSpace(abilitySummary)
                ? $"{lead} · no decoded 0x8xxx auto-ability words"
                : $"{lead} · {abilitySummary}";
        }

        static string BuildDetailSummary(
            IReadOnlyList<ushort> words,
            byte flags,
            byte owner,
            byte gearType,
            byte unknown03,
            byte damageFormula,
            byte power,
            byte critBonus,
            byte slotCount,
            string abilitySummary)
        {
            string rawWords = string.Join(" ", words.Select((word, index) => $"w{index}:{word:X4}h"));
            string abilitySection = string.IsNullOrWhiteSpace(abilitySummary)
                ? "auto-abilities: none decoded from 0x8xxx words"
                : $"auto-abilities: {abilitySummary}";

            return $"owner/type: {BuildOwnerLabel(owner)} {BuildGearTypeLabel(gearType)} · flags {flags:X2}h · unk03 {unknown03:X2}h · formula {damageFormula} · power {power} · crit {critBonus} · slots {slotCount} · {abilitySection} · raw words {rawWords} · final equipment name bridge is not proved here.";
        }

        static string BuildOwnerLabel(byte owner) => owner switch
        {
            0 => "Tidus",
            1 => "Yuna",
            2 => "Auron",
            3 => "Lulu",
            4 => "Wakka",
            5 => "Kimahri",
            6 => "Rikku",
            _ => $"owner {owner:X2}h"
        };

        static string BuildGearTypeLabel(byte gearType) => gearType switch
        {
            0 => "Weapon",
            1 => "Armor",
            _ => $"type {gearType:X2}h"
        };

        internal static string BuildAbilitySummary(IEnumerable<ushort> rawWords)
        {
            List<string> abilities = [];
            foreach (ushort rawWord in rawWords)
            {
                if (rawWord == 0x00FF)
                    continue;

                if ((rawWord & 0xF000) == 0x8000)
                {
                    ushort abilityIndex = (ushort)(rawWord & 0x0FFF);
                    abilities.Add(AutoAbility_Dictionary.Instance.TryGetValue(abilityIndex, out string? abilityName)
                        ? abilityName
                        : $"auto-ability #{abilityIndex:X3}");
                    continue;
                }

                if (rawWord != 0)
                    abilities.Add($"{rawWord:X4}h");
            }

            return abilities.Count == 0
                ? string.Empty
                : string.Join(" / ", abilities);
        }
    }

    internal sealed class BukiGetTreasureCatalog
    {
        public required byte[] OriginalBytes { get; init; }
        public required IndexedFixedTableHeader Header { get; init; }
        public required Dictionary<int, BukiGetTreasureEntry> EntriesByIndex { get; init; }
    }

    internal sealed class BukiGetTreasureEntry
    {
        public required int Index { get; init; }
        public ushort[] RawWords { get; set; } = [];
        public required byte Flags { get; set; }
        public required byte Owner { get; set; }
        public required byte GearType { get; set; }
        public required byte Unknown03 { get; set; }
        public required byte DamageFormula { get; set; }
        public required byte Power { get; set; }
        public required byte CritBonus { get; set; }
        public required byte SlotCount { get; set; }
        public required byte ObservedSlotCount { get; init; }
        public IReadOnlyList<ushort> AbilityWords => RawWords.Length >= 8 ? RawWords[4..] : [];
        public required string AbilitySummary { get; set; }
        public required string DisplayLabel { get; set; }
        public required string Summary { get; set; }
        public required string DetailSummary { get; set; }
    }
}

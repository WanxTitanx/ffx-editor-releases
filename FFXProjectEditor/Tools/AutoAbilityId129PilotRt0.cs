using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Arm;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.Tools
{
    // Headless gate for the first no-grow AutoAbility pilot. It never writes the supplied files:
    // it fills the existing free slot 129 in memory with a data-driven donor shape, rebuilds text
    // and arms_rate.bin, then rereads and preserve-writes the staged output.
    // Run via: FFXProjectEditor.exe --autoability-id129-pilot-rt0 [a_ability.bin] [arms_rate.bin]
    internal static class AutoAbilityId129PilotRt0
    {
        const int TargetIndex = 129;
        const int DonorIndex = 30; // Firestrike: element-strike donor for the first staged slot.

        public static int Run(string abilityPath, string pricePath)
        {
            Console.WriteLine("=== AutoAbility id129 PILOT RT0 (existing free slot, text + price rebuild) ===");
            Console.WriteLine($"ability file : {abilityPath}");
            Console.WriteLine($"price file   : {pricePath}");
            if (!File.Exists(abilityPath)) { Console.WriteLine("a_ability.bin NOT FOUND"); return 2; }
            if (!File.Exists(pricePath)) { Console.WriteLine("arms_rate.bin NOT FOUND"); return 2; }

            byte[] origAbility = File.ReadAllBytes(abilityPath);
            byte[] origPrices = File.ReadAllBytes(pricePath);

            AutoAbilityTable table;
            try { table = AutoAbility_File.Read(origAbility, origPrices); }
            catch (Exception ex) { Console.WriteLine($"READ THREW: {ex.Message}"); return 2; }

            AutoAbilityEntry? donor = table.Entries.FirstOrDefault(entry => entry.Index == DonorIndex);
            AutoAbilityEntry? target = table.Entries.FirstOrDefault(entry => entry.Index == TargetIndex);
            if (donor is null)
            {
                Console.WriteLine($"VERDICT: BLOCKED - donor #{DonorIndex} not found.");
                return 2;
            }
            if (target is null)
            {
                Console.WriteLine($"VERDICT: BLOCKED - target slot #{TargetIndex} not found; use grow route first.");
                return 2;
            }

            bool targetLooksFree = target.RawBytes.All(b => b == 0)
                && string.IsNullOrWhiteSpace(target.NameText)
                && string.IsNullOrWhiteSpace(target.DescriptionText);

            AutoAbilityEntry staged = BuildStagedEntry(donor, TargetIndex);
            List<AutoAbilityEntry> entries = table.Entries
                .Select(entry => entry.Index == TargetIndex ? staged : entry)
                .ToList();

            AutoAbilityTable writeTable = new AutoAbilityTable
            {
                OriginalAbilityBytes = table.OriginalAbilityBytes,
                AbilityHeader = table.AbilityHeader,
                PriceTable = table.PriceTable,
                PriceCoverageCount = table.PriceCoverageCount,
                Entries = entries
            };

            byte[] editedAbility;
            byte[] editedPrices;
            AutoAbilityTable reread;
            try
            {
                editedAbility = AutoAbility_File.WriteAbilitiesAndText(writeTable, FfxEncoding.UsDecoder);
                editedPrices = AutoAbility_File.WritePrices(writeTable);
                reread = AutoAbility_File.Read(editedAbility, editedPrices);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WRITE/REREAD THREW: {ex.Message}");
                return 2;
            }

            AutoAbilityEntry rereadTarget = reread.Entries.First(entry => entry.Index == TargetIndex);
            bool countOk = reread.Entries.Count == table.Entries.Count;
            bool priceOk = rereadTarget.GilPrice == donor.GilPrice
                && reread.PriceTable.Rates.Count == reread.Entries.Count;
            bool textOk = string.Equals(rereadTarget.NameText, staged.NameText, StringComparison.Ordinal)
                && string.Equals(rereadTarget.DescriptionText, staged.DescriptionText, StringComparison.Ordinal);
            bool dataShapeOk = SameDataDrivenShape(staged, rereadTarget);
            bool specialBytesPreserved = SameSpecialBytes(staged, rereadTarget);
            bool preserveOk = AutoAbility_File.WriteAbilities(reread).SequenceEqual(editedAbility)
                && Arms_Rate.Write(reread.PriceTable).SequenceEqual(editedPrices);

            Console.WriteLine($"entries      : {table.Entries.Count} -> {reread.Entries.Count}");
            Console.WriteLine($"target       : #{TargetIndex} (equip word 0x{(0x8000 | TargetIndex):X4}, wasFree={targetLooksFree})");
            Console.WriteLine($"donor        : #{DonorIndex} {donor.Label}");
            Console.WriteLine($"ability len  : {origAbility.Length} -> {editedAbility.Length}");
            Console.WriteLine($"price len    : {origPrices.Length} -> {editedPrices.Length}");
            Console.WriteLine($"checks       : count={countOk}, price={priceOk}, text={textOk}, dataShape={dataShapeOk}, special62to67Preserved={specialBytesPreserved}, preserve={preserveOk}");

            if (!targetLooksFree)
                Console.WriteLine("NOTE: target slot was not blank before staging; gate still proves disposable write/reread shape.");

            if (!countOk || !priceOk || !textOk || !dataShapeOk || !specialBytesPreserved || !preserveOk)
            {
                Console.WriteLine("VERDICT: FAIL - id129 staged auto-ability did not satisfy the writer/readback contract.");
                return 1;
            }

            Console.WriteLine("VERDICT: PASS - id129 can be staged as a data-driven auto-ability with text and arms_rate lockstep in memory. Gameplay validation remains external.");
            return 0;
        }

        static AutoAbilityEntry BuildStagedEntry(AutoAbilityEntry donor, int targetIndex)
        {
            byte[] raw = donor.RawBytes.ToArray();

            return new AutoAbilityEntry
            {
                Index = targetIndex,
                Label = $"Firebrand #{targetIndex:D3}",
                RawBytes = raw,
                NameText = $"Firebrand #{targetIndex:D3}",
                AuxiliaryText1 = donor.AuxiliaryText1,
                DescriptionText = "Firestrike clone staged in an auto-ability slot.",
                AuxiliaryText2 = donor.AuxiliaryText2,
                GilPrice = donor.GilPrice,
                SosFlagByte = donor.SosFlagByte,
                ElementStrike = donor.ElementStrike,
                ElementAbsorb = donor.ElementAbsorb,
                ElementImmune = donor.ElementImmune,
                ElementResist = donor.ElementResist,
                ElementWeak = donor.ElementWeak,
                StatusInflict = donor.StatusInflict.ToArray(),
                StatusDuration = donor.StatusDuration.ToArray(),
                StatusResist = donor.StatusResist.ToArray(),
                StatIncreaseAmount = donor.StatIncreaseAmount,
                StatIncreaseFlags = donor.StatIncreaseFlags,
                AutoStatusesPermanent = donor.AutoStatusesPermanent,
                AutoStatusesTemporal = donor.AutoStatusesTemporal,
                AutoStatusesExtra = donor.AutoStatusesExtra,
                ExtraStatusInflict = donor.ExtraStatusInflict,
                ExtraStatusImmunities = donor.ExtraStatusImmunities,
                AbilityFlags62 = donor.AbilityFlags62,
                AbilityFlags63 = donor.AbilityFlags63,
                AbilityFlags64 = donor.AbilityFlags64,
                AbilityFlags65 = donor.AbilityFlags65,
                AbilityFlags66 = donor.AbilityFlags66,
                UnknownByte67 = donor.UnknownByte67,
                Icon = donor.Icon,
                GroupIndex = donor.GroupIndex,
                GroupLevel = donor.GroupLevel,
                InternationalBonusIndex = donor.InternationalBonusIndex
            };
        }

        static bool SameDataDrivenShape(AutoAbilityEntry expected, AutoAbilityEntry actual)
        {
            return expected.SosFlagByte == actual.SosFlagByte
                && expected.ElementStrike == actual.ElementStrike
                && expected.ElementAbsorb == actual.ElementAbsorb
                && expected.ElementImmune == actual.ElementImmune
                && expected.ElementResist == actual.ElementResist
                && expected.ElementWeak == actual.ElementWeak
                && expected.StatusInflict.SequenceEqual(actual.StatusInflict)
                && expected.StatusDuration.SequenceEqual(actual.StatusDuration)
                && expected.StatusResist.SequenceEqual(actual.StatusResist)
                && expected.StatIncreaseAmount == actual.StatIncreaseAmount
                && expected.StatIncreaseFlags == actual.StatIncreaseFlags
                && expected.AutoStatusesPermanent == actual.AutoStatusesPermanent
                && expected.AutoStatusesTemporal == actual.AutoStatusesTemporal
                && expected.AutoStatusesExtra == actual.AutoStatusesExtra
                && expected.ExtraStatusInflict == actual.ExtraStatusInflict
                && expected.ExtraStatusImmunities == actual.ExtraStatusImmunities
                && SameSpecialBytes(expected, actual)
                && expected.Icon == actual.Icon
                && expected.GroupIndex == actual.GroupIndex
                && expected.GroupLevel == actual.GroupLevel
                && expected.InternationalBonusIndex == actual.InternationalBonusIndex;
        }

        static bool SameSpecialBytes(AutoAbilityEntry expected, AutoAbilityEntry actual)
        {
            return expected.AbilityFlags62 == actual.AbilityFlags62
                && expected.AbilityFlags63 == actual.AbilityFlags63
                && expected.AbilityFlags64 == actual.AbilityFlags64
                && expected.AbilityFlags65 == actual.AbilityFlags65
                && expected.AbilityFlags66 == actual.AbilityFlags66
                && expected.UnknownByte67 == actual.UnknownByte67;
        }
    }
}

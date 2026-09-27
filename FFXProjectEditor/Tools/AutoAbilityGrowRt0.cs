using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Arm;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.Tools
{
    // Headless gate for the AutoAbility grow writer path. It never writes the supplied files:
    // it grows a_ability.bin by one 0x6C entry in memory, grows arms_rate.bin in lockstep,
    // rebuilds text through the same path the UI uses, and proves the grown output rereads cleanly.
    // Run via: FFXProjectEditor.exe --autoability-grow-rt0 [a_ability.bin] [arms_rate.bin]
    internal static class AutoAbilityGrowRt0
    {
        public static int Run(string abilityPath, string pricePath)
        {
            Console.WriteLine("=== AutoAbility_File GROW RT0 (in-memory 1-entry append + arms_rate lockstep) ===");
            Console.WriteLine($"ability file : {abilityPath}");
            Console.WriteLine($"price file   : {pricePath}");
            if (!File.Exists(abilityPath)) { Console.WriteLine("a_ability.bin NOT FOUND"); return 2; }
            if (!File.Exists(pricePath)) { Console.WriteLine("arms_rate.bin NOT FOUND"); return 2; }

            byte[] origAbility = File.ReadAllBytes(abilityPath);
            byte[] origPrices = File.ReadAllBytes(pricePath);

            AutoAbilityTable table;
            try { table = AutoAbility_File.Read(origAbility, origPrices); }
            catch (Exception ex) { Console.WriteLine($"READ THREW: {ex.Message}"); return 2; }

            AutoAbilityEntry donor = table.Entries.FirstOrDefault(entry => entry.Index == 128)
                ?? table.Entries.Last();
            int newIndex = table.AbilityHeader.MaxIndex + 1;
            AutoAbilityEntry newEntry = BuildNewAbilityEntry(donor, newIndex);

            byte[] grownAbilityBase;
            byte[] grownPriceBase;
            try
            {
                grownAbilityBase = AutoAbility_File.GrowByOne(table, newEntry);
                grownPriceBase = Arms_Rate.AppendRate(table.PriceTable, newEntry.GilPrice);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GROW THREW: {ex.Message}");
                return 2;
            }

            AutoAbilityTable grownBase;
            try { grownBase = AutoAbility_File.Read(grownAbilityBase, grownPriceBase); }
            catch (Exception ex) { Console.WriteLine($"REREAD GROWN BASE THREW: {ex.Message}"); return 2; }

            List<AutoAbilityEntry> entries = grownBase.Entries
                .Select(entry => entry.Index == newIndex ? newEntry : entry)
                .ToList();

            AutoAbilityTable writeTable = new AutoAbilityTable
            {
                OriginalAbilityBytes = grownAbilityBase,
                AbilityHeader = grownBase.AbilityHeader,
                PriceTable = grownBase.PriceTable,
                PriceCoverageCount = grownBase.PriceCoverageCount,
                Entries = entries
            };

            byte[] grownAbility;
            byte[] grownPrices;
            AutoAbilityTable reread;
            try
            {
                grownAbility = AutoAbility_File.WriteAbilitiesAndText(writeTable, FfxEncoding.UsDecoder);
                grownPrices = AutoAbility_File.WritePrices(writeTable);
                reread = AutoAbility_File.Read(grownAbility, grownPrices);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WRITE/REREAD THREW: {ex.Message}");
                return 2;
            }

            bool countOk = reread.Entries.Count == table.Entries.Count + 1;
            bool priceOk = reread.PriceTable.Rates.Count == reread.Entries.Count;
            bool indexOk = reread.AbilityHeader.MaxIndex == newIndex && reread.Entries.Any(entry => entry.Index == newIndex);
            AutoAbilityEntry rereadNew = reread.Entries.First(entry => entry.Index == newIndex);
            bool specialBytesPreserved = SameSpecialBytes(donor, rereadNew);
            bool preserveOk = AutoAbility_File.WriteAbilities(reread).SequenceEqual(grownAbility)
                && Arms_Rate.Write(reread.PriceTable).SequenceEqual(grownPrices);

            Console.WriteLine($"entries      : {table.Entries.Count} -> {reread.Entries.Count}");
            Console.WriteLine($"new id       : {newIndex} (equip word 0x{(0x8000 | newIndex):X4})");
            Console.WriteLine($"ability len  : {origAbility.Length} -> {grownAbility.Length}");
            Console.WriteLine($"price len    : {origPrices.Length} -> {grownPrices.Length}");
            Console.WriteLine($"checks       : count={countOk}, price={priceOk}, index={indexOk}, special62to67Preserved={specialBytesPreserved}, preserve={preserveOk}");

            if (!countOk || !priceOk || !indexOk || !specialBytesPreserved || !preserveOk)
            {
                Console.WriteLine("VERDICT: FAIL - grown table did not satisfy the structural grow contract.");
                return 1;
            }

            Console.WriteLine("VERDICT: PASS - grow path appends one auto-ability and arms_rate entry, rereads cleanly, and preserve-writes byte-identically after grow.");
            return 0;
        }

        static AutoAbilityEntry BuildNewAbilityEntry(AutoAbilityEntry donor, int newIndex)
        {
            byte[] raw = donor.RawBytes.ToArray();

            string donorName = string.IsNullOrWhiteSpace(donor.NameText) ? donor.Label : donor.NameText;
            string newName = $"New {donorName} #{newIndex:D3}";

            return new AutoAbilityEntry
            {
                Index = newIndex,
                Label = newName,
                RawBytes = raw,
                NameText = newName,
                AuxiliaryText1 = donor.AuxiliaryText1,
                DescriptionText = $"Duplicate of #{donor.Index:D3}; edit AU1..AU7 fields as needed.",
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.Utils.Encoding;
using Xe.BinaryMapper;
using static FFXProjectEditor.FfxLib.Ability.Ability_Structs;

namespace FFXProjectEditor.FfxLib.Ability
{
    public enum KimahriExtendedSpellKind
    {
        RonsoBlueMage,
        Demita,
        LancetPlus,
    }

    public sealed class KimahriExtendedSpellEntry
    {
        public required int NewId { get; init; }
        public required int DonorId { get; init; }
        public required string Name { get; init; }
        public required byte MpCost { get; init; }
        public required KimahriExtendedSpellKind Kind { get; init; }
    }

    public sealed class KimahriExtendedGrowResult
    {
        public required byte[] OriginalBytes { get; init; }
        public required byte[] GrownBytes { get; init; }
        public required int OriginalEntryCount { get; init; }
        public required int NewEntryCount { get; init; }
        public required IReadOnlyList<KimahriExtendedSpellEntry> Spells { get; init; }
        public required int OriginalLength { get; init; }
        public required int GrownLength { get; init; }
        public required bool ExistingRecordPayloadPreserved { get; init; }
        public required bool ExistingTextPoolPrefixPreserved { get; init; }
        public required bool RereadCountOk { get; init; }
        public required bool PreserveWriteOk { get; init; }

        public int AppendedCount => Spells.Count;

        public bool Pass => ExistingRecordPayloadPreserved
            && ExistingTextPoolPrefixPreserved
            && RereadCountOk
            && PreserveWriteOk;
    }

    /// <summary>
    /// Append Spira Reforge Kimahri Blue Mage pack: clone every Ronso Rage OD row (104–115) as MP Skill
    /// commands, plus Demita and Lancet+ signature spells.
    /// </summary>
    public static class KimahriExtendedCommandWriter
    {
        public const int VanillaRowCount = 320;
        public const int WardPackRowCount = CommandGrowWriter.RadiantWardCommandId + 2;

        public const int DemiDonorId = 78;
        public const int LancetDonorId = 32;
        public const int RonsoRageMenuDonorId = 282;

        public static readonly int[] RonsoRageDonorIds =
        [
            104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115,
        ];

        /// <summary>MP costs aligned with RonsoManaHook <c>kRonsoSkillCosts</c> (row order 104..115).</summary>
        public static readonly byte[] RonsoBlueMageMpCosts =
        [
            40, 70, 50, 100, 45, 65, 70, 90, 110, 85, 250, 255,
        ];

        /// <summary>Engine loop2 case 4 — Ronso Rage / OD center ring (+296, 24 slots). Vanilla #104–115, #282.</summary>
        public const int KimahriRonsoRingSubMenu = 4;

        /// <summary>Engine loop2 case 14 — Blue Magic children (+232, 32 slots). Isolated from OD ring.</summary>
        public const int KimahriBlueMagicRingSubMenu = 14;

        public const string DemitaName = "Demita";
        public const string LancetPlusName = "Lancet+";

        public static int ExpectedAppendCount => RonsoRageDonorIds.Length + 2;

        public static KimahriExtendedGrowResult AppendKimahriBasePack(byte[] originalBytes)
        {
            ArgumentNullException.ThrowIfNull(originalBytes);

            EntryListFile original = EntryListFile.Unpack(originalBytes);
            if (original.Header.EntrySize != CommandGrowWriter.CommandEntrySize)
            {
                throw new InvalidDataException(
                    $"Expected command.bin entry size 0x{CommandGrowWriter.CommandEntrySize:X}, got 0x{original.Header.EntrySize:X}.");
            }

            List<Ability_Command> entries = Ability_Command.ReadList(originalBytes, hasExtraInfo: true);
            if (entries.Count < VanillaRowCount)
            {
                throw new InvalidDataException(
                    $"Expected at least {VanillaRowCount} rows before Kimahri grow, got {entries.Count}.");
            }

            foreach (int donorId in RonsoRageDonorIds)
                EnsureDonor(entries, donorId, $"Ronso #{donorId}");
            EnsureDonor(entries, DemiDonorId, "Demi");
            EnsureDonor(entries, LancetDonorId, "Lancet");

            if (RonsoRageDonorIds.Length != RonsoBlueMageMpCosts.Length)
            {
                throw new InvalidOperationException("Ronso donor ids and MP cost table length mismatch.");
            }

            int startId = entries.Count;
            List<KimahriExtendedSpellEntry> spellLog = [];
            List<Ability_Command> toAppend = [];

            for (int i = 0; i < RonsoRageDonorIds.Length; i++)
            {
                int donorId = RonsoRageDonorIds[i];
                Ability_Command clone = CloneCommand(entries[donorId]);
                ApplyRonsoBlueMageRecipe(clone, RonsoBlueMageMpCosts[i]);
                toAppend.Add(clone);
                spellLog.Add(new KimahriExtendedSpellEntry
                {
                    NewId = startId + toAppend.Count - 1,
                    DonorId = donorId,
                    Name = DecodeUs(clone.NameScriptBytes, donorId),
                    MpCost = RonsoBlueMageMpCosts[i],
                    Kind = KimahriExtendedSpellKind.RonsoBlueMage,
                });
            }

            Ability_Command demita = CloneCommand(entries[DemiDonorId]);
            ApplyDemitaRecipe(demita);
            toAppend.Add(demita);
            spellLog.Add(new KimahriExtendedSpellEntry
            {
                NewId = startId + toAppend.Count - 1,
                DonorId = DemiDonorId,
                Name = DemitaName,
                MpCost = demita.CostMp,
                Kind = KimahriExtendedSpellKind.Demita,
            });

            Ability_Command lancetPlus = CloneCommand(entries[LancetDonorId]);
            ApplyLancetPlusRecipe(lancetPlus);
            toAppend.Add(lancetPlus);
            spellLog.Add(new KimahriExtendedSpellEntry
            {
                NewId = startId + toAppend.Count - 1,
                DonorId = LancetDonorId,
                Name = LancetPlusName,
                MpCost = lancetPlus.CostMp,
                Kind = KimahriExtendedSpellKind.LancetPlus,
            });

            byte[] textPool = original.SecondFile.ToArray();
            byte[] grownRows = original.FirstFile.ToArray();
            foreach (Ability_Command command in toAppend)
                grownRows = grownRows.Concat(BuildNewRow(command, ref textPool)).ToArray();

            byte[] grownBytes = EntryListFile.Pack(
                entrySize: CommandGrowWriter.CommandEntrySize,
                entryCount: checked((short)(entries.Count + toAppend.Count)),
                firstFile: grownRows,
                secondFile: textPool);

            List<Ability_Command> reread = Ability_Command.ReadList(grownBytes, hasExtraInfo: true);

            bool existingRowsPreserved = grownRows.Length >= original.FirstFile.Length
                && original.FirstFile.AsSpan().SequenceEqual(grownRows.AsSpan(0, original.FirstFile.Length));
            bool existingTextPreserved = textPool.Length >= original.SecondFile.Length
                && original.SecondFile.AsSpan().SequenceEqual(textPool.AsSpan(0, original.SecondFile.Length));

            return new KimahriExtendedGrowResult
            {
                OriginalBytes = originalBytes,
                GrownBytes = grownBytes,
                OriginalEntryCount = entries.Count,
                NewEntryCount = reread.Count,
                Spells = spellLog,
                OriginalLength = originalBytes.Length,
                GrownLength = grownBytes.Length,
                ExistingRecordPayloadPreserved = existingRowsPreserved,
                ExistingTextPoolPrefixPreserved = existingTextPreserved,
                RereadCountOk = reread.Count == entries.Count + toAppend.Count,
                PreserveWriteOk = Ability_Command.WriteList(reread, hasExtraInfo: true).SequenceEqual(grownBytes),
            };
        }

        static void EnsureDonor(List<Ability_Command> entries, int donorId, string label)
        {
            if (donorId < 0 || donorId >= entries.Count)
            {
                throw new InvalidDataException(
                    $"Donor {label} (#{donorId}) is out of range for this command.bin (count={entries.Count}).");
            }
        }

        static void ApplyRonsoBlueMageRecipe(Ability_Command command, byte mpCost)
        {
            command.CharacterUser = Character_Enum.Kimahri;
            command.CostMp = mpCost;
            command.CostOverdrive = 0;
            command.OverdriveCategory = 0;
            command.SubMenuCategorization = KimahriBlueMagicRingSubMenu;
            command.SubSubMenuCategorization = (byte)KimahriBlueMagicRingSubMenu;

            string name = DecodeUs(command.NameScriptBytes, -1);
            command.DescriptionScriptBytes = EncodeUs(
                $"Blue Mage: cast {name} with MP instead of Overdrive.");
        }

        static void ApplyDemitaRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs(DemitaName);
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Cuts all enemies' HP by half.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();

            command.CharacterUser = Character_Enum.Kimahri;
            command.CostMp = 24;
            command.FlagTargetMulti = true;
            command.SubMenuCategorization = KimahriBlueMagicRingSubMenu;
            command.SubSubMenuCategorization = (byte)KimahriBlueMagicRingSubMenu;
        }

        static void ApplyLancetPlusRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs(LancetPlusName);
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Learn enemy abilities as usable commands.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();

            command.CharacterUser = Character_Enum.Kimahri;
            command.SubMenuCategorization = KimahriBlueMagicRingSubMenu;
            command.SubSubMenuCategorization = (byte)KimahriBlueMagicRingSubMenu;
        }

        static Ability_Command CloneCommand(Ability_Command donor) =>
            donor.CloneDeep(hasExtraInfo: true);

        static byte[] BuildNewRow(Ability_Command command, ref byte[] textPool)
        {
            Ability_CommandStruct row = new() { AbilityInfo = command };
            if (command.ExtraInfo == null)
                command.ExtraInfo = new Ability_Command.ExtraCommandInfo();

            AppendScript(ref textPool, row.NameTSInfo, command.NameScriptBytes, command.NameScriptId);
            AppendScript(ref textPool, row.UnusedText1TSInfo, command.JapaneseOnlyText1ScriptBytes, command.JapaneseOnlyText1ScriptId);
            AppendScript(ref textPool, row.DescriptionTSInfo, command.DescriptionScriptBytes, command.DescriptionScriptId);
            AppendScript(ref textPool, row.UnusedText2TSInfo, command.JapaneseOnlyText2ScriptBytes, command.JapaneseOnlyText2ScriptId);

            using MemoryStream stream = new();
            BinaryMapping.WriteObject(stream, row);
            BinaryMapping.WriteObject(stream, command.ExtraInfo);
            byte[] rowBytes = stream.ToArray();
            if (rowBytes.Length != CommandGrowWriter.CommandEntrySize)
            {
                throw new InvalidDataException(
                    $"New command row serialized to 0x{rowBytes.Length:X}; expected 0x{CommandGrowWriter.CommandEntrySize:X}.");
            }

            return rowBytes;
        }

        static void AppendScript(ref byte[] textPool, CommonStructs.TextScriptInfo info, byte[] scriptBytes, ushort scriptId)
        {
            if (textPool.Length > ushort.MaxValue)
                throw new InvalidDataException($"Text pool is too large for a 16-bit script offset: 0x{textPool.Length:X}.");

            info.Offset = (ushort)textPool.Length;
            info.ScriptId = scriptId;
            textPool = FfxEncoding.WriteBytesIntoTextFile(textPool, scriptBytes);
        }

        static byte[] EncodeUs(string text) => FfxEncoding.EncodeString(text, FfxEncoding.UsEncoder).ByteArray;

        static string DecodeUs(byte[] bytes, int fallbackId)
        {
            try
            {
                string decoded = FfxEncoding.DecodeScript(bytes).GetString(FfxEncoding.UsDecoder);
                if (!string.IsNullOrWhiteSpace(decoded))
                    return decoded;
            }
            catch { /* fall through */ }

            return fallbackId >= 0 && CommandCharacter_Dictionary.Instance.TryGetValue((ushort)fallbackId, out string? dictName)
                ? dictName
                : $"#{fallbackId}";
        }
    }
}

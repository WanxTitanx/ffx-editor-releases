using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.Utils.Encoding;
using Xe.BinaryMapper;
using static FFXProjectEditor.FfxLib.Ability.Ability_Structs;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Ability
{
    public sealed class CommandGrowResult
    {
        public required byte[] OriginalBytes { get; init; }
        public required byte[] GrownBytes { get; init; }
        public required int OriginalEntryCount { get; init; }
        public required int NewEntryCount { get; init; }
        public required int RadiantWardId { get; init; }
        public required int UmbralWardId { get; init; }
        public required string RadiantWardName { get; init; }
        public required string UmbralWardName { get; init; }
        public required int OriginalLength { get; init; }
        public required int GrownLength { get; init; }
        public required bool ExistingRecordPayloadPreserved { get; init; }
        public required bool ExistingTextPoolPrefixPreserved { get; init; }
        public required bool RereadCountOk { get; init; }
        public required bool PreserveWriteOk { get; init; }
        public bool Pass => ExistingRecordPayloadPreserved
            && ExistingTextPoolPrefixPreserved
            && RereadCountOk
            && PreserveWriteOk;
    }

    /// <summary>Grow <c>command.bin</c> (0x60 rows + ExtraCommandInfo) with Spira Reforge element wards.</summary>
    public static class CommandGrowWriter
    {
        public const int CommandEntrySize = 0x60;

        public const int NulTideDonorId = 49;
        public const int NulShockDonorId = 48;

        /// <summary>First grown row after vanilla 320-entry table (ids 0..319).</summary>
        public const int RadiantWardCommandId = 320;

        public const int UmbralWardCommandId = 321;

        // Animation (VFX): the cast visual is driven by Anim1Id/Anim2Id, which select
        // magicFiles\FFX\magic_%04d.dll (proven: anim 146 -> magic_0146).
        // CRASH NOTE (proved in-game 2026-06-16): the donor's NATIVE Nul anim (NulTide 142/143,
        // NulShock 140/141) CRASHES on these brand-new command ids (320/321). The Nul effect DLLs
        // DO exist on disk, so it is NOT a missing-file issue; vanilla NulTide (cmd 49) runs magic_0142
        // fine. The new command clears the Nul status-inflict (hook owns the block), and the Nul effect
        // appears to deref per-command/status data that is now absent -> null deref -> crash.
        // Until dedicated Nul-Holy / Nul-Dark effects exist (clone magic_0140/0142 + recolor) AND the
        // status-dependency is solved, we PIN a crash-safe placeholder: a damage-spell effect (no status
        // dependency) = magic_0146 (Holy flash). Temporary + intentional; swap when the real FX land.
        public const short PlaceholderWardAnim = 146;

        public const string RadiantWardName = "Radiant Ward";
        public const string UmbralWardName = "Umbral Ward";

        public static CommandGrowResult AppendElementWards(byte[] originalBytes, bool hookHandlesNulStatus = true)
        {
            ArgumentNullException.ThrowIfNull(originalBytes);

            EntryListFile original = EntryListFile.Unpack(originalBytes);
            if (original.Header.EntrySize != CommandEntrySize)
                throw new InvalidDataException($"Expected command.bin entry size 0x60, got 0x{original.Header.EntrySize:X}.");

            List<Ability_Command> entries = Ability_Command.ReadList(originalBytes, hasExtraInfo: true);
            if (entries.Count == 0)
                throw new InvalidDataException("Cannot grow an empty command table.");

            if (entries.Count != RadiantWardCommandId)
            {
                throw new InvalidDataException(
                    $"Expected {RadiantWardCommandId} vanilla rows before grow, got {entries.Count}. "
                    + "Patch offsets or bump RadiantWardCommandId.");
            }

            Ability_Command radiant = CloneCommand(entries[NulTideDonorId]);
            ApplyRadiantWardRecipe(radiant, hookHandlesNulStatus);

            Ability_Command umbral = CloneCommand(entries[NulShockDonorId]);
            ApplyUmbralWardRecipe(umbral, hookHandlesNulStatus);

            byte[] textPool = original.SecondFile.ToArray();
            byte[] grownRows = original.FirstFile.ToArray();
            grownRows = grownRows.Concat(BuildNewRow(radiant, ref textPool)).ToArray();
            grownRows = grownRows.Concat(BuildNewRow(umbral, ref textPool)).ToArray();

            byte[] grownBytes = EntryListFile.Pack(
                entrySize: CommandEntrySize,
                entryCount: checked((short)(entries.Count + 2)),
                firstFile: grownRows,
                secondFile: textPool);

            List<Ability_Command> reread = Ability_Command.ReadList(grownBytes, hasExtraInfo: true);

            bool existingRowsPreserved = grownRows.Length >= original.FirstFile.Length
                && original.FirstFile.AsSpan().SequenceEqual(grownRows.AsSpan(0, original.FirstFile.Length));
            bool existingTextPreserved = textPool.Length >= original.SecondFile.Length
                && original.SecondFile.AsSpan().SequenceEqual(textPool.AsSpan(0, original.SecondFile.Length));

            return new CommandGrowResult
            {
                OriginalBytes = originalBytes,
                GrownBytes = grownBytes,
                OriginalEntryCount = entries.Count,
                NewEntryCount = reread.Count,
                RadiantWardId = RadiantWardCommandId,
                UmbralWardId = UmbralWardCommandId,
                RadiantWardName = RadiantWardName,
                UmbralWardName = UmbralWardName,
                OriginalLength = originalBytes.Length,
                GrownLength = grownBytes.Length,
                ExistingRecordPayloadPreserved = existingRowsPreserved,
                ExistingTextPoolPrefixPreserved = existingTextPreserved,
                RereadCountOk = reread.Count == entries.Count + 2,
                PreserveWriteOk = Ability_Command.WriteList(reread, hasExtraInfo: true).SequenceEqual(grownBytes),
            };
        }

        static void ApplyRadiantWardRecipe(Ability_Command command, bool hookHandlesNulStatus)
        {
            command.NameScriptBytes = EncodeUs(RadiantWardName);
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs(Strings.F2_nullifies_one_holy_attack_on_a_party_mem_47f7ea6b);
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            // TEMP crash-safe placeholder VFX (see PlaceholderWardAnim). Real Nul-Holy effect TODO offline.
            command.Anim1Id = PlaceholderWardAnim;
            command.Anim2Id = PlaceholderWardAnim;
            command.ElementFlgs = Ability_Command.ElementFlags.Holy;
            ApplyNulWardGameplay(command, wardStatusIndex: 18, hookHandlesNulStatus);
        }

        static void ApplyUmbralWardRecipe(Ability_Command command, bool hookHandlesNulStatus)
        {
            command.NameScriptBytes = EncodeUs(UmbralWardName);
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs(Strings.F2_nullifies_one_dark_attack_on_a_party_mem_3daed252);
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            // TEMP crash-safe placeholder VFX (see PlaceholderWardAnim). Real Nul-Dark effect TODO offline.
            command.Anim1Id = PlaceholderWardAnim;
            command.Anim2Id = PlaceholderWardAnim;
            command.ElementFlgs = Ability_Command.ElementFlags.Dark;
            ApplyNulWardGameplay(command, wardStatusIndex: 20, hookHandlesNulStatus);
        }

        /// <summary>
        /// Clone of vanilla Nul* white magic (MAG vs MDF, rank 2, 2 MP, ally, white submenu).
        /// When <paramref name="hookHandlesNulStatus"/> is true, status inflict stays cleared — NulWardHook owns blocks.
        /// </summary>
        static void ApplyNulWardGameplay(Ability_Command command, int wardStatusIndex, bool hookHandlesNulStatus)
        {
            command.DamageFormula = DamageFormula_Enum.Magic;
            command.AttackPower = 0;
            command.HitCount = 1;
            command.CostMp = 2;
            command.MoveRank = 2;
            command.DamageTypeFlgs = 0;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc3Piercing = true;
            command.FlagMisc4ShowSpellcastAura = true;
            command.SubMenuCategorization = 2;

            // Caster scope: hard-lock to Yuna at the DATA level (command.bin "Character (User)" field).
            // This makes the wards Yuna-only WITHOUT a DLL grant-scope hook — the teach-hook only needs to
            // SURFACE the command; the engine filters the usable caster by CharacterUser. (Verify in-game that
            // the party-wide command path honors this; if it does not, also scope the teach-grant to ch=1.)
            command.CharacterUser = Character_Enum.Yuna;

            // Targeting: protect the active PARTY (allies), NEVER enemies — a warded enemy would become immune
            // to your own Holy/Dark. Enabled + Multi = whole ally party (the 3); Enemies + EitherTeam stay OFF so
            // the cursor cannot cross to the enemy side. NOTE: the "block lands on 31 actors" report is the HOOK
            // looping every actor on apply (fixed separately in NulWardHook); these flags only constrain the cast
            // cursor. TargetsAllowed is left as inherited from the donor Nul* spell.
            command.TargetFlgs = Ability_Command.TargetFlags.Enabled | Ability_Command.TargetFlags.Multi;

            ClearStatusInflict(command);
            if (!hookHandlesNulStatus)
                SetInfiniteOneBlock(command, wardStatusIndex);
        }

        static void ClearStatusInflict(Ability_Command command)
        {
            command.StatusChance = new StatusByteList();
            command.StatusDuration = new StatusDurationByteList();
            command.StatusFlgs = 0;
        }

        static void SetInfiniteOneBlock(Ability_Command command, int chanceIndex)
        {
            byte infinite = 254;
            byte oneBlock = 1;

            switch (chanceIndex)
            {
                case 18: command.StatusChance.NulTide = infinite; command.StatusDuration.NulTide = oneBlock; break;
                case 19: command.StatusChance.NulBlaze = infinite; command.StatusDuration.NulBlaze = oneBlock; break;
                case 20: command.StatusChance.NulShock = infinite; command.StatusDuration.NulShock = oneBlock; break;
                case 21: command.StatusChance.NulFrost = infinite; command.StatusDuration.NulFrost = oneBlock; break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(chanceIndex), chanceIndex, "Unsupported nul status slot.");
            }
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
            if (rowBytes.Length != CommandEntrySize)
                throw new InvalidDataException($"New command row serialized to 0x{rowBytes.Length:X}; expected 0x{CommandEntrySize:X}.");

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
    }
}

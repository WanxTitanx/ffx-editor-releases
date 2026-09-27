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
    public sealed class MonsterMagicGrowResult
    {
        public required byte[] OriginalBytes { get; init; }
        public required byte[] GrownBytes { get; init; }
        public required int OriginalEntryCount { get; init; }
        public required int NewEntryCount { get; init; }
        public required int DonorId { get; init; }
        public required int NewId { get; init; }
        public required ushort Operand { get; init; }
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required int OriginalLength { get; init; }
        public required int GrownLength { get; init; }
        public required bool ExistingRecordPayloadPreserved { get; init; }
        public required bool ExistingTextPoolPrefixPreserved { get; init; }
        public required bool RereadCountOk { get; init; }
        public required bool RereadNewTextOk { get; init; }
        public required bool PreserveWriteOk { get; init; }
        public bool Pass => ExistingRecordPayloadPreserved
            && ExistingTextPoolPrefixPreserved
            && RereadCountOk
            && RereadNewTextOk
            && PreserveWriteOk;
    }

    public static partial class MonsterMagicGrowWriter
    {
        public const int MonMagicEntrySize = 0x5C;

        public static MonsterMagicGrowResult AppendPrismFlare(byte[] originalBytes, bool monsterMagic2 = true, int donorId = 171)
        {
            ArgumentNullException.ThrowIfNull(originalBytes);

            EntryListFile original = EntryListFile.Unpack(originalBytes);
            if (original.Header.EntrySize != MonMagicEntrySize)
                throw new InvalidDataException($"Expected a monmagic table with entry size 0x5C, got 0x{original.Header.EntrySize:X}.");

            var entries = Ability_Command.ReadList(originalBytes, hasExtraInfo: false);
            if (entries.Count == 0)
                throw new InvalidDataException("Cannot grow an empty monmagic table.");

            // Dedup: search for existing "Prism Flare" entry
            int existing = entries.FindIndex(c =>
                DecodeUs(c.NameScriptBytes).Contains("Prism Flare", StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                ushort dedupOperandBase = monsterMagic2 ? (ushort)0x6000 : (ushort)0x4000;
                return new MonsterMagicGrowResult
                {
                    OriginalBytes = originalBytes,
                    GrownBytes = originalBytes,
                    OriginalEntryCount = entries.Count,
                    NewEntryCount = entries.Count,
                    DonorId = donorId,
                    NewId = existing,
                    Operand = (ushort)(dedupOperandBase | (existing & 0x0FFF)),
                    Name = DecodeUs(entries[existing].NameScriptBytes),
                    Description = DecodeUs(entries[existing].DescriptionScriptBytes),
                    OriginalLength = originalBytes.Length,
                    GrownLength = originalBytes.Length,
                    ExistingRecordPayloadPreserved = true,
                    ExistingTextPoolPrefixPreserved = true,
                    RereadCountOk = true,
                    RereadNewTextOk = true,
                    PreserveWriteOk = true
                };
            }

            if (donorId < 0 || donorId >= entries.Count)
                donorId = entries.Count - 1;

            int newId = entries.Count;
            ushort operandBase = monsterMagic2 ? (ushort)0x6000 : (ushort)0x4000;
            ushort operand = (ushort)(operandBase | (newId & 0x0FFF));

            Ability_Command donor = entries[donorId];
            Ability_Command newCommand = CloneCommand(donor);
            ApplyPrismFlareRecipe(newCommand);

            byte[] newRow = BuildNewRow(newCommand, original.SecondFile, out byte[] grownText);
            byte[] grownRows = original.FirstFile.Concat(newRow).ToArray();
            byte[] grownBytes = EntryListFile.Pack(
                entrySize: MonMagicEntrySize,
                entryCount: checked((short)(entries.Count + 1)),
                firstFile: grownRows,
                secondFile: grownText);

            EntryListFile grown = EntryListFile.Unpack(grownBytes);
            var reread = Ability_Command.ReadList(grownBytes, hasExtraInfo: false);
            Ability_Command rereadNew = reread[newId];

            bool existingRowsPreserved = grown.FirstFile.Length >= original.FirstFile.Length
                && original.FirstFile.AsSpan().SequenceEqual(grown.FirstFile.AsSpan(0, original.FirstFile.Length));
            bool existingTextPreserved = grown.SecondFile.Length >= original.SecondFile.Length
                && original.SecondFile.AsSpan().SequenceEqual(grown.SecondFile.AsSpan(0, original.SecondFile.Length));
            bool rereadCountOk = reread.Count == entries.Count + 1;
            bool rereadNewTextOk = rereadNew.NameScriptBytes.SequenceEqual(newCommand.NameScriptBytes)
                && rereadNew.DescriptionScriptBytes.SequenceEqual(newCommand.DescriptionScriptBytes);
            bool preserveWriteOk = Ability_Command.WriteList(reread, hasExtraInfo: false).SequenceEqual(grownBytes);

            return new MonsterMagicGrowResult
            {
                OriginalBytes = originalBytes,
                GrownBytes = grownBytes,
                OriginalEntryCount = entries.Count,
                NewEntryCount = reread.Count,
                DonorId = donorId,
                NewId = newId,
                Operand = operand,
                Name = DecodeUs(newCommand.NameScriptBytes),
                Description = DecodeUs(newCommand.DescriptionScriptBytes),
                OriginalLength = originalBytes.Length,
                GrownLength = grownBytes.Length,
                ExistingRecordPayloadPreserved = existingRowsPreserved,
                ExistingTextPoolPrefixPreserved = existingTextPreserved,
                RereadCountOk = rereadCountOk,
                RereadNewTextOk = rereadNewTextOk,
                PreserveWriteOk = preserveWriteOk
            };
        }

        public static MonsterMagicGrowResult AppendThundaFira(byte[] originalBytes, bool monsterMagic2 = true, int donorId = 171)
        {
            ArgumentNullException.ThrowIfNull(originalBytes);

            EntryListFile original = EntryListFile.Unpack(originalBytes);
            if (original.Header.EntrySize != MonMagicEntrySize)
                throw new InvalidDataException($"Expected a monmagic table with entry size 0x5C, got 0x{original.Header.EntrySize:X}.");

            var entries = Ability_Command.ReadList(originalBytes, hasExtraInfo: false);
            if (entries.Count == 0)
                throw new InvalidDataException("Cannot grow an empty monmagic table.");

            int existing = entries.FindIndex(c =>
                DecodeUs(c.NameScriptBytes).Contains("ThundaFira", StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                ushort operandBase = monsterMagic2 ? (ushort)0x6000 : (ushort)0x4000;
                return new MonsterMagicGrowResult
                {
                    OriginalBytes = originalBytes,
                    GrownBytes = originalBytes,
                    OriginalEntryCount = entries.Count,
                    NewEntryCount = entries.Count,
                    DonorId = donorId,
                    NewId = existing,
                    Operand = (ushort)(operandBase | (existing & 0x0FFF)),
                    Name = DecodeUs(entries[existing].NameScriptBytes),
                    Description = DecodeUs(entries[existing].DescriptionScriptBytes),
                    OriginalLength = originalBytes.Length,
                    GrownLength = originalBytes.Length,
                    ExistingRecordPayloadPreserved = true,
                    ExistingTextPoolPrefixPreserved = true,
                    RereadCountOk = true,
                    RereadNewTextOk = true,
                    PreserveWriteOk = true
                };
            }

            if (donorId < 0 || donorId >= entries.Count)
                donorId = entries.Count - 1;

            int newId = entries.Count;
            ushort operandBaseGrow = monsterMagic2 ? (ushort)0x6000 : (ushort)0x4000;
            ushort operand = (ushort)(operandBaseGrow | (newId & 0x0FFF));

            Ability_Command donor = entries[donorId];
            Ability_Command newCommand = CloneCommand(donor);
            ApplyThundaFiraRecipe(newCommand, useSplitPhaseExplosion: false);

            byte[] newRow = BuildNewRow(newCommand, original.SecondFile, out byte[] grownText);
            byte[] grownRows = original.FirstFile.Concat(newRow).ToArray();
            byte[] grownBytes = EntryListFile.Pack(
                entrySize: MonMagicEntrySize,
                entryCount: checked((short)(entries.Count + 1)),
                firstFile: grownRows,
                secondFile: grownText);

            EntryListFile grown = EntryListFile.Unpack(grownBytes);
            var reread = Ability_Command.ReadList(grownBytes, hasExtraInfo: false);
            Ability_Command rereadNew = reread[newId];

            bool existingRowsPreserved = grown.FirstFile.Length >= original.FirstFile.Length
                && original.FirstFile.AsSpan().SequenceEqual(grown.FirstFile.AsSpan(0, original.FirstFile.Length));
            bool existingTextPreserved = grown.SecondFile.Length >= original.SecondFile.Length
                && original.SecondFile.AsSpan().SequenceEqual(grown.SecondFile.AsSpan(0, original.SecondFile.Length));
            bool rereadCountOk = reread.Count == entries.Count + 1;
            bool rereadNewTextOk = rereadNew.NameScriptBytes.SequenceEqual(newCommand.NameScriptBytes)
                && rereadNew.DescriptionScriptBytes.SequenceEqual(newCommand.DescriptionScriptBytes);
            bool preserveWriteOk = Ability_Command.WriteList(reread, hasExtraInfo: false).SequenceEqual(grownBytes);

            return new MonsterMagicGrowResult
            {
                OriginalBytes = originalBytes,
                GrownBytes = grownBytes,
                OriginalEntryCount = entries.Count,
                NewEntryCount = reread.Count,
                DonorId = donorId,
                NewId = newId,
                Operand = operand,
                Name = DecodeUs(newCommand.NameScriptBytes),
                Description = DecodeUs(newCommand.DescriptionScriptBytes),
                OriginalLength = originalBytes.Length,
                GrownLength = grownBytes.Length,
                ExistingRecordPayloadPreserved = existingRowsPreserved,
                ExistingTextPoolPrefixPreserved = existingTextPreserved,
                RereadCountOk = rereadCountOk,
                RereadNewTextOk = rereadNewTextOk,
                PreserveWriteOk = preserveWriteOk
            };
        }

        static Ability_Command CloneCommand(Ability_Command donor)
        {
            byte[] single = donor.WriteSingle(hasExtraInfo: false);
            Ability_Command clone = Ability_Command.ReadSingle(single, hasExtraInfo: false);
            clone.OriginalNameOffset = null;
            clone.OriginalJapaneseOnlyText1Offset = null;
            clone.OriginalDescriptionOffset = null;
            clone.OriginalJapaneseOnlyText2Offset = null;
            return clone;
        }

        public const int PrismFlareCommandId = 247;

        public const int ThundaFiraCommandId = 248;

        /// <summary>Thundaga cast lightning (character spell visual donor).</summary>
        public const short ThundaFiraVanillaThunderAnimId = 94;
        /// <summary>Vanilla Thundaga Anim2 / ground burst — <c>magic_0095</c> (not Firaga).</summary>
        public const short ThundaFiraVanillaThundagaExplosionAnimId = 95;

        /// <summary>Legacy Multi-Fira explosion donor (use <see cref="ThundaFiraVanillaFiragaAnimId"/> by default).</summary>
        public const short ThundaFiraVanillaFireAnimId = 82;

        /// <summary>Firaga-scale explosion donor — default ThundaFira phase 2 (`0090` → clone `717`).</summary>
        public const short ThundaFiraVanillaFiragaAnimId = 90;

        /// <summary>Dedicated clones: Thundaga phase + Multi-Fira/Firaga phase.</summary>
        public const short ThundaFiraCloneThunderAnimId = 716;
        public const short ThundaFiraCloneFireAnimId = 717;

        /// <summary>Which vanilla magic id seeds clone <see cref="ThundaFiraCloneFireAnimId"/> when split phase is on.</summary>
        public enum ThundaFiraExplosionDonor
        {
            ThundagaPhase2 = ThundaFiraVanillaThundagaExplosionAnimId,
            Firaga = ThundaFiraVanillaFiragaAnimId,
            MultiFira = ThundaFiraVanillaFireAnimId,
        }

        public static short ResolveThundaFiraExplosionSourceId(ThundaFiraExplosionDonor donor) => (short)donor;

        /// <summary>Vanilla Multi-Fira donor visuals — guaranteed in-game parity (#171 uses 82/82).</summary>
        public const short PrismFlareVanillaAnim1Id = 82;
        public const short PrismFlareVanillaAnim2Id = 82;

        /// <summary>Dedicated clones: cast phase (82→714) + fire pillar phase (86→715).</summary>
        public const short PrismFlareCloneAnim1Id = 714;
        public const short PrismFlareCloneAnim2Id = 715;

        /// <summary>
        /// Visual recipe: Anim1 cast (magic_0714) + Anim2 fire pillar (magic_0715).
        /// Monster Multi-Fira uses 82/82; Prism uses split clones so phase-2 fire textures load from 715.
        /// </summary>
        public static void ApplyPrismFlareVisualIds(Ability_Command command, bool useDedicatedClones = false)
        {
            if (useDedicatedClones)
            {
                command.Anim1Id = PrismFlareCloneAnim1Id;
                command.Anim2Id = PrismFlareCloneAnim2Id;
            }
            else
            {
                command.Anim1Id = PrismFlareVanillaAnim1Id;
                command.Anim2Id = PrismFlareVanillaAnim2Id;
            }
        }

        /// <summary>Patches an existing Prism Flare row (default id 247) in a grown monmagic2 table.</summary>
        public static byte[] PatchPrismFlareV2(byte[] originalBytes, int commandId = PrismFlareCommandId, bool hasExtraInfo = false, bool useDedicatedClones = false)
        {
            ArgumentNullException.ThrowIfNull(originalBytes);
            List<Ability_Command> entries = Ability_Command.ReadList(originalBytes, hasExtraInfo);
            if (commandId < 0 || commandId >= entries.Count)
                throw new ArgumentOutOfRangeException(nameof(commandId), $"Command id {commandId} outside 0..{entries.Count - 1}.");

            Ability_Command target = entries[commandId];
            string name = DecodeUs(target.NameScriptBytes);
            if (!name.Contains("Prism", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Row {commandId} is '{name}', expected Prism Flare.");

            ApplyPrismFlareGameplayOnly(target);
            ApplyPrismFlareVisualIds(target, useDedicatedClones);
            byte[] result = Ability_Command.WriteList(entries, hasExtraInfo);
            if (result.Length != originalBytes.Length)
            {
                throw new InvalidDataException(
                    $"monmagic2 size drifted {originalBytes.Length} -> {result.Length} bytes; "
                    + "text pool was rebuilt — aborting to avoid kernel softlock.");
            }

            return result;
        }

        public static byte[] PatchThundaFira(
            byte[] bytes,
            int commandId,
            bool hasExtraInfo = false,
            bool useDedicatedClones = true,
            bool useSplitPhaseExplosion = false,
            ThundaFiraExplosionDonor explosionDonor = ThundaFiraExplosionDonor.ThundagaPhase2)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            List<Ability_Command> entries = Ability_Command.ReadList(bytes, hasExtraInfo);
            if (commandId < 0 || commandId >= entries.Count)
                throw new ArgumentOutOfRangeException(nameof(commandId), $"Command id {commandId} outside 0..{entries.Count - 1}.");

            Ability_Command target = entries[commandId];
            string name = DecodeUs(target.NameScriptBytes);
            if (!name.Contains("ThundaFira", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Row {commandId} is '{name}', expected ThundaFira.");

            ApplyThundaFiraGameplayOnly(target);
            ApplyThundaFiraVisualIds(target, useDedicatedClones, useSplitPhaseExplosion, explosionDonor);
            return Ability_Command.WriteList(entries, hasExtraInfo);
        }

        /// <summary>
        /// LAB visuals. Default: pure Thundaga clone <c>716/716</c> (both phases = Anim1 bolts + Anim2 burst in one asset).
        /// Split <c>716/717</c>: Anim1=<c>0094</c> clone, Anim2=<c>0095</c> clone by default (vanilla Thundaga <c>94/95</c>).
        /// </summary>
        public static void ApplyThundaFiraVisualIds(
            Ability_Command command,
            bool useDedicatedClones,
            bool useSplitPhaseExplosion = false,
            ThundaFiraExplosionDonor explosionDonor = ThundaFiraExplosionDonor.ThundagaPhase2)
        {
            if (useDedicatedClones)
            {
                command.Anim1Id = ThundaFiraCloneThunderAnimId;
                command.Anim2Id = useSplitPhaseExplosion
                    ? ThundaFiraCloneFireAnimId
                    : ThundaFiraCloneThunderAnimId;
            }
            else
            {
                command.Anim1Id = ThundaFiraVanillaThunderAnimId;
                command.Anim2Id = useSplitPhaseExplosion
                    ? ResolveThundaFiraExplosionSourceId(explosionDonor)
                    : ThundaFiraVanillaThunderAnimId;
            }
        }

        static void ApplyThundaFiraGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.Magic;
            command.AttackPower = 38;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = Ability_Command.ElementFlags.Fire | Ability_Command.ElementFlags.Thunder;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
            command.StatusChance.Slow = 0;
            command.StatusDuration.Slow = 0;
        }

        static void ApplyThundaFiraRecipe(Ability_Command command, bool useSplitPhaseExplosion = false, ThundaFiraExplosionDonor explosionDonor = ThundaFiraExplosionDonor.ThundagaPhase2)
        {
            command.NameScriptBytes = EncodeUs("ThundaFira");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs(Strings.F2_lightning_converges_on_the_foe_then_erup_bd1ef873);
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyThundaFiraGameplayOnly(command);
            ApplyThundaFiraVisualIds(command, useDedicatedClones: true, useSplitPhaseExplosion, explosionDonor);
        }

        /// <summary>Numeric/flags only — never touches name/description bytes (preserves text pool layout).</summary>
        static void ApplyPrismFlareGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.Magic;
            command.AttackPower = 42;
            command.HitCount = 3;
            command.CostMp = 0;
            command.ElementFlgs = Ability_Command.ElementFlags.Fire
                | Ability_Command.ElementFlags.Thunder;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
            command.StatusChance.Slow = 65;
            command.StatusDuration.Slow = 4;
        }

        static void ApplyPrismFlareRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Prism Flare");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("New monster spell: 3-hit Fire/Thunder magic with Slow.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyPrismFlareGameplayOnly(command);
        }

        public const int FlameFlanFloodCommandId = 249;

        /// <summary>Vanilla Waterga tier-3 visuals — character spell donors.</summary>
        public const short FlameFlanVanillaWatergaAnim1Id = 96;
        public const short FlameFlanVanillaWatergaAnim2Id = 97;

        /// <summary>Dedicated clones: Waterga cast (`96`→`718`) + burst (`97`→`719`). No FFX.exe patch.</summary>
        public const short FlameFlanCloneWatergaAnim1Id = 718;
        public const short FlameFlanCloneWatergaAnim2Id = 719;

        public static MonsterMagicGrowResult AppendFlameFlanFlood(byte[] originalBytes, bool monsterMagic2 = true, int donorId = 171)
        {
            ArgumentNullException.ThrowIfNull(originalBytes);

            EntryListFile original = EntryListFile.Unpack(originalBytes);
            if (original.Header.EntrySize != MonMagicEntrySize)
                throw new InvalidDataException($"Expected a monmagic table with entry size 0x5C, got 0x{original.Header.EntrySize:X}.");

            var entries = Ability_Command.ReadList(originalBytes, hasExtraInfo: false);
            if (entries.Count == 0)
                throw new InvalidDataException("Cannot grow an empty monmagic table.");

            int existing = entries.FindIndex(c =>
                DecodeUs(c.NameScriptBytes).Contains("Flan Flood", StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                ushort operandBase = monsterMagic2 ? (ushort)0x6000 : (ushort)0x4000;
                return new MonsterMagicGrowResult
                {
                    OriginalBytes = originalBytes,
                    GrownBytes = originalBytes,
                    OriginalEntryCount = entries.Count,
                    NewEntryCount = entries.Count,
                    DonorId = donorId,
                    NewId = existing,
                    Operand = (ushort)(operandBase | (existing & 0x0FFF)),
                    Name = DecodeUs(entries[existing].NameScriptBytes),
                    Description = DecodeUs(entries[existing].DescriptionScriptBytes),
                    OriginalLength = originalBytes.Length,
                    GrownLength = originalBytes.Length,
                    ExistingRecordPayloadPreserved = true,
                    ExistingTextPoolPrefixPreserved = true,
                    RereadCountOk = true,
                    RereadNewTextOk = true,
                    PreserveWriteOk = true
                };
            }

            if (donorId < 0 || donorId >= entries.Count)
                donorId = entries.Count - 1;

            int newId = entries.Count;
            ushort operandBaseGrow = monsterMagic2 ? (ushort)0x6000 : (ushort)0x4000;
            ushort operand = (ushort)(operandBaseGrow | (newId & 0x0FFF));

            Ability_Command donor = entries[donorId];
            Ability_Command newCommand = CloneCommand(donor);
            ApplyFlameFlanFloodRecipe(newCommand);

            byte[] newRow = BuildNewRow(newCommand, original.SecondFile, out byte[] grownText);
            byte[] grownRows = original.FirstFile.Concat(newRow).ToArray();
            byte[] grownBytes = EntryListFile.Pack(
                entrySize: MonMagicEntrySize,
                entryCount: checked((short)(entries.Count + 1)),
                firstFile: grownRows,
                secondFile: grownText);

            EntryListFile grown = EntryListFile.Unpack(grownBytes);
            var reread = Ability_Command.ReadList(grownBytes, hasExtraInfo: false);
            Ability_Command rereadNew = reread[newId];

            bool existingRowsPreserved = grown.FirstFile.Length >= original.FirstFile.Length
                && original.FirstFile.AsSpan().SequenceEqual(grown.FirstFile.AsSpan(0, original.FirstFile.Length));
            bool existingTextPreserved = grown.SecondFile.Length >= original.SecondFile.Length
                && original.SecondFile.AsSpan().SequenceEqual(grown.SecondFile.AsSpan(0, original.SecondFile.Length));
            bool rereadCountOk = reread.Count == entries.Count + 1;
            bool rereadNewTextOk = rereadNew.NameScriptBytes.SequenceEqual(newCommand.NameScriptBytes)
                && rereadNew.DescriptionScriptBytes.SequenceEqual(newCommand.DescriptionScriptBytes);
            bool preserveWriteOk = Ability_Command.WriteList(reread, hasExtraInfo: false).SequenceEqual(grownBytes);

            return new MonsterMagicGrowResult
            {
                OriginalBytes = originalBytes,
                GrownBytes = grownBytes,
                OriginalEntryCount = entries.Count,
                NewEntryCount = reread.Count,
                DonorId = donorId,
                NewId = newId,
                Operand = operand,
                Name = DecodeUs(newCommand.NameScriptBytes),
                Description = DecodeUs(newCommand.DescriptionScriptBytes),
                OriginalLength = originalBytes.Length,
                GrownLength = grownBytes.Length,
                ExistingRecordPayloadPreserved = existingRowsPreserved,
                ExistingTextPoolPrefixPreserved = existingTextPreserved,
                RereadCountOk = rereadCountOk,
                RereadNewTextOk = rereadNewTextOk,
                PreserveWriteOk = preserveWriteOk
            };
        }

        public static byte[] PatchFlameFlanFlood(byte[] bytes, int commandId, bool hasExtraInfo = false, bool useDedicatedClones = true)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            List<Ability_Command> entries = Ability_Command.ReadList(bytes, hasExtraInfo);
            if (commandId < 0 || commandId >= entries.Count)
                throw new ArgumentOutOfRangeException(nameof(commandId), $"Command id {commandId} outside 0..{entries.Count - 1}.");

            Ability_Command target = entries[commandId];
            string name = DecodeUs(target.NameScriptBytes);
            if (!name.Contains("Flan Flood", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Row {commandId} is '{name}', expected Flan Flood.");

            ApplyFlameFlanFloodGameplayOnly(target);
            ApplyFlameFlanFloodVisualIds(target, useDedicatedClones);
            return Ability_Command.WriteList(entries, hasExtraInfo);
        }

        public static MonsterMagicGrowResult AppendGoreCharge(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
        {
            ArgumentNullException.ThrowIfNull(originalBytes);
            EntryListFile original = EntryListFile.Unpack(originalBytes);
            if (original.Header.EntrySize != MonMagicEntrySize)
                throw new InvalidDataException($"Expected a monmagic table with entry size 0x5C, got 0x{original.Header.EntrySize:X}.");
            var entries = Ability_Command.ReadList(originalBytes, hasExtraInfo: false);
            if (entries.Count == 0)
                throw new InvalidDataException("Cannot grow an empty monmagic table.");
            if (donorId < 0 || donorId >= entries.Count) donorId = 171;
            int newId = entries.Count;
            ushort operandBase = monsterMagic2 ? (ushort)0x6000 : (ushort)0x4000;
            ushort operand = (ushort)(operandBase | (newId & 0x0FFF));
            Ability_Command donor = entries[donorId];
            Ability_Command newCommand = CloneCommand(donor);
            ApplyGoreChargeRecipe(newCommand);
            byte[] newRow = BuildNewRow(newCommand, original.SecondFile, out byte[] grownText);
            byte[] grownRows = original.FirstFile.Concat(newRow).ToArray();
            byte[] grownBytes = EntryListFile.Pack(entrySize: MonMagicEntrySize, entryCount: checked((short)(entries.Count + 1)), firstFile: grownRows, secondFile: grownText);
            EntryListFile grown = EntryListFile.Unpack(grownBytes);
            var reread = Ability_Command.ReadList(grownBytes, hasExtraInfo: false);
            Ability_Command rereadNew = reread[newId];
            bool existingRowsPreserved = grown.FirstFile.Length >= original.FirstFile.Length && original.FirstFile.AsSpan().SequenceEqual(grown.FirstFile.AsSpan(0, original.FirstFile.Length));
            bool existingTextPreserved = grown.SecondFile.Length >= original.SecondFile.Length && original.SecondFile.AsSpan().SequenceEqual(grown.SecondFile.AsSpan(0, original.SecondFile.Length));
            bool rereadCountOk = reread.Count == entries.Count + 1;
            bool rereadNewTextOk = rereadNew.NameScriptBytes.SequenceEqual(newCommand.NameScriptBytes) && rereadNew.DescriptionScriptBytes.SequenceEqual(newCommand.DescriptionScriptBytes);
            bool preserveWriteOk = Ability_Command.WriteList(reread, hasExtraInfo: false).SequenceEqual(grownBytes);
            return new MonsterMagicGrowResult { OriginalBytes = originalBytes, GrownBytes = grownBytes, OriginalEntryCount = entries.Count, NewEntryCount = reread.Count, DonorId = donorId, NewId = newId, Operand = operand, Name = DecodeUs(newCommand.NameScriptBytes), Description = DecodeUs(newCommand.DescriptionScriptBytes), OriginalLength = originalBytes.Length, GrownLength = grownBytes.Length, ExistingRecordPayloadPreserved = existingRowsPreserved, ExistingTextPoolPrefixPreserved = existingTextPreserved, RereadCountOk = rereadCountOk, RereadNewTextOk = rereadNewTextOk, PreserveWriteOk = preserveWriteOk };
        }

        public static MonsterMagicGrowResult AppendFangStrike(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
        {
            ArgumentNullException.ThrowIfNull(originalBytes);
            EntryListFile original = EntryListFile.Unpack(originalBytes);
            if (original.Header.EntrySize != MonMagicEntrySize)
                throw new InvalidDataException($"Expected a monmagic table with entry size 0x5C, got 0x{original.Header.EntrySize:X}.");
            var entries = Ability_Command.ReadList(originalBytes, hasExtraInfo: false);
            if (entries.Count == 0)
                throw new InvalidDataException("Cannot grow an empty monmagic table.");
            if (donorId < 0 || donorId >= entries.Count) donorId = 171;
            int newId = entries.Count;
            ushort operandBase = monsterMagic2 ? (ushort)0x6000 : (ushort)0x4000;
            ushort operand = (ushort)(operandBase | (newId & 0x0FFF));
            Ability_Command donor = entries[donorId];
            Ability_Command newCommand = CloneCommand(donor);
            ApplyFangStrikeRecipe(newCommand);
            byte[] newRow = BuildNewRow(newCommand, original.SecondFile, out byte[] grownText);
            byte[] grownRows = original.FirstFile.Concat(newRow).ToArray();
            byte[] grownBytes = EntryListFile.Pack(entrySize: MonMagicEntrySize, entryCount: checked((short)(entries.Count + 1)), firstFile: grownRows, secondFile: grownText);
            EntryListFile grown = EntryListFile.Unpack(grownBytes);
            var reread = Ability_Command.ReadList(grownBytes, hasExtraInfo: false);
            Ability_Command rereadNew = reread[newId];
            bool existingRowsPreserved = grown.FirstFile.Length >= original.FirstFile.Length && original.FirstFile.AsSpan().SequenceEqual(grown.FirstFile.AsSpan(0, original.FirstFile.Length));
            bool existingTextPreserved = grown.SecondFile.Length >= original.SecondFile.Length && original.SecondFile.AsSpan().SequenceEqual(grown.SecondFile.AsSpan(0, original.SecondFile.Length));
            bool rereadCountOk = reread.Count == entries.Count + 1;
            bool rereadNewTextOk = rereadNew.NameScriptBytes.SequenceEqual(newCommand.NameScriptBytes) && rereadNew.DescriptionScriptBytes.SequenceEqual(newCommand.DescriptionScriptBytes);
            bool preserveWriteOk = Ability_Command.WriteList(reread, hasExtraInfo: false).SequenceEqual(grownBytes);
            return new MonsterMagicGrowResult { OriginalBytes = originalBytes, GrownBytes = grownBytes, OriginalEntryCount = entries.Count, NewEntryCount = reread.Count, DonorId = donorId, NewId = newId, Operand = operand, Name = DecodeUs(newCommand.NameScriptBytes), Description = DecodeUs(newCommand.DescriptionScriptBytes), OriginalLength = originalBytes.Length, GrownLength = grownBytes.Length, ExistingRecordPayloadPreserved = existingRowsPreserved, ExistingTextPoolPrefixPreserved = existingTextPreserved, RereadCountOk = rereadCountOk, RereadNewTextOk = rereadNewTextOk, PreserveWriteOk = preserveWriteOk };
        }

        static MonsterMagicGrowResult AppendMonsterSkill(byte[] originalBytes, bool monsterMagic2, int donorId, Action<Ability_Command> recipe)
        {
            ArgumentNullException.ThrowIfNull(originalBytes);
            EntryListFile original = EntryListFile.Unpack(originalBytes);
            if (original.Header.EntrySize != MonMagicEntrySize)
                throw new InvalidDataException($"Expected a monmagic table with entry size 0x5C, got 0x{original.Header.EntrySize:X}.");
            var entries = Ability_Command.ReadList(originalBytes, hasExtraInfo: false);
            if (entries.Count == 0)
                throw new InvalidDataException("Cannot grow an empty monmagic table.");
            if (donorId < 0 || donorId >= entries.Count) donorId = 171;
            int newId = entries.Count;
            ushort operandBase = monsterMagic2 ? (ushort)0x6000 : (ushort)0x4000;
            ushort operand = (ushort)(operandBase | (newId & 0x0FFF));
            Ability_Command donor = entries[donorId];
            Ability_Command newCommand = CloneCommand(donor);
            recipe(newCommand);
            ApplyLegacyMonsterVfxIfConfigured(newCommand, newId);
            byte[] newRow = BuildNewRow(newCommand, original.SecondFile, out byte[] grownText);
            byte[] grownRows = original.FirstFile.Concat(newRow).ToArray();
            byte[] grownBytes = EntryListFile.Pack(entrySize: MonMagicEntrySize, entryCount: checked((short)(entries.Count + 1)), firstFile: grownRows, secondFile: grownText);
            EntryListFile grown = EntryListFile.Unpack(grownBytes);
            var reread = Ability_Command.ReadList(grownBytes, hasExtraInfo: false);
            Ability_Command rereadNew = reread[newId];
            bool existingRowsPreserved = grown.FirstFile.Length >= original.FirstFile.Length && original.FirstFile.AsSpan().SequenceEqual(grown.FirstFile.AsSpan(0, original.FirstFile.Length));
            bool existingTextPreserved = grown.SecondFile.Length >= original.SecondFile.Length && original.SecondFile.AsSpan().SequenceEqual(grown.SecondFile.AsSpan(0, original.SecondFile.Length));
            bool rereadCountOk = reread.Count == entries.Count + 1;
            bool rereadNewTextOk = rereadNew.NameScriptBytes.SequenceEqual(newCommand.NameScriptBytes) && rereadNew.DescriptionScriptBytes.SequenceEqual(newCommand.DescriptionScriptBytes);
            bool preserveWriteOk = Ability_Command.WriteList(reread, hasExtraInfo: false).SequenceEqual(grownBytes);
            return new MonsterMagicGrowResult { OriginalBytes = originalBytes, GrownBytes = grownBytes, OriginalEntryCount = entries.Count, NewEntryCount = reread.Count, DonorId = donorId, NewId = newId, Operand = operand, Name = DecodeUs(newCommand.NameScriptBytes), Description = DecodeUs(newCommand.DescriptionScriptBytes), OriginalLength = originalBytes.Length, GrownLength = grownBytes.Length, ExistingRecordPayloadPreserved = existingRowsPreserved, ExistingTextPoolPrefixPreserved = existingTextPreserved, RereadCountOk = rereadCountOk, RereadNewTextOk = rereadNewTextOk, PreserveWriteOk = preserveWriteOk };
        }

        public static MonsterMagicGrowResult AppendSnipe(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplySnipeRecipe(cmd));

        public static MonsterMagicGrowResult AppendFeatherStorm(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyFeatherStormRecipe(cmd));

        public static MonsterMagicGrowResult AppendUltraBlizzara(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyUltraBlizzaraRecipe(cmd));

        public static MonsterMagicGrowResult AppendVenomSting(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyVenomStingRecipe(cmd));

        public static MonsterMagicGrowResult AppendChaosSpark(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1) => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyChaosSparkRecipe(cmd));
        public static MonsterMagicGrowResult AppendMaggotBurst(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1) => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyMaggotBurstRecipe(cmd));
        public static MonsterMagicGrowResult AppendEvilGaze(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1) => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyEvilGazeRecipe(cmd));
        public static MonsterMagicGrowResult AppendSoulDrain(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1) => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplySoulDrainRecipe(cmd));
        public static MonsterMagicGrowResult AppendThunderCharge(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1) => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyThunderChargeRecipe(cmd));
        public static MonsterMagicGrowResult AppendWildFlurry(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1) => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyWildFlurryRecipe(cmd));

        public static void ApplyFlameFlanFloodVisualIds(Ability_Command command, bool useDedicatedClones)
        {
            if (useDedicatedClones)
            {
                command.Anim1Id = FlameFlanCloneWatergaAnim1Id;
                command.Anim2Id = FlameFlanCloneWatergaAnim2Id;
            }
            else
            {
                command.Anim1Id = FlameFlanVanillaWatergaAnim1Id;
                command.Anim2Id = FlameFlanVanillaWatergaAnim2Id;
            }
        }

        static void ApplyFlameFlanFloodGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.Magic;
            command.AttackPower = 42;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = Ability_Command.ElementFlags.Water | Ability_Command.ElementFlags.Fire;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
            command.StatusChance.Slow = 0;
            command.StatusDuration.Slow = 0;
        }

        static void ApplyFlameFlanFloodRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Flan Flood");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Scalding flan-tide - Waterga visuals tuned for FlameFlan.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyFlameFlanFloodGameplayOnly(command);
            ApplyFlameFlanFloodVisualIds(command, useDedicatedClones: true);
        }

        public const int GoreChargeCommandId = 250;
        public const int FangStrikeCommandId = 251;

        static void ApplyGoreChargeGameplayOnly(Ability_Command command)
        {
            command.AttackPower = 48;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = 0;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Physical;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.DamageFormula = DamageFormula_Enum.IgnoreDefense;
            command.FlagMisc4ShowSpellcastAura = true;
            command.StatusChance.Slow = 100;
            command.StatusDuration.Slow = 3;
        }

        static void ApplyGoreChargeRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Gore Charge");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Powerful charge that ignores defense and delays the target.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyGoreChargeGameplayOnly(command);
        }

        static void ApplyFangStrikeGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.IgnoreDefense;
            command.AttackPower = 48;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = 0;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Physical;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
            command.FlagTargetMulti = false;
            command.StatusChance.Slow = 100;
            command.StatusDuration.Slow = 3;
            command.StatusChance.Silence = 100;
            command.StatusDuration.Silence = 3;
            command.StatusChance.Darkness = 100;
            command.StatusDuration.Darkness = 3;
        }

        static void ApplyFangStrikeRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Fang Strike");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Brutal fang attack that slows, silences, and blinds the target.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyFangStrikeGameplayOnly(command);
        }

        static void ApplySnipeGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.IgnoreDefense;
            command.AttackPower = 60;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = 0;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Physical;
            command.FlagSpecialBuffAlwaysCrit = true;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagTargetMulti = false;
        }

        static void ApplySnipeRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Snipe");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Precise strike that always crits and ignores defense.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplySnipeGameplayOnly(command);
        }

        static void ApplyFeatherStormGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.IgnoreDefense;
            command.AttackPower = 40;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = 0;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Physical;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagTargetMulti = true;
            command.StatusChance.Darkness = 100;
            command.StatusDuration.Darkness = 3;
        }

        static void ApplyFeatherStormRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Feather Storm");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Blinding feather tempest that strikes all foes.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyFeatherStormGameplayOnly(command);
        }

        static void ApplyUltraBlizzaraGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.Normal;
            command.AttackPower = 28;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = Ability_Command.ElementFlags.Blizzard;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagTargetMulti = true;
            command.StatusChance.Slow = 100;
            command.StatusDuration.Slow = 3;
            command.StatusChance.Silence = 100;
            command.StatusDuration.Silence = 3;
        }

        static void ApplyUltraBlizzaraRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Ultra Blizzara");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Massive ice blast that freezes and slows all foes.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyUltraBlizzaraGameplayOnly(command);
        }

        static void ApplyVenomStingGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.IgnoreDefense;
            command.AttackPower = 36;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = 0;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Physical;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagTargetMulti = false;
            command.StatusChance.Poison = 100;
        }

        static void ApplyVenomStingRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Venom Sting");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Venomous sting that poisons and delays the target.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyVenomStingGameplayOnly(command);
        }

        // ── Thunder Plains OD recipes ──
        static void ApplyRandomHitSkill(Ability_Command command, int power, int hits, byte poison, byte sleep, byte silence, byte dark, byte confuse, byte slow)
        { command.DamageFormula = DamageFormula_Enum.Normal; command.AttackPower = (byte)power; command.HitCount = (byte)hits; command.CostMp = 0; command.ElementFlgs = 0; command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp; command.DamageFlgs = Ability_Command.DamageFlags.Magical; command.FlagMisc1UseInCombat = true; command.FlagMisc1DisplayMoveName = true; command.FlagMisc3AffectedBySilence = true; command.FlagTargetMulti = false; command.FlagMisc2RandomTargets = true;
            if (poison>0) command.StatusChance.Poison = poison; if (sleep>0) { command.StatusChance.Sleep = sleep; command.StatusDuration.Sleep = 3; } if (silence>0) { command.StatusChance.Silence = silence; command.StatusDuration.Silence = 3; } if (dark>0) { command.StatusChance.Darkness = dark; command.StatusDuration.Darkness = 3; } if (confuse>0) { command.StatusChance.Confuse = confuse; } if (slow>0) { command.StatusChance.Slow = slow; command.StatusDuration.Slow = 3; }
        }
        static void ApplyChaosSparkRecipe(Ability_Command command) { command.NameScriptBytes = EncodeUs("Chaos Spark"); command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>(); command.DescriptionScriptBytes = EncodeUs("Random elemental sparks that poison, silence, and blind."); command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>(); ApplyRandomHitSkill(command, 32, 2, (byte)60, (byte)0, (byte)60, (byte)60, (byte)0, (byte)0); }
        static void ApplyMaggotBurstRecipe(Ability_Command command) { command.NameScriptBytes = EncodeUs("Maggot Burst"); command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>(); command.DescriptionScriptBytes = EncodeUs("Exploding maggots that poison and slow all foes."); command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>(); ApplyRandomHitSkill(command, 28, 3, (byte)80, (byte)0, (byte)0, (byte)0, (byte)0, (byte)80); }
        static void ApplyEvilGazeRecipe(Ability_Command command) { command.NameScriptBytes = EncodeUs("Evil Gaze"); command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>(); command.DescriptionScriptBytes = EncodeUs("Malevolent glare that confuses and puts to sleep."); command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>(); ApplyRandomHitSkill(command, 20, 4, (byte)0, (byte)50, (byte)0, (byte)0, (byte)50, (byte)0); }
        static void ApplySoulDrainGameplayOnly(Ability_Command command) { command.DamageFormula = DamageFormula_Enum.IgnoreDefense; command.AttackPower = 48; command.HitCount = 1; command.CostMp = 0; command.ElementFlgs = 0; command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp; command.DamageFlgs = Ability_Command.DamageFlags.Magical; command.FlagMisc1UseInCombat = true; command.FlagMisc1DisplayMoveName = true; command.FlagMisc3AffectedBySilence = true; command.FlagTargetMulti = false; command.FlagStatusCurse = true; }
        static void ApplySoulDrainRecipe(Ability_Command command) { command.NameScriptBytes = EncodeUs("Soul Drain"); command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>(); command.DescriptionScriptBytes = EncodeUs("Drains the target's life force and curses them."); command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>(); ApplySoulDrainGameplayOnly(command); }
        static void ApplyThunderChargeGameplayOnly(Ability_Command command) { command.DamageFormula = DamageFormula_Enum.IgnoreDefense; command.AttackPower = 52; command.HitCount = 1; command.CostMp = 0; command.ElementFlgs = Ability_Command.ElementFlags.Thunder; command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp; command.DamageFlgs = Ability_Command.DamageFlags.Magical; command.FlagMisc1UseInCombat = true; command.FlagMisc1DisplayMoveName = true; command.FlagMisc3AffectedBySilence = true; command.FlagTargetMulti = false; command.FlagSpecialBuffAlwaysCrit = true; }
        static void ApplyThunderChargeRecipe(Ability_Command command) { command.NameScriptBytes = EncodeUs("Thunder Charge"); command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>(); command.DescriptionScriptBytes = EncodeUs("Massive lightning blast that always crits."); command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>(); ApplyThunderChargeGameplayOnly(command); }
        static void ApplyWildFlurryGameplayOnly(Ability_Command command) { command.DamageFormula = DamageFormula_Enum.Normal; command.AttackPower = 32; command.HitCount = 3; command.CostMp = 0; command.ElementFlgs = 0; command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp; command.DamageFlgs = Ability_Command.DamageFlags.Physical; command.FlagMisc1UseInCombat = true; command.FlagMisc1DisplayMoveName = true; command.FlagMisc3AffectedBySilence = true; command.FlagTargetMulti = false; }
        static void ApplyWildFlurryRecipe(Ability_Command command) { command.NameScriptBytes = EncodeUs("Wild Flurry"); command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>(); command.DescriptionScriptBytes = EncodeUs("Frenzied 3-hit assault, each strike a critical."); command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>(); ApplyWildFlurryGameplayOnly(command); }

        // ── Macalania OD recipes ──
        static void ApplyPermafrostGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.Magic;
            command.AttackPower = 36;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = Ability_Command.ElementFlags.Blizzard;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical | Ability_Command.DamageFlags.CanCrit;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagTargetEnabled = true;
            command.FlagTargetEnemies = true;
            command.FlagTargetMulti = true;
            command.FlagMisc2DelayS = true;
            command.Anim1Id = 0x0B;
            command.Anim2Id = 0x00;
            command.IconId = 0x0B;
            command.CasterAnimId = 0x02;
            command.AttackAccuracy = 255;
            command.PreviewFlgs = Ability_Command.PreviewFlags.Active;
            command.StatusChance.Slow = 100;
            command.StatusDuration.Slow = 3;
            command.StatusChance.Silence = 80;
            command.StatusDuration.Silence = 3;
        }
        static void ApplyPermafrostRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Permafrost");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("AoE Ice damage. Inflicts Slow & Silence.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyPermafrostGameplayOnly(command);
        }
        public static MonsterMagicGrowResult AppendPermafrost(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyPermafrostRecipe(cmd));

        static void ApplyManaStormFireGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.Magic;
            command.AttackPower = 38;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = Ability_Command.ElementFlags.Fire;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical | Ability_Command.DamageFlags.CanCrit;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagTargetEnabled = true;
            command.FlagTargetEnemies = true;
            command.FlagTargetMulti = false;
            command.Anim1Id = 0x0C;
            command.Anim2Id = 0x00;
            command.IconId = 0x0C;
            command.CasterAnimId = 0x02;
            command.AttackAccuracy = 255;
            command.PreviewFlgs = Ability_Command.PreviewFlags.Active;
        }
        static void ApplyManaStormFireRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Mana Storm");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Fire damage. Chimera's elemental storm.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyManaStormFireGameplayOnly(command);
        }
        public static MonsterMagicGrowResult AppendManaStormFire(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyManaStormFireRecipe(cmd));

        static void ApplyManaStormIceGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.Magic;
            command.AttackPower = 38;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = Ability_Command.ElementFlags.Blizzard;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical | Ability_Command.DamageFlags.CanCrit;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagTargetEnabled = true;
            command.FlagTargetEnemies = true;
            command.FlagTargetMulti = false;
            command.Anim1Id = 0x0B;
            command.Anim2Id = 0x00;
            command.IconId = 0x0B;
            command.CasterAnimId = 0x02;
            command.AttackAccuracy = 255;
            command.PreviewFlgs = Ability_Command.PreviewFlags.Active;
        }
        static void ApplyManaStormIceRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Mana Storm");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Ice damage. Chimera's elemental storm.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyManaStormIceGameplayOnly(command);
        }
        public static MonsterMagicGrowResult AppendManaStormIce(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyManaStormIceRecipe(cmd));

        static void ApplyManaStormThunderGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.Magic;
            command.AttackPower = 38;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = Ability_Command.ElementFlags.Thunder;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical | Ability_Command.DamageFlags.CanCrit;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagTargetEnabled = true;
            command.FlagTargetEnemies = true;
            command.FlagTargetMulti = false;
            command.Anim1Id = 0x0D;
            command.Anim2Id = 0x00;
            command.IconId = 0x0D;
            command.CasterAnimId = 0x02;
            command.AttackAccuracy = 255;
            command.PreviewFlgs = Ability_Command.PreviewFlags.Active;
        }
        static void ApplyManaStormThunderRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Mana Storm");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Thunder damage. Chimera's elemental storm.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyManaStormThunderGameplayOnly(command);
        }
        public static MonsterMagicGrowResult AppendManaStormThunder(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyManaStormThunderRecipe(cmd));

        static void ApplyFrostClawGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.IgnoreDefense;
            command.AttackPower = 48;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = Ability_Command.ElementFlags.Blizzard;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Physical | Ability_Command.DamageFlags.CanCrit;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc3Piercing = true;
            command.FlagTargetEnabled = true;
            command.FlagTargetEnemies = true;
            command.FlagTargetMulti = false;
            command.Anim1Id = 0x2D;
            command.Anim2Id = 0x0B;
            command.IconId = 0x0B;
            command.CasterAnimId = 0x01;
            command.AttackAccuracy = 255;
            command.PreviewFlgs = Ability_Command.PreviewFlags.Active;
            command.StatusChance.Slow = 100;
            command.StatusDuration.Slow = 3;
            command.StatusChance.Silence = 80;
            command.StatusDuration.Silence = 3;
        }
        static void ApplyFrostClawRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Frost Claw");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Physical Ice dmg (IgnoreDef). Inflicts Slow & Silence.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyFrostClawGameplayOnly(command);
        }
        public static MonsterMagicGrowResult AppendFrostClaw(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyFrostClawRecipe(cmd));

        // ── UNI-004 Ward Stack skill ──
        static void ApplyWardStackGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.Magic;
            command.AttackPower = 0;
            command.HitCount = 1;
            command.CostMp = 0;
            command.DamageTypeFlgs = 0;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagTargetEnabled = true;
            command.FlagTargetMulti = false;
            command.Anim1Id = 0x21A1;   // NulAll anim
            command.Anim2Id = 0x003A;
            command.IconId = 0xA8;
            command.CasterAnimId = 0x21; // NulAll caster anim
            command.AttackAccuracy = 255;
            command.PreviewFlgs = Ability_Command.PreviewFlags.Active;
            // Self-buff: Shell + Regen + NulBlaze + NulShock
            command.StatusChance.Shell = 254;    // infinite
            command.StatusDuration.Shell = 1;    // 1 block (shell doesn't expire)
            command.StatusChance.Regen = 254;
            command.StatusDuration.Regen = 1;
            command.StatusChance.NulBlaze = 254;
            command.StatusDuration.NulBlaze = 1;
            command.StatusChance.NulShock = 254;
            command.StatusDuration.NulShock = 1;
        }
        static void ApplyWardStackRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Ward Stack");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Shell + Regen + NulBlaze + NulShock on self.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyWardStackGameplayOnly(command);
                    command.Anim1Id = 776;
            command.Anim2Id = 769;
        }
        public static MonsterMagicGrowResult AppendWardStack(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyWardStackRecipe(cmd));

        // ─── Sin Possessed pack: 5 monster skills for UNI-002/005/006/007/008 ───

        public const int FrostFloodWeaveCommandId = 268;

        // ─── Calm Lands: 4 monster overdrive skills ───
        //  0x6111 Blaster Cannon (Coeurl m089)
        //  0x6112 Mighty Guard   (Chimera Brain m088)
        //  0x6113 Psychic Storm  (Chimera Brain m088)
        //  0x6114 Ogre Smash     (Ogre m066)

        public const int BlasterCannonCommandId = 273;
        public const int MightyGuardCommandId = 274;
        public const int PsychicStormCommandId = 275;
        public const int OgreSmashCommandId = 276;

        public static MonsterMagicGrowResult AppendBlasterCannon(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyBlasterCannonRecipe(cmd));
        public static MonsterMagicGrowResult AppendMightyGuard(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyMightyGuardRecipe(cmd));
        public static MonsterMagicGrowResult AppendPsychicStorm(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyPsychicStormRecipe(cmd));
        public static MonsterMagicGrowResult AppendOgreSmash(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyOgreSmashRecipe(cmd));

        static void ApplyBlasterCannonGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.IgnoreDefense;
            command.AttackPower = 48;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = Ability_Command.ElementFlags.Thunder;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical | Ability_Command.DamageFlags.CanCrit;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
            command.FlagTargetMulti = false;
            command.StatusChance.Petrify = 60;
        }

        static void ApplyBlasterCannonRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Blaster Cannon");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Thunder blast that petrifies. Coeurl's overdrive.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyBlasterCannonGameplayOnly(command);
            command.Anim1Id = 797;
            command.Anim2Id = 798;
        }

        static void ApplyMightyGuardGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.NoDamage;
            command.AttackPower = 0;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = 0;
            command.DamageTypeFlgs = 0;
            command.DamageFlgs = 0;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
            command.FlagTargetMulti = false;
            command.FlagTargetSelfOnly = true;
            command.StatusChance.Protect = 255;
            command.StatusChance.Shell = 255;
            command.StatusDuration.Protect = 0;
            command.StatusDuration.Shell = 0;
            command.StatBuffFlgs = Ability_Command.StatBuffFlags.Cheer
                | Ability_Command.StatBuffFlags.Aim | Ability_Command.StatBuffFlags.Focus
                | Ability_Command.StatBuffFlags.Reflex | Ability_Command.StatBuffFlags.Luck;
            command.StatBuffValue = 3;
            command.PreviewFlgs = Ability_Command.PreviewFlags.Active;
        }

        static void ApplyMightyGuardRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Mighty Guard");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Boosts DEF & MDEF. Grants Protect & Shell.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyMightyGuardGameplayOnly(command);
            command.Anim1Id = 725;
            command.Anim2Id = 725;
        }

        static void ApplyPsychicStormGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.Magic;
            command.AttackPower = 42;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = 0;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical | Ability_Command.DamageFlags.CanCrit;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
            command.FlagTargetMulti = true;
            command.StatusChance.Confuse = 100;
        }

        static void ApplyPsychicStormRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Psychic Storm");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Psychic wave that confuses all foes.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyPsychicStormGameplayOnly(command);
            command.Anim1Id = 722;
            command.Anim2Id = 0;
        }

        static void ApplyOgreSmashGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.IgnoreDefense;
            command.AttackPower = 52;
            command.HitCount = 2;
            command.CostMp = 0;
            command.ElementFlgs = 0;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Physical | Ability_Command.DamageFlags.CanCrit;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3Piercing = true;
            command.FlagMisc3AffectedBySilence = false;
            command.FlagMisc4ShowSpellcastAura = false;
            command.FlagTargetMulti = false;
            command.StatusChance.Slow = 100;
            command.StatusDuration.Slow = 3;
        }

        static void ApplyOgreSmashRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Ogre Smash");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Double ground slam that slows and ignores defense.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyOgreSmashGameplayOnly(command);
            command.Anim1Id = 788;
            command.Anim2Id = 788;
        }

        // ─── Bikanel: 3 monster overdrive skills ───
        //  0x6115 Sand Breath      (Mushussu m062)
        //  0x6116 Sonic Storm      (Zu m045)
        //  0x6117 10,000 Needles   (Cactuar m208)

        public const int SandBreathCommandId = 277;
        public const int SonicStormCommandId = 278;
        public const int TenThousandNeedlesCommandId = 279;

        public static MonsterMagicGrowResult AppendSandBreath(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplySandBreathRecipe(cmd));
        public static MonsterMagicGrowResult AppendSonicStorm(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplySonicStormRecipe(cmd));
        public static MonsterMagicGrowResult AppendTenThousandNeedles(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyTenThousandNeedlesRecipe(cmd));

        static void ApplySandBreathGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.Magic;
            command.AttackPower = 40;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = 0;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical | Ability_Command.DamageFlags.CanCrit;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
            command.FlagTargetMulti = true;
            command.StatusChance.Darkness = 100;
            command.StatusDuration.Darkness = 3;
        }

        static void ApplySandBreathRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Sand Breath");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Desert blast that blinds all foes.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplySandBreathGameplayOnly(command);
            command.Anim1Id = 727;
            command.Anim2Id = 727;
        }

        static void ApplySonicStormGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.Magic;
            command.AttackPower = 44;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = 0;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical | Ability_Command.DamageFlags.CanCrit;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
            command.FlagTargetMulti = true;
            command.StatusChance.Confuse = 80;

        }

        static void ApplySonicStormRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Sonic Storm");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Supersonic screech that confuses all foes.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplySonicStormGameplayOnly(command);
            command.Anim1Id = 728;
            command.Anim2Id = 728;
        }

        static void ApplyTenThousandNeedlesGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.Normal;
            command.AttackPower = 60;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = 0;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Physical | Ability_Command.DamageFlags.CanCrit;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = false;
            command.FlagMisc4ShowSpellcastAura = false;
            command.FlagTargetMulti = false;
            command.FlagSpecialBuffAlwaysCrit = true;
        }

        static void ApplyTenThousandNeedlesRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("10,000 Needles");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Cactuar's classic salvo. Always critical.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyTenThousandNeedlesGameplayOnly(command);
            command.Anim1Id = 752;
            command.Anim2Id = 0;
        }

        // ─── Mt. Gagazet: 14 monster overdrive skills (0x6118-0x6125) ───
        //  0x6118 Magic Burst      (Imp m008)
        //  0x6119 Hydro Cannon     (Splasher m071)
        //  0x611A Feral Rush       (Bandersnatch m015)
        //  0x611B Dragon Breath    (Nidhogg m063)
        //  0x611C Shell Shatter    (Grat m040)
        //  0x611D Doom Gaze        (Ahriman m038)
        //  0x611E Spike Rain       (Maelspike m054)
        //  0x611F Whirlpool        (Achelous m052)
        //  0x6120 Full Burst       (Grenade m194)
        //  0x6121 Knuckle Press    (Bashura m067)
        //  0x6122 Dark Eruption    (Dark Flan m021)
        //  0x6123 Savage Rend      (Grendel m057)
        //  0x6124 Meteor           (Behemoth m085)
        //  0x6125 Scream           (Mandragora m190)

        public const int MagicBurstCommandId = 280;
        public const int HydroCannonCommandId = 281;
        public const int FeralRushCommandId = 282;
        public const int DragonBreathCommandId = 283;
        public const int ShellShatterCommandId = 284;
        public const int DoomGazeCommandId = 285;
        public const int SpikeRainCommandId = 286;
        public const int WhirlpoolCommandId = 287;
        public const int FullBurstCommandId = 288;
        public const int KnucklePressCommandId = 289;
        public const int DarkEruptionCommandId = 290;
        public const int SavageRendCommandId = 291;
        public const int MeteorCommandId = 292;
        public const int ScreamCommandId = 293;

        public static MonsterMagicGrowResult AppendMagicBurst(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyMagicBurstRecipe(cmd));
        public static MonsterMagicGrowResult AppendHydroCannon(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyHydroCannonRecipe(cmd));
        public static MonsterMagicGrowResult AppendFeralRush(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyFeralRushRecipe(cmd));
        public static MonsterMagicGrowResult AppendDragonBreath(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyDragonBreathRecipe(cmd));
        public static MonsterMagicGrowResult AppendShellShatter(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyShellShatterRecipe(cmd));
        public static MonsterMagicGrowResult AppendDoomGaze(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyDoomGazeRecipe(cmd));
        public static MonsterMagicGrowResult AppendSpikeRain(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplySpikeRainRecipe(cmd));
        public static MonsterMagicGrowResult AppendWhirlpool(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyWhirlpoolRecipe(cmd));
        public static MonsterMagicGrowResult AppendFullBurst(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyFullBurstRecipe(cmd));
        public static MonsterMagicGrowResult AppendKnucklePress(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyKnucklePressRecipe(cmd));
        public static MonsterMagicGrowResult AppendDarkEruption(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyDarkEruptionRecipe(cmd));
        public static MonsterMagicGrowResult AppendSavageRend(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplySavageRendRecipe(cmd));
        public static MonsterMagicGrowResult AppendMeteor(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyMeteorRecipe(cmd));
        public static MonsterMagicGrowResult AppendScream(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyScreamRecipe(cmd));

        static void ApplyMagicBurstGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.Magic;
            command.AttackPower = 38;
            command.HitCount = 3;
            command.CostMp = 0;
            command.ElementFlgs = 0;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical | Ability_Command.DamageFlags.CanCrit;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
            command.FlagTargetMulti = true;
        }

        static void ApplyMagicBurstRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Magic Burst");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Triple non-elemental magic burst. Imp's overdrive.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyMagicBurstGameplayOnly(command);
            command.Anim1Id = 786;
            command.Anim2Id = 0;
        }

        static void ApplyHydroCannonGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.IgnoreDefense;
            command.AttackPower = 50;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = Ability_Command.ElementFlags.Water;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical | Ability_Command.DamageFlags.CanCrit;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
            command.FlagTargetMulti = false;
            command.FlagSpecialBuffAlwaysCrit = true;
        }

        static void ApplyHydroCannonRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Hydro Cannon");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Pressurized water jet. Always critical.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyHydroCannonGameplayOnly(command);
            command.Anim1Id = 731;
            command.Anim2Id = 731;
        }

        static void ApplyFeralRushGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.Normal;
            command.AttackPower = 36;
            command.HitCount = 3;
            command.CostMp = 0;
            command.ElementFlgs = 0;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Physical | Ability_Command.DamageFlags.CanCrit;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = false;
            command.FlagMisc4ShowSpellcastAura = false;
            command.FlagTargetMulti = false;
            command.FlagMisc2RandomTargets = true;
        }

        static void ApplyFeralRushRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Feral Rush");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Three random physical strikes.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyFeralRushGameplayOnly(command);
            command.Anim1Id = 732;
            command.Anim2Id = 732;
        }

        static void ApplyDragonBreathGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.Magic;
            command.AttackPower = 42;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = Ability_Command.ElementFlags.Fire;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical | Ability_Command.DamageFlags.CanCrit;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
            command.FlagTargetMulti = true;
            command.StatusChance.Slow = 100;
            command.StatusDuration.Slow = 3;
        }

        static void ApplyDragonBreathRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Dragon Breath");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("AoE Fire breath that slows all foes.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyDragonBreathGameplayOnly(command);
            command.Anim1Id = 733;
            command.Anim2Id = 733;
        }

        static void ApplyShellShatterGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.Normal;
            command.AttackPower = 44;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = 0;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Physical | Ability_Command.DamageFlags.CanCrit;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = false;
            command.FlagMisc4ShowSpellcastAura = false;
            command.FlagTargetMulti = false;
            command.StatusChance.BreakArmor = 100;
        }

        static void ApplyShellShatterRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Shell Shatter");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Crushing blow that lowers target's Defense.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyShellShatterGameplayOnly(command);
            command.Anim1Id = 790;
            command.Anim2Id = 790;
        }

        static void ApplyDoomGazeGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.Magic;
            command.AttackPower = 40;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = 0;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical | Ability_Command.DamageFlags.CanCrit;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
            command.FlagTargetMulti = false;
            command.StatusChance.Petrify = 80;
        }

        static void ApplyDoomGazeRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Doom Gaze");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Evil gaze that petrifies the target.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyDoomGazeGameplayOnly(command);
            command.Anim1Id = 735;
            command.Anim2Id = 735;
        }

        static void ApplySpikeRainGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.Normal;
            command.AttackPower = 38;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = Ability_Command.ElementFlags.Water;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Physical | Ability_Command.DamageFlags.CanCrit;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = false;
            command.FlagMisc4ShowSpellcastAura = false;
            command.FlagTargetMulti = true;
        }

        static void ApplySpikeRainRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Spike Rain");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("AoE water spikes. Maelspike's overdrive.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplySpikeRainGameplayOnly(command);
            command.Anim1Id = 736;
            command.Anim2Id = 736;
        }

        static void ApplyWhirlpoolGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.Magic;
            command.AttackPower = 36;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = Ability_Command.ElementFlags.Water;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical | Ability_Command.DamageFlags.CanCrit;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
            command.FlagTargetMulti = true;
            command.StatusChance.Slow = 100;
            command.StatusDuration.Slow = 3;
        }

        static void ApplyWhirlpoolRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Whirlpool");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Water vortex that slows all foes.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyWhirlpoolGameplayOnly(command);
            command.Anim1Id = 737;
            command.Anim2Id = 737;
        }

        static void ApplyFullBurstGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.Normal;
            command.AttackPower = 54;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = Ability_Command.ElementFlags.Fire;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Physical | Ability_Command.DamageFlags.CanCrit;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = false;
            command.FlagMisc4ShowSpellcastAura = false;
            command.FlagTargetMulti = true;
        }

        static void ApplyFullBurstRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Full Burst");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Massive AoE fire explosion. Grenade's overdrive.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyFullBurstGameplayOnly(command);
            command.Anim1Id = 738;
            command.Anim2Id = 739;
        }

        static void ApplyKnucklePressGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.IgnoreDefense;
            command.AttackPower = 56;
            command.HitCount = 2;
            command.CostMp = 0;
            command.ElementFlgs = 0;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Physical | Ability_Command.DamageFlags.CanCrit;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3Piercing = true;
            command.FlagMisc3AffectedBySilence = false;
            command.FlagMisc4ShowSpellcastAura = false;
            command.FlagTargetMulti = false;
        }

        static void ApplyKnucklePressRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Knuckle Press");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Double ignore-defense punch. Bashura's overdrive.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyKnucklePressGameplayOnly(command);
            command.Anim1Id = 740;
            command.Anim2Id = 740;
        }

        static void ApplyDarkEruptionGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.Magic;
            command.AttackPower = 44;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = 0;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical | Ability_Command.DamageFlags.CanCrit;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
            command.FlagTargetMulti = true;
            command.StatusChance.Darkness = 100;
            command.StatusDuration.Darkness = 3;
        }

        static void ApplyDarkEruptionRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Dark Eruption");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Wave of darkness that blinds all foes.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyDarkEruptionGameplayOnly(command);
            command.Anim1Id = 741;
            command.Anim2Id = 742;
        }

        static void ApplySavageRendGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.Normal;
            command.AttackPower = 48;
            command.HitCount = 2;
            command.CostMp = 0;
            command.ElementFlgs = 0;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Physical | Ability_Command.DamageFlags.CanCrit;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = false;
            command.FlagMisc4ShowSpellcastAura = false;
            command.FlagTargetMulti = false;
            command.StatusChance.Poison = 80;
        }

        static void ApplySavageRendRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Savage Rend");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Double rend that poisons the target.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplySavageRendGameplayOnly(command);
            command.Anim1Id = 743;
            command.Anim2Id = 743;
        }

        static void ApplyMeteorGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.Magic;
            command.AttackPower = 60;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = 0;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical | Ability_Command.DamageFlags.CanCrit;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
            command.FlagTargetMulti = false;
        }

        static void ApplyMeteorRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Meteor");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Massive non-elemental magic. Behemoth's overdrive.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyMeteorGameplayOnly(command);
            command.Anim1Id = 791;
            command.Anim2Id = 791;
        }

        static void ApplyScreamGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.Magic;
            command.AttackPower = 40;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = 0;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical | Ability_Command.DamageFlags.CanCrit;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
            command.FlagTargetMulti = true;
            command.StatusChance.Petrify = 60;
            command.StatusChance.Confuse = 60;

        }

        static void ApplyScreamRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Scream");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Sonic blast that petrifies and confuses all foes.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyScreamGameplayOnly(command);
            command.Anim1Id = 745;
            command.Anim2Id = 745;
        }

        // ─── Cavern of the Stolen Fayth: 3 monster overdrive skills (0x6126-0x6128) ───
        //  0x6126 Megadeath     (Ghost m050)
        //  0x6127 Dark Pulse    (Dark Element m082)
        //  0x6128 Shield Crash  (Defender m048)

        public const int MegadeathCommandId = 294;
        public const int DarkPulseCommandId = 295;
        public const int ShieldCrashCommandId = 296;

        public static MonsterMagicGrowResult AppendMegadeath(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyMegadeathRecipe(cmd));
        public static MonsterMagicGrowResult AppendDarkPulse(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyDarkPulseRecipe(cmd));
        public static MonsterMagicGrowResult AppendShieldCrash(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyShieldCrashRecipe(cmd));

        static void ApplyMegadeathGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.IgnoreDefense;
            command.AttackPower = 48;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = 0;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical | Ability_Command.DamageFlags.CanCrit;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
            command.FlagTargetMulti = false;
            command.StatusChance.Death = 80;
        }

        static void ApplyMegadeathRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Megadeath");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Soul-rending wail. High chance of Instant Death.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyMegadeathGameplayOnly(command);
            command.Anim1Id = 793;
            command.Anim2Id = 793;
        }

        static void ApplyDarkPulseGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.Magic;
            command.AttackPower = 46;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = 0;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical | Ability_Command.DamageFlags.CanCrit;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
            command.FlagTargetMulti = true;
            command.StatusChance.Darkness = 100;
            command.StatusDuration.Darkness = 3;
        }

        static void ApplyDarkPulseRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Dark Pulse");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Wave of darkness that blinds all foes.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyDarkPulseGameplayOnly(command);
            command.Anim1Id = 747;
            command.Anim2Id = 0;
        }

        static void ApplyShieldCrashGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.IgnoreDefense;
            command.AttackPower = 55;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = 0;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Physical | Ability_Command.DamageFlags.CanCrit;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3Piercing = true;
            command.FlagMisc3AffectedBySilence = false;
            command.FlagMisc4ShowSpellcastAura = false;
            command.FlagTargetMulti = false;
            command.StatusChance.BreakPower = 100;
            command.StatusChance.BreakMagic = 100;
            command.StatusChance.BreakArmor = 100;
            command.StatusChance.BreakMental = 100;
            command.StatusChance.Slow = 100;
            command.StatusDuration.Slow = 3;
        }

        static void ApplyShieldCrashRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Shield Crash");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("All stat breaks + slow. Defender's overdrive.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyShieldCrashGameplayOnly(command);
            command.Anim1Id = 748;
            command.Anim2Id = 748;
        }

        // ─── Zanarkand: 1 monster overdrive skill (0x6129) ───
        //  0x6129 Guardian Blast (Defender Z m049)

        public const int GuardianBlastCommandId = 297;

        public static MonsterMagicGrowResult AppendGuardianBlast(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyGuardianBlastRecipe(cmd));

        static void ApplyGuardianBlastGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.Normal;
            command.AttackPower = 60;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = 0;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Physical | Ability_Command.DamageFlags.CanCrit;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = false;
            command.FlagMisc4ShowSpellcastAura = false;
            command.FlagTargetMulti = true;
            command.StatusChance.BreakPower = 100;
            command.StatusChance.Slow = 100;
            command.StatusDuration.Slow = 3;
        }

        static void ApplyGuardianBlastRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Guardian Blast");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("AoE blast that Power Breaks and slows all foes.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyGuardianBlastGameplayOnly(command);
            command.Anim1Id = 753;
            command.Anim2Id = 753;
        }
        public const int ShorelineBreakCommandId = 269;
        public const int SinSalveCommandId = 270;
        public const int MistChorusCommandId = 271;
        public const int CounterMarchCommandId = 272;

        public static MonsterMagicGrowResult AppendFrostFloodWeave(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyFrostFloodWeaveRecipe(cmd));

        public static MonsterMagicGrowResult AppendShorelineBreak(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyShorelineBreakRecipe(cmd));

        public static MonsterMagicGrowResult AppendSinSalve(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplySinSalveRecipe(cmd));

        public static MonsterMagicGrowResult AppendMistChorus(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyMistChorusRecipe(cmd));

        public static MonsterMagicGrowResult AppendCounterMarch(byte[] originalBytes, bool monsterMagic2 = true, int donorId = -1)
            => AppendMonsterSkill(originalBytes, monsterMagic2, donorId, cmd => ApplyCounterMarchRecipe(cmd));

        static void ApplyFrostFloodWeaveGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.Magic;
            command.AttackPower = 38;
            command.HitCount = 2;
            command.CostMp = 0;
            command.ElementFlgs = Ability_Command.ElementFlags.Blizzard | Ability_Command.ElementFlags.Water;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
            command.StatusChance.Slow = 0;
            command.StatusDuration.Slow = 0;
        }

        static void ApplyFrostFloodWeaveRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Frost-Flood Weave");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Dual cast: Blizzara then Watera on the front row.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyFrostFloodWeaveGameplayOnly(command);
            command.Anim1Id = 770;
            command.Anim2Id = 771;
        }

        static void ApplyShorelineBreakGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.Magic;
            command.AttackPower = 32;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = Ability_Command.ElementFlags.Water;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
            command.FlagTargetMulti = true;
            command.StatusChance.Slow = 0;
            command.StatusDuration.Slow = 0;
        }

        static void ApplyShorelineBreakRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Shoreline Break");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Watera forced on FrontlineChars (whole front row).");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyShorelineBreakGameplayOnly(command);
            command.Anim1Id = 772;
            command.Anim2Id = 773;
        }

        static void ApplySinSalveGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.NoDamage;
            command.AttackPower = 0;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = 0;
            command.DamageTypeFlgs = 0;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
            command.FlagTargetMulti = false;
            command.FlagDamageHeals = true;
            command.PreviewFlgs = Ability_Command.PreviewFlags.Active | Ability_Command.PreviewFlags.HealHp;
        }

        static void ApplySinSalveRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Sin Salve");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Cure on self when HP < 50% (NearDeath threshold).");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplySinSalveGameplayOnly(command);
            command.Anim1Id = 43;
            command.Anim2Id = 43;
        }

        static void ApplyMistChorusGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.NoDamage;
            command.AttackPower = 0;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = 0;
            command.DamageTypeFlgs = 0;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
            command.FlagTargetMulti = true;
            command.FlagDamageHeals = true;
            command.PreviewFlgs = Ability_Command.PreviewFlags.Active | Ability_Command.PreviewFlags.HealHp;
        }

        static void ApplyMistChorusRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Mist Chorus");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("White Wind on all allied monsters when hurt.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyMistChorusGameplayOnly(command);
            command.Anim1Id = 175;
            command.Anim2Id = 175;
        }

        static void ApplyCounterMarchGameplayOnly(Ability_Command command)
        {
            command.DamageFormula = DamageFormula_Enum.Normal;
            command.AttackPower = 24;
            command.HitCount = 1;
            command.CostMp = 0;
            command.ElementFlgs = 0;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Physical;
            command.FlagDamageBreaksDamageLimit = false;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.FlagMisc3AffectedBySilence = false;
            command.FlagMisc4ShowSpellcastAura = false;
            command.FlagTargetMulti = false;
            command.StatusChance.Slow = 100;
            command.StatusDuration.Slow = 3;
        }

        static void ApplyCounterMarchRecipe(Ability_Command command)
        {
            command.NameScriptBytes = EncodeUs("Counter March");
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs("Counter with a delaying strike that slows the attacker.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            ApplyCounterMarchGameplayOnly(command);
                    command.Anim1Id = 774;
            command.Anim2Id = 775;
        }

        // UNI-010 is an action-replay hook candidate and deliberately has no command row.
        public const int SandMantleCommandId = 298;
        public const int SinSiphonCommandId = 299;
        public const int SandstormCommandId = 300;
        public const int DustWardCommandId = 301;
        public const int HushWaveCommandId = 302;
        public const int SilicaShardsCommandId = 303;

        public static MonsterMagicGrowResult AppendSandMantle(byte[] bytes, bool monsterMagic2 = true, int donorId = 274)
            => AppendMonsterSkill(bytes, monsterMagic2, donorId, ApplySandMantleRecipe);

        public static MonsterMagicGrowResult AppendSinSiphon(byte[] bytes, bool monsterMagic2 = true, int donorId = 259)
            => AppendMonsterSkill(bytes, monsterMagic2, donorId, ApplySinSiphonRecipe);

        public static MonsterMagicGrowResult AppendSandstorm(byte[] bytes, bool monsterMagic2 = true, int donorId = 110)
            => AppendMonsterSkill(bytes, monsterMagic2, donorId, ApplySandstormRecipe);

        public static MonsterMagicGrowResult AppendDustWard(byte[] bytes, bool monsterMagic2 = true, int donorId = 274)
            => AppendMonsterSkill(bytes, monsterMagic2, donorId, ApplyDustWardRecipe);

        public static MonsterMagicGrowResult AppendHushWave(byte[] bytes, bool monsterMagic2 = true, int donorId = 110)
            => AppendMonsterSkill(bytes, monsterMagic2, donorId, ApplyHushWaveRecipe);

        public static MonsterMagicGrowResult AppendSilicaShards(byte[] bytes, bool monsterMagic2 = true, int donorId = 269)
            => AppendMonsterSkill(bytes, monsterMagic2, donorId, ApplySilicaShardsRecipe);

        static void SetBikanelSinText(Ability_Command command, string name, string description)
        {
            command.NameScriptBytes = EncodeUs(name);
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.DescriptionScriptBytes = EncodeUs(description);
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            command.CostMp = 0;
            command.HitCount = 1;
            command.FlagMisc1UseInCombat = true;
            command.FlagMisc1DisplayMoveName = true;
            command.PreviewFlgs = Ability_Command.PreviewFlags.Active;
        }

        static void ApplySandMantleRecipe(Ability_Command command)
        {
            SetBikanelSinText(command, "Sand Mantle", "Self Protect, Cheer and Reflex after an action.");
            command.DamageFormula = DamageFormula_Enum.NoDamage;
            command.AttackPower = 0;
            command.DamageTypeFlgs = 0;
            command.DamageFlgs = 0;
            command.ElementFlgs = 0;
            command.TargetFlgs = Ability_Command.TargetFlags.Enabled
                | Ability_Command.TargetFlags.SelfOnly | Ability_Command.TargetFlags.EitherTeam;
            command.StatusChance.Protect = 254;
            command.StatusDuration.Protect = 6;
            command.StatusChance.Shell = 0;
            command.StatusDuration.Shell = 0;
            command.StatBuffFlgs = Ability_Command.StatBuffFlags.Cheer | Ability_Command.StatBuffFlags.Reflex;
            command.StatBuffValue = 1;
            command.FlagMisc1AffectedByReflect = false;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
            command.Anim1Id = 760;
            command.Anim2Id = 760;
        }

        static void ApplySinSiphonRecipe(Ability_Command command)
        {
            SetBikanelSinText(command, "Sin Siphon", "Drain HP from one frontline foe.");
            command.DamageFormula = DamageFormula_Enum.Magic;
            command.AttackPower = 20;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Magical;
            command.ElementFlgs = 0;
            command.TargetFlgs = Ability_Command.TargetFlags.Enabled
                | Ability_Command.TargetFlags.Enemies | Ability_Command.TargetFlags.EitherTeam
                | Ability_Command.TargetFlags.LongRange;
            command.FlagMisc2AbsorbDamage = true;
            command.FlagStatusCurse = false;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
            command.Anim1Id = 761;
            command.Anim2Id = 762;
        }

        static void ApplySandstormRecipe(Ability_Command command)
        {
            SetBikanelSinText(command, "Sandstorm", "Delay, Darkness, Silence and Jinx all foes.");
            command.DamageFormula = DamageFormula_Enum.NoDamage;
            command.AttackPower = 0;
            command.DamageTypeFlgs = 0;
            command.DamageFlgs = 0;
            command.ElementFlgs = 0;
            command.TargetFlgs = Ability_Command.TargetFlags.Enabled
                | Ability_Command.TargetFlags.Enemies | Ability_Command.TargetFlags.Multi
                | Ability_Command.TargetFlags.EitherTeam | Ability_Command.TargetFlags.LongRange;
            command.FlagMisc2DelayS = true;
            command.StatusChance.Darkness = 100;
            command.StatusDuration.Darkness = 3;
            command.StatusChance.Silence = 100;
            command.StatusDuration.Silence = 3;
            command.StatBuffFlgs = Ability_Command.StatBuffFlags.Jinx;
            command.StatBuffValue = 1;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
            command.Anim1Id = 763;
            command.Anim2Id = 763;
        }

        static void ApplyDustWardRecipe(Ability_Command command)
        {
            SetBikanelSinText(command, "Dust Ward", "Protect all allied monsters.");
            command.DamageFormula = DamageFormula_Enum.NoDamage;
            command.AttackPower = 0;
            command.DamageTypeFlgs = 0;
            command.DamageFlgs = 0;
            command.ElementFlgs = 0;
            command.TargetFlgs = Ability_Command.TargetFlags.Enabled
                | Ability_Command.TargetFlags.Multi | Ability_Command.TargetFlags.EitherTeam
                | Ability_Command.TargetFlags.LongRange;
            command.StatusChance.Protect = 254;
            command.StatusDuration.Protect = 6;
            command.StatusChance.Shell = 0;
            command.StatusDuration.Shell = 0;
            command.StatBuffFlgs = 0;
            command.StatBuffValue = 0;
            command.FlagMisc1AffectedByReflect = false;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
            command.Anim1Id = 764;
            command.Anim2Id = 764;
        }

        static void ApplyHushWaveRecipe(Ability_Command command)
        {
            SetBikanelSinText(command, "Hush Wave", "Silence all opposing frontline actors.");
            command.DamageFormula = DamageFormula_Enum.NoDamage;
            command.AttackPower = 0;
            command.DamageTypeFlgs = 0;
            command.DamageFlgs = 0;
            command.ElementFlgs = 0;
            command.TargetFlgs = Ability_Command.TargetFlags.Enabled
                | Ability_Command.TargetFlags.Enemies | Ability_Command.TargetFlags.Multi
                | Ability_Command.TargetFlags.EitherTeam | Ability_Command.TargetFlags.LongRange;
            command.StatusChance.Silence = 100;
            command.StatusDuration.Silence = 3;
            command.FlagMisc3AffectedBySilence = true;
            command.FlagMisc4ShowSpellcastAura = true;
            command.Anim1Id = 765;
            command.Anim2Id = 766;
        }

        static void ApplySilicaShardsRecipe(Ability_Command command)
        {
            SetBikanelSinText(command, "Silica Shards", "Physical area damage and Silence; no MACHINA bonus.");
            command.DamageFormula = DamageFormula_Enum.Normal;
            command.AttackPower = 34;
            command.DamageTypeFlgs = Ability_Command.DamageTypeFlags.Hp;
            command.DamageFlgs = Ability_Command.DamageFlags.Physical | Ability_Command.DamageFlags.CanCrit;
            command.ElementFlgs = 0;
            command.TargetFlgs = Ability_Command.TargetFlags.Enabled
                | Ability_Command.TargetFlags.Enemies | Ability_Command.TargetFlags.Multi
                | Ability_Command.TargetFlags.EitherTeam | Ability_Command.TargetFlags.LongRange;
            command.StatusChance.Silence = 80;
            command.StatusDuration.Silence = 3;
            command.FlagMisc3AffectedBySilence = false;
            command.FlagMisc4ShowSpellcastAura = false;
            command.Anim1Id = 779;
            command.Anim2Id = 779;
        }

        static byte[] BuildNewRow(Ability_Command command, byte[] originalTextPool, out byte[] grownTextPool)
        {
            grownTextPool = originalTextPool.ToArray();
            Ability_CommandStruct row = new() { AbilityInfo = command };

            AppendScript(ref grownTextPool, row.NameTSInfo, command.NameScriptBytes, command.NameScriptId);
            AppendScript(ref grownTextPool, row.UnusedText1TSInfo, command.JapaneseOnlyText1ScriptBytes, command.JapaneseOnlyText1ScriptId);
            AppendScript(ref grownTextPool, row.DescriptionTSInfo, command.DescriptionScriptBytes, command.DescriptionScriptId);
            AppendScript(ref grownTextPool, row.UnusedText2TSInfo, command.JapaneseOnlyText2ScriptBytes, command.JapaneseOnlyText2ScriptId);

            using MemoryStream stream = new();
            BinaryMapping.WriteObject(stream, row);
            byte[] rowBytes = stream.ToArray();
            if (rowBytes.Length != MonMagicEntrySize)
                throw new InvalidDataException($"New monmagic row serialized to 0x{rowBytes.Length:X}; expected 0x{MonMagicEntrySize:X}.");

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

        static byte[] EncodeUs(string text)
        {
            return FfxEncoding.EncodeString(text, FfxEncoding.UsEncoder).ByteArray;
        }

        static string DecodeUs(byte[] bytes)
        {
            return FfxEncoding.DecodeScript(bytes).GetString(FfxEncoding.UsDecoder, withControlCodes: true);
        }
    }
}

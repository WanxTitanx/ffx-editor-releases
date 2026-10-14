using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;

namespace FFXProjectEditor.FfxLib.Ability
{
    public sealed class SpiraReforgeSpellEntry
    {
        public required int NewId { get; init; }
        public required SpiraReforgeSpellPack Pack { get; init; }
        public required string Name { get; init; }
        public required int DonorId { get; init; }
        public required Character_Enum Owner { get; init; }
        public required bool IsMenuOpener { get; init; }
    }

    public sealed class SpiraReforgeGrowOptions
    {
        public bool IncludeWards { get; init; } = true;

        /// <summary>Apply Spira Reforge vanilla offensive command balance changes in <c>command.bin</c>.</summary>
        public bool ApplyVanillaOffensiveRebalance { get; init; } = true;

        /// <summary>Set <c>CharacterUser=Rikku</c> on vanilla row #40 Copycat.</summary>
        public bool PatchCopycatExclusive { get; init; } = true;

        /// <summary>When null, append every pack. Otherwise only listed packs.</summary>
        public IReadOnlySet<SpiraReforgeSpellPack>? Packs { get; init; }
    }

    public sealed class SpiraReforgeExtendedGrowResult
    {
        public required byte[] OriginalBytes { get; init; }
        public required byte[] GrownBytes { get; init; }
        public required int OriginalEntryCount { get; init; }
        public required int NewEntryCount { get; init; }
        public required IReadOnlyList<SpiraReforgeSpellEntry> Spells { get; init; }
        public required int OriginalLength { get; init; }
        public required int GrownLength { get; init; }
        public required bool WardsIncluded { get; init; }
        public required bool VanillaOffensiveRebalanceApplied { get; init; }
        public required bool CopycatPatched { get; init; }
        public required bool ExistingRecordPayloadPreserved { get; init; }
        public required bool ExistingTextPoolPrefixPreserved { get; init; }
        public required bool RereadCountOk { get; init; }
        public required bool PreserveWriteOk { get; init; }

        public int AppendedCount => Spells.Count;
        public int SkillCount => Spells.Count(s => !s.IsMenuOpener);

        public bool Pass => ExistingRecordPayloadPreserved
            && ExistingTextPoolPrefixPreserved
            && RereadCountOk
            && PreserveWriteOk;
    }

    /// <summary>
    /// One-pass grow for all Spira Reforge extended commands: optional wards, 44 character appends,
    /// optional Copycat vanilla patch (#40).
    /// </summary>
    public static class SpiraReforgeExtendedCommandWriter
    {
        public const int CopycatVanillaId = 40;
        public const int ExpectedAppendCount = 45;
        public const int ExpectedSkillCount = 43;

        static readonly IReadOnlyDictionary<int, VanillaOffensiveBuff> VanillaOffensiveBuffs =
            new Dictionary<int, VanillaOffensiveBuff>
            {
                [6] = new(18, 100, 6),   // Delay Attack
                [7] = new(22, 100, 12),  // Delay Buster
                [8] = new(20, 60, 6),    // Sleep Attack
                [9] = new(20, 60, 6),    // Silence Attack
                [10] = new(20, 60, 6),   // Dark Attack
                [11] = new(24, 60, 10),  // Zombie Attack
                [12] = new(26, 100, 12), // Sleep Buster
                [13] = new(26, 100, 12), // Silence Buster
                [14] = new(26, 100, 12), // Dark Buster
                [15] = new(32, 90, 28),  // Triple Foul
                [16] = new(22, 80, 10),  // Power Break
                [17] = new(22, 80, 10),  // Magic Break
                [18] = new(24, 70, 14),  // Armor Break
                [19] = new(24, 70, 14),  // Mental Break
                [20] = new(20, 100, 10), // Mug
                [21] = new(12, 0, 48),   // Quick Hit: keep identity, make spam weaker and costlier
                [89] = new(48, 90, 75),  // Full Break
            };

        public static SpiraReforgeExtendedGrowResult AppendFullPack(
            byte[] vanillaBytes,
            SpiraReforgeGrowOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(vanillaBytes);
            options ??= new SpiraReforgeGrowOptions();

            List<Ability_Command> vanillaRows = Ability_Command.ReadList(vanillaBytes, hasExtraInfo: true);
            if (vanillaRows.Count != CommandBinGrowCore.VanillaRowCount)
            {
                throw new InvalidDataException(
                    $"Full pack expects exactly {CommandBinGrowCore.VanillaRowCount} vanilla rows, got {vanillaRows.Count}.");
            }

            byte[] source = vanillaBytes;
            bool wardsIncluded = false;

            if (options.IncludeWards)
            {
                CommandGrowResult wardGrow = CommandGrowWriter.AppendElementWards(vanillaBytes, hookHandlesNulStatus: true);
                if (!wardGrow.Pass)
                    throw new InvalidDataException("Nul Ward pre-grow failed preflight checks.");

                source = wardGrow.GrownBytes;
                wardsIncluded = true;
            }

            int startId = Ability_Command.ReadList(source, hasExtraInfo: true).Count;
            IReadOnlyList<SpiraReforgeSpellSpec> specs = FilterSpecs(options.Packs);
            ValidateDonors(vanillaRows, specs);

            List<Ability_Command> toAppend = BuildAppendRows(vanillaRows, specs);
            List<SpiraReforgeSpellEntry> spellLog = AssignSpellLog(startId, specs);

            byte[] grownBytes = GrowInCharacterBatches(source, specs, vanillaRows);
            int sourceRowCount = Ability_Command.ReadList(source, hasExtraInfo: true).Count;
            List<Ability_Command> verifyList = Ability_Command.ReadList(grownBytes, hasExtraInfo: true);

            bool prefixOk = PrefixMatches(source, grownBytes, sourceRowCount);
            bool preserveWriteOk = Ability_Command.WriteList(verifyList, hasExtraInfo: true).SequenceEqual(grownBytes);

            byte[] finalBytes = grownBytes;
            bool vanillaOffensiveRebalanceApplied = false;
            bool copycatPatched = false;

            if (options.ApplyVanillaOffensiveRebalance)
            {
                finalBytes = ApplyVanillaOffensiveRebalancePatch(finalBytes);
                finalBytes = ApplyKimahriOverdrivePatch(finalBytes);
                vanillaOffensiveRebalanceApplied = true;
            }

            if (options.PatchCopycatExclusive)
            {
                finalBytes = ApplyCopycatExclusivePatch(finalBytes);
                copycatPatched = true;
            }

            if (wardsIncluded)
                finalBytes = ApplyDeactivatedWardRowDescriptions(finalBytes);

            List<Ability_Command> reread = Ability_Command.ReadList(finalBytes, hasExtraInfo: true);
            if (!finalBytes.SequenceEqual(grownBytes))
                preserveWriteOk = Ability_Command.WriteList(reread, hasExtraInfo: true).SequenceEqual(finalBytes);

            return new SpiraReforgeExtendedGrowResult
            {
                OriginalBytes = vanillaBytes,
                GrownBytes = finalBytes,
                OriginalEntryCount = sourceRowCount,
                NewEntryCount = reread.Count,
                Spells = spellLog,
                OriginalLength = vanillaBytes.Length,
                GrownLength = finalBytes.Length,
                WardsIncluded = wardsIncluded,
                VanillaOffensiveRebalanceApplied = vanillaOffensiveRebalanceApplied,
                CopycatPatched = copycatPatched,
                ExistingRecordPayloadPreserved = prefixOk,
                ExistingTextPoolPrefixPreserved = prefixOk,
                RereadCountOk = reread.Count == sourceRowCount + toAppend.Count,
                PreserveWriteOk = preserveWriteOk,
            };
        }

        static byte[] GrowInCharacterBatches(
            byte[] source,
            IReadOnlyList<SpiraReforgeSpellSpec> specs,
            List<Ability_Command> vanillaRows)
        {
            byte[] current = source;
            foreach (SpiraReforgeSpellSpec spec in specs)
            {
                Ability_Command clone = vanillaRows[spec.DonorId].CloneDeep(hasExtraInfo: true);
                spec.ApplyRecipe(clone);

                CommandGrowAppendResult result = CommandBinGrowCore.AppendRows(current, [clone]);
                if (!result.Pass)
                    throw new InvalidDataException($"Grow failed for {spec.Pack}/{spec.Name} (preflight drift).");

                current = result.GrownBytes;
            }

            return current;
        }

        static IEnumerable<SpiraReforgeSpellPack> OrderedPacks(IReadOnlyList<SpiraReforgeSpellSpec> specs)
        {
            SpiraReforgeSpellPack[] order =
            [
                SpiraReforgeSpellPack.Kimahri,
                SpiraReforgeSpellPack.Lulu,
                SpiraReforgeSpellPack.Yuna,
                SpiraReforgeSpellPack.Wakka,
                SpiraReforgeSpellPack.Rikku,
                SpiraReforgeSpellPack.Tidus,
                SpiraReforgeSpellPack.Auron,
            ];

            return order.Where(p => specs.Any(s => s.Pack == p));
        }

        static List<Ability_Command> BuildAppendRows(
            List<Ability_Command> vanillaRows,
            IReadOnlyList<SpiraReforgeSpellSpec> specs)
        {
            List<Ability_Command> rows = [];
            foreach (SpiraReforgeSpellSpec spec in specs)
            {
                Ability_Command clone = vanillaRows[spec.DonorId].CloneDeep(hasExtraInfo: true);
                spec.ApplyRecipe(clone);
                rows.Add(clone);
            }

            return rows;
        }

        static List<SpiraReforgeSpellEntry> AssignSpellLog(int startId, IReadOnlyList<SpiraReforgeSpellSpec> specs)
        {
            List<SpiraReforgeSpellEntry> log = [];
            for (int i = 0; i < specs.Count; i++)
            {
                SpiraReforgeSpellSpec spec = specs[i];
                log.Add(new SpiraReforgeSpellEntry
                {
                    NewId = startId + i,
                    Pack = spec.Pack,
                    Name = spec.Name,
                    DonorId = spec.DonorId,
                    Owner = spec.Owner,
                    IsMenuOpener = spec.IsMenuOpener,
                });
            }

            return log;
        }

        static bool PrefixMatches(byte[] before, byte[] after, int prefixRowCount)
        {
            EntryListFile beforeFile = EntryListFile.Unpack(before);
            EntryListFile afterFile = EntryListFile.Unpack(after);
            int prefixBytes = prefixRowCount * CommandGrowWriter.CommandEntrySize;
            if (beforeFile.FirstFile.Length < prefixBytes || afterFile.FirstFile.Length < prefixBytes)
                return false;

            return beforeFile.FirstFile.AsSpan(0, prefixBytes).SequenceEqual(afterFile.FirstFile.AsSpan(0, prefixBytes));
        }

        public static byte[] ApplyCopycatExclusivePatch(byte[] commandBin)
        {
            List<Ability_Command> list = Ability_Command.ReadList(commandBin, hasExtraInfo: true);
            if (CopycatVanillaId >= list.Count)
                throw new InvalidDataException($"Cannot patch Copycat — file has only {list.Count} rows.");

            if (list[CopycatVanillaId].CharacterUser == Character_Enum.Rikku)
                return commandBin;

            list[CopycatVanillaId].CharacterUser = Character_Enum.Rikku;
            return Ability_Command.WriteList(list, hasExtraInfo: true);
        }

        public static byte[] ApplyVanillaOffensiveRebalancePatch(byte[] commandBin)
        {
            List<Ability_Command> list = Ability_Command.ReadList(commandBin, hasExtraInfo: true);
            foreach ((int id, VanillaOffensiveBuff buff) in VanillaOffensiveBuffs)
            {
                if (id < 0 || id >= list.Count)
                    throw new InvalidDataException($"Cannot patch vanilla command #{id}; file has only {list.Count} rows.");

                Ability_Command row = list[id];
                row.AttackPower = buff.Power;
                row.AttackAccuracy = buff.Accuracy;
                row.CostMp = buff.Mp;
            }

            return Ability_Command.WriteList(list, hasExtraInfo: true);
        }

        public static byte[] ApplyKimahriOverdrivePatch(byte[] commandBin)
        {
            List<Ability_Command> list = Ability_Command.ReadList(commandBin, hasExtraInfo: true);
            KimahriOverdriveCommandPatch.ApplyAll(list);
            return Ability_Command.WriteList(list, hasExtraInfo: true);
        }

        static byte[] ApplyDeactivatedWardRowDescriptions(byte[] commandBin)
        {
            List<Ability_Command> list = Ability_Command.ReadList(commandBin, hasExtraInfo: true);
            if (list.Count <= CommandGrowWriter.UmbralWardCommandId)
                return commandBin;

            SpiraReforgeExtendedCommandRecipes.ApplyDeactivatedRowDescription(
                list[CommandGrowWriter.RadiantWardCommandId],
                CommandGrowWriter.RadiantWardCommandId);
            SpiraReforgeExtendedCommandRecipes.ApplyDeactivatedRowDescription(
                list[CommandGrowWriter.UmbralWardCommandId],
                CommandGrowWriter.UmbralWardCommandId);
            return Ability_Command.WriteList(list, hasExtraInfo: true);
        }

        static IReadOnlyList<SpiraReforgeSpellSpec> FilterSpecs(IReadOnlySet<SpiraReforgeSpellPack>? packs)
        {
            IReadOnlyList<SpiraReforgeSpellSpec> all = SpiraReforgeExtendedCommandRecipes.GetAllAppendSpecs();
            if (packs == null || packs.Count == 0)
                return all;

            return all.Where(s => packs.Contains(s.Pack)).ToList();
        }

        static void ValidateDonors(List<Ability_Command> vanillaRows, IReadOnlyList<SpiraReforgeSpellSpec> specs)
        {
            foreach (SpiraReforgeSpellSpec spec in specs)
            {
                if (spec.DonorId < 0 || spec.DonorId >= vanillaRows.Count)
                {
                    string label = CommandCharacter_Dictionary.Instance.TryGetValue((ushort)spec.DonorId, out string? name)
                        ? name
                        : $"#{spec.DonorId}";
                    throw new InvalidDataException(
                        $"Donor {label} (#{spec.DonorId}) for {spec.Name} is out of vanilla range.");
                }
            }
        }

        readonly record struct VanillaOffensiveBuff(byte Power, byte Accuracy, byte Mp);
    }
}

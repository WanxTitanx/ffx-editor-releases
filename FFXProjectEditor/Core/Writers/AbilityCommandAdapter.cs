using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.FfxLib.Ability;

namespace FFXProjectEditor.Core.Writers
{
    /// <summary>
    /// Adapts Ability_Command.ReadList/WriteList (battle kernel: command.bin / item /
    /// monmagic a_ability.bin) to the OperationPlan pipeline.
    ///
    /// The file holds a list of fixed-size command records (0x60 with extra info,
    /// 0x5C without) plus a shared text pool. WriteList preserves the original pool
    /// byte-for-byte on a no-edit save (captured Original*Offset), so the no-op
    /// invariant is RT0-proven against vanilla fixtures.
    ///
    /// Only fixed-block fields are in the allowlist. Text script editing is
    /// intentionally NOT exposed yet: it switches WriteList to the append-rebuild
    /// path, which is a bigger semantic change and must be gated separately.
    /// </summary>
    public sealed class AbilityCommandAdapter : IWriterAdapter
    {
        public string CapabilityId => "ability-command";
        public string DisplayName => "Battle Command / Item / Monster Ability";
        public RiskLevel Risk => RiskLevel.Moderate;

        /// <summary>Index of the command record inside the file (0-based). Required.</summary>
        public const string CommandIndexField = "CommandIndex";

        private static readonly HashSet<string> EditableFields = new(StringComparer.OrdinalIgnoreCase)
        {
            CommandIndexField,
            // Fixed block (safe, byte-local, covered by round-trip tests).
            "Anim1Id", "Anim2Id", "IconId", "CasterAnimId",
            "CostMp", "CostOverdrive", "AttackCritBonus",
            "AttackAccuracy", "AttackPower", "HitCount", "ShatterChance",
            "MoveRank", "OverdriveCategory", "StatBuffValue",
            "CharacterUser", "DamageFormula", "TargetsAllowed",
            "MenuFlgs", "SubMenuCategorization", "SubSubMenuCategorization",
            "TargetFlgs", "ElementFlgs", "StatusFlgs", "StatBuffFlgs",
            "SpecialBuffFlgs", "DamageFlgs", "DamageTypeFlgs", "Misc1Flgs",
            "Misc2Flgs", "Misc3Flgs", "Misc4Flgs", "PreviewFlgs",
            // Extra info (command.bin / items only).
            "OrderingIndexInMenu", "SphereTypeForSphereGrid",
        };


        public string ComputeBeforeHash(string sourcePath)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(sourcePath);
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }

        public async Task<string> StageAsync(
            string sourcePath,
            string stagingPath,
            IReadOnlyDictionary<string, object> edits,
            CancellationToken ct = default)
        {
            byte[] originalBytes = await File.ReadAllBytesAsync(sourcePath, ct);
            bool hasExtraInfo = DetectExtraInfo(originalBytes);

            List<Ability_Command> commands = Ability_Command.ReadList(originalBytes, hasExtraInfo);
            int index = ResolveCommandIndex(edits, commands.Count);

            Ability_Command target = commands[index];

            foreach (var kvp in edits)
            {
                if (!EditableFields.Contains(kvp.Key) || kvp.Key == CommandIndexField)
                    continue;

                var prop = typeof(Ability_Command).GetProperty(kvp.Key);
                if (prop == null || !prop.CanWrite)
                    continue;

                var value = Convert.ChangeType(kvp.Value, prop.PropertyType);
                prop.SetValue(target, value);
            }

            byte[] newBytes = Ability_Command.WriteList(commands, hasExtraInfo);

            Directory.CreateDirectory(Path.GetDirectoryName(stagingPath)!);
            await File.WriteAllBytesAsync(stagingPath, newBytes, ct);

            using var sha = SHA256.Create();
            using var stream = File.OpenRead(stagingPath);
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }


        public FileDiffSummary DescribeChanges(IReadOnlyDictionary<string, object> edits)
        {
            var changed = new List<string>();
            foreach (var kvp in edits)
            {
                if (EditableFields.Contains(kvp.Key))
                    changed.Add(kvp.Key);
            }

            return new FileDiffSummary
            {
                FieldsChanged = changed.Count,
                ChangedFieldNames = changed,
                HumanSummary = "Battle command #" + ResolveRawIndex(edits) + ": " + changed.Count + " field(s) modified (" + string.Join(", ", changed) + ")"
            };
        }

        public IReadOnlyList<string> ValidateEdits(IReadOnlyDictionary<string, object> edits)
        {
            var errors = new List<string>();

            if (!edits.TryGetValue(CommandIndexField, out var indexValue))
            {
                errors.Add("'" + CommandIndexField + "' is required");
                return errors;
            }

            if (indexValue is not int index || index < 0)
            {
                errors.Add("'" + CommandIndexField + "' must be a non-negative integer");
                return errors;
            }

            foreach (var kvp in edits)
            {
                if (kvp.Key == CommandIndexField)
                    continue;

                if (!EditableFields.Contains(kvp.Key))
                {
                    errors.Add("Field '" + kvp.Key + "' is not editable on battle commands");
                    continue;
                }

                var prop = typeof(Ability_Command).GetProperty(kvp.Key);
                if (prop == null || !prop.CanWrite)
                {
                    errors.Add("Field '" + kvp.Key + "' has no writable property");
                    continue;
                }

                // Numeric range validation for the common byte/short fields.
                if (kvp.Value is byte b)
                {
                    if (b > 255)
                        errors.Add("Field '" + kvp.Key + "' value " + b + " exceeds byte range (0-255)");
                }
                else if (kvp.Value is int i)
                {
                    if (i < 0 || i > 65535)
                        errors.Add("Field '" + kvp.Key + "' value " + i + " exceeds u16 range (0-65535)");
                }
            }

            return errors;
        }

        /// <summary>Command/item files carry 4 extra info bytes (0x60 entries); monmagic does not (0x5C).
        /// EntrySize lives at header offset 12 (after Signature(1)+Unknown(7)+PreviousFileCount(2)+EntryCount(2)).
        /// monmagic (0x5C) is supported since the preserved-text-pool writer landed
        /// (no-edit round-trip proven byte-identical by AbilityCommandRoundTripTests).</summary>
        private static bool DetectExtraInfo(byte[] fileBytes)
        {
            if (fileBytes.Length < 16)
                return false;

            short entrySize = BitConverter.ToInt16(fileBytes, 12);
            short entryCount = BitConverter.ToInt16(fileBytes, 10);
            if (entryCount < 0 || entrySize <= 0)
                return false;

            return entrySize == 0x60;
        }

        private static int ResolveCommandIndex(IReadOnlyDictionary<string, object> edits, int commandCount)
        {
            int index = ResolveRawIndex(edits);
            if (index < 0 || index >= commandCount)
                throw new ArgumentOutOfRangeException(
                    nameof(edits),
                    "'" + CommandIndexField + "' " + index + " is out of range (file has " + commandCount + " commands)");
            return index;
        }

        private static int ResolveRawIndex(IReadOnlyDictionary<string, object> edits)
        {
            return edits.TryGetValue(CommandIndexField, out var v) && v is int index ? index : -1;
        }
    }
}

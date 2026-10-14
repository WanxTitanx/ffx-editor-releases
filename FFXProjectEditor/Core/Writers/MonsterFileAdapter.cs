using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.FfxLib.Monster;
using static FFXProjectEditor.FfxLib.Monster.Monster_Structs;

namespace FFXProjectEditor.Core.Writers
{
    /// <summary>
    /// Adapts <see cref="Monster_File.Read(byte[])"/>/<see cref="Monster_File.Write"/> to the
    /// OperationPlan pipeline. Operates on the whole monster_*.bin container and supports
    /// editing three namespaces (canonical prefixed edit keys):
    /// <list type="bullet">
    /// <item><c>Stat.&lt;field&gt;</c> — fixed stat sheet block (Hp, Mp, Strength, Defense, ...);</item>
    /// <item><c>Loot.&lt;field&gt;</c> — loot record (Gil, Ap, drop/steal/gear entries);</item>
    /// <item><c>Metadata.&lt;field&gt;</c> — header metadata: <c>Signature</c> (int) and raw section
    /// blobs (<c>AiFile</c>, <c>WorkerFile</c>, <c>UnkFile</c>, <c>AudioFile</c>, <c>TextFile</c>
    /// as byte[] or base64 string). Section pointers and FileSize are always recomputed by
    /// <see cref="Monster_File.Write"/>, never edited directly.</item>
    /// </list>
    /// No-edit saves reproduce the source byte-for-byte (RT0, preserve-only writers inside
    /// Monster_StatSheet/Monster_Loot); edits re-serialize the container with recomputed pointers.
    /// </summary>
    public sealed class MonsterFileAdapter : IWriterAdapter
    {
        public string CapabilityId => "monster-file";
        public string DisplayName => "Monster File";
        public RiskLevel Risk => RiskLevel.Moderate;

        private const string StatPrefix = "Stat.";
        private const string LootPrefix = "Loot.";
        private const string MetadataPrefix = "Metadata.";

        private static readonly HashSet<string> StatFields = new(StringComparer.OrdinalIgnoreCase)
        {
            "Hp", "Mp", "HpOverkill",
            "Strength", "Defense", "Magic", "MagicDefense",
            "Agility", "Luck", "Evasion", "Accuracy",
            "PoisonDamage",
            "ForcedAction", "MonsterId", "ModelId", "CtbIconId", "DoomCount",
            "ArenaId", "Model2Id"
        };

        private static readonly HashSet<string> LootFields = new(StringComparer.OrdinalIgnoreCase)
        {
            "Gil", "Ap", "ApOverkill", "RonsoRageId",
            "Drop1Chance", "Drop2Chance", "StealChance", "GearChance",
            "Drop1Id", "Drop1RareId", "Drop2Id", "Drop2RareId",
            "Drop1Count", "Drop1RareCount", "Drop2Count", "Drop2RareCount",
            "DropOverkillId", "DropOverkillRareId", "DropOverkill2Id", "DropOverkill2RareId",
            "DropOverkillCount", "DropOverkillRareCount", "DropOverkill2Count", "DropOverkill2RareCount",
            "StealId", "StealRareId", "StealCount", "StealRareCount",
            "BribeId", "BribeCount",
            "GearSlotCount", "GearFormula", "GearCrit", "GearAttack", "GearAbilityCount",
            "ZanmatoLevel", "Unk1", "Unk2", "Unk3"
        };

        private static readonly HashSet<string> MetadataSectionFields = new(StringComparer.OrdinalIgnoreCase)
        {
            "AiFile", "WorkerFile", "UnkFile", "AudioFile", "TextFile"
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

            Monster_File file = Monster_File.Read(originalBytes);

            ApplyStatEdits(file, edits);
            ApplyLootEdits(file, edits);
            ApplyMetadataEdits(file, edits);

            byte[] newBytes = file.Write();

            Directory.CreateDirectory(Path.GetDirectoryName(stagingPath)!);
            await File.WriteAllBytesAsync(stagingPath, newBytes, ct);

            using var sha = SHA256.Create();
            using var stream = File.OpenRead(stagingPath);
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }

        // --- Edit application ----------------------------------------------------

        private static void ApplyStatEdits(Monster_File file, IReadOnlyDictionary<string, object> edits)
        {
            var statEdits = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in edits)
            {
                if (kvp.Key.StartsWith(StatPrefix, StringComparison.OrdinalIgnoreCase))
                    statEdits[kvp.Key.Substring(StatPrefix.Length)] = kvp.Value;
            }
            if (statEdits.Count == 0)
                return;

            // From-scratch fallback for monsters without a stat sheet section.
            file.StatSheetFile ??= new Monster_StatSheet();

            foreach (var kvp in statEdits)
            {
                if (!StatFields.Contains(kvp.Key))
                    continue;
                ApplyProperty(file.StatSheetFile, typeof(Monster_StatSheet), kvp.Key, kvp.Value);
            }
        }

        private static void ApplyLootEdits(Monster_File file, IReadOnlyDictionary<string, object> edits)
        {
            var lootEdits = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in edits)
            {
                if (kvp.Key.StartsWith(LootPrefix, StringComparison.OrdinalIgnoreCase))
                    lootEdits[kvp.Key.Substring(LootPrefix.Length)] = kvp.Value;
            }
            if (lootEdits.Count == 0)
                return;

            // From-scratch fallback for monsters without a loot section.
            file.LootFile ??= new Monster_Loot();

            foreach (var kvp in lootEdits)
            {
                if (!LootFields.Contains(kvp.Key))
                    continue;
                ApplyProperty(file.LootFile, typeof(Monster_Loot), kvp.Key, kvp.Value);
            }
        }

        private static void ApplyMetadataEdits(Monster_File file, IReadOnlyDictionary<string, object> edits)
        {
            foreach (var kvp in edits)
            {
                if (!kvp.Key.StartsWith(MetadataPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                string field = kvp.Key.Substring(MetadataPrefix.Length);

                if (field.Equals("Signature", StringComparison.OrdinalIgnoreCase))
                {
                    file.OriginalHeader ??= new MonsterHeaderFile();
                    file.OriginalHeader.Signature = Convert.ToInt32(kvp.Value);
                    continue;
                }

                if (!MetadataSectionFields.Contains(field))
                    continue;

                byte[] sectionBytes = kvp.Value switch
                {
                    byte[] raw => raw,
                    string base64 => Convert.FromBase64String(base64),
                    _ => throw new FormatException(
                        $"Metadata.{field} must be a byte[] or a base64 string, got {kvp.Value?.GetType().Name ?? "null"}")
                };

                switch (field.ToLowerInvariant())
                {
                    case "aifile": file.AiFile = sectionBytes; break;
                    case "workerfile": file.WorkerFile = sectionBytes; break;
                    case "unkfile": file.UnkFile = sectionBytes; break;
                    case "audiofile": file.AudioFile = sectionBytes; break;
                    case "textfile": file.TextFile = sectionBytes; break;
                }
            }
        }

        private static void ApplyProperty(object target, Type type, string field, object value)
        {
            var prop = type.GetProperty(field, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            if (prop == null || !prop.CanWrite)
                return;

            var converted = Convert.ChangeType(value, prop.PropertyType);
            prop.SetValue(target, converted);
        }

        // --- Diff / validation ---------------------------------------------------

        public FileDiffSummary DescribeChanges(IReadOnlyDictionary<string, object> edits)
        {
            var changed = new List<string>();
            foreach (var kvp in edits)
            {
                if (IsEditableKey(kvp.Key))
                    changed.Add(kvp.Key);
            }

            return new FileDiffSummary
            {
                FieldsChanged = changed.Count,
                ChangedFieldNames = changed,
                HumanSummary = $"Monster file: {changed.Count} field(s) modified ({string.Join(", ", changed)})"
            };
        }

        public IReadOnlyList<string> ValidateEdits(IReadOnlyDictionary<string, object> edits)
        {
            var errors = new List<string>();

            foreach (var kvp in edits)
            {
                if (!TryClassify(kvp.Key, out string prefix, out string field))
                {
                    errors.Add(
                        $"Field '{kvp.Key}' is not editable on Monster File " +
                        $"(use 'Stat.<field>', 'Loot.<field>' or 'Metadata.<field>')");
                    continue;
                }

                if (prefix == StatPrefix)
                {
                    if (!StatFields.Contains(field))
                    {
                        errors.Add($"Field '{kvp.Key}' is not editable on Monster Stat Sheet");
                        continue;
                    }
                    ValidateScalar(kvp.Key, field, kvp.Value, typeof(Monster_StatSheet), errors);
                }
                else if (prefix == LootPrefix)
                {
                    if (!LootFields.Contains(field))
                    {
                        errors.Add($"Field '{kvp.Key}' is not editable on Monster Loot");
                        continue;
                    }
                    ValidateScalar(kvp.Key, field, kvp.Value, typeof(Monster_Loot), errors);
                }
                else // MetadataPrefix
                {
                    if (field.Equals("Signature", StringComparison.OrdinalIgnoreCase))
                    {
                        if (kvp.Value == null || !TryConvertToInt32(kvp.Value, out _))
                            errors.Add($"Field '{kvp.Key}' must be an int, got {kvp.Value?.GetType().Name ?? "null"}");
                    }
                    else if (MetadataSectionFields.Contains(field))
                    {
                        ValidateSectionBytes(kvp.Key, kvp.Value, errors);
                    }
                    else
                    {
                        errors.Add($"Field '{kvp.Key}' is not editable on Monster File metadata");
                    }
                }
            }

            return errors;
        }

        private static void ValidateScalar(
            string fullKey, string field, object value, Type ownerType, List<string> errors)
        {
            if (value == null)
            {
                errors.Add($"Field '{fullKey}' cannot be null");
                return;
            }

            // HP/MP/HP-overkill cap (matches MonsterStatSheetAdapter).
            if ((field.Equals("Hp", StringComparison.OrdinalIgnoreCase) ||
                 field.Equals("Mp", StringComparison.OrdinalIgnoreCase) ||
                 field.Equals("HpOverkill", StringComparison.OrdinalIgnoreCase)) &&
                TryGetInt64(value, out long numeric) && numeric > 99999)
            {
                errors.Add($"Field '{fullKey}' value {numeric} exceeds max HP/MP (99999)");
                return;
            }

            var prop = ownerType.GetProperty(field, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            if (prop == null || !prop.CanWrite)
            {
                errors.Add($"Field '{fullKey}' is not writable");
                return;
            }

            try
            {
                Convert.ChangeType(value, prop.PropertyType);
            }
            catch (Exception ex) when (ex is InvalidCastException or OverflowException or FormatException)
            {
                errors.Add(
                    $"Field '{fullKey}' value '{value}' is not valid for {prop.PropertyType.Name} " +
                    $"({ex.GetType().Name})");
            }
        }

        private static void ValidateSectionBytes(string fullKey, object value, List<string> errors)
        {
            if (value == null)
            {
                errors.Add($"Field '{fullKey}' cannot be null");
                return;
            }

            switch (value)
            {
                case byte[]:
                    return; // valid
                case string base64:
                    try
                    {
                        Convert.FromBase64String(base64);
                    }
                    catch (FormatException)
                    {
                        errors.Add($"Field '{fullKey}' is not a valid base64 string");
                    }
                    return;
                default:
                    errors.Add($"Field '{fullKey}' must be a byte[] or a base64 string, got {value.GetType().Name}");
                    return;
            }
        }

        // --- Helpers ---------------------------------------------------------------

        private static bool IsEditableKey(string key)
        {
            if (!TryClassify(key, out string prefix, out string field))
                return false;

            return prefix switch
            {
                _ when prefix == StatPrefix => StatFields.Contains(field),
                _ when prefix == LootPrefix => LootFields.Contains(field),
                _ => field.Equals("Signature", StringComparison.OrdinalIgnoreCase) ||
                     MetadataSectionFields.Contains(field)
            };
        }

        private static bool TryClassify(string key, out string prefix, out string field)
        {
            prefix = string.Empty;
            field = string.Empty;

            foreach (string candidate in new[] { StatPrefix, LootPrefix, MetadataPrefix })
            {
                if (key.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    prefix = candidate;
                    field = key.Substring(candidate.Length);
                    return field.Length > 0;
                }
            }

            return false;
        }

        private static bool TryGetInt64(object value, out long result)
        {
            try
            {
                result = Convert.ToInt64(value);
                return true;
            }
            catch (Exception ex) when (ex is InvalidCastException or OverflowException or FormatException)
            {
                result = 0;
                return false;
            }
        }

        private static bool TryConvertToInt32(object value, out int result)
        {
            try
            {
                result = Convert.ToInt32(value);
                return true;
            }
            catch (Exception ex) when (ex is InvalidCastException or OverflowException or FormatException)
            {
                result = 0;
                return false;
            }
        }
    }
}

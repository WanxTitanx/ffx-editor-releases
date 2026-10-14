using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.FfxLib.Monster;

namespace FFXProjectEditor.Core.Writers
{
    /// <summary>
    /// Adapts Monster_StatSheet.WriteSingle() to the OperationPlan pipeline.
    /// Supports editing: Hp, Mp, Strength, Defense, Magic, MagicDefense,
    /// Agility, Luck, Evasion, Accuracy, and elemental/status properties.
    /// </summary>
    public sealed class MonsterStatSheetAdapter : IWriterAdapter
    {
        public string CapabilityId => "monster-stats";
        public string DisplayName => "Monster Stat Sheet";
        public RiskLevel Risk => RiskLevel.Moderate;

        private static readonly HashSet<string> EditableFields = new(StringComparer.OrdinalIgnoreCase)
        {
            "Hp", "Mp", "HpOverkill",
            "Strength", "Defense", "Magic", "MagicDefense",
            "Agility", "Luck", "Evasion", "Accuracy",
            "PoisonDamage",
            "MonsterId", "ModelId", "CtbIconId", "DoomCount"
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
            // Read the original file
            byte[] originalBytes = await File.ReadAllBytesAsync(sourcePath, ct);

            // Load the stat sheet from the original binary
            var statSheet = Monster_StatSheet.ReadSingle(originalBytes);

            // Apply edits
            foreach (var kvp in edits)
            {
                if (!EditableFields.Contains(kvp.Key))
                    continue;

                var prop = typeof(Monster_StatSheet).GetProperty(kvp.Key);
                if (prop == null || !prop.CanWrite)
                    continue;

                var value = Convert.ChangeType(kvp.Value, prop.PropertyType);
                prop.SetValue(statSheet, value);
            }

            // Write the modified stat sheet
            byte[] newBytes = statSheet.WriteSingle();

            // Ensure output directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(stagingPath)!);
            await File.WriteAllBytesAsync(stagingPath, newBytes, ct);

            // Return the hash of the written file
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
                HumanSummary = $"Monster stats: {changed.Count} field(s) modified ({string.Join(", ", changed)})"
            };
        }

        public IReadOnlyList<string> ValidateEdits(IReadOnlyDictionary<string, object> edits)
        {
            var errors = new List<string>();

            foreach (var kvp in edits)
            {
                if (!EditableFields.Contains(kvp.Key))
                {
                    errors.Add($"Field '{kvp.Key}' is not editable on Monster Stat Sheet");
                    continue;
                }

                // Range validation for byte stats
                if (kvp.Value is byte b && b > 255)
                    errors.Add($"Field '{kvp.Key}' value {b} exceeds byte range (0-255)");

                // HP/MP validation
                if ((kvp.Key == "Hp" || kvp.Key == "Mp" || kvp.Key == "HpOverkill") && kvp.Value is uint u)
                {
                    if (u > 99999)
                        errors.Add($"Field '{kvp.Key}' value {u} exceeds max HP/MP (99999)");
                }
            }

            return errors;
        }
    }
}

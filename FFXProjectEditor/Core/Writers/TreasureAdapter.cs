using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.FfxLib.Treasure;

namespace FFXProjectEditor.Core.Writers
{
    /// <summary>
    /// Adapts Treasure_File.WriteSingle()/WriteAll() to the OperationPlan pipeline.
    /// Supports editing: Kind, Quantity, Type of individual treasure entries.
    /// </summary>
    public sealed class TreasureAdapter : IWriterAdapter
    {
        public string CapabilityId => "treasure-editor";
        public string DisplayName => "Treasure Table";
        public RiskLevel Risk => RiskLevel.Safe;

        private static readonly HashSet<string> EditableFields = new(StringComparer.OrdinalIgnoreCase)
        {
            "Kind", "Quantity", "Type"
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

            // Parse all entries
            var entries = Treasure_File.ReadAll(originalBytes);

            // Apply edits to each entry (or target specific index)
            if (edits.TryGetValue("_entryIndex", out var idxObj) && idxObj is int idx && idx < entries.Count)
            {
                ApplyEditsToEntry(entries[idx], edits);
            }
            else
            {
                // Apply to all entries
                foreach (var entry in entries)
                    ApplyEditsToEntry(entry, edits);
            }

            // Write back
            byte[] newBytes = Treasure_File.WriteAll(entries);

            Directory.CreateDirectory(Path.GetDirectoryName(stagingPath)!);
            await File.WriteAllBytesAsync(stagingPath, newBytes, ct);

            using var sha = SHA256.Create();
            using var stream = File.OpenRead(stagingPath);
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }

        private static void ApplyEditsToEntry(Treasure_Entry entry, IReadOnlyDictionary<string, object> edits)
        {
            foreach (var kvp in edits)
            {
                if (kvp.Key.StartsWith("_"))
                    continue;
                if (!EditableFields.Contains(kvp.Key))
                    continue;

                var prop = typeof(Treasure_Entry).GetProperty(kvp.Key);
                if (prop != null && prop.CanWrite)
                {
                    var value = Convert.ChangeType(kvp.Value, prop.PropertyType);
                    prop.SetValue(entry, value);
                }
            }
        }

        public FileDiffSummary DescribeChanges(IReadOnlyDictionary<string, object> edits)
        {
            var changed = new List<string>();
            foreach (var kvp in edits)
            {
                if (!kvp.Key.StartsWith("_") && EditableFields.Contains(kvp.Key))
                    changed.Add(kvp.Key);
            }

            return new FileDiffSummary
            {
                FieldsChanged = changed.Count,
                ChangedFieldNames = changed,
                HumanSummary = $"Treasure: {changed.Count} field(s) modified ({string.Join(", ", changed)})"
            };
        }

        public IReadOnlyList<string> ValidateEdits(IReadOnlyDictionary<string, object> edits)
        {
            var errors = new List<string>();
            foreach (var kvp in edits)
            {
                if (kvp.Key.StartsWith("_"))
                    continue;
                if (!EditableFields.Contains(kvp.Key))
                    errors.Add($"Field '{kvp.Key}' is not editable on Treasure entries");
            }
            return errors;
        }
    }
}

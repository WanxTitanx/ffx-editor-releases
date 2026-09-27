using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.Core.Writers
{
    /// <summary>
    /// Adapts TextTable_File.Read()/Write() to the OperationPlan pipeline.
    /// Supports editing: RegularText and SimplifiedText of individual entries,
    /// targeted by the _entryIndex key (0-based).
    /// </summary>
    public sealed class TextTableAdapter : IWriterAdapter
    {
        public string CapabilityId => "text-table";
        public string DisplayName => "Text Table (Field-String)";
        public RiskLevel Risk => RiskLevel.Safe;

        private static readonly HashSet<string> EditableFields = new(StringComparer.OrdinalIgnoreCase)
        {
            "RegularText", "SimplifiedText"
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

            // Parse the text table using the US decoder (standard for FFX International / Steam)
            var decoder = FfxEncoding.UsDecoder;
            var textTable = TextTable_File.Read(originalBytes, decoder);

            // Determine which entry to edit
            if (!edits.TryGetValue("_entryIndex", out var idxObj) || idxObj is not int idx)
                throw new InvalidOperationException(
                    "TextTableAdapter requires an '_entryIndex' key (int) to identify which entry to edit.");

            if (idx < 0 || idx >= textTable.EntryCount)
                throw new ArgumentOutOfRangeException(nameof(edits),
                    $"Entry index {idx} is out of range (0..{textTable.EntryCount - 1}).");

            var entry = textTable.Entries[idx];

            // Apply text edits to the targeted entry
            foreach (var kvp in edits)
            {
                if (kvp.Key.StartsWith("_"))
                    continue;

                if (!EditableFields.Contains(kvp.Key))
                    continue;

                switch (kvp.Key, kvp.Value)
                {
                    case ("RegularText", string text):
                        entry.RegularText = text;
                        break;
                    case ("SimplifiedText", string text):
                        entry.SimplifiedText = text;
                        break;
                }
            }

            // Write back
            byte[] newBytes = textTable.Write(decoder);

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
                if (!kvp.Key.StartsWith("_") && EditableFields.Contains(kvp.Key))
                    changed.Add(kvp.Key);
            }

            return new FileDiffSummary
            {
                FieldsChanged = changed.Count,
                ChangedFieldNames = changed,
                HumanSummary = $"Text table: {changed.Count} field(s) modified ({string.Join(", ", changed)})"
            };
        }

        public IReadOnlyList<string> ValidateEdits(IReadOnlyDictionary<string, object> edits)
        {
            var errors = new List<string>();
            bool hasEntryIndex = false;

            foreach (var kvp in edits)
            {
                if (kvp.Key == "_entryIndex")
                {
                    hasEntryIndex = true;
                    if (kvp.Value is not int)
                        errors.Add("'_entryIndex' must be an int (0-based entry index)");
                    continue;
                }

                if (kvp.Key.StartsWith("_"))
                    continue;

                if (!EditableFields.Contains(kvp.Key))
                    errors.Add($"Field '{kvp.Key}' is not editable on Text Table entries");
            }

            if (!hasEntryIndex)
                errors.Add("Missing required '_entryIndex' key (0-based entry index to edit)");

            return errors;
        }
    }
}

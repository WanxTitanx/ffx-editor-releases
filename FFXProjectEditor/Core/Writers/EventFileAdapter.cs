using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.FfxLib.Event;
using FFXProjectEditor.FfxLib.Text;

namespace FFXProjectEditor.Core.Writers
{
    /// <summary>
    /// Adapts <see cref="Event_File"/> (EV01 *.ebp container) to the OperationPlan pipeline.
    /// Supports editing:
    /// <list type="bullet">
    /// <item>Japanese / English text entries by string index (JpText_NN / EnText_NN).</item>
    /// <item>ATEL script chunk replacement (ScriptOverride).</item>
    /// </list>
    /// The Tier 1 writer re-packs the container, re-emitting only edited text chunks and
    /// preserving every other chunk byte-for-byte. A no-edit save is byte-identical.
    /// </summary>
    public sealed class EventFileAdapter : IWriterAdapter
    {
        public string CapabilityId => "event-file";
        public string DisplayName => "Event File (.ebp)";
        public RiskLevel Risk => RiskLevel.Moderate;

        /// <summary>Regex matching JpText_0, EnText_12, etc.</summary>
        private static readonly Regex TextEntryKey = new(@"^(Jp|En)Text_(\d+)$", RegexOptions.Compiled);

        /// <summary>Maximum valid string index for a text chunk (0-based).</summary>
        private const int MaxStringIndex = 0xFFFF;

        // -------------------------------------------------------------------
        // IWriterAdapter
        // -------------------------------------------------------------------

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
            string eventId = Path.GetFileNameWithoutExtension(sourcePath);
            var evt = Event_File.Read(eventId, originalBytes);

            // Apply text edits: JpText_N / EnText_N → set RegularText on the matching entry.
            foreach (var kvp in edits)
            {
                var match = TextEntryKey.Match(kvp.Key);
                if (!match.Success)
                    continue;

                string lang = match.Groups[1].Value; // "Jp" or "En"
                int index = int.Parse(match.Groups[2].Value);
                string newText = Convert.ToString(kvp.Value) ?? string.Empty;

                TextTable_File? table = lang == "Jp" ? evt.JapaneseTable : evt.EnglishTable;
                if (table == null)
                    continue; // chunk absent — silently skip (validated separately)

                if (index >= 0 && index < table.Entries.Count)
                {
                    table.Entries[index].RegularText = newText;
                    table.Entries[index].SimplifiedText = newText;
                }
            }

            // Apply script override: ScriptOverride → byte[]
            if (edits.TryGetValue("ScriptOverride", out var scriptObj) && scriptObj is byte[] scriptBytes)
            {
                evt.ScriptChunkOverride = scriptBytes;
            }

            // Write the modified container.
            byte[] newBytes = evt.Write();

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
                if (TextEntryKey.IsMatch(kvp.Key))
                    changed.Add(kvp.Key);
                else if (kvp.Key == "ScriptOverride")
                    changed.Add("ATEL Script");
            }

            return new FileDiffSummary
            {
                FieldsChanged = changed.Count,
                ChangedFieldNames = changed,
                HumanSummary = $"Event file: {changed.Count} field(s) modified ({string.Join(", ", changed)})"
            };
        }

        public IReadOnlyList<string> ValidateEdits(IReadOnlyDictionary<string, object> edits)
        {
            var errors = new List<string>();

            foreach (var kvp in edits)
            {
                var match = TextEntryKey.Match(kvp.Key);
                if (match.Success)
                {
                    int index = int.Parse(match.Groups[2].Value);
                    if (index < 0 || index > MaxStringIndex)
                        errors.Add($"String index {index} out of range (0..{MaxStringIndex})");

                    if (kvp.Value is not string)
                        errors.Add($"Text value for '{kvp.Key}' must be a string");
                }
                else if (kvp.Key == "ScriptOverride")
                {
                    if (kvp.Value is not byte[])
                        errors.Add("ScriptOverride value must be byte[]");
                }
                else
                {
                    errors.Add($"Unknown edit key '{kvp.Key}'. Valid keys: JpText_N, EnText_N, ScriptOverride");
                }
            }

            return errors;
        }
    }
}

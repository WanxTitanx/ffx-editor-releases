using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.FfxLib.SphereGrid;

namespace FFXProjectEditor.Core.Writers
{
    /// <summary>
    /// Adapts SphereGrid_File.WriteIdentity() to the OperationPlan pipeline.
    /// Supports editing sphere-type scalar fields: Behavior, Activates,
    /// Range, SpecialRole, Reserved0x0E (Fahrenheit/Ghidra `Sphere` naming,
    /// cross-confirmed against vanilla corpus 2026-07-31).
    /// Edit keys use "Index.Field" notation (e.g. "1.Behavior") for per-entry edits,
    /// or bare field names to apply to all entries.
    /// </summary>
    public sealed class SphereGridAdapter : IWriterAdapter
    {
        public string CapabilityId => "sphere-grid";
        public string DisplayName => "Sphere Grid";
        public RiskLevel Risk => RiskLevel.High;

        private static readonly HashSet<string> EditableFields = new(StringComparer.OrdinalIgnoreCase)
        {
            "Behavior", "Activates", "Range", "SpecialRole", "Reserved0x0E"
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
            // Determine JP/US paths: sourcePath = JP (required), a sibling "us" file = US (optional).
            string jpPath = sourcePath;
            string? usPath = ResolveUsPath(sourcePath);

            // Read the sphere-type table
            var table = SphereGrid_File.ReadSphereTypes(jpPath, usPath);

            // Apply edits to cloned entries
            var modifiedEntries = ApplyEdits(table.Entries, edits);

            // Create a new table with modified entries
            var modifiedTable = new SphereGridSphereTypeTable
            {
                JpPath = table.JpPath,
                UsPath = table.UsPath,
                JpOriginalBytes = table.JpOriginalBytes,
                UsOriginalBytes = table.UsOriginalBytes,
                Header = table.Header,
                Entries = modifiedEntries
            };

            // Write via WriteIdentity (byte-faithful preserve-only writer)
            byte[] newBytes = SphereGrid_File.WriteIdentity(modifiedTable);

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
                var fieldName = ExtractFieldName(kvp.Key);
                if (EditableFields.Contains(fieldName) && !changed.Contains(fieldName))
                    changed.Add(fieldName);
            }

            return new FileDiffSummary
            {
                FieldsChanged = changed.Count,
                ChangedFieldNames = changed,
                HumanSummary = $"Sphere Grid: {changed.Count} field(s) modified ({string.Join(", ", changed)})"
            };
        }

        public IReadOnlyList<string> ValidateEdits(IReadOnlyDictionary<string, object> edits)
        {
            var errors = new List<string>();

            foreach (var kvp in edits)
            {
                var fieldName = ExtractFieldName(kvp.Key);

                if (!EditableFields.Contains(fieldName))
                {
                    errors.Add($"Field '{fieldName}' is not editable on Sphere Grid");
                    continue;
                }

                // Range validation for Behavior (ushort) — vanilla corpus proves only 0..2 (None/Activator/Modifier).
                if (fieldName.Equals("Behavior", StringComparison.OrdinalIgnoreCase) && kvp.Value is ushort behavior)
                {
                    if (behavior > 0x0002)
                        errors.Add($"Behavior 0x{behavior:X4}h is outside known range (0x0000-0x0002: None/Activator/Modifier)");
                }

                // Range validation for Activates (ushort)
                if (fieldName.Equals("Activates", StringComparison.OrdinalIgnoreCase) && kvp.Value is ushort activates)
                {
                    if (activates > 0xFFFF)
                        errors.Add($"Activates value {activates} exceeds ushort range");
                }

                // Range validation for Range (byte) — vanilla corpus proves 0x00/0x01/0x20.
                if (fieldName.Equals("Range", StringComparison.OrdinalIgnoreCase) && kvp.Value is byte range)
                {
                    if (range > 0x20)
                        errors.Add($"Range 0x{range:X2}h is outside known range (0x00-0x20: None/Normal/Unlimited)");
                }

                // Range validation for SpecialRole (byte) — vanilla corpus uses 0..24 (Key L1..L4 = 8..11, modifiers 1..7, extras up to 24).
                if (fieldName.Equals("SpecialRole", StringComparison.OrdinalIgnoreCase) && kvp.Value is byte role)
                {
                    if (role > 0x1F)
                        errors.Add($"SpecialRole 0x{role:X2}h is outside known range (0x00-0x1F)");
                }

                // Range validation for Reserved0x0E (ushort)
                if (fieldName.Equals("Reserved0x0E", StringComparison.OrdinalIgnoreCase) && kvp.Value is ushort zero)
                {
                    if (zero != 0)
                        errors.Add($"Reserved0x0E should be 0, got {zero}");
                }
            }

            return errors;
        }

        /// <summary>
        /// Resolves the US locale file path given a JP path.
        /// Convention: JP file ends with "_jp.bin" or "jp.bin"; US file replaces that segment.
        /// Returns null if the US file does not exist.
        /// </summary>
        private static string? ResolveUsPath(string jpPath)
        {
            string dir = Path.GetDirectoryName(jpPath) ?? ".";
            string name = Path.GetFileNameWithoutExtension(jpPath);
            string ext = Path.GetExtension(jpPath);

            // Try common naming: sphere_jp.bin -> sphere_us.bin, spherejp.bin -> sphereus.bin
            string[] usCandidates;
            if (name.EndsWith("_jp", StringComparison.OrdinalIgnoreCase))
            {
                string baseName = name[..^3]; // remove "_jp"
                usCandidates = new[] { Path.Combine(dir, baseName + "_us" + ext) };
            }
            else if (name.EndsWith("jp", StringComparison.OrdinalIgnoreCase) && name.Length > 2 && char.IsLower(name[^3]))
            {
                string baseName = name[..^2]; // remove "jp"
                usCandidates = new[] { Path.Combine(dir, baseName + "us" + ext) };
            }
            else
            {
                usCandidates = Array.Empty<string>();
            }

            foreach (string candidate in usCandidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        /// <summary>
        /// Parses the edit key to extract the field name.
        /// Supports "Index.Field" format (e.g. "12.Behavior") and bare field names.
        /// </summary>
        private static string ExtractFieldName(string editKey)
        {
            int dotIndex = editKey.IndexOf('.');
            return dotIndex > 0 ? editKey[(dotIndex + 1)..] : editKey;
        }

        /// <summary>
        /// Parses the edit key to extract the entry index.
        /// Returns the 0-based index if the key uses "Index.Field" format, or null for bare field names.
        /// </summary>
        private static int? ExtractEntryIndex(string editKey)
        {
            int dotIndex = editIndexof(editKey);
            if (dotIndex <= 0)
                return null;

            string indexPart = editKey[..dotIndex];
            if (int.TryParse(indexPart, out int index))
                return index;

            return null;
        }

        private static int editIndexof(string s)
        {
            return s.IndexOf('.');
        }

        /// <summary>
        /// Applies edits to a list of sphere-type entries, returning new entries with modifications.
        /// </summary>
        private IReadOnlyList<SphereGridSphereTypeEntry> ApplyEdits(
            IReadOnlyList<SphereGridSphereTypeEntry> entries,
            IReadOnlyDictionary<string, object> edits)
        {
            // Group edits by entry index
            var editsByEntry = new Dictionary<int, Dictionary<string, object>>();
            var globalEdits = new Dictionary<string, object>();

            foreach (var kvp in edits)
            {
                int? entryIndex = ExtractEntryIndex(kvp.Key);
                var fieldName = ExtractFieldName(kvp.Key);

                if (!EditableFields.Contains(fieldName))
                    continue;

                if (entryIndex.HasValue)
                {
                    if (!editsByEntry.TryGetValue(entryIndex.Value, out var entryEdits))
                    {
                        entryEdits = new Dictionary<string, object>();
                        editsByEntry[entryIndex.Value] = entryEdits;
                    }
                    entryEdits[fieldName] = kvp.Value;
                }
                else
                {
                    globalEdits[fieldName] = kvp.Value;
                }
            }

            var result = new List<SphereGridSphereTypeEntry>(entries.Count);

            foreach (var entry in entries)
            {
                // Merge global edits with per-entry edits (per-entry wins)
                var merged = new Dictionary<string, object>(globalEdits, StringComparer.OrdinalIgnoreCase);
                if (editsByEntry.TryGetValue(entry.Index, out var specificEdits))
                {
                    foreach (var kvp in specificEdits)
                        merged[kvp.Key] = kvp.Value;
                }

                if (merged.Count == 0)
                {
                    result.Add(entry);
                    continue;
                }

                result.Add(new SphereGridSphereTypeEntry
                {
                    Index = entry.Index,
                    Description = entry.Description,
                    SimplifiedDescription = entry.SimplifiedDescription,
                    Behavior = TryGetUShort(merged, "Behavior", entry.Behavior),
                    Activates = TryGetUShort(merged, "Activates", entry.Activates),
                    Range = TryGetByte(merged, "Range", entry.Range),
                    SpecialRole = TryGetByte(merged, "SpecialRole", entry.SpecialRole),
                    Reserved0x0E = TryGetUShort(merged, "Reserved0x0E", entry.Reserved0x0E),
                    RawBytes = entry.RawBytes
                });
            }

            return result;
        }

        private static ushort TryGetUShort(IReadOnlyDictionary<string, object> edits, string field, ushort fallback)
        {
            if (edits.TryGetValue(field, out var value))
            {
                return Convert.ToUInt16(value);
            }
            return fallback;
        }

        private static byte TryGetByte(IReadOnlyDictionary<string, object> edits, string field, byte fallback)
        {
            if (edits.TryGetValue(field, out var value))
            {
                return Convert.ToByte(value);
            }
            return fallback;
        }
    }
}

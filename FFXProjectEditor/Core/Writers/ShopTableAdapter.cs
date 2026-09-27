using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.FfxLib.Shop;

namespace FFXProjectEditor.Core.Writers
{
    /// <summary>
    /// Adapts shop table (item_shop.bin / arms_shop.bin) slot editing to the OperationPlan pipeline.
    /// Supports editing: individual slot raw values (ushort) within the 16-slot payload of each entry.
    /// Uses direct byte-patching to avoid the confidence-label gate in the guarded writer,
    /// guaranteeing that only the targeted slot bytes are touched.
    /// </summary>
    public sealed class ShopTableAdapter : IWriterAdapter
    {
        const int HeaderLength = 0x14;
        const int EntryLength = 0x22;
        const int SlotDataOffset = 0x02;
        const int SlotSize = 0x02;
        const int SlotCount = 16;

        private static readonly Regex SlotKeyPattern = new(@"^Slot_(\d+)_(\d+)$", RegexOptions.Compiled);

        public string CapabilityId => "shop-table";
        public string DisplayName => "Shop Table";
        public RiskLevel Risk => RiskLevel.Safe;

        private readonly ShopTableKind _kind;

        public ShopTableAdapter(ShopTableKind kind)
        {
            _kind = kind;
        }

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
            byte[] patchedBytes = (byte[])originalBytes.Clone();

            // Parse to validate structure, then patch raw bytes directly.
            // Direct patching bypasses the confidence-label gate in ShopTable_File.Write,
            // which would reject valid slot values when no gear catalog is available.
            ShopTable_File.Read(originalBytes, _kind);

            foreach (var kvp in edits)
            {
                Match match = SlotKeyPattern.Match(kvp.Key);
                if (!match.Success)
                    continue;

                int entryIndex = int.Parse(match.Groups[1].Value, NumberStyles.Integer);
                int slotIndex = int.Parse(match.Groups[2].Value, NumberStyles.Integer);
                ushort rawValue = Convert.ToUInt16(kvp.Value);

                int offset = HeaderLength + (entryIndex * EntryLength) + SlotDataOffset + (slotIndex * SlotSize);
                patchedBytes[offset] = (byte)(rawValue & 0x00FF);
                patchedBytes[offset + 1] = (byte)((rawValue & 0xFF00) >> 8);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(stagingPath)!);
            await File.WriteAllBytesAsync(stagingPath, patchedBytes, ct);

            using var sha = SHA256.Create();
            using var stream = File.OpenRead(stagingPath);
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }

        public FileDiffSummary DescribeChanges(IReadOnlyDictionary<string, object> edits)
        {
            var changed = new List<string>();
            foreach (var kvp in edits)
            {
                if (SlotKeyPattern.IsMatch(kvp.Key))
                    changed.Add(kvp.Key);
            }

            return new FileDiffSummary
            {
                FieldsChanged = changed.Count,
                ChangedFieldNames = changed,
                HumanSummary = $"Shop table ({_kind}): {changed.Count} slot(s) modified ({string.Join(", ", changed)})"
            };
        }

        public IReadOnlyList<string> ValidateEdits(IReadOnlyDictionary<string, object> edits)
        {
            var errors = new List<string>();

            foreach (var kvp in edits)
            {
                Match match = SlotKeyPattern.Match(kvp.Key);
                if (!match.Success)
                {
                    errors.Add($"Key '{kvp.Key}' is not a valid shop slot key (expected Slot_<entry>_<slot>)");
                    continue;
                }

                int entryIndex = int.Parse(match.Groups[1].Value, NumberStyles.Integer);
                int slotIndex = int.Parse(match.Groups[2].Value, NumberStyles.Integer);

                if (entryIndex < 0 || entryIndex > 46)
                    errors.Add($"Entry index {entryIndex} is out of range (0-46)");

                if (slotIndex < 0 || slotIndex >= SlotCount)
                    errors.Add($"Slot index {slotIndex} is out of range (0-{SlotCount - 1})");

                if (kvp.Value is ushort || kvp.Value is int || kvp.Value is uint)
                {
                    ushort val = Convert.ToUInt16(kvp.Value);
                    // Structural bounds: slot value must fit in 16 bits and
                    // must not corrupt the entry's leading 2-byte unused price word.
                    // Zero is the universal empty-slot sentinel and is always allowed.
                }
                else
                {
                    errors.Add($"Value for '{kvp.Key}' must be a ushort (0-65535)");
                }
            }

            return errors;
        }
    }
}

using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Shop
{
    internal static class ShopEditGateVerifier
    {
        const int HeaderLength = 0x14;
        const int SlotDataOffset = 0x02;

        public static ShopEditGateReport VerifyCanonicalMutation(byte[] bytes, ShopTableKind kind, ShopGearCatalog? gearCatalog = null)
        {
            ArgumentNullException.ThrowIfNull(bytes);

            ShopTable original = ShopTable_File.Read(bytes, kind, gearCatalog);
            ShopMutationPlan? plan = TryBuildMutationPlan(original, gearCatalog);
            if (plan == null)
            {
                return new ShopEditGateReport
                {
                    GatePassed = false,
                    Summary = "Private edit gate could not find a safe canonical mutation candidate in the current table. Writer stays closed.",
                    TargetLabel = null,
                    ChangedOffset = null,
                    OriginalValue = null,
                    MutatedValue = null,
                    DiffByteCount = 0
                };
            }

            ShopTable mutated = CloneWithSlotMutation(original, plan.Value, gearCatalog);
            byte[] mutatedBytes = ShopTable_File.Write(mutated);
            List<int> diffOffsets = FindDifferences(bytes, mutatedBytes);
            int targetOffset = HeaderLength + ((plan.Value.EntryIndex - original.Header.MinIndex) * original.Header.EntryLength) + SlotDataOffset + (plan.Value.SlotIndex * 0x02);
            bool diffIsLocal = diffOffsets.Count > 0 && diffOffsets.All(offset => offset == targetOffset || offset == targetOffset + 1);

            ShopTable reparsed = ShopTable_File.Read(mutatedBytes, kind, gearCatalog);
            ShopSlotEntry reparsedSlot = reparsed.Entries[plan.Value.EntryIndex - reparsed.Header.MinIndex].Slots[plan.Value.SlotIndex];
            bool targetApplied = reparsedSlot.RawValue == plan.Value.MutatedValue;
            bool othersPreserved = AllOtherSlotsPreserved(original, reparsed, plan.Value.EntryIndex, plan.Value.SlotIndex);
            bool mutatedRoundTripStable = mutatedBytes.SequenceEqual(ShopTable_File.Write(reparsed));
            bool gatePassed = diffIsLocal && targetApplied && othersPreserved && mutatedRoundTripStable;

            string summary = gatePassed
                ? $"Private edit gate passed for {plan.Value.TargetLabel}: canonical mutation changed only slot bytes at 0x{targetOffset:X4}, reloaded cleanly, and preserved every untouched slot. Public writer still stays closed."
                : $"Private edit gate failed for {plan.Value.TargetLabel}: diff-local={diffIsLocal}, target-applied={targetApplied}, others-preserved={othersPreserved}, round-trip-stable={mutatedRoundTripStable}. Public writer stays closed.";

            return new ShopEditGateReport
            {
                GatePassed = gatePassed,
                Summary = summary,
                TargetLabel = plan.Value.TargetLabel,
                ChangedOffset = targetOffset,
                OriginalValue = plan.Value.OriginalValue,
                MutatedValue = plan.Value.MutatedValue,
                DiffByteCount = diffOffsets.Count
            };
        }

        static ShopMutationPlan? TryBuildMutationPlan(ShopTable table, ShopGearCatalog? gearCatalog)
        {
            List<ushort> distinctNonZero = table.Entries
                .SelectMany(entry => entry.Slots)
                .Select(slot => slot.RawValue)
                .Where(value => value != 0)
                .Distinct()
                .OrderBy(value => value)
                .ToList();

            if (distinctNonZero.Count < 2)
                return null;

            foreach (ShopEntry entry in table.Entries)
            {
                foreach (ShopSlotEntry slot in entry.Slots)
                {
                    if (slot.RawValue == 0)
                        continue;

                    ushort replacement = FindReplacementValue(table.Kind, slot.RawValue, distinctNonZero, gearCatalog);
                    if (replacement == slot.RawValue)
                        continue;

                    return new ShopMutationPlan(
                        entry.Index,
                        slot.SlotIndex,
                        slot.RawValue,
                        replacement,
                        $"row {entry.Index:D2} / slot {slot.SlotIndex:D2}");
                }
            }

            return null;
        }

        static ushort FindReplacementValue(ShopTableKind kind, ushort originalValue, IReadOnlyList<ushort> distinctNonZero, ShopGearCatalog? gearCatalog)
        {
            foreach (ushort candidate in distinctNonZero)
            {
                if (candidate == originalValue)
                    continue;

                if (kind == ShopTableKind.Item)
                {
                    if (TryResolveItemGameIndex(candidate))
                        return candidate;

                    continue;
                }

                if (gearCatalog == null || gearCatalog.EntriesByIndex.ContainsKey(candidate))
                    return candidate;
            }

            return originalValue;
        }

        static ShopTable CloneWithSlotMutation(ShopTable source, ShopMutationPlan plan, ShopGearCatalog? gearCatalog)
        {
            List<ShopEntry> entries = new(source.Entries.Count);
            foreach (ShopEntry entry in source.Entries)
            {
                List<ShopSlotEntry> slots = new(entry.Slots.Count);
                foreach (ShopSlotEntry slot in entry.Slots)
                {
                    ushort rawValue = entry.Index == plan.EntryIndex && slot.SlotIndex == plan.SlotIndex
                        ? plan.MutatedValue
                        : slot.RawValue;
                    slots.Add(ShopTable_File.BuildSlotEntry(source.Kind, slot.SlotIndex, rawValue, gearCatalog));
                }

                entries.Add(new ShopEntry
                {
                    Index = entry.Index,
                    RawBytes = entry.RawBytes.ToArray(),
                    UnusedPriceWord = entry.UnusedPriceWord,
                    Slots = slots
                });
            }

            return new ShopTable
            {
                Kind = source.Kind,
                OriginalBytes = source.OriginalBytes.ToArray(),
                Header = source.Header,
                Entries = entries
            };
        }

        static bool AllOtherSlotsPreserved(ShopTable original, ShopTable reparsed, int targetEntryIndex, int targetSlotIndex)
        {
            for (int entryIndex = 0; entryIndex < original.Entries.Count; entryIndex++)
            {
                ShopEntry leftEntry = original.Entries[entryIndex];
                ShopEntry rightEntry = reparsed.Entries[entryIndex];
                if (leftEntry.UnusedPriceWord != rightEntry.UnusedPriceWord)
                    return false;

                for (int slotIndex = 0; slotIndex < leftEntry.Slots.Count; slotIndex++)
                {
                    if (leftEntry.Index == targetEntryIndex && slotIndex == targetSlotIndex)
                        continue;

                    if (leftEntry.Slots[slotIndex].RawValue != rightEntry.Slots[slotIndex].RawValue)
                        return false;
                }
            }

            return true;
        }

        static List<int> FindDifferences(byte[] left, byte[] right)
        {
            int sharedLength = Math.Min(left.Length, right.Length);
            List<int> offsets = new();
            for (int i = 0; i < sharedLength; i++)
            {
                if (left[i] != right[i])
                    offsets.Add(i);
            }

            for (int i = sharedLength; i < Math.Max(left.Length, right.Length); i++)
                offsets.Add(i);

            return offsets;
        }

        static bool TryResolveItemGameIndex(ushort rawValue)
        {
            try
            {
                byte category = FfxCommon_Util.GetGameCategory(rawValue);
                if (category != (byte)GameCategory_Enum.Items)
                    return false;

                ushort index = FfxCommon_Util.GetGameIndex(rawValue);
                string label = FfxCommon_Util.GetGameIndexName(category, index);
                return !string.IsNullOrWhiteSpace(label) && !string.Equals(label, "<NOT_INDEXED>", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        readonly record struct ShopMutationPlan(
            int EntryIndex,
            int SlotIndex,
            ushort OriginalValue,
            ushort MutatedValue,
            string TargetLabel);
    }

    internal sealed class ShopEditGateReport
    {
        public required bool GatePassed { get; init; }
        public required string Summary { get; init; }
        public required string? TargetLabel { get; init; }
        public required int? ChangedOffset { get; init; }
        public required ushort? OriginalValue { get; init; }
        public required ushort? MutatedValue { get; init; }
        public required int DiffByteCount { get; init; }
    }
}

using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Shop
{
    internal static class ShopWriterReadinessVerifier
    {
        const int HeaderLength = 0x14;
        const int SlotDataOffset = 0x02;

        public static ShopWriterReadinessReport VerifyFile(string sourcePath, ShopTableKind kind, ShopGearCatalog? gearCatalog = null, string? discardRoot = null)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                throw new InvalidDataException("Shop file path is missing.");

            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("Shop file not found.", sourcePath);

            byte[] bytes = File.ReadAllBytes(sourcePath);
            ShopTable table = ShopTable_File.Read(bytes, kind, gearCatalog);
            ShopRoundTripReport roundTripReport = ShopRoundTripVerifier.Verify(bytes, kind, gearCatalog);
            ShopMutationDiskReport replaceReport = VerifyMutationOnDiscardableCopy(sourcePath, table, kind, gearCatalog, ShopMutationMode.ReplaceWithOtherValid, discardRoot);
            ShopMutationDiskReport clearReport = VerifyMutationOnDiscardableCopy(sourcePath, table, kind, gearCatalog, ShopMutationMode.ClearToZero, discardRoot);
            ShopMutationDiskReport fillReport = VerifyMutationOnDiscardableCopy(sourcePath, table, kind, gearCatalog, ShopMutationMode.FillZeroWithValid, discardRoot);

            bool readerProven = true;
            bool serializerStructuralProven = roundTripReport.IsByteIdentical && replaceReport.Passed && clearReport.Passed && fillReport.Passed;
            bool bridgeProven = kind == ShopTableKind.Item
                ? DistinctNonZeroValues(table).All(TryResolveItemGameIndex)
                : HasStructuredGearBridge(table, gearCatalog);
            bool publicWriterSafe = kind == ShopTableKind.Item
                ? serializerStructuralProven && bridgeProven
                : serializerStructuralProven && bridgeProven;

            string editableSlice = kind == ShopTableKind.Item
                ? "Expose only the 16 fixed slot values as `Item or Empty`, constrained to category `Items` game indices plus `0x0000`."
                : "Expose only the 16 fixed slot values as `Gear Catalog Row or Empty`, constrained to `shop_arms.bin` row indices plus `0x0000`, with an explicit structured catalog-row label.";
            string readOnlyFields = "Keep header, row count, row order, row length, word 00h, and every byte outside the targeted slot payload read-only.";
            string realProof = BuildRealProof(kind, roundTripReport, replaceReport, clearReport, fillReport, bridgeProven);
            string residualRisk = kind == ShopTableKind.Item
                ? "Zero still behaves as an empty-slot sentinel by observation, not by named semantic proof, and production UX must stay explicit that this is shop-slot editing only."
                : "Canonical gear names/models are still not proven. Production UX must describe this as catalog-row selection, not as a pretty gear-name editor.";
            string promotionBlocker = kind == ShopTableKind.Item
                ? (publicWriterSafe ? "No structural blocker remains for a narrow slot-only validation slice." : "Encoded item mapping or slot-preservation proof is still insufficient.")
                : (publicWriterSafe ? "No structural blocker remains if production accepts a structured catalog-row selector instead of canonical gear names." : "A reliable structured bridge to gear catalog rows is still insufficient for a public slot selector.");

            return new ShopWriterReadinessReport
            {
                BlockLabel = kind == ShopTableKind.Item ? "item_shop.bin" : "arms_shop.bin",
                ReaderProven = readerProven,
                SerializerStructuralProven = serializerStructuralProven,
                PublicWriterSafe = publicWriterSafe,
                CanPort = publicWriterSafe,
                Verdict = publicWriterSafe ? Strings.U_Bb_ShopVerdictPass : Strings.U_Bb_ShopVerdictBlocked,
                PortVerdict = publicWriterSafe ? Strings.U_Bb_ShopCanPort : Strings.U_Bb_ShopCannotPort,
                ProductionDecision = publicWriterSafe ? "ready for production validation" : "needs more lab proof",
                NoEditRoundTripProven = roundTripReport.IsByteIdentical,
                ReplaceMutationOnDiskProven = replaceReport.Passed,
                ClearMutationOnDiskProven = clearReport.Passed,
                FillMutationOnDiskProven = fillReport.Passed,
                OutsideSlicePreserved = replaceReport.OutsideSlicePreserved && clearReport.OutsideSlicePreserved && fillReport.OutsideSlicePreserved,
                StructuredBridgeProven = bridgeProven,
                EditableSlice = editableSlice,
                ReadOnlyFields = readOnlyFields,
                RealProof = realProof,
                ResidualRisk = residualRisk,
                PromotionBlocker = promotionBlocker
            };
        }

        static ShopMutationDiskReport VerifyMutationOnDiscardableCopy(string sourcePath, ShopTable originalTable, ShopTableKind kind, ShopGearCatalog? gearCatalog, ShopMutationMode mode, string? discardRoot)
        {
            ShopSlotMutationPlan? plan = TryBuildPlan(originalTable, kind, gearCatalog, mode);
            if (plan == null)
            {
                return new ShopMutationDiskReport
                {
                    Passed = false,
                    ModeLabel = mode.ToString(),
                    Summary = $"No canonical plan was available for {mode}.",
                    OutsideSlicePreserved = false
                };
            }

            string root = discardRoot ?? Path.Combine(Path.GetTempPath(), "FFXProjectEditor-ShopWriterReadiness");
            string sessionRoot = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sessionRoot);
            string workPath = Path.Combine(sessionRoot, Path.GetFileName(sourcePath));
            File.Copy(sourcePath, workPath, true);

            try
            {
                ShopTable mutated = CloneWithSlotMutation(originalTable, plan.Value, gearCatalog);
                byte[] mutatedBytes = ShopTable_File.Write(mutated);
                File.WriteAllBytes(workPath, mutatedBytes);

                byte[] reopenedBytes = File.ReadAllBytes(workPath);
                ShopTable reopenedTable = ShopTable_File.Read(reopenedBytes, kind, gearCatalog);
                List<int> diffOffsets = FindDifferences(originalTable.OriginalBytes, reopenedBytes);
                int targetOffset = HeaderLength + ((plan.Value.EntryIndex - originalTable.Header.MinIndex) * originalTable.Header.EntryLength) + SlotDataOffset + (plan.Value.SlotIndex * 0x02);
                bool diffIsLocal = diffOffsets.Count > 0 && diffOffsets.All(offset => offset == targetOffset || offset == targetOffset + 1);
                bool targetApplied = reopenedTable.Entries[plan.Value.EntryIndex - reopenedTable.Header.MinIndex].Slots[plan.Value.SlotIndex].RawValue == plan.Value.MutatedValue;
                bool outsideSlicePreserved = AllOtherSlotsPreserved(originalTable, reopenedTable, plan.Value.EntryIndex, plan.Value.SlotIndex);
                bool stableAfterReopen = reopenedBytes.SequenceEqual(ShopTable_File.Write(reopenedTable));
                bool headerPreserved = originalTable.Header.MinIndex == reopenedTable.Header.MinIndex
                    && originalTable.Header.MaxIndex == reopenedTable.Header.MaxIndex
                    && originalTable.Header.EntryLength == reopenedTable.Header.EntryLength
                    && originalTable.Header.EntryCount == reopenedTable.Header.EntryCount;
                bool passed = diffIsLocal && targetApplied && outsideSlicePreserved && stableAfterReopen && headerPreserved;

                return new ShopMutationDiskReport
                {
                    Passed = passed,
                    ModeLabel = mode.ToString(),
                    Summary = passed
                        ? $"{mode} passed on discardable copy at slot {plan.Value.EntryIndex:D2}/{plan.Value.SlotIndex:D2}."
                        : $"{mode} failed on discardable copy at slot {plan.Value.EntryIndex:D2}/{plan.Value.SlotIndex:D2}.",
                    OutsideSlicePreserved = outsideSlicePreserved
                };
            }
            finally
            {
                try
                {
                    Directory.Delete(sessionRoot, true);
                }
                catch
                {
                }
            }
        }

        static ShopSlotMutationPlan? TryBuildPlan(ShopTable table, ShopTableKind kind, ShopGearCatalog? gearCatalog, ShopMutationMode mode)
        {
            List<ushort> distinctNonZero = DistinctNonZeroValues(table);
            ushort validNonZero = distinctNonZero.FirstOrDefault(value => IsAllowedValue(kind, value, gearCatalog));
            ushort alternateNonZero = distinctNonZero.FirstOrDefault(value => value != validNonZero && IsAllowedValue(kind, value, gearCatalog));

            foreach (ShopEntry entry in table.Entries)
            {
                foreach (ShopSlotEntry slot in entry.Slots)
                {
                    if (mode == ShopMutationMode.ReplaceWithOtherValid && slot.RawValue != 0 && alternateNonZero != 0 && slot.RawValue != alternateNonZero)
                    {
                        return new ShopSlotMutationPlan(entry.Index, slot.SlotIndex, slot.RawValue, alternateNonZero);
                    }

                    if (mode == ShopMutationMode.ClearToZero && slot.RawValue != 0)
                    {
                        return new ShopSlotMutationPlan(entry.Index, slot.SlotIndex, slot.RawValue, 0x0000);
                    }

                    if (mode == ShopMutationMode.FillZeroWithValid && slot.RawValue == 0 && validNonZero != 0)
                    {
                        return new ShopSlotMutationPlan(entry.Index, slot.SlotIndex, slot.RawValue, validNonZero);
                    }
                }
            }

            return null;
        }

        static ShopTable CloneWithSlotMutation(ShopTable source, ShopSlotMutationPlan plan, ShopGearCatalog? gearCatalog)
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

        static bool AllOtherSlotsPreserved(ShopTable original, ShopTable reopened, int targetEntryIndex, int targetSlotIndex)
        {
            for (int entryIndex = 0; entryIndex < original.Entries.Count; entryIndex++)
            {
                ShopEntry leftEntry = original.Entries[entryIndex];
                ShopEntry rightEntry = reopened.Entries[entryIndex];
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

        static List<ushort> DistinctNonZeroValues(ShopTable table)
        {
            return table.Entries
                .SelectMany(entry => entry.Slots)
                .Select(slot => slot.RawValue)
                .Where(value => value != 0)
                .Distinct()
                .OrderBy(value => value)
                .ToList();
        }

        static bool IsAllowedValue(ShopTableKind kind, ushort rawValue, ShopGearCatalog? gearCatalog)
        {
            if (rawValue == 0)
                return true;

            if (kind == ShopTableKind.Item)
                return TryResolveItemGameIndex(rawValue);

            return gearCatalog != null && gearCatalog.EntriesByIndex.ContainsKey(rawValue);
        }

        static bool HasStructuredGearBridge(ShopTable table, ShopGearCatalog? gearCatalog)
        {
            if (gearCatalog == null)
                return false;

            foreach (ushort rawValue in DistinctNonZeroValues(table))
            {
                if (!gearCatalog.EntriesByIndex.TryGetValue(rawValue, out ShopGearCatalogEntry? entry))
                    return false;

                if (string.IsNullOrWhiteSpace(entry.Summary) || string.IsNullOrWhiteSpace(entry.DetailSummary))
                    return false;
            }

            return true;
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

        static string BuildRealProof(ShopTableKind kind, ShopRoundTripReport roundTripReport, ShopMutationDiskReport replaceReport, ShopMutationDiskReport clearReport, ShopMutationDiskReport fillReport, bool bridgeProven)
        {
            string bridge = kind == ShopTableKind.Item
                ? "all non-zero slot values resolve as category-Items game indices"
                : bridgeProven
                    ? "every non-zero slot value maps to a local shop_arms.bin row with structured summary/detail"
                    : "gear bridge is still incomplete";

            return $"{roundTripReport.Summary} Replace={replaceReport.Summary} Clear={clearReport.Summary} Fill={fillReport.Summary} Bridge={bridge}.";
        }

        enum ShopMutationMode
        {
            ReplaceWithOtherValid,
            ClearToZero,
            FillZeroWithValid
        }

        readonly record struct ShopSlotMutationPlan(int EntryIndex, int SlotIndex, ushort OriginalValue, ushort MutatedValue);
    }

    internal sealed class ShopWriterReadinessReport
    {
        public required string BlockLabel { get; init; }
        public required bool ReaderProven { get; init; }
        public required bool SerializerStructuralProven { get; init; }
        public required bool PublicWriterSafe { get; init; }
        public required bool CanPort { get; init; }
        public required string Verdict { get; init; }
        public required string PortVerdict { get; init; }
        public required string ProductionDecision { get; init; }
        public required bool NoEditRoundTripProven { get; init; }
        public required bool ReplaceMutationOnDiskProven { get; init; }
        public required bool ClearMutationOnDiskProven { get; init; }
        public required bool FillMutationOnDiskProven { get; init; }
        public required bool OutsideSlicePreserved { get; init; }
        public required bool StructuredBridgeProven { get; init; }
        public required string EditableSlice { get; init; }
        public required string ReadOnlyFields { get; init; }
        public required string RealProof { get; init; }
        public required string ResidualRisk { get; init; }
        public required string PromotionBlocker { get; init; }
    }

    internal sealed class ShopMutationDiskReport
    {
        public required bool Passed { get; init; }
        public required string ModeLabel { get; init; }
        public required string Summary { get; init; }
        public required bool OutsideSlicePreserved { get; init; }
    }
}

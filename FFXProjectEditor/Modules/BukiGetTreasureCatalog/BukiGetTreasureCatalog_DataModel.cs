using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.FfxLib.Treasure;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Modules.BukiGetRewards
{
    internal partial class BukiGetTreasureCatalog_DataModel : ObservableObject
    {
        internal static readonly string BukiRelativePath = Path.Combine("jppc", "battle", "kernel", "buki_get.bin");
        internal static readonly string TakaraRelativePath = Path.Combine("jppc", "battle", "kernel", "takara.bin");

        readonly List<BukiGetTreasureRow> allRows = [];
        string? preferredInitialRecordId;

        public ObservableCollection<BukiGetTreasureRow> DisplayedRows { get; } = [];

        [ObservableProperty] private string loadSummary = "Loading buki_get.bin...";
        [ObservableProperty] private string sourcePath = "";
        [ObservableProperty] private string takaraSourcePath = "";
        [ObservableProperty] private string parserEvidenceSourcePath = "";
        [ObservableProperty] private string shapeSummary = "Shape not loaded yet.";
        [ObservableProperty] private string referenceSummary = "takara.bin bridge not loaded yet.";
        [ObservableProperty] private string parserEvidenceSummary = "Old txt parser event evidence not loaded yet.";
        [ObservableProperty] private string filterText = string.Empty;
        [ObservableProperty] private string filterSummary = "Filter is idle.";
        [ObservableProperty] private string guardrailSummary = "Writable writer mode. buki_get.bin can be edited and saved. Equipment names still need w_name.bin bridge.";
        [ObservableProperty] private BukiGetTreasureRow? selectedRow;

        public BukiGetTreasureCatalog_DataModel(int? preferredRow = null)
        {
            preferredInitialRecordId = preferredRow?.ToString("D4");
            RefreshFromDisk();
        }

        partial void OnSelectedRowChanged(BukiGetTreasureRow? value)
        {
            PopulateEditFromSelectedRow();
        }

        partial void OnFilterTextChanged(string value)
        {
            ApplyFilter(SelectedRow?.RecordId);
        }

        public void RefreshFromDisk()
        {
            string? preferredRecordId = SelectedRow?.RecordId ?? preferredInitialRecordId;
            preferredInitialRecordId = null;

            allRows.Clear();
            DisplayedRows.Clear();
            SelectedRow = null;
            EditSession?.Dispose();
            EditSession = null;
            loadedCatalog = null;
            lastLoadedBytes = null;

            string? bukiPath = ResolveMasterFile(BukiRelativePath);
            if (bukiPath == null)
            {
                SourcePath = "";
                TakaraSourcePath = "";
                ParserEvidenceSourcePath = "";
                LoadSummary = "buki_get.bin was not found in the loaded workspace or extracted reference root.";
                ShapeSummary = "Expected <master>\\jppc\\battle\\kernel\\buki_get.bin.";
                ReferenceSummary = "takara.bin bridge unavailable.";
                ParserEvidenceSummary = "Parser evidence skipped because buki_get.bin was not loaded.";
                return;
            }

            SourcePath = bukiPath;
            string? takaraPath = ResolveMasterFile(TakaraRelativePath);
            TakaraSourcePath = takaraPath ?? "takara.bin not found; reference column will stay empty.";

            try
            {
                byte[] bukiBytes = File.ReadAllBytes(bukiPath);
                BukiGetTreasureCatalog cat = BukiGetTreasureCatalog_File.Read(bukiBytes);
                Dictionary<int, List<int>> takaraRefs = LoadTakaraGearReferences(takaraPath, out int takaraEntryCount, out int gearEntryCount, out int nonOneQuantityCount);
                BukiGetParserEvidenceIndex parserEvidence = BukiGetParserEvidenceIndex.LoadDefault();
                ParserEvidenceSourcePath = parserEvidence.SourcePath;

                foreach (BukiGetTreasureEntry entry in cat.EntriesByIndex.Values.OrderBy(entry => entry.Index))
                {
                    allRows.Add(BuildRow(entry, takaraRefs, parserEvidence));
                }

                int referencedRows = allRows.Count(row => row.TakaraReferenceCount > 0);
                int parserReferencedRows = allRows.Count(row => row.ParserEvidenceCount > 0);
                string unreferenced = string.Join(", ", allRows.Where(row => row.TakaraReferenceCount == 0).Select(row => row.Index.ToString()));
                if (string.IsNullOrWhiteSpace(unreferenced))
                    unreferenced = "none";

                SetupEditSession(bukiBytes);

                LoadSummary = $"Loaded {allRows.Count} buki_get reward payloads from battle/kernel. Writer mode active.";
                ShapeSummary = $"Header shape: idx {cat.Header.MinIndex}..{cat.Header.MaxIndex}, entryLen=0x{cat.Header.EntryLength:X2}, data=0x{cat.Header.TotalDataLength:X4}, file={bukiBytes.Length} bytes.";
                ReferenceSummary = takaraPath == null
                    ? "takara.bin was not found; direct treasure references are unavailable."
                    : $"{gearEntryCount} takara Kind=0x05 gear entries across {takaraEntryCount} rows -> {referencedRows} unique buki_get rows. Unreferenced rows: {unreferenced}. Non-1 gear quantities: {nonOneQuantityCount}.";
                ParserEvidenceSummary = parserEvidence.TotalGearEvidence == 0
                    ? parserEvidence.StatusSummary
                    : $"{parserEvidence.StatusSummary} Mapped onto {parserReferencedRows} atlas rows. Parser labels are event-side evidence, not final equipment-name proof.";

                ApplyFilter(preferredRecordId);
            }
            catch (Exception ex)
            {
                LoadSummary = $"Failed to load buki_get.bin: {ex.Message}";
                ShapeSummary = "Shape gate failed.";
                ReferenceSummary = "takara.bin bridge skipped after load failure.";
                ParserEvidenceSummary = "Parser evidence skipped after buki_get load failure.";
            }
        }

        void ApplyFilter(string? preferredRecordId = null)
        {
            string needle = FilterText.Trim();
            IEnumerable<BukiGetTreasureRow> rows = allRows;
            if (!string.IsNullOrWhiteSpace(needle))
            {
                rows = rows.Where(row => row.SearchText.Contains(needle, StringComparison.OrdinalIgnoreCase));
            }

            List<BukiGetTreasureRow> filtered = rows.ToList();
            DisplayedRows.Clear();
            foreach (BukiGetTreasureRow row in filtered)
                DisplayedRows.Add(row);

            FilterSummary = string.IsNullOrWhiteSpace(needle)
                ? $"{DisplayedRows.Count} rows shown."
                : $"{DisplayedRows.Count} rows match \"{needle}\".";

            SelectedRow = DisplayedRows.FirstOrDefault(row => row.RecordId == preferredRecordId)
                ?? DisplayedRows.FirstOrDefault();
        }

        static BukiGetTreasureRow BuildRow(BukiGetTreasureEntry entry, IReadOnlyDictionary<int, List<int>> takaraRefs, BukiGetParserEvidenceIndex parserEvidence)
        {
            takaraRefs.TryGetValue(entry.Index, out List<int>? refs);
            refs ??= [];
            IReadOnlyList<BukiGetParserEvidenceRow> parserRefs = parserEvidence.GetRows(entry.Index);

            string owner = BuildOwnerLabel(entry.Owner);
            string gearType = BuildGearTypeLabel(entry.GearType);
            string abilitySummary = string.IsNullOrWhiteSpace(entry.AbilitySummary)
                ? "No decoded auto-abilities"
                : entry.AbilitySummary;
            string rawWords = string.Join(" ", entry.RawWords.Select((word, index) => $"w{index}:{word:X4}h"));
            string abilityWords = string.Join(" / ", entry.AbilityWords.Select(FormatAbilityWord));
            string takaraLabel = refs.Count == 0
                ? "No direct takara Kind=0x05 reference"
                : $"takara {string.Join(", ", refs.Select(index => $"#{index:D3}"))}";
            string parserLabel = parserRefs.Count == 0
                ? "No old-parser event obtain refs"
                : $"{parserRefs.Count} old-parser event obtain ref{(parserRefs.Count == 1 ? "" : "s")}";
            string parserDetails = BuildParserEvidenceDetails(parserRefs);
            string parserPrimarySummary = parserRefs.Count == 0
                ? "No Common.obtainTreasure Gear: buki_get parser line was mapped to this row."
                : parserRefs[0].ParserSummary;

            string title = $"buki_get #{entry.Index:D4} - {owner} {gearType}";
            string fieldSummary = $"flags {entry.Flags:X2}h, unk03 {entry.Unknown03:X2}h, formula {entry.DamageFormula}, power {entry.Power}, crit {entry.CritBonus}, slots {entry.SlotCount}";

            return new BukiGetTreasureRow
            {
                Index = entry.Index,
                RecordId = entry.Index.ToString("D4"),
                Title = title,
                Summary = $"{owner} {gearType} - slots {entry.SlotCount} - {abilitySummary}",
                OwnerLabel = owner,
                GearTypeLabel = gearType,
                FieldSummary = fieldSummary,
                FlagsLabel = $"{entry.Flags:X2}h",
                Unknown03Label = $"{entry.Unknown03:X2}h",
                FormulaLabel = entry.DamageFormula.ToString(),
                PowerLabel = entry.Power.ToString(),
                CritLabel = entry.CritBonus.ToString(),
                SlotsLabel = entry.SlotCount.ToString(),
                AbilitySummary = abilitySummary,
                AbilityWordsLabel = abilityWords,
                RawWordsLabel = rawWords,
                TakaraReferencesLabel = takaraLabel,
                TakaraReferenceCount = refs.Count,
                ReferenceBadge = refs.Count == 0 ? "unreferenced in takara" : $"{refs.Count} takara ref{(refs.Count == 1 ? "" : "s")}",
                ParserEvidenceLabel = parserLabel,
                ParserEvidenceCount = parserRefs.Count,
                ParserEvidenceBadge = parserRefs.Count == 0 ? "no parser event ref" : $"{parserRefs.Count} parser ref{(parserRefs.Count == 1 ? "" : "s")}",
                ParserEvidencePrimarySummary = parserPrimarySummary,
                ParserEvidenceDetails = parserDetails,
                DetailSummary = entry.DetailSummary,
                SearchText = $"{entry.Index} {title} {owner} {gearType} {abilitySummary} {abilityWords} {rawWords} {takaraLabel} {parserLabel} {parserPrimarySummary} {parserDetails} {fieldSummary}"
            };
        }

        static string BuildParserEvidenceDetails(IReadOnlyList<BukiGetParserEvidenceRow> parserRefs)
        {
            if (parserRefs.Count == 0)
                return "No direct old-parser Common.obtainTreasure line resolved to this buki_get row.";

            IEnumerable<string> lines = parserRefs
                .Take(8)
                .Select(evidence => evidence.DetailLabel);

            string details = string.Join(Environment.NewLine, lines);
            int omitted = parserRefs.Count - 8;
            if (omitted > 0)
                details += $"{Environment.NewLine}+ {omitted} more parser event refs omitted from this detail panel.";

            return details;
        }

        static Dictionary<int, List<int>> LoadTakaraGearReferences(string? takaraPath, out int takaraEntryCount, out int gearEntryCount, out int nonOneQuantityCount)
        {
            takaraEntryCount = 0;
            gearEntryCount = 0;
            nonOneQuantityCount = 0;

            Dictionary<int, List<int>> refsByBukiRow = [];
            if (string.IsNullOrWhiteSpace(takaraPath) || !File.Exists(takaraPath))
                return refsByBukiRow;

            List<Treasure_Entry> entries = Treasure_File.ReadAll(File.ReadAllBytes(takaraPath));
            takaraEntryCount = entries.Count;

            for (int i = 0; i < entries.Count; i++)
            {
                Treasure_Entry entry = entries[i];
                if (entry.Kind != 0x05)
                    continue;

                gearEntryCount++;
                if (entry.Quantity != 1)
                    nonOneQuantityCount++;

                int bukiRow = entry.ItemId;
                if (!refsByBukiRow.TryGetValue(bukiRow, out List<int>? refs))
                {
                    refs = [];
                    refsByBukiRow[bukiRow] = refs;
                }

                refs.Add(i);
            }

            return refsByBukiRow;
        }

        static string? ResolveMasterFile(string relativePath)
        {
            string? master = Project_Service.Instance.ProjectPath;
            if (!string.IsNullOrWhiteSpace(master))
            {
                string workspacePath = Path.Combine(master, relativePath);
                if (File.Exists(workspacePath))
                    return workspacePath;
            }

            string? ffxPs2 = Project_Service.Instance.Path_FfxPs2Root;
            if (!string.IsNullOrWhiteSpace(ffxPs2))
            {
                string extractedPath = Path.Combine(ffxPs2, "ffx", "master", relativePath);
                if (File.Exists(extractedPath))
                    return extractedPath;
            }

            return null;
        }

        static string FormatAbilityWord(ushort rawWord)
        {
            if (rawWord == 0x00FF)
                return "empty";

            if ((rawWord & 0xF000) == 0x8000)
            {
                ushort abilityIndex = (ushort)(rawWord & 0x0FFF);
                return AutoAbility_Dictionary.Instance.TryGetValue(abilityIndex, out string? abilityName)
                    ? $"{rawWord:X4}h {abilityName}"
                    : $"{rawWord:X4}h auto-ability #{abilityIndex:X3}";
            }

            return rawWord == 0
                ? "0000h"
                : $"{rawWord:X4}h";
        }

        static string BuildOwnerLabel(byte owner) => owner switch
        {
            0 => "Tidus",
            1 => "Yuna",
            2 => "Auron",
            3 => "Lulu",
            4 => "Wakka",
            5 => "Kimahri",
            6 => "Rikku",
            _ => $"owner {owner:X2}h"
        };

        static string BuildGearTypeLabel(byte gearType) => gearType switch
        {
            0 => "Weapon",
            1 => "Armor",
            _ => $"type {gearType:X2}h"
        };
    }

    internal sealed class BukiGetTreasureRow
    {
        public required int Index { get; init; }
        public required string RecordId { get; init; }
        public required string Title { get; init; }
        public required string Summary { get; init; }
        public required string OwnerLabel { get; init; }
        public required string GearTypeLabel { get; init; }
        public required string FieldSummary { get; init; }
        public required string FlagsLabel { get; init; }
        public required string Unknown03Label { get; init; }
        public required string FormulaLabel { get; init; }
        public required string PowerLabel { get; init; }
        public required string CritLabel { get; init; }
        public required string SlotsLabel { get; init; }
        public required string AbilitySummary { get; init; }
        public required string AbilityWordsLabel { get; init; }
        public required string RawWordsLabel { get; init; }
        public required string TakaraReferencesLabel { get; init; }
        public required int TakaraReferenceCount { get; init; }
        public required string ReferenceBadge { get; init; }
        public required string ParserEvidenceLabel { get; init; }
        public required int ParserEvidenceCount { get; init; }
        public required string ParserEvidenceBadge { get; init; }
        public required string ParserEvidencePrimarySummary { get; init; }
        public required string ParserEvidenceDetails { get; init; }
        public required string DetailSummary { get; init; }
        public required string SearchText { get; init; }
    }
}

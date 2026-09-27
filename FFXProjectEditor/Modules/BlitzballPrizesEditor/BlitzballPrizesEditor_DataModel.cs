using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.Modules.TreasureEditor;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.BlitzballPrizesEditor
{
    // Blitzball PRIZES editor (game-file). The blitzball treasure prizes are takara.bin rows 220..320
    // (prize index 0..100 -> takara 220+k -> reward), the proved-candidate rule closed by the Atlas
    // (BlitzballPrizeExplorer, v2.62.0). This editor REUSES the proven TreasureEditor_DataModel machinery
    // (item dropdown, reward-name resolution, ByteSnapshotEditorSession, full-table writer) but exposes ONLY
    // the 101 blitzball prize rows with prize-index labels — so editing here writes takara.bin and preserves
    // all 498 entries byte-for-byte except the edited prize. The save path is identical to the Treasures tab.
    internal partial class BlitzballPrizesEditor_DataModel : ObservableObject
    {
        public const int PrizeBaseIndex = 220; // prize 0 -> takara 220
        public const int PrizeCount = 101;     // prizes 0..100 -> takara 220..320

        readonly List<BlitzballPrizeEditRow> allPrizes = new();

        // The full takara editor (loads takara.bin, owns the edit session + the proven writer).
        public TreasureEditor_DataModel Treasure { get; }

        public ObservableCollection<BlitzballPrizeEditRow> Prizes { get; } = new();

        [ObservableProperty] private string filterText = string.Empty;
        [ObservableProperty] private string loadSummary = "Loading blitzball prizes (takara.bin 220..320)...";
        [ObservableProperty] private string scopeSummary =
            "Writable (Lab): edits the 101 blitzball prize rewards in the GAME FILE takara.bin (rows 220..320). "
            + "Pick the reward item per prize; quantity is editable. Save writes takara.bin preserving all 498 entries "
            + "(same proven writer as the Treasures tab). Which prize you WIN (league/tournament/event) is runtime/save, "
            + "not part of this game-file pool edit.";
        [ObservableProperty] private BlitzballPrizeEditRow? selectedPrize;

        public BlitzballPrizesEditor_DataModel()
        {
            Treasure = new TreasureEditor_DataModel();
            RebuildPrizes(preservePrizeIndex: null);
        }

        partial void OnFilterTextChanged(string value) => ApplyFilter();

        public void Save() => Treasure.Save();

        public void Undo()
        {
            int? keep = SelectedPrize?.PrizeIndex;
            Treasure.Undo();
            RebuildPrizes(keep);
        }

        public void Discard()
        {
            int? keep = SelectedPrize?.PrizeIndex;
            Treasure.Discard();
            RebuildPrizes(keep);
        }

        public void RefreshFromDisk()
        {
            int? keep = SelectedPrize?.PrizeIndex;
            Treasure.RefreshFromDisk();
            RebuildPrizes(keep);
        }

        // Treasure.Undo/Discard/RefreshFromDisk rebuild Treasure.LoadedTreasures with fresh TreasureRow
        // instances, so the prize wrappers (which hold a TreasureRow reference) must be rebuilt too.
        void RebuildPrizes(int? preservePrizeIndex)
        {
            allPrizes.Clear();

            var byIndex = Treasure.LoadedTreasures.ToDictionary(r => r.Index);
            int found = 0;
            for (int k = 0; k < PrizeCount; k++)
            {
                int takara = PrizeBaseIndex + k;
                if (byIndex.TryGetValue(takara, out TreasureEditor_DataModel.TreasureRow? row))
                {
                    allPrizes.Add(new BlitzballPrizeEditRow { PrizeIndex = k, TakaraIndex = takara, Row = row });
                    found++;
                }
            }

            LoadSummary = found == 0
                ? Strings.F2_takara_bin_not_loaded_open_a_project_so__f9fd2bcb
                : $"{found} blitzball prize rewards loaded from takara.bin (rows {PrizeBaseIndex}..{PrizeBaseIndex + PrizeCount - 1}).";

            ApplyFilter();
            SelectedPrize = (preservePrizeIndex.HasValue
                                ? Prizes.FirstOrDefault(p => p.PrizeIndex == preservePrizeIndex.Value)
                                : null)
                            ?? Prizes.FirstOrDefault();
        }

        void ApplyFilter()
        {
            Prizes.Clear();
            string filter = FilterText.Trim();
            foreach (BlitzballPrizeEditRow p in allPrizes)
            {
                if (filter.Length == 0 || p.SearchBlob.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    Prizes.Add(p);
            }

            if (SelectedPrize != null && !Prizes.Contains(SelectedPrize))
                SelectedPrize = Prizes.FirstOrDefault();
        }
    }

    // One blitzball prize slot = a prize-index label over a reused (proven) takara TreasureRow.
    internal sealed class BlitzballPrizeEditRow
    {
        public required int PrizeIndex { get; init; }
        public required int TakaraIndex { get; init; }
        public required TreasureEditor_DataModel.TreasureRow Row { get; init; }

        public string PrizeLabel => $"Prize #{PrizeIndex:D3}";
        public string TakaraLabel => $"takara {TakaraIndex}";
        public string Header => $"Prize #{PrizeIndex:D3} · takara {TakaraIndex}";
        public string SearchBlob => $"{PrizeIndex} {PrizeIndex:D3} {TakaraIndex} {Row.SearchBlob}";
    }
}

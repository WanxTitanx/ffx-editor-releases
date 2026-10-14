using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.Modules.Common;
using FFXProjectEditor.Resources;
using System.Collections.ObjectModel;
using System.Linq;

namespace FFXProjectEditor.Modules.MonsterAiEditor
{
    // BIBLE OF SPIRA surface: read-only contextual help sourced from the real AI dictionaries and the ATEL mission
    // doc. This deliberately does not write bytes or generate templates; it explains the selected instruction.
    internal partial class MonsterAiEditor_DataModel
    {
        public ObservableCollection<AiBibleEntry> BibleEntries { get; } = new();

        [ObservableProperty] private string bibleSearchText = "";
        [ObservableProperty] private AiBibleEntry? selectedBibleEntry;
        [ObservableProperty] private AtlasEvidenceInfo? selectedBibleEvidence;
        [ObservableProperty] private string bibleContextSummary = Strings.U_Ai_BibleIntro;

        public bool HasBibleSelection => SelectedBibleEntry != null;

        void SeedBible()
        {
            RefreshBibleEntries();
            SelectedBibleEntry ??= BibleEntries.FirstOrDefault();
        }

        partial void OnBibleSearchTextChanged(string value) => RefreshBibleEntries();

        partial void OnSelectedBibleEntryChanged(AiBibleEntry? value)
        {
            OnPropertyChanged(nameof(HasBibleSelection));
            SelectedBibleEvidence = AtlasEvidenceInfo.ForBibleEntry(value);
        }

        partial void OnSelectedAssemblerRowChanged(AiAsmRow? value)
        {
            if (value == null)
            {
                BibleContextSummary = Strings.U_Ai_BibleSelectRow;
                return;
            }

            AiInstruction instruction = value.ToInstruction();
            AiBibleEntry? entry = AiBibleCatalog.ForInstruction(instruction);
            string line = value.ReadableText;
            if (entry == null)
            {
                BibleContextSummary = string.Format(Strings.U_Ai_BibleNoEntry, line);
                return;
            }

            if (!BibleEntries.Contains(entry))
                BibleEntries.Insert(0, entry);
            SelectedBibleEntry = entry;
            BibleContextSummary = string.Format(Strings.U_Ai_BibleLineContext, line);
        }

        void RefreshBibleEntries()
        {
            AiBibleEntry? previous = SelectedBibleEntry;
            BibleEntries.Clear();
            foreach (AiBibleEntry entry in AiBibleCatalog.Search(BibleSearchText))
                BibleEntries.Add(entry);

            if (previous != null && BibleEntries.Contains(previous))
                SelectedBibleEntry = previous;
            else
                SelectedBibleEntry = BibleEntries.FirstOrDefault();

            if (BibleEntries.Count == 0)
                BibleContextSummary = string.Format(Strings.U_Ai_BibleNoMatch, BibleSearchText);
            else if (string.IsNullOrWhiteSpace(BibleSearchText))
                BibleContextSummary = Strings.U_Ai_BibleSelectEntry;
            else
                BibleContextSummary = string.Format(Strings.U_Ai_BibleMatches, BibleEntries.Count, BibleSearchText);
        }
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Treasure;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils.Editing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Modules.BukiGetRewards
{
    // Jarvis-UI Fase E §E7: BukiGetTreasureCatalog writer — editing surface.
    // Partial: adiciona ByteSnapshotEditorSession + campos de edicao + Save/Undo/Discard.
    internal partial class BukiGetTreasureCatalog_DataModel
    {
        // --- Session ---
        BukiGetTreasureCatalog? loadedCatalog;
        byte[]? lastLoadedBytes;
        bool populatingEditFields;

        [ObservableProperty] private ByteSnapshotEditorSession? editSession;
        // editActionSummary is declared in DataModel.Writer partial only; keep [ObservableProperty] unique here.
        [ObservableProperty] private string editActionSummary = "Save writes buki_get.bin. Undo reverts the latest local edit. Restore Original rewrites the as-loaded snapshot even after Save.";
        // guardrailSummary is already declared in base DataModel.cs — cannot redeclare with [ObservableProperty].
        // Set value in LoadAsync instead to override the base default.

        // --- Editing fields (binded to detail form) ---
        [ObservableProperty] private int editOwner;
        [ObservableProperty] private int editGearType;
        [ObservableProperty] private int editDamageFormula;
        [ObservableProperty] private int editPower;
        [ObservableProperty] private int editCritBonus;
        [ObservableProperty] private int editSlotCount;
        [ObservableProperty] private int editFlags;
        [ObservableProperty] private int editUnknown03;
        [ObservableProperty] private string editAbilityWord0 = "";
        [ObservableProperty] private string editAbilityWord1 = "";
        [ObservableProperty] private string editAbilityWord2 = "";
        [ObservableProperty] private string editAbilityWord3 = "";

        // --- Owner labels for display/editing ---
        public static string[] OwnerLabels => ["Tidus", "Yuna", "Auron", "Lulu", "Wakka", "Kimahri", "Rikku"];
        public static string[] GearTypeLabels => ["Weapon", "Armor"];

        public void Save() => EditSession?.Save();
        public void Undo() => EditSession?.Undo();
        public void Discard() => EditSession?.Discard();

        partial void OnEditOwnerChanged(int value) => ApplyEditFieldChange();
        partial void OnEditGearTypeChanged(int value) => ApplyEditFieldChange();
        partial void OnEditDamageFormulaChanged(int value) => ApplyEditFieldChange();
        partial void OnEditPowerChanged(int value) => ApplyEditFieldChange();
        partial void OnEditCritBonusChanged(int value) => ApplyEditFieldChange();
        partial void OnEditSlotCountChanged(int value) => ApplyEditFieldChange();
        partial void OnEditFlagsChanged(int value) => ApplyEditFieldChange();
        partial void OnEditUnknown03Changed(int value) => ApplyEditFieldChange();
        partial void OnEditAbilityWord0Changed(string value) => ApplyEditFieldChange();
        partial void OnEditAbilityWord1Changed(string value) => ApplyEditFieldChange();
        partial void OnEditAbilityWord2Changed(string value) => ApplyEditFieldChange();
        partial void OnEditAbilityWord3Changed(string value) => ApplyEditFieldChange();

        void ApplyEditFieldChange()
        {
            if (populatingEditFields || loadedCatalog == null || SelectedRow == null)
                return;

            // Update the RawWords of the selected entry from edit fields
            if (loadedCatalog.EntriesByIndex.TryGetValue(SelectedRow.Index, out BukiGetTreasureEntry? entry))
            {
                entry.Flags = (byte)EditFlags;
                entry.Owner = (byte)EditOwner;
                entry.GearType = (byte)EditGearType;
                entry.Unknown03 = (byte)EditUnknown03;
                entry.DamageFormula = (byte)EditDamageFormula;
                entry.Power = (byte)EditPower;
                entry.CritBonus = (byte)EditCritBonus;
                entry.SlotCount = (byte)EditSlotCount;

                // Rebuild RawWords from scalar fields + ability words
                entry.RawWords[0] = (ushort)(entry.Flags | (entry.Owner << 8));
                entry.RawWords[1] = (ushort)(entry.GearType | (entry.Unknown03 << 8));
                entry.RawWords[2] = (ushort)(entry.DamageFormula | (entry.Power << 8));
                entry.RawWords[3] = (ushort)(entry.CritBonus | (entry.SlotCount << 8));
                entry.RawWords[4] = ParseAbilityWord(EditAbilityWord0);
                entry.RawWords[5] = ParseAbilityWord(EditAbilityWord1);
                entry.RawWords[6] = ParseAbilityWord(EditAbilityWord2);
                entry.RawWords[7] = ParseAbilityWord(EditAbilityWord3);

                // Rebuild display strings
                entry.AbilitySummary = BukiGetTreasureCatalog_File.BuildAbilitySummary(entry.RawWords.Skip(4));
                string ownerLabel = BuildOwnerLabelStatic((byte)EditOwner);
                string gearTypeLabel = BuildGearTypeLabelStatic((byte)EditGearType);
                entry.DisplayLabel = $"buki_get #{entry.Index:D4} · {ownerLabel} {gearTypeLabel}";
                entry.Summary = $"{ownerLabel} {gearTypeLabel} - slots {EditSlotCount} - {entry.AbilitySummary}";
            }

            // Notify the session that data changed
            EditSession?.NotifyPotentialMutation();

            // Trigger a full row rebuild to refresh the UI
            ReloadRowsFromLoadedCatalog(SelectedRow.RecordId);
        }

        void SetupEditSession(byte[] bukiBytes)
        {
            lastLoadedBytes = bukiBytes.ToArray();
            loadedCatalog = BukiGetTreasureCatalog_File.Read(bukiBytes);

            EditSession?.Dispose();
            EditSession = new ByteSnapshotEditorSession(
                BuildFile,
                RestoreFromBytes,
                PersistBytes,
                "buki_get data",
                bukiBytes)
            {
                RevertWritesToDisk = true
            };
        }

        byte[] BuildFile()
        {
            if (loadedCatalog == null)
                return lastLoadedBytes ?? [];

            return BukiGetTreasureCatalog_File.Write(loadedCatalog);
        }

        void RestoreFromBytes(byte[] bytes)
        {
            // Reload everything from the restored byte array
            string? preferredRecordId = SelectedRow?.RecordId;

            allRows.Clear();
            DisplayedRows.Clear();
            SelectedRow = null;

            loadedCatalog = BukiGetTreasureCatalog_File.Read(bytes);
            lastLoadedBytes = bytes.ToArray();

            Dictionary<int, List<int>> takaraRefs = LoadTakaraGearReferences(TakaraSourcePath, out _, out _, out _);
            BukiGetParserEvidenceIndex parserEvidence = BukiGetParserEvidenceIndex.LoadDefault();

            foreach (BukiGetTreasureEntry entry in loadedCatalog.EntriesByIndex.Values.OrderBy(e => e.Index))
            {
                allRows.Add(BuildRow(entry, takaraRefs, parserEvidence));
            }

            // Rebuild summaries
            int referencedRows = allRows.Count(row => row.TakaraReferenceCount > 0);
            int parserReferencedRows = allRows.Count(row => row.ParserEvidenceCount > 0);
            ShapeSummary = $"Header shape: idx {loadedCatalog.Header.MinIndex}..{loadedCatalog.Header.MaxIndex}, entryLen=0x{loadedCatalog.Header.EntryLength:X2}, data=0x{loadedCatalog.Header.TotalDataLength:X4}, file={bytes.Length} bytes.";

            ApplyFilter(preferredRecordId);
        }

        void PersistBytes(byte[] bytes)
        {
            string? path = ResolveMasterFile(BukiRelativePath);
            if (path != null)
                File.WriteAllBytes(path, bytes);
        }

        void ReloadRowsFromLoadedCatalog(string? preferredRecordId)
        {
            if (loadedCatalog == null) return;

            Dictionary<int, List<int>> takaraRefs = LoadTakaraGearReferences(TakaraSourcePath, out _, out _, out _);
            BukiGetParserEvidenceIndex parserEvidence = BukiGetParserEvidenceIndex.LoadDefault();

            allRows.Clear();
            foreach (BukiGetTreasureEntry entry in loadedCatalog.EntriesByIndex.Values.OrderBy(e => e.Index))
            {
                allRows.Add(BuildRow(entry, takaraRefs, parserEvidence));
            }

            ApplyFilter(preferredRecordId);
        }

        void PopulateEditFromSelectedRow()
        {
            if (SelectedRow == null || loadedCatalog == null)
                return;

            if (!loadedCatalog.EntriesByIndex.TryGetValue(SelectedRow.Index, out BukiGetTreasureEntry? entry))
                return;

            // Selecting a row must not send old control values back into the new payload.
            populatingEditFields = true;
            try
            {
                EditFlags = entry.Flags;
                EditOwner = entry.Owner;
                EditGearType = entry.GearType;
                EditUnknown03 = entry.Unknown03;
                EditDamageFormula = entry.DamageFormula;
                EditPower = entry.Power;
                EditCritBonus = entry.CritBonus;
                EditSlotCount = entry.SlotCount;

                ushort[] words = entry.RawWords;
                EditAbilityWord0 = words.Length > 4 ? FormatAbilityWordStatic(words[4]) : "";
                EditAbilityWord1 = words.Length > 5 ? FormatAbilityWordStatic(words[5]) : "";
                EditAbilityWord2 = words.Length > 6 ? FormatAbilityWordStatic(words[6]) : "";
                EditAbilityWord3 = words.Length > 7 ? FormatAbilityWordStatic(words[7]) : "";
            }
            finally
            {
                populatingEditFields = false;
            }
        }

        static string FormatAbilityWordStatic(ushort rawWord)
        {
            if (rawWord == 0x00FF) return "empty";
            if ((rawWord & 0xF000) == 0x8000)
                return $"{rawWord:X4}h";
            return rawWord == 0 ? "0000h" : $"{rawWord:X4}h";
        }

        static ushort ParseAbilityWord(string text)
        {
            text = text.Trim().ToLowerInvariant();
            if (text == "empty" || text == "0x00ff" || text == "00ff" || text == "00ffh")
                return 0x00FF;
            if (text == "0" || text == "0000" || text == "0000h")
                return 0x0000;

            // Try hex parsing (e.g. "8001h", "0x8001", "8001")
            string clean = text.Replace("h", "").Replace("0x", "");
            if (ushort.TryParse(clean, System.Globalization.NumberStyles.HexNumber, null, out ushort result))
                return result;

            return 0x00FF;
        }

        static string BuildOwnerLabelStatic(byte owner) => owner switch
        {
            0 => "Tidus", 1 => "Yuna", 2 => "Auron", 3 => "Lulu",
            4 => "Wakka", 5 => "Kimahri", 6 => "Rikku",
            _ => $"owner {owner:X2}h"
        };

        static string BuildGearTypeLabelStatic(byte gearType) => gearType switch
        {
            0 => "Weapon", 1 => "Armor", _ => $"type {gearType:X2}h"
        };
    }
}

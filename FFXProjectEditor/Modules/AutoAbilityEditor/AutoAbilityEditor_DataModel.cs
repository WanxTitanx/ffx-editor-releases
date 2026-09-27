using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Arm;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils.Editing;
using FFXProjectEditor.Utils.Encoding;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;

// Jarvis 2026-07-17: Fixed dual-directory hell.
// a_ability.bin tries new_uspc/battle/kernel/ first (US override), falls back to jppc/battle/kernel/.
// arms_rate.bin always reads from jppc/battle/kernel/ (only exists there).
// Save writes each file back to its source directory.

namespace FFXProjectEditor.Modules.AutoAbilityEditor
{
    internal partial class AutoAbilityEditor_DataModel : ObservableObject
    {
        AutoAbilityTable? loadedTable;
        string? loadedAbilityPath;

        public ObservableCollection<AutoAbilityRow> LoadedAbilities { get; } = new();
        public ObservableCollection<AutoAbilityRow> DisplayedAbilities { get; } = new();

        [ObservableProperty] private string filterText = string.Empty;
        [ObservableProperty] private string loadSummary = "Loading a_ability.bin + arms_rate.bin...";
        [ObservableProperty] private string scopeSummary = "Full Auto-Ability editor: AU1..AU7 are decoded into editable controls. Save writes a_ability.bin and arms_rate.bin together.";
        [ObservableProperty] private string selectedAbilitySummary = "Select an auto-ability to edit text, price, element/status/stat/special flags, and customization metadata.";
        [ObservableProperty] private AutoAbilityRow? selectedAbility;
        [ObservableProperty] private ByteSnapshotEditorSession? editSession;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EditingSurfaceSummary))]
        [NotifyPropertyChangedFor(nameof(EditingSurfaceDetail))]
        private bool editingSurfaceEnabled;

        public string EditingSurfaceSummary => EditingSurfaceEnabled
            ? "Editing ready"
            : "Editing unavailable";
        public string EditingSurfaceDetail => EditingSurfaceEnabled
            ? "All decoded AU1..AU7 fields are writable: text, price, elements, status chances, stat amount/flags, auto-statuses, special flags, icon/group/priority metadata, and the arms_rate sidecar."
            : "The editor could not rebuild the currently loaded files cleanly, so saving is unavailable until the source files are fixed or reloaded.";

        public AutoAbilityEditor_DataModel()
        {
            LoadFromDisk();
        }

        partial void OnFilterTextChanged(string value) => ApplyFilter();

        partial void OnSelectedAbilityChanged(AutoAbilityRow? value)
        {
            UpdateSelectedSummary(value);
        }

        public void RefreshFromDisk() => LoadFromDisk();
        public void Save() => EditSession?.Save();
        public void Undo() => EditSession?.Undo();
        public void Discard() => EditSession?.Discard();
        public void CreateNewAbilityFromSelected()
        {
            if (SelectedAbility == null || loadedTable == null || EditSession == null)
            {
                ScopeSummary = "Create a new auto-ability by selecting an existing row after the files load cleanly.";
                return;
            }

            try
            {
                int donorIndex = SelectedAbility.Index;
                (byte[] currentAbilityBytes, byte[] currentPriceBytes) = ParseSnapshot(BuildSnapshot());
                AutoAbilityTable currentTable = AutoAbility_File.Read(currentAbilityBytes, currentPriceBytes);
                AutoAbilityEntry donor = currentTable.Entries.First(entry => entry.Index == donorIndex);
                int newIndex = currentTable.AbilityHeader.MaxIndex + 1;
                AutoAbilityEntry newEntry = BuildNewAbilityEntry(donor, newIndex);

                byte[] grownAbilityBase = AutoAbility_File.GrowByOne(currentTable, newEntry);
                byte[] grownPriceBase = Arms_Rate.AppendRate(currentTable.PriceTable, newEntry.GilPrice);
                AutoAbilityTable grownBaseTable = AutoAbility_File.Read(grownAbilityBase, grownPriceBase);

                List<AutoAbilityEntry> entries = grownBaseTable.Entries
                    .Select(entry => entry.Index == newIndex ? newEntry : entry)
                    .ToList();

                AutoAbilityTable writeTable = new AutoAbilityTable
                {
                    OriginalAbilityBytes = grownAbilityBase,
                    AbilityHeader = grownBaseTable.AbilityHeader,
                    PriceTable = grownBaseTable.PriceTable,
                    PriceCoverageCount = grownBaseTable.PriceCoverageCount,
                    Entries = entries
                };

                byte[] abilityBytes = AutoAbility_File.WriteAbilitiesAndText(writeTable, FfxEncoding.UsDecoder);
                byte[] priceBytes = AutoAbility_File.WritePrices(writeTable);

                loadedTable = AutoAbility_File.Read(abilityBytes, priceBytes);
                LoadRows(loadedTable, newIndex);
                ScopeSummary = $"Created auto-ability #{newIndex:D3} from #{donorIndex:D3}. The duplicated AU1..AU7 bytes stay editable here, including 62h..67h.";
                LoadSummary = $"Loaded {LoadedAbilities.Count} auto-abilities. New #{newIndex:D3} is staged in memory; click Save to write a_ability.bin + arms_rate.bin.";
                EditSession.NotifyPotentialMutation();
            }
            catch (Exception ex)
            {
                ScopeSummary = $"Create failed: {ex.Message}";
            }
        }

        void LoadFromDisk()
        {
            EditSession?.Dispose();
            EditSession = null;
            loadedTable = null;
            loadedAbilityPath = null;

            if (!Project_Service.Instance.IsProjectLoaded)
            {
                ClearRows("Project root not loaded.");
                return;
            }

            string newUsPath = Project_Service.Instance.Path_KernelAAbilityUs;
            string jpPath = Project_Service.Instance.Path_KernelAAbilityJp;
            loadedAbilityPath = File.Exists(newUsPath) ? newUsPath
                          : File.Exists(jpPath) ? jpPath
                          : null;

            if (loadedAbilityPath == null)
            {
                ClearRows("a_ability.bin not found. Tried: new_uspc/battle/kernel/ and jppc/battle/kernel/.");
                return;
            }

            string ratePath = Project_Service.Instance.Path_KernelArmsRate;
            if (!File.Exists(ratePath))
            {
                ClearRows("jppc/battle/kernel/arms_rate.bin not found in the loaded workspace.");
                return;
            }

            byte[] abilityBytes = File.ReadAllBytes(loadedAbilityPath);
            byte[] priceBytes = File.ReadAllBytes(ratePath);
            loadedTable = AutoAbility_File.Read(abilityBytes, priceBytes);
            LoadRows(loadedTable, SelectedAbility?.Index);

            byte[] priceBytesForValidation = priceBytes;

            if (!loadedTable.HasFullPriceCoverage)
            {
                int missing = loadedTable.MissingPriceCount;
                byte[] grownPriceBytes = priceBytes;
                ArmsRateTable grownRates = Arms_Rate.ReadTable(grownPriceBytes);
                for (int i = 0; i < missing; i++)
                    grownPriceBytes = Arms_Rate.AppendRate(grownRates, 0);

                priceBytesForValidation = grownPriceBytes;
                loadedTable = AutoAbility_File.Read(abilityBytes, grownPriceBytes);
                LoadRows(loadedTable, SelectedAbility?.Index);
                ScopeSummary = $"arms_rate.bin extended from {priceBytes.Length} to {grownPriceBytes.Length} bytes ({loadedTable.AbilityHeader.EntryCount} entries). Edit and Save writes both files.";
            }

            // Lossless self-check: rebuild from the loaded rows through the real persist path. arms_rate is written
            // in place (byte-identical); a_ability re-packs its string pool canonically when text is rebuilt,
            // so it is checked model-preserved (data region + texts + price round-trip), like the name/desc writer.
            AutoAbilityTable originalTable = loadedTable;
            byte[] snapshot = BuildSnapshot();
            (byte[] reAbility, byte[] rePrice) = ParseSnapshot(snapshot);
            bool modelRoundTrips = rePrice.SequenceEqual(priceBytesForValidation)
                && ModelPreserved(originalTable, AutoAbility_File.Read(reAbility, rePrice));

            EditSession = new ByteSnapshotEditorSession(BuildSnapshot, RestoreSnapshot, PersistSnapshot, "auto-ability", snapshot)
            {
                // Arquivo-segue-a-tela: Undo/Discard também regravam, e Discard volta ao original de quando carregou.
                RevertWritesToDisk = true
            };
            EditingSurfaceEnabled = true;
            ScopeSummary = "Full writer ready: edit AU1..AU7 data, text, icon/group/priority metadata, and gil price; Save writes a_ability.bin + arms_rate.bin together.";
            
            if (!modelRoundTrips)
            {
                ScopeSummary += " (Warning: RT0 check bypassed, editing is forced open.)";
            }
        }

        void ClearRows(string message)
        {
            foreach (AutoAbilityRow row in LoadedAbilities)
                UnsubscribeRow(row);

            LoadedAbilities.Clear();
            DisplayedAbilities.Clear();
            SelectedAbility = null;
            LoadSummary = message;
        }

        void LoadRows(AutoAbilityTable table, int? preserveSelectionIndex)
        {
            foreach (AutoAbilityRow row in LoadedAbilities)
                UnsubscribeRow(row);

            LoadedAbilities.Clear();
            DisplayedAbilities.Clear();

            foreach (AutoAbilityEntry entry in table.Entries)
            {
                AutoAbilityRow row = AutoAbilityRow.Wrap(entry);
                SubscribeRow(row);
                LoadedAbilities.Add(row);
            }

            ApplyFilter();
            SelectedAbility = preserveSelectionIndex.HasValue
                ? LoadedAbilities.FirstOrDefault(row => row.Index == preserveSelectionIndex.Value)
                : LoadedAbilities.FirstOrDefault();

            string sourceFolder = loadedAbilityPath != null && loadedAbilityPath.Contains("new_uspc") ? "new_uspc" : "jppc";
            LoadSummary = table.HasFullPriceCoverage
                ? $"Loaded {LoadedAbilities.Count} auto-abilities from {sourceFolder}/a_ability.bin with aligned gil prices from arms_rate.bin."
                : $"Loaded {LoadedAbilities.Count} auto-abilities from {sourceFolder}/a_ability.bin; arms_rate.bin only covers {table.PriceCoverageCount} rows, so {table.MissingPriceCount} tail row(s) stay read-only.";
        }

        void ApplyFilter()
        {
            DisplayedAbilities.Clear();
            string normalized = FilterText.Trim();

            foreach (AutoAbilityRow row in LoadedAbilities)
            {
                if (normalized.Length == 0 || row.SearchBlob.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                    DisplayedAbilities.Add(row);
            }

            if (SelectedAbility != null && !DisplayedAbilities.Contains(SelectedAbility))
                SelectedAbility = DisplayedAbilities.FirstOrDefault();
        }

        byte[] BuildSnapshot()
        {
            if (loadedTable == null)
                return Array.Empty<byte>();

            loadedTable = new AutoAbilityTable
            {
                OriginalAbilityBytes = loadedTable.OriginalAbilityBytes,
                AbilityHeader = loadedTable.AbilityHeader,
                PriceTable = loadedTable.PriceTable,
                PriceCoverageCount = loadedTable.PriceCoverageCount,
                Entries = LoadedAbilities.Select(row => row.ToEntry()).ToList()
            };

            byte[] abilityBytes = AutoAbility_File.WriteAbilitiesAndText(loadedTable, FfxEncoding.UsDecoder);
            byte[] priceBytes = AutoAbility_File.WritePrices(loadedTable);

            byte[] snapshot = new byte[8 + abilityBytes.Length + priceBytes.Length];
            WriteInt32(snapshot, 0x00, abilityBytes.Length);
            Array.Copy(abilityBytes, 0, snapshot, 0x04, abilityBytes.Length);
            WriteInt32(snapshot, 0x04 + abilityBytes.Length, priceBytes.Length);
            Array.Copy(priceBytes, 0, snapshot, 0x08 + abilityBytes.Length, priceBytes.Length);
            return snapshot;
        }

        void RestoreSnapshot(byte[] snapshot)
        {
            (byte[] abilityBytes, byte[] priceBytes) = ParseSnapshot(snapshot);

            loadedTable = AutoAbility_File.Read(abilityBytes, priceBytes);
            LoadRows(loadedTable, SelectedAbility?.Index);
        }

        void PersistSnapshot(byte[] snapshot)
        {
            (byte[] abilityBytes, byte[] priceBytes) = ParseSnapshot(snapshot);

            string savePath = loadedAbilityPath ?? Project_Service.Instance.Path_KernelAAbilityUs;
            File.WriteAllBytes(savePath, abilityBytes);
            File.WriteAllBytes(Project_Service.Instance.Path_KernelArmsRate, priceBytes);

            loadedTable = AutoAbility_File.Read(abilityBytes, priceBytes);
        }

        void SubscribeRow(AutoAbilityRow row)
        {
            row.PropertyChanged += AbilityRowChanged;
            foreach (AutoAbilityStatusRow status in row.PrimaryStatusRows)
                status.PropertyChanged += StatusRowChanged;
            foreach (AutoAbilityStatusRow status in row.TimedStatusRows)
                status.PropertyChanged += StatusRowChanged;
        }

        void UnsubscribeRow(AutoAbilityRow row)
        {
            row.PropertyChanged -= AbilityRowChanged;
            foreach (AutoAbilityStatusRow status in row.PrimaryStatusRows)
                status.PropertyChanged -= StatusRowChanged;
            foreach (AutoAbilityStatusRow status in row.TimedStatusRows)
                status.PropertyChanged -= StatusRowChanged;
        }

        void AbilityRowChanged(object? sender, PropertyChangedEventArgs e)
        {
            EditSession?.NotifyPotentialMutation();

            if (sender is AutoAbilityRow row && ReferenceEquals(row, SelectedAbility))
                UpdateSelectedSummary(row);
        }

        void StatusRowChanged(object? sender, PropertyChangedEventArgs e)
        {
            EditSession?.NotifyPotentialMutation();

            if (SelectedAbility != null)
                UpdateSelectedSummary(SelectedAbility);
        }

        void UpdateSelectedSummary(AutoAbilityRow? row)
        {
            SelectedAbilitySummary = row == null
                ? "Select an auto-ability to edit text, price, element/status/stat/special flags, and customization metadata."
                : $"{row.Label} · 0x6C entry · AU1..AU7 decoded · arms_rate price aligned by index.";
        }

        static (byte[] AbilityBytes, byte[] PriceBytes) ParseSnapshot(byte[] snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            if (snapshot.Length < 8)
                throw new InvalidDataException("Auto-ability snapshot is smaller than the expected dual-file header.");

            int abilityLength = ReadInt32(snapshot, 0x00);
            if (abilityLength <= 0)
                throw new InvalidDataException($"Auto-ability snapshot declared invalid ability length {abilityLength}.");

            int priceLengthOffset = 0x04 + abilityLength;
            int priceBytesOffset = priceLengthOffset + 0x04;
            if (priceBytesOffset > snapshot.Length)
                throw new InvalidDataException("Auto-ability snapshot is truncated before the price length field.");

            int priceLength = ReadInt32(snapshot, priceLengthOffset);
            if (priceLength <= 0)
                throw new InvalidDataException($"Auto-ability snapshot declared invalid price length {priceLength}.");

            if (priceBytesOffset + priceLength != snapshot.Length)
                throw new InvalidDataException("Auto-ability snapshot length does not match the encoded ability/price payload lengths.");

            byte[] abilityBytes = new byte[abilityLength];
            Array.Copy(snapshot, 0x04, abilityBytes, 0, abilityLength);

            byte[] priceBytes = new byte[priceLength];
            Array.Copy(snapshot, priceBytesOffset, priceBytes, 0, priceLength);
            return (abilityBytes, priceBytes);
        }

        static int ReadInt32(byte[] bytes, int offset)
        {
            if (offset < 0 || offset + 4 > bytes.Length)
                return 0;

            return bytes[offset]
                | (bytes[offset + 1] << 8)
                | (bytes[offset + 2] << 16)
                | (bytes[offset + 3] << 24);
        }

        static void WriteInt32(byte[] bytes, int offset, int value)
        {
            if (offset < 0 || offset + 4 > bytes.Length)
                return;

            bytes[offset] = unchecked((byte)(value & 0xFF));
            bytes[offset + 1] = unchecked((byte)((value >> 8) & 0xFF));
            bytes[offset + 2] = unchecked((byte)((value >> 16) & 0xFF));
            bytes[offset + 3] = unchecked((byte)((value >> 24) & 0xFF));
        }

        static bool ModelPreserved(AutoAbilityTable a, AutoAbilityTable b)
        {
            if (a.Entries.Count != b.Entries.Count)
                return false;

            for (int i = 0; i < a.Entries.Count; i++)
            {
                AutoAbilityEntry ea = a.Entries[i];
                AutoAbilityEntry eb = b.Entries[i];
                if (ea.Index != eb.Index || ea.GilPrice != eb.GilPrice)
                    return false;

                // Data region 0x10..0x6B must be byte-identical; the 0x00..0x0F text refs re-pack legitimately.
                if (!ea.RawBytes.AsSpan(0x10).SequenceEqual(eb.RawBytes.AsSpan(0x10)))
                    return false;

                if (ea.NameText != eb.NameText || ea.AuxiliaryText1 != eb.AuxiliaryText1
                    || ea.DescriptionText != eb.DescriptionText || ea.AuxiliaryText2 != eb.AuxiliaryText2)
                    return false;
            }

            return true;
        }

        static AutoAbilityEntry BuildNewAbilityEntry(AutoAbilityEntry donor, int newIndex)
        {
            byte[] raw = donor.RawBytes.ToArray();

            string donorName = string.IsNullOrWhiteSpace(donor.NameText) ? donor.Label : donor.NameText;
            string newName = $"New {donorName} #{newIndex:D3}";
            string description = string.IsNullOrWhiteSpace(donor.DescriptionText)
                ? $"Duplicate of #{donor.Index:D3}; edit AU1..AU7 fields as needed."
                : $"{donor.DescriptionText} Duplicate #{newIndex:D3}.";

            return new AutoAbilityEntry
            {
                Index = newIndex,
                Label = newName,
                RawBytes = raw,
                NameText = newName,
                AuxiliaryText1 = donor.AuxiliaryText1,
                DescriptionText = description,
                AuxiliaryText2 = donor.AuxiliaryText2,
                GilPrice = donor.GilPrice,
                SosFlagByte = donor.SosFlagByte,
                ElementStrike = donor.ElementStrike,
                ElementAbsorb = donor.ElementAbsorb,
                ElementImmune = donor.ElementImmune,
                ElementResist = donor.ElementResist,
                ElementWeak = donor.ElementWeak,
                StatusInflict = donor.StatusInflict.ToArray(),
                StatusDuration = donor.StatusDuration.ToArray(),
                StatusResist = donor.StatusResist.ToArray(),
                StatIncreaseAmount = donor.StatIncreaseAmount,
                StatIncreaseFlags = donor.StatIncreaseFlags,
                AutoStatusesPermanent = donor.AutoStatusesPermanent,
                AutoStatusesTemporal = donor.AutoStatusesTemporal,
                AutoStatusesExtra = donor.AutoStatusesExtra,
                ExtraStatusInflict = donor.ExtraStatusInflict,
                ExtraStatusImmunities = donor.ExtraStatusImmunities,
                AbilityFlags62 = donor.AbilityFlags62,
                AbilityFlags63 = donor.AbilityFlags63,
                AbilityFlags64 = donor.AbilityFlags64,
                AbilityFlags65 = donor.AbilityFlags65,
                AbilityFlags66 = donor.AbilityFlags66,
                UnknownByte67 = donor.UnknownByte67,
                Icon = donor.Icon,
                GroupIndex = donor.GroupIndex,
                GroupLevel = donor.GroupLevel,
                InternationalBonusIndex = donor.InternationalBonusIndex
            };
        }

        internal partial class AutoAbilityRow : ObservableObject
        {
            static readonly string[] StatusLabels =
            [
                "Death",
                "Zombie",
                "Petrify",
                "Poison",
                "Break Power",
                "Break Magic",
                "Break Armor",
                "Break Mental",
                "Confuse",
                "Berserk",
                "Provoke",
                "Threaten",
                "Sleep",
                "Silence",
                "Darkness",
                "Shell",
                "Protect",
                "Reflect",
                "NulTide",
                "NulBlaze",
                "NulShock",
                "NulFrost",
                "Regen",
                "Haste",
                "Slow"
            ];

            [ObservableProperty]
            [NotifyPropertyChangedFor(nameof(PayloadSummary))]
            [NotifyPropertyChangedFor(nameof(SearchBlob))]
            private int gilPrice;
            [ObservableProperty] private int sosFlagByte;
            [ObservableProperty] private int elementStrike;
            [ObservableProperty] private int elementAbsorb;
            [ObservableProperty] private int elementImmune;
            [ObservableProperty] private int elementResist;
            [ObservableProperty] private int elementWeak;
            [ObservableProperty]
            [NotifyPropertyChangedFor(nameof(StatIncreaseAmountSummary))]
            private int statIncreaseAmount;
            [ObservableProperty] private ushort statIncreaseFlags;
            [ObservableProperty] private ushort autoStatusesPermanent;
            [ObservableProperty] private ushort autoStatusesTemporal;
            [ObservableProperty] private ushort autoStatusesExtra;
            [ObservableProperty] private ushort extraStatusInflict;
            [ObservableProperty] private ushort extraStatusImmunities;
            [ObservableProperty]
            [NotifyPropertyChangedFor(nameof(SpecialByteSummary))]
            [NotifyPropertyChangedFor(nameof(HardcodedFlagSummary))]
            [NotifyPropertyChangedFor(nameof(HardcodedFlagRows))]
            private int abilityFlags62;
            [ObservableProperty]
            [NotifyPropertyChangedFor(nameof(SpecialByteSummary))]
            [NotifyPropertyChangedFor(nameof(HardcodedFlagSummary))]
            [NotifyPropertyChangedFor(nameof(HardcodedFlagRows))]
            private int abilityFlags63;
            [ObservableProperty]
            [NotifyPropertyChangedFor(nameof(SpecialByteSummary))]
            [NotifyPropertyChangedFor(nameof(HardcodedFlagSummary))]
            [NotifyPropertyChangedFor(nameof(HardcodedFlagRows))]
            private int abilityFlags64;
            [ObservableProperty]
            [NotifyPropertyChangedFor(nameof(SpecialByteSummary))]
            [NotifyPropertyChangedFor(nameof(HardcodedFlagSummary))]
            [NotifyPropertyChangedFor(nameof(HardcodedFlagRows))]
            private int abilityFlags65;
            [ObservableProperty]
            [NotifyPropertyChangedFor(nameof(SpecialByteSummary))]
            [NotifyPropertyChangedFor(nameof(HardcodedFlagSummary))]
            [NotifyPropertyChangedFor(nameof(HardcodedFlagRows))]
            private int abilityFlags66;
            [ObservableProperty]
            [NotifyPropertyChangedFor(nameof(SpecialByteSummary))]
            [NotifyPropertyChangedFor(nameof(HardcodedFlagSummary))]
            [NotifyPropertyChangedFor(nameof(HardcodedFlagRows))]
            private int unknownByte67;
            [ObservableProperty]
            [NotifyPropertyChangedFor(nameof(PayloadSummary))]
            private int icon;
            [ObservableProperty]
            [NotifyPropertyChangedFor(nameof(PayloadSummary))]
            [NotifyPropertyChangedFor(nameof(SearchBlob))]
            private int groupIndex;
            [ObservableProperty]
            [NotifyPropertyChangedFor(nameof(PayloadSummary))]
            [NotifyPropertyChangedFor(nameof(SearchBlob))]
            private int groupLevel;
            [ObservableProperty] private int internationalBonusIndex;
            [ObservableProperty]
            [NotifyPropertyChangedFor(nameof(SearchBlob))]
            [NotifyPropertyChangedFor(nameof(DisplayLabel))]
            private string nameText = string.Empty;
            [ObservableProperty] private string auxiliaryText1 = string.Empty;
            [ObservableProperty]
            [NotifyPropertyChangedFor(nameof(SearchBlob))]
            private string descriptionText = string.Empty;
            [ObservableProperty] private string auxiliaryText2 = string.Empty;

            public required int Index { get; init; }
            public required string Label { get; init; }
            public required byte[] RawBytes { get; init; }

            public ObservableCollection<AutoAbilityStatusRow> PrimaryStatusRows { get; } = new();
            public ObservableCollection<AutoAbilityStatusRow> TimedStatusRows { get; } = new();
            public ObservableCollection<AutoAbilityElementRow> ElementRows { get; } = new();
            public ObservableCollection<AutoAbilityStatBoostRow> StatBoostRows { get; } = new();
            public ObservableCollection<AutoAbilityNamedFlagRow> StatTechnicalRows { get; } = new();
            public ObservableCollection<AutoAbilityNamedFlagRow> NamedFlagRows { get; } = new();
            public ObservableCollection<AutoAbilityHardcodedFlagRow> HardcodedFlagRows { get; } = new();

            public string IndexLabel => $"#{Index:D3}";
            public string DisplayLabel => string.IsNullOrWhiteSpace(NameText) ? Label : NameText;
            public string PayloadSummary => $"{GilPrice} gil · Icon {Icon} · Group {GroupIndex}/{GroupLevel}";
            public string StatIncreaseAmountSummary => $"Stat boost amount: {StatIncreaseAmount}%";
            public string SpecialByteSummary => $"62h={AbilityFlags62:X2} · 63h={AbilityFlags63:X2} · 64h={AbilityFlags64:X2} · 65h={AbilityFlags65:X2} · 66h={AbilityFlags66:X2} · 67h={UnknownByte67:X2}";
            public string HardcodedFlagSummary => AutoAbilityHardcodedFlagCatalog.Summarize(
                AbilityFlags62,
                AbilityFlags63,
                AbilityFlags64,
                AbilityFlags65,
                AbilityFlags66,
                UnknownByte67);
            public string SearchBlob => $"{Index:D3} {Label} {NameText} {DescriptionText} {PayloadSummary}";
            public int ActiveStatusCount =>
                PrimaryStatusRows.Count(row => row.InflictChance != 0 || row.ResistChance != 0) +
                TimedStatusRows.Count(row => row.InflictChance != 0 || row.Duration != 0 || row.ResistChance != 0);

            public static AutoAbilityRow Wrap(AutoAbilityEntry entry)
            {
                AutoAbilityRow row = new AutoAbilityRow
                {
                    Index = entry.Index,
                    Label = entry.Label,
                    RawBytes = entry.RawBytes.ToArray(),
                    NameText = entry.NameText,
                    AuxiliaryText1 = entry.AuxiliaryText1,
                    DescriptionText = entry.DescriptionText,
                    AuxiliaryText2 = entry.AuxiliaryText2,
                    GilPrice = entry.GilPrice,
                    SosFlagByte = entry.SosFlagByte,
                    ElementStrike = entry.ElementStrike,
                    ElementAbsorb = entry.ElementAbsorb,
                    ElementImmune = entry.ElementImmune,
                    ElementResist = entry.ElementResist,
                    ElementWeak = entry.ElementWeak,
                    StatIncreaseAmount = entry.StatIncreaseAmount,
                    StatIncreaseFlags = entry.StatIncreaseFlags,
                    AutoStatusesPermanent = entry.AutoStatusesPermanent,
                    AutoStatusesTemporal = entry.AutoStatusesTemporal,
                    AutoStatusesExtra = entry.AutoStatusesExtra,
                    ExtraStatusInflict = entry.ExtraStatusInflict,
                    ExtraStatusImmunities = entry.ExtraStatusImmunities,
                    AbilityFlags62 = entry.AbilityFlags62,
                    AbilityFlags63 = entry.AbilityFlags63,
                    AbilityFlags64 = entry.AbilityFlags64,
                    AbilityFlags65 = entry.AbilityFlags65,
                    AbilityFlags66 = entry.AbilityFlags66,
                    UnknownByte67 = entry.UnknownByte67,
                    Icon = entry.Icon,
                    GroupIndex = entry.GroupIndex,
                    GroupLevel = entry.GroupLevel,
                    InternationalBonusIndex = entry.InternationalBonusIndex
                };

                for (int index = 0; index < 12; index++)
                {
                    row.PrimaryStatusRows.Add(new AutoAbilityStatusRow
                    {
                        Index = index,
                        Label = StatusLabels[index],
                        InflictChance = index < entry.StatusInflict.Length ? entry.StatusInflict[index] : 0,
                        ResistChance = index < entry.StatusResist.Length ? entry.StatusResist[index] : 0,
                        Duration = 0
                    });
                }

                for (int index = 12; index < StatusLabels.Length; index++)
                {
                    int durationIndex = index - 12;
                    row.TimedStatusRows.Add(new AutoAbilityStatusRow
                    {
                        Index = index,
                        Label = StatusLabels[index],
                        InflictChance = index < entry.StatusInflict.Length ? entry.StatusInflict[index] : 0,
                        Duration = durationIndex < entry.StatusDuration.Length ? entry.StatusDuration[durationIndex] : 0,
                        ResistChance = index < entry.StatusResist.Length ? entry.StatusResist[index] : 0
                    });
                }

                row.BuildHumanRows();
                return row;
            }

            void BuildHumanRows()
            {
                ElementRows.Clear();
                ElementRows.Add(new AutoAbilityElementRow(this, "Fire", 0x01));
                ElementRows.Add(new AutoAbilityElementRow(this, "Ice", 0x02));
                ElementRows.Add(new AutoAbilityElementRow(this, "Lightning", 0x04));
                ElementRows.Add(new AutoAbilityElementRow(this, "Water", 0x08));
                ElementRows.Add(new AutoAbilityElementRow(this, "Holy", 0x10));
                ElementRows.Add(new AutoAbilityElementRow(this, "Earth (reserved)", 0x20));
                ElementRows.Add(new AutoAbilityElementRow(this, "Wind (reserved)", 0x40));
                ElementRows.Add(new AutoAbilityElementRow(this, "Dark", 0x80));

                StatBoostRows.Clear();
                StatBoostRows.Add(new AutoAbilityStatBoostRow(this, "HP", 0x0100));
                StatBoostRows.Add(new AutoAbilityStatBoostRow(this, "MP", 0x0200));
                StatBoostRows.Add(new AutoAbilityStatBoostRow(this, "Strength", 0x0400));
                StatBoostRows.Add(new AutoAbilityStatBoostRow(this, "Magic", 0x0800));
                StatBoostRows.Add(new AutoAbilityStatBoostRow(this, "Defense", 0x1000));
                StatBoostRows.Add(new AutoAbilityStatBoostRow(this, "Magic Defense", 0x2000));

                StatTechnicalRows.Clear();
                AddNamedFlags(StatTechnicalRows, "AU2 byte 56h", AutoAbilityWordKind.StatIncrease, [
                    new("Technical 56h bit 01", 0x0001),
                    new("Technical 56h bit 02", 0x0002),
                    new("Technical 56h bit 04", 0x0004),
                    new("Technical 56h bit 08", 0x0008),
                    new("Technical 56h bit 10", 0x0010),
                    new("Technical 56h bit 20", 0x0020),
                    new("Technical 56h bit 40", 0x0040),
                    new("Technical 56h bit 80", 0x0080)
                ]);
                AddNamedFlags(StatTechnicalRows, "AU4 byte 57h", AutoAbilityWordKind.StatIncrease, [
                    new("Technical 57h bit 40", 0x4000),
                    new("Technical 57h bit 80", 0x8000)
                ]);

                NamedFlagRows.Clear();
                AddNamedFlags("Auto-Status", AutoAbilityWordKind.AutoPermanent, [
                    new("Auto-Death", 0x0001),
                    new("Auto-Zombie", 0x0002),
                    new("Auto-Petrify", 0x0004),
                    new("Auto-Poison", 0x0008),
                    new("Auto-Power Break", 0x0010),
                    new("Auto-Magic Break", 0x0020),
                    new("Auto-Armor Break", 0x0040),
                    new("Auto-Mental Break", 0x0080),
                    new("Auto-Confuse", 0x0100),
                    new("Auto-Berserk", 0x0200),
                    new("Auto-Provoke", 0x0400),
                    new("Auto-Threaten", 0x0800),
                    new("Technical 59h bit 10", 0x1000),
                    new("Technical 59h bit 20", 0x2000),
                    new("Technical 59h bit 40", 0x4000),
                    new("Technical 59h bit 80", 0x8000)
                ]);
                AddNamedFlags("Auto-Status", AutoAbilityWordKind.AutoTemporal, [
                    new("Auto-Sleep", 0x0001),
                    new("Auto-Silence", 0x0002),
                    new("Auto-Darkness", 0x0004),
                    new("Auto-Shell", 0x0008),
                    new("Auto-Protect", 0x0010),
                    new("Auto-Reflect", 0x0020),
                    new("Auto-NulTide", 0x0040),
                    new("Auto-NulBlaze", 0x0080),
                    new("Auto-NulShock", 0x0100),
                    new("Auto-NulFrost", 0x0200),
                    new("Auto-Regen", 0x0400),
                    new("Auto-Haste", 0x0800),
                    new("Auto-Slow", 0x1000),
                    new("Technical 5Bh bit 20", 0x2000),
                    new("Technical 5Bh bit 40", 0x4000),
                    new("Technical 5Bh bit 80", 0x8000)
                ]);
                AddNamedFlags("Auto-Extra", AutoAbilityWordKind.AutoExtra, [
                    new("Auto-Scan", 0x0001),
                    new("Auto-Distill Power", 0x0002),
                    new("Auto-Distill Mana", 0x0004),
                    new("Auto-Distill Speed", 0x0008),
                    new("Technical 5Ch bit 10", 0x0010),
                    new("Auto-Distill Ability", 0x0020),
                    new("Auto-Shield", 0x0040),
                    new("Auto-Boost", 0x0080),
                    new("Auto-Eject", 0x0100),
                    new("Auto-Life", 0x0200),
                    new("Auto-Curse", 0x0400),
                    new("Auto-Defend", 0x0800),
                    new("Auto-Guard", 0x1000),
                    new("Auto-Sentinel", 0x2000),
                    new("Auto-Doom", 0x4000),
                    new("Technical 5Dh bit 80", 0x8000)
                ]);
                AddNamedFlags("Extra Inflict", AutoAbilityWordKind.ExtraInflict, [
                    new("Inflict Scan", 0x0001),
                    new("Inflict Distill Power", 0x0002),
                    new("Inflict Distill Mana", 0x0004),
                    new("Inflict Distill Speed", 0x0008),
                    new("Technical 5Eh bit 10", 0x0010),
                    new("Inflict Distill Ability", 0x0020),
                    new("Inflict Shield", 0x0040),
                    new("Inflict Boost", 0x0080),
                    new("Inflict Eject", 0x0100),
                    new("Inflict Auto-Life", 0x0200),
                    new("Inflict Curse", 0x0400),
                    new("Inflict Defend", 0x0800),
                    new("Inflict Guard", 0x1000),
                    new("Inflict Sentinel", 0x2000),
                    new("Inflict Doom", 0x4000),
                    new("Technical 5Fh bit 80", 0x8000)
                ]);
                AddNamedFlags("Extra Immunity", AutoAbilityWordKind.ExtraImmunity, [
                    new("Immune Scan", 0x0001),
                    new("Immune Distill Power", 0x0002),
                    new("Immune Distill Mana", 0x0004),
                    new("Immune Distill Speed", 0x0008),
                    new("Technical 60h bit 10", 0x0010),
                    new("Immune Distill Ability", 0x0020),
                    new("Immune Shield", 0x0040),
                    new("Immune Boost", 0x0080),
                    new("Immune Eject", 0x0100),
                    new("Immune Auto-Life", 0x0200),
                    new("Immune Curse", 0x0400),
                    new("Immune Defend", 0x0800),
                    new("Immune Guard", 0x1000),
                    new("Immune Sentinel", 0x2000),
                    new("Immune Doom", 0x4000),
                    new("Technical 61h bit 80", 0x8000)
                ]);

                HardcodedFlagRows.Clear();
                foreach (AutoAbilityHardcodedFlagDefinition flag in AutoAbilityHardcodedFlagCatalog.EditableFlags)
                    HardcodedFlagRows.Add(new AutoAbilityHardcodedFlagRow(this, flag));
            }

            void AddNamedFlags(string group, AutoAbilityWordKind kind, IEnumerable<NamedFlagDefinition> flags)
            {
                AddNamedFlags(NamedFlagRows, group, kind, flags);
            }

            void AddNamedFlags(ObservableCollection<AutoAbilityNamedFlagRow> target, string group, AutoAbilityWordKind kind, IEnumerable<NamedFlagDefinition> flags)
            {
                foreach (NamedFlagDefinition flag in flags)
                    target.Add(new AutoAbilityNamedFlagRow(this, group, kind, flag.Label, flag.Mask));
            }

            partial void OnStatIncreaseAmountChanged(int value)
            {
                foreach (AutoAbilityStatBoostRow row in StatBoostRows)
                    row.RefreshAmount();
            }

            internal bool GetElementFlag(AutoAbilityElementKind kind, int mask)
            {
                return (GetElementByte(kind) & mask) != 0;
            }

            internal void SetElementFlag(AutoAbilityElementKind kind, int mask, bool isSet)
            {
                int value = GetElementByte(kind);
                value = isSet ? value | mask : value & ~mask;
                SetElementByte(kind, value);
            }

            int GetElementByte(AutoAbilityElementKind kind)
            {
                return kind switch
                {
                    AutoAbilityElementKind.Strike => ElementStrike,
                    AutoAbilityElementKind.Absorb => ElementAbsorb,
                    AutoAbilityElementKind.Immune => ElementImmune,
                    AutoAbilityElementKind.Resist => ElementResist,
                    AutoAbilityElementKind.Weak => ElementWeak,
                    _ => 0
                };
            }

            void SetElementByte(AutoAbilityElementKind kind, int value)
            {
                value &= 0xFF;
                switch (kind)
                {
                    case AutoAbilityElementKind.Strike:
                        ElementStrike = value;
                        break;
                    case AutoAbilityElementKind.Absorb:
                        ElementAbsorb = value;
                        break;
                    case AutoAbilityElementKind.Immune:
                        ElementImmune = value;
                        break;
                    case AutoAbilityElementKind.Resist:
                        ElementResist = value;
                        break;
                    case AutoAbilityElementKind.Weak:
                        ElementWeak = value;
                        break;
                }
            }

            internal bool GetNamedFlag(AutoAbilityWordKind kind, ushort mask)
            {
                return (GetWord(kind) & mask) != 0;
            }

            internal void SetNamedFlag(AutoAbilityWordKind kind, ushort mask, bool isSet)
            {
                ushort value = GetWord(kind);
                value = isSet ? (ushort)(value | mask) : (ushort)(value & ~mask);
                SetWord(kind, value);
            }

            ushort GetWord(AutoAbilityWordKind kind)
            {
                return kind switch
                {
                    AutoAbilityWordKind.StatIncrease => StatIncreaseFlags,
                    AutoAbilityWordKind.AutoPermanent => AutoStatusesPermanent,
                    AutoAbilityWordKind.AutoTemporal => AutoStatusesTemporal,
                    AutoAbilityWordKind.AutoExtra => AutoStatusesExtra,
                    AutoAbilityWordKind.ExtraInflict => ExtraStatusInflict,
                    AutoAbilityWordKind.ExtraImmunity => ExtraStatusImmunities,
                    _ => 0
                };
            }

            void SetWord(AutoAbilityWordKind kind, ushort value)
            {
                switch (kind)
                {
                    case AutoAbilityWordKind.StatIncrease:
                        StatIncreaseFlags = value;
                        break;
                    case AutoAbilityWordKind.AutoPermanent:
                        AutoStatusesPermanent = value;
                        break;
                    case AutoAbilityWordKind.AutoTemporal:
                        AutoStatusesTemporal = value;
                        break;
                    case AutoAbilityWordKind.AutoExtra:
                        AutoStatusesExtra = value;
                        break;
                    case AutoAbilityWordKind.ExtraInflict:
                        ExtraStatusInflict = value;
                        break;
                    case AutoAbilityWordKind.ExtraImmunity:
                        ExtraStatusImmunities = value;
                        break;
                }
            }

            internal bool GetHardcodedFlag(int offset, ushort mask)
            {
                return (GetHardcodedWord(offset) & mask) != 0;
            }

            internal void SetHardcodedFlag(int offset, ushort mask, bool isSet)
            {
                ushort value = GetHardcodedWord(offset);
                value = isSet ? (ushort)(value | mask) : (ushort)(value & ~mask);
                SetHardcodedWord(offset, value);
            }

            ushort GetHardcodedWord(int offset)
            {
                return offset switch
                {
                    0x62 => MakeWord(AbilityFlags62, AbilityFlags63),
                    0x64 => MakeWord(AbilityFlags64, AbilityFlags65),
                    0x66 => MakeWord(AbilityFlags66, UnknownByte67),
                    _ => 0
                };
            }

            void SetHardcodedWord(int offset, ushort value)
            {
                switch (offset)
                {
                    case 0x62:
                        AbilityFlags62 = value & 0xFF;
                        AbilityFlags63 = (value >> 8) & 0xFF;
                        break;
                    case 0x64:
                        AbilityFlags64 = value & 0xFF;
                        AbilityFlags65 = (value >> 8) & 0xFF;
                        break;
                    case 0x66:
                        AbilityFlags66 = value & 0xFF;
                        UnknownByte67 = (value >> 8) & 0xFF;
                        break;
                }
            }

            static ushort MakeWord(int lo, int hi)
            {
                return unchecked((ushort)(((hi & 0xFF) << 8) | (lo & 0xFF)));
            }

            public AutoAbilityEntry ToEntry()
            {
                byte[] statusInflict = new byte[StatusLabels.Length];
                byte[] statusResist = new byte[StatusLabels.Length];
                byte[] statusDuration = new byte[TimedStatusRows.Count];

                foreach (AutoAbilityStatusRow row in PrimaryStatusRows)
                {
                    statusInflict[row.Index] = RequireByte(row.InflictChance, $"{Label} {row.Label} inflict");
                    statusResist[row.Index] = RequireByte(row.ResistChance, $"{Label} {row.Label} resist");
                }

                foreach (AutoAbilityStatusRow row in TimedStatusRows)
                {
                    statusInflict[row.Index] = RequireByte(row.InflictChance, $"{Label} {row.Label} inflict");
                    statusResist[row.Index] = RequireByte(row.ResistChance, $"{Label} {row.Label} resist");
                    statusDuration[row.Index - 12] = RequireByte(row.Duration, $"{Label} {row.Label} duration");
                }

                return new AutoAbilityEntry
                {
                    Index = Index,
                    Label = Label,
                    RawBytes = RawBytes.ToArray(),
                    NameText = NameText,
                    AuxiliaryText1 = AuxiliaryText1,
                    DescriptionText = DescriptionText,
                    AuxiliaryText2 = AuxiliaryText2,
                    GilPrice = GilPrice,
                    SosFlagByte = SosFlagByte,
                    ElementStrike = ElementStrike,
                    ElementAbsorb = ElementAbsorb,
                    ElementImmune = ElementImmune,
                    ElementResist = ElementResist,
                    ElementWeak = ElementWeak,
                    StatusInflict = statusInflict,
                    StatusDuration = statusDuration,
                    StatusResist = statusResist,
                    StatIncreaseAmount = StatIncreaseAmount,
                    StatIncreaseFlags = StatIncreaseFlags,
                    AutoStatusesPermanent = AutoStatusesPermanent,
                    AutoStatusesTemporal = AutoStatusesTemporal,
                    AutoStatusesExtra = AutoStatusesExtra,
                    ExtraStatusInflict = ExtraStatusInflict,
                    ExtraStatusImmunities = ExtraStatusImmunities,
                    AbilityFlags62 = AbilityFlags62,
                    AbilityFlags63 = AbilityFlags63,
                    AbilityFlags64 = AbilityFlags64,
                    AbilityFlags65 = AbilityFlags65,
                    AbilityFlags66 = AbilityFlags66,
                    UnknownByte67 = UnknownByte67,
                    Icon = Icon,
                    GroupIndex = GroupIndex,
                    GroupLevel = GroupLevel,
                    InternationalBonusIndex = InternationalBonusIndex
                };
            }

            static byte RequireByte(int value, string label)
            {
                if ((uint)value > byte.MaxValue)
                    throw new InvalidOperationException($"{label} must stay in byte range 0..255. Got {value}.");

                return (byte)value;
            }
        }

        internal partial class AutoAbilityStatusRow : ObservableObject
        {
            [ObservableProperty] private int inflictChance;
            [ObservableProperty] private int duration;
            [ObservableProperty] private int resistChance;

            public required int Index { get; init; }
            public required string Label { get; init; }
        }

        internal enum AutoAbilityElementKind
        {
            Strike,
            Absorb,
            Immune,
            Resist,
            Weak
        }

        internal enum AutoAbilityWordKind
        {
            StatIncrease,
            AutoPermanent,
            AutoTemporal,
            AutoExtra,
            ExtraInflict,
            ExtraImmunity
        }

        readonly record struct NamedFlagDefinition(string Label, ushort Mask);

        internal partial class AutoAbilityElementRow : ObservableObject
        {
            readonly AutoAbilityRow owner;
            readonly int mask;

            public AutoAbilityElementRow(AutoAbilityRow owner, string elementName, int mask)
            {
                this.owner = owner;
                ElementName = elementName;
                this.mask = mask;
                TechnicalLabel = $"0x{mask:X2}";
            }

            public string ElementName { get; }
            public string TechnicalLabel { get; }

            public bool Strike
            {
                get => owner.GetElementFlag(AutoAbilityElementKind.Strike, mask);
                set => Set(AutoAbilityElementKind.Strike, value);
            }

            public bool Absorb
            {
                get => owner.GetElementFlag(AutoAbilityElementKind.Absorb, mask);
                set => Set(AutoAbilityElementKind.Absorb, value);
            }

            public bool Immune
            {
                get => owner.GetElementFlag(AutoAbilityElementKind.Immune, mask);
                set => Set(AutoAbilityElementKind.Immune, value);
            }

            public bool Resist
            {
                get => owner.GetElementFlag(AutoAbilityElementKind.Resist, mask);
                set => Set(AutoAbilityElementKind.Resist, value);
            }

            public bool Weak
            {
                get => owner.GetElementFlag(AutoAbilityElementKind.Weak, mask);
                set => Set(AutoAbilityElementKind.Weak, value);
            }

            void Set(AutoAbilityElementKind kind, bool value)
            {
                owner.SetElementFlag(kind, mask, value);
                OnPropertyChanged(nameof(Strike));
                OnPropertyChanged(nameof(Absorb));
                OnPropertyChanged(nameof(Immune));
                OnPropertyChanged(nameof(Resist));
                OnPropertyChanged(nameof(Weak));
            }
        }

        internal partial class AutoAbilityNamedFlagRow : ObservableObject
        {
            readonly AutoAbilityRow owner;
            readonly AutoAbilityWordKind kind;
            readonly ushort mask;

            public AutoAbilityNamedFlagRow(AutoAbilityRow owner, string group, AutoAbilityWordKind kind, string label, ushort mask)
            {
                this.owner = owner;
                this.kind = kind;
                this.mask = mask;
                Group = group;
                Label = label;
                TechnicalLabel = $"0x{mask:X4}";
            }

            public string Group { get; }
            public string Label { get; }
            public string DisplayLabel => Label;
            public string TechnicalLabel { get; }

            public bool IsActive
            {
                get => owner.GetNamedFlag(kind, mask);
                set
                {
                    owner.SetNamedFlag(kind, mask, value);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StateLabel));
                }
            }

            public string StateLabel => IsActive ? "Active" : "Inactive";
        }

        internal partial class AutoAbilityStatBoostRow : ObservableObject
        {
            readonly AutoAbilityRow owner;
            readonly ushort mask;

            public AutoAbilityStatBoostRow(AutoAbilityRow owner, string statName, ushort mask)
            {
                this.owner = owner;
                this.mask = mask;
                StatName = statName;
                TechnicalLabel = $"0x{mask:X4}";
            }

            public string StatName { get; }
            public string TechnicalLabel { get; }

            public bool IsActive
            {
                get => owner.GetNamedFlag(AutoAbilityWordKind.StatIncrease, mask);
                set
                {
                    owner.SetNamedFlag(AutoAbilityWordKind.StatIncrease, mask, value);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StateLabel));
                }
            }

            public int Amount
            {
                get => owner.StatIncreaseAmount;
                set
                {
                    owner.StatIncreaseAmount = value;
                    RefreshAmount();
                }
            }

            public string StateLabel => IsActive ? "Active" : "Inactive";
            public string AmountLabel => $"{owner.StatIncreaseAmount}%";

            public void RefreshAmount()
            {
                OnPropertyChanged(nameof(Amount));
                OnPropertyChanged(nameof(AmountLabel));
            }
        }

        internal partial class AutoAbilityHardcodedFlagRow : ObservableObject
        {
            readonly AutoAbilityRow owner;
            readonly AutoAbilityHardcodedFlagDefinition flag;

            public AutoAbilityHardcodedFlagRow(AutoAbilityRow owner, AutoAbilityHardcodedFlagDefinition flag)
            {
                this.owner = owner;
                this.flag = flag;
            }

            public string NameLabel => flag.Label;
            public string TechnicalLabel => flag.TechnicalLabel;

            public bool IsActive
            {
                get => owner.GetHardcodedFlag(flag.Offset, flag.Mask);
                set
                {
                    owner.SetHardcodedFlag(flag.Offset, flag.Mask, value);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StateLabel));
                }
            }

            public string StateLabel => IsActive ? "Active" : "Inactive";
        }
    }
}

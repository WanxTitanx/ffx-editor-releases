using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.FfxLib.WeaponGear;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Modules.WeaponGear
{
    internal partial class WeaponGear_DataModel : ObservableObject
    {
        const string WeaponGearRelativePath = @"jppc\battle\kernel\weapon.bin";

        WeaponGearTable? currentTable;
        string? currentFilePath;

        public ObservableCollection<WeaponGearRow> DisplayedEntries { get; } = [];

        [ObservableProperty] private string loadSummary = "Loading weapon.bin...";
        [ObservableProperty] private string sourcePath = "";
        [ObservableProperty] private string scopeSummary = "Weapon Gear Atlas — master weapon/armor catalog writer.";
        [ObservableProperty] private string shapeSummary = "Shape not loaded yet.";
        [ObservableProperty] private string selectedEntrySummary = "Select an entry to inspect.";
        [ObservableProperty] private WeaponGearRow? selectedEntry;
        [ObservableProperty] private string filterText = string.Empty;
        [ObservableProperty] private string filterSummary = "Filter is idle.";
        [ObservableProperty] private string entryCountSummary = "";
        [ObservableProperty] private string writerMode = "WRITER";
        [ObservableProperty] private bool isDirty;

        public WeaponGear_DataModel()
        {
            RefreshFromDisk();
        }

        partial void OnFilterTextChanged(string value) => ApplyFilter();

        partial void OnSelectedEntryChanged(WeaponGearRow? value)
        {
            SelectedEntrySummary = value?.Detail ?? "Select an entry to inspect.";
        }

        public void RefreshFromDisk()
        {
            DisplayedEntries.Clear();
            SelectedEntry = null;
            IsDirty = false;

            currentFilePath = ResolveMasterFile(WeaponGearRelativePath);
            if (currentFilePath == null)
            {
                SourcePath = "";
                LoadSummary = "weapon.bin was not found.";
                ShapeSummary = "Expected <master>\\jppc\\battle\\kernel\\weapon.bin.";
                ScopeSummary = "weapon.bin not loaded.";
                return;
            }

            SourcePath = currentFilePath;

            try
            {
                byte[] weaponBytes = File.ReadAllBytes(currentFilePath);
                currentTable = WeaponGear_File.Read(weaponBytes);

                foreach (var kvp in currentTable.EntriesByIndex.OrderBy(e => e.Key))
                {
                    DisplayedEntries.Add(new WeaponGearRow(kvp.Value, this));
                }

                UpdateSummaries();
                ApplyFilter();
            }
            catch (Exception ex)
            {
                LoadSummary = $"Failed to load weapon.bin: {ex.Message}";
                ShapeSummary = "Parser error.";
            }
        }

        public void Save()
        {
            if (currentTable == null || currentFilePath == null) return;

            try
            {
                byte[] output = WeaponGear_File.Write(currentTable);
                File.WriteAllBytes(currentFilePath, output);
                currentTable.OriginalBytes = output;
                IsDirty = false;
                LoadSummary = $"Saved to disk ({DisplayedEntries.Count} entries).";
                RefreshLabels();
            }
            catch (Exception ex)
            {
                LoadSummary = $"Save failed: {ex.Message}";
            }
        }

        public void Discard()
        {
            RefreshFromDisk();
        }

        public void AddNewEntry(WeaponGearEntry template)
        {
            if (currentTable == null) return;

            int newIndex = currentTable.MaxIndex + 1;
            WeaponGearEntry newEntry = WeaponGearEntry.CloneFrom(template, newIndex);
            byte[] grownBytes = WeaponGear_File.GrowByOne(currentTable, newEntry);

            currentTable = WeaponGear_File.Read(grownBytes);
            DisplayedEntries.Clear();
            foreach (var kvp in currentTable.EntriesByIndex.OrderBy(e => e.Key))
            {
                DisplayedEntries.Add(new WeaponGearRow(kvp.Value, this));
            }

            IsDirty = true;
            UpdateSummaries();
            ApplyFilter();
            LoadSummary = $"Entry #{newIndex} added (pending save).";
        }

        public void MarkDirty()
        {
            IsDirty = true;
            RefreshLabels();
        }

        void UpdateSummaries()
        {
            if (currentTable == null) return;
            LoadSummary = $"weapon.bin loaded: {currentTable.EntryCount} entries (idx {currentTable.MinIndex}–{currentTable.MaxIndex}).";
            ShapeSummary = $"Header: 0x14 bytes, entry: 0x{currentTable.Header.EntryLength:X2} bytes, data: 0x{currentTable.Header.TotalDataLength:X4} bytes.";
            EntryCountSummary = $"{currentTable.EntryCount} entries · idx {currentTable.MinIndex}–{currentTable.MaxIndex}";
        }

        void RefreshLabels()
        {
            foreach (var row in DisplayedEntries)
                row.RefreshLabels();
        }

        void ApplyFilter()
        {
            string filter = FilterText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(filter))
            {
                foreach (var row in DisplayedEntries) row.IsVisible = true;
                FilterSummary = $"{DisplayedEntries.Count} entries, no filter.";
                return;
            }

            int visible = 0;
            foreach (var row in DisplayedEntries)
            {
                bool match = row.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || row.Summary.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || row.Detail.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || row.Index.ToString().Contains(filter);
                row.IsVisible = match;
                if (match) visible++;
            }
            FilterSummary = $"{visible}/{DisplayedEntries.Count} entries match \"{filter}\".";
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
    }

    internal partial class WeaponGearRow : ObservableObject
    {
        readonly WeaponGearEntry entry;
        readonly WeaponGear_DataModel owner;

        [ObservableProperty] private bool isVisible = true;

        public int Index => entry.Index;

        public string Title { get; private set; }
        public string Summary { get; private set; }
        public string Detail { get; private set; }
        public string CharacterName { get; private set; }
        public string GearType { get; private set; }
        public string Abilities { get; private set; }
        public string RawFields { get; private set; }
        public string ModelIdHex { get; private set; }
        public string SaveFields { get; private set; }
        public string Flages { get; private set; } // "Flags" corrected per user demand
        public int Power => entry.Power;
        public int Crit => entry.Crit;
        public int Slots => entry.SlotCount;
        public int Formula => entry.Formula;
        public System.Collections.ObjectModel.ObservableCollection<AbilitySlotDisplay> AbilityNames { get; } = [];

        // ── Editable options (ComboBox sources) ─────────────────────────────

        public IReadOnlyList<OptionItem<byte>> CharacterOptions { get; } = BuildCharacterOptions();
        public IReadOnlyList<OptionItem<byte>> GearTypeOptions { get; } =
            [new("Weapon", 0), new("Armor", 1)];
        public IReadOnlyList<OptionItem<byte>> FormulaOptions { get; } = BuildFormulaOptions();
        public IReadOnlyList<OptionItem<ushort>> AbilityOptions { get; } = BuildAbilityOptions();

        // ── Editable selection (TwoWay bind) ────────────────────────────────

        OptionItem<byte>? selectedCharacter;
        public OptionItem<byte>? SelectedCharacter
        {
            get => selectedCharacter;
            set
            {
                if (value is not null && !Equals(selectedCharacter, value))
                {
                    selectedCharacter = value;
                    RawCharacter = value.Value;
                }
                OnPropertyChanged();
            }
        }

        OptionItem<byte>? selectedGearType;
        public OptionItem<byte>? SelectedGearType
        {
            get => selectedGearType;
            set
            {
                if (value is not null && !Equals(selectedGearType, value))
                {
                    selectedGearType = value;
                    RawIsArmor = value.Value;
                }
                OnPropertyChanged();
            }
        }

        OptionItem<byte>? selectedFormula;
        public OptionItem<byte>? SelectedFormula
        {
            get => selectedFormula;
            set
            {
                if (value is not null && !Equals(selectedFormula, value))
                {
                    selectedFormula = value;
                    RawFormula = value.Value;
                }
                OnPropertyChanged();
            }
        }

        OptionItem<ushort>? selectedAbility1;
        public OptionItem<ushort>? SelectedAbility1
        {
            get => selectedAbility1;
            set
            {
                if (value is not null && !Equals(selectedAbility1, value))
                {
                    selectedAbility1 = value;
                    RawAbility1 = value.Value;
                }
                OnPropertyChanged();
            }
        }

        OptionItem<ushort>? selectedAbility2;
        public OptionItem<ushort>? SelectedAbility2
        {
            get => selectedAbility2;
            set
            {
                if (value is not null && !Equals(selectedAbility2, value))
                {
                    selectedAbility2 = value;
                    RawAbility2 = value.Value;
                }
                OnPropertyChanged();
            }
        }

        OptionItem<ushort>? selectedAbility3;
        public OptionItem<ushort>? SelectedAbility3
        {
            get => selectedAbility3;
            set
            {
                if (value is not null && !Equals(selectedAbility3, value))
                {
                    selectedAbility3 = value;
                    RawAbility3 = value.Value;
                }
                OnPropertyChanged();
            }
        }

        OptionItem<ushort>? selectedAbility4;
        public OptionItem<ushort>? SelectedAbility4
        {
            get => selectedAbility4;
            set
            {
                if (value is not null && !Equals(selectedAbility4, value))
                {
                    selectedAbility4 = value;
                    RawAbility4 = value.Value;
                }
                OnPropertyChanged();
            }
        }

        // ── Editable flags (CheckBox) ───────────────────────────────────────

        public bool IsSummon      { get => (entry.Flags & 0x01) != 0; set => SetFlag(0x01, value); }
        public bool IsHidden      { get => (entry.Flags & 0x02) != 0; set => SetFlag(0x02, value); }
        public bool IsNoKaizou    { get => (entry.Flags & 0x04) != 0; set => SetFlag(0x04, value); }
        public bool IsBrotherhood { get => (entry.Flags & 0x08) != 0; set => SetFlag(0x08, value); }

        void SetFlag(byte mask, bool on)
        {
            byte f = entry.Flags;
            entry.Flags = on ? (byte)(f | mask) : (byte)(f & (byte)~mask);
            OnChanged();
            OnPropertyChanged(nameof(IsSummon));
            OnPropertyChanged(nameof(IsHidden));
            OnPropertyChanged(nameof(IsNoKaizou));
            OnPropertyChanged(nameof(IsBrotherhood));
            OnPropertyChanged(nameof(Flages));
        }

        // ── Template source for "+ Add" (duplicate selected entry) ──────────

        internal WeaponGearEntry Entry => entry;

        static IReadOnlyList<OptionItem<byte>> BuildCharacterOptions()
        {
            var list = new System.Collections.Generic.List<OptionItem<byte>>();
            for (int i = 0; i <= 17; i++)
            {
                string name = i <= 6
                    ? ((Character_Enum)(sbyte)i).ToString()
                    : i switch
                    {
                        7 => "Seymour", 8 => "Valefor", 9 => "Ifrit", 10 => "Ixion",
                        11 => "Shiva", 12 => "Bahamut", 13 => "Anima", 14 => "Yojimbo",
                        15 => "Cindy", 16 => "Sandy", 17 => "Mindy",
                        _ => $"char_{i:X2}h"
                    };
                list.Add(new OptionItem<byte>(name, (byte)i));
            }
            return list;
        }

        static IReadOnlyList<OptionItem<byte>> BuildFormulaOptions()
        {
            var list = new System.Collections.Generic.List<OptionItem<byte>>();
            foreach (var f in Enum.GetValues<DamageFormula_Enum>())
                list.Add(new OptionItem<byte>($"{f} ({f:D})", (byte)f));
            return list;
        }

        static IReadOnlyList<OptionItem<ushort>> BuildAbilityOptions()
        {
            var list = new System.Collections.Generic.List<OptionItem<ushort>>
            {
                new("Empty", 0x00FF)
            };
            foreach (var kvp in AutoAbility_Dictionary.Instance.OrderBy(k => k.Key))
                list.Add(new OptionItem<ushort>($"{kvp.Value} ({kvp.Key})", kvp.Key));
            return list;
        }

        // Property get/set pairs so the UI can bind
        public byte RawCharacter { get => entry.Character; set { entry.Character = value; OnChanged(); } }
        public byte RawIsArmor { get => entry.IsArmor; set { entry.IsArmor = value; OnChanged(); } }
        public byte RawPower { get => entry.Power; set { entry.Power = value; OnChanged(); } }
        public byte RawCrit { get => entry.Crit; set { entry.Crit = value; OnChanged(); } }
        public byte RawSlots { get => entry.SlotCount; set { entry.SlotCount = value; OnChanged(); } }
        public byte RawFormula { get => entry.Formula; set { entry.Formula = value; OnChanged(); } }
        public byte RawFlags { get => entry.Flags; set { entry.Flags = value; OnChanged(); } }
        public ushort RawModelId { get => entry.ModelId; set { entry.ModelId = value; OnChanged(); } }
        public ushort RawAbility1 { get => entry.Ability1; set { entry.Ability1 = value; OnChanged(); } }
        public ushort RawAbility2 { get => entry.Ability2; set { entry.Ability2 = value; OnChanged(); } }
        public ushort RawAbility3 { get => entry.Ability3; set { entry.Ability3 = value; OnChanged(); } }
        public ushort RawAbility4 { get => entry.Ability4; set { entry.Ability4 = value; OnChanged(); } }
        public ushort RawAlwaysZero1 { get => entry.AlwaysZero1; set { entry.AlwaysZero1 = value; OnChanged(); } }
        public byte RawZeroOrOne { get => entry.ZeroOrOne; set { entry.ZeroOrOne = value; OnChanged(); } }
        public ushort RawAlwaysZero2 { get => entry.AlwaysZero2; set { entry.AlwaysZero2 = value; OnChanged(); } }

        public WeaponGearRow(WeaponGearEntry entry, WeaponGear_DataModel owner)
        {
            this.entry = entry;
            this.owner = owner;
            RefreshLabels();
        }

        public void RefreshLabels()
        {
            CharacterName = FormatCharacter(entry.Character);
            GearType = entry.IsArmor == 0 ? "Weapon" : "Armor";
            Abilities = FormatAbilityList(entry.Ability1, entry.Ability2, entry.Ability3, entry.Ability4);
            Flages = FormatFlags(entry.Flags);
            RawFields = $"z1={entry.AlwaysZero1:X4} zO={entry.ZeroOrOne:X2} z2={entry.AlwaysZero2:X4} mId=0x{entry.ModelId:X4}";
            ModelIdHex = entry.ModelId == 0 ? "0x0000 (via w_name.bin)" : $"0x{entry.ModelId:X4}";
            SaveFields = $"nameId={entry.AlwaysZero1:X4} exists={entry.ZeroOrOne:X2} equipped={entry.AlwaysZero2:X4}";
            Title = BuildDisplayLabel();
            Summary = BuildSummary();
            Detail = BuildDetail();

            AbilityNames.Clear();
            AddAbility("S1", entry.Ability1);
            AddAbility("S2", entry.Ability2);
            AddAbility("S3", entry.Ability3);
            AddAbility("S4", entry.Ability4);

            // Sync editable selections to the current entry.
            selectedCharacter = CharacterOptions.FirstOrDefault(o => o.Value == entry.Character);
            selectedGearType = GearTypeOptions.FirstOrDefault(o => o.Value == entry.IsArmor);
            selectedFormula = FormulaOptions.FirstOrDefault(o => o.Value == entry.Formula);
            selectedAbility1 = AbilityOptions.FirstOrDefault(o => o.Value == entry.Ability1);
            selectedAbility2 = AbilityOptions.FirstOrDefault(o => o.Value == entry.Ability2);
            selectedAbility3 = AbilityOptions.FirstOrDefault(o => o.Value == entry.Ability3);
            selectedAbility4 = AbilityOptions.FirstOrDefault(o => o.Value == entry.Ability4);
            OnPropertyChanged(nameof(SelectedCharacter));
            OnPropertyChanged(nameof(SelectedGearType));
            OnPropertyChanged(nameof(SelectedFormula));
            OnPropertyChanged(nameof(SelectedAbility1));
            OnPropertyChanged(nameof(SelectedAbility2));
            OnPropertyChanged(nameof(SelectedAbility3));
            OnPropertyChanged(nameof(SelectedAbility4));
            OnPropertyChanged(nameof(IsSummon));
            OnPropertyChanged(nameof(IsHidden));
            OnPropertyChanged(nameof(IsNoKaizou));
            OnPropertyChanged(nameof(IsBrotherhood));

            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(Detail));
            OnPropertyChanged(nameof(CharacterName));
            OnPropertyChanged(nameof(GearType));
            OnPropertyChanged(nameof(Abilities));
            OnPropertyChanged(nameof(RawFields));
            OnPropertyChanged(nameof(ModelIdHex));
            OnPropertyChanged(nameof(SaveFields));
            OnPropertyChanged(nameof(Flages));
            OnPropertyChanged(nameof(Power));
            OnPropertyChanged(nameof(Crit));
            OnPropertyChanged(nameof(Slots));
            OnPropertyChanged(nameof(Formula));
        }

        void AddAbility(string slot, ushort raw)
        {
            ushort masked = (ushort)(raw & 0x7FFF);
            string name = raw switch
            {
                0x00FF => "Empty",
                _ when AutoAbility_Dictionary.Instance.TryGetValue(masked, out string? n) => n,
                _ => $"0x{raw:X4}"
            };
            AbilityNames.Add(new AbilitySlotDisplay { Slot = slot, Name = name, Hex = $"({raw:X4})" });
        }

        void OnChanged()
        {
            owner.MarkDirty();
            RefreshLabels();
        }

        string BuildDisplayLabel()
        {
            string charName = CharacterName;
            string type = GearType;
            var hints = new System.Collections.Generic.List<string>();
            if ((entry.Flags & 0x01) != 0) hints.Add("flag1");
            if ((entry.Flags & 0x02) != 0) hints.Add("hidden");
            if ((entry.Flags & 0x04) != 0) hints.Add("no_kaizou");
            if ((entry.Flags & 0x08) != 0) hints.Add("brotherhood");
            string prefix = hints.Count == 0 ? "" : $"{string.Join("/", hints)} ";
            return $"#{entry.Index:D3} · {prefix}{charName} {type}";
        }

        string BuildSummary()
        {
            var hints = new System.Collections.Generic.List<string>();
            if ((entry.Flags & 0x01) != 0) hints.Add("flag1");
            if ((entry.Flags & 0x02) != 0) hints.Add("hidden");
            if ((entry.Flags & 0x04) != 0) hints.Add("no_kaizou");
            if ((entry.Flags & 0x08) != 0) hints.Add("brotherhood");
            string prefix = hints.Count == 0 ? "" : $"{string.Join("/", hints)} · ";
            return $"{prefix}{CharacterName} {GearType} · f{entry.Formula} pow{entry.Power} crit{entry.Crit} slots{entry.SlotCount}";
        }

        string BuildDetail()
        {
            string flagDesc = FormatFlags(entry.Flags);
            return $"#{entry.Index:D3} · {CharacterName} {GearType} · {flagDesc} · f{entry.Formula} pow{entry.Power} crit{entry.Crit} slots{entry.SlotCount} · "
                 + $"nameId={entry.AlwaysZero1:X4} exists={entry.ZeroOrOne:X2} equippable={entry.AlwaysZero2:X4} modelId=0x{entry.ModelId:X4} · abilities [{Abilities}]";
        }

        static string FormatCharacter(byte character)
        {
            if (character <= 6)
                return ((Character_Enum)(sbyte)character).ToString();
            return character switch
            {
                7 => "Seymour", 8 => "Valefor", 9 => "Ifrit", 10 => "Ixion",
                11 => "Shiva", 12 => "Bahamut", 13 => "Anima", 14 => "Yojimbo",
                15 => "Cindy", 16 => "Sandy", 17 => "Mindy",
                _ => $"char_{character:X2}h"
            };
        }

        static string FormatFlags(byte flags)
        {
            if (flags == 0) return "none";
            var f = new System.Collections.Generic.List<string>();
            if ((flags & 0x01) != 0) f.Add("Flag1");
            if ((flags & 0x02) != 0) f.Add("Hidden");
            if ((flags & 0x04) != 0) f.Add("NoKaizou");
            if ((flags & 0x08) != 0) f.Add("Brotherhood");
            if ((flags & 0xF0) != 0) f.Add($"extra:{flags >> 4:X2}h");
            return string.Join("|", f);
        }

        static string FormatAbilityList(ushort a1, ushort a2, ushort a3, ushort a4)
        {
            return string.Join(" · ", new[] { a1, a2, a3, a4 }.Select(FormatAbility));
        }

        static string FormatAbility(ushort raw)
        {
            if (raw == 0x00FF) return "Empty";
            ushort masked = (ushort)(raw & 0x7FFF);
            if (AutoAbility_Dictionary.Instance.TryGetValue(masked, out string? name))
                return name;
            return $"{raw:X4}h";
        }
    }

    internal class AbilitySlotDisplay
    {
        public required string Slot { get; init; }
        public required string Name { get; init; }
        public required string Hex { get; init; }
    }

    /// <summary>A label/value pair for ComboBox selection (value equality, safe for TwoWay bind).</summary>
    internal sealed record OptionItem<T>(string Label, T Value);
}

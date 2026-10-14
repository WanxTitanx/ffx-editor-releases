using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Customization;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.FfxLib.SpiraDataAtlas;
using FFXProjectEditor.Modules;
using FFXProjectEditor.Modules.Common;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils.Editing;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.CustomizationEditor
{
    internal partial class CustomizationEditor_DataModel : ObservableObject
    {
        readonly List<TargetOption> gearTargetOptions =
        [
            new TargetOption(0x0001, "Weapon"),
            new TargetOption(0x0002, "Armor"),
            new TargetOption(0x007F, "Aeon")
        ];

        IndexedFixedTableHeader? gearHeader;
        IndexedFixedTableHeader? aeonHeader;

        public ObservableCollection<GearCustomizationRow> LoadedGearRecipes { get; } = new();
        public ObservableCollection<GearCustomizationRow> DisplayedGearRecipes { get; } = new();
        public ObservableCollection<AeonCustomizationRow> LoadedAeonRecipes { get; } = new();
        public ObservableCollection<AeonCustomizationRow> DisplayedAeonRecipes { get; } = new();

        [ObservableProperty] private string gearFilterText = string.Empty;
        [ObservableProperty] private string aeonFilterText = string.Empty;
        [ObservableProperty] private string gearLoadSummary = "Loading kaizou.bin...";
        [ObservableProperty] private string aeonLoadSummary = "Loading sum_grow.bin...";
        [ObservableProperty] private string gearScopeSummary = "Safe scope: edit existing gear customization recipes only. Jarvis preserves the fixed table size and header shape.";
        [ObservableProperty] private string aeonScopeSummary = "Safe scope: edit existing aeon teach/stat recipes only. Jarvis preserves the fixed table size and header shape.";
        [ObservableProperty] private GearCustomizationRow? selectedGearRecipe;
        [ObservableProperty] private AeonCustomizationRow? selectedAeonRecipe;
        [ObservableProperty] private string selectedGearSummary = Strings.F2_select_a_gear_customization_recipe_to_in_9d82abba;
        [ObservableProperty] private string selectedAeonSummary = "Select an aeon customization recipe to inspect or edit it.";
        [ObservableProperty] private AtlasEvidenceInfo? selectedAeonEvidence;
        [ObservableProperty] private ByteSnapshotEditorSession? gearEditSession;
        [ObservableProperty] private ByteSnapshotEditorSession? aeonEditSession;

        public IReadOnlyList<StatOption> StatOptions => CustomizationNaming_Util.StatOptions;
        public IReadOnlyList<TargetOption> GearTargetOptions => gearTargetOptions;

        public CustomizationEditor_DataModel()
        {
            LoadFromDisk();
        }

        partial void OnGearFilterTextChanged(string value) => ApplyGearFilter();
        partial void OnAeonFilterTextChanged(string value) => ApplyAeonFilter();

        partial void OnSelectedGearRecipeChanged(GearCustomizationRow? value)
        {
            SelectedGearSummary = value == null
                ? Strings.F2_select_a_gear_customization_recipe_to_in_9d82abba
                : $"Recipe #{value.Index:D3} · {value.Summary}";
        }

        partial void OnSelectedAeonRecipeChanged(AeonCustomizationRow? value)
        {
            SelectedAeonSummary = value == null
                ? "Select an aeon customization recipe to inspect or edit it."
                : $"Recipe #{value.Index:D3} · {value.Summary}";

            RefreshSelectedAeonEvidence();
        }

        // Read-only Spira Data Atlas evidence for the selected Aeon recipe (sum_grow.bin), shown only when the
        // value-guard matches. sum_grow.bin is Aeon-only and its corpus is a compiled, byte-grounded table;
        // TryGetAeonGrowthRecipe looks the recipe up by entry index and value-guards on the CURRENT raw Result +
        // Item. Editing the taught ability/stat or the cost item (or an out-of-range index) breaks the match, the
        // accessor returns null, and the strip hides itself — no stale badge. Read-only: never a writer.
        void RefreshSelectedAeonEvidence()
        {
            AeonCustomizationRow? row = SelectedAeonRecipe;
            SelectedAeonEvidence = row != null
                && SpiraDataAtlasCatalog.TryGetAeonGrowthRecipe(row.Index, row.RawResult, row.CostItem.Unwrap(), out SpiraDataAtlasDetailEntry? detail)
                && detail != null
                    ? AtlasEvidenceInfo.ForDetail(detail)
                    : null;
        }

        public void RefreshFromDisk() => LoadFromDisk();
        public void SaveGear() => GearEditSession?.Save();
        public void UndoGear() => GearEditSession?.Undo();
        public void DiscardGear() => GearEditSession?.Discard();
        public void SaveAeon() => AeonEditSession?.Save();
        public void UndoAeon() => AeonEditSession?.Undo();
        public void DiscardAeon() => AeonEditSession?.Discard();

        void LoadFromDisk()
        {
            GearEditSession?.Dispose();
            AeonEditSession?.Dispose();
            GearEditSession = null;
            AeonEditSession = null;

            if (!Project_Service.Instance.IsProjectLoaded)
            {
                ClearAll("Project root not loaded.");
                return;
            }

            if (!File.Exists(Project_Service.Instance.Path_KernelKaizou))
            {
                ClearGear("kaizou.bin not found in the loaded workspace.");
            }
            else
            {
                byte[] gearBytes = File.ReadAllBytes(Project_Service.Instance.Path_KernelKaizou);
                CustomizationTable<GearCustomizationEntry> gearTable = Customization_File.ReadGear(gearBytes);
                gearHeader = gearTable.Header;
                LoadGearRows(gearTable.Entries, SelectedGearRecipe?.Index);
                GearEditSession = new ByteSnapshotEditorSession(BuildGearFile, RestoreGearFromBytes, PersistGearBytes, "gear customization recipes", BuildGearFile());
            }

            if (!File.Exists(Project_Service.Instance.Path_KernelSumGrow))
            {
                ClearAeon("sum_grow.bin not found in the loaded workspace.");
            }
            else
            {
                byte[] aeonBytes = File.ReadAllBytes(Project_Service.Instance.Path_KernelSumGrow);
                CustomizationTable<AeonCustomizationEntry> aeonTable = Customization_File.ReadAeon(aeonBytes);
                aeonHeader = aeonTable.Header;
                LoadAeonRows(aeonTable.Entries, SelectedAeonRecipe?.Index);
                AeonEditSession = new ByteSnapshotEditorSession(BuildAeonFile, RestoreAeonFromBytes, PersistAeonBytes, "aeon customization recipes", BuildAeonFile());
            }
        }

        void ClearAll(string message)
        {
            ClearGear(message);
            ClearAeon(message);
        }

        void ClearGear(string message)
        {
            foreach (GearCustomizationRow row in LoadedGearRecipes)
                row.PropertyChanged -= GearRecipeChanged;

            gearHeader = null;
            LoadedGearRecipes.Clear();
            DisplayedGearRecipes.Clear();
            SelectedGearRecipe = null;
            GearLoadSummary = message;
        }

        void ClearAeon(string message)
        {
            foreach (AeonCustomizationRow row in LoadedAeonRecipes)
                row.PropertyChanged -= AeonRecipeChanged;

            aeonHeader = null;
            LoadedAeonRecipes.Clear();
            DisplayedAeonRecipes.Clear();
            SelectedAeonRecipe = null;
            AeonLoadSummary = message;
        }

        void LoadGearRows(IReadOnlyList<GearCustomizationEntry> entries, int? preserveSelectionIndex)
        {
            foreach (GearCustomizationRow row in LoadedGearRecipes)
                row.PropertyChanged -= GearRecipeChanged;

            LoadedGearRecipes.Clear();
            DisplayedGearRecipes.Clear();

            foreach (GearCustomizationEntry entry in entries)
            {
                GearCustomizationRow row = GearCustomizationRow.Wrap(entry, gearTargetOptions, CustomizationNaming_Util.StatOptions);
                row.PropertyChanged += GearRecipeChanged;
                LoadedGearRecipes.Add(row);
            }

            ApplyGearFilter();
            SelectedGearRecipe = preserveSelectionIndex.HasValue
                ? LoadedGearRecipes.FirstOrDefault(row => row.Index == preserveSelectionIndex.Value)
                : LoadedGearRecipes.FirstOrDefault();

            GearLoadSummary = $"Loaded {LoadedGearRecipes.Count} gear customization recipes from kaizou.bin.";
        }

        void LoadAeonRows(IReadOnlyList<AeonCustomizationEntry> entries, int? preserveSelectionIndex)
        {
            foreach (AeonCustomizationRow row in LoadedAeonRecipes)
                row.PropertyChanged -= AeonRecipeChanged;

            LoadedAeonRecipes.Clear();
            DisplayedAeonRecipes.Clear();

            foreach (AeonCustomizationEntry entry in entries)
            {
                AeonCustomizationRow row = AeonCustomizationRow.Wrap(entry, CustomizationNaming_Util.StatOptions);
                row.PropertyChanged += AeonRecipeChanged;
                LoadedAeonRecipes.Add(row);
            }

            ApplyAeonFilter();
            SelectedAeonRecipe = preserveSelectionIndex.HasValue
                ? LoadedAeonRecipes.FirstOrDefault(row => row.Index == preserveSelectionIndex.Value)
                : LoadedAeonRecipes.FirstOrDefault();

            AeonLoadSummary = $"Loaded {LoadedAeonRecipes.Count} aeon recipes from sum_grow.bin.";
        }

        void ApplyGearFilter()
        {
            DisplayedGearRecipes.Clear();
            string normalized = GearFilterText.Trim();
            foreach (GearCustomizationRow row in LoadedGearRecipes)
            {
                if (normalized.Length == 0 || row.SearchBlob.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                    DisplayedGearRecipes.Add(row);
            }

            if (SelectedGearRecipe != null && !DisplayedGearRecipes.Contains(SelectedGearRecipe))
                SelectedGearRecipe = DisplayedGearRecipes.FirstOrDefault();
        }

        void ApplyAeonFilter()
        {
            DisplayedAeonRecipes.Clear();
            string normalized = AeonFilterText.Trim();
            foreach (AeonCustomizationRow row in LoadedAeonRecipes)
            {
                if (normalized.Length == 0 || row.SearchBlob.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                    DisplayedAeonRecipes.Add(row);
            }

            if (SelectedAeonRecipe != null && !DisplayedAeonRecipes.Contains(SelectedAeonRecipe))
                SelectedAeonRecipe = DisplayedAeonRecipes.FirstOrDefault();
        }

        byte[] BuildGearFile()
        {
            if (gearHeader == null)
                return Array.Empty<byte>();

            CustomizationTable<GearCustomizationEntry> table = new(gearHeader, LoadedGearRecipes.Select(row => row.Unwrap()).ToList());
            return Customization_File.WriteGear(table);
        }

        byte[] BuildAeonFile()
        {
            if (aeonHeader == null)
                return Array.Empty<byte>();

            CustomizationTable<AeonCustomizationEntry> table = new(aeonHeader, LoadedAeonRecipes.Select(row => row.Unwrap()).ToList());
            return Customization_File.WriteAeon(table);
        }

        void RestoreGearFromBytes(byte[] bytes)
        {
            CustomizationTable<GearCustomizationEntry> table = Customization_File.ReadGear(bytes);
            gearHeader = table.Header;
            LoadGearRows(table.Entries, SelectedGearRecipe?.Index);
        }

        void RestoreAeonFromBytes(byte[] bytes)
        {
            CustomizationTable<AeonCustomizationEntry> table = Customization_File.ReadAeon(bytes);
            aeonHeader = table.Header;
            LoadAeonRows(table.Entries, SelectedAeonRecipe?.Index);
        }

        void PersistGearBytes(byte[] bytes)
        {
            File.WriteAllBytes(Project_Service.Instance.Path_KernelKaizou, bytes);
        }

        void PersistAeonBytes(byte[] bytes)
        {
            File.WriteAllBytes(Project_Service.Instance.Path_KernelSumGrow, bytes);
        }

        void GearRecipeChanged(object? sender, PropertyChangedEventArgs e)
        {
            GearEditSession?.NotifyPotentialMutation();
        }

        void AeonRecipeChanged(object? sender, PropertyChangedEventArgs e)
        {
            AeonEditSession?.NotifyPotentialMutation();

            // Re-evaluate the read-only Atlas badge when the SELECTED recipe's raw Result word changes, so editing
            // the taught ability/stat or the cost item (both raise RawResult via NotifyComputedChanged) hides or
            // updates the evidence instead of leaving a stale reward badge. Hide-on-edit, mirror of the Mix module.
            if (ReferenceEquals(sender, SelectedAeonRecipe) && e.PropertyName == nameof(AeonCustomizationRow.RawResult))
                RefreshSelectedAeonEvidence();
        }

        internal partial class GearCustomizationRow : ObservableObject
        {
            readonly IReadOnlyList<TargetOption> targetOptions;
            readonly IReadOnlyList<StatOption> statOptions;

            [ObservableProperty] private ushort target;
            [ObservableProperty] private byte primaryValue;
            [ObservableProperty] private byte secondaryValue;
            [ObservableProperty] private ushort statId;

            public int Index { get; init; }
            public bool IsStatRecipe { get; init; }
            public bool IsAbilityRecipe => !IsStatRecipe;
            public GameIndex_Wrapper ResultAbility { get; init; } = new();
            public GameIndex_Wrapper CostItem { get; init; } = new();
            public string TargetLabel => SelectedTargetOption?.Name ?? $"Unknown ({Target:X4}h)";
            public string ResultLabel => IsStatRecipe
                ? $"{SelectedStatOption?.Name ?? CustomizationNaming_Util.ResolveStatLabel(StatId)} +{StatAmount}"
                : ResultAbility.SelectedOption?.Name ?? CustomizationNaming_Util.ResolveGameLabel(ResultAbility.Unwrap());
            public string CostLabel => $"{CostAmount}x {CostItem.SelectedOption?.Name ?? CustomizationNaming_Util.ResolveGameLabel(CostItem.Unwrap())}";
            public string Summary => $"{TargetLabel} · {ResultLabel} · {CostLabel}";
            public string SearchBlob => $"{Index:D3} {TargetLabel} {ResultLabel} {CostLabel}";
            public string ModeLabel => IsStatRecipe ? "Stat Recipe" : "Auto-Ability Recipe";
            public string RawResultHex => $"{RawResult:X4}h";
            public string RawItemHex => $"{CostItem.Unwrap():X4}h";
            public string RawTargetHex => $"{Target:X4}h";
            public string SecondaryValueLabel => $"{SecondaryValue}";

            GearCustomizationRow(IReadOnlyList<TargetOption> targetOptions, IReadOnlyList<StatOption> statOptions)
            {
                this.targetOptions = targetOptions;
                this.statOptions = statOptions;
            }

            public TargetOption? SelectedTargetOption
            {
                get
                {
                    TargetOption option = targetOptions.FirstOrDefault(candidate => candidate.Value == Target);
                    return string.IsNullOrWhiteSpace(option.Name) ? null : option;
                }
                set
                {
                    if (!value.HasValue)
                        return;

                    Target = value.Value.Value;
                    NotifyComputedChanged();
                }
            }

            public StatOption? SelectedStatOption
            {
                get
                {
                    StatOption option = statOptions.FirstOrDefault(candidate => candidate.Id == StatId);
                    return string.IsNullOrWhiteSpace(option.Name) ? null : option;
                }
                set
                {
                    if (!value.HasValue)
                        return;

                    StatId = value.Value.Id;
                    NotifyComputedChanged();
                }
            }

            public int StatAmount
            {
                get => PrimaryValue;
                set
                {
                    PrimaryValue = ClampByte(value);
                    NotifyComputedChanged();
                }
            }

            public int CostAmount
            {
                get => IsStatRecipe ? SecondaryValue : PrimaryValue;
                set
                {
                    if (IsStatRecipe)
                        SecondaryValue = ClampByte(value);
                    else
                        PrimaryValue = ClampByte(value);

                    NotifyComputedChanged();
                }
            }

            public ushort RawResult => IsStatRecipe ? StatId : ResultAbility.Unwrap();

            public static GearCustomizationRow Wrap(GearCustomizationEntry entry, IReadOnlyList<TargetOption> targetOptions, IReadOnlyList<StatOption> statOptions)
            {
                GearCustomizationRow row = new(targetOptions, statOptions)
                {
                    Index = entry.Index,
                    Target = entry.Target,
                    PrimaryValue = entry.PrimaryValue,
                    SecondaryValue = entry.SecondaryValue,
                    StatId = entry.IsStatRecipe ? entry.Result : (ushort)0,
                    IsStatRecipe = entry.IsStatRecipe,
                    ResultAbility = GameIndex_Wrapper.Wrap(entry.Result),
                    CostItem = GameIndex_Wrapper.Wrap(entry.Item)
                };

                row.ResultAbility.PropertyChanged += row.NestedObjectChanged;
                row.CostItem.PropertyChanged += row.NestedObjectChanged;
                return row;
            }

            public GearCustomizationEntry Unwrap()
            {
                return new GearCustomizationEntry
                {
                    Index = Index,
                    Target = Target,
                    Result = RawResult,
                    Item = CostItem.Unwrap(),
                    PrimaryValue = IsStatRecipe ? (byte)StatAmount : (byte)CostAmount,
                    SecondaryValue = IsStatRecipe ? (byte)CostAmount : SecondaryValue
                };
            }

            partial void OnTargetChanged(ushort value) => NotifyComputedChanged();
            partial void OnPrimaryValueChanged(byte value) => NotifyComputedChanged();
            partial void OnSecondaryValueChanged(byte value) => NotifyComputedChanged();
            partial void OnStatIdChanged(ushort value) => NotifyComputedChanged();

            void NestedObjectChanged(object? sender, PropertyChangedEventArgs e) => NotifyComputedChanged();

            void NotifyComputedChanged()
            {
                OnPropertyChanged(nameof(SelectedTargetOption));
                OnPropertyChanged(nameof(SelectedStatOption));
                OnPropertyChanged(nameof(TargetLabel));
                OnPropertyChanged(nameof(ResultLabel));
                OnPropertyChanged(nameof(CostLabel));
                OnPropertyChanged(nameof(Summary));
                OnPropertyChanged(nameof(SearchBlob));
                OnPropertyChanged(nameof(RawResult));
                OnPropertyChanged(nameof(RawResultHex));
                OnPropertyChanged(nameof(RawItemHex));
                OnPropertyChanged(nameof(RawTargetHex));
                OnPropertyChanged(nameof(StatAmount));
                OnPropertyChanged(nameof(CostAmount));
                OnPropertyChanged(nameof(SecondaryValueLabel));
            }
        }

        internal partial class AeonCustomizationRow : ObservableObject
        {
            readonly IReadOnlyList<StatOption> statOptions;

            [ObservableProperty] private ushort target;
            [ObservableProperty] private byte primaryValue;
            [ObservableProperty] private byte secondaryValue;
            [ObservableProperty] private ushort statId;

            public int Index { get; init; }
            public bool IsStatRecipe { get; init; }
            public bool IsAbilityRecipe => !IsStatRecipe;
            public GameIndex_Wrapper CommandRef { get; init; } = new();
            public GameIndex_Wrapper CostItem { get; init; } = new();
            public string TargetLabel => Target == 0x007F ? "Aeon" : $"Unknown ({Target:X4}h)";
            public string ResultLabel => IsStatRecipe
                ? $"{SelectedStatOption?.Name ?? CustomizationNaming_Util.ResolveStatLabel(StatId)} +{PrimaryValue}"
                : CommandRef.SelectedOption?.Name ?? CustomizationNaming_Util.ResolveGameLabel(CommandRef.Unwrap());
            public string CostLabel => $"{PrimaryValue}x {CostItem.SelectedOption?.Name ?? CustomizationNaming_Util.ResolveGameLabel(CostItem.Unwrap())}";
            public string Summary => $"{ResultLabel} · {CostLabel}";
            public string SearchBlob => $"{Index:D3} {TargetLabel} {ResultLabel} {CostLabel}";
            public string ModeLabel => IsStatRecipe ? "Stat Recipe" : "Ability Recipe";
            public string RawResultHex => $"{RawResult:X4}h";
            public string RawItemHex => $"{CostItem.Unwrap():X4}h";
            public string RawTargetHex => $"{Target:X4}h";
            public string ModeRawFlag => SecondaryValue == 0 ? "0" : "1";

            AeonCustomizationRow(IReadOnlyList<StatOption> statOptions)
            {
                this.statOptions = statOptions;
            }

            public StatOption? SelectedStatOption
            {
                get
                {
                    StatOption option = statOptions.FirstOrDefault(candidate => candidate.Id == StatId);
                    return string.IsNullOrWhiteSpace(option.Name) ? null : option;
                }
                set
                {
                    if (!value.HasValue)
                        return;

                    StatId = value.Value.Id;
                    NotifyComputedChanged();
                }
            }

            public int Amount
            {
                get => PrimaryValue;
                set
                {
                    PrimaryValue = ClampByte(value);
                    NotifyComputedChanged();
                }
            }

            public ushort RawResult => IsStatRecipe ? StatId : CommandRef.Unwrap();

            public static AeonCustomizationRow Wrap(AeonCustomizationEntry entry, IReadOnlyList<StatOption> statOptions)
            {
                AeonCustomizationRow row = new(statOptions)
                {
                    Index = entry.Index,
                    Target = entry.Target,
                    PrimaryValue = entry.PrimaryValue,
                    SecondaryValue = entry.SecondaryValue,
                    StatId = entry.IsStatRecipe ? entry.Result : (ushort)0,
                    IsStatRecipe = entry.IsStatRecipe,
                    CommandRef = GameIndex_Wrapper.Wrap(entry.Result),
                    CostItem = GameIndex_Wrapper.Wrap(entry.Item)
                };

                row.CommandRef.PropertyChanged += row.NestedObjectChanged;
                row.CostItem.PropertyChanged += row.NestedObjectChanged;
                return row;
            }

            public AeonCustomizationEntry Unwrap()
            {
                return new AeonCustomizationEntry
                {
                    Index = Index,
                    Target = Target,
                    Result = RawResult,
                    Item = CostItem.Unwrap(),
                    PrimaryValue = PrimaryValue,
                    SecondaryValue = IsStatRecipe ? (byte)1 : (byte)0
                };
            }

            partial void OnTargetChanged(ushort value) => NotifyComputedChanged();
            partial void OnPrimaryValueChanged(byte value) => NotifyComputedChanged();
            partial void OnSecondaryValueChanged(byte value) => NotifyComputedChanged();
            partial void OnStatIdChanged(ushort value) => NotifyComputedChanged();

            void NestedObjectChanged(object? sender, PropertyChangedEventArgs e) => NotifyComputedChanged();

            void NotifyComputedChanged()
            {
                OnPropertyChanged(nameof(SelectedStatOption));
                OnPropertyChanged(nameof(TargetLabel));
                OnPropertyChanged(nameof(ResultLabel));
                OnPropertyChanged(nameof(CostLabel));
                OnPropertyChanged(nameof(Summary));
                OnPropertyChanged(nameof(SearchBlob));
                OnPropertyChanged(nameof(RawResult));
                OnPropertyChanged(nameof(RawResultHex));
                OnPropertyChanged(nameof(RawItemHex));
                OnPropertyChanged(nameof(RawTargetHex));
                OnPropertyChanged(nameof(Amount));
                OnPropertyChanged(nameof(ModeRawFlag));
            }
        }

        static byte ClampByte(int value)
        {
            if (value < byte.MinValue)
                return byte.MinValue;
            if (value > byte.MaxValue)
                return byte.MaxValue;
            return (byte)value;
        }
    }

    internal readonly record struct TargetOption(ushort Value, string Name)
    {
        public string Display => $"{Name} ({Value:X4}h)";
    }
}

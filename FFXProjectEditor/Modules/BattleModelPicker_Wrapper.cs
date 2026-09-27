using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Dictionaries;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.Modules
{
    public partial class BattleModelPicker_Wrapper : ObservableObject
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Name))]
        [NotifyPropertyChangedFor(nameof(HexLabel))]
        [NotifyPropertyChangedFor(nameof(SelectionLabel))]
        [NotifyPropertyChangedFor(nameof(SelectedOption))]
        public short value;

        [ObservableProperty] public string optionFilterText = string.Empty;
        [ObservableProperty] public IReadOnlyList<BattleModelOption> filteredOptions = [];

        BattleModelOption? pinnedSelection;

        public string Name => BattleModel_Dictionary.ResolveName(Value);
        public string HexLabel => $"0x{Value:X4}";
        public string SelectionLabel => $"{HexLabel} — {Name}";

        public BattleModelOption? SelectedOption
        {
            get => pinnedSelection;
            set
            {
                if (value == null)
                    return;

                Value = (short)value.Id;
            }
        }

        partial void OnValueChanged(short value)
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(HexLabel));
            OnPropertyChanged(nameof(SelectionLabel));
            ApplyFilter();
        }

        partial void OnOptionFilterTextChanged(string value) => ApplyFilter();

        public void RefreshOptions() => ApplyFilter();

        void ApplyFilter()
        {
            List<BattleModelOption> list = BattleModel_Dictionary.Search(OptionFilterText)
                .Select(entry => new BattleModelOption(entry))
                .ToList();

            BattleModelOption? current = list.FirstOrDefault(option => option.Id == Value);
            if (current == null && BattleModel_Dictionary.TryGet(Value, out BattleModel_Dictionary.Entry? entry))
            {
                current = new BattleModelOption(entry);
            }

            if (current != null)
            {
                list.RemoveAll(option => option.Id == current.Id);
                list.Insert(0, current);
            }

            pinnedSelection = current;
            FilteredOptions = list;
            OnPropertyChanged(nameof(SelectedOption));
        }

        public sealed class BattleModelOption
        {
            public BattleModelOption(BattleModel_Dictionary.Entry entry)
            {
                Id = entry.Id;
                Name = entry.Name;
                Display = entry.Display;
                IsVariant = entry.IsVariant;
                Warning = entry.CrashesGame
                    ? "crash risk"
                    : entry.IsInvisible
                        ? "invisible"
                        : entry.IsUnderwater
                            ? "underwater"
                            : entry.IsVariant
                                ? $"variant of 0x{entry.MainModelId:X4}"
                                : string.Empty;
            }

            public int Id { get; }
            public string Name { get; }
            public string Display { get; }
            public string Warning { get; }
            public bool IsVariant { get; }
        }
    }
}

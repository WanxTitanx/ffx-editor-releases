using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.Services;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.Modules
{
    public partial class GameIndex_Wrapper : ObservableObject
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Name))]
        [NotifyPropertyChangedFor(nameof(ItemIconImage))]
        [NotifyPropertyChangedFor(nameof(HasItemIcon))]
        [NotifyPropertyChangedFor(nameof(ItemIconTooltip))]
        public byte category;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Name))]
        [NotifyPropertyChangedFor(nameof(ItemIconImage))]
        [NotifyPropertyChangedFor(nameof(HasItemIcon))]
        [NotifyPropertyChangedFor(nameof(ItemIconTooltip))]
        public ushort index;

        [ObservableProperty] public IReadOnlyList<GameIndexOption_Wrapper> availableOptions = [];
        [ObservableProperty] public IReadOnlyList<GameIndexOption_Wrapper> filteredOptions = [];
        [ObservableProperty] public string optionFilterText = string.Empty;

        string Name => FfxCommon_Util.GetGameIndexName(category, index);
        public Bitmap? ItemIconImage => ItemIcon_Service.TryResolveBitmap(Category, Index, out Bitmap? bitmap) ? bitmap : null;
        public bool HasItemIcon => ItemIconImage != null;
        public string ItemIconTooltip => Category == (byte)GameCategory_Enum.Items ? ItemIcon_Service.BuildTooltip(Index) : string.Empty;

        public GameIndexOption_Wrapper? SelectedOption
        {
            get => AvailableOptions.FirstOrDefault(option => option.Index == Index);
            set
            {
                if (value == null)
                    return;

                Index = value.Index;
            }
        }

        partial void OnCategoryChanged(byte value)
        {
            AvailableOptions = BuildOptions(value);
            ApplyFilter();
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(SelectedOption));
            OnPropertyChanged(nameof(ItemIconImage));
            OnPropertyChanged(nameof(HasItemIcon));
            OnPropertyChanged(nameof(ItemIconTooltip));
        }

        partial void OnIndexChanged(ushort value)
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(SelectedOption));
            OnPropertyChanged(nameof(ItemIconImage));
            OnPropertyChanged(nameof(HasItemIcon));
            OnPropertyChanged(nameof(ItemIconTooltip));
        }

        public static GameIndex_Wrapper Wrap(ushort gameIndex)
        {
            GameIndex_Wrapper wrapper = new();
            wrapper.Category = FfxCommon_Util.GetGameCategory(gameIndex);
            wrapper.Index = FfxCommon_Util.GetGameIndex(gameIndex);
            return wrapper;
        }

        public ushort Unwrap()
        {
            ushort gameIndex = new();
            gameIndex = FfxCommon_Util.SetGameCategory(gameIndex, Category);
            gameIndex = FfxCommon_Util.SetGameIndex(gameIndex, Index);
            return gameIndex;
        }

        partial void OnOptionFilterTextChanged(string value)
        {
            ApplyFilter();
        }

        void ApplyFilter()
        {
            IEnumerable<GameIndexOption_Wrapper> options = AvailableOptions;
            string filter = OptionFilterText.Trim();
            if (filter.Length > 0)
            {
                options = options.Where(option =>
                    option.Name.Contains(filter, System.StringComparison.OrdinalIgnoreCase) ||
                    option.Display.Contains(filter, System.StringComparison.OrdinalIgnoreCase));
            }

            FilteredOptions = options.ToList();
            OnPropertyChanged(nameof(SelectedOption));
        }

        static IReadOnlyList<GameIndexOption_Wrapper> BuildOptions(byte category)
        {
            GameCategory_Enum categoryEnum = (GameCategory_Enum)category;
            IEnumerable<KeyValuePair<ushort, string>> pairs = categoryEnum switch
            {
                GameCategory_Enum.Items => Item_Dictionary.Instance.OrderBy(pair => pair.Key),
                GameCategory_Enum.Commands => CommandCharacter_Dictionary.Instance.OrderBy(pair => pair.Key),
                GameCategory_Enum.MonMagic1 => CommandMonster1_Dictionary.Instance.OrderBy(pair => pair.Key),
                GameCategory_Enum.MonMagic2 => CommandMonster2_Dictionary.Instance.OrderBy(pair => pair.Key),
                GameCategory_Enum.AutoAbilities => AutoAbility_Dictionary.Instance.OrderBy(pair => pair.Key),
                _ => Enumerable.Empty<KeyValuePair<ushort, string>>()
            };

            if (categoryEnum == GameCategory_Enum.Items)
            {
                return pairs
                    .Select(pair =>
                    {
                        ItemIcon_Service.TryResolveBitmap(pair.Key, out Bitmap? bitmap);
                        return new GameIndexOption_Wrapper(pair.Key, pair.Value, bitmap, ItemIcon_Service.BuildTooltip(pair.Key));
                    })
                    .ToList();
            }

            return pairs.Select(pair => new GameIndexOption_Wrapper(pair.Key, pair.Value)).ToList();
        }
    }

    public sealed class GameIndexOption_Wrapper
    {
        public GameIndexOption_Wrapper(ushort index, string name, Bitmap? itemIconImage = null, string? itemIconTooltip = null)
        {
            Index = index;
            Name = string.IsNullOrWhiteSpace(name) ? "<EMPTY>" : name;
            ItemIconImage = itemIconImage;
            ItemIconTooltip = itemIconTooltip ?? string.Empty;
        }

        public ushort Index { get; }
        public string Name { get; }
        public Bitmap? ItemIconImage { get; }
        public bool HasItemIcon => ItemIconImage != null;
        public string ItemIconTooltip { get; }
        public string Display => $"[{Index}] {Name}";
    }
}

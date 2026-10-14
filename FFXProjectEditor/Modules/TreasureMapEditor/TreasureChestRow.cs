using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.TreasureMap;
using FFXProjectEditor.Resources;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.Modules.TreasureMapEditor;

public sealed class TreasureChestRow : ObservableObject
{
    private readonly string _masterPath;
    private bool _loading;
    private TreasureKind _selectedKind;
    private TreasureRewardOption? _selectedReward;
    private byte _quantity;
    private IReadOnlyList<TreasureRewardOption> _rewardOptions = [];

    public ProjectedChestLocation Location { get; }
    public TreasureRecord? ActiveReward { get; private set; }
    public event EventHandler? Edited;
    public IReadOnlyList<TreasureKind> KindOptions { get; } = Enum.GetValues<TreasureKind>();
    public IReadOnlyList<TreasureRewardOption> RewardOptions { get => _rewardOptions; private set => SetProperty(ref _rewardOptions, value); }
    public bool CanEdit => ActiveReward?.Kind is not null;
    public bool HasRealPosition => Location.GuideX.HasValue && Location.GuideZ.HasValue;
    public string Label => string.Format(Location.Confidence == ChestLocationConfidence.Conditional
        ? Strings.TreasureMapConditionalReward : Strings.TreasureMapChestLabel, Location.TreasureIds.Single());
    public string Confidence => Location.Confidence switch
    {
        ChestLocationConfidence.Exact => Strings.TreasureMapPositionKnown,
        ChestLocationConfidence.Conditional => Strings.TreasureMapPositionConditional,
        _ => Strings.TreasureMapPositionUnknown
    };
    public string AmountLabel => SelectedKind == TreasureKind.Gil ? Strings.TreasureMapGilUnits : Strings.TreasureMapQuantity;
    public bool SelectedRewardVisible => SelectedKind != TreasureKind.Gil;
    public bool IsValid => !CanEdit || SelectedKind == TreasureKind.Gil || SelectedReward is not null && RewardOptions.Contains(SelectedReward);
    public string RewardText => !CanEdit ? Strings.TreasureMapUnsupportedReward
        : SelectedKind == TreasureKind.Gil ? $"{Quantity * 100:N0} {Strings.CommonGil}"
        : SelectedReward is null ? Strings.TreasureMapChooseReward
        : SelectedKind == TreasureKind.Equipment ? SelectedReward.Display : $"{Quantity} × {SelectedReward.Display}";

    public TreasureKind SelectedKind
    {
        get => _selectedKind;
        set
        {
            if (_selectedKind == value) return;
            _selectedKind = value;
            _loading = true;
            RefreshOptions(value == ActiveReward?.Kind ? ActiveReward.Type : null);
            _loading = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AmountLabel));
            OnPropertyChanged(nameof(SelectedRewardVisible));
            Changed();
        }
    }
    public TreasureRewardOption? SelectedReward
    {
        get => _selectedReward;
        set { if (SetProperty(ref _selectedReward, value)) Changed(); }
    }
    public byte Quantity
    {
        get => _quantity;
        set { if (SetProperty(ref _quantity, value)) Changed(); }
    }
    public bool HasChanges => CurrentRecord() is TreasureRecord current && current != ActiveReward;

    public TreasureChestRow(ProjectedChestLocation location, string masterPath = "")
    { Location = location; _masterPath = masterPath; }

    public void LoadRecord(TreasureRecord original, TreasureRecord? edited = null)
    {
        _loading = true;
        ActiveReward = original;
        var current = edited ?? original;
        _selectedKind = current.Kind ?? TreasureKind.Gil;
        _quantity = current.Quantity;
        RefreshOptions(current.Type);
        OnPropertyChanged(nameof(ActiveReward));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(SelectedKind));
        OnPropertyChanged(nameof(Quantity));
        OnPropertyChanged(nameof(AmountLabel));
        OnPropertyChanged(nameof(SelectedRewardVisible));
        _loading = false;
        NotifyState();
    }

    public TreasureRecord? ToEditedRecord() => HasChanges ? CurrentRecord() : null;

    private TreasureRecord? CurrentRecord()
    {
        if (!CanEdit || !IsValid) return null;
        // Gil's payload is unused. Preserve existing raw values; a conversion to Gil uses 0.
        ushort type = SelectedKind == TreasureKind.Gil
            ? ActiveReward!.Kind == TreasureKind.Gil ? ActiveReward.Type : (ushort)0
            : SelectedReward!.EncodedId;
        return ActiveReward! with { RawKind = (byte)SelectedKind, Quantity = Quantity, Type = type };
    }

    private void RefreshOptions(ushort? currentId)
    {
        var options = TreasureRewardLookup.Build(SelectedKind, _masterPath).ToList();
        // A catalog name may be unavailable without invalidating the original encoded ID.
        if (currentId.HasValue && SelectedKind != TreasureKind.Gil && options.All(o => o.EncodedId != currentId))
            options.Add(new TreasureRewardOption(currentId.Value, string.Format(Strings.TreasureMapRawReward, currentId.Value)));
        RewardOptions = options;
        SelectedReward = currentId.HasValue ? options.FirstOrDefault(o => o.EncodedId == currentId) : null;
    }

    private void Changed()
    {
        if (_loading) return;
        NotifyState();
        Edited?.Invoke(this, EventArgs.Empty);
    }
    private void NotifyState()
    {
        OnPropertyChanged(nameof(RewardText));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(HasChanges));
    }
}

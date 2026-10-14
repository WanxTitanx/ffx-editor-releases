// ============================================================================
// GameMusicImportSelectionModel — explicit opt-in state for the audio flyout
// PURPOSE : expose the fixed ten-track allowlist, consent, progress, and the
//           exact selection without retaining the user's source FSB path.
// WHY     : local game-audio derivation must never start from an implicit or
//           remembered selection. Every import is a fresh, visible opt-in.
// MAINT   : this model intentionally contains no source-path property.
// ============================================================================

using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace FFXProjectEditor.Services.ReleaseRuntime;

public sealed partial class GameMusicImportTrackSelection : ObservableObject
{
    [ObservableProperty]
    private bool isSelected;

    public GameMusicImportTrackSelection(GameMusicTrackDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    public GameMusicTrackDefinition Definition { get; }
    public string Id => Definition.Id;
    public string DisplayName => Definition.DisplayName;
    public string SelectionLabel => $"{Definition.StreamIndex:D2} · {Definition.DisplayName}";
}

public sealed partial class GameMusicImportSelectionModel : ObservableObject
{
    [ObservableProperty]
    private bool userConfirmedOwnershipAndLocalUse;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private int progressPercent;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    public GameMusicImportSelectionModel()
    {
        Tracks = GameMusicImportCatalog.Tracks
            .Select(track => new GameMusicImportTrackSelection(track))
            .ToArray();
        foreach (GameMusicImportTrackSelection track in Tracks)
        {
            track.PropertyChanged += OnTrackPropertyChanged;
        }
    }

    public IReadOnlyList<GameMusicImportTrackSelection> Tracks { get; }

    public bool CanImport => UserConfirmedOwnershipAndLocalUse
        && !IsBusy
        && Tracks.Any(track => track.IsSelected);

    public IReadOnlyList<string> GetExplicitSelection() => Tracks
        .Where(track => track.IsSelected)
        .Select(track => track.Id)
        .ToArray();

    public void ResetOptIn()
    {
        UserConfirmedOwnershipAndLocalUse = false;
        foreach (GameMusicImportTrackSelection track in Tracks)
        {
            track.IsSelected = false;
        }
    }

    partial void OnUserConfirmedOwnershipAndLocalUseChanged(bool value) =>
        OnPropertyChanged(nameof(CanImport));

    partial void OnIsBusyChanged(bool value) =>
        OnPropertyChanged(nameof(CanImport));

    private void OnTrackPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameMusicImportTrackSelection.IsSelected))
        {
            OnPropertyChanged(nameof(CanImport));
        }
    }
}

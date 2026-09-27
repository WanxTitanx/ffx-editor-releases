using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.Diagnostics;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Services.Extras;
using FFXProjectEditor.Services.ReleaseRuntime;
using FFXProjectEditor.Utils;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FFXProjectEditor.Services
{
    public sealed partial class AudioStudio_Service : SingletonBase<AudioStudio_Service>, IDisposable
    {
        public sealed class AudioTrackOption
        {
            public string Id { get; }
            public string DisplayName { get; }
            public string RelativePath { get; }
            public long? LoopStartSamples { get; }
            public long? LoopEndSamples { get; }

            public AudioTrackOption(string id, string displayName, string relativePath, long? loopStartSamples = null, long? loopEndSamples = null)
            {
                Id = id;
                DisplayName = displayName;
                RelativePath = relativePath;
                LoopStartSamples = loopStartSamples;
                LoopEndSamples = loopEndSamples;
            }

            public override string ToString()
            {
                return DisplayName;
            }
        }

        private sealed class LoopStream : WaveStream
        {
            private readonly WaveStream _sourceStream;
            private readonly long _loopStartPosition;
            private readonly long _loopEndPosition;

            public LoopStream(WaveStream sourceStream, long loopStartPosition = 0, long? loopEndPosition = null)
            {
                _sourceStream = sourceStream;
                _loopStartPosition = Math.Max(0, Math.Min(loopStartPosition, sourceStream.Length));
                _loopEndPosition = Math.Max(_loopStartPosition, Math.Min(loopEndPosition ?? sourceStream.Length, sourceStream.Length));
            }

            public override WaveFormat WaveFormat => _sourceStream.WaveFormat;

            public override long Length => _sourceStream.Length;

            public override long Position
            {
                get => _sourceStream.Position;
                set => _sourceStream.Position = value;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int totalBytesRead = 0;

                while (totalBytesRead < count)
                {
                    if (_sourceStream.Position >= _loopEndPosition)
                    {
                        _sourceStream.Position = _loopStartPosition;
                    }

                    long remainingUntilLoop = _loopEndPosition - _sourceStream.Position;
                    if (remainingUntilLoop <= 0)
                    {
                        _sourceStream.Position = _loopStartPosition;
                        remainingUntilLoop = _loopEndPosition - _sourceStream.Position;
                        if (remainingUntilLoop <= 0)
                        {
                            break;
                        }
                    }

                    int bytesToRead = (int)Math.Min(count - totalBytesRead, remainingUntilLoop);
                    int bytesRead = _sourceStream.Read(buffer, offset + totalBytesRead, bytesToRead);
                    if (bytesRead == 0)
                    {
                        _sourceStream.Position = _loopStartPosition;
                        bytesRead = _sourceStream.Read(buffer, offset + totalBytesRead, count - totalBytesRead);
                        if (bytesRead == 0)
                        {
                            break;
                        }
                    }

                    totalBytesRead += bytesRead;
                }

                return totalBytesRead;
            }
        }

        private const string FxFocusPath = @"Assets\Audio\Sfx\ffx_ui_focus.wav";
        private const string FxConfirmPath = @"Assets\Audio\Sfx\ffx_ui_confirm.wav";
        private const string FxAltPath = @"Assets\Audio\Sfx\ffx_ui_alt.wav";
        private const float MusicDefaultVolume = 0.28f;
        private const float FxDefaultScale = 1.0f;
        private const float MusicFadeInStartVolume = 0.02f;
        private const int MusicFadeInDurationMs = 2400;
        private const int MusicFadeInStepMs = 45;

        private static readonly Dictionary<string, string> LegacyTrackAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["to-zanarkand"] = "1_02_zanarkand",
            ["otherworld"] = "1_05_otherworld",
            ["besaid"] = "1_18_besaid",
            ["yunas-theme"] = "2_01_yuna_s_theme",
            ["luca"] = "2_06_luca",
            ["blitzers"] = "1_17_the_blitzers",
            ["blitz-off"] = "2_11_blitz_off",
            ["wakkas-theme"] = "m_71_wakka_s_theme",
            ["wandering-flame"] = "4_07_wandering_flame",
            ["someday-the-dream-will-end"] = "4_08_someday_the_dream_will_end"
        };

        private readonly object _fxLock = new();
        private readonly List<WaveOutEvent> _activeFxOutputs = [];
        private readonly string _settingsPath;
        private readonly string _gameMusicDestinationRoot;
        private readonly GameMusicImportService _gameMusicImportService;

        private WaveOutEvent? _musicOutput;
        private AudioFileReader? _musicReader;
        private LoopStream? _musicLoop;
        private CancellationTokenSource? _startupLeadInCts;
        private CancellationTokenSource? _musicFadeInCts;
        private bool _musicPlaybackBlockedForIntro = true;
        private bool _startupIntroPlayed;

        private sealed class AudioPreferences
        {
            public string? SelectedTrackId { get; set; }
            public bool MusicEnabled { get; set; } = true;
            public bool SoundEffectsEnabled { get; set; } = true;
            public double VolumeMusic { get; set; } = 0.50;
            public double VolumeFx { get; set; } = 0.50;
            public double VolumePreview { get; set; } = 0.50;
            // ON by product decision (user request 2026-09-15): picking the game install is the
            // ownership confirmation; this flag is the explicit local-only import consent and can
            // be turned off in the music flyout. No audio bytes are ever redistributed.
            public bool AutoMusicImportEnabled { get; set; } = true;
        }

        [ObservableProperty] public bool musicEnabled = true;
        [ObservableProperty] public bool soundEffectsEnabled = true;
        [ObservableProperty] public string nowPlayingLabel = "Luca";
        [ObservableProperty] public string audioStatusSummary = "Music ready · FX ready";
        [ObservableProperty] public AudioTrackOption? selectedMusicTrack;
        [ObservableProperty] public double volumeMusic = 0.50;
        [ObservableProperty] public double volumeFx = 0.50;
        [ObservableProperty] public double volumePreview = 0.50;
        [ObservableProperty] public bool autoMusicImportEnabled = true;

        public ObservableCollection<AudioTrackOption> MusicTrackOptions { get; } = [];
        public GameMusicImportSelectionModel GameMusicImport { get; } = new();
        public string MusicTrackCountLabel => string.Format(Strings.AudioImportedTrackCountFormat, MusicTrackOptions.Count);

        private static float SliderToVolume(double sliderValue) => (float)(sliderValue * sliderValue);

        public AudioStudio_Service()
        {
            _settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FFXProjectEditor",
                "audio-settings.json");
            _gameMusicDestinationRoot = GameMusicImportService.GetDefaultLocalDestinationRoot();
            _gameMusicImportService = new GameMusicImportService();
            LoadImportedMusicIntoCollection();

            AudioPreferences preferences = LoadPreferences();
            musicEnabled = preferences.MusicEnabled;
            soundEffectsEnabled = preferences.SoundEffectsEnabled;
            volumeMusic = preferences.VolumeMusic;
            volumeFx = preferences.VolumeFx;
            volumePreview = preferences.VolumePreview;
            autoMusicImportEnabled = preferences.AutoMusicImportEnabled;
            selectedMusicTrack = ResolveTrackById(preferences.SelectedTrackId) ?? ResolveTrackById("luca") ?? (MusicTrackOptions.Count > 0 ? MusicTrackOptions[0] : null);
            nowPlayingLabel = selectedMusicTrack?.DisplayName ?? Strings.AudioNoTrack;
            RefreshAudioStatus();

            // Automatic default-track import: the game install and workspace roots are already
            // hydrated by App.OnFrameworkInitializationCompleted before this service exists, so
            // the delayed kick sees the final paths. Root changes re-trigger via PropertyChanged.
            Project_Service.Instance.PropertyChanged += OnProjectPathsChanged;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(800).ConfigureAwait(false);
                    await EnsureDefaultGameMusicAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Auto-import is opportunistic; failures surface via StatusMessage only.
                }
            });
        }

        private void OnProjectPathsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(Project_Service.Path_GameInstallRoot)
                or nameof(Project_Service.ProjectPath)
                or nameof(Project_Service.Path_FfxPs2Root)
                or nameof(Project_Service.Path_Ps3DataRoot))
            {
                _ = EnsureDefaultGameMusicAsync();
            }
        }

        partial void OnAutoMusicImportEnabledChanged(bool value)
        {
            SavePreferences();
            if (value)
            {
                _ = EnsureDefaultGameMusicAsync();
            }
        }

        partial void OnMusicEnabledChanged(bool value)
        {
            SavePreferences();
            if (CanStartMusicPlayback())
            {
                RefreshMusicPlayback();
            }
            else if (!value)
            {
                StopMusic();
            }
            RefreshAudioStatus();
        }

        partial void OnVolumeMusicChanged(double value)
        {
            SavePreferences();
            CancelMusicFadeIn(); // aborta qualquer fade em andamento
            if (_musicReader != null)
                _musicReader.Volume = SliderToVolume(value);
            RefreshAudioStatus();
        }

        partial void OnVolumeFxChanged(double value)
        {
            SavePreferences();
            RefreshAudioStatus();
        }

        partial void OnVolumePreviewChanged(double value)
        {
            SavePreferences();
            RefreshAudioStatus();
        }

        partial void OnSoundEffectsEnabledChanged(bool value)
        {
            SavePreferences();
            RefreshAudioStatus();
        }

        partial void OnSelectedMusicTrackChanged(AudioTrackOption? value)
        {
            NowPlayingLabel = value?.DisplayName ?? Strings.AudioNoTrack;
            SavePreferences();
            if (CanStartMusicPlayback())
            {
                RefreshMusicPlayback();
            }
            RefreshAudioStatus();
        }

        public void SetTrack(AudioTrackOption? track)
        {
            if (track == null)
            {
                RefreshAudioStatus("No music track selected.");
                return;
            }

            _musicPlaybackBlockedForIntro = false;
            if (!MusicEnabled)
            {
                MusicEnabled = true;
            }

            if (!ReferenceEquals(SelectedMusicTrack, track))
            {
                SelectedMusicTrack = track;
            }
            else
            {
                SavePreferences();
                RefreshMusicPlayback();
                RefreshAudioStatus();
            }
        }

        public void PreviewSelectedMusicTrack()
        {
            SetTrack(SelectedMusicTrack);
        }

        /// <summary>
        /// Imports the current explicit ten-track allowlist selection from a user-picked FSB.
        /// The source path is call-scoped only: it is never copied to a field, preferences, or manifest.
        /// </summary>
        public async Task<bool> ImportSelectedGameMusicAsync(string sourceFsbPath)
        {
            IReadOnlyList<string> selection = GameMusicImport.GetExplicitSelection();
            if (!GameMusicImport.UserConfirmedOwnershipAndLocalUse || selection.Count == 0)
            {
                GameMusicImport.StatusMessage = Strings.AudioImportSelectionRequired;
                return false;
            }
            if (GameMusicImport.IsBusy)
            {
                return false;
            }

            string? toolPath = FfxAudioToolsLocator.LocateVgmStream();
            string? expectedToolHash = toolPath == null
                ? null
                : GameMusicImportCatalog.ValidatedVgmstreamSha256For(Path.GetFileName(toolPath));
            if (toolPath == null || expectedToolHash == null)
            {
                GameMusicImport.StatusMessage = Strings.AudioImportToolMissing;
                return false;
            }

            GameMusicImport.IsBusy = true;
            GameMusicImport.ProgressPercent = 0;
            GameMusicImport.StatusMessage = Strings.AudioImportValidating;
            try
            {
                Progress<GameMusicImportProgress> progress = new(UpdateGameMusicImportProgress);
                GameMusicImportResult result = await _gameMusicImportService.ImportAsync(
                    new GameMusicImportRequest(
                        sourceFsbPath,
                        _gameMusicDestinationRoot,
                        new GameMusicToolDescriptor(
                            toolPath,
                            expectedToolHash),
                        selection,
                        UserConfirmedSourceOwnership: true,
                        UserConsentedToLocalImport: true),
                    progress);

                ReloadImportedMusic();
                GameMusicImport.ProgressPercent = 100;
                GameMusicImport.StatusMessage = string.Format(
                    Strings.AudioImportSuccessFormat,
                    result.Tracks.Count);
                GameMusicImport.ResetOptIn();
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException
                                           or InvalidDataException
                                           or InvalidOperationException
                                           or IOException
                                           or UnauthorizedAccessException)
            {
                GameMusicImport.StatusMessage = string.Format(
                    Strings.AudioImportFailureFormat,
                    ex.Message);
                DebugLog.Error(
                    "AudioStudio.GameMusicImport",
                    $"Local-only music import failed ({ex.GetType().Name}).");
                return false;
            }
            finally
            {
                GameMusicImport.IsBusy = false;
            }
        }

        /// <summary>
        /// Auto-import of the ten approved default tracks. Runs once per missing set, in the
        /// background, whenever the game install/extraction roots change or the editor starts.
        /// Skips when every approved track is already imported and hash-valid. The explicit
        /// per-import opt-in flow is untouched: automatic import is consented by the
        /// AutoMusicImportEnabled preference and by the user having selected their own install.
        /// </summary>
        private int _autoImportRunning;

        public Task<bool> EnsureDefaultGameMusicAsync(CancellationToken cancellationToken = default)
        {
            if (!AutoMusicImportEnabled
                || GameMusicImport.IsBusy
                || Interlocked.Exchange(ref _autoImportRunning, 1) != 0)
            {
                return Task.FromResult(false);
            }

            return Task.Run(async () =>
            {
                try
                {
                    return await EnsureDefaultGameMusicCoreAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    Volatile.Write(ref _autoImportRunning, 0);
                }
            });
        }

        private async Task<bool> EnsureDefaultGameMusicCoreAsync(CancellationToken cancellationToken)
        {
            HashSet<string> presentIds = new(
                ImportedGameMusicCatalog.Load(_gameMusicDestinationRoot).Select(track => track.Id),
                StringComparer.Ordinal);
            string[] missing = GameMusicImportCatalog.Tracks
                .Where(track => !presentIds.Contains(track.Id))
                .Select(track => track.Id)
                .ToArray();
            if (missing.Length == 0)
            {
                SetGameMusicImportStatus(Strings.AudioAutoImportAlreadyPresent);
                DebugLog.Info(
                    "AudioStudio.GameMusicAutoImport",
                    "All default tracks already imported and hash-valid; skipping.");
                return true;
            }

            string? sourceFsb = GameMusicSourceLocator.Locate();
            if (sourceFsb == null)
            {
                SetGameMusicImportStatus(Strings.AudioAutoImportNoFsb);
                DebugLog.Info(
                    "AudioStudio.GameMusicAutoImport",
                    $"Default music bank not reachable; {missing.Length} track(s) pending. Checked known install/extraction roots.");
                return false;
            }

            string? toolPath = FfxAudioToolsLocator.LocateVgmStream();
            string? expectedToolHash = toolPath == null
                ? null
                : GameMusicImportCatalog.ValidatedVgmstreamSha256For(Path.GetFileName(toolPath));
            if (toolPath == null || expectedToolHash == null)
            {
                SetGameMusicImportStatus(Strings.AudioImportToolMissing);
                return false;
            }

            PostGameMusicImportState(busy: true, percent: 0, status: Strings.AudioImportValidating);
            try
            {
                Progress<GameMusicImportProgress> progress = new(report =>
                    Dispatcher.UIThread.Post(() => UpdateGameMusicImportProgress(report)));
                GameMusicImportResult result = await _gameMusicImportService.ImportAsync(
                    new GameMusicImportRequest(
                        sourceFsb,
                        _gameMusicDestinationRoot,
                        new GameMusicToolDescriptor(toolPath, expectedToolHash),
                        missing,
                        UserConfirmedSourceOwnership: true,
                        UserConsentedToLocalImport: true),
                    progress,
                    cancellationToken).ConfigureAwait(false);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ReloadImportedMusic();
                    GameMusicImport.ProgressPercent = 100;
                    GameMusicImport.StatusMessage = string.Format(
                        Strings.AudioAutoImportSuccessFormat,
                        result.Tracks.Count);
                });
                DebugLog.Info(
                    "AudioStudio.GameMusicAutoImport",
                    $"Default music import published {result.Tracks.Count} track(s).");
                return true;
            }
            catch (OperationCanceledException)
            {
                PostGameMusicImportState(busy: false, percent: 0, status: Strings.AudioAutoImportCancelled);
                return false;
            }
            catch (Exception ex) when (ex is ArgumentException
                                           or InvalidDataException
                                           or InvalidOperationException
                                           or IOException
                                           or UnauthorizedAccessException
                                           or TimeoutException)
            {
                PostGameMusicImportState(
                    busy: false,
                    percent: 0,
                    status: string.Format(Strings.AudioImportFailureFormat, ex.Message));
                DebugLog.Error(
                    "AudioStudio.GameMusicAutoImport",
                    $"Automatic default-track import failed ({ex.GetType().Name}).");
                return false;
            }
            finally
            {
                Dispatcher.UIThread.Post(() => GameMusicImport.IsBusy = false);
            }
        }

        private void SetGameMusicImportStatus(string status) =>
            Dispatcher.UIThread.Post(() => GameMusicImport.StatusMessage = status);

        private void PostGameMusicImportState(bool busy, int percent, string status) =>
            Dispatcher.UIThread.Post(() =>
            {
                GameMusicImport.IsBusy = busy;
                GameMusicImport.ProgressPercent = percent;
                GameMusicImport.StatusMessage = status;
            });

        public int ReloadImportedMusic()
        {
            string? preferredTrackId = SelectedMusicTrack?.Id;
            StopMusic();
            LoadImportedMusicIntoCollection();
            SelectedMusicTrack = ResolveTrackById(preferredTrackId)
                ?? ResolveTrackById("2_06_luca")
                ?? (MusicTrackOptions.Count > 0 ? MusicTrackOptions[0] : null);
            OnPropertyChanged(nameof(MusicTrackCountLabel));
            DebugLog.Info(
                "AudioStudio.GameMusicImport",
                $"Reloaded {MusicTrackOptions.Count} locally imported track(s). No source path retained.");
            return MusicTrackOptions.Count;
        }

        private void UpdateGameMusicImportProgress(GameMusicImportProgress progress)
        {
            GameMusicImport.ProgressPercent = progress.Percent;
            GameMusicImport.StatusMessage = progress.Phase switch
            {
                GameMusicImportPhase.Validating => Strings.AudioImportValidating,
                GameMusicImportPhase.Probing => string.Format(
                    Strings.AudioImportProbingFormat,
                    progress.CompletedTracks,
                    progress.TotalTracks),
                GameMusicImportPhase.Decoding => string.Format(
                    Strings.AudioImportDecodingFormat,
                    progress.CompletedTracks,
                    progress.TotalTracks),
                GameMusicImportPhase.Publishing => Strings.AudioImportPublishing,
                GameMusicImportPhase.Complete => Strings.AudioImportFinishing,
                _ => GameMusicImport.StatusMessage,
            };
        }

        public void PlayNavigation()
        {
            PlayEffect(FxAltPath, 0.38f);
        }

        public void PlaySoftNavigation()
        {
            PlayEffect(FxAltPath, 0.24f);
        }

        public void PlayMiniEditorHover()
        {
            PlayEffect(FxAltPath, 0.18f);
        }

        public void PlayMiniEditorConfirm()
        {
            PlayEffect(FxFocusPath, 0.38f);
        }

        public void PlayListSelection()
        {
            PlayEffect(FxFocusPath, 0.54f);
        }

        public void PlayConfirm()
        {
            PlayEffect(FxFocusPath, 0.62f);
        }

        public void PlayToggle()
        {
            PlayEffect(FxFocusPath, 0.46f);
        }

        public void PlayAlternative()
        {
            PlayEffect(FxAltPath, 0.65f);
        }

        public void PlayEditorOpen()
        {
            PlayEffect(FxConfirmPath, 1.00f);
        }

        public async void BeginStartupSequence()
        {
            if (_startupIntroPlayed)
            {
                return;
            }

            _startupIntroPlayed = true;
            _musicPlaybackBlockedForIntro = true;
            StopMusic();
            PlayEditorOpen();

            _startupLeadInCts?.Cancel();
            _startupLeadInCts?.Dispose();
            _startupLeadInCts = new CancellationTokenSource();
            CancellationToken token = _startupLeadInCts.Token;

            try
            {
                int delayMs = GetAudioDurationMilliseconds(FxConfirmPath, 950) + 90;
                await Task.Delay(delayMs, token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            _musicPlaybackBlockedForIntro = false;
            RefreshMusicPlayback();
            RefreshAudioStatus();
        }

        private void PlayEffect(string relativePath, float baseVolume)
        {
            if (!SoundEffectsEnabled)
            {
                return;
            }

            string resolvedPath = ResolveAssetPath(relativePath);
            if (!File.Exists(resolvedPath))
            {
                RefreshAudioStatus($"Missing FX asset: {Path.GetFileName(relativePath)}");
                return;
            }

            try
            {
                float actualVolume = (float)(baseVolume * SliderToVolume(VolumeFx));
                AudioFileReader reader = new(resolvedPath)
                {
                    Volume = actualVolume
                };
                WaveOutEvent output = new();

                EventHandler<StoppedEventArgs>? onStopped = null;
                onStopped = (sender, args) =>
                {
                    output.PlaybackStopped -= onStopped;
                    lock (_fxLock)
                    {
                        _activeFxOutputs.Remove(output);
                    }
                    output.Dispose();
                    reader.Dispose();
                };

                output.PlaybackStopped += onStopped;
                output.Init(reader);

                lock (_fxLock)
                {
                    _activeFxOutputs.Add(output);
                }

                output.Play();
            }
            catch (Exception ex)
            {
                RefreshAudioStatus($"FX offline: {ex.Message}");
            }
        }

        private void RefreshMusicPlayback()
        {
            StopMusic();
            CancelMusicFadeIn();

            if (!CanStartMusicPlayback() || !MusicEnabled || SelectedMusicTrack == null)
            {
                RefreshAudioStatus();
                return;
            }

            string resolvedPath = ResolveAssetPath(SelectedMusicTrack.RelativePath);
            if (!File.Exists(resolvedPath))
            {
                RefreshAudioStatus($"Missing music asset: {Path.GetFileName(SelectedMusicTrack.RelativePath)}");
                return;
            }

            try
            {
                float targetVol = SliderToVolume(VolumeMusic);
                _musicReader = new AudioFileReader(resolvedPath)
                {
                    Volume = targetVol
                };

                (long loopStart, long loopEnd) = ResolveLoopByteRange(_musicReader, SelectedMusicTrack);
                _musicLoop = new LoopStream(_musicReader, loopStart, loopEnd);
                _musicOutput = new WaveOutEvent();
                _musicOutput.Init(_musicLoop);
                _musicOutput.Play();
            }
            catch (Exception ex)
            {
                StopMusic();
                RefreshAudioStatus($"Music offline: {ex.Message}");
                return;
            }

            RefreshAudioStatus();
        }

        private void StopMusic()
        {
            CancelMusicFadeIn();

            try
            {
                _musicOutput?.Stop();
            }
            catch
            {
                // Ignore output-stop race conditions during disposal.
            }

            _musicOutput?.Dispose();
            _musicOutput = null;

            _musicLoop?.Dispose();
            _musicLoop = null;

            _musicReader?.Dispose();
            _musicReader = null;
        }

        private void StartMusicFadeIn(AudioFileReader reader, float targetVolume)
        {
            CancelMusicFadeIn();

            _musicFadeInCts = new CancellationTokenSource();
            CancellationToken token = _musicFadeInCts.Token;

            _ = Task.Run(async () =>
            {
                float startVolume = MusicFadeInStartVolume;
                int steps = Math.Max(1, MusicFadeInDurationMs / MusicFadeInStepMs);

                for (int step = 0; step < steps; step++)
                {
                    if (token.IsCancellationRequested || !ReferenceEquals(reader, _musicReader))
                    {
                        return;
                    }

                    float progress = (step + 1f) / steps;
                    reader.Volume = startVolume + ((targetVolume - startVolume) * progress);

                    try
                    {
                        await Task.Delay(MusicFadeInStepMs, token);
                    }
                    catch (TaskCanceledException)
                    {
                        return;
                    }
                }

                if (!token.IsCancellationRequested && ReferenceEquals(reader, _musicReader))
                {
                    reader.Volume = targetVolume;
                }
            }, token);
        }

        private void CancelMusicFadeIn()
        {
            _musicFadeInCts?.Cancel();
            _musicFadeInCts?.Dispose();
            _musicFadeInCts = null;
        }

        private void RefreshAudioStatus(string? overrideMessage = null)
        {
            if (!string.IsNullOrWhiteSpace(overrideMessage))
            {
                AudioStatusSummary = overrideMessage;
                return;
            }

            string musicSummary = MusicEnabled
                ? $"Music: {SelectedMusicTrack?.DisplayName ?? "Unavailable"} ({(int)(VolumeMusic * 100)}%)"
                : "Music: Off";

            string fxSummary = SoundEffectsEnabled ? $"FX: On ({(int)(VolumeFx * 100)}%)" : "FX: Off";
            AudioStatusSummary = $"{musicSummary} · {fxSummary}";
        }

        private static string ResolveAssetPath(string relativePath)
        {
            return Path.IsPathRooted(relativePath)
                ? Path.GetFullPath(relativePath)
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, relativePath));
        }

        private bool CanStartMusicPlayback()
        {
            return !_musicPlaybackBlockedForIntro;
        }

        private void LoadImportedMusicIntoCollection()
        {
            MusicTrackOptions.Clear();
            foreach (ImportedGameMusicTrack track in ImportedGameMusicCatalog.Load(_gameMusicDestinationRoot))
            {
                MusicTrackOptions.Add(new AudioTrackOption(
                    track.Id,
                    track.DisplayName,
                    track.FilePath,
                    track.LoopStartSamples,
                    track.LoopEndSamples));
            }
        }

        private static (long loopStartPosition, long loopEndPosition) ResolveLoopByteRange(WaveStream stream, AudioTrackOption? track)
        {
            if (track?.LoopStartSamples is not long loopStartSamples || track.LoopEndSamples is not long loopEndSamples)
            {
                return (0, stream.Length);
            }

            long bytesPerSampleFrame = stream.WaveFormat.BlockAlign;
            long loopStartPosition = loopStartSamples * bytesPerSampleFrame;
            long loopEndPosition = loopEndSamples * bytesPerSampleFrame;

            loopStartPosition = Math.Max(0, Math.Min(loopStartPosition, stream.Length));
            loopEndPosition = Math.Max(loopStartPosition, Math.Min(loopEndPosition, stream.Length));

            return (loopStartPosition, loopEndPosition);
        }

        private AudioTrackOption? ResolveTrackById(string? trackId)
        {
            if (string.IsNullOrWhiteSpace(trackId))
            {
                return null;
            }

            if (LegacyTrackAliases.TryGetValue(trackId, out string? canonicalId))
            {
                trackId = canonicalId;
            }

            foreach (AudioTrackOption option in MusicTrackOptions)
            {
                if (string.Equals(option.Id, trackId, StringComparison.OrdinalIgnoreCase))
                {
                    return option;
                }
            }

            return null;
        }

        private AudioPreferences LoadPreferences()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    return new AudioPreferences();
                }

                string json = File.ReadAllText(_settingsPath);
                AudioPreferences? preferences = JsonSerializer.Deserialize<AudioPreferences>(json);
                return preferences ?? new AudioPreferences();
            }
            catch
            {
                return new AudioPreferences();
            }
        }

        private void SavePreferences()
        {
            try
            {
                string? directory = Path.GetDirectoryName(_settingsPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                AudioPreferences preferences = new()
                {
                    SelectedTrackId = SelectedMusicTrack?.Id,
                    MusicEnabled = MusicEnabled,
                    SoundEffectsEnabled = SoundEffectsEnabled,
                    VolumeMusic = VolumeMusic,
                    VolumeFx = VolumeFx,
                    VolumePreview = VolumePreview,
                    AutoMusicImportEnabled = AutoMusicImportEnabled
                };

                string json = JsonSerializer.Serialize(preferences, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(_settingsPath, json);
            }
            catch
            {
                // Preference persistence should never break the editor shell.
            }
        }

        private int GetAudioDurationMilliseconds(string relativePath, int fallbackMs)
        {
            try
            {
                string resolvedPath = ResolveAssetPath(relativePath);
                if (!File.Exists(resolvedPath))
                {
                    return fallbackMs;
                }

                using AudioFileReader reader = new(resolvedPath);
                return Math.Max(1, (int)Math.Ceiling(reader.TotalTime.TotalMilliseconds));
            }
            catch
            {
                return fallbackMs;
            }
        }

        public void Dispose()
        {
            try
            {
                Project_Service.Instance.PropertyChanged -= OnProjectPathsChanged;
            }
            catch
            {
                // Unsubscribing a never-created service is a no-op at shutdown.
            }
            _startupLeadInCts?.Cancel();
            _startupLeadInCts?.Dispose();
            _startupLeadInCts = null;
            CancelMusicFadeIn();
            StopMusic();

            lock (_fxLock)
            {
                foreach (WaveOutEvent fxOutput in _activeFxOutputs)
                {
                    try
                    {
                        fxOutput.Stop();
                    }
                    catch
                    {
                        // Ignore stop errors while shutting the shell down.
                    }

                    fxOutput.Dispose();
                }

                _activeFxOutputs.Clear();
            }
        }
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FFXProjectEditor.FfxLib.Ps2;
using FFXProjectEditor.Services.Extras;
using NAudio.Wave;
using System;
using System.IO;
using System.Threading.Tasks;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor.Modules.Extras
{
    public partial class Ps2AudioBrowser_Control : UserControl, IRestorableModule
    {
        readonly Ps2AudioBrowser_DataModel dataModel;
        WaveOutEvent? player;
        AudioFileReader? reader;

        public Ps2AudioBrowser_Control()
        {
            InitializeComponent();
            dataModel = new Ps2AudioBrowser_DataModel();
            DataContext = dataModel;
            DetachedFromVisualTree += (_, _) => StopPlayback();
        }

        void Button_Refresh(object? sender, RoutedEventArgs e) => dataModel.Refresh();

        async void Button_SetRoot(object? sender, RoutedEventArgs e)
        {
            TopLevel? top = TopLevel.GetTopLevel(this);
            if (top == null) return;

            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select your EXTRACTED ffx_ps2 folder (the one containing ffx\\proj\\sound\\wave)",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                string? path = folders[0].TryGetLocalPath();
                if (!string.IsNullOrEmpty(path))
                    dataModel.SetRoot(path);
            }
        }

        void Button_OpenFolder(object? sender, RoutedEventArgs e)
        {
            if (dataModel.Selected != null)
            {
                string? dir = Path.GetDirectoryName(dataModel.Selected.FullPath);
                if (!string.IsNullOrEmpty(dir))
                    ExtrasFileOpen_Service.TryOpenInExplorer(dir);
            }
        }

        async void Button_Probe(object? sender, RoutedEventArgs e)
        {
            if (dataModel.Selected == null) return;
            dataModel.SelectedMetadata = "Asking vgmstream...";
            await Task.Run(() => dataModel.ProbeSelectedMetadata());
        }

        async void Button_ExportAll(object? sender, RoutedEventArgs e)
        {
            Ps2WdBankEntry? bank = dataModel.Selected;
            if (bank == null) return;

            TopLevel? top = TopLevel.GetTopLevel(this);
            if (top == null) return;

            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = $"Choose an output folder for {bank.Name} WAV files",
                AllowMultiple = false
            });
            if (folders.Count == 0) return;
            string? outDir = folders[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(outDir)) return;

            dataModel.PlaybackStatus = $"Exporting {bank.Name} via vgmstream...";
            var result = await Task.Run(() => Ps2VgmStream_Service.ExportAll(bank.FullPath, outDir));
            dataModel.PlaybackStatus = result.Message;
            if (result.Ok)
                ExtrasFileOpen_Service.TryOpenInExplorer(outDir);
        }

        async void Button_PlaySelected(object? sender, RoutedEventArgs e)
        {
            Ps2WdBankEntry? bank = dataModel.Selected;
            Ps2WdSample? sample = dataModel.SelectedSample;
            if (bank == null || sample == null) return;

            if (!Ps2VgmStream_Service.IsAvailable)
            {
                dataModel.PlaybackStatus = "vgmstream-cli.exe not found — use 'Set vgmstream...' first.";
                return;
            }

            StopPlayback();
            string tempDir = Path.Combine(Path.GetTempPath(), "ffxedit_wd");
            string wav = Path.Combine(tempDir, $"{bank.Name}_s{sample.Subsong:D2}.wav");
            dataModel.PlaybackStatus = $"Decoding subsong {sample.Subsong}...";

            var result = await Task.Run(() => Ps2VgmStream_Service.ExportOne(bank.FullPath, sample.Subsong, wav));
            if (!result.Ok)
            {
                dataModel.PlaybackStatus = result.Message;
                return;
            }

            try
            {
                reader = new AudioFileReader(wav);
                reader.Volume = (float)dataModel.VolumePreview;
                player = new WaveOutEvent();
                player.PlaybackStopped += OnPlaybackStopped;
                player.Init(reader);
                player.Play();
                dataModel.PlaybackStatus = $"Playing {bank.Name} subsong {sample.Subsong} at {dataModel.VolumePreview:P0} volume.";
            }
            catch (Exception ex)
            {
                dataModel.PlaybackStatus = $"Playback failed: {ex.Message}";
                StopPlayback();
            }
        }

        void Button_Stop(object? sender, RoutedEventArgs e)
        {
            StopPlayback();
            dataModel.PlaybackStatus = "Stopped.";
        }

        void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                dataModel.PlaybackStatus = "Idle.";
                StopPlayback();
            });
        }

        void StopPlayback()
        {
            try
            {
                if (player != null)
                {
                    player.PlaybackStopped -= OnPlaybackStopped;
                    player.Stop();
                    player.Dispose();
                    player = null;
                }
                reader?.Dispose();
                reader = null;
            }
            catch
            {
                // best-effort teardown
            }
        }
    }
}

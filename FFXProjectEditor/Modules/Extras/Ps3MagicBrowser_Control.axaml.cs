using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FFXProjectEditor.Services.Extras;
using FFXProjectEditor.Utils;
using System;
using System.Collections.Generic;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor.Modules.Extras
{
    public partial class Ps3MagicBrowser_Control : UserControl, IRestorableModule
    {
        readonly Ps3MagicBrowser_DataModel dataModel;
        readonly DispatcherTimer playTimer;

        public Ps3MagicBrowser_Control(int? initialMagicId = null)
        {
            InitializeComponent();
            dataModel = new Ps3MagicBrowser_DataModel(initialMagicId);
            DataContext = dataModel;

            playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            playTimer.Tick += (_, _) => dataModel.AdvanceLayer();
            DetachedFromVisualTree += (_, _) => StopPlay();
        }

        void Button_Refresh(object? sender, RoutedEventArgs e)
        {
            dataModel.Refresh();
        }

        void Button_OpenMagicFolder(object? sender, RoutedEventArgs e)
        {
            if (dataModel.SelectedMagic != null)
                ExtrasFileOpen_Service.TryOpenInExplorer(dataModel.SelectedMagic.FullPath);
        }

        void Button_OpenRuntimeDll(object? sender, RoutedEventArgs e)
        {
            if (dataModel.HasRuntimeDll)
                ExtrasFileOpen_Service.TryOpenInExplorer(dataModel.RuntimeDllPath);
        }

        async void Button_ExtractRuntimeDll(object? sender, RoutedEventArgs e)
        {
            if (!dataModel.HasRuntimeDll)
                return;

            List<string> folders = await AvaloniaDialog_Util.OpenFolderDialog(this, "Choose output folder for runtime magic DLL decompile");
            if (folders.Count > 0)
                dataModel.DecompileSelectedRuntimeDll(folders[0]);
        }

        void Button_OpenTextureFile(object? sender, RoutedEventArgs e)
        {
            if (dataModel.SelectedTexture != null)
                ExtrasFileOpen_Service.TryOpenInExplorer(dataModel.SelectedTexture.Texture.FullPath);
        }

        async void Button_ExtractDds(object? sender, RoutedEventArgs e)
        {
            if (dataModel.SelectedTexture == null)
                return;

            string path = await AvaloniaDialog_Util.SaveFileDialog(
                this,
                "Extract magic texture DDS",
                dataModel.SuggestedDdsFileName,
                "dds",
                new List<FilePickerFileType>
                {
                    new("DDS") { Patterns = new[] { "*.dds" } }
                });

            if (!string.IsNullOrWhiteSpace(path))
                dataModel.ExtractSelectedDds(path);
        }

        async void Button_RepackDds(object? sender, RoutedEventArgs e)
        {
            if (dataModel.SelectedTexture == null)
                return;

            List<string> files = await AvaloniaDialog_Util.OpenFileDialog(
                this,
                "Choose edited DDS payload",
                false,
                dataModel.SuggestedDdsFileName,
                new List<FilePickerFileType>
                {
                    new("DDS or raw mip0") { Patterns = new[] { "*.dds", "*.bin" } }
                });

            if (files.Count == 0)
                return;

            string outputPath = await AvaloniaDialog_Util.SaveFileDialog(
                this,
                "Save repacked .dds.phyre",
                dataModel.SuggestedPhyreFileName,
                "phyre",
                new List<FilePickerFileType>
                {
                    new("Phyre DDS") { Patterns = new[] { "*.dds.phyre", "*.phyre" } }
                });

            if (!string.IsNullOrWhiteSpace(outputPath))
                dataModel.RepackSelectedDds(files[0], outputPath);
        }

        async void Button_RecolorTexture(object? sender, RoutedEventArgs e)
        {
            if (dataModel.SelectedTexture == null)
                return;

            string suggested = dataModel.SuggestedPhyreFileName.EndsWith(".dds.phyre", StringComparison.OrdinalIgnoreCase)
                ? dataModel.SuggestedPhyreFileName[..^".dds.phyre".Length] + ".recolor.dds.phyre"
                : dataModel.SuggestedPhyreFileName + ".recolor.dds.phyre";

            string outputPath = await AvaloniaDialog_Util.SaveFileDialog(
                this,
                "Save recolored .dds.phyre",
                suggested,
                "phyre",
                new List<FilePickerFileType>
                {
                    new("Phyre DDS") { Patterns = new[] { "*.dds.phyre", "*.phyre" } }
                });

            if (!string.IsNullOrWhiteSpace(outputPath))
                dataModel.RecolorSelectedTexture(outputPath);
        }

        async void Button_RecolorFolder(object? sender, RoutedEventArgs e)
        {
            if (dataModel.SelectedMagic == null)
                return;

            List<string> folders = await AvaloniaDialog_Util.OpenFolderDialog(this, "Choose output folder for recolored magic_####");
            if (folders.Count > 0)
                dataModel.RecolorSelectedMagicFolder(folders[0]);
        }

        void Button_TogglePlay(object? sender, RoutedEventArgs e)
        {
            dataModel.IsPlaying = !dataModel.IsPlaying;
            if (dataModel.IsPlaying)
                playTimer.Start();
            else
                playTimer.Stop();
        }

        void StopPlay()
        {
            playTimer.Stop();
            dataModel.IsPlaying = false;
        }

        public int? GetSelectedMagicId() => dataModel.SelectedMagicId;
    }
}

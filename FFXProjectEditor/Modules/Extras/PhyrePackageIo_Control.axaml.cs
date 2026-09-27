using FFXProjectEditor.Resources;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FFXProjectEditor.Services.Extras;
using FFXProjectEditor.Utils;
using System.Collections.Generic;
using System.IO;

using FFXProjectEditor.Modules.Common;
using System.Linq;
using Avalonia.VisualTree;
using FFXProjectEditor.Services;
namespace FFXProjectEditor.Modules.Extras
{
    public partial class PhyrePackageIo_Control : UserControl, IRestorableModule
    {
        readonly PhyrePackageIo_DataModel dataModel;

        public PhyrePackageIo_Control(string? initialSourcePath = null)
        {
            InitializeComponent();
            dataModel = new PhyrePackageIo_DataModel();
            DataContext = dataModel;
            if (!string.IsNullOrWhiteSpace(initialSourcePath) && File.Exists(initialSourcePath))
            {
                dataModel.SetSourcePath(initialSourcePath);
                dataModel.InspectSource();
            }
        }

        async void Button_BrowseSource(object? sender, RoutedEventArgs e)
        {
            List<string> files = await AvaloniaDialog_Util.OpenFileDialog(
                this,
                "Choose source Phyre package",
                false,
                "asset.dae.phyre",
                PhyreFileTypes());

            if (files.Count > 0)
            {
                dataModel.SetSourcePath(files[0]);
                dataModel.InspectSource();
            }
        }

        async void Button_BrowseReplacement(object? sender, RoutedEventArgs e)
        {
            List<string> files = await AvaloniaDialog_Util.OpenFileDialog(
                this,
                "Choose DDS/raw payload or compiled Phyre replacement",
                false,
                "replacement.dae.phyre",
                new List<FilePickerFileType>
                {
                    new("DDS / raw / Phyre") { Patterns = new[] { "*.dds", "*.bin", "*.dds.phyre", "*.dae.phyre", "*.ags.phyre", "*.fx.phyre", "*.phyre" } }
                });

            if (files.Count > 0)
                dataModel.SetReplacementPath(files[0]);
        }

        async void Button_BrowseOutputFile(object? sender, RoutedEventArgs e)
        {
            string path = await AvaloniaDialog_Util.SaveFileDialog(
                this,
                "Choose output Phyre package",
                "edited.phyre",
                "phyre",
                PhyreFileTypes());

            if (!string.IsNullOrWhiteSpace(path))
                dataModel.SetOutputPath(path);
        }

        async void Button_BrowseOutputFolder(object? sender, RoutedEventArgs e)
        {
            List<string> folders = await AvaloniaDialog_Util.OpenFolderDialog(this, "Choose Phyre extract output folder");
            if (folders.Count > 0)
                dataModel.SetOutputFolder(folders[0]);
        }

        void Button_Inspect(object? sender, RoutedEventArgs e) => dataModel.InspectSource();
        void Button_ExtractPackage(object? sender, RoutedEventArgs e) => dataModel.ExtractPackage();
        void Button_ImportDds(object? sender, RoutedEventArgs e) => dataModel.ImportDdsPayload();
        void Button_ImportCompiled(object? sender, RoutedEventArgs e) => dataModel.ImportCompiledPackage();

        async void Button_ExtractDds(object? sender, RoutedEventArgs e)
        {
            string path = await AvaloniaDialog_Util.SaveFileDialog(
                this,
                "Extract DDS mip0",
                "extracted.dds",
                "dds",
                new List<FilePickerFileType>
                {
                    new("DDS") { Patterns = new[] { "*.dds" } }
                });

            if (!string.IsNullOrWhiteSpace(path))
                dataModel.ExtractDds(path);
        }

        private async void Button_ReviewDiff_Click(object? sender, RoutedEventArgs e)
        {
            AudioStudio_Service.Instance.PlayEditorOpen();

            string pkgName = System.IO.Path.GetFileName(dataModel.SourcePath) ?? "Phyre Package";
            
            var preview = new Core.OperationPreview
            {
                OperationId = "phyre-package-diff",
                DisplayName = $"Phyre Package IO ({pkgName})",
                FileCount = 1,
                TotalBytes = 2048,
                OverallRisk = Core.RiskLevel.Safe,
                FilePreviewSummaries = new System.Collections.Generic.List<Core.FilePreviewSummary>
                {
                    new Core.FilePreviewSummary
                    {
                        FileId = pkgName,
                        SourceRelativePath = $"{pkgName}",
                        OutputRelativePath = $"{pkgName}",
                        BeforeHash = "before",
                        PredictedAfterHash = "after",
                        ByteDiffLines = new[] { "0x0000: [Diff do payload Phyre]" },
                        DisassemblyDiffLines = System.Array.Empty<string>(),
                        SemanticDiffLines = new[]
                        {
                            string.Format(Strings.U_Pio_FileBullet, pkgName),
                            Strings.U_Pio_OperationBullet
                        },
                        HumanSummary = string.Format(Strings.U_Pio_HumanSummary, pkgName)
                    }
                }
            };

            var mainWindow = this.GetVisualAncestors().OfType<Main_Window>().FirstOrDefault();
            if (mainWindow != null)
            {
                mainWindow.ShowChangeSetPreview(preview);
            }
            else
            {
                var window = new Window
                {
                    Title = Strings.U_Pio_ReviewTitle + pkgName,
                    Width = 550,
                    Height = 350,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = new Controls.DiffSummaryView
                    {
                        DataContext = preview.FilePreviewSummaries[0]
                    }
                };
                var topLevel = TopLevel.GetTopLevel(this) as Window;
                if (topLevel != null) await window.ShowDialog(topLevel);
                else window.Show();
            }
        }


        void Button_OpenOutputFolder(object? sender, RoutedEventArgs e)
        {
            ExtrasFileOpen_Service.TryOpenInExplorer(dataModel.OutputFolder);
        }

        static List<FilePickerFileType> PhyreFileTypes() =>
        [
            new("Phyre Packages") { Patterns = new[] { "*.dds.phyre", "*.dae.phyre", "*.ags.phyre", "*.fx.phyre", "*.phyre" } }
        ];
    }
}

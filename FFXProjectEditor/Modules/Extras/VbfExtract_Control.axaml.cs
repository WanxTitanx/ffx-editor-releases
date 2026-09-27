using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FFXProjectEditor.Services.Extras;
using FFXProjectEditor.Utils;
using System.Collections.Generic;
using System.Threading.Tasks;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor.Modules.Extras
{
    public partial class VbfExtract_Control : UserControl, IRestorableModule
    {
        readonly VbfExtract_DataModel dataModel;

        public VbfExtract_Control()
        {
            InitializeComponent();
            dataModel = new VbfExtract_DataModel();
            DataContext = dataModel;
        }

        void Button_DetectFfx(object? sender, RoutedEventArgs e) => dataModel.DetectFfx();
        void Button_DetectFfx2(object? sender, RoutedEventArgs e) => dataModel.DetectFfx2();
        void Button_DetectMetaMenu(object? sender, RoutedEventArgs e) => dataModel.DetectMetaMenu();
        void Button_RefreshProbe(object? sender, RoutedEventArgs e) => dataModel.RefreshProbe();
        void Button_Cancel(object? sender, RoutedEventArgs e) => dataModel.Cancel();

        async void Button_BrowseTool(object? sender, RoutedEventArgs e)
        {
            List<string> files = await AvaloniaDialog_Util.OpenFileDialog(
                this,
                "Choose vbfextract.exe",
                false,
                "vbfextract.exe",
                new List<FilePickerFileType>
                {
                    new("Executable") { Patterns = new[] { "*.exe" } }
                });

            if (files.Count > 0)
                dataModel.SetToolPath(files[0]);
        }

        async void Button_BrowseVbf(object? sender, RoutedEventArgs e)
        {
            List<string> files = await AvaloniaDialog_Util.OpenFileDialog(
                this,
                "Choose FFX/FFX-2 VBF archive",
                false,
                "FFX_Data.vbf",
                new List<FilePickerFileType>
                {
                    new("VBF Archive") { Patterns = new[] { "*.vbf" } }
                });

            if (files.Count > 0)
                dataModel.SetVbfPath(files[0]);
        }

        async void Button_BrowseDictionary(object? sender, RoutedEventArgs e)
        {
            List<string> files = await AvaloniaDialog_Util.OpenFileDialog(
                this,
                "Choose filename dictionary",
                false,
                "FFX_Data.txt",
                new List<FilePickerFileType>
                {
                    new("Text Dictionary") { Patterns = new[] { "*.txt" } }
                });

            if (files.Count > 0)
                dataModel.SetDictionaryPath(files[0]);
        }

        async void Button_BrowseOutput(object? sender, RoutedEventArgs e)
        {
            List<string> folders = await AvaloniaDialog_Util.OpenFolderDialog(this, "Choose VBF extraction output folder");
            if (folders.Count > 0)
                dataModel.SetOutputRoot(folders[0]);
        }

        async void Button_Extract(object? sender, RoutedEventArgs e)
        {
            await dataModel.StartExtractionAsync();
        }

        void Button_OpenOutput(object? sender, RoutedEventArgs e)
        {
            ExtrasFileOpen_Service.TryOpenInExplorer(dataModel.OutputRoot);
        }
    }
}

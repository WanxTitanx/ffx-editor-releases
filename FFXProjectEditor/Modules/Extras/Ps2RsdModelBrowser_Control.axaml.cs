using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FFXProjectEditor.Services.Extras;
using System.IO;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor.Modules.Extras
{
    public partial class Ps2RsdModelBrowser_Control : UserControl, IRestorableModule
    {
        readonly Ps2RsdModelBrowser_DataModel dataModel;

        public Ps2RsdModelBrowser_Control()
        {
            InitializeComponent();
            dataModel = new Ps2RsdModelBrowser_DataModel();
            DataContext = dataModel;
        }

        void Button_Refresh(object? sender, RoutedEventArgs e)
        {
            dataModel.Refresh();
        }

        async void Button_SetRoot(object? sender, RoutedEventArgs e)
        {
            TopLevel? top = TopLevel.GetTopLevel(this);
            if (top == null)
                return;

            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select your EXTRACTED ffx_ps2 folder (the one containing ffx\\yonishi_data)",
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

        void Button_OpenRsdFile(object? sender, RoutedEventArgs e)
        {
            if (dataModel.Selected != null)
                ExtrasFileOpen_Service.TryOpenInExplorer(dataModel.Selected.FullPath);
        }
    }
}

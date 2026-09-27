using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Services.Extras;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor.Modules.Extras
{
    public partial class TextureFamilyBrowser_Control : UserControl, IRestorableModule
    {
        readonly TextureFamilyBrowser_DataModel dataModel;

        public TextureFamilyBrowser_Control()
        {
            InitializeComponent();
            dataModel = new TextureFamilyBrowser_DataModel();
            DataContext = dataModel;
        }

        void Button_Refresh(object? sender, RoutedEventArgs e)
        {
            dataModel.Refresh();
        }

        void Button_OpenRoot(object? sender, RoutedEventArgs e)
        {
            ExtrasFileOpen_Service.TryOpenInExplorer(Services.Project_Service.Instance.Path_FfxPs2Root);
        }

        void Button_OpenFile(object? sender, RoutedEventArgs e)
        {
            if (dataModel.SelectedAsset != null)
                ExtrasFileOpen_Service.TryOpenInExplorer(dataModel.SelectedAsset.Metadata.FullPath);
        }

        void Button_OpenSupportFile(object? sender, RoutedEventArgs e)
        {
            if (dataModel.SelectedSupportAsset != null)
                ExtrasFileOpen_Service.TryOpenInExplorer(dataModel.SelectedSupportAsset.Metadata.FullPath);
        }
    }
}

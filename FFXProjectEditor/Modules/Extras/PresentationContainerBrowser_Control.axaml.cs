using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Services.Extras;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor.Modules.Extras
{
    public partial class PresentationContainerBrowser_Control : UserControl, IRestorableModule
    {
        readonly PresentationContainerBrowser_DataModel dataModel;

        public PresentationContainerBrowser_Control()
        {
            InitializeComponent();
            dataModel = new PresentationContainerBrowser_DataModel();
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
            if (dataModel.SelectedRecord != null)
                ExtrasFileOpen_Service.TryOpenInExplorer(dataModel.SelectedRecord.FullPath);
        }
    }
}

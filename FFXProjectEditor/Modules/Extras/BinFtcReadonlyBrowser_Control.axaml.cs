using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Services;
using FFXProjectEditor.Services.Extras;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor.Modules.Extras
{
    public partial class BinFtcReadonlyBrowser_Control : UserControl, IRestorableModule
    {
        readonly BinFtcReadonlyBrowser_DataModel dataModel;

        public BinFtcReadonlyBrowser_Control()
        {
            InitializeComponent();
            dataModel = new BinFtcReadonlyBrowser_DataModel();
            DataContext = dataModel;
        }

        void Button_Refresh(object? sender, RoutedEventArgs e)
        {
            dataModel.Refresh();
        }

        void Button_OpenRoot(object? sender, RoutedEventArgs e)
        {
            ExtrasFileOpen_Service.TryOpenInExplorer(Project_Service.Instance.ProjectPath);
        }

        void Button_OpenFile(object? sender, RoutedEventArgs e)
        {
            if (dataModel.SelectedFile != null)
                ExtrasFileOpen_Service.TryOpenInExplorer(dataModel.SelectedFile.FullPath);
        }
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Services.Extras;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor.Modules.Extras
{
    public partial class MagicEffectBrowser_Control : UserControl, IRestorableModule
    {
        readonly MagicEffectBrowser_DataModel dataModel;

        public MagicEffectBrowser_Control()
        {
            InitializeComponent();
            dataModel = new MagicEffectBrowser_DataModel();
            DataContext = dataModel;
        }

        void Button_Refresh(object? sender, RoutedEventArgs e)
        {
            dataModel.Refresh();
        }

        void Button_OpenKernel(object? sender, RoutedEventArgs e)
        {
            if (dataModel.SelectedKernel != null)
                ExtrasFileOpen_Service.TryOpenInExplorer(dataModel.SelectedKernel.FullPath);
        }

        void Button_OpenPackage(object? sender, RoutedEventArgs e)
        {
            if (dataModel.SelectedPackage != null)
                ExtrasFileOpen_Service.TryOpenInExplorer(dataModel.SelectedPackage.FullPath);
        }
    }
}

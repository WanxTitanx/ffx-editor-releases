using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Services.Extras;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor.Modules.Extras
{
    public partial class ProjectPipelineExplorer_Control : UserControl, IRestorableModule
    {
        readonly ProjectPipelineExplorer_DataModel dataModel;

        public ProjectPipelineExplorer_Control()
        {
            InitializeComponent();
            dataModel = new ProjectPipelineExplorer_DataModel();
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

        void Button_OpenSelectedCdIndex(object? sender, RoutedEventArgs e)
        {
            if (dataModel.SelectedCdIndex != null)
                ExtrasFileOpen_Service.TryOpenInExplorer(dataModel.SelectedCdIndex.DirectoryPath);
        }

        void Button_OpenSelectedDescriptor(object? sender, RoutedEventArgs e)
        {
            if (dataModel.SelectedDescriptor != null)
                ExtrasFileOpen_Service.TryOpenInExplorer(dataModel.SelectedDescriptor.Path);
        }

        void Button_OpenSelectedAbmap(object? sender, RoutedEventArgs e)
        {
            if (dataModel.SelectedAbmap != null)
                ExtrasFileOpen_Service.TryOpenInExplorer(dataModel.SelectedAbmap.Path);
        }
    }
}

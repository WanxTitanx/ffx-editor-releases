using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Services.Extras;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor.Modules.Extras
{
    public partial class Ps2KnowledgeHub_Control : UserControl, IRestorableModule
    {
        readonly Ps2KnowledgeHub_DataModel dataModel;

        public Ps2KnowledgeHub_Control()
        {
            InitializeComponent();
            dataModel = new Ps2KnowledgeHub_DataModel();
            DataContext = dataModel;
        }

        void Button_Refresh(object? sender, RoutedEventArgs e)
        {
            dataModel.Refresh();
        }

        void Button_OpenRoot(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string path)
                ExtrasFileOpen_Service.TryOpenInExplorer(path);
        }
    }
}

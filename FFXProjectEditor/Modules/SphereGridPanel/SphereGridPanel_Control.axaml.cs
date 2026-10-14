using Avalonia.Controls;
using Avalonia.Interactivity;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor.Modules.SphereGridPanel
{
    public partial class SphereGridPanel_Control : UserControl, IRestorableModule
    {
        readonly SphereGridPanel_DataModel _dataModel = new();

        public SphereGridPanel_Control()
        {
            InitializeComponent();
            DataContext = _dataModel;
            Loaded += (_, _) => _dataModel.Refresh();
        }

        void Button_Refresh(object? sender, RoutedEventArgs e) => _dataModel.Refresh();
        void Button_Save(object? sender, RoutedEventArgs e) => _dataModel.Save();
        void Button_RestoreBackup(object? sender, RoutedEventArgs e) => _dataModel.RestoreFromBackup();
        void Button_RestoreVanilla(object? sender, RoutedEventArgs e) => _dataModel.RestoreVanilla();
        void Button_PopulateClones(object? sender, RoutedEventArgs e) => _dataModel.PopulateClones();
        void Button_EnsureCommand(object? sender, RoutedEventArgs e) => _dataModel.EnsureSelectedCommand();
    }
}

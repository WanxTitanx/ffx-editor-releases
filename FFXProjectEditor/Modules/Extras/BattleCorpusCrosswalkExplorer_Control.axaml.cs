using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Services.Extras;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor.Modules.Extras
{
    public partial class BattleCorpusCrosswalkExplorer_Control : UserControl, IRestorableModule
    {
        readonly BattleCorpusCrosswalkExplorer_DataModel dataModel;

        public BattleCorpusCrosswalkExplorer_Control()
        {
            InitializeComponent();
            dataModel = new BattleCorpusCrosswalkExplorer_DataModel();
            DataContext = dataModel;
        }

        void Button_Refresh(object? sender, RoutedEventArgs e)
        {
            dataModel.Refresh();
        }

        void Button_OpenSelectedBattle(object? sender, RoutedEventArgs e)
        {
            if (dataModel.SelectedBattle != null)
                ExtrasFileOpen_Service.TryOpenInExplorer(dataModel.SelectedBattle.FullPath);
        }
    }
}

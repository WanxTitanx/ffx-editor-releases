using Avalonia.Controls;
using FFXProjectEditor.Modules.BlitzballPrizeExplorer;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor;

// Read-only Blitzball prize explorer surface. Consumes the closed Atlas catalog
// (SpiraDataAtlasCatalog.BlitzballPrizeDetails) — no writer, no runtime, no project gate.
public partial class BlitzballPrizeExplorer_Control : UserControl, IRestorableModule
{
    readonly BlitzballPrizeExplorer_DataModel dataModel;

    public BlitzballPrizeExplorer_Control()
    {
        dataModel = new BlitzballPrizeExplorer_DataModel();
        DataContext = dataModel;
        InitializeComponent();
    }
}

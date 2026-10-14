using FFXProjectEditor.Modules.Common;
using Avalonia.Controls;

namespace FFXProjectEditor.Modules.Unwired
{
    // Minimal read-only browser for a Wave-1 family that has a proven reader+writer+RT0 gate but no dedicated
    // editor surface. Lives under the "???" nav category. One control serves all three families (picked by ctor).
    public partial class UnwiredCatalog_Control : UserControl, IRestorableModule
    {
        // Parameterless ctor for the XAML designer only.
        public UnwiredCatalog_Control() : this(UnwiredFamily.BukiGetTreasure) { }

        public UnwiredCatalog_Control(UnwiredFamily family)
        {
            InitializeComponent();
            DataContext = new UnwiredCatalog_DataModel(family);
        }
    }
}

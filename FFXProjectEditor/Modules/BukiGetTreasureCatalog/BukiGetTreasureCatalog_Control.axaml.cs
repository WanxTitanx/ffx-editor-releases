using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules.Common;
using System.Collections.Generic;

namespace FFXProjectEditor.Modules.BukiGetRewards
{
    public partial class BukiGetTreasureCatalog_Control : UserControl, IRestorableModule
    {
        readonly BukiGetTreasureCatalog_DataModel dataModel;

        public BukiGetTreasureCatalog_Control() : this(null) { }

        public BukiGetTreasureCatalog_Control(int? preferredRow)
        {
            InitializeComponent();
            dataModel = new BukiGetTreasureCatalog_DataModel(preferredRow);
            dataModel.GuardrailSummary = "Writable writer mode. buki_get.bin can be edited and saved. Equipment names still need w_name.bin bridge.";
            DataContext = dataModel;
        }

        private void Button_Refresh(object? sender, RoutedEventArgs e)
        {
            dataModel.RefreshFromDisk();
        }

        private void Button_Save(object? sender, RoutedEventArgs e)
        {
            dataModel.Save();
        }

        private void Button_Undo(object? sender, RoutedEventArgs e)
        {
            dataModel.Undo();
        }

        private void Button_Discard(object? sender, RoutedEventArgs e)
        {
            dataModel.Discard();
        }

        // --- IRestorableModule (Jarvis-UI Fase D §D2) ---
        public Dictionary<string, object?>? CaptureState()
        {
            int? selectedIndex = dataModel.SelectedRow is { } row
                ? dataModel.DisplayedRows.IndexOf(row)
                : null;
            return new()
            {
                ["filterText"] = dataModel.FilterText,
                ["selectedIndex"] = selectedIndex >= 0 ? selectedIndex : null,
            };
        }

        public void RestoreState(Dictionary<string, object?>? state)
        {
            if (state == null) return;

            if (state.TryGetValue("filterText", out object? filterObj) && filterObj is string filter)
                dataModel.FilterText = filter;

            if (state.TryGetValue("selectedIndex", out object? indexObj) && indexObj is int idx && idx >= 0)
            {
                var displayed = dataModel.DisplayedRows;
                if (idx < displayed.Count)
                    dataModel.SelectedRow = displayed[idx];
            }
        }
    }
}

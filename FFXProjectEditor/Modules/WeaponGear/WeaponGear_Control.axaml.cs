using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Linq;

namespace FFXProjectEditor.Modules.WeaponGear
{
    public partial class WeaponGear_Control : UserControl
    {
        readonly WeaponGear_DataModel dataModel;

        public WeaponGear_Control()
        {
            InitializeComponent();
            dataModel = new WeaponGear_DataModel();
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

        private void Button_Discard(object? sender, RoutedEventArgs e)
        {
            dataModel.Discard();
        }

        private void Button_AddEntry(object? sender, RoutedEventArgs e)
        {
            // Clone the selected entry (or the first entry) as the template for the new entry.
            var template = dataModel.SelectedEntry?.Entry
                ?? dataModel.DisplayedEntries.FirstOrDefault()?.Entry;
            if (template is null) return;

            dataModel.AddNewEntry(template);
        }
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FFXProjectEditor.Modules.CustomBossCreator;
using FFXProjectEditor.Utils;
using System.Collections.Generic;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor
{
    public partial class CustomBossCreator_Control : UserControl, IRestorableModule
    {
        readonly CustomBossCreator_DataModel _dm;

        public CustomBossCreator_Control()
        {
            InitializeComponent();
            _dm = new CustomBossCreator_DataModel();
            DataContext = _dm;
        }

        private async void Button_ImportRecipe(object? sender, RoutedEventArgs e)
        {
            List<string> files = await AvaloniaDialog_Util.OpenFileDialog(
                this,
                "Importar Boss Recipe",
                false,
                "m000.bossrecipe.json",
                new List<FilePickerFileType>
                {
                    new("Boss Recipe") { Patterns = new[] { "*.bossrecipe.json", "*.json" } }
                });

            if (files.Count > 0)
            {
                _dm.ImportBossRecipe(files[0]);
            }
        }

        private async void Button_ExportRecipe(object? sender, RoutedEventArgs e)
        {
            string path = await AvaloniaDialog_Util.SaveFileDialog(
                this,
                "Exportar Boss Recipe",
                $"m{_dm.TargetId:D3}.bossrecipe.json",
                "json",
                new List<FilePickerFileType>
                {
                    new("Boss Recipe") { Patterns = new[] { "*.bossrecipe.json", "*.json" } }
                });

            if (!string.IsNullOrEmpty(path))
            {
                _dm.ExportBossRecipe(path);
            }
        }
    }
}

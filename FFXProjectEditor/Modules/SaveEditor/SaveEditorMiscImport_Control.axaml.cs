using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FFXProjectEditor.Modules.SaveEditor;
using FFXProjectEditor.Utils;
using System.Collections.Generic;
using System.Threading.Tasks;

using FFXProjectEditor.Modules.Common;
namespace FFXProjectEditor;

public partial class SaveEditorMiscImport_Control : UserControl, IRestorableModule
{
    public SaveEditorMiscImport_Control() => InitializeComponent();

    void ApplyMisc_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SaveEditor_DataModel m)
            m.ApplyMiscFields();
    }

    void BatchNgPlus_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SaveEditor_DataModel m)
            m.RunBatchAction(FfxLib.Save.FfxSaveSection.MiscImport, 55);
    }

    async void ImportEntire_Click(object? sender, RoutedEventArgs e) =>
        await ImportRegion(0, FfxLib.Save.FfxSaveCore.DataSize);

    async void ImportSphere_Click(object? sender, RoutedEventArgs e)
    {
        await ImportRegion(8748, 10467 - 8748);
        await ImportRegion(11308, 12188 - 11308 + 1);
        await ImportRegion(12588, 12601 - 12588 + 1);
    }

    async void ImportEquip_Click(object? sender, RoutedEventArgs e) =>
        await ImportRegion(17628, 22027 - 17628 + 1);

    async void ImportItems_Click(object? sender, RoutedEventArgs e) =>
        await ImportRegion(16140, 16907 - 16140 + 1);

    async void ImportBlitz_Click(object? sender, RoutedEventArgs e)
    {
        await ImportRegion(3252, 3257 - 3252 + 1);
        await ImportRegion(4652, 7211 - 4652 + 1);
    }

    async Task ImportRegion(int offset, int length)
    {
        if (DataContext is not SaveEditor_DataModel m)
            return;

        List<string> files = await AvaloniaDialog_Util.OpenFileDialog(
            this,
            "Donor FFX save",
            fileTypeFilter: [new FilePickerFileType("FFX saves") { Patterns = ["*.psu", "*.ffx", "*.bin", "*.*"] }]);

        if (files.Count == 0)
            return;

        if (length == FfxLib.Save.FfxSaveCore.DataSize)
            m.ImportEntireFromFile(files[0]);
        else
            m.ImportRegionFromFile(files[0], offset, length);
    }
}

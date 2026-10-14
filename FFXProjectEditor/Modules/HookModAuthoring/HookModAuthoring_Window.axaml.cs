using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FFXProjectEditor.Diagnostics;
using FFXProjectEditor.Resources;
using FFXProjectEditor.FfxLib.Mods;
using System.Text;

namespace FFXProjectEditor.Modules.HookModAuthoring;

public partial class HookModAuthoring_Window : Window
{
    public HookModAuthoring_Window() : this("") { }
    public HookModAuthoring_Window(string sourceRoot)
    {
        InitializeComponent();
        DataContext = new HookModAuthoring_DataModel(sourceRoot);
        Closed += (_, _) =>
        {
            if (DataContext is HookModAuthoring_DataModel model)
            {
                model.AdmitElementalCommand.Cancel(); model.BuildLanguageCommand.Cancel();
                model.PrepareAbilitiesCommand.Cancel();
                model.InspectProductionCommand.Cancel(); model.InspectArcanaCommand.Cancel(); model.PreviewWorkshopPlanCommand.Cancel();
            }
        };
    }

    private async void BrowseSource(object? sender, RoutedEventArgs e) => await BrowseFolder(false);
    internal void SelectRewards() => this.FindControl<TabControl>("Sections")!.SelectedIndex = 3;
    private async void BrowseWorkshop(object? sender, RoutedEventArgs e) => await BrowseFolder(true);
    private async System.Threading.Tasks.Task BrowseFolder(bool workshop)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
            if (folders.Count == 0) return;
            var model = (HookModAuthoring_DataModel)DataContext!;
            if (workshop) model.WorkshopDirectory = folders[0].TryGetLocalPath() ?? "";
            else model.SourceRoot = folders[0].TryGetLocalPath() ?? "";
        }
        catch (Exception ex) { Report(ex); }
    }
    private async void BrowseManifest(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false });
            if (files.Count > 0) ((HookModAuthoring_DataModel)DataContext!).ManifestPath = files[0].TryGetLocalPath() ?? "";
        }
        catch (Exception ex) { Report(ex); }
    }
    private async void BrowseJson(object? sender, RoutedEventArgs e)
    {
        try
        {
            string? target = (sender as Button)?.Tag as string;
            if (target is not ("RegistryJson" or "PresetJson" or "ElementalJson" or "ElementalInputsJson" or "F7Json" or "ScanIni" or "LanguageRecipeJson" or "WorkshopPlanJson" or "ArcanaAssetsJson")) return;
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false });
            if (files.Count == 0 || files[0].TryGetLocalPath() is not string path) return;
            string text = new UTF8Encoding(false, true).GetString(ModAuthoringFiles.Read(path, 1 << 20));
            typeof(HookModAuthoring_DataModel).GetProperty(target)!.SetValue(DataContext, text);
        }
        catch (Exception ex) { Report(ex); }
    }
    private void Report(Exception ex)
    {
        ((HookModAuthoring_DataModel)DataContext!).Status = string.Format(Strings.U_HookMods_Failed, ex.Message);
        DebugLog.Warn("HookModAuthoring", ex.Message);
    }
    private void CloseWindow(object? sender, RoutedEventArgs e) => Close();
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using FFXProjectEditor.Modules.Common;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Services.Extras;
using FFXProjectEditor.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.Extras;

public partial class VbfExtract_Control : UserControl, IRestorableModule
{
    VbfExtract_DataModel dataModel;
    Window? ownerWindow;
    bool closeApproved;
    bool closePromptPending;
    static List<FilePickerFileType> ArchiveTypes => new() { new(Strings.VbfArchiveLabel) { Patterns = new[] { "*.vbf" } } };
    VbfBrowserRow[] Selection => ArchiveGrid.SelectedItems.Cast<VbfBrowserRow>().ToArray();

    public VbfExtract_Control()
    {
        InitializeComponent();
        DataContext = dataModel = new VbfExtract_DataModel();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AttachedToVisualTree += (_, _) =>
        {
            ownerWindow = TopLevel.GetTopLevel(this) as Window;
            if (ownerWindow != null) ownerWindow.Closing += OnWindowClosing;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            dataModel.Cancel();
            if (ownerWindow != null) ownerWindow.Closing -= OnWindowClosing;
            ownerWindow = null;
        };
    }

    async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (closeApproved || (!dataModel.IsBusy && !dataModel.HasUnsavedChanges)) return;
        e.Cancel = true;
        if (dataModel.IsBusy) { dataModel.Cancel(); return; }
        if (closePromptPending) return;
        closePromptPending = true;
        try
        {
            if (await CanDiscardAsync() && sender is Window window)
            { closeApproved = true; window.Close(); }
        }
        finally { closePromptPending = false; }
    }

    public Dictionary<string, object?> CaptureState() => new() { ["vbfSession"] = dataModel };

    public void RestoreState(Dictionary<string, object?>? state)
    {
        if (state?.GetValueOrDefault("vbfSession") is VbfExtract_DataModel saved)
        {
            dataModel.Cancel();
            DataContext = dataModel = saved;
        }
    }

    async Task<bool> CanDiscardAsync() => !dataModel.IsBusy && (!dataModel.HasUnsavedChanges ||
        await ConfirmAsync(Strings.VbfDiscard, Strings.VbfDiscardButton));

    async Task<bool> ConfirmAsync(string message, string action)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return false;
        var accept = new Button { Content = action, MinWidth = 110, Classes = { "primaryAction" } };
        var cancel = new Button { Content = Strings.VbfCancel, MinWidth = 110, IsCancel = true };
        var dialog = new Window
        {
            Title = Strings.VbfTitle, Width = 620, MinHeight = 240, MaxHeight = 620,
            SizeToContent = SizeToContent.Height, CanResize = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(22), Spacing = 18,
                Children =
                {
                    new TextBlock { Text = action, FontSize = 18, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                    new ScrollViewer
                    {
                        MaxHeight = 390, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                        Content = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }
                    },
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 10, Children = { cancel, accept } }
                }
            }
        };
        accept.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        dialog.Opened += (_, _) => cancel.Focus();
        return await dialog.ShowDialog<bool>(owner);
    }

    Task<bool> ConfirmImportAsync(VbfImportPreview preview)
    {
        string list = string.Join(Environment.NewLine, preview.Paths.Take(5));
        if (preview.Paths.Count > 5) list += Environment.NewLine + string.Format(Strings.VbfMoreFiles, preview.Paths.Count - 5);
        string message = string.Format(Strings.VbfImportConfirm, preview.Added, preview.Replaced, preview.Folder)
            + Environment.NewLine + Environment.NewLine + list;
        return ConfirmAsync(message, Strings.VbfConfirmImport);
    }

    async void Button_New(object? sender, RoutedEventArgs e)
    {
        if (await CanDiscardAsync()) dataModel.New();
    }

    async void Button_Open(object? sender, RoutedEventArgs e)
    {
        if (!await CanDiscardAsync()) return;
        var files = await AvaloniaDialog_Util.OpenFileDialog(this, Strings.VbfOpen, false, "FFX_Data.vbf", ArchiveTypes);
        if (files.Count > 0) await dataModel.OpenAsync(files[0]);
    }

    async Task OpenPreset(string basename)
    {
        if (!await CanDiscardAsync()) return;
        string root = VbfExtract_Service.DefaultSteamDataRoot;
        if (string.IsNullOrEmpty(root))
        { await AvaloniaDialog_Util.ShowMessageAsync(this, Strings.VbfTitle, Strings.VbfNoGamePath); return; }
        await dataModel.OpenAsync(Path.Combine(root, basename + ".vbf"));
    }

    async void Button_DetectFfx(object? sender, RoutedEventArgs e) => await OpenPreset("FFX_Data");
    async void Button_DetectFfx2(object? sender, RoutedEventArgs e) => await OpenPreset("FFX2_Data");
    async void Button_DetectMetaMenu(object? sender, RoutedEventArgs e) => await OpenPreset("metamenu");
    void Button_Cancel(object? sender, RoutedEventArgs e) => dataModel.Cancel();
    async void Button_Undo(object? sender, RoutedEventArgs e)
    {
        if (dataModel.CanUndo && await ConfirmAsync(Strings.VbfUndoConfirm, Strings.VbfUndo)) dataModel.Undo();
    }
    void Button_Up(object? sender, RoutedEventArgs e) => dataModel.Up();
    async void Button_Verify(object? sender, RoutedEventArgs e) => await dataModel.VerifyAsync();

    async void Button_CreateFolder(object? sender, RoutedEventArgs e)
    {
        if (!await CanDiscardAsync()) return;
        var folders = await AvaloniaDialog_Util.OpenFolderDialog(this, Strings.VbfCreateFolder);
        if (folders.Count == 0) return;
        await dataModel.AddAsync(folders, folderContents: true, confirm: ConfirmImportAsync, startNewArchive: true);
    }

    async void Button_AddFiles(object? sender, RoutedEventArgs e)
    {
        var files = await AvaloniaDialog_Util.OpenFileDialog(this, Strings.VbfAddFiles, true);
        if (files.Count > 0) await dataModel.AddAsync(files, confirm: ConfirmImportAsync);
    }

    async void Button_AddFolder(object? sender, RoutedEventArgs e)
    {
        var folders = await AvaloniaDialog_Util.OpenFolderDialog(this, Strings.VbfAddFolder);
        if (folders.Count > 0) await dataModel.AddAsync(folders, confirm: ConfirmImportAsync);
    }

    async void Button_Replace(object? sender, RoutedEventArgs e)
    {
        var rows = Selection;
        if (rows.Length != 1 || rows[0].IsFolder)
        { await AvaloniaDialog_Util.ShowMessageAsync(this, Strings.VbfTitle, Strings.VbfSelectFile); return; }
        var files = await AvaloniaDialog_Util.OpenFileDialog(this, Strings.VbfReplace, false);
        if (files.Count > 0) await dataModel.ReplaceAsync(rows[0].Path, files[0], ConfirmImportAsync);
    }

    async void Button_Delete(object? sender, RoutedEventArgs e)
    {
        var rows = Selection;
        if (rows.Length == 0) return;
        int count = dataModel.Workspace.Select(rows.Select(x => x.Path)).Count;
        if (await ConfirmAsync(string.Format(Strings.VbfDeleteConfirm, count), Strings.VbfDelete))
            dataModel.Delete(rows.Select(x => x.Path));
    }

    async void Button_Rename(object? sender, RoutedEventArgs e)
    {
        var rows = Selection;
        if (rows.Length != 1 || TopLevel.GetTopLevel(this) is not Window owner) return;
        var input = new TextBox { Text = rows[0].Path.TrimEnd('/'), MinWidth = 430 };
        var apply = new Button { Content = Strings.VbfRename };
        var cancel = new Button { Content = Strings.VbfCancel };
        var dialog = new Window
        {
            Title = Strings.VbfRename, Width = 560, Height = 210,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20), Spacing = 12,
                Children =
                {
                    new TextBlock { Text = Strings.VbfRenameHint, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, input,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { apply, cancel } }
                }
            }
        };
        apply.Click += (_, _) => dialog.Close(input.Text);
        cancel.Click += (_, _) => dialog.Close((string?)null);
        string? result = await dialog.ShowDialog<string?>(owner);
        if (!string.IsNullOrWhiteSpace(result)) dataModel.Rename(rows[0].Path, result);
    }

    async void Button_Save(object? sender, RoutedEventArgs e)
    {
        string name = Path.GetFileNameWithoutExtension(dataModel.SourcePath);
        string path = await AvaloniaDialog_Util.SaveFileDialog(this, Strings.VbfSaveAs,
            string.IsNullOrEmpty(name) ? "archive.vbf" : name + "_repacked.vbf", "vbf", ArchiveTypes);
        if (!string.IsNullOrEmpty(path) && await ConfirmAsync(
            string.Format(Strings.VbfSaveConfirm, dataModel.Workspace.Files.Count, path), Strings.VbfConfirmSave))
            await dataModel.SaveAsync(path);
    }

    async void Button_ExtractAll(object? sender, RoutedEventArgs e) => await Extract(null);
    async void Button_ExtractSelected(object? sender, RoutedEventArgs e)
    {
        var rows = Selection;
        if (rows.Length == 0)
        { await AvaloniaDialog_Util.ShowMessageAsync(this, Strings.VbfTitle, Strings.VbfSelectItems); return; }
        await Extract(rows.Select(x => x.Path).ToArray());
    }

    async Task Extract(string[]? paths)
    {
        var folders = await AvaloniaDialog_Util.OpenFolderDialog(this, Strings.VbfExtractDestination);
        if (folders.Count == 0) return;
        int count = paths == null ? dataModel.Workspace.Files.Count : dataModel.Workspace.Select(paths).Count;
        string policy = dataModel.OverwriteExtract ? Strings.VbfExtractWithBackups : Strings.VbfExtractNoOverwrite;
        if (await ConfirmAsync(string.Format(Strings.VbfExtractConfirm, count, folders[0], policy), Strings.VbfConfirmExtract))
            await dataModel.ExtractAsync(folders[0], paths);
    }

    void Grid_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ArchiveGrid.SelectedItem is VbfBrowserRow { IsFolder: true } row) dataModel.Navigate(row.Path);
    }

    void Grid_KeyDown(object? sender, KeyEventArgs e)
    {
        if (dataModel.IsBusy) return;
        if (e.Key == Key.Enter && ArchiveGrid.SelectedItem is VbfBrowserRow { IsFolder: true } row)
        { dataModel.Navigate(row.Path); e.Handled = true; }
        else if (e.Key == Key.Back) { dataModel.Up(); e.Handled = true; }
        else if (e.Key == Key.Delete) { Button_Delete(sender, e); e.Handled = true; }
        else if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { ArchiveGrid.SelectAll(); e.Handled = true; }
        else if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { Button_Undo(sender, e); e.Handled = true; }
    }

    void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = !dataModel.IsBusy && e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    async void OnDrop(object? sender, DragEventArgs e)
    {
        // Consume the drop before the shell's project-folder handler sees it.
        e.Handled = true;
        if (dataModel.IsBusy) return;
        var paths = e.Data.GetFiles()?.Select(x => x.TryGetLocalPath()).Where(x => x != null).Cast<string>().ToArray() ?? Array.Empty<string>();
        if (paths.Length == 0) return;
        if (paths.Length == 1 && Path.GetExtension(paths[0]).Equals(".vbf", StringComparison.OrdinalIgnoreCase)
            && dataModel.Workspace.Files.Count == 0 && string.IsNullOrEmpty(dataModel.SourcePath))
            await dataModel.OpenAsync(paths[0]);
        else await dataModel.AddAsync(paths, confirm: ConfirmImportAsync);
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Archives;
using FFXProjectEditor.Resources;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.Extras;

internal sealed record VbfBrowserRow(string Path, bool IsFolder, long Size, long PackedSize, string State, bool IsPending = false)
{
    public string Name => (IsFolder ? "▸ " : "") + System.IO.Path.GetFileName(Path.TrimEnd('/'));
    public string SizeText => VbfExtract_DataModel.FormatBytes(Size);
    public string PackedText => IsFolder || IsPending ? "—" : VbfExtract_DataModel.FormatBytes(PackedSize);
}

internal sealed record VbfImportPreview(IReadOnlyList<string> Paths, int Added, int Replaced, string Folder);

internal partial class VbfExtract_DataModel : ObservableObject
{
    VbfWorkspace workspace = new();
    CancellationTokenSource? cancellation;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool hasUnsavedChanges;
    [ObservableProperty] private string sourcePath = string.Empty;
    [ObservableProperty] private string currentFolder = string.Empty;
    [ObservableProperty] private string filterText = string.Empty;
    [ObservableProperty] private string statusText = Strings.VbfReady;
    [ObservableProperty] private string progressText = string.Empty;
    [ObservableProperty] private double progressValue;
    [ObservableProperty] private string logText = string.Empty;
    [ObservableProperty] private bool compressNewFiles = true;
    [ObservableProperty] private bool overwriteExtract;
    [ObservableProperty] private IReadOnlyList<VbfBrowserRow> rows = Array.Empty<VbfBrowserRow>();
    public VbfWorkspace Workspace => workspace;
    public bool CanEdit => !IsBusy;
    public bool CanUndo => !IsBusy && workspace.HasChanges;
    public bool CanVerify => !IsBusy && workspace.Original != null;
    public string FolderDisplay => "/" + CurrentFolder;
    public string ArchiveName => string.IsNullOrEmpty(SourcePath) ? Strings.VbfNewArchive : Path.GetFileName(SourcePath);
    public string Summary => string.Format(Strings.VbfSummary, workspace.Files.Count,
        FormatBytes(workspace.Files.Sum(x => x.Size)), HasUnsavedChanges ? Strings.VbfPending : Strings.VbfUnchanged);

    partial void OnIsBusyChanged(bool value) => NotifyState();
    partial void OnHasUnsavedChangesChanged(bool value) => NotifyState();
    partial void OnFilterTextChanged(string value) => RefreshRows();
    partial void OnCurrentFolderChanged(string value) { OnPropertyChanged(nameof(FolderDisplay)); RefreshRows(); }
    partial void OnSourcePathChanged(string value) => OnPropertyChanged(nameof(ArchiveName));

    public void New()
    {
        if (IsBusy) return;
        workspace = new VbfWorkspace(); SourcePath = ""; CurrentFolder = ""; FilterText = "";
        HasUnsavedChanges = false; StatusText = Strings.VbfReady; RefreshRows();
    }

    public Task OpenAsync(string path) => RunAsync(async (progress, token) =>
    {
        var archive = await Task.Run(() => VbfArchive.Open(path, token), token);
        workspace = new VbfWorkspace(archive); SourcePath = archive.FilePath;
        CurrentFolder = ""; FilterText = ""; HasUnsavedChanges = false;
        StatusText = string.Format(Strings.VbfOpened, archive.Entries.Count); RefreshRows();
    });

    public Task AddAsync(IEnumerable<string> paths, bool folderContents = false,
        Func<VbfImportPreview, Task<bool>>? confirm = null, bool startNewArchive = false) => RunAsync(async (progress, token) =>
    {
        string[] inputs = paths.ToArray(); string folder = startNewArchive ? "" : CurrentFolder;
        var imported = await Task.Run(() => VbfWorkspace.Import(inputs, folder, folderContents, token), token);
        if (imported.Count == 0) { StatusText = Strings.VbfNothingToImport; return; }
        var existing = startNewArchive ? new HashSet<string>() : workspace.Files.Select(x => x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int replaced = imported.Count(x => existing.Contains(x.Path));
        var preview = new VbfImportPreview(imported.Select(x => x.Path).ToArray(), imported.Count - replaced, replaced, "/" + folder);
        if (confirm != null && !await confirm(preview)) { StatusText = Strings.VbfDeclined; return; }
        token.ThrowIfCancellationRequested();
        if (startNewArchive)
        {
            workspace = new VbfWorkspace(); SourcePath = ""; CurrentFolder = ""; FilterText = "";
            HasUnsavedChanges = false;
        }
        workspace.Add(imported); HasUnsavedChanges |= imported.Count > 0;
        StatusText = string.Format(Strings.VbfAdded, imported.Count); RefreshRows();
    });

    public Task ReplaceAsync(string path, string source, Func<VbfImportPreview, Task<bool>>? confirm = null) => RunAsync(async (progress, token) =>
    {
        var imported = await Task.Run(() => VbfWorkspace.Import(new[] { source }, token: token), token);
        string target = VbfArchive.NormalizePath(path);
        var preview = new VbfImportPreview(new[] { target }, 0, 1, "/" + CurrentFolder);
        if (confirm != null && !await confirm(preview)) { StatusText = Strings.VbfDeclined; return; }
        token.ThrowIfCancellationRequested();
        workspace.Add(new[] { imported.Single() with { Path = target } });
        HasUnsavedChanges = true; StatusText = string.Format(Strings.VbfAdded, 1); RefreshRows();
    });

    public void Delete(IEnumerable<string> paths)
    {
        if (IsBusy) return;
        workspace.Remove(paths); HasUnsavedChanges = true; RefreshRows(); StatusText = Strings.VbfDeleted;
    }

    public void Rename(string oldPath, string newPath)
    {
        if (IsBusy) return;
        try { workspace.Rename(oldPath, newPath); HasUnsavedChanges = true; RefreshRows(); StatusText = Strings.VbfRenamed; }
        catch (Exception ex) { ReportError(ex); }
    }

    public void Undo()
    {
        if (!CanUndo) return;
        workspace.Undo(); HasUnsavedChanges = true; RefreshRows(); StatusText = Strings.VbfUndone;
    }

    public void Navigate(string folder)
    {
        if (IsBusy) return;
        FilterText = ""; CurrentFolder = folder;
    }

    public void Up()
    {
        string folder = CurrentFolder.TrimEnd('/');
        int slash = folder.LastIndexOf('/'); Navigate(slash < 0 ? "" : folder[..(slash + 1)]);
    }

    public Task SaveAsync(string path) => RunAsync(async (progress, token) =>
    {
        var files = workspace.Files.ToArray(); bool compress = CompressNewFiles;
        var original = workspace.Original;
        var result = await Task.Run(() => VbfArchiveWriter.Save(files, path, compress, progress, token, original), token);
        HasUnsavedChanges = false; StatusText = string.Format(Strings.VbfSaved, result.Path);
        AppendLog("SHA-256: " + result.Sha256);
        if (result.BackupPath != null) AppendLog(string.Format(Strings.VbfBackup, result.BackupPath));
        foreach (var source in result.SourceHashes) AppendLog("Source SHA-256: " + source.Value + " · " + source.Key);
        NotifyState();
    });

    public Task ExtractAsync(string root, IEnumerable<string>? selected = null) => RunAsync(async (progress, token) =>
    {
        var files = selected == null ? workspace.Files.ToArray() : workspace.Select(selected).ToArray();
        bool overwrite = OverwriteExtract;
        int count = await Task.Run(() => VbfArchiveOperations.Extract(files, root, overwrite, progress, token), token);
        StatusText = string.Format(Strings.VbfExtracted, count, root);
    });

    public Task VerifyAsync() => RunAsync(async (progress, token) =>
    {
        if (workspace.Original == null) return;
        await Task.Run(() => VbfArchiveOperations.Verify(workspace.Original, progress, token), token);
        StatusText = Strings.VbfVerified;
    });

    public void Cancel() => cancellation?.Cancel();

    async Task RunAsync(Func<IProgress<VbfProgress>, CancellationToken, Task> action)
    {
        if (IsBusy) return;
        using var run = new CancellationTokenSource(); cancellation = run;
        IsBusy = true; ProgressValue = 0; ProgressText = Strings.VbfWorking; StatusText = Strings.VbfWorking;
        var uiProgress = new Progress<VbfProgress>(p =>
        {
            if (!ReferenceEquals(cancellation, run)) return;
            ProgressValue = p.Total == 0 ? 0 : 100d * p.Completed / p.Total;
            string phase = p.Phase switch
            {
                "source" or "hash" => Strings.VbfHashing,
                "verify" => Strings.VbfVerifying,
                "extract" => Strings.VbfExtracting,
                _ => Strings.VbfWriting
            };
            ProgressText = phase + " · " + p.Path;
        });
        try { await action(new ThrottledProgress(uiProgress), run.Token); AppendLog(StatusText); ProgressValue = 100; }
        catch (OperationCanceledException) { StatusText = Strings.VbfCancelled; AppendLog(StatusText); }
        catch (Exception ex) { ReportError(ex); }
        finally { cancellation = null; IsBusy = false; ProgressText = ""; }
    }

    void ReportError(Exception ex)
    {
        StatusText = string.Format(Strings.VbfFailed, ex.Message); AppendLog(StatusText);
        FFXProjectEditor.Diagnostics.DebugLog.Error("VBF", "Archive operation failed.", ex);
    }

    void AppendLog(string line)
    {
        LogText = (LogText + Environment.NewLine + line).TrimStart();
        if (LogText.Length > 24000) LogText = LogText[^24000..];
    }

    void RefreshRows()
    {
        if (workspace == null) return;
        var result = new List<VbfBrowserRow>();
        var folders = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        bool search = !string.IsNullOrWhiteSpace(FilterText);
        foreach (var file in workspace.Files)
        {
            if (!file.Path.StartsWith(CurrentFolder, StringComparison.OrdinalIgnoreCase)) continue;
            if (search && !file.Path.Contains(FilterText, StringComparison.OrdinalIgnoreCase)) continue;
            string remainder = file.Path[CurrentFolder.Length..];
            int slash = remainder.IndexOf('/');
            if (!search && slash >= 0)
            {
                string folder = CurrentFolder + remainder[..(slash + 1)];
                folders[folder] = folders.GetValueOrDefault(folder) + file.Size;
            }
            else
            {
                string state = file.IsPending ? Strings.VbfPendingEntry : file.Entry?.Path != file.Path ? Strings.VbfRenamedState : Strings.VbfUnchanged;
                result.Add(new(file.Path, false, file.Size, file.PackedSize, state, file.IsPending));
            }
        }
        result.AddRange(folders.Select(x => new VbfBrowserRow(x.Key, true, x.Value, 0, Strings.VbfFolder)));
        Rows = result.OrderByDescending(x => x.IsFolder).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        NotifyState();
    }

    void NotifyState()
    {
        OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanVerify)); OnPropertyChanged(nameof(Summary));
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
        double value = bytes; int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }

    sealed class ThrottledProgress(IProgress<VbfProgress> target) : IProgress<VbfProgress>
    {
        readonly Stopwatch clock = Stopwatch.StartNew();
        string lastPhase = "";
        public void Report(VbfProgress value)
        {
            if (clock.ElapsedMilliseconds < 120 && value.Phase == lastPhase && value.Completed != value.Total) return;
            lastPhase = value.Phase; clock.Restart(); target.Report(value);
        }
    }
}

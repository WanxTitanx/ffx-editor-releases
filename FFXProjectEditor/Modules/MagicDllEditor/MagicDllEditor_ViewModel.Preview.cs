// WHY: The Magic preview lifecycle must own exactly its session. The managed NoClip contract
// (magicPreview=<session>) serves the DLL's own .data resource through /viewer-data, so a preview
// never requires a vanilla 11/<id>.bin nor a catalogue entry — custom and uncatalogued effects
// render identically. The existing control detach/document-loaded paths call ClosePreview.
// MAINT: Keep generated command names/CanExecute and PreviewUrl bindings stable. These per-instance
// dependency resolvers support real headless command tests without replacing process-global state.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using FFXProjectEditor.Diagnostics;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MagicDllEditor;

internal partial class MagicDllEditor_ViewModel
{
    private MagicPreviewSession? _previewSession;
    private string? _previewSessionRoot;

    /// <summary>Root under which the preview session publishes its manifest and binaries.</summary>
    internal Func<string?> PreviewSessionRootResolver { get; set; } =
        () => ViewerHubService.ViewerDataRoot;

    /// <summary>(magicId, sessionId) → (preview url, actual server) for the managed contract.</summary>
    internal Func<int, string, (string? Url, StudioWebServer? Server)> PreviewHubResolver { get; set; } =
        ResolvePreviewHub;

    private static (string? Url, StudioWebServer? Server) ResolvePreviewHub(int id, string sessionId)
    {
        // forExternalBrowser: the control opens PreviewUrl in the system browser outside Windows.
        string? url = ViewerHubService.BuildUrl(
            "magic-studio",
            $"magic={id}&magicPreview={sessionId}&lang={CultureInfo.CurrentUICulture.TwoLetterISOLanguageName}",
            forExternalBrowser: true);
        return (url, ViewerHubService.Server);
    }

    /// <summary>Open or refresh the current working resource in the same NoClip session.</summary>
    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void PreviewMagic()
    {
        if (!HasDocument || ApplyPendingFieldEdits() < 0) return;
        byte[]? working = _document.WorkingBytes;
        if (working == null)
        {
            AppendLog("No working bytes to preview.", MagicLogLevel.Warn);
            return;
        }

        bool opening = false;
        try
        {
            string? sessionRoot = PreviewSessionRootResolver();
            if (string.IsNullOrWhiteSpace(sessionRoot))
            {
                AppendLog(Strings.U_Md_MemoryPreviewUnavailable, MagicLogLevel.Warn);
                DebugLog.Warn("Magic.Preview", "The viewer data root is unavailable.");
                return;
            }

            opening = _previewSession == null ||
                !string.Equals(_previewSessionRoot, sessionRoot, StringComparison.Ordinal);
            if (opening)
            {
                _previewSession?.Dispose();
                _previewSession = new MagicPreviewSession(sessionRoot);
                _previewSessionRoot = sessionRoot;
            }

            var project = Services.Project_Service.Instance;
            var textureRoots = new List<string>();
            if (project.Path_Ps3DataRoot is { } ps3Root)
                textureRoots.Add(Path.Combine(ps3Root, "magic"));
            if (project.Path_ModsPs3MagicRoot is { } modsRoot)
                textureRoots.Add(modsRoot);
            _previewSession!.Publish(
                _document.ParsedFile!, working, DocumentTitle, textureRoots.ToArray());
            UpdateShaDisplay();

            if (opening)
            {
                (string? url, StudioWebServer? server) =
                    PreviewHubResolver(_document.MagicId, _previewSession.Id);
                if (string.IsNullOrWhiteSpace(url) || server is not { IsRunning: true })
                    throw new InvalidOperationException(ViewerHubService.StatusText);
                PreviewUrl = url;
                PreviewToolTab = 2;
            }
            AppendLog(Strings.U_Md_WorkingPreviewUpdated);
            DebugLog.Info("Magic.Preview", $"Published Magic preview for effect {_document.MagicId}.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
            UnauthorizedAccessException or InvalidOperationException or ArgumentException or
            NotSupportedException)
        {
            if (opening) ClosePreview();
            AppendLog(
                string.Format(Strings.U_Md_WorkingPreviewFailed, error.Message),
                MagicLogLevel.Error);
            DebugLog.Error("Magic.Preview", "Magic preview could not be published.", error);
        }
        finally
        {
            Array.Clear(working, 0, working.Length);
        }
    }

    /// <summary>Closes only this preview owner; the session directory is removed with it.</summary>
    [RelayCommand]
    private void ClosePreview()
    {
        _previewSession?.Dispose();
        _previewSession = null;
        _previewSessionRoot = null;
        PreviewUrl = string.Empty;
    }
}

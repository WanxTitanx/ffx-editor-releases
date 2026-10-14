using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace FFXProjectEditor.Modules.Common.ViewerShell
{
    internal partial class ViewerShell_DataModel : ObservableObject
    {
        /// <summary>Catálogo de cenas FFX (árvore do noclip — ids hex 3 dígitos; battle = "b"+hex).
        /// Extraído do scenes.ts do noclip (FFXLevelSceneDesc.id = hexzero(index, 3)).</summary>
        public static readonly (string Region, (string Id, string Name)[] Scenes)[] FfxSceneCatalog = new[]
        {
            ("Zanarkand", new[] { ("010", "Zanarkand Ruins"), ("014", "Zanarkand - Harbor (night)"), ("018", "Zanarkand - Overpass"), ("00E", "Zanarkand - Harbor"), ("00D", "Zanarkand - Harbor (dream)") }),
            ("Ruins", new[] { ("01E", "Submerged Ruins"), ("020", "Ruins - Underwater Hall"), ("024", "Ruins - Stairs"), ("028", "Ruins - Antechamber"), ("02A", "Ruins - Fayth") }),
            ("Besaid", new[] { ("041", "Besaid - Port"), ("042", "Besaid - Port (with boat)") }),
            ("Batalhas", new[] { ("b00a", "Highroad - Oldroad"), ("b00b", "Mushroom Rock - Plateau"), ("b024", "Bevelle - Main Gate"), ("b025", "Calm Lands"), ("b027", "Remiem Temple"), ("b030", "Zanarkand Dome"), ("b035", "Omega Ruins") }),
        };

        [ObservableProperty] private string title = "Viewer 3D";
        [ObservableProperty] private string statusText = "Iniciando servidor in-process...";
        [ObservableProperty] private string url = "";
        [ObservableProperty] private bool isStarting = true;
        [ObservableProperty] private bool isRunning;
        [ObservableProperty] private bool isError;
        [ObservableProperty] private bool canConfigureNoclip;
        [ObservableProperty] private bool hasWebView2InitializationFailure;
        [ObservableProperty] private bool canShowWebView2Installer;
        [ObservableProperty] private bool hasWebView2InstallerAction;
        [ObservableProperty] private string embeddedViewerFailureTitle = "";
        [ObservableProperty] private string webView2RecoveryMessage = "";

        // Non-Windows fallback: WebView2 embed is Windows-only, so on Linux/macOS the viewer
        // opens in the system browser (the in-process server + data layer work identically).
        // IsEmbedSupported also hides the NativeControlHost itself — on X11 it creates an opaque
        // native child window that renders ABOVE Avalonia content and would cover the fallback card.
        public bool IsEmbedSupported { get; } = OperatingSystem.IsWindows();
        [ObservableProperty] private bool isExternalBrowserFallback;
        [ObservableProperty] private string externalBrowserUrl = "";

        // Tools por modo (F3 — Aurora): painel de contexto colapsável.
        [ObservableProperty] private bool hasTools;
        [ObservableProperty] private bool hasBattles;
        [ObservableProperty] private ObservableCollection<string> battles = new();
        [ObservableProperty] private string? selectedBattle;
        [ObservableProperty] private bool actorsEnabled = true;
        [ObservableProperty] private bool overrideEnabled = true;
        [ObservableProperty] private bool hasScenes;
        [ObservableProperty] private ObservableCollection<string> sceneRegions = new();
        [ObservableProperty] private string? selectedRegion;
        [ObservableProperty] private ObservableCollection<string> scenes = new();
        [ObservableProperty] private string? selectedScene;

        // F3.5 — modo edição (sidecar): deltas por slot aplicados pelo viewer.
        [ObservableProperty] private bool hasEditor;
        [ObservableProperty] private ObservableCollection<string> editSlots = new();
        [ObservableProperty] private int editSlot;
        [ObservableProperty] private string editDx = "0";
        [ObservableProperty] private string editDy = "0";
        [ObservableProperty] private string editDz = "0";
        [ObservableProperty] private string editHeading = "";
        [ObservableProperty] private string editScale = "";
        [ObservableProperty] private bool editGizmo;

        /// <summary>Estado do pill de status da toolbar universal: "starting" | "running" | "error".</summary>
        public void SetState(string state)
        {
            IsStarting = state == "starting";
            IsRunning = state == "running";
            IsError = state == "error";
        }

        /// <summary>
        /// Applies the user-facing WebView2 recovery state without depending on a native WebView2
        /// instance. The caller supplies localized text and the packaged-installer capability.
        /// </summary>
        public void ShowWebView2InitializationFailure(
            string statusText,
            string recoveryMessage,
            bool installerAvailable,
            bool showInstallerAction = true)
        {
            StatusText = statusText;
            EmbeddedViewerFailureTitle = statusText;
            WebView2RecoveryMessage = recoveryMessage;
            CanShowWebView2Installer = installerAvailable;
            HasWebView2InstallerAction = showInstallerAction;
            HasWebView2InitializationFailure = true;
            SetState("error");
        }

        public void ClearWebView2InitializationFailure()
        {
            HasWebView2InitializationFailure = false;
            CanShowWebView2Installer = false;
            HasWebView2InstallerAction = false;
            EmbeddedViewerFailureTitle = "";
            WebView2RecoveryMessage = "";
        }

        partial void OnSelectedRegionChanged(string? value)
        {
            Scenes.Clear();
            foreach (var r in FfxSceneCatalog)
            {
                if (r.Region == value)
                    foreach (var s in r.Scenes)
                        Scenes.Add($"{s.Id} — {s.Name}");
            }
            SelectedScene = Scenes.FirstOrDefault();
        }
    }
}

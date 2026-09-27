using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Utils;

namespace FFXProjectEditor.Modules.Common.ViewerShell
{
    /// <summary>
    /// 🐉 VIEWER SHELL — painel universal de visualização 3D embutido. Recebe o id de um
    /// ViewerDescriptor, pede uma URL ao ViewerHubService e navega somente quando o alvo tem um
    /// backend seguro. Plataformas sem esse backend exibem a indisponibilidade sem iniciar servidor
    /// ou navegador externo.
    /// </summary>
    public partial class ViewerShell_Control : UserControl
    {
        /// <summary>Skin do FFX Mod Studio sobre apps web hospedados (noclip): esconde a UI cosmética
        /// e genérica do noclip — bottom bar (play/pause/fullscreen/share), painel "About" (créditos
        /// do noclip.website), painel "Games" (menu de jogos Wii/GC/outros) e botões de settings/help —
        /// mantendo o painel funcional (dropdown de monstros/magias) e o canvas 3D. Os painéis do
        /// noclip gravam o título em data-title (Panel.setTitle) — seletor robusto por atributo.
        /// Aplicada via AddScriptToExecuteOnDocumentCreatedAsync (roda antes do script da página).
        /// partilhada com a AuroraViewerWindow (pop-up RealGame) p/ não duplicar a receita (internal).</summary>
        internal const string NoclipSkinScript = @"(function () {
    'use strict';
    // data-title é gravado pelo noclip (Panel.setTitle -> toplevel.dataset.title).
    var css = '#BottomBar,#About,#studioSidePanelHelpBtn,.SettingsButton,div[data-title=""About""],div[data-title=""Games""]{display:none!important}';
    var style = document.createElement('style');
    style.id = 'ffx-studio-skin';
    style.textContent = css;
    (document.head || document.documentElement).appendChild(style);
    var apply = function () {
        var els = document.querySelectorAll('#BottomBar,#About,#studioSidePanelHelpBtn,.SettingsButton,div[data-title=""About""],div[data-title=""Games""]');
        for (var i = 0; i < els.length; i++) els[i].style.display = 'none';
    };
    apply();
    new MutationObserver(apply).observe(document.documentElement, { childList: true, subtree: true });
})();";

        private readonly ViewerShell_DataModel _dataModel = new();
        private readonly string _descId;
        private readonly string? _extraQuery;
        private readonly bool _requiresNoclip;
        private string? _webView2InstallerPath;
        private bool _navigated;
        private readonly FFXProjectEditor.Modules.AuroraChamber.AuroraBattleOverlayOwner
            _battleOverlayOwner = new(ViewerHubService.ViewerDataRoot);

        public ViewerShell_Control(string descId, string? extraQuery = null)
        {
            InitializeComponent();
            _descId = descId;
            _extraQuery = extraQuery;
            DataContext = _dataModel;
            ViewerHost.InitializationFailed += OnWebView2InitializationFailed;

            if (ViewerHubService.Find(descId) is { } desc)
            {
                _dataModel.Title = desc.Title;
                _requiresNoclip = desc.RequiresNoclip;
                _dataModel.CanConfigureNoclip = desc.RequiresNoclip;
                SetupTools(desc);
            }
            else
            {
                _dataModel.StatusText = $"viewer desconhecido: {descId}";
            }

            Loaded += OnLoaded;
        }

        /// <summary>F3 — tools por modo (desc.Tools): battle/actors/override/cenas FFX (Aurora).</summary>
        private void SetupTools(ViewerDescriptor desc)
        {
            _dataModel.HasTools = desc.Tools is { Count: > 0 };
            if (!_dataModel.HasTools)
                return;

            foreach (string tool in desc.Tools!)
            {
                if (tool == "battle")
                {
                    _dataModel.HasBattles = true;
                    _ = LoadBattlesAsync();
                }
                else if (tool == "scenes")
                {
                    _dataModel.HasScenes = true;
                    foreach (var r in ViewerShell_DataModel.FfxSceneCatalog)
                        _dataModel.SceneRegions.Add(r.Region);
                    _dataModel.SelectedRegion = _dataModel.SceneRegions.FirstOrDefault();
                }
                else if (tool == "editor")
                {
                    _dataModel.HasEditor = true;
                    for (int s = 0; s < 8; s++)
                        _dataModel.EditSlots.Add(s.ToString());
                    _dataModel.EditSlot = 0;
                }
            }
        }

        private async Task LoadBattlesAsync()
        {
            var battles = await Task.Run(() =>
                FFXProjectEditor.Modules.AuroraChamber.Aurora3DLauncher.ListResolvedBattles());
            foreach (string b in battles)
                _dataModel.Battles.Add(b);
            if (battles.Count > 0)
                _dataModel.SelectedBattle = battles[0];
            else
                _dataModel.StatusText = Strings.F2_no_battle_resolved_bridge_sha_load_a_pro_48a1b7ea;
        }

        /// <summary>F3 — abre o battle selecionado no Aurora (override 0e/ opcional + actors).</summary>
        private void Button_OpenBattle(object? sender, RoutedEventArgs e)
        {
            string? battleId = _dataModel.SelectedBattle;
            if (string.IsNullOrWhiteSpace(battleId))
            {
                if (OperatingSystem.IsLinux()) _battleOverlayOwner.Clear();
                _dataModel.StatusText = Strings.F2_select_a_battle_from_the_list_6c3d4cc1;
                _dataModel.SetState("error");
                return;
            }

            if (!FFXProjectEditor.Modules.AuroraChamber.Aurora3DLauncher.TryResolveEncounter(battleId, out int encId))
            {
                if (OperatingSystem.IsLinux()) _battleOverlayOwner.Clear();
                _dataModel.StatusText = string.Format(Strings.U_Vh_NoEncounterMapping, battleId);
                _dataModel.SetState("error");
                return;
            }

            int? mapIndex = FFXProjectEditor.Modules.AuroraChamber.Aurora3DLauncher.ResolveBattleMapIndex(battleId) ?? 0;
            string actors = _dataModel.ActorsEnabled ? "1" : "0";
            string edit = _dataModel.EditGizmo ? "1" : "0";

            string query = $"battle={encId}&map={mapIndex}&actors={actors}&edit={edit}";
            string? url = ViewerHubService.BuildUrl("aurora", query, "#ffx/battle-preview", forExternalBrowser: true);
            if (url == null)
            {
                if (OperatingSystem.IsLinux()) _battleOverlayOwner.Clear();
                ShowViewerStartupFailure();
                return;
            }
            // Exact preview overlays can only be registered after the loopback server exists.
            // BuildUrl establishes that server first; native memory and Windows staging leave selected data/ read-only.
            if (ViewerHubService.Server is not { IsRunning: true } server)
            {
                if (OperatingSystem.IsLinux()) _battleOverlayOwner.Clear();
                _dataModel.StatusText = ViewerHubService.StatusText;
                _dataModel.SetState("error");
                return;
            }
            string overrideMsg = _battleOverlayOwner.Update(
                _dataModel.OverrideEnabled,
                server,
                () => FFXProjectEditor.Modules.AuroraChamber.Aurora3DLauncher.CreateBattleOverride(
                    battleId,
                    encId,
                    server),
                Strings.U_Vh_OverrideDisabled);
            _dataModel.Url = url;
            _dataModel.StatusText = $"{overrideMsg} · mapa 1a/{mapIndex:X3} · actors={actors} · gizmo={(edit == "1" ? "on" : "off")}";
            _dataModel.SetState("running");
            NavigateOrOpenExternal(url, "#ffx/battle-preview");
        }

        /// <summary>F3.5 — stages the edit sidecar outside selected data and exposes only that
        /// encounter's exact JSON path to NoClip. Unedited sidecars still fall back to selected data.</summary>
        private void Button_ApplyEdits(object? sender, RoutedEventArgs e)
        {
            string? battleId = _dataModel.SelectedBattle;
            if (string.IsNullOrWhiteSpace(battleId))
            {
                _dataModel.StatusText = Strings.U_Vh_SelectBattleFirst;
                _dataModel.SetState("error");
                return;
            }
            if (!FFXProjectEditor.Modules.AuroraChamber.Aurora3DLauncher.TryResolveEncounter(battleId, out int encId))
            {
                _dataModel.StatusText = string.Format(Strings.U_Vh_NoEncounterMapping, battleId);
                _dataModel.SetState("error");
                return;
            }
            try
            {
                double? dx = null, dy = null, dz = null, heading = null, scale = null;
                if (double.TryParse(_dataModel.EditDx, NumberStyles.Float, CultureInfo.InvariantCulture, out double dxv) &&
                    double.TryParse(_dataModel.EditDy, NumberStyles.Float, CultureInfo.InvariantCulture, out double dyv) &&
                    double.TryParse(_dataModel.EditDz, NumberStyles.Float, CultureInfo.InvariantCulture, out double dzv))
                {
                    dx = dxv; dy = dyv; dz = dzv;
                }
                if (double.TryParse(_dataModel.EditHeading, NumberStyles.Float, CultureInfo.InvariantCulture, out double hv))
                    heading = hv;
                if (double.TryParse(_dataModel.EditScale, NumberStyles.Float, CultureInfo.InvariantCulture, out double sv))
                    scale = sv;

                if (ViewerHubService.Server is not { IsRunning: true } server)
                {
                    _ = ViewerHubService.BuildUrl("aurora", forExternalBrowser: true);
                    server = ViewerHubService.Server;
                }
                if (server is not { IsRunning: true })
                    throw new IOException(ViewerHubService.StatusText);

                string stagedEdit = NoclipOverlayStore.WriteEdit(
                    ViewerHubService.ViewerDataRoot,
                    encId,
                    _dataModel.EditSlot,
                    dx,
                    dy,
                    dz,
                    heading,
                    scale);
                if (!server.TryMapExactFile(
                    $"/data/FinalFantasyX/edits/{encId}.json",
                    stagedEdit))
                    throw new IOException("The local NoClip edit overlay could not be registered.");

                _dataModel.StatusText = string.Format(Strings.U_Vh_SidecarUpdated, encId, _dataModel.EditSlot);
                _dataModel.SetState("running");
            }
            catch (Exception ex)
            {
                _dataModel.StatusText = $"falha ao gravar sidecar: {ex.Message}";
                _dataModel.SetState("error");
            }
        }

        /// <summary>F3 — abre a cena FFX selecionada (árvore de cenas no shell).</summary>
        private void Button_OpenScene(object? sender, RoutedEventArgs e)
        {
            _battleOverlayOwner.Clear();
            string? sel = _dataModel.SelectedScene;
            if (string.IsNullOrWhiteSpace(sel))
            {
                _dataModel.StatusText = "Selecione uma cena FFX.";
                _dataModel.SetState("error");
                return;
            }

            int sep = sel.IndexOf(' ');
            string id = sep > 0 ? sel.Substring(0, sep) : sel;
            string route = $"#ffx/{id}";
            string? url = ViewerHubService.BuildUrl("aurora", routeOverride: route, forExternalBrowser: true);
            if (url == null)
            {
                ShowViewerStartupFailure();
                return;
            }
            _dataModel.Url = url;
            _dataModel.StatusText = $"Cena: {sel}";
            _dataModel.SetState("running");
            NavigateOrOpenExternal(url, route);
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            if (_navigated) return;
            _navigated = true;
            _ = StartViewerAsync();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _battleOverlayOwner.Clear();
            base.OnDetachedFromVisualTree(e);
        }

        private async Task StartViewerAsync()
        {
            if (_requiresNoclip)
            {
                NoclipDataCapability.Report capability = await Task.Run(() =>
                    NoclipDataCapability.Validate(NoclipLocator.Find()));
                if (!capability.Ready)
                {
                    _dataModel.StatusText = Strings.U_Vh_NoclipDataPreparing;
                    _dataModel.SetState("starting");
                    await NoclipDataBootstrap.EnsureDataAsync();
                }
            }

            string? url = await Task.Run(() => ViewerHubService.BuildUrl(_descId, _extraQuery, forExternalBrowser: true));
            if (url == null)
            {
                ShowViewerStartupFailure();
                return;
            }
            _dataModel.Url = url;
            _dataModel.StatusText = ViewerHubService.StatusText;
            _dataModel.SetState("running");

            if (_requiresNoclip)
                ViewerHost.InjectScriptOnDocumentCreated(NoclipSkinScript);

            NavigateOrOpenExternal(url, ViewerHubService.Find(_descId)?.Route ?? "");
        }

        /// <summary>
        /// Non-Windows path: the WebView2 embed is Windows-only, but the in-process server and the
        /// whole data layer (bootstrap + fetch-through + overlays) are platform-neutral — so the
        /// viewer simply opens in the system browser at the same loopback URL. The fallback card
        /// keeps a re-open affordance; navigation state is shared with the Windows embed path.
        /// </summary>
        private void NavigateOrOpenExternal(string url, string route)
        {
            if (OperatingSystem.IsWindows())
            {
                ViewerHost.NavigatePinned(url, route);
                return;
            }

            _dataModel.ExternalBrowserUrl = url;
            _dataModel.IsExternalBrowserFallback = true;
            OpenUrlInBrowser(url);
        }

        private void Button_OpenInBrowser(object? sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_dataModel.ExternalBrowserUrl))
                OpenUrlInBrowser(_dataModel.ExternalBrowserUrl);
        }

        private void OpenUrlInBrowser(string url)
        {
            if (!ExternalBrowserLauncher.Open(url))
            {
                _dataModel.StatusText = Strings.U_Vh_ExternalViewerFailed;
                _dataModel.SetState("error");
            }
        }

        private void Button_Reload(object? sender, RoutedEventArgs e)
        {
            bool recoveringFromWebView2Failure =
                ViewerHost.InitializationFailure != null ||
                _dataModel.HasWebView2InitializationFailure;
            _dataModel.ClearWebView2InitializationFailure();

            string? url = ViewerHubService.BuildUrl(_descId, _extraQuery, forExternalBrowser: true);
            if (url != null)
            {
                _dataModel.Url = url;
                _dataModel.StatusText = ViewerHubService.StatusText;
                _dataModel.SetState(recoveringFromWebView2Failure ? "starting" : "running");
                NavigateOrOpenExternal(url, ViewerHubService.Find(_descId)?.Route ?? "");
            }
            else
            {
                ShowViewerStartupFailure();
            }
        }

        private void OnWebView2InitializationFailed(
            object? sender,
            WebView2InitializationFailedEventArgs e)
        {
            if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(
                    () => OnWebView2InitializationFailed(sender, e));
                return;
            }

            _webView2InstallerPath = e.InstallerPath;
            bool platformUnavailable =
                e.Kind == WebView2InitializationFailureKind.PlatformUnavailable;
            string title = platformUnavailable
                ? Strings.U_Vh_EmbeddedViewerUnavailableTitle
                : Strings.U_Vh_WebView2InitializationFailedTitle;
            string recoveryMessage = platformUnavailable
                ? Strings.U_Vh_EmbeddedViewerUnavailableMessage
                : e.InstallerAvailable
                    ? Strings.U_Vh_WebView2RecoveryMessage
                    : Strings.U_Vh_WebView2InstallerMissingMessage;
            _dataModel.ShowWebView2InitializationFailure(
                title,
                recoveryMessage,
                e.InstallerAvailable,
                showInstallerAction: !platformUnavailable);
        }

        private void ShowViewerStartupFailure()
        {
            if (!WebView2Host.IsSupported)
            {
                _webView2InstallerPath = null;
                _dataModel.ShowWebView2InitializationFailure(
                    Strings.U_Vh_EmbeddedViewerUnavailableTitle,
                    Strings.U_Vh_EmbeddedViewerUnavailableMessage,
                    installerAvailable: false,
                    showInstallerAction: false);
                return;
            }

            _dataModel.StatusText = ViewerHubService.StatusText;
            _dataModel.SetState("error");
        }

        /// <summary>
        /// Opens File Explorer with the packaged offline installer selected. This intentionally
        /// never launches the installer: installation remains an explicit human action.
        /// </summary>
        private void Button_ShowWebView2Installer(object? sender, RoutedEventArgs e)
        {
            if (!OperatingSystem.IsWindows())
            {
                _webView2InstallerPath = null;
                _dataModel.ShowWebView2InitializationFailure(
                    Strings.U_Vh_EmbeddedViewerUnavailableTitle,
                    Strings.U_Vh_EmbeddedViewerUnavailableMessage,
                    installerAvailable: false,
                    showInstallerAction: false);
                FFXProjectEditor.Diagnostics.DebugLog.Warn(
                    "ViewerShell.WebView2Recovery",
                    "Ignored the Windows-only installer action on a non-Windows platform.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_webView2InstallerPath) ||
                !File.Exists(_webView2InstallerPath))
            {
                _dataModel.WebView2RecoveryMessage = Strings.U_Vh_WebView2InstallerMissingMessage;
                _dataModel.CanShowWebView2Installer = false;
                FFXProjectEditor.Diagnostics.DebugLog.Warn(
                    "ViewerShell.WebView2Recovery",
                    "The packaged WebView2 installer was not found; no process was started.");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{_webView2InstallerPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                FFXProjectEditor.Diagnostics.DebugLog.Info(
                    "ViewerShell.WebView2Recovery",
                    "Opened File Explorer at the packaged WebView2 installer; the installer was not executed.");
            }
            catch (Exception ex)
            {
                _dataModel.WebView2RecoveryMessage = Strings.U_Vh_WebView2InstallerRevealFailed;
                FFXProjectEditor.Diagnostics.DebugLog.Error(
                    "ViewerShell.WebView2Recovery",
                    "Failed to reveal the packaged WebView2 installer in File Explorer.",
                    ex);
            }
        }

        /// <summary>
        /// Lets a clean-machine user bind the bundled viewer to game data they already own. The
        /// selected directory is validated and persisted by NoclipLocator; no content is downloaded,
        /// copied, repaired, or written back into the game extraction.
        /// </summary>
        private async void Button_ConfigureNoclipData(object? sender, RoutedEventArgs e)
        {
            var folders = await AvaloniaDialog_Util.OpenFolderDialog(
                this,
                Strings.U_Vh_ChooseNoclipDataFolder);
            if (folders.Count == 0)
                return;

            if (!NoclipLocator.TryConfigureRoot(folders[0], out string error))
            {
                _dataModel.StatusText = Strings.U_Vh_NoclipDataFolderRejected;
                _dataModel.SetState("error");
                FFXProjectEditor.Diagnostics.DebugLog.Warn(
                    "ViewerShell.ConfigureNoclip",
                    $"Selected local data root was rejected: {error}");
                return;
            }

            _dataModel.StatusText = Strings.U_Vh_NoclipDataConfigured;
            _dataModel.SetState("starting");
            await StartViewerAsync();
        }
    }
}

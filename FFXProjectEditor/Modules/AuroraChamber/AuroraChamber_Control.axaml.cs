using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia;
using System.ComponentModel;

using FFXProjectEditor.Diagnostics;
using FFXProjectEditor.Modules.Common;
using FFXProjectEditor.Resources;
namespace FFXProjectEditor.Modules.AuroraChamber
{
    /// <summary>
    /// 🌅 AURORA CHAMBER — Dashboard (nova arquitetura de pop-ups).
    /// Tela principal = escolher mapas (badges). Os viewers (EditViewer / RealGame) abrem em
    /// JANELAS POP-UP dedicadas (WebView2). Regras do plano UI/UX:
    ///   • as janelas NÃO interagem entre si (podem ficar lado a lado);
    ///   • ao mudar de mapa/battle no dashboard, TODAS as janelas abertas são fechadas
    ///     (previne edição de monstros no mapa errado / vazamento de memória).
    /// </summary>
    public partial class AuroraChamber_Control : UserControl, IRestorableModule
    {
        private readonly AuroraChamber_DataModel dataModel;
        private AuroraViewerWindow? _editViewerWindow;
        private AuroraViewerWindow? _realGameWindow;
        private string? _realGameUrl;

        public AuroraChamber_Control()
        {
            dataModel = new AuroraChamber_DataModel();
            DataContext = dataModel;
            InitializeComponent();
            dataModel.EmbeddedReloadRequested += () => ReloadViewerWindows();
            dataModel.PropertyChanged += OnDataModelPropertyChanged;
        }

        /// <summary>Mudou mapa/battle → fecha TODAS as janelas de visualização (regra do plano).</summary>
        private void OnDataModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is "SelectedScene" or "SelectedBattle")
            {
                CloseViewerWindows();
            }
        }

        private void CloseViewerWindows()
        {
            _editViewerWindow?.CloseViewer();
            _realGameWindow?.CloseViewer();
            dataModel.CloseRealGamePreview();
            _realGameUrl = null;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            CloseViewerWindows();
            base.OnDetachedFromVisualTree(e);
        }

        private void ReloadViewerWindows()
        {
            // Save re-aplicou o override do RealGame → recarrega as janelas com a URL atual.
            _editViewerWindow?.Reload();
            _realGameWindow?.Reload();
        }

        private async void Button_Render(object? sender, RoutedEventArgs e)
        {
            // EditViewer em janela pop-up (WebView2) no Windows; em Linux/macOS o embed não existe —
            // a mesma URL loopback abre no navegador externo (server in-process é plataforma-neutro).
            (bool ok, string? url, _) = await dataModel.RenderSelectedEmbeddedAsync();
            if (ok && url != null)
            {
                if (!Modules.Common.ViewerShell.ExternalBrowserLauncher.IsEmbeddedViewerSupported)
                {
                    Modules.Common.ViewerShell.ExternalBrowserLauncher.Open(url);
                    return;
                }
                _editViewerWindow ??= CreateViewerWindow(isRealGame: false);
                _editViewerWindow.ShowViewer(url,
                    dataModel.LastEmbeddedIsBattleStage
                        ? $"EditViewer · {dataModel.SelectedBattle} (batalha)"
                        : $"EditViewer · {dataModel.SelectedScene?.Scene.SceneId}",
                    dataModel.LastEmbeddedIsBattleStage
                        ? Strings.U_Au_EditStageDesc
                        : Strings.U_Au_EditViewerDesc,
                    isRealGame: false);
            }
        }

        private void Button_OpenBattle3D(object? sender, RoutedEventArgs e)
        {
            // 🐉 RealGame em janela pop-up no Windows; fora dele vai direto pro navegador.
            string? url = dataModel.OpenRealGameEmbedded();
            if (url != null)
            {
                _realGameUrl = url;
                if (!Modules.Common.ViewerShell.ExternalBrowserLauncher.IsEmbeddedViewerSupported)
                {
                    Modules.Common.ViewerShell.ExternalBrowserLauncher.Open(url);
                    return;
                }
                _realGameWindow ??= CreateViewerWindow(isRealGame: true);
                _realGameWindow.ShowViewer(url,
                    $"RealGame · {dataModel.SelectedBattle}",
                    Strings.U_Noclip_PositionHint,
                    isRealGame: true);
            }
        }

        private AuroraViewerWindow CreateViewerWindow(bool isRealGame)
        {
            var w = new AuroraViewerWindow();
            // 🐛 FIX (2026-08-15, Jarvis-Aurora): a janela cacheada (_editViewerWindow/_realGameWindow via ??=) nunca
            // era zerada quando o usuário a fechava pelo X. Na re-abertura o ShowViewer() chamava Show() numa janela
            // JÁ FECHADA -> InvalidOperationException "Cannot re-show a closed window" -> crash do editor (0xe0434352,
            // stack em Avalonia.Controls.Window.Show, Event Log 15/08 22:17). Zerar o campo aqui obriga o ??= a criar
            // uma JANELA NOVA e fresca na próxima abertura.
            w.Closed += (_, _) =>
            {
                if (isRealGame)
                {
                    dataModel.CloseRealGamePreview();
                    _realGameWindow = null;
                }
                else _editViewerWindow = null;
                DebugLog.Info("AuroraChamber.Overlay", $"Viewer {(isRealGame ? "RealGame" : "EditViewer")} fechado pelo X — referência cacheada zerada.");
            };
            if (isRealGame)
            {
                w.RestoreRequested += () =>
                {
                    dataModel.RestoreBattle3DOverrides();
                    w.Reload();
                };
            }
            return w;
        }

        private void Button_Refresh(object? sender, RoutedEventArgs e) => dataModel.RefreshCatalog();

        private async void Button_MountAll(object? sender, RoutedEventArgs e) => await dataModel.MountAllMissingAsync();

        private async void Button_RemountAll(object? sender, RoutedEventArgs e) => await dataModel.RemountAllScenesAsync();

        private void Button_ReloadBattle(object? sender, RoutedEventArgs e) => dataModel.ReloadSelectedBattle();

        private async void Button_ReloadScene(object? sender, RoutedEventArgs e) => await dataModel.ReloadSelectedSceneAsync();

        private async void Button_RemountScene(object? sender, RoutedEventArgs e) => await dataModel.RemountSelectedSceneAsync();

        private void Button_OpenSceneFolder(object? sender, RoutedEventArgs e) => dataModel.OpenSceneOutputFolder();

        private void Button_OpenMapViewerFolder(object? sender, RoutedEventArgs e) => dataModel.OpenMapViewerWebFolder();

        private void Button_ExportAnchors(object? sender, RoutedEventArgs e) => dataModel.ExportAnchorsJson();

        private void Button_SaveDraggedPositions(object? sender, RoutedEventArgs e) => dataModel.SaveDraggedPositions();

        private void Button_AddMonster(object? sender, RoutedEventArgs e) => dataModel.AddMonster();

        private void Button_RemoveMonster(object? sender, RoutedEventArgs e) => dataModel.RemoveMonster();

        private void Button_RestoreBattle3D(object? sender, RoutedEventArgs e) => dataModel.RestoreBattle3DOverrides();

        private void Button_SaveCamera(object? sender, RoutedEventArgs e) => dataModel.SaveCameraShots();

        private void Button_SaveCameraFloats(object? sender, RoutedEventArgs e) => dataModel.SaveCameraFloats();
    }
}

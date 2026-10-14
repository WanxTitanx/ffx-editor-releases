using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using FFXProjectEditor.Modules.MonsterAiEditor;
using FFXProjectEditor.Services;

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
        private void Button_PrepareOpening(object? sender, RoutedEventArgs e) => dataModel.PrepareOpeningTemplate();
        private void Button_SaveEncounterEdits(object? sender, RoutedEventArgs e) => dataModel.SaveEncounterEdits();
        private void Button_EditCompanionAi(object? sender, RoutedEventArgs e)
        {
            if (!dataModel.CanOpenCompanionAi || sender is not Button { DataContext: AuroraCompanionEditRow row }) return;
            MonsterAiEditor_DataModel? aiModel = null;
            try
            {
                string project = Project_Service.Instance.ProjectPath;
                string? battle = dataModel.SelectedBattle;
                string path = Path.GetFullPath(Project_Service.Instance.GetPathMon((int)row.MonsterId));
                var control = new MonsterAiEditor_Control();
                aiModel = (MonsterAiEditor_DataModel)control.DataContext!;
                var monster = aiModel.Monsters.FirstOrDefault(m => string.Equals(Path.GetFullPath(m.Path), path,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
                if (monster == null) throw new InvalidOperationException(string.Format(Strings.U_Au_CompanionFileMissing, (int)row.MonsterId));
                aiModel.SelectedMonster = monster;
                var window = new Window { Title = $"{Strings.U_Au_CompanionEditAi} · {monster.Id} · {row.MonsterName}",
                    Width = 1380, Height = 900, MinWidth = 900, MinHeight = 600, Content = control,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner };
                var ownedModel = aiModel;
                window.Closed += (_, _) =>
                {
                    ownedModel.Dispose();
                    if (Project_Service.Instance.ProjectPath == project && dataModel.SelectedBattle == battle && !dataModel.HasPendingEncounterEdits)
                        dataModel.ReloadSelectedBattle();
                };
                if (TopLevel.GetTopLevel(this) is Window owner) window.Show(owner); else window.Show();
            }
            catch (Exception ex)
            {
                aiModel?.Dispose();
                DebugLog.Error("aurora-chamber.CompanionAI", "Could not open companion AI editor", ex);
                dataModel.EncounterEditStatus = string.Format(Strings.U_Au_EncounterEditFailed, ex.Message);
            }
        }
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
            try
            {
                // This advanced action is HD-only. The primary battle action owns NoClip;
                // falling back here would silently make both buttons open the same surface.
                (bool ok, string? url, _) = await dataModel.RenderSelectedEmbeddedAsync(hdOnly: true);
                if (!ok || url == null) return;
                if (!Modules.Common.ViewerShell.ExternalBrowserLauncher.IsEmbeddedViewerSupported)
                {
                    if (!Modules.Common.ViewerShell.ExternalBrowserLauncher.Open(url))
                        dataModel.RenderStatus = Strings.U_Au_WorkspaceBrowserUnavailable;
                    return;
                }
                _editViewerWindow ??= CreateViewerWindow(isRealGame: false);
                _editViewerWindow.ShowViewer(url, dataModel.EmbeddedTitle, dataModel.EmbeddedStatus, isRealGame: false);
            }
            catch (Exception error)
            {
                DebugLog.Error("aurora-chamber.Viewer", "HD map viewer could not be opened.", error);
                dataModel.RenderStatus = string.Format(Strings.U_Au_WorkspacePreviewError, error.Message);
            }
        }

        private void Button_OpenBattle3D(object? sender, RoutedEventArgs e)
        {
            dataModel.PreviewStatus = Strings.U_Au_WorkspaceOpening;
            try
            {
                string? url = dataModel.OpenRealGameEmbedded();
                if (url == null) { dataModel.PreviewStatus = dataModel.Battle3DStatus; return; }
                _realGameUrl = url;
                if (!Modules.Common.ViewerShell.ExternalBrowserLauncher.IsEmbeddedViewerSupported)
                {
                    dataModel.PreviewStatus = Modules.Common.ViewerShell.ExternalBrowserLauncher.Open(url)
                        ? Strings.U_Au_WorkspacePreviewOpened : Strings.U_Au_WorkspaceBrowserUnavailable;
                    return;
                }
                _realGameWindow ??= CreateViewerWindow(isRealGame: true);
                _realGameWindow.ShowViewer(url, dataModel.EmbeddedTitle, Strings.U_Noclip_CompositionHint, isRealGame: true);
                dataModel.PreviewStatus = Strings.U_Au_WorkspacePreviewOpened;
            }
            catch (Exception error)
            {
                DebugLog.Error("aurora-chamber.Viewer", "Battle preview could not be opened.", error);
                dataModel.PreviewStatus = string.Format(Strings.U_Au_WorkspacePreviewError, error.Message);
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

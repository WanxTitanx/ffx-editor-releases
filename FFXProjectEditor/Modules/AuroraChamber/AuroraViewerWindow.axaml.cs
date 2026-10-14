using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Diagnostics;

using FFXProjectEditor.Diagnostics;

namespace FFXProjectEditor.Modules.AuroraChamber
{
    /// <summary>
    /// 🐉 Pop-up dedicado de visualização (EditViewer ou RealGame). Regra do plano UI/UX: as janelas NÃO
    /// interagem entre si (podem ficar lado a lado) e são fechadas automaticamente quando o mapa/battle
    /// selecionado muda no dashboard (o AuroraChamber_Control gerencia isso). Só leitura sobre o jogo:
    /// o EditViewer edita via AuroraDragBridge (POST) e o RealGame reflete o override do 0e/.
    /// </summary>
    public partial class AuroraViewerWindow : Window
    {
        private string? _url;
        private bool _isRealGame;
        private bool _closed; // 🐛 FIX (2026-08-15): rastreado no Closed; impede re-Show() em janela fechada (crash).

        public AuroraViewerWindow()
        {
            InitializeComponent();
            Closed += (_, _) => { _url = null; _closed = true; };
        }

        /// <summary>Abre (ou reusa) a janela navegando para a URL do viewer.</summary>
        public void ShowViewer(string url, string title, string status, bool isRealGame)
        {
            // 🐛 FIX (2026-08-15, Jarvis-Aurora): nunca chamar Show() numa janela JÁ FECHADA — Avalonia lança
            // InvalidOperationException "Cannot re-show a closed window" (crash 0xe0434352). O dono agora zera a
            // referência cacheada no Closed (cria janela nova), mas mantemos o guard aqui como defesa extra: se a
            // janela foi fechada, substituir por uma nova. Rejeitamos também URL nula/vazia (o Closed zera _url).
            if (string.IsNullOrWhiteSpace(url))
            {
                DebugLog.Warn("AuroraChamber.Overlay", "ShowViewer ignorado: URL vazia (janela já fechada?).");
                return;
            }
            if (_closed)
            {
                DebugLog.Warn("AuroraChamber.Overlay", "ShowViewer numa janela fechada — abandona e deixa o dono criar nova.");
                return;
            }
            _url = url;
            _isRealGame = isRealGame;
            TitleText.Text = title;
            StatusText.Text = status;
            RestoreBtn.IsVisible = isRealGame;
            if (isRealGame) RestoreBtn.IsEnabled = true;
            ViewerHost.Navigate(url);
            if (!IsVisible) Show();
            else Activate();
        }

        public void CloseViewer()
        {
            if (IsVisible) Close();
        }

        public void Reload()
        {
            if (_url != null) ViewerHost.Navigate(_url);
        }

        private void Button_Reload(object? sender, RoutedEventArgs e) => Reload();

        private void Button_Restore(object? sender, RoutedEventArgs e)
        {
            // O dashboard escuta e restaura via Aurora3DLauncher (a janela não tem o DataModel).
            RestoreRequested?.Invoke();
        }

        /// <summary>Disparado pelo botão Restaurar RealGame (o dono da janela aplica a restauração).</summary>
        public event Action? RestoreRequested;

    }
}

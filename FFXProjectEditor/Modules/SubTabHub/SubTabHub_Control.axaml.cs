using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Modules.Common;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;

namespace FFXProjectEditor;

// Reusable consolidation shell. Callers configure it with AddTab(...) and hand it to SetModule. Each sub-tab's
// UserControl is built lazily on first selection and cached, so opening a hub never spins up every editor at once.
// Pure presentation/navigation: the hosted controls own all of their writer/parser/session logic unchanged.
public partial class SubTabHub_Control : UserControl, IRestorableModule
{
    public enum TabMode { Writer, ReadOnly }

    // Jarvis-UI (Sprint B 2026-06-20, OPT-B4): copy do placeholder requiresProject. Antes era um literal
    // PT fixo ("Carregue um projeto..."); agora cada hub pode passar a frase que faz sentido pro seu
    // contexto (Save Editor, Field Hub, etc.), mantendo o default legado p/ quem não passar nada.
    const string DefaultRequiresProjectMessage = "Carregue um projeto para usar esta aba.";

    sealed class TabSpec
    {
        public string Label = string.Empty;
        public Func<Control> Factory = () => new Control();
        public TabMode Mode;
        public string PillText = string.Empty;
        public bool RequiresProject;
        public string A11yName = string.Empty;
        public Control? Built;
    }

    readonly List<TabSpec> tabs = new();

    // Jarvis-UI (Fase D §D3): índice da aba ativa, rastreado em SelectTab. Expõe o estado de navegação
    // do hub pra que back/forward possa restaurar qual sub-aba estava aberta (cobre os 9 hubs).
    int _activeTabIndex = 0;

    public SubTabHub_Control()
    {
        InitializeComponent();
    }

    /// <summary>Mensagem do placeholder quando <c>requiresProject</c> bloqueia a aba. Default PT legado.</summary>
    public string RequiresProjectMessage { get; set; } = DefaultRequiresProjectMessage;

    // Fluent: hub.AddTab(...).AddTab(...). The first tab added is auto-selected.
    //
    // Jarvis-UI (Sprint B 2026-06-20, OPT-B1/B2): <paramref name="a11yName"/> opcional vira
    // AutomationProperties.Name no botão de tab gerado; se vazio, deriva do <paramref name="label"/>
    // (prefixo "Aba ") pra que screen readers sempre leiam algo além do texto curto.
    public SubTabHub_Control AddTab(string label, Func<Control> factory, TabMode mode, string pillText, bool requiresProject = false, string? a11yName = null)
    {
        int index = tabs.Count;
        string resolvedA11y = string.IsNullOrWhiteSpace(a11yName) ? $"Aba {label}" : a11yName;
        tabs.Add(new TabSpec { Label = label, Factory = factory, Mode = mode, PillText = pillText, RequiresProject = requiresProject, A11yName = resolvedA11y });

        var button = new Button { Content = label, Tag = index.ToString() };
        button.Classes.Add("tabPill");
        AutomationProperties.SetName(button, resolvedA11y);
        button.Click += Tab_Click;
        TabStrip.Children.Add(button);

        if (index == 0)
            SelectTab(0);
        return this;
    }

    private void Tab_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string tag && int.TryParse(tag, out int index))
        {
            SelectTab(index);
            AudioStudio_Service.Instance.PlayAlternative();
        }
    }

    private void SelectTab(int index)
    {
        if (index < 0 || index >= tabs.Count)
            return;

        _activeTabIndex = index;
        TabSpec spec = tabs[index];

        if (spec.RequiresProject && !Project_Service.Instance.IsProjectLoaded)
        {
            // Don't construct an editor that expects a loaded project — show a calm placeholder instead.
            // Jarvis-UI (Sprint B 2026-06-20, OPT-B4): copy agora vem de RequiresProjectMessage (configurável
            // por hub), não mais um literal PT fixo — mantém o default legado se ninguém setar.
            TabHost.Content = new TextBlock
            {
                Text = RequiresProjectMessage,
                Margin = new Thickness(18),
                Classes = { "muted" },
            };
        }
        else
        {
            spec.Built ??= spec.Factory();
            TabHost.Content = spec.Built;
        }

        foreach (var child in TabStrip.Children)
        {
            if (child is Button b && b.Tag is string tag && int.TryParse(tag, out int i))
            {
                bool active = i == index;
                if (active && !b.Classes.Contains("tabPillActive"))
                    b.Classes.Add("tabPillActive");
                else if (!active)
                    b.Classes.Remove("tabPillActive");
            }
        }

        bool readOnly = spec.Mode == TabMode.ReadOnly;
        ReadonlyPill.IsVisible = readOnly;
        WriterPill.IsVisible = !readOnly;
        (readOnly ? ReadonlyPillText : WriterPillText).Text = spec.PillText;
    }

    // --- IRestorableModule (Jarvis-UI Fase D §D3) ---
    // O hub só tem uma peça de estado de navegação significativa: qual sub-aba está ativa. Capturamos o
    // índice; no restore chamamos SelectTab, que re-aplica pill/active/hosted-content normalmente. O
    // conteúdo de cada sub-aba (filtro/seleção interna) é responsabilidade do controle hospedado — se ele
    // também implementar IRestorableModule, o Main_Window encadeia o restore na sequência.
    public Dictionary<string, object?>? CaptureState()
    {
        return new()
        {
            ["activeTab"] = _activeTabIndex,
        };
    }

    public void RestoreState(Dictionary<string, object?>? state)
    {
        if (state == null) return;

        if (state.TryGetValue("activeTab", out object? tabObj) && tabObj is int idx)
            SelectTab(idx);
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using FFXProjectEditor.Modules.AiAssistant;
using FFXProjectEditor.Modules.AutoAbilityEditor;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Modules.BattleKernel.Commands;
using FFXProjectEditor.Modules.BattleExplorer;
using FFXProjectEditor.Modules.BukiGetRewards;
using FFXProjectEditor.Modules.CtbBaseEditor;
using FFXProjectEditor.Modules.ThunderPlainsEditor;
using FFXProjectEditor.Modules.TreasureMapEditor;
using FFXProjectEditor.Modules.Common;
using FFXProjectEditor.Modules.CustomizationEditor;
using FFXProjectEditor.Modules.DifficultyDirector;
using FFXProjectEditor.Modules.Extras;
using FFXProjectEditor.Modules.KeyItemEditor;
using FFXProjectEditor.Modules.LiveBattleLab;
using FFXProjectEditor.Modules.Main;
using FFXProjectEditor.Modules.MixTableEditor;
using FFXProjectEditor.Modules.PlayerGrowthEditor;
using FFXProjectEditor.Modules.ShopExplorer;
using FFXProjectEditor.Modules.TreasureEditor;
            using FFXProjectEditor.Modules.WeaponGear;
            using FFXProjectEditor.Modules.CelestialWeaponEditor;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FFXProjectEditor;

public partial class Main_Window : Window
{
    private readonly Main_DataModel DataModel;
    private readonly HashSet<Control> _hoverSoundBoundControls = [];
    private Control? _lastHoveredControl;
    private DateTime _lastHoverAtUtc = DateTime.MinValue;
    private bool _playedEditorOpenFx;
    private readonly Stack<NavigationSnapshot> _backHistory = new();
    private readonly Stack<NavigationSnapshot> _forwardHistory = new();
    private bool _isRestoringNavigation;

    // ===== Rail / drawer state (Jarvis-UI, audit 2026-06-19).
    // O IconRail pode ser oculto por 2 motivos: tela estreita (Window.narrow style) ou toggle manual
    // (Ctrl+B / botão RailDrawerToggle). Este bool só governa o toggle manual; o estilo narrow é independente.
    // UpdateRailToggleVisibility() mostra o botão flutuante quando AMBOS são necessários para reabrir.
    private bool _isRailManuallyHidden;

    // ===== v2.160.1.0 (Fase D §16): navegação back/forward genérica via IRestorableModule.
    //    Antes: NavigationSnapshot tinha Kind enum (3 valores) + campos fixos, e Capture fazia
    //    pattern-match em 3 tipos concretos — não escalava pra ~38 módulos.
    //    Agora: snapshot carrega (ModuleId, State) onde State é Dictionary opaco produzido pelo
    //    IRestorableModule do controle ativo. O Main_Window não conhece a semântica do State.
    //    Os 3 módulos originais (Home/Monster/KernelCommands) continuam funcionando: Home é stateless
    //    (null), Monster/Kernel implementam IRestorableModule delegando pros getters existentes.
    private readonly record struct NavigationSnapshot(
        string ModuleId,
        Dictionary<string, object?>? State = null);

    /// <summary>Id do módulo atualmente exibido no ContentFrame (setado em SetModule/handlers).</summary>
    private string _currentModuleId = "home";
    private string? _lastActiveModuleId; // 🐉 DEBUG LOG: área do módulo anterior (para Deactivate no SetModule)

    public Main_Window(string? startupProjectPath = null)
    {
        DataModel = new Main_DataModel();
        this.DataContext = DataModel;
        InitializeComponent();
        // Tunnel: atalhos globais (Ctrl+K/B, Alt+←/→) mesmo com foco num TextBox/editor filho.
        AddHandler(KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DropEvent, Drop_ProjectFolder);
        // 🐉 DEBUG LOG (v2.224.1.13): UI Event Tracer — loga automaticamente QUALQUER clique de botão
        // com o módulo ativo (a flag da área decide: 1 apita quando o módulo está aberto, 2 sempre).
        AddHandler(Button.ClickEvent, OnAnyButtonClick, RoutingStrategies.Bubble);
        Opened += Window_Opened;
        _ = AudioStudio_Service.Instance;
        BuildIconRail();        // §16: rail gerado do ModuleRegistry (1 ícone por módulo, zero MenuFlyout).
        BuildRailDrawer();      // §16: drawer narrow = grid de ícones (não mais lista textual).
        ShowHome();
        UpdateNavigationState();

        // v2.208.0.0 (Jarvis-CLINE): reflete o idioma atual no botão do seletor (PT/EN).
        if (this.FindControl<TextBlock>("LangButtonText") is { } langButton)
            langButton.Text = FFXProjectEditor.Resources.Strings.CurrentLanguage.ToUpperInvariant();

        // Rail/drawer usam RequiresProject gate estático (não há binding XAML agora que são gerados
        // em code-behind). Reage ao IsProjectLoaded para (re)abilitar botões quando o workspace muda.
        Project_Service.Instance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(Project_Service.IsProjectLoaded))
                return;
            RefreshRailProjectGates();
        };

        // AI feature gate (Jarvis-UI): o toggle vive no flyout do header; ao virar,
        // rebuilda rail+drawer (o registry re-filtra) e, se o painel AI estava aberto
        // quando desligaram, volta pro Home — módulo oculto não fica órfão na tela.
        Core.AiFeatureGate.Changed += () =>
        {
            BuildIconRail();
            BuildRailDrawer();
            RefreshRailProjectGates();
            if (!Core.AiFeatureGate.Enabled && _currentModuleId == Core.AiFeatureGate.ModuleId)
                ShowHome();
        };

        if (!string.IsNullOrWhiteSpace(startupProjectPath) && Project_Service.IsPathValid(startupProjectPath))
        {
            DataModel.LoadProjectFolder(startupProjectPath);
        }

        // Reage a mudanças de breakpoint narrow/wide para mostrar/ocultar o drawer toggle.
        // O projeto não referencia System.Reactive, então usamos um IObserver leve em vez de lambda.
        this.GetObservable(BoundsProperty).Subscribe(new BoundsActionObserver(() => UpdateRailToggleVisibility()));
    }

    public void ShowChangeSetPreview(FFXProjectEditor.Core.OperationPreview preview)
    {
        // Preview-only: os módulos que ainda não produzem um OperationPlan real passam null.
        // O ChangeSetDrawer desabilita o botão "Aplicar" quando o plano é null — seguro, honesto.
        ChangeSetDrawerControl.SetPreview(preview, plan: null);
    }

    // ===== Alternador de idioma (v2.208.0.0 Jarvis-CLINE; +es/fr/de/it v2.220.6.0 IFRT-2) =====
    // 🐉 DEBUG LOG (v2.224.1.13): abre o debug.log no editor padrão (menu do seletor de idioma).
    private void MenuItem_OpenDebugLog_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            string log = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FFXProjectEditor", "debug.log");
            if (!File.Exists(log))
                log = Path.Combine(Directory.GetCurrentDirectory(), "work", "editor-debug.log");
            if (File.Exists(log))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = log, UseShellExecute = true });
            else
                FFXProjectEditor.Diagnostics.DebugLog.Warn("Ui", "debug.log not found");
        }
        catch (Exception ex) { FFXProjectEditor.Diagnostics.DebugLog.Error("Ui", "failed to open debug.log", ex); }
    }
    private void MenuItem_Language_Pt_Click(object? sender, RoutedEventArgs e) => ApplyLanguage("pt");

    private void MenuItem_Language_En_Click(object? sender, RoutedEventArgs e) => ApplyLanguage("en");

    private void MenuItem_Language_Es_Click(object? sender, RoutedEventArgs e) => ApplyLanguage("es");

    private void MenuItem_Language_Fr_Click(object? sender, RoutedEventArgs e) => ApplyLanguage("fr");

    private void MenuItem_Language_De_Click(object? sender, RoutedEventArgs e) => ApplyLanguage("de");

    private void MenuItem_Language_It_Click(object? sender, RoutedEventArgs e) => ApplyLanguage("it");

    private void MenuItem_Language_Ja_Click(object? sender, RoutedEventArgs e) => ApplyLanguage("ja");

    private void MenuItem_Language_Ko_Click(object? sender, RoutedEventArgs e) => ApplyLanguage("ko");

    private void MenuItem_Language_Zh_Click(object? sender, RoutedEventArgs e) => ApplyLanguage("zh");

    private void ApplyLanguage(string lang)
    {
        if (FFXProjectEditor.Resources.Strings.CurrentLanguage == lang)
            return;

        FFXProjectEditor.Resources.Strings.SetLanguage(lang);

        // {x:Static} bindings são resolvidos no load do XAML — a troca só reavalia após reiniciar.
        // Reinício limpo: relança o exe atual e encerra este processo.
        string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        if (!string.IsNullOrEmpty(exe))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });

        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    // ===== Alternador de tema: Dark Studio ↔ Alto Contraste WCAG =====
    private bool _isHighContrast = false;

    private void Button_ThemeToggle_Click(object? sender, RoutedEventArgs e)
    {
        _isHighContrast = !_isHighContrast;
        ApplyTheme(_isHighContrast);
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void ApplyTheme(bool highContrast)
    {
        var app = Avalonia.Application.Current;
        if (app == null) return;

        if (highContrast)
        {
            app.Resources["PanelDeepColor"] = Avalonia.Media.Color.Parse("#050505");
            app.Resources["PanelDeeperColor"] = Avalonia.Media.Color.Parse("#000000");
            app.Resources["HeroAccentColor"] = Avalonia.Media.Color.Parse("#FFD600");
            app.Resources["TextPrimaryBrush"] = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White);
            app.Resources["TextMutedBrush"] = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#DDDDDD"));
            app.Resources["AccentCoolBrush"] = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FFD600"));
            app.Resources["AccentWarmBrush"] = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF6B00"));
        }
        else
        {
            app.Resources["PanelDeepColor"] = Avalonia.Media.Color.Parse("#0E1117");
            app.Resources["PanelDeeperColor"] = Avalonia.Media.Color.Parse("#080B0F");
            app.Resources["HeroAccentColor"] = Avalonia.Media.Color.Parse("#1A3A5C");
            app.Resources["TextPrimaryBrush"] = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E8EAF0"));
            app.Resources["TextMutedBrush"] = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#8892A4"));
            app.Resources["AccentCoolBrush"] = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#5BA3F5"));
            app.Resources["AccentWarmBrush"] = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F0A045"));
        }

        // Safe: try to update the toggle button text — if Content isn't a TextBlock (e.g. null at startup,
        // template not yet applied, or designer mode), silently skip. The visual toggle still works via colors.
        if (Button_ThemeToggle?.Content is TextBlock tb)
            tb.Text = highContrast ? "♿" : "🌗";
    }

    // IObserver leve para Bounds (o projeto não tem o Subscribe(Action<T>) de System.Reactive).
    private sealed class BoundsActionObserver : IObserver<Rect>
    {
        private readonly Action _onNext;
        public BoundsActionObserver(Action onNext) => _onNext = onNext;
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(Rect value) => _onNext();
    }

    // ===== Keyboard shortcuts (Jarvis-UI, audit 2026-06-19; fix v2.162.6.0: tunnel + Popup.IsOpen).
    // Antes deste trecho o app não tinha NENHUM KeyBinding/OnKeyDown em todo o repo, apesar do
    // comentário do Main_Window.axaml prometer Ctrl+B desde a camada 7. Aqui cumprimos o que estava prometido.
    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool alt  = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

        if (ctrl && e.Key == Key.B)
        {
            ToggleIconRail();
            e.Handled = true;
            return;
        }
        // Jarvis-UI Fase D §D5 (v2.161.0.0): Ctrl+K toggles the Command Palette.
        // Esc/light-dismiss (dentro do popup) e a seleção (Enter/Click) fecham via DispatchRequested/CloseRequested.
        if (ctrl && e.Key == Key.K)
        {
            ToggleCommandPalette();
            e.Handled = true;
            return;
        }
        if (alt && e.Key == Key.Left)
        {
            if (DataModel.CanNavigateBack) Button_NavigateBack(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
        if (alt && e.Key == Key.Right)
        {
            if (DataModel.CanNavigateForward) Button_NavigateForward(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
    }

    // ===== Drawer overlay / Ctrl+B (P0-4, Jarvis-UI, audit 2026-06-19).
    // WIDE mode: "≡"/Ctrl+B toggles the inline IconRailCol Border (hide to free width for
    // dense editing, re-show after). NARROW mode: o estilo Window.narrow já força IconRailCol
    // IsVisible=False; antes deste branch, clicar em "≡" ou Ctrl+B não fazia nada visível e
    // TODA navegação se perdia. Agora o narrow abre/fecha o Popup RailDrawer (drawer overlay
    // flutuante com os mesmos grupos do rail, como botões de texto verticais).
    private void Button_ToggleIconRail(object? sender, RoutedEventArgs e) => ToggleIconRail();

    private void ToggleIconRail()
    {
        bool isNarrow = Classes.Contains("narrow");

        if (isNarrow)
        {
            // NARROW: toggle the floating drawer Popup. The inline Border stays hidden by style.
            if (this.FindControl<Popup>("RailDrawer") is { } drawer)
            {
                drawer.PlacementTarget ??= this;
                drawer.IsOpen = !drawer.IsOpen;
            }
            return;
        }

        // WIDE: toggle the inline IconRailCol Border visibility (frees width for dense editing).
        _isRailManuallyHidden = !_isRailManuallyHidden;

        if (this.FindControl<Border>("IconRailCol") is { } rail)
            rail.IsVisible = !_isRailManuallyHidden;

        UpdateRailToggleVisibility();
    }

    // Mostra o botão "≡" flutuante no header sempre que o rail estiver oculto (narrow OU toggle manual em wide).
    private void UpdateRailToggleVisibility()
    {
        if (this.FindControl<Button>("RailDrawerToggle") is not { } toggle) return;
        if (this.FindControl<Border>("IconRailCol") is not { } rail) return;

        bool isNarrow = Classes.Contains("narrow");
        bool railHidden = isNarrow || _isRailManuallyHidden;
        toggle.IsVisible = railHidden && !rail.IsVisible;
    }

    // ===== Command Palette / Ctrl+K (Jarvis-UI Fase D §D5, v2.161.0.0).
    // Central overlay with fuzzy search across the release-approved ModuleRegistry.Public surface.
    // UserControl próprio (CommandPalette_Popup) que não conhece handlers — só dispara eventos.
    // DispatchRequested → Dispatch(id) + fecha; CloseRequested → fecha.
    private void ToggleCommandPalette()
    {
        if (this.FindControl<Popup>("CommandPalette") is not { } popup) return;

        if (popup.IsOpen)
        {
            popup.IsOpen = false;
            return;
        }

        // Avalonia 11: Popup abre com IsOpen (IsVisible não mostra overlay).
        popup.PlacementTarget ??= this;

        // Conteúdo instanciado em code-behind (evita declaração de namespace XAML extra).
        // Cacheado num campo pra não recriar a cada toggle — só reseta o estado (query/foco).
        if (popup.Child is not CommandPalette_Popup palette)
        {
            palette = new CommandPalette_Popup();
            palette.DispatchRequested += Palette_DispatchRequested;
            palette.CloseRequested += Palette_CloseRequested;
            popup.Child = palette;
        }

        popup.IsOpen = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => palette.Reset(), Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void Palette_DispatchRequested(string moduleId)
    {
        if (this.FindControl<Popup>("CommandPalette") is { } popup)
            popup.IsOpen = false;
        Dispatch(moduleId);
    }

    private void Palette_CloseRequested()
    {
        if (this.FindControl<Popup>("CommandPalette") is { } popup)
            popup.IsOpen = false;
    }

    public async void Drop_ProjectFolder(object sender, DragEventArgs e)
    {
        List<string> files = e.Data.GetFileNames().ToList();

        if (files.Count == 0)
        {
            Debug.WriteLine("No files found on drop");
            return;
        }

        string filePath = Uri.UnescapeDataString(files[0]);

        if (!await TryLoadWorkspaceFolder(filePath))
            return;

        AudioStudio_Service.Instance.PlayConfirm();
    }

    // ── Workspace folder selection ──
    // Single funnel for picker + drop: an invalid folder must surface an actionable dialog,
    // never an unhandled exception — async-void handlers escalate a throw into an
    // AppDomain.UnhandledException process kill (crash.log 2026-09-14: "not master folder").
    private async Task<bool> TryLoadWorkspaceFolder(string folder)
    {
        if (!Project_Service.IsPathValid(folder))
        {
            await AvaloniaDialog_Util.ShowMessageAsync(this,
                Strings.U_WsInvalidFolderTitle, Strings.U_WsInvalidFolderMessage);
            return false;
        }

        if (!DataModel.LoadProjectFolder(folder))
        {
            await AvaloniaDialog_Util.ShowMessageAsync(this,
                Strings.U_WsLoadFailedTitle,
                string.Format(Strings.U_WsLoadFailedMessage, DataModel.LastWorkspaceError));
            return false;
        }

        return true;
    }

    private async void Button_ProjectPath(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        List<string> openDialogResults = await AvaloniaDialog_Util.OpenFolderDialog(this, "Select the project folder");
        if (openDialogResults.Count == 0 || !Directory.Exists(openDialogResults[0]))
        {
            return;
        }
        if (!await TryLoadWorkspaceFolder(openDialogResults[0]))
        {
            return;
        }
        AudioStudio_Service.Instance.PlayConfirm();
    }

    // ── Game folder selection ──
    // The game install root is picked independently of the master workspace (the master
    // may live outside the install). Same funnel rule as TryLoadWorkspaceFolder: the
    // async-void handler must surface an actionable dialog, never throw. A valid pick
    // (folder or ancestor with FFX.exe/modules\) is persisted via SetGameRoot.
    private async void Button_GamePath(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        List<string> openDialogResults = await AvaloniaDialog_Util.OpenFolderDialog(this, Strings.U_GameFolderPickerTitle);
        if (openDialogResults.Count == 0)
        {
            return;
        }
        if (!DataModel.SelectGameFolder(openDialogResults[0]))
        {
            await AvaloniaDialog_Util.ShowMessageAsync(this,
                Strings.U_GameInvalidFolderTitle,
                string.Format(Strings.U_GameInvalidFolderMessage, openDialogResults[0]));
            return;
        }
        AudioStudio_Service.Instance.PlayConfirm();
    }

    private void Button_Home(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _currentModuleId = "home";
        PushCurrentSnapshotForNavigation(new NavigationSnapshot("home"));
        ShowHome(playConfirmFx: true);
    }

    private void Button_PreviewMusic(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        AudioStudio_Service.Instance.PreviewSelectedMusicTrack();
        AudioStudio_Service.Instance.PlayMiniEditorConfirm();
    }

    private async void Button_ImportGameMusic(object? sender, RoutedEventArgs e)
    {
        AudioStudio_Service audioService = AudioStudio_Service.Instance;
        if (!audioService.GameMusicImport.CanImport)
        {
            audioService.GameMusicImport.StatusMessage = Strings.AudioImportSelectionRequired;
            return;
        }

        try
        {
            List<string> files = await AvaloniaDialog_Util.OpenFileDialog(
                this,
                Strings.AudioImportPickerTitle,
                fileTypeFilter:
                [
                    new FilePickerFileType(Strings.AudioImportFsbFilter)
                    {
                        Patterns = ["*.fsb"],
                    },
                ]);
            if (files.Count == 0)
            {
                return;
            }

            if (await audioService.ImportSelectedGameMusicAsync(files[0]))
            {
                audioService.PlayConfirm();
            }
        }
        catch (Exception ex)
        {
            audioService.GameMusicImport.StatusMessage = string.Format(
                Strings.AudioImportFailureFormat,
                ex.Message);
            FFXProjectEditor.Diagnostics.DebugLog.Error(
                "AudioStudio.GameMusicImport",
                $"FSB picker/import boundary failed ({ex.GetType().Name}).");
        }
    }

    private void MenuItem_MonsterEditor(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        NavigateToMonsterEditor();
    }
    private void MenuItem_Commands(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        _currentModuleId = "battle-commands-hub";
        PushCurrentSnapshotForNavigation(new NavigationSnapshot("battle-commands-hub"));
        ShowKernelCommands(CommandFile_enum.Command);
    }
    private void MenuItem_Items(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        _currentModuleId = "items-hub";
        PushCurrentSnapshotForNavigation(new NavigationSnapshot("items-hub"));
        ShowKernelCommands(CommandFile_enum.Item);
    }
    private void MenuItem_Treasures(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new TreasureEditor_Control(),
            "Treasures / Chests",
            "Edit takara.bin, the kernel treasure payload table used by chest and treasure obtain calls across the game.",
            "Writable",
            "This is the treasure-payload domain only. You can change what a treasure index gives, but map placement and chest scripting still live in events and treasure flags.",
            "Primary write path for takara.bin. Safe v1 scope: edit existing entries, not placement or entry count."
        );
    }
    private void MenuItem_Blitzball(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Single Blitzball surface — five sub-tabs (Roster / Recruits / Prize Pool / Prize Table / Atlas)
        // on the shared SubTabHub shell. No project gate: the read-only Atlas tab works without a project,
        // and the four writer tabs keep their own "project not loaded" empty states, so requiresProject stays false.
        //
        // Jarvis-UI (Sprint B 2026-06-20): construção das 5 abas delegada ao BlitzballSubTabHubFactory
        // (OPT-B3) — o MenuItem_Blitzball virou ≤5 linhas e a definição das tabs vive num lugar só.
        // A5: emoji 🏐 removido do title do SetModule (continua na label do menu/IconRail como antes).
        var hub = BlitzballSubTabHubFactory.Create();
        SetModule(
            hub,
            "Blitzball",
            "Roster, recruits, prize pool, prize table and the read-only prize Atlas — one surface, five sub-tabs.",
            "Atlas + Writer Lab",
            "Four writer-lab editors (game-file edits, RT0 byte-identity proven / in-game RT2 pending) plus a read-only Spira Data Atlas catalog. The active sub-tab's mode pill is the authoritative read-only vs writer signal.",
            "Writers edit bltz0002.ebp (roster stat-growth), the recruitment .ebp scripts, takara.bin (prize pool) and bltz0200.ebp (prize table + odds). The Atlas tab is read-only with no game-file writes."
        );
    }

    private void MenuItem_SaveEditor(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetModule(
            new SaveEditorHub_Control(),
            "Save Editor",
            "Edit FFX playthrough save files (.psu, raw 25848-byte blobs, PC .ffx). Native port of FFXED v0.749 — Character tab first; other sections via FFXED.jar launcher until ported.",
            "Save Writer",
            "Independent from the kernel workspace: point at a real save on disk. Checksum + tamper tag recalculated on save (dagal/FFXED algorithm). Equipment/Items/Blitzball/Sphere Grid/Minigame/Misc still route to bundled FFXED.jar.",
            "Writer: Character stats (18 slots). PSU + raw PS2 + PC .ffx supported. RT0: --ffx-save-rt0 on 25848-byte files."
        );
    }

    // ===== Sidebar sections: removidos na Camada 2 (Icon Rail). Os grupos agora vivem em
    //     Button.Flyout no XAML e chamam os mesmos MenuItem_*. O estado de colapso por grupo
    //     deixou de aplicar (o flyout já é transitório). SidebarState_Service permanece vivo
    //     para outros usos, mas não é mais consultado aqui. =====

    private void MenuItem_BattleCommandsHub(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        var hub = new SubTabHub_Control();
        hub.AddTab(Strings.U_Mw_CharCommandsTab, () => CreateKernelCommandsControl(CommandFile_enum.Command), SubTabHub_Control.TabMode.Writer, "WRITABLE · command.bin", requiresProject: true)
           .AddTab("Monster Commands 1", () => CreateKernelCommandsControl(CommandFile_enum.MonMagic1), SubTabHub_Control.TabMode.Writer, "WRITABLE · monmagic1.bin", requiresProject: true)
           .AddTab("Monster Commands 2", () => CreateKernelCommandsControl(CommandFile_enum.MonMagic2), SubTabHub_Control.TabMode.Writer, "WRITABLE · monmagic2.bin", requiresProject: true);
        SetModule(
            hub,
            "Battle Commands",
            Strings.U_Mw_BattleCommandsDesc,
            "Battle Commands hub",
            Strings.F2_each_sub_tab_edits_a_kernel_command_tabl_ab1db300,
            Strings.U_Mw_BattleCommandsScope
        );
    }
    private void MenuItem_ItemsHub(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        var hub = new SubTabHub_Control();
        hub.AddTab("Items", () => CreateKernelCommandsControl(CommandFile_enum.Item), SubTabHub_Control.TabMode.Writer, "WRITABLE · item.bin", requiresProject: true)
           .AddTab("Key Items", () => new KeyItemEditor_Control(), SubTabHub_Control.TabMode.ReadOnly, Strings.U_Mw_KeyItemsTooltip, requiresProject: true)
           .AddTab("Treasures", () => new TreasureEditor_Control(), SubTabHub_Control.TabMode.Writer, "WRITABLE · takara.bin", requiresProject: true)
           .AddTab("Gear Rewards", () => new BukiGetTreasureCatalog_Control(null), SubTabHub_Control.TabMode.Writer, "WRITABLE · buki_get.bin (Gear Rewards)", requiresProject: true)
           .AddTab("Gear Templates", () => new WeaponGear_Control(), SubTabHub_Control.TabMode.Writer, "WRITER · weapon.bin (Master Gear Catalog, 153 entries)", requiresProject: true)
                .AddTab("Celestial Weapons", () => new CelestialWeaponEditor_Control(), SubTabHub_Control.TabMode.ReadOnly, "Canonical save identities and Crest/Sigil paths for Caladbolg, Nirvana, Masamune, Spirit Lance, World Champion, Onion Knight, and God Hand.", requiresProject: true)
           .AddTab("Shop", () => new ShopExplorer_Control(), SubTabHub_Control.TabMode.Writer, "GUARDED WRITER · shop slot payload", requiresProject: true)
           .AddTab("Mix Table", () => new MixTableEditor_Control(), SubTabHub_Control.TabMode.Writer, "WRITABLE · prepare.bin (mix matrix)", requiresProject: true);
        SetModule(
            hub,
            "Items",
            Strings.F2_items_key_items_chests_treasures_equipme_dfb5b599,
            "Items hub",
            Strings.F2_items_item_bin_key_items_important_bin_g_bf662b26,
            Strings.U_Mw_ItemsScope
        );
    }
    private void MenuItem_SphereGridHub(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // No project gate: Builder/Canvas constroem topologia do zero (sem projeto). A aba Explorer pede projeto
        // (requiresProject) e mostra placeholder se não houver — preserva o comportamento original de cada um.
        var hub = new SubTabHub_Control();
        hub.AddTab("Explorer (DB)", () => new SphereGridExplorer_Control(), SubTabHub_Control.TabMode.Writer, "EXPLORER · sphere/panel/abmap + Save", requiresProject: true)
           .AddTab("Panel (panel.bin)", () => new FFXProjectEditor.Modules.SphereGridPanel.SphereGridPanel_Control(), SubTabHub_Control.TabMode.Writer, "PANEL · node types grow/restore/edit", requiresProject: true)
           .AddTab("Builder", () => new FFXProjectEditor.Modules.SphereGridBuilder.SphereGridBuilder_Control(), SubTabHub_Control.TabMode.Writer, Strings.U_Mw_BuilderTooltip)
           .AddTab("Canvas", () => new FFXProjectEditor.Modules.SphereGridBuilder.SphereGridCanvas_Control(), SubTabHub_Control.TabMode.Writer, Strings.U_Mw_CanvasTooltip);
        SetModule(
            hub,
            "Sphere Grid",
            Strings.U_Mw_SphereGridDesc,
            "Sphere Grid hub",
            Strings.U_Mw_SphereGridNotes,
            Strings.F2_explorer_panel_require_a_project_builder_753aa4af
        );
    }
    private void MenuItem_TextHub(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        var hub = new SubTabHub_Control();
        hub.AddTab("String Explorer", () => new StringExplorer_Control(), SubTabHub_Control.TabMode.ReadOnly, "EXPLORER · read-only (text tables)", requiresProject: true)
           .AddTab("Macro Explorer", () => new MacroExplorer_Control(), SubTabHub_Control.TabMode.ReadOnly, "EXPLORER · read-only (macro dicts)", requiresProject: true)
           .AddTab("Weapon Names", () => new WeaponNameExplorer_Control(), SubTabHub_Control.TabMode.Writer, "EDITOR · w_name.bin (safe text writer)", requiresProject: true)
           .AddTab("Battle Text", () => new BattleTextExplorer_Control(), SubTabHub_Control.TabMode.Writer, "EDITOR · btl_txt.bin (append-only safe)", requiresProject: true)
           .AddTab("Event Explorer", () => new EventExplorer_Control(), SubTabHub_Control.TabMode.ReadOnly, "EXPLORER · read-only (.ebp)", requiresProject: true);
        SetModule(
            hub,
            Strings.U_Mw_TextHubTitle,
            Strings.U_Mw_TextHubDesc,
            Strings.U_Mw_TextHubMode,
            Strings.F2_string_macro_event_read_only_explorers_w_a88fc293,
            Strings.U_Mw_TextHubScope
        );
    }
    private void MenuItem_EnemyDesignHub(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        // Monster AI Editor + Custom Boss Creator + Difficulty Director — the enemy-design tools (all
        // simple-construct, no internal TabControl, no nav-snapshot wiring -> clean to host). The Monster Editor
        // stays a top-level screen (heavy internal tabs + back/forward deep-links make it a poor hub citizen).
        var hub = new SubTabHub_Control();
        hub.AddTab("Monster AI Editor", () => new MonsterAiEditor_Control(), SubTabHub_Control.TabMode.Writer, "WRITER · monster_*.bin AI script (ATEL)", requiresProject: true)
           .AddTab("Custom Boss Creator", () => new CustomBossCreator_Control(), SubTabHub_Control.TabMode.Writer, Strings.U_Mw_CustomBossTooltip, requiresProject: true)
           .AddTab("Difficulty Director", () => new DifficultyDirector_Control(), SubTabHub_Control.TabMode.Writer, Strings.F2_writer_scales_hp_mp_stats_of_all_m_bin_c06994f6, requiresProject: true);
        SetModule(
            hub,
            "Enemy Design",
            Strings.F2_monster_ai_custom_boss_creation_and_the__bc2c2520,
            "Enemy Design hub",
            Strings.F2_monster_ai_editor_behavior_editor_and_te_6e9ea74f,
            "Writers: scripts de IA em monster_*.bin, clones de m###.bin (novo slot), e escala em massa de m###.bin."
        );
    }
    private void MenuItem_CustomizationsHub(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        var hub = new SubTabHub_Control();
        hub.AddTab("Gear Customizations", () => new GearCustomization_Control(), SubTabHub_Control.TabMode.Writer, "WRITABLE · kaizou.bin", requiresProject: true)
           .AddTab("Aeon Grow / Teach", () => new AeonCustomization_Control(), SubTabHub_Control.TabMode.Writer, "WRITABLE · sum_grow.bin", requiresProject: true)
           .AddTab("Auto-Abilities", () => new AutoAbilityEditor_Control(), SubTabHub_Control.TabMode.Writer, "EDITOR (RT0 bypassed) · a_ability.bin + arms_rate.bin", requiresProject: true);
        SetModule(
            hub,
            "Customizations / Aeons",
            "Customization de equipamento, aeon grow/teach e auto-abilities.",
            "Customizations hub",
            Strings.F2_gear_customizations_writer_for_kaizou_bi_606288ab,
            "Writers: kaizou.bin, sum_grow.bin, a_ability.bin, arms_rate.bin."
        );
    }
    private void MenuItem_StatsHub(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        var hub = new SubTabHub_Control();
        hub.AddTab("PC Stats / Growth", () => new PlayerGrowthEditor_Control(), SubTabHub_Control.TabMode.Writer, "WRITABLE · ply_save.bin + ply_rom.bin", requiresProject: true)
           .AddTab("CTB Base", () => new CtbBaseEditor_Control(), SubTabHub_Control.TabMode.Writer, "WRITABLE · ctb_base.bin", requiresProject: true);
        SetModule(
            hub,
            "Stats",
            Strings.F2_character_stats_growth_and_the_ctb_battl_61a39422,
            "Stats hub",
            Strings.F2_pc_stats_growth_writer_for_ply_save_bin__f02136b3,
            "Writers: ply_save.bin, ply_rom.bin, ctb_base.bin."
        );
    }
    private void MenuItem_EncountersHub(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        // Encounter routing (which battle fires on a map) + Formation editing (which 8 monsters fill that btl_*) —
        // the natural pair. Encounter Table keeps its double-click -> Aurora Chamber bridge inside the tab factory.
        var hub = new SubTabHub_Control();
        hub.AddTab("Encounter Table", () =>
            {
                EncounterTableExplorer_Control explorer = new();
                explorer.RequestOpenAuroraChamberForMap += OpenAuroraChamberForMap;
                return explorer;
            }, SubTabHub_Control.TabMode.ReadOnly, "EXPLORER · btl.bin routing (read-only)", requiresProject: true)
           .AddTab("Formation Editor", () => new FormationEditor_Control(), SubTabHub_Control.TabMode.Writer, "WRITER · btl_* 8 slots (RT0 858/858)", requiresProject: true);
        SetModule(
            hub,
            "Encounters & Formation",
            Strings.F2_encounter_routing_btl_bin_and_the_format_9f6add97,
            "Encounters & Formation hub",
            Strings.F2_encounter_table_read_only_view_of_the_ro_4011aa4b,
            Strings.F2_writer_the_8_monster_slots_of_btl_format_f0fbee6e
        );
    }
    private void MenuItem_BukiGetRewards(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        OpenBukiGetRewards();
    }

    public void OpenBukiGetRewards(int? preferredRow = null)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new BukiGetTreasureCatalog_Control(preferredRow),
            "Gear Rewards / buki_get",
            "Inspect buki_get.bin, the fixed gear-reward payload catalog referenced by takara.bin Gear Pickup entries.",
            "Read-only Atlas",
            "Direct bridge: takara Kind=0x05 Type=N points at buki_get row N. This belongs to Treasure / Rewards, not the general weapon-name editor.",
            "Read-only for now. Owner/type/slots/auto-abilities are decoded from the 0x10-byte payload; final equipment names/models still need the w_name.bin / weapon-data bridge."
        );
    }
    private void MenuItem_Customizations(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        MenuItem_CustomizationsHub(sender, e);
    }
    private void MenuItem_PlayerGrowth(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new PlayerGrowthEditor_Control(),
            "PC Stats / Growth",
            "Edit known starting stat and growth fields in new_uspc ply_save.bin and ply_rom.bin while preserving the existing table shape and opaque bytes.",
            "Writable",
            "This is the conservative player-kernel bridge: exposed stat and coefficient fields only, with the rest of each entry carried through untouched.",
            "Primary write paths for ply_save.bin and ply_rom.bin. Safe v1 scope: edit existing rows only; no row synthesis, no text-pool reshaping."
        );
    }
    private void MenuItem_CtbBase(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new CtbBaseEditor_Control(),
            "CTB Base",
            "Edit ctb_base.bin, the fixed agility-to-tickspeed/ICV table used by the battle timing layer.",
            "Writable",
            "A clean fixed-length table: one row per agility value, two proven bytes per row, and no structural resize games.",
            "Primary write path for ctb_base.bin. Safe v1 scope: edit the existing 255 agility rows only."
        );
    }
    private void MenuItem_MixTable(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new MixTableEditor_Control(),
            "Mix Table",
            "Edit prepare.bin as a fixed Rikku mix matrix: existing item-pair slots only, with proven 16-bit result ids and no table resize.",
            "Writable",
            "Jarvis is treating this as a structured kernel table, not as fuzzy gameplay logic. Both item axes stay fixed; only the result ids are editable.",
            "Primary write path for prepare.bin. Safe v1 scope: preserve the 112x112 matrix and patch existing result entries in place."
        );
    }
    private void MenuItem_AutoAbilities(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new AutoAbilityEditor_Control(),
            "Auto-Abilities",
            "Edit a_ability.bin auto-ability data (elements, statuses, stat boosts, auto-statuses, flags, icon/group) plus the aligned arms_rate.bin gil sidecar.",
            "Editor",
            "Structural data writer behind an RT0 self-check: read->write must reproduce a_ability.bin + arms_rate.bin byte-for-byte before editing unlocks. Undo/Discard follow the screen (Discard restores the as-loaded original).",
            "Edits write byte-locally to a_ability.bin (new_uspc) and arms_rate.bin (jppc). Field semantics are partly proven; the gameplay effect of an edit is your experiment (RT2)."
        );
    }
    private void MenuItem_KeyItems(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new KeyItemEditor_Control(),
            "Key Items",
            "Inspect the real important.bin text-prefix layout and preserved payload tail while production keeps mutation work in the lab backlog.",
            "Guarded",
            "Pt21 proved reader closure and byte-identical no-edit rebuilds. Pt22 measured candidate tail ranges, but nothing here is promoted as a public mutation-safe writer.",
            "Production slice today: reader + no-edit guard for important.bin. Candidate tail mutations remain backlog-only until a narrower feature-flag validation exists."
        );
    }
    private void MenuItem_MonsterMagic1(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        _currentModuleId = "battle-commands-hub";
        PushCurrentSnapshotForNavigation(new NavigationSnapshot("battle-commands-hub"));
        ShowKernelCommands(CommandFile_enum.MonMagic1);
    }
    private void MenuItem_MonsterMagic2(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        _currentModuleId = "battle-commands-hub";
        PushCurrentSnapshotForNavigation(new NavigationSnapshot("battle-commands-hub"));
        ShowKernelCommands(CommandFile_enum.MonMagic2);
    }
    private void MenuItem_MonsterAiExplorer(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new MonsterAiExplorer_Control(),
            "Monster AI Explorer",
            "Inspect parser-corpus monster AI blocks: script code, workers, variables, forced action, ability list, loot context, and localized strings before any safe patcher touches behavior.",
            "Explorer",
            "This is the safe front door into AI work. Jarvis is exposing the decompiled/static surface first so runtime proof can come before any script patching fantasy.",
            "Read-heavy today. Use this together with Live Battle Lab to correlate live behavior against the static monsterAiOutput corpus."
        );
    }
    private void MenuItem_MonsterAiEditor(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new MonsterAiEditor_Control(),
            "Monster AI Editor",
            "Read AND EDIT the real monster AI: reads the AiFile of each monster_*.bin, decodes it with the in-repo ATEL codec (opcode table + worker/jump-table proven over the corpus and confirmed in IDA), and lets you change operands or restructure the script.",
            "Editor",
            "Jarvis cracked the ATEL VM natively (FFX_Atel_FetchOpcode/InterpretWorkerOpcodes/DispatchNativeCall). Shows workers, entrypoints, jump-tables and the decoded listing — own reading of the AI, no external decompiler. BIBLE OF SPIRA adds read-only contextual guidance over the real dictionaries.",
            Strings.F2_you_can_edit_3_paths_all_write_monster_b_22ab204f
        );
    }
    private void MenuItem_CustomBossCreator(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new CustomBossCreator_Control(),
            "Custom Boss Creator",
            "Clone an existing m###.bin, override stats/name/ModelId, and save as a new monster slot. The result can be placed in battles via the Formation Editor.",
            "Writable",
            "Clone-and-edit: reads the source Monster_File (Read→Write round-trip proven), stamps only the overridden fields (stat-block slot-only or full rebuild if name changed), writes to battle/mon/_m###/m###.bin. Registers the boss name in Monster_Dictionary at runtime so Formation Editor shows a useful label.",
            "Tier 0 (offline): creates the m###.bin. Placing it in battles requires Formation Editor (existing .btl slot swap). RT2 in-game = same path as Formation Editor."
        );
    }

    private void MenuItem_DifficultyDirector(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new DifficultyDirector_Control(),
            "Difficulty Director",
            "Scale HP, MP, and monster stats across every m###.bin in the loaded project with presets and a top-impact preview.",
            "Writable",
            "Offline writer only: backs up each monster as .difficulty.bak before the first scale pass, then rewrites the existing Monster_File shape through the proven serializer.",
            "Applies to jppc/battle/mon monster files. The running game sees the change on the next monster load; live stat scaling remains future probe work."
        );
    }

    private void MenuItem_BattleExplorer(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        BattleExplorer_Control battleExplorer = new();
        battleExplorer.OpenMonsterRequested += BattleExplorer_OpenMonsterRequested;

        SetModule(
            battleExplorer,
            "Battle Explorer",
            "Inspect battle files as real multi-chunk assets: ATEL script, worker mapping, formation, positions, and encounter-table footprint.",
            "Explorer",
            "This is the first serious read-heavy bridge into encounter work. Safe focus: inspect structures, confirm references, and prepare the future formation editor.",
            "Read-heavy today. Formation editing comes next; full encounter routing and ATEL authoring stay controlled."
        );
    }
    private void MenuItem_SphereGridBuilder(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // 🧩 SPHERE GRID BUILDER (v1, from scratch) — surfaces the proven SphereGridLayoutBuilder/WriteLayout in the UI.
        // No project gate: it builds a grid from scratch (saves to the project root if loaded, else the exe dir).
        SetModule(
            new FFXProjectEditor.Modules.SphereGridBuilder.SphereGridBuilder_Control(),
            "Sphere Grid Builder",
            Strings.F2_create_a_sphere_grid_layout_topology_fro_8bfa5717,
            "Builder (v1)",
            Strings.F2_v1_form_list_based_no_visual_canvas_yet__f60fb7f1,
            Strings.F2_topology_authoring_edit_existing_node_va_2a2a2e3d
        );
    }
    private void MenuItem_MagicDllEditor(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // 🧪 MAGIC DLL EDITOR (Jarvis-PPP-C2C3 2026-08-01) — lane PPP Assembler (C2/C3 → editor).
        // Abre qualquer magic_XXXX.dll com estruturas declaradas (Root → Descriptors → Programs →
        // Slots → Fields), edita campos existentes e adiciona campos novos (grow pointer-trust).
        // Sem project gate: fluxo abre DLL arbitrária e salva SEMPRE em cópia escolhida pelo usuário.
        SetModule(
            new FFXProjectEditor.Modules.MagicDllEditor.MagicDllEditor_Control(),
            "Magic DLL Editor",
            Strings.F2_open_any_magic_xxxx_dll_with_all_structu_8798e48d,
            "Writer (byte-safe)",
            Strings.U_Mw_MagicDllNotes,
            Strings.F2_writable_magic_xxxx_dll_user_chosen_copy_1d5639c2
        );
    }
    private void MenuItem_SphereGridCanvas(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // 🧩 SPHERE GRID BUILDER v2 — visual canvas: LOAD Original/Standard/Expert (FromExisting) or start New,
        // drag nodes / draw links / edit content on a graph, save byte-safe (Build -> WriteLayout). No project gate
        // (loading falls back to the extracted abmap reference); saving INTO the project needs a loaded project.
        SetModule(
            new FFXProjectEditor.Modules.SphereGridBuilder.SphereGridCanvas_Control(),
            "Sphere Grid Canvas",
            Strings.F2_visual_sphere_grid_editor_open_original__9dd2c774,
            "Canvas (v2)",
            Strings.F2_visual_canvas_edit_existing_grids_fromex_3f1e5de3,
            Strings.F2_topology_authoring_editing_edit_sphere_v_c261b507
        );
    }
    private void MenuItem_SphereGridExplorer(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new SphereGridExplorer_Control(),
            "Sphere Grid Explorer",
            "Inspect sphere types, node types, and the Original / Standard / Expert grid layouts as real decoded structures instead of loose dat files.",
            "Explorer",
            "This is the first serious bridge into Sphere Grid authoring. Read-heavy scope for now: decode localized sphere and panel tables, prove the layout graph, and prepare the write path after the structures are fully trusted.",
            "Reads sphere.bin, panel.bin, and abmap dat01/02/03 + dat09/10/11 with English-first display and Japanese fallback."
        );
    }
    private void MenuItem_EncounterTableExplorer(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        EncounterTableExplorer_Control explorer = new();
        explorer.RequestOpenAuroraChamberForMap += OpenAuroraChamberForMap;  // double-click a table -> render its scene in Aurora
        SetModule(
            explorer,
            "Encounter Table Explorer",
            "Inspect battle/kernel/btl.bin as the routing table between map buckets, group danger, battlefield selection, and concrete battle ids. Double-click a table to open its scene in the Aurora Chamber.",
            "Explorer",
            "This is the clean bridge into random encounter routing. Jarvis is exposing the proven read path first: maps, groups, battlefields, danger values, and weights before any writer touches this file.",
            "Read-heavy today. Proven surface: tables, groups, formation weights, and battle-id footprint. No encounter-table save path exposed yet."
        );
    }

    // Cross-module bridge: EncounterTable double-click -> open the Aurora Chamber rendered at that encounter's map (e.g. "bika02").
    private void OpenAuroraChamberForMap(string mapKey)
    {
        var dm = OpenAuroraChamber();
        if (dm != null)
            dm.SelectSceneByMapKey(mapKey);
    }
    // 🌅 AURORA CHAMBER — abre o control do Chamber (catálogo BIANCA + MapViewer + Abrir RealGame).
    // Este é o "Aurora" de verdade; o battle preview (noclip) é LEGADO (item 3 do plano).
    private FFXProjectEditor.Modules.AuroraChamber.AuroraChamber_DataModel? OpenAuroraChamber()
    {
        FFXProjectEditor.Modules.AuroraChamber.AuroraChamber_Control aurora = new();
        SetModule(
            aurora,
            "Aurora Chamber",
            Strings.F2_connects_bianca_catalog_of_25_btlmap_sce_05d19d5b,
            "Battle preview / placement",
            Strings.F2_opened_via_the_aurora_menu_or_double_cli_8183e73e,
            Strings.F2_offline_read_only_scene_picker_render_ru_fead46f0
        );
        return aurora.DataContext as FFXProjectEditor.Modules.AuroraChamber.AuroraChamber_DataModel;
    }
    private void MenuItem_SpiraForgeHub(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // SPIRA FORGE Fase 0 — sem gate de projeto: o field picker roda 100% offline do bridge CSV.
        SetModule(
            new SpiraForgeHub_Control(),
            "Spira Forge · Field Hub",
            Strings.U_Mw_FieldContextDesc,
            "Read-Only Hub",
            Strings.U_Mw_FieldContextNotes,
            Strings.F2_100_offline_read_only_field_picker_works_cdcaf198
        );
    }
    private void MenuItem_AuroraChamber(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // 🌅 AURORA CHAMBER — Wave 2 head. Read-only/additive: consumes 🌙 BIANCA (scene catalog) + the MapViewer
        // (renderer) and lifts chunk3 actor coordinates. No project gate — the scene picker + render run offline.
        // Este é o "Aurora" de verdade (o battle preview noclip é legado).
        OpenAuroraChamber();
    }

    private void MenuItem_Aurora(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // "Aurora" no catálogo = o AURORA CHAMBER (o battle preview noclip é código legado — item 3 do plano).
        OpenAuroraChamber();
    }
    private void MenuItem_AuroraFieldExplorer(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var explorer = new FFXProjectEditor.Modules.AuroraFieldExplorer.AuroraFieldExplorer_Control();
        explorer.RequestOpenAuroraChamberForMap += OpenAuroraChamberForMap;
        SetModule(
            explorer,
            "Aurora Field Explorer",
            Strings.U_Mw_FieldExplorerDesc,
            "Read-Only Explorer",
            Strings.U_Mw_FieldExplorerNotes,
            Strings.U_Mw_FieldExplorerScope
        );
    }
    private void MenuItem_FormationEditor(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new FormationEditor_Control(),
            "Spira Forge · Formation Editor",
            Strings.U_Mw_FormationEditorDesc,
            "Writable",
            Strings.U_Mw_FormationEditorNotes,
            Strings.U_Mw_FormationEditorScope
        );
    }
    private void MenuItem_ShopExplorer(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new ShopExplorer_Control(),
            "Shop Explorer",
            "Inspect item_shop.bin and arms_shop.bin as fixed 47-row tables and edit only the proven slot payload slice.",
            "Guarded",
            "Guarded writer: only the proven 16-slot payload slice is editable. Word 00h, row count, row order, and every non-slot byte stay read-only.",
            "Item rows edit as Item or Empty. Gear rows edit as Catalog Row or Empty when the local shop_arms.bin bridge is present."
        );
    }
    private void MenuItem_StringExplorer(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new StringExplorer_Control(),
            "String Explorer",
            "Decode curated string-table sources and monster localization binaries, bringing READ_STRING_FILE and READ_MONSTER_LOCALIZATIONS into the shell.",
            "Explorer",
            "Read-heavy by design. This is the safest bridge into text-layer work because it proves sources, offsets, and decoded content before write paths are exposed.",
            "Curated text domains only: menu/status/config/summon/misc text tables plus monster1/2/3 localizations."
        );
    }
    private void MenuItem_MacroExplorer(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new MacroExplorer_Control(),
            "Macro Explorer",
            "Inspect menu macro dictionaries such as area-name lookups and reusable text fragments, mirroring the parser's READ_MACROS mode.",
            "Explorer",
            "Read-heavy scope: decode and inspect macro dictionaries first, then graduate to controlled text authoring once macro coverage is proven.",
            "US and JP macro dictionaries when present. No write path exposed yet."
        );
    }
    private void MenuItem_WeaponNameExplorer(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new WeaponNameExplorer_Control(),
            "Weapon Name Explorer",
            "Edit the per-character weapon/armor name strings in w_name.bin (regular + simplified), with the equipment model word shown for context.",
            "Editor",
            "Safe text writer: the string pool + offset table rebuild automatically while keys, model words and the final word are preserved verbatim. RT0 + edit-locality proven on the corpus.",
            "US weapon names (new_uspc/battle/kernel/w_name.bin). Model swaps and key semantics stay read-only."
        );
    }
    private void MenuItem_BattleTextExplorer(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new BattleTextExplorer_Control(),
            "Battle Text Explorer",
            "Edit battle text lines in btl_txt.bin. Control codes appear as reversible <Cn> tokens so formatting survives a re-encode.",
            "Editor",
            "Append-only safe writer: the original overlapping string pool is preserved byte-for-byte and only edited lines are appended. RT0 byte-identity + RT1 locality proven on the corpus.",
            "US battle text (new_uspc/battle/kernel/btl_txt.bin). 1-byte markers/empties stay read-only and preserved."
        );
    }
    private void MenuItem_EventExplorer(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new EventExplorer_Control(),
            "Event Explorer",
            "Inspect event .ebp files structurally and, when the parser corpus is available, surface decompiled script previews inline.",
            "Explorer",
            "This is the controlled PARSE_EVENT bridge: file structure and decoded text from the workspace, script preview from parser corpora when available.",
            "Read-heavy today. No event recompilation or generalized ATEL editing exposed yet."
        );
    }
    private void MenuItem_Ps2KnowledgeHub(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new Ps2KnowledgeHub_Control(),
            "Extras / PS2 Knowledge",
            "Expose the PS2 campaign as a read-only product shell: source roots, family registry, provenance, sensitivity, and shared Extras contracts before decoder or writer claims land.",
            "Read-Only Hub",
            "This is the first real Extras surface. Jarvis is keeping it family-first, provenance-heavy, and brutally honest about what is structural, blocked, or not ready for promotion.",
            "Read-only only. Companion roots: master, ffx_ps2, ps3data. No decoder-final authority, no playback claims, no writer-safe promotion."
        );
    }
    // === "???" category — remaining Wave-1 families with a proven byte-safe reader+writer+RT0 gate but no dedicated editor ===
    private void MenuItem_UnwiredAlBhed(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new AlBhedDictionaryEditor_Control(),
            "Texto / Al Bhed Dictionary",
            "Edit US Latin and JP kana glyph mappings in albheddic.bin with byte-safe undo/save.",
            "Writer",
            "Safe scope: mapped glyph and group bucket on existing rows. Table shape and source bytes stay fixed.",
            "WRITABLE · menu/albheddic.bin (US + JP locales)."
        );
    }
    private void MenuItem_UnwiredPointerScript(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => ShowUnwired(FFXProjectEditor.Modules.Unwired.UnwiredFamily.PointerScriptTable);
    private void ShowUnwired(FFXProjectEditor.Modules.Unwired.UnwiredFamily family)
    {
        SetModule(
            new FFXProjectEditor.Modules.Unwired.UnwiredCatalog_Control(family),
            "??? / Wave-1 sem editor",
            Strings.U_Mw_Wave1Desc,
            "??? (read-only)",
            Strings.U_Mw_Wave1Notes,
            Strings.U_Mw_Wave1Scope
        );
    }
    private void MenuItem_ThunderPlains(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new ThunderPlainsEditor_Control(),
            "Thunder Plains / Lightning Dodge",
            "Edit the consecutive-dodge and total-bolt thresholds for the lightning minigame.",
            "Writable EBP patcher",
            "Patches kami0000.ebp and kami0300.ebp bytes directly. Values 1-999 (byte-capped at 255). Always backs up to .bak on save.",
            "EBP byte patcher for event/obj/ka/kami0000/kami0000.ebp and /kami0300/kami0300.ebp."
        );
    }

    private void MenuItem_TreasureMap(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new TreasureMapEditor_Control(),
            "Treasure Map",
            "Browse and edit the treasure chest catalog across every field, with real rewards from takara.bin and a guide-map overlay (positions when recoverable, bounds fallback otherwise).",
            "Writer (takara.bin)",
            "Scans field events (ATEL) for obtainTreasure to list confirmed chests per area with their real reward from takara.bin. Chest positions come from ATEL setPosition constants when available; otherwise a bounds placeholder is drawn. Saves via TreasureCatalogSaveTransaction with atomic backup.",
            "WRITABLE · jppc/battle/kernel/takara.bin + read-only event/map scan."
        );
    }
    private void MenuItem_AiAssistant(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetModule(
            new AiAssistant_Control(),
            "AI Assistant",
            "BYOK AI coding assistant. Connect your own LLM endpoint (API key in memory, zeroized on disconnect). Returns PatchProposal — nothing is written without a proven recipe + human approval.",
            "AI Assistant (BYOK)",
            "Bring-your-own-key: enter your own API key (memory only). Connects to any OpenAI-compatible endpoint. Remote endpoints require human confirmation. Proposal protocol (P2B).",
            "AGENT · LLM-powered proposal protocol. No direct file writes from this panel."
        );
    }



    private void MenuItem_TextureFamilyBrowser(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new TextureFamilyBrowser_Control(),
            "Extras / Textures (TM2)",
            "Inspect the first honest visual Extras lane: TIM2 inventory, provenance, preview state, and read-only preview for compatible indexed cohorts.",
            "Read-Only Preview",
            "Jarvis is starting with the Pt56 quick win on purpose. This surface is for preview, metadata, and guardrails, not for pretending the whole texture family is solved.",
            "Read-only only. Native preview is limited to the proved indexed TIM2 cohort; experimental, metadata-only, and blocked states stay explicit."
        );
    }
    private void MenuItem_BinFtcReadonlyBrowser(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new BinFtcReadonlyBrowser_Control(),
            "Extras / BIN-FTC Atlas",
            "Inspect the PS2 master `.bin/.ftc` forest as buckets, sidecar pairs, FTC lanes, and sensitivity zones instead of pretending it is one solved format.",
            "Read-Only Atlas",
            "Jarvis is only exposing the honest P0 lane here: overview, buckets, first64 dwords, FTC header lane, paired sidecars, and hard read-only warnings.",
            "Read-only only. No parser-final claim, no writer, no repack, no semantic decoder promotion."
        );
    }
    private void MenuItem_ProjectPipelineExplorer(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new ProjectPipelineExplorer_Control(),
            "Extras / Project / Pipeline",
            "Inspect proj, cdrom.*, eiichi_abmap_data, and ABMap lineage as build/index/pipeline surfaces instead of pretending those files are final runtime assets.",
            "Read-Only Pipeline",
            "Jarvis is exposing the honest Pt54 cut here: cdrom triplets, descriptor lanes, ABMap support inventory, graph edges, and hard provenance warnings.",
            "Read-only only. PIPELINE / NOT FINAL ASSET / DO NOT WRITE stay explicit. This is for lineage, descriptors, and support carriers, not for patching or repacking."
        );
    }
    private void MenuItem_MagicEffectBrowser(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new MagicEffectBrowser_Control(),
            "Extras / Magic Effects",
            "Inspect the battle/kernel lane, mag_* packages, bat_eff presentation lane, and the still-blocked causal bridge between them without pretending magic construction is solved.",
            "Read-Only Crosswalk",
            "This is the honest Pt67 cut: kernel-side, package-side, and presentation-side context in one place, with the blocked kernel -> mag_* -> bat_eff join kept explicit.",
            "Read-only only. No magic constructor, no writer, no color-override promotion, and no runtime-consumer proof claim."
        );
    }
    private void MenuItem_Ps3MagicBrowser(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        ShowPs3MagicBrowser();
    }

    private void ShowPs3MagicBrowser(int? initialMagicId = null, bool playConfirmFx = true)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new Ps3MagicBrowser_Control(initialMagicId),
            "Extras / PS3 Magic (HD)",
            "Browse the HD remaster magic-effect textures (ps3data\\magic), preview decoded .dds.phyre surfaces, extract DDS, import same-shape DDS/raw mip0 and recolor cloned effects.",
            "Texture I/O LAB",
            "Jarvis is surfacing the ps3data magic tree as honest texture authoring: mip0 decode/repack for ARGB8 / DXT1 / DXT3 / DXT5 / L8, with unknown classes kept metadata-only.",
            initialMagicId.HasValue
                ? $"Writable only in the narrow mip0/same-shape path. Focused jump from Battle Commands animation id {initialMagicId.Value:D4}; no mip-chain authoring, atlas reassembly, timeline/compiler, or model/animation playback claim."
                : "Writable only in the narrow mip0/same-shape path. No mip-chain authoring, atlas reassembly, timeline/compiler, or model/animation playback claim.",
            playConfirmFx
        );
    }

    private void MenuItem_MagicDllBrowser(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        MagicDllBrowser_Control control = new();
        control.OpenPs3MagicRequested += magicId => ShowPs3MagicBrowser(magicId, playConfirmFx: false);
        control.OpenMagicViewerRequested += magicId => ShowMagicViewer(magicId, playConfirmFx: false);
        control.OpenPhyrePackageRequested += path => ShowPhyrePackageIo(path, playConfirmFx: false);
        SetModule(
            control,
            "Extras / Magic DLLs (FFX)",
            "Inspect and author the native magicFiles\\FFX magic_####.dll lane: PE sections, imports, exports, strings, overlay-table evidence, byte-preserving rebuild, patch plans, clone-to-ID, and generated C/ASM rebuild projects.",
            "Native DLL LAB",
            "Jarvis is exposing the missing runtime half of HD magic effects. The safe path is byte-preserving compile plus controlled byte/string/ASM patches; full C behavior requires manual native authoring over the generated harness.",
            "Writes are explicit only: clone, byte-identical repack, patch plan, or external C/ASM rebuild. Automatic perfect C decompilation is not claimed."
        );
    }

    private void ShowMagicViewer(int? magicId, bool playConfirmFx)
    {
        // 🐉 ViewerHub F2: Magic Viewer servido pelo servidor in-process (prefixo /magic/), embed no shell.
        string? query = magicId.HasValue ? $"magic={magicId.Value:D4}" : null;
        SetModule(
            new FFXProjectEditor.Modules.Common.ViewerShell.ViewerShell_Control("magic-viewer", query),
            "Magic Viewer (DLLs PS3)",
            Strings.U_Mw_MagicRuntimeDesc,
            "Embedded 3D Viewer",
            magicId.HasValue
                ? string.Format(Strings.U_Mw_DeepLinkMagic, magicId.Value)
                : Strings.U_Mw_MagicRuntimeNotes,
            Strings.U_Mw_MagicBrowserDesc,
            playConfirmFx
        );
    }

    private void ShowPhyrePackageIo(string? initialSourcePath, bool playConfirmFx)
    {
        SetModule(
            new PhyrePackageIo_Control(initialSourcePath),
            "Extras / Phyre Package I/O",
            "Import, extract and safely stage PhyreEngine packages used by FFX HD: .dds.phyre textures, .dae.phyre model packages, .ags.phyre animation carriers and .fx.phyre shader blobs.",
            "Native Phyre I/O LAB",
            "DDS is native same-shape payload I/O: extract DDS and import DDS/raw mip0 into the existing container. DAE/AGS/FX are protected compiled-package imports: inspect, manifest, backup, and replace with an already-built .phyre package.",
            string.IsNullOrWhiteSpace(initialSourcePath)
                ? "This does not yet compile glTF/FBX/DAE source into .dae.phyre. Whole model compilation remains the Phyre mesh compiler frontier; this UI exposes the safe package I/O path directly."
                : $"Opened from Magic DLL bridge: {initialSourcePath}",
            playConfirmFx
        );
    }

    private void MenuItem_PhyrePackageIo(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ShowPhyrePackageIo(null, playConfirmFx: true);
    }

    private void MenuItem_VbfExtract(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetModule(
            new VbfExtract_Control(),
            "Extras / VBF Extract",
            "Extract the user's own FFX/FFX-2 HD VBF archives into a local source tree so the editor's PS2/PS3 asset browsers can work without a separate manual tool step.",
            "Extract Only",
            "Jarvis is wrapping the local VBFExtract tool as a preparation step: read original archive, write extracted files, then keep runtime modding on loose files through the existing hook/external loader.",
            "No VBF repack path is exposed here. Repacking stays research/lab-only until separately proved and intentionally requested."
        );
    }

    private void MenuItem_MagicViewerWeb(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ShowMagicViewer(null, playConfirmFx: true);
    }
    private void MenuItem_ModelViewerWeb(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // 🐉 MODEL VIEWER (HD) — ViewerHub F2: servidor in-process (prefixo /model/ + /work/), embed no shell.
        SetModule(
            new FFXProjectEditor.Modules.Common.ViewerShell.ViewerShell_Control("model-viewer"),
            "Model Viewer (HD)",
            Strings.F2_3d_hd_gallery_runtimetools_ffxmodelviewe_cc3eb58c,
            "Embedded 3D Viewer",
            Strings.F2_viewerhub_studiowebserver_serves_model_r_255f95d1,
            Strings.U_Mw_ModelViewerDesc
        );
    }
    private void MenuItem_MonsterStudioWeb(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // 🐉 MONSTER STUDIO — ViewerHub: servidor in-process (1 porta 8769, host virtual noclip.localhost)
        // + ViewerShell embed. Nada de browser: o FFX Mod Studio É o navegador (decisão 2026-08-02).
        SetModule(
            new FFXProjectEditor.Modules.Common.ViewerShell.ViewerShell_Control("monster-studio"),
            "Monster Studio (PS2 3D)",
            Strings.F2_view_ps2_monster_models_in_idle_walk_run_5e2a9d9b,
            "Embedded 3D Viewer",
            Strings.F2_viewerhub_studiowebserver_127_0_0_1_8769_3eedebc7,
            Strings.F2_read_only_3d_viewer_requires_noclip_webs_ff04bbff
        );
    }

    private void MenuItem_MagicStudioWeb(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // 🐉 MAGIC STUDIO — TODAS as 372 magias num dropdown com filtro, no ViewerShell embed.
        SetModule(
            new FFXProjectEditor.Modules.Common.ViewerShell.ViewerShell_Control("magic-studio"),
            "Magic Studio (372 magias)",
            Strings.F2_view_any_in_game_magic_dropdown_with_all_e550de1a,
            "Embedded 3D Viewer",
            Strings.F2_viewerhub_same_in_process_server_as_mons_9b6537b7,
            Strings.F2_read_only_3d_viewer_requires_noclip_webs_ff04bbff
        );
    }
    private void MenuItem_ModelViewerEmbedded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // 🐉 MODEL VIEWER (EMBEDDED) — mesma galeria do Model Viewer, no ViewerShell (ViewerHub).
        MenuItem_ModelViewerWeb(sender, e);
    }
    private void MenuItem_MapSceneEditorEmbedded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // 🗺️ MAP SCENE EDITOR — ViewerHub F2: servidor in-process (prefixo /map/), embed no shell.
        SetModule(
            new FFXProjectEditor.Modules.Common.ViewerShell.ViewerShell_Control("map-scene-editor"),
            "Map Scene Editor",
            Strings.F2_edit_the_map_scene_runtimetools_ffxmapvi_12a6f2ed,
            "Embedded Scene Lab",
            Strings.F2_viewerhub_studiowebserver_serves_map_run_ca1a7cbd,
            Strings.F2_editable_lab_sidecar_without_re_serializ_942c28bf
        );
    }
    private void MenuItem_Ps2RsdModelBrowser(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new Ps2RsdModelBrowser_Control(),
            "Extras / PS2 Models (RSD)",
            "Browse the PS2 RSD model bundles (MatEditor toolchain) from ffx_ps2: ASCII manifest @RSD940102 + @PLY940102 mesh counts + @MAT990928 material + .tm2 texture linkage.",
            "Read-Only Structural",
            "Jarvis is surfacing the proved PS2 RSD bundle: PLY geometry counts (V/N/Poly), MAT material count, and TEX -> .tm2 resolution. Structural truth only.",
            "Read-only only. No 3D render (that stays the ModelViewer lane), no vertex/material editing, and no writer."
        );
    }
    private void MenuItem_Ps2AudioBrowser(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new Ps2AudioBrowser_Control(),
            "Extras / PS2 Audio (.wd)",
            "Browse the PS2 .wd sound banks (Square WD header) from ffx_ps2: parse the proved descriptor layout (id / programs / samples + per-sample body offsets and ADSR), then decode PlayStation-ADPCM to WAV through the external vgmstream oracle.",
            "Read-Only Audio",
            "Jarvis is surfacing the proved PS2 WD bank: descriptor inventory validated at 843/843 no-edit byte identity, with optional vgmstream decode/export/play. No codec is reimplemented in-app.",
            "Read-only only. No writer/repack into .wd, no codec reimplementation, and no claim that the ~25 vgmstream-blocked variants are solved. WAV output goes only to a folder you pick."
        );
    }
    private async void Button_SetFfxPs2Root(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        TopLevel? top = TopLevel.GetTopLevel(this);
        if (top == null)
            return;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "Select your EXTRACTED ffx_ps2 folder (the one containing ffx\\yonishi_data)",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            string path = folders[0].Path.LocalPath;
            if (!string.IsNullOrEmpty(path))
            {
                Project_Service.FfxPs2RootOverride = path;
                Project_Service.Instance.NotifyFfxPs2RootChanged();
                if (Project_Service.Instance.IsProjectLoaded)
                    MenuItem_Ps2RsdModelBrowser(sender, e);
            }
        }
    }
    private void MenuItem_PresentationContainerBrowser(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new PresentationContainerBrowser_Control(),
            "Extras / Presentation Containers",
            "Browse .vpa/.ebp/.omd/.sps2 cohorts as read-only container families with signature, companion, and cohort evidence before any deep decoder claims land.",
            "Read-Only Containers",
            "This is the honest Pt57 cut: strong cohorts, useful signatures, and cold structural navigation without pretending map/event/presentation semantics are fully solved.",
            "Read-only only. No map renderer, no event compiler, no timeline decoder, and no container writer."
        );
    }
    private void MenuItem_BattleCorpusCrosswalkExplorer(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        SetModule(
            new BattleCorpusCrosswalkExplorer_Control(),
            "Extras / Battle Corpus Crosswalk",
            "Crosswalk battleId -> formation slots 0..7 -> actor rows 0..10 -> rawMonsterId -> parser corpus overlay without promoting watchlist rows into composition truth.",
            "Read-Only Crosswalk",
            "This is the honest Pt44 cut: battle composition, encounter references, and corpus overlay in one cold surface, with rows 8..10 kept as runtime watchlist only.",
            "Read-only only. No owner/target/runtime claims, no formation writer, and no dispatch promotion."
        );
    }
    private void MenuItem_DebugMenu(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetModule(
            new DebugMenu_Control(),
            "Debug Menu",
            "Live runtime toggles and save-adjacent debugging helpers for rapid inspection work against the running game.",
            "Runtime",
            "Useful for fast experiments, but runtime editing should not replace file-authored workflows.",
            "Live-game helper. Not the canonical save path."
        );
    }
    private void MenuItem_RuntimeDllManager(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetModule(
            new RuntimeDllManager_Control(),
            "Injected DLLs",
            "Show which runtime DLLs are installed, disabled, or currently visible in FFX, then stage on/off changes for the next game boot.",
            "Runtime Switchboard",
            "Jarvis is keeping this honest: file toggles arm the loader by renaming/copying DLLs; already loaded code stays alive until FFX restarts.",
            "Controls game-root proxy DLLs and modules\\*.dll. Runtime proof comes from process modules plus FFXProbeBlock_v1 / FFXHooksBlock_v1 where available."
        );
    }
    private void MenuItem_LiveBattleLab(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetModule(
            new LiveBattleLab_Control(),
            "Live Battle Lab",
            "Inspect the live BTL runtime, reload proven file domains straight into memory, and prepare force-battle work without guessing at ATEL or routing writes.",
            "Runtime Lab",
            "Jarvis is only exposing the pieces already proven by this codebase: battle-state inspection, enemy runtime snapshots, in-memory reload of known buffers, and BTL debug flags. Force Battle stays experimental until its trigger path is genuinely trusted.",
            "Read-heavy plus proven runtime writes. Good for rapid test loops against Commands, Items, MonMagic, Auto-Abilities, Customizations, and Aeon Grow."
        );
    }
    private void MenuItem_AuroraOverlayLab(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetModule(
            new AuroraOverlayLab_Control(),
            "Aurora Overlay Lab",
            "Configure the ffx-hooks.dll Aurora W2S/texture overlay from the editor, writing persistent modules\\config flags and aurora_overlay.ini for the next game boot.",
            "Runtime Product Lab",
            "This productizes the proved W2S/D3D11 overlay as an operable switchboard. It does not rename the native renderer or promote the IDA owner candidates beyond their current evidence.",
            "Writes only module-loader config files under the game modules folder. The running DLL reads these on startup; restart the game after changing the config."
        );
    }
    private void MenuItem_BattleTracker(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetModule(
            new BattleTracker_Control(),
            "Battle Tracker",
            "Inspect live battle-state data while the game is running and compare runtime values against file-authored expectations.",
            "Runtime",
            "Best used as telemetry and verification, not as the foundation of the authoring pipeline.",
            "Live-game helper. Read-heavy."
        );
    }
    private void MenuItem_InventoryTracker(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetModule(
            new InventoryTracker_Control(),
            "Inventory Tracker",
            "Inspect runtime inventory state and quickly validate item-table effects or progression-related test cases.",
            "Runtime",
            "Good for debug loops and validating kernel-side edits.",
            "Live-game helper. Read-heavy."
        );
    }
    private void MenuItem_ArenaTracker(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetModule(
            new ArenaTracker_Control(),
            "Arena Tracker",
            "Track monster arena progress and runtime state during real gameplay sessions for research and verification.",
            "Runtime",
            "Useful as an operational panel while authoring monster and encounter changes elsewhere.",
            "Live-game helper. Read-heavy."
        );
    }


    private void ShowHome(bool playConfirmFx = false)
    {
        if (playConfirmFx)
        {
            AudioStudio_Service.Instance.PlayConfirm();
        }

        DataModel.ShowHome();
        var dashboard = new MainDashboard_Control
        {
            DataContext = DataModel
        };
        dashboard.QuickLaunchRequested += OnQuickLaunchRequested;
        ContentFrame.Content = dashboard;
        RegisterHoverAudioHandlers();
        Core.EditorContextHub.ReportModule("home");
        // Same DebugLog scope switch as SetModule — ShowHome bypasses it, so without
        // this "home" would never activate and dashboard logs (Home.*) stay mute.
        if (!string.IsNullOrEmpty(_lastActiveModuleId))
            FFXProjectEditor.Diagnostics.DebugLog.Deactivate(_lastActiveModuleId);
        _currentModuleId = "home";
        FFXProjectEditor.Diagnostics.DebugLog.Activate(_currentModuleId);
        _lastActiveModuleId = _currentModuleId;
    }

    // ===== v2.160.0.0 (Jarvis-UI §16–§17): roteamento Id → handler.
    // Antes (6 keys hardcoded: monster/kernel/sphere/save/extras/home). Agora: todos os Ids do
    // ModuleRegistry. Rail inline, drawer narrow e dashboard passam pelo MESMO Dispatch — uma fonte.
    private void OnQuickLaunchRequested(string moduleId) => Dispatch(moduleId);

    private void Dispatch(string moduleId)
    {
        // v2.160.1.0 (Fase D): todo tráfego de navegação (rail/drawer/dashboard/restore) passa por aqui.
        // D4 2026-09-13: a autoridade de roteamento é a tabela tipada DispatchRoutes — a UI e os
        // testes compartilham exatamente a mesma fonte de verdade (sem switch duplicável).
        if (!ModuleDispatchRoute.IsKnownPublicOrInternal(moduleId)
            || !DispatchRoutes.TryGetValue(moduleId, out ModuleDispatchRoute? route))
        {
            FFXProjectEditor.Diagnostics.DebugLog.Error(
                "Main.Dispatch", $"Rejected unknown module id: '{moduleId}'.");
            return;
        }

        // AI Tools (Jarvis-MAGIC-IA) — opt-in module: gated at the dispatcher too,
        // so a stale palette/rail entry can't open it while the flag is off.
        if (moduleId == Core.AiFeatureGate.ModuleId && !Core.AiFeatureGate.Enabled)
            return;

        // WHY: the active identity must describe content that can actually be
        // created; assigning it before lookup corrupted navigation snapshots.
        _currentModuleId = moduleId;
        route.Invoke(this);
    }

    // ===== §D4: autoridade tipada de dispatch (tabela única UI+testes). =====
    internal static readonly IReadOnlyDictionary<string, ModuleDispatchRoute> DispatchRoutes =
        BuildDispatchRoutes();

    private static IReadOnlyDictionary<string, ModuleDispatchRoute> BuildDispatchRoutes()
    {
        var handlers = new Dictionary<string, Action<Main_Window>>(StringComparer.Ordinal)
        {
            ["home"] = static w => w.ShowHome(true),
            ["open_workspace"] = static w => w.Button_ProjectPath(null, new RoutedEventArgs()),
            ["monster-editor"] = static w => w.MenuItem_MonsterEditor(null, new RoutedEventArgs()),
            ["magic-dll-editor"] = static w => w.MenuItem_MagicDllEditor(null, new RoutedEventArgs()),
            ["battle-commands-hub"] = static w => w.MenuItem_BattleCommandsHub(null, new RoutedEventArgs()),
            ["items-hub"] = static w => w.MenuItem_ItemsHub(null, new RoutedEventArgs()),
            ["customizations-hub"] = static w => w.MenuItem_CustomizationsHub(null, new RoutedEventArgs()),
            ["stats-hub"] = static w => w.MenuItem_StatsHub(null, new RoutedEventArgs()),
            ["enemy-design-hub"] = static w => w.MenuItem_EnemyDesignHub(null, new RoutedEventArgs()),
            ["sphere-grid-hub"] = static w => w.MenuItem_SphereGridHub(null, new RoutedEventArgs()),
            ["text-hub"] = static w => w.MenuItem_TextHub(null, new RoutedEventArgs()),
            ["blitzball"] = static w => w.MenuItem_Blitzball(null, new RoutedEventArgs()),
            ["save-editor"] = static w => w.MenuItem_SaveEditor(null, new RoutedEventArgs()),
            ["encounters-hub"] = static w => w.MenuItem_EncountersHub(null, new RoutedEventArgs()),
            ["aurora"] = static w => w.MenuItem_Aurora(null, new RoutedEventArgs()),
            ["aurora-chamber"] = static w => w.MenuItem_AuroraChamber(null, new RoutedEventArgs()),
            ["aurora-field-explorer"] = static w => w.MenuItem_AuroraFieldExplorer(null, new RoutedEventArgs()),
            ["map-scene-editor"] = static w => w.MenuItem_MapSceneEditorEmbedded(null, new RoutedEventArgs()),
            ["battle-explorer"] = static w => w.MenuItem_BattleExplorer(null, new RoutedEventArgs()),
            ["runtime-dll-manager"] = static w => w.MenuItem_RuntimeDllManager(null, new RoutedEventArgs()),
            ["live-battle-lab"] = static w => w.MenuItem_LiveBattleLab(null, new RoutedEventArgs()),
            ["aurora-overlay-lab"] = static w => w.MenuItem_AuroraOverlayLab(null, new RoutedEventArgs()),
            ["battle-tracker"] = static w => w.MenuItem_BattleTracker(null, new RoutedEventArgs()),
            ["inventory-tracker"] = static w => w.MenuItem_InventoryTracker(null, new RoutedEventArgs()),
            ["arena-tracker"] = static w => w.MenuItem_ArenaTracker(null, new RoutedEventArgs()),
            ["debug-menu"] = static w => w.MenuItem_DebugMenu(null, new RoutedEventArgs()),
            ["thunder-plains"] = static w => w.MenuItem_ThunderPlains(null, new RoutedEventArgs()),
            ["textures-tm2"] = static w => w.MenuItem_TextureFamilyBrowser(null, new RoutedEventArgs()),
            ["bin-ftc-atlas"] = static w => w.MenuItem_BinFtcReadonlyBrowser(null, new RoutedEventArgs()),
            ["project-pipeline"] = static w => w.MenuItem_ProjectPipelineExplorer(null, new RoutedEventArgs()),
            ["magic-effects"] = static w => w.MenuItem_MagicEffectBrowser(null, new RoutedEventArgs()),
            ["ps3-magic"] = static w => w.MenuItem_Ps3MagicBrowser(null, new RoutedEventArgs()),
            ["magic-dll-browser"] = static w => w.MenuItem_MagicDllBrowser(null, new RoutedEventArgs()),
            ["phyre-package-io"] = static w => w.MenuItem_PhyrePackageIo(null, new RoutedEventArgs()),
            ["vbf-extract"] = static w => w.MenuItem_VbfExtract(null, new RoutedEventArgs()),
            ["magic-viewer-web"] = static w => w.MenuItem_MagicViewerWeb(null, new RoutedEventArgs()),
            ["monster-studio-web"] = static w => w.MenuItem_MonsterStudioWeb(null, new RoutedEventArgs()),
            ["magic-studio-web"] = static w => w.MenuItem_MagicStudioWeb(null, new RoutedEventArgs()),
            ["model-viewer-web"] = static w => w.MenuItem_ModelViewerWeb(null, new RoutedEventArgs()),
            ["model-viewer-embedded"] = static w => w.MenuItem_ModelViewerEmbedded(null, new RoutedEventArgs()),
            ["ps2-rsd-models"] = static w => w.MenuItem_Ps2RsdModelBrowser(null, new RoutedEventArgs()),
            ["ps2-audio"] = static w => w.MenuItem_Ps2AudioBrowser(null, new RoutedEventArgs()),
            ["presentation-containers"] = static w => w.MenuItem_PresentationContainerBrowser(null, new RoutedEventArgs()),
            ["battle-corpus-crosswalk"] = static w => w.MenuItem_BattleCorpusCrosswalkExplorer(null, new RoutedEventArgs()),
            ["ps2-knowledge"] = static w => w.MenuItem_Ps2KnowledgeHub(null, new RoutedEventArgs()),
            ["treasure-map"] = static w => w.MenuItem_TreasureMap(null, new RoutedEventArgs()),
            ["ai-assistant"] = static w => w.MenuItem_AiAssistant(null, new RoutedEventArgs()),
            ["albhed-dictionary"] = static w => w.MenuItem_UnwiredAlBhed(null, new RoutedEventArgs()),
            ["pointer-script-table"] = static w => w.MenuItem_UnwiredPointerScript(null, new RoutedEventArgs()),
        };

        return handlers.ToDictionary(
            pair => pair.Key,
            pair => new ModuleDispatchRoute(pair.Key, pair.Value),
            StringComparer.Ordinal);
    }

    // ===== §16 Icon Rail / Drawer generation (v2.160.0.0).
    // Ambos os containers são populados a partir do ModuleRegistry: 1 botão por módulo,
    // agrupado por cluster (com separador visual). Os handlers são o MESMO Dispatch(id) —
    // o registry não conhece handlers (pure data), o roteamento vive só aqui.
    private void Button_RailModule_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            // Fecha o drawer narrow se estiver aberto (clique a partir dele).
            if (this.FindControl<Popup>("RailDrawer") is { } drawer && drawer.IsOpen)
                drawer.IsOpen = false;
            Dispatch(id);
        }
    }

    private void BuildIconRail()
    {
        if (this.FindControl<StackPanel>("RailStack") is not { } rail)
            return;

        // Idempotent: cleared so a rebuild after the AI feature gate toggles is safe.
        rail.Children.Clear();

        ModuleCatalogPolicy.ModuleCluster? lastCluster = null;
        foreach (var entry in ModuleRegistry.Public)
        {
            // Separador entre clusters: Border 1px horizontal (não flyout, não submenu).
            if (lastCluster.HasValue && entry.Cluster != lastCluster.Value)
            {
                rail.Children.Add(new Border
                {
                    Height = 1,
                    Margin = new Thickness(0, 4),
                    Background = (IBrush?)Application.Current?.FindResource("PanelStrokeBrush"),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                });
            }
            lastCluster = entry.Cluster;

            rail.Children.Add(MakeModuleRailButton(entry));
        }
    }

    private void BuildRailDrawer()
    {
        if (this.FindControl<WrapPanel>("RailDrawerGrid") is not { } grid)
            return;

        // Idempotent: cleared so a rebuild after the AI feature gate toggles is safe.
        grid.Children.Clear();

        foreach (var entry in ModuleRegistry.Public)
        {
            grid.Children.Add(MakeModuleRailButton(entry));
        }
    }

    // Factory de botão de ícone do rail/drawer. Reaproveitado por ambos: 22px no rail inline,
    // 24px no drawer narrow. PathIcon.Data resolve via DynamicResource (FindResource em runtime),
    // então ícones adicionados depois em StudioIcons.axaml aparecem sem recompilar o factory.
    private Button MakeModuleRailButton(ModuleCatalogPolicy.ModuleCatalogEntry entry)
    {
        var button = new Button
        {
            Classes = { "railIcon" },
            Tag = entry.Id,
        };
        ToolTip.SetTip(button, entry.LocalizedTitle);

        // PathIcon.Data precisa de um Geometry/StreamGeometry. Resolvemos via IconKeyToGeometryConverter.
        // ResolveGeometry, que percorre Resources + MergedDictionaries (StudioIcons.axaml é merged).
        var geometry = FFXProjectEditor.Converters.IconKeyToGeometryConverter.ResolveGeometry(entry.IconKey)
            ?? FFXProjectEditor.Converters.IconKeyToGeometryConverter.ResolveGeometry("IconQuestion")
            ?? new StreamGeometry();
        button.Content = new PathIcon { Data = geometry, Width = 22, Height = 22 };

        // Project gate dinâmico: RequiresProject desabilita sem workspace. Trackers pedem jogo vivo.
        if (entry.RequiresProject)
            button.IsEnabled = Project_Service.Instance.IsProjectLoaded;

        button.Click += Button_RailModule_Click;
        return button;
    }

    // Re-aplica o gate RequiresProject em todos os botões do rail + drawer (workspace carregado/descarregado).
    private void RefreshRailProjectGates()
    {
        bool loaded = Project_Service.Instance.IsProjectLoaded;
        if (this.FindControl<StackPanel>("RailStack") is { } rail)
        {
            foreach (var child in rail.Children)
            {
                if (child is Button btn && btn.Tag is string id && ModuleRegistry.Find(id) is { } entry)
                    btn.IsEnabled = !entry.RequiresProject || loaded;
            }
        }
        if (this.FindControl<WrapPanel>("RailDrawerGrid") is { } drawer)
        {
            foreach (var child in drawer.Children)
            {
                if (child is Button btn && btn.Tag is string id && ModuleRegistry.Find(id) is { } entry)
                    btn.IsEnabled = !entry.RequiresProject || loaded;
            }
        }
    }

    private void SetModule(Control content, string title, string description, string mode, string notes, string scope, bool playConfirmFx = true)
    {
        if (playConfirmFx)
        {
            AudioStudio_Service.Instance.PlayConfirm();
        }

        // v2.162.5.0: overlay ModuleRegistry i18n when navigating via Dispatch (Id already in _currentModuleId).
        if (ModuleRegistry.Find(_currentModuleId) is { } entry)
        {
            title = entry.LocalizedTitle;
            description = entry.LocalizedDescription;
            mode = entry.LocalizedMode;
            notes = entry.LocalizedNotes;
            if (scope == entry.Scope)
                scope = entry.LocalizedScope;
        }

        DataModel.SetModuleInfo(title, description, mode, notes, scope);
        ContentFrame.Content = content;
        RegisterHoverAudioHandlers();
        // EditorContextHub (Jarvis-UI): o agente do AI Assistant injeta o módulo ativo
        // no system prompt — comandos tipo "esse monstro" resolvem pelo contexto.
        Core.EditorContextHub.ReportModule(_currentModuleId ?? "home");
        // 🐉 DEBUG LOG: o escopo ativo da área segue o módulo aberto (a flag 1 precisa saber qual área está
        // sendo manipulada). O _currentModuleId é o módulo NOVO (setado pelo Dispatch antes do SetModule).
        if (!string.IsNullOrEmpty(_lastActiveModuleId))
            FFXProjectEditor.Diagnostics.DebugLog.Deactivate(_lastActiveModuleId);
        FFXProjectEditor.Diagnostics.DebugLog.Activate(_currentModuleId);
        _lastActiveModuleId = _currentModuleId;
    }

    // 🐉 DEBUG LOG — UI Event Tracer: loga o clique de botão com o módulo ativo (categoria UiTracer).
    private void OnAnyButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Avalonia.Controls.Button b)
            return;
        string label = b.Content?.ToString() ?? b.Name ?? "?";
        FFXProjectEditor.Diagnostics.DebugLog.Info("UiTracer", $"click '{label}' (module={_currentModuleId})");
    }

    private void ShowMonsterEditor(int? initialMonsterIndex = null, bool playConfirmFx = true)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
            return;

        MonEditorSelector_Control monsterEditor = new(initialMonsterIndex);
        SetModule(
            monsterEditor,
            "Monster Editor",
            "Browse and edit the FFX monster species files with strong control over stats, loot, resistances, and structural data.",
            "Writable",
            "Backed by the current FFXProjectEditor monster file flow. Best candidate for heavy authoring work.",
            initialMonsterIndex.HasValue
                ? $"Primary write path for monster data. Focused jump from another module into monster index {initialMonsterIndex.Value:D3}."
                : "Primary write path for monster data.",
            playConfirmFx
        );
    }

    private void BattleExplorer_OpenMonsterRequested(int monsterIndex)
    {
        NavigateToMonsterEditor(monsterIndex);
    }

    private void KernelCommands_OpenMonsterRequested(int monsterIndex)
    {
        NavigateToMonsterEditor(monsterIndex);
    }

    private void KernelCommands_OpenMagicEffectRequested(int magicId)
    {
        ShowPs3MagicBrowser(magicId, playConfirmFx: false);
    }

    private KernelCommands_Control CreateKernelCommandsControl(CommandFile_enum commandFileType)
    {
        KernelCommands_Control control = new(commandFileType);
        control.OpenMonsterRequested += KernelCommands_OpenMonsterRequested;
        control.OpenMagicEffectRequested += KernelCommands_OpenMagicEffectRequested;
        return control;
    }

    private void ShowKernelCommands(CommandFile_enum commandFileType, int? selectedEntryIndex = null, string? filterText = null, bool playConfirmFx = true)
    {
        KernelCommands_Control control = CreateKernelCommandsControl(commandFileType);
        if (selectedEntryIndex.HasValue || !string.IsNullOrWhiteSpace(filterText))
        {
            control.RestoreViewState(filterText, selectedEntryIndex);
        }

        switch (commandFileType)
        {
            case CommandFile_enum.Command:
                SetModule(
                    control,
                    "Battle Commands",
                    "Edit the main player command table and inspect battle behavior at the command-structure level.",
                    "Writable",
                    "This is one of the safest domains in the stack. The command table sizing split is already known and implemented.",
                    "Primary write path for command.bin.",
                    playConfirmFx
                );
                return;
            case CommandFile_enum.Item:
                SetModule(
                    control,
                    "Items",
                    "Edit item table entries and inspect their command-like battle behavior in a controlled grid workflow.",
                    "Writable",
                    "Use this for item-driven behavior and text-linked balancing work.",
                    "Primary write path for item.bin.",
                    playConfirmFx
                );
                return;
            case CommandFile_enum.MonMagic1:
                SetModule(
                    control,
                    "Monster Commands 1",
                    "Inspect and edit the first enemy-exclusive command table with direct access to battle-side monster abilities.",
                    "Writable",
                    "This is a high-value enemy behavior surface, especially when paired with AI exploration.",
                    "Primary write path for monmagic1.bin.",
                    playConfirmFx
                );
                return;
            case CommandFile_enum.MonMagic2:
                SetModule(
                    control,
                    "Monster Commands 2",
                    "Inspect and edit the second enemy-exclusive command table used by special bosses and enemy-only behavior.",
                    "Writable",
                    "Use this together with AI and encounter research to understand enemy move distribution.",
                    "Primary write path for monmagic2.bin.",
                    playConfirmFx
                );
                return;
        }
    }

    private void NavigateToMonsterEditor(int? initialMonsterIndex = null)
    {
        _currentModuleId = "monster-editor";
        NavigationSnapshot targetSnapshot = new("monster-editor");
        PushCurrentSnapshotForNavigation(targetSnapshot);
        ShowMonsterEditor(initialMonsterIndex);
    }

    public void OpenMonsterEditorFromExternalModule(int monsterIndex)
    {
        if (!Project_Service.Instance.IsProjectLoaded)
        {
            return;
        }

        NavigateToMonsterEditor(monsterIndex);
    }

    private NavigationSnapshot? CaptureCurrentNavigationSnapshot()
    {
        // v2.160.1.0 (Fase D): capture genérico via IRestorableModule. O Main_Window é opaco ao
        // conteúdo do State — o controle decide o que capturar (SelectedIndex, FilterText, etc.).
        // Se o controle não implementa IRestorableModule, capturamos só o ModuleId (sem state) —
        // back/forward ainda volta pra ele, só não restaura sub-seleção.
        if (ContentFrame.Content is IRestorableModule restorable)
        {
            return new NavigationSnapshot(_currentModuleId, restorable.CaptureState());
        }

        // Home (MainDashboard_Control) e controles não-IRestorable: snapshot stateless.
        if (!string.IsNullOrEmpty(_currentModuleId))
        {
            return new NavigationSnapshot(_currentModuleId, null);
        }

        return null;
    }

    private void PushCurrentSnapshotForNavigation(NavigationSnapshot targetSnapshot)
    {
        if (_isRestoringNavigation)
        {
            return;
        }

        NavigationSnapshot? currentSnapshot = CaptureCurrentNavigationSnapshot();
        if (!currentSnapshot.HasValue || IsSameNavigationSurface(currentSnapshot.Value, targetSnapshot))
        {
            UpdateNavigationState();
            return;
        }

        if (_backHistory.Count == 0 || !_backHistory.Peek().Equals(currentSnapshot.Value))
        {
            _backHistory.Push(currentSnapshot.Value);
        }

        _forwardHistory.Clear();
        UpdateNavigationState();
    }

    private static bool IsSameNavigationSurface(NavigationSnapshot currentSnapshot, NavigationSnapshot targetSnapshot)
    {
        // v2.160.1.0 (Fase D): comparação só por ModuleId. O State interno é best-effort
        // (mesmo módulo = mesma "superfície"); restaurar sub-seleção é responsabilidade do IRestorableModule.
        return currentSnapshot.ModuleId == targetSnapshot.ModuleId;
    }

    private void Button_NavigateBack(object? sender, RoutedEventArgs e)
    {
        if (_backHistory.Count == 0)
        {
            return;
        }

        AudioStudio_Service.Instance.PlayAlternative();
        NavigationSnapshot targetSnapshot = _backHistory.Pop();
        NavigationSnapshot? currentSnapshot = CaptureCurrentNavigationSnapshot();
        if (currentSnapshot.HasValue)
        {
            _forwardHistory.Push(currentSnapshot.Value);
        }

        RestoreNavigationSnapshot(targetSnapshot);
    }

    private void Button_NavigateForward(object? sender, RoutedEventArgs e)
    {
        if (_forwardHistory.Count == 0)
        {
            return;
        }

        AudioStudio_Service.Instance.PlayAlternative();
        NavigationSnapshot targetSnapshot = _forwardHistory.Pop();
        NavigationSnapshot? currentSnapshot = CaptureCurrentNavigationSnapshot();
        if (currentSnapshot.HasValue)
        {
            _backHistory.Push(currentSnapshot.Value);
        }

        RestoreNavigationSnapshot(targetSnapshot);
    }

    private void RestoreNavigationSnapshot(NavigationSnapshot snapshot)
    {
        _isRestoringNavigation = true;
        try
        {
            // v2.160.1.0 (Fase D): restore genérico. Dispatch recria o controle (Id → handler/factory);
            // depois aplicamos o State capturado no controle novo via IRestorableModule.RestoreState.
            // O dispatch é síncrono (SetModule atribui ContentFrame.Content), então o controle já existe.
            string restoredModuleId = ModuleDispatchRoute.ResolveRestoredId(snapshot.ModuleId);
            if (restoredModuleId != snapshot.ModuleId)
            {
                FFXProjectEditor.Diagnostics.DebugLog.Warn(
                    "Main.Dispatch",
                    $"Saved module id '{snapshot.ModuleId}' is unknown; restoring '{restoredModuleId}'.");
            }
            Dispatch(restoredModuleId);

            if (restoredModuleId == snapshot.ModuleId
                && snapshot.State != null
                && ContentFrame.Content is IRestorableModule restorable)
            {
                restorable.RestoreState(snapshot.State);
            }
        }
        finally
        {
            _isRestoringNavigation = false;
            UpdateNavigationState();
        }
    }

    private void UpdateNavigationState()
    {
        DataModel.SetNavigationState(_backHistory.Count > 0, _forwardHistory.Count > 0);
    }

    private void Window_Opened(object? sender, EventArgs e)
    {
        RegisterHoverAudioHandlers();

        if (_playedEditorOpenFx)
        {
            return;
        }

        _playedEditorOpenFx = true;
        AudioStudio_Service.Instance.BeginStartupSequence();
    }

    private void InteractivePointerEntered(object? sender, PointerEventArgs e)
    {
        Control? hoveredControl = ResolveInteractiveControl(sender);
        if (hoveredControl == null || !hoveredControl.IsEnabled || !hoveredControl.IsVisible)
        {
            return;
        }

        if (ReferenceEquals(_lastHoveredControl, hoveredControl) &&
            (DateTime.UtcNow - _lastHoverAtUtc).TotalMilliseconds < 125)
        {
            return;
        }

        _lastHoveredControl = hoveredControl;
        _lastHoverAtUtc = DateTime.UtcNow;
        AudioStudio_Service.Instance.PlayNavigation();
    }

    private void InteractivePointerExited(object? sender, PointerEventArgs e)
    {
        Control? hoveredControl = ResolveInteractiveControl(sender);
        if (hoveredControl != null && ReferenceEquals(_lastHoveredControl, hoveredControl))
        {
            _lastHoveredControl = null;
        }
    }

    private void RegisterHoverAudioHandlers()
    {
        RegisterHoverAudioHandlersForRoot(this);

        if (ContentFrame.Content is Control contentControl)
        {
            RegisterHoverAudioHandlersForRoot(contentControl);
        }
    }

    private void RegisterHoverAudioHandlersForRoot(Control root)
    {
        foreach (Control control in root.GetVisualDescendants().OfType<Control>())
        {
            if (!IsHoverSoundTarget(control))
            {
                continue;
            }

            if (_hoverSoundBoundControls.Add(control))
            {
                control.PointerEntered += InteractivePointerEntered;
                control.PointerExited += InteractivePointerExited;
            }
        }
    }

    private static bool IsHoverSoundTarget(Control control)
    {
        if (control is CheckBox || control.FindAncestorOfType<CheckBox>() != null)
        {
            return false;
        }

        return control is Button
            || control is TabItem
            || control is ListBoxItem
            || control is ComboBox
            || control is ComboBoxItem;
    }

    private static Control? ResolveInteractiveControl(object? source)
    {
        if (source is not Control control)
        {
            return null;
        }

        if (control is CheckBox || control.FindAncestorOfType<CheckBox>() != null)
        {
            return null;
        }

        Control? interactiveControl = control as Button;
        interactiveControl ??= control.FindAncestorOfType<Button>();
        interactiveControl ??= control as TabItem;
        interactiveControl ??= control.FindAncestorOfType<TabItem>();
        interactiveControl ??= control as ListBoxItem;
        interactiveControl ??= control.FindAncestorOfType<ListBoxItem>();

        interactiveControl ??= control as ComboBox;
        interactiveControl ??= control.FindAncestorOfType<ComboBox>();
        interactiveControl ??= control as ComboBoxItem;
        interactiveControl ??= control.FindAncestorOfType<ComboBoxItem>();

        if (interactiveControl == null)
        {
            return null;
        }

        return interactiveControl;
    }
}

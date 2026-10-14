using System;
using System.Collections.Generic;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.Diagnostics;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Services;

namespace FFXProjectEditor.Modules.Main
{
    internal partial class Main_DataModel : ObservableObject
    {
        [ObservableProperty] public string gameHookInfo = "Game not hooked";
        [ObservableProperty] public string currentModuleTitle = "Workspace Overview";
        [ObservableProperty] public string currentModuleDescription = "Use the left rail to jump into kernel data, battle exploration, text and macro explorers, monster authoring, live runtime tools, and the new Extras read-only product line.";
        [ObservableProperty] public string currentModuleMode = "Dashboard";
        [ObservableProperty] public string currentModuleNotes = "Write paths are strongest in battle kernel and monster domains. Battle Explorer, String Explorer, Macro Explorer, Event Explorer, and Extras are the safe bridges into encounter, text, script, and binary-family research while serializers remain controlled.";
        [ObservableProperty] public string currentModuleScope = "Kernel and Monster editing are safe. Battle, String, Macro, Event, and Extras structures can now be explored deeply. Encounter routing, AI, Event recompilation, Maps, and all PS2/PS3 companion families should remain read-heavy for now.";
        [ObservableProperty] public bool canNavigateBack;
        [ObservableProperty] public bool canNavigateForward;

        // ===== Camada 7/9 (overhaul de UI): path do workspace + glyphs/cores derivados para header e status bar.
        //    ProjectPath usa [ObservableProperty] uma única vez (não dá p/ duplicar atributo). =====
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(WorkspaceBadgeLabel))]
        [NotifyPropertyChangedFor(nameof(ProjectStatusForeground))]
        [NotifyPropertyChangedFor(nameof(ProjectStatusGlyph))]
        public string projectPath = Strings.ProjectPathPlaceholder;

        // Workspace badge: nome da pasta se houver projeto, senão "Open Workspace".
        public string WorkspaceBadgeLabel =>
            string.IsNullOrWhiteSpace(ProjectPath) || ProjectPath.Contains("Select") || ProjectPath == Strings.ProjectPathPlaceholder
                ? Strings.WorkspaceBadgeOpen
                : System.IO.Path.GetFileName(ProjectPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, '/', '\\'));

        // Game badge: the game install root is an independent pick (persisted override →
        // workspace-derived → env → Steam). Badge shows the resolved folder name so the
        // two selections stay visually distinct: game folder ≠ master workspace.
        public string GameRootBadgeLabel
        {
            get
            {
                string? root = PortablePathResolver.GameInstallRoot;
                if (string.IsNullOrWhiteSpace(root))
                    return Strings.GameBadgeOpen;
                string leaf = System.IO.Path.GetFileName(root.TrimEnd(System.IO.Path.DirectorySeparatorChar, '/', '\\'));
                return string.IsNullOrEmpty(leaf) ? root : leaf;
            }
        }

        public string GameRootTooltip =>
            PortablePathResolver.GameInstallRoot ?? Strings.U_GameFolderPickerTitle;

        // Hook status (cor + glyph + tooltip). ProcService notifica IsAlive; bindings disparam refresh.
        public IBrush HookStatusBrush => ProcService.IsAlive ? new SolidColorBrush(Color.Parse("#73E2AE")) : new SolidColorBrush(Color.Parse("#9EB0C2"));
        public string HookStatusForeground => ProcService.IsAlive ? "#73E2AE" : "#9EB0C2";
        public string HookStatusGlyph => ProcService.IsAlive ? "● Live" : "○ Off";
        public string HookStatusTooltip => ProcService.IsAlive ? "FFX hooked and running" : "FFX not hooked";

        // Project status (cor + glyph)
        public string ProjectStatusForeground => ProjService.IsProjectLoaded ? "#73E2AE" : "#9EB0C2";
        public string ProjectStatusGlyph => ProjService.IsProjectLoaded ? "● Loaded" : "○ None";

        // Dashboard hero (Jarvis-UI, audit 2026-06-19): copy muda conforme workspace carregado.
        public string DashboardHeroTitle =>
            ProjService.IsProjectLoaded ? Strings.DashboardHeroTitleLoaded : Strings.DashboardHeroTitleEmpty;

        public string DashboardHeroSubtitle =>
            ProjService.IsProjectLoaded ? Strings.DashboardHeroSubtitleLoaded : Strings.DashboardHeroSubtitleEmpty;

        public Process_Service ProcService { get => Process_Service.Instance; }
        public Project_Service ProjService { get => Project_Service.Instance; }
        public AudioStudio_Service AudioService { get => AudioStudio_Service.Instance; }

        // ===== v2.160.0.0 (Jarvis-UI §17 Workspace Ready): fonte única dos módulos do dashboard.
        // ItemsSource do dashboard binda aqui; o ModuleRegistry é read-only após a carga estática,
        // então não precisa de notificação de mudança. Adicionar módulo = append no ModuleRegistry.
        public IReadOnlyList<ModuleCatalogPolicy.ModuleCatalogEntry> Modules => ModuleRegistry.Public;

        /// <summary>
        /// Opt-in flag do AI Assistant (Jarvis-UI): OFF por padrão, persiste em
        /// feature-flags.json via <see cref="Core.AiFeatureGate"/>. Ligar revela o módulo
        /// no rail/grid/palette; desligar esconde + volta pro Home se estiver aberto
        /// (o bounce fica no handler do Changed em Main_Window).
        /// </summary>
        public bool AiAssistantEnabled
        {
            get => Core.AiFeatureGate.Enabled;
            set
            {
                if (Core.AiFeatureGate.Enabled == value) return;
                Core.AiFeatureGate.Enabled = value; // persiste + dispara Changed (rail rebuild)
                OnPropertyChanged();
                OnPropertyChanged(nameof(Modules));
            }
        }

        public Main_DataModel()
        {
            // Hook status bindings precisam ser reavaliados quando ProcService.IsAlive muda.
            ProcService.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Process_Service.IsAlive))
                {
                    OnPropertyChanged(nameof(HookStatusBrush));
                    OnPropertyChanged(nameof(HookStatusForeground));
                    OnPropertyChanged(nameof(HookStatusGlyph));
                    OnPropertyChanged(nameof(HookStatusTooltip));
                }
            };
            ProjService.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Project_Service.Path_GameInstallRoot))
                {
                    OnPropertyChanged(nameof(GameRootBadgeLabel));
                    OnPropertyChanged(nameof(GameRootTooltip));
                    return;
                }

                if (e.PropertyName != nameof(Project_Service.IsProjectLoaded))
                    return;

                OnPropertyChanged(nameof(ProjectStatusForeground));
                OnPropertyChanged(nameof(ProjectStatusGlyph));
                OnPropertyChanged(nameof(DashboardHeroTitle));
                OnPropertyChanged(nameof(DashboardHeroSubtitle));
            };
        }

        // Last load rejection reason (service exception message) — read by the window to render
        // the "workspace not loaded" dialog. Cleared on every successful load.
        public string? LastWorkspaceError { get; private set; }

        // ── Workspace load boundary ──
        // Project_Service.LoadProject throws on invalid folders; every caller reaches this
        // method from an async-void UI handler, where an escaping exception is an
        // AppDomain.UnhandledException process kill (crash.log 2026-09-14). The boundary
        // converts failure into a result; the window decides how to present it.
        public bool LoadProjectFolder(string selectedFolder)
        {
            try
            {
                Project_Service.Instance.LoadProject(selectedFolder);
            }
            catch (Exception ex)
            {
                LastWorkspaceError = ex.Message;
                DebugLog.Error("Main.Workspace", $"Workspace load rejected for '{selectedFolder}'", ex);
                return false;
            }

            LastWorkspaceError = null;
            ProjectPath = selectedFolder;
            // NotifyPropertyChangedFor (no atributo) já cuida do refresh; garantimos aqui p/ descontar de
            // bindings que o compilador não consegue ver via atributo (Foreground/Brush).
            OnPropertyChanged(nameof(HookStatusBrush));
            OnPropertyChanged(nameof(HookStatusForeground));
            OnPropertyChanged(nameof(HookStatusGlyph));
            OnPropertyChanged(nameof(HookStatusTooltip));
            OnPropertyChanged(nameof(ProjectStatusForeground));
            OnPropertyChanged(nameof(ProjectStatusGlyph));
            OnPropertyChanged(nameof(DashboardHeroTitle));
            OnPropertyChanged(nameof(DashboardHeroSubtitle));
            return true;
        }

        // Last game-folder rejection (the rejected path) — read by the window to render
        // the "invalid game folder" dialog. Cleared on every successful pick.
        public string? LastGameRootError { get; private set; }

        // ── Game folder boundary ──
        // Same async-void rule as LoadProjectFolder: the picker handler must get a result,
        // not an exception. Validation lives in Project_Service.SetGameRoot (the pick or an
        // ancestor must contain FFX.exe or modules\); a successful pick is persisted.
        public bool SelectGameFolder(string selectedFolder)
        {
            if (!Project_Service.Instance.SetGameRoot(selectedFolder))
            {
                LastGameRootError = selectedFolder;
                DebugLog.Error("Main.GameRoot", $"Game folder rejected: '{selectedFolder}'");
                return false;
            }

            LastGameRootError = null;
            OnPropertyChanged(nameof(GameRootBadgeLabel));
            OnPropertyChanged(nameof(GameRootTooltip));
            DebugLog.Info("Main.GameRoot", $"Game root set to {GameRootTooltip}");
            return true;
        }

        public void SetModuleInfo(string title, string description, string mode, string notes, string scope)
        {
            CurrentModuleTitle = title;
            CurrentModuleDescription = description;
            CurrentModuleMode = mode;
            CurrentModuleNotes = notes;
            CurrentModuleScope = scope;
        }

        public void SetNavigationState(bool canGoBack, bool canGoForward)
        {
            CanNavigateBack = canGoBack;
            CanNavigateForward = canGoForward;
        }

        public void ShowHome()
        {
            if (ModuleRegistry.Find("home") is { } home)
            {
                SetModuleInfo(
                    home.LocalizedTitle,
                    home.LocalizedDescription,
                    home.LocalizedMode,
                    home.LocalizedNotes,
                    home.LocalizedScope);
                return;
            }

            SetModuleInfo(
                "Workspace Overview",
                "This shell is the staging ground for FFX kernel authoring, battle exploration, string and macro inspection, monster editing, future encounter and AI research, plus read-only Extras surfaces for PS2/PS3 knowledge.",
                "Dashboard",
                "Start with Commands, Items, Monster Commands 1/2, Monster Editor, Battle Explorer, String Explorer, Macro Explorer, Event Explorer, and the new Extras read-only shell. Those are the strongest surfaces already proven by the current stack.",
                "Green domains: Kernel and Monsters. Blue domains: Battle, String, Macro, Event, and Extras read-only hubs. Yellow domains: Encounter routing, AI write paths, Event recompilation, Maps, and companion-file decoders.");
        }
    }
}

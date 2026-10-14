using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FFXProjectEditor.Services;
using FFXProjectEditor.Resources;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            // Jarvis-UI Fase F §F4 (v2.162.4.0): set the UI culture ONCE, before any XAML binding
            // reads a Strings.* property. Env FFX_UI_LANG=pt selects the PT satellite; anything else
            // (unset / en) falls back to the neutral resource (en).
            Strings.ApplyUiCulture();
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // 🐉 DEBUG LOG (v2.224.1.13): exceção global — QUALQUER crash não tratado cai no debug.log
            // (categoria Global) com o stack trace, mesmo sem depurador anexado.
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                FFXProjectEditor.Diagnostics.DebugLog.Error("Global", "AppDomain unhandled exception", e.ExceptionObject as System.Exception);
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
                FFXProjectEditor.Diagnostics.DebugLog.Error("Global", "Unobserved task exception", e.Exception);
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Priority: explicit CLI arg → the workspace the user last loaded (persisted) → the
                // hardcoded fallbacks. Persisting the last choice means "load once, remembered forever"
                // and stops the editor from silently defaulting back to the Steam-mod folder every launch.
                // Hydrate the explicit game-folder pick (game-root.txt) before any probe
                // or path resolver runs — the game root is chosen independently of the
                // master workspace and must already be in place when the dashboard opens.
                Project_Service.GameRootOverride = Project_Service.LoadLastGameRoot();
                // Same hydration for the explicit output pick (output-root.txt): a pinned
                // deploy folder survives restarts; without it Path_OutputRoot auto-resolves.
                Project_Service.OutputRootOverride = Project_Service.LoadLastOutputRoot();

                string? startupProjectPath = desktop.Args?.FirstOrDefault(Project_Service.IsPathValid)
                    ?? Project_Service.LoadLastProject()
                    ?? GetDefaultProjectPath();
                desktop.MainWindow = new Main_Window(startupProjectPath);
            }

            base.OnFrameworkInitializationCompleted();
        }

        // Portabilidade (plano §6.4/§7): sem default absoluto de máquina. O último workspace é
        // persistido (Project_Service.LoadLastProject); o jogo é descoberto/selecionado por
        // GameEnvironmentProbe ou manualmente. Retorna null => o usuário escolhe (ou abre sem projeto).
        private static string? GetDefaultProjectPath()
        {
            return null;
        }
    }
}

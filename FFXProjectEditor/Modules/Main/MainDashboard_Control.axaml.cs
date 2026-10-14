using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FFXProjectEditor.Core;
using FFXProjectEditor.Modules.Common;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils;

namespace FFXProjectEditor.Modules.Main;

/// <summary>
/// Dashboard / home do FFX Project Editor (Jarvis-UI, overhaul 2026-06-19; v2.160.0.0 §17 Workspace Ready).
/// Hero + workspace status + grid de TODOS os módulos (data-bound ao <see cref="ModuleRegistry"/>).
/// </summary>
public partial class MainDashboard_Control : UserControl, IRestorableModule
{
    /// <summary>Disparado quando o usuário clica em um card de módulo. O argumento é o <c>Id</c> do módulo no <see cref="ModuleRegistry"/>.</summary>
    public event Action<string>? QuickLaunchRequested;

    private readonly GameEnvironmentProbe _environmentProbe = new();

    public MainDashboard_Control()
    {
        InitializeComponent();
        RefreshHealthAndStatus();
        HealthCard.RefreshRequested += (s, e) => RefreshHealthAndStatus();
        HealthCard.SelectGameFolderRequested += async (s, e) => await PickGameFolderAsync();
        StatusCard.ChangeWorkspaceRequested += (s, e) => Button_OpenWorkspace(s, new RoutedEventArgs());
        StatusCard.SelectOutputFolderRequested += async (s, e) => await PickOutputFolderAsync();
        StatusCard.ResetOutputRequested += (s, e) =>
        {
            Project_Service.Instance.ClearOutputRoot();
            RefreshHealthAndStatus();
        };

        // The game root can move when the workspace loads (derived) or when the user picks
        // a game folder from the header badge — keep both cards in sync without a recheck.
        Project_Service.Instance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Project_Service.IsProjectLoaded) or nameof(Project_Service.Path_GameInstallRoot) or nameof(Project_Service.Path_OutputRoot))
                RefreshHealthAndStatus();
        };
    }

    // Game root and workspace are independent picks (the master may live outside the
    // install). Same funnel as the window's picker: OpenFolderDialog → validated
    // SetGameRoot → actionable dialog on reject — never an unhandled exception.
    private async Task PickGameFolderAsync()
    {
        var folders = await AvaloniaDialog_Util.OpenFolderDialog(this, Strings.U_GameFolderPickerTitle);
        if (folders.Count == 0)
            return;
        if (!Project_Service.Instance.SetGameRoot(folders[0]))
        {
            await AvaloniaDialog_Util.ShowMessageAsync(this,
                Strings.U_GameInvalidFolderTitle,
                string.Format(Strings.U_GameInvalidFolderMessage, folders[0]));
            return;
        }
        RefreshHealthAndStatus();
    }

    // Output is user-overridable: the pick pins the deploy folder (persisted); the
    // reset button on the card clears the pin and falls back to auto-resolution
    // (data\mods under the external loader, else output_staging).
    private async Task PickOutputFolderAsync()
    {
        var folders = await AvaloniaDialog_Util.OpenFolderDialog(this, Strings.U_OutputFolderPickerTitle);
        if (folders.Count == 0)
            return;
        if (!Project_Service.Instance.SetOutputRoot(folders[0]))
        {
            await AvaloniaDialog_Util.ShowMessageAsync(this,
                Strings.U_OutputInvalidFolderTitle,
                string.Format(Strings.U_OutputInvalidFolderMessage, folders[0]));
            return;
        }
        RefreshHealthAndStatus();
    }

    public void RefreshHealthAndStatus()
    {
        var report = _environmentProbe.Detect();
        HealthCard.SetReport(report);

        var projectService = Project_Service.Instance;
        var sampleInspection = new WorkspaceInspectionResult
        {
            // GAME = install root (FFX.exe — deploy paths derive from it); WORKSPACE =
            // extracted master (editing base, may live outside the install); OUTPUT =
            // resolved deploy root (data\mods under the external loader, else
            // output_staging). SourcePath is required non-null, so the fallback string
            // keeps the "no workspace selected" state renderable.
            GamePath = projectService.Path_GameInstallRoot ?? report.GameRoot,
            SourcePath = projectService.ProjectPath ?? Strings.U_Ws_NoDirectorySelected,
            OutputPath = projectService.Path_OutputRoot,
            DetectedVersion = report.GameVersion ?? "v1.0.0",
            DetectedPlatform = Platform.PC,
            Capabilities = BuildCapabilitiesList(report),
            MissingDependencies = Array.Empty<string>(),
            Warnings = report.Issues.Select(i => i.Message).ToList(),
            ScanTimestamp = DateTimeOffset.UtcNow
        };
        Diagnostics.DebugLog.Info("Home.Paths",
            $"game={sampleInspection.GamePath ?? "(none)"} workspace={sampleInspection.SourcePath} output={sampleInspection.OutputPath ?? "(none)"}");
        StatusCard.SetInspectionResult(sampleInspection);
    }

    private static IReadOnlyList<CapabilityAvailability> BuildCapabilitiesList(GameEnvironmentReport report)
    {
        var list = new List<CapabilityAvailability>();
        var catalog = new WriterAdapterCatalog();
        foreach (var adapter in catalog.GetAll())
        {
            list.Add(new CapabilityAvailability
            {
                Descriptor = new CapabilityDescriptor
                {
                    Id = adapter.CapabilityId,
                    Domain = adapter.CapabilityId,
                    Title = adapter.CapabilityId,
                    Description = string.Format(Strings.U_Env_AdapterDescription, adapter.CapabilityId),
                    Mode = CapabilityMode.OfflineWriter,
                    Evidence = EvidenceLevel.Production,
                    Platforms = new[] { Platform.PC },
                    RequiredDependencies = Array.Empty<string>(),
                    OptionalDependencies = Array.Empty<string>(),
                    Risks = Array.Empty<string>(),
                    AllowedOperations = new[] { AllowedOperation.Read, AllowedOperation.Edit },
                    ProhibitedOperations = Array.Empty<AllowedOperation>(),
                    Preconditions = Array.Empty<string>(),
                    DocumentationLinks = Array.Empty<string>(),
                    OwnerAgent = "Jarvis"
                },
                IsAvailable = report.GameFound,
                UnavailabilityReasons = report.GameFound ? Array.Empty<string>() : new[] { Strings.U_Env_GameNotFoundReason },
                UserEnabled = true
            });
        }
        return list;
    }

    // ===== Quick-launch handlers (Jarvis-UI, audit 2026-06-19).
    private void Button_QuickLaunch(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string moduleId)
        {
            QuickLaunchRequested?.Invoke(moduleId);
        }
    }

    private void Button_OpenWorkspace(object? sender, RoutedEventArgs e)
    {
        QuickLaunchRequested?.Invoke("open_workspace");
    }
}


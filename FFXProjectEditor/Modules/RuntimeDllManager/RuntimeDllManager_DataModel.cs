using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text.Json;

namespace FFXProjectEditor.Modules.RuntimeDllManager
{
    internal partial class RuntimeDllManager_DataModel : ObservableObject
    {
        private static readonly string KnownSteamRoot = Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.SteamCommonRoot, @"FINAL FANTASY FFX&FFX-2 HD Remaster");
        private const string SteamAppId = "359870";
        private const string HooksMmfName = "Local\\FFXHooksBlock_v1";
        private const int HooksBlockSize = 256;
        private const uint HooksMagic = 0x48584646; // 'FFXH'
        private static readonly char[] PathTrimChars = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ModulesPath))]
        [NotifyPropertyChangedFor(nameof(GameRootReady))]
        [NotifyPropertyChangedFor(nameof(GameRootHasFfxExe))]
        [NotifyPropertyChangedFor(nameof(GameRootHasModules))]
        [NotifyPropertyChangedFor(nameof(IsGameClosed))]
        [NotifyPropertyChangedFor(nameof(IsRuntimeDetached))]
        private string gameRoot = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsGameClosed))]
        [NotifyPropertyChangedFor(nameof(IsRuntimeDetached))]
        private bool gameOpen;

        [ObservableProperty] private bool probeAttached;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsRuntimeDetached))]
        private bool probeHooked;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsRuntimeDetached))]
        private bool hooksAttached;

        [ObservableProperty] private bool moduleListAvailable;
        [ObservableProperty] private string runtimeSummary = "Refresh pendente.";
        [ObservableProperty] private string fileSummary = Strings.F2_no_target_loaded_0f8cc9e5;
        [ObservableProperty] private string statusSummary = Strings.F2_detect_the_game_directory_and_choose_whi_cf67e7c2;
        [ObservableProperty] private string lastActionSummary = "-";

        public ObservableCollection<RuntimeDllItem> Dlls { get; } = new();

        public Process_Service ProcService => Process_Service.Instance;
        public Project_Service ProjService => Project_Service.Instance;

        public string ModulesPath => string.IsNullOrWhiteSpace(GameRoot) ? string.Empty : Path.Combine(GameRoot, "modules");
        public bool GameRootReady => IsLikelyGameRoot(GameRoot);
        public bool GameRootHasFfxExe => HasFfxExe(GameRoot);
        public bool GameRootHasModules => HasModules(GameRoot);

        // Jarvis-UI (audit 2026-06-20): banners de estado de runtime, derivados das flags já existentes.
        // IsGameClosed   = alvo válido mas FFX fechado → toggles só armam o disco (próximo boot).
        // IsRuntimeDetached = FFX aberto mas nenhum hook/probe ativo → status ao vivo não é confiável.
        public bool IsGameClosed => GameRootReady && !GameOpen;
        public bool IsRuntimeDetached => GameRootReady && GameOpen && !ProbeHooked && !HooksAttached;

        public RuntimeDllManager_DataModel()
        {
            DetectGameRoot();
            RefreshStatus();
        }

        partial void OnGameRootChanged(string value)
        {
            string normalized = NormalizeGameRootInput(value);
            if (!string.Equals(value, normalized, StringComparison.Ordinal))
            {
                GameRoot = normalized;
                return;
            }

            RefreshStatus();
        }

        public void DetectGameRoot()
        {
            Process_Service.Instance.AutoDetect();

            string? fromProcess = TryResolveGameRootFromProcess();
            if (!string.IsNullOrWhiteSpace(fromProcess))
            {
                GameRoot = NormalizeGameRootInput(fromProcess);
                StatusSummary = Strings.F2_target_detected_by_the_open_ffx_exe_adfb3e9e;
                return;
            }

            string? fromProject = TryResolveGameRootFromProject(Project_Service.Instance.ProjectPath);
            if (!string.IsNullOrWhiteSpace(fromProject))
            {
                GameRoot = NormalizeGameRootInput(fromProject);
                StatusSummary = Strings.F2_target_detected_from_the_loaded_workspac_6d210b83;
                return;
            }

            if (Directory.Exists(KnownSteamRoot))
            {
                GameRoot = NormalizeGameRootInput(KnownSteamRoot);
                StatusSummary = Strings.F2_target_detected_by_the_known_steam_path_220c3e05;
                return;
            }

            StatusSummary = Strings.F2_could_not_detect_the_game_directory_past_cde7a7f5;
            RefreshStatus();
        }

        public void RefreshStatus()
        {
            GameRoot = NormalizeGameRootInput(GameRoot);
            Process_Service.Instance.AutoDetect();

            Process? liveProcess = TryGetLiveFfxProcess();
            LoadedModuleSnapshot loadedModules = LoadedModuleSnapshot.FromProcess(liveProcess);
            string? liveRoot = TryGetProcessRoot(liveProcess);
            bool targetMismatch = GameRootReady &&
                                  !string.IsNullOrWhiteSpace(liveRoot) &&
                                  !PathsEqual(GameRoot, liveRoot);

            GameOpen = liveProcess != null;
            ModuleListAvailable = loadedModules.Available;
            ProbeAttached = FfxProbe_Service.Instance.IsAttached;
            ProbeHooked = FfxProbe_Service.Instance.IsHooked;
            HooksAttached = TryReadHooksBlock();

            List<RuntimeDllItem> rows = BuildRows(loadedModules);
            Dlls.Clear();
            foreach (RuntimeDllItem row in rows)
            {
                Dlls.Add(row);
            }

            int enabled = rows.Count(row => row.IsEnabledOnDisk);
            int disabled = rows.Count(row => row.IsDisabledOnDisk && !row.IsEnabledOnDisk);
            int missing = rows.Count(row => row.IsMissing);
            int active = rows.Count(row => row.IsRuntimeActive);

            RuntimeSummary = GameOpen
                ? targetMismatch
                    ? $"FFX.exe PID {liveProcess?.Id} vivo, mas esta em {liveRoot}. Alvo selecionado: {GameRoot}."
                    : $"FFX.exe PID {liveProcess?.Id} vivo. Runtime: {active} DLL(s) detectadas; probe={Flag(ProbeHooked)} hooks={Flag(HooksAttached)}."
                : Strings.F2_ffx_exe_not_detected_changes_here_arm_th_4908b63d;

            string moduleScan = ModuleListAvailable
                ? "Process module list available"
                : GameOpen ? "Process module list unavailable; using MMF/disk name" : "sem processo vivo";
            FileSummary = GameRootReady
                ? $"{enabled} ligada(s), {disabled} desligada(s), {missing} ausente(s); {moduleScan}. Alvo: {GameRoot}"
                : "Alvo invalido ou vazio.";
        }

        public void ToggleDll(RuntimeDllItem item)
        {
            if (item == null)
            {
                return;
            }

            try
            {
                if (!GameRootReady)
                {
                    LastActionSummary = Strings.F2_nothing_applied_a2fff495;
                    StatusSummary = Strings.F2_invalid_target_enter_the_directory_conta_579e5151;
                    RefreshStatus();
                    return;
                }

                string enabledPath = item.EnabledPath;
                string disabledPath = item.DisabledPath;
                if (string.IsNullOrWhiteSpace(enabledPath) || string.IsNullOrWhiteSpace(disabledPath))
                {
                    LastActionSummary = Strings.F2_nothing_applied_a2fff495;
                    StatusSummary = $"Caminho invalido para {item.FileName}.";
                    RefreshStatus();
                    return;
                }

                bool wasRuntimeActive = item.IsRuntimeActive;
                string nextBootSuffix = GameOpen
                    ? " FFX esta aberto: se a DLL ja carregou, a mudanca vale de verdade no proximo boot."
                    : string.Empty;

                if (File.Exists(enabledPath) && File.Exists(disabledPath))
                {
                    LastActionSummary = "Conflito detectado.";
                    StatusSummary = $"{item.FileName} existe como .dll e .dll.disabled. Resolva manualmente antes do toggle.";
                    RefreshStatus();
                    return;
                }

                if (File.Exists(enabledPath))
                {
                    File.Move(enabledPath, disabledPath);
                    LastActionSummary = $"{item.FileName}: ligado -> desligado.";
                    StatusSummary = $"{item.DisplayName} desligado no disco.{nextBootSuffix}";
                    RefreshStatus();
                    return;
                }

                if (File.Exists(disabledPath))
                {
                    File.Move(disabledPath, enabledPath);
                    LastActionSummary = $"{item.FileName}: desligado -> ligado.";
                    StatusSummary = $"{item.DisplayName} ligado no disco.{nextBootSuffix}";
                    RefreshStatus();
                    return;
                }

                if (!string.IsNullOrWhiteSpace(item.RepoSourcePath) && File.Exists(item.RepoSourcePath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(enabledPath) ?? GameRoot);
                    File.Copy(item.RepoSourcePath, enabledPath, overwrite: false);
                    LastActionSummary = $"{item.FileName}: instalado do repo.";
                    StatusSummary = $"{item.DisplayName} copiado para o alvo e ligado para o proximo boot.{nextBootSuffix}";
                    RefreshStatus();
                    return;
                }

                LastActionSummary = Strings.F2_nothing_applied_a2fff495;
                StatusSummary = $"{item.FileName} esta ausente e nao encontrei build local para instalar.";
                RefreshStatus();
            }
            catch (Exception ex)
            {
                LastActionSummary = $"Falha em {item.FileName}.";
                StatusSummary = $"Toggle falhou: {ex.Message}";
                RefreshStatus();
            }
        }

        public void OpenTargetFolder()
        {
            try
            {
                GameRoot = NormalizeGameRootInput(GameRoot);
                if (!GameRootReady)
                {
                    StatusSummary = Strings.F2_invalid_target_first_enter_the_game_dire_e7f66fb2;
                    RefreshStatus();
                    return;
                }

                string folder = Directory.Exists(ModulesPath) ? ModulesPath : GameRoot;
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = folder,
                    UseShellExecute = true
                });
                StatusSummary = "Pasta runtime aberta.";
            }
            catch (Exception ex)
            {
                StatusSummary = $"Falha ao abrir pasta: {ex.Message}";
            }
        }

        public void LaunchGame()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"steam://rungameid/{SteamAppId}",
                    UseShellExecute = true
                });
                StatusSummary = "Launch via Steam enviado (appid 359870).";
            }
            catch (Exception ex)
            {
                StatusSummary = $"Falha ao abrir FFX pelo Steam: {ex.Message}";
            }

            RefreshStatus();
        }

        private List<RuntimeDllItem> BuildRows(LoadedModuleSnapshot loadedModules)
        {
            Dictionary<string, RuntimeDllItem> rows = new(StringComparer.OrdinalIgnoreCase);
            AddKnown(rows, loadedModules, new RuntimeDllCandidate(
                "dinput8.dll",
                "FFX Module Loader",
                RuntimeDllLocation.GameRoot,
                "Loader core",
                "Proxy DINPUT8 local que carrega modules\\*.dll.",
                RepoSourceRelativePath: null,
                IsCore: true));

            AddKnown(rows, loadedModules, new RuntimeDllCandidate(
                "dxgi.dll",
                "UnX / Special K DXGI",
                RuntimeDllLocation.GameRoot,
                "Root hook",
                "Camada DXGI/UnX presente na pasta do jogo.",
                RepoSourceRelativePath: null,
                IsCore: false));

            AddKnown(rows, loadedModules, new RuntimeDllCandidate(
                "unx.dll",
                "UnX companion",
                RuntimeDllLocation.GameRoot,
                "Root hook",
                "DLL companheira do UnX.",
                RepoSourceRelativePath: null,
                IsCore: false));

            AddKnown(rows, loadedModules, new RuntimeDllCandidate(
                "ff10-file-loader.dll",
                "FFX External File Loader",
                RuntimeDllLocation.Modules,
                "Module loader DLL",
                "Carrega loose files/mods pela pasta data\\mods.",
                RepoSourceRelativePath: null,
                IsCore: false));

            AddKnown(rows, loadedModules, new RuntimeDllCandidate(
                "ffx-probe.dll",
                "FFX Probe",
                RuntimeDllLocation.Modules,
                "Main-thread bridge",
                "MMF FFXProbeBlock_v1 para READ/WRITE/CALL/Force Battle na thread principal.",
                @"RuntimeTools\FfxDinput8Probe\ffx-probe.dll",
                IsCore: false,
                RuntimeActiveOverride: ProbeAttached || ProbeHooked));

            AddKnown(rows, loadedModules, new RuntimeDllCandidate(
                "ffx-hooks.dll",
                "FFX Hooks",
                RuntimeDllLocation.Modules,
                "Engine hook layer",
                "MMF FFXHooksBlock_v1; MusicHook, ElementHook e Aurora overlay vivem aqui.",
                @"RuntimeTools\FfxHooksDll\bin\Release\ffx-hooks.dll",
                IsCore: false,
                RuntimeActiveOverride: HooksAttached));

            foreach (RuntimeDllCandidate discovered in DiscoverExtraCandidates())
            {
                AddKnown(rows, loadedModules, discovered);
            }

            return rows.Values
                .OrderBy(row => row.SortGroup)
                .ThenBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void AddKnown(
            Dictionary<string, RuntimeDllItem> rows,
            LoadedModuleSnapshot loadedModules,
            RuntimeDllCandidate candidate)
        {
            string targetDirectory = candidate.Location == RuntimeDllLocation.GameRoot ? GameRoot : ModulesPath;
            string enabledPath = string.IsNullOrWhiteSpace(targetDirectory) ? string.Empty : Path.Combine(targetDirectory, candidate.FileName);
            string disabledPath = string.IsNullOrWhiteSpace(enabledPath) ? string.Empty : enabledPath + ".disabled";
            PluginMetadata metadata = LoadPluginMetadata(candidate.FileName, targetDirectory);
            string repoSourcePath = ResolveRepoSource(FirstNonEmpty(metadata.RepoSourceRelativePath, candidate.RepoSourceRelativePath));
            bool enabled = File.Exists(enabledPath);
            bool disabled = File.Exists(disabledPath);
            bool sourceAvailable = !string.IsNullOrWhiteSpace(repoSourcePath) && File.Exists(repoSourcePath);
            string versionInfoSource = enabled ? enabledPath : disabled ? disabledPath : sourceAvailable ? repoSourcePath : string.Empty;
            FileVersionMetadata versionInfo = ReadFileVersionMetadata(versionInfoSource);
            bool loaded = loadedModules.ContainsPathOrName(candidate.FileName, enabledPath);
            bool runtimeActive = candidate.RuntimeActiveOverride || loaded;
            bool canToggle = GameRootReady && (enabled || disabled || sourceAvailable) && !(enabled && disabled);
            string displayName = FirstNonEmpty(metadata.DisplayName, metadata.Name, versionInfo.DisplayName, candidate.DisplayName);
            string kind = FirstNonEmpty(metadata.Kind, candidate.Kind);
            string description = FirstNonEmpty(metadata.Description, versionInfo.Description, candidate.Description);
            string versionText = FirstNonEmpty(metadata.Version, versionInfo.Version);

            RuntimeDllItem row = new()
            {
                DisplayName = displayName,
                FileName = candidate.FileName,
                Kind = kind,
                Description = description,
                Location = candidate.Location == RuntimeDllLocation.GameRoot ? "game root" : "modules",
                EnabledPath = enabledPath,
                DisabledPath = disabledPath,
                RepoSourcePath = sourceAvailable ? repoSourcePath : string.Empty,
                IsKnown = !candidate.IsDiscovered,
                IsCore = candidate.IsCore,
                HasManifest = metadata.HasManifest,
                VersionText = versionText,
                ManifestPath = metadata.ManifestPath,
                IsEnabledOnDisk = enabled,
                IsDisabledOnDisk = disabled,
                IsRuntimeActive = runtimeActive,
                RuntimeStatusText = BuildRuntimeStatus(candidate, runtimeActive, loadedModules.Available),
                DiskStatusText = BuildDiskStatus(enabledPath, disabledPath, repoSourcePath),
                CanToggle = canToggle,
                SortGroup = candidate.Location == RuntimeDllLocation.GameRoot ? 0 : candidate.IsDiscovered ? 2 : 1
            };

            rows[RowKey(candidate.Location, candidate.FileName)] = row;
        }

        private IEnumerable<RuntimeDllCandidate> DiscoverExtraCandidates()
        {
            if (string.IsNullOrWhiteSpace(ModulesPath) || !Directory.Exists(ModulesPath))
            {
                yield break;
            }

            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            foreach (string path in Directory.EnumerateFiles(ModulesPath, "*.dll"))
            {
                names.Add(Path.GetFileName(path));
            }

            foreach (string path in Directory.EnumerateFiles(ModulesPath, "*.dll.disabled"))
            {
                string name = Path.GetFileName(path);
                if (name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(name[..^".disabled".Length]);
                }
            }

            foreach (string name in names)
            {
                if (name.Equals("ff10-file-loader.dll", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("ffx-probe.dll", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("ffx-hooks.dll", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return new RuntimeDllCandidate(
                    name,
                    Path.GetFileNameWithoutExtension(name),
                    RuntimeDllLocation.Modules,
                    "Discovered module",
                    "DLL encontrada em modules.",
                    RepoSourceRelativePath: null,
                    IsCore: false,
                    IsDiscovered: true);
            }
        }

        private static string BuildRuntimeStatus(RuntimeDllCandidate candidate, bool runtimeActive, bool moduleListAvailable)
        {
            if (runtimeActive)
            {
                return Strings.F2_running_now_33cfd5d5;
            }

            if (!moduleListAvailable && candidate.Location == RuntimeDllLocation.Modules)
            {
                return "Runtime nao confirmado";
            }

            return Strings.F2_not_detected_in_the_process_5de5c053;
        }

        private static string BuildDiskStatus(string enabledPath, string disabledPath, string repoSourcePath)
        {
            bool enabled = File.Exists(enabledPath);
            bool disabled = File.Exists(disabledPath);
            if (enabled && disabled)
            {
                return "Conflito: .dll e .dll.disabled existem";
            }

            if (enabled)
            {
                return $"Ligada no disco ({FileSizeLabel(enabledPath)})";
            }

            if (disabled)
            {
                return $"Desligada no disco ({FileSizeLabel(disabledPath)})";
            }

            if (!string.IsNullOrWhiteSpace(repoSourcePath) && File.Exists(repoSourcePath))
            {
                return $"Disponivel no repo ({FileSizeLabel(repoSourcePath)})";
            }

            return "Ausente";
        }

        private PluginMetadata LoadPluginMetadata(string fileName, string targetDirectory)
        {
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            string configDirectory = string.IsNullOrWhiteSpace(ModulesPath) ? string.Empty : Path.Combine(ModulesPath, "config");
            string pluginsDirectory = string.IsNullOrWhiteSpace(configDirectory) ? string.Empty : Path.Combine(configDirectory, "plugins");

            foreach (string path in CandidateManifestPaths(targetDirectory, configDirectory, pluginsDirectory, fileName, baseName))
            {
                PluginMetadata metadata = TryReadPluginManifest(path);
                if (metadata.HasManifest)
                {
                    return metadata;
                }
            }

            return new PluginMetadata();
        }

        private static IEnumerable<string> CandidateManifestPaths(string targetDirectory, string configDirectory, string pluginsDirectory, string fileName, string baseName)
        {
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                yield return Path.Combine(targetDirectory, $"{fileName}.plugin.json");
                yield return Path.Combine(targetDirectory, $"{baseName}.plugin.json");
            }

            if (!string.IsNullOrWhiteSpace(configDirectory))
            {
                yield return Path.Combine(configDirectory, $"{baseName}.plugin.json");
                yield return Path.Combine(configDirectory, $"{fileName}.plugin.json");
            }

            if (!string.IsNullOrWhiteSpace(pluginsDirectory))
            {
                yield return Path.Combine(pluginsDirectory, $"{baseName}.json");
                yield return Path.Combine(pluginsDirectory, $"{fileName}.json");
            }
        }

        private static PluginMetadata TryReadPluginManifest(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new PluginMetadata();
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                JsonElement root = doc.RootElement;
                return new PluginMetadata
                {
                    HasManifest = true,
                    ManifestPath = path,
                    Name = ReadJsonString(root, "name", "id"),
                    DisplayName = ReadJsonString(root, "displayName", "display_name", "title"),
                    Kind = ReadJsonString(root, "kind", "category", "type"),
                    Description = ReadJsonString(root, "description", "summary"),
                    Version = ReadJsonString(root, "version"),
                    RepoSourceRelativePath = ReadJsonString(root, "repoSourceRelativePath", "repo_source_relative_path", "repoSource")
                };
            }
            catch
            {
                return new PluginMetadata();
            }
        }

        private static string ReadJsonString(JsonElement root, params string[] names)
        {
            foreach (string name in names)
            {
                if (root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
                {
                    string? text = value.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text.Trim();
                    }
                }
            }

            return string.Empty;
        }

        private static FileVersionMetadata ReadFileVersionMetadata(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new FileVersionMetadata();
            }

            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                return new FileVersionMetadata
                {
                    DisplayName = FirstNonEmpty(info.ProductName, info.FileDescription),
                    Description = info.FileDescription ?? string.Empty,
                    Version = FirstNonEmpty(info.ProductVersion, info.FileVersion)
                };
            }
            catch
            {
                return new FileVersionMetadata();
            }
        }

        private string ResolveRepoSource(string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return string.Empty;
            }

            string? root = TryResolveRepoRoot();
            if (string.IsNullOrWhiteSpace(root))
            {
                return string.Empty;
            }

            return Path.Combine(root, relativePath);
        }

        private static string? TryResolveRepoRoot()
        {
            foreach (string seed in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            {
                try
                {
                    DirectoryInfo? cursor = new(seed);
                    while (cursor != null)
                    {
                        if (Directory.Exists(Path.Combine(cursor.FullName, "RuntimeTools")) &&
                            Directory.Exists(Path.Combine(cursor.FullName, "FFXProjectEditor")))
                        {
                            return cursor.FullName;
                        }

                        cursor = cursor.Parent;
                    }
                }
                catch
                {
                    // Try the next seed.
                }
            }

            return null;
        }

        private static bool TryReadHooksBlock()
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            try
            {
                using MemoryMappedFile mmf = MemoryMappedFile.OpenExisting(HooksMmfName);
                using MemoryMappedViewAccessor view = mmf.CreateViewAccessor(0, HooksBlockSize, MemoryMappedFileAccess.Read);
                return view.ReadUInt32(0) == HooksMagic;
            }
            catch
            {
                return false;
            }
        }

        private static string RowKey(RuntimeDllLocation location, string fileName)
        {
            return $"{location}:{fileName}";
        }

        private static string NormalizeGameRootInput(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string normalized = path.Trim().Trim('"');
            try
            {
                if (File.Exists(normalized))
                {
                    normalized = Path.GetDirectoryName(normalized) ?? normalized;
                }

                normalized = normalized.TrimEnd(PathTrimChars);
                string leaf = Path.GetFileName(normalized);
                if (leaf.Equals("modules", StringComparison.OrdinalIgnoreCase))
                {
                    DirectoryInfo? parent = Directory.GetParent(normalized);
                    if (parent != null)
                    {
                        normalized = parent.FullName.TrimEnd(PathTrimChars);
                    }
                }
            }
            catch
            {
                return normalized;
            }

            return normalized;
        }

        private static string? TryResolveGameRootFromProcess()
        {
            try
            {
                Process? process = TryGetLiveFfxProcess();
                if (process == null || process.HasExited)
                {
                    return null;
                }

                string? exe = process.MainModule?.FileName;
                string? root = string.IsNullOrWhiteSpace(exe) ? null : Path.GetDirectoryName(exe);
                return IsLikelyGameRoot(root) ? root : null;
            }
            catch
            {
                return null;
            }
        }

        private static Process? TryGetLiveFfxProcess()
        {
            try
            {
                Process? serviceProcess = Process_Service.Instance.GameProcess;
                if (IsLiveFfxProcess(serviceProcess))
                {
                    return serviceProcess;
                }

                foreach (Process process in Process.GetProcessesByName("FFX"))
                {
                    if (IsLiveFfxProcess(process))
                    {
                        return process;
                    }

                    process.Dispose();
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsLiveFfxProcess(Process? process)
        {
            if (!IsLiveProcess(process))
            {
                return false;
            }

            try
            {
                return process!.ProcessName.Equals("FFX", StringComparison.OrdinalIgnoreCase) ||
                       (process.MainModule?.FileName?.EndsWith("FFX.exe", StringComparison.OrdinalIgnoreCase) ?? false);
            }
            catch
            {
                return process!.ProcessName.Equals("FFX", StringComparison.OrdinalIgnoreCase);
            }
        }

        private static bool IsLiveProcess(Process? process)
        {
            try
            {
                return process != null && !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private static string? TryResolveGameRootFromProject(string? projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                return null;
            }

            DirectoryInfo? cursor = new(projectPath);
            while (cursor != null)
            {
                if (IsLikelyGameRoot(cursor.FullName))
                {
                    return cursor.FullName;
                }

                cursor = cursor.Parent;
            }

            return null;
        }

        private static bool IsLikelyGameRoot(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return false;
            }

            return HasFfxExe(path) || HasModules(path);
        }

        private static bool HasFfxExe(string? path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                   File.Exists(Path.Combine(path, "FFX.exe"));
        }

        private static bool HasModules(string? path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                   Directory.Exists(Path.Combine(path, "modules"));
        }

        private static string? TryGetProcessRoot(Process? process)
        {
            try
            {
                string? exe = process?.MainModule?.FileName;
                return string.IsNullOrWhiteSpace(exe)
                    ? null
                    : NormalizeGameRootInput(Path.GetDirectoryName(exe));
            }
            catch
            {
                return null;
            }
        }

        private static bool PathsEqual(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            try
            {
                string a = Path.GetFullPath(NormalizeGameRootInput(left)).TrimEnd(PathTrimChars);
                string b = Path.GetFullPath(NormalizeGameRootInput(right)).TrimEnd(PathTrimChars);
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string FileSizeLabel(string path)
        {
            try
            {
                long kb = new FileInfo(path).Length / 1024;
                return $"{kb} KB";
            }
            catch
            {
                return "size ?";
            }
        }

        private static string Flag(bool value) => value ? "on" : "off";

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (string? value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        private sealed record RuntimeDllCandidate(
            string FileName,
            string DisplayName,
            RuntimeDllLocation Location,
            string Kind,
            string Description,
            string? RepoSourceRelativePath,
            bool IsCore,
            bool RuntimeActiveOverride = false,
            bool IsDiscovered = false);

        private sealed class PluginMetadata
        {
            public bool HasManifest { get; init; }
            public string ManifestPath { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public string DisplayName { get; init; } = string.Empty;
            public string Kind { get; init; } = string.Empty;
            public string Description { get; init; } = string.Empty;
            public string Version { get; init; } = string.Empty;
            public string RepoSourceRelativePath { get; init; } = string.Empty;
        }

        private sealed class FileVersionMetadata
        {
            public string DisplayName { get; init; } = string.Empty;
            public string Description { get; init; } = string.Empty;
            public string Version { get; init; } = string.Empty;
        }

        private enum RuntimeDllLocation
        {
            GameRoot,
            Modules
        }

        private sealed class LoadedModuleSnapshot
        {
            private readonly Dictionary<string, List<string>> _pathsByName;

            private LoadedModuleSnapshot(bool available, Dictionary<string, List<string>> pathsByName)
            {
                Available = available;
                _pathsByName = pathsByName;
            }

            public bool Available { get; }

            public bool ContainsPathOrName(string fileName, string expectedPath)
            {
                if (!_pathsByName.TryGetValue(fileName, out List<string>? paths))
                {
                    return false;
                }

                if (string.IsNullOrWhiteSpace(expectedPath))
                {
                    return true;
                }

                foreach (string path in paths)
                {
                    if (PathsEqual(path, expectedPath))
                    {
                        return true;
                    }
                }

                return false;
            }

            public static LoadedModuleSnapshot FromProcess(Process? process)
            {
                Dictionary<string, List<string>> result = new(StringComparer.OrdinalIgnoreCase);
                if (!IsLiveProcess(process))
                {
                    return new LoadedModuleSnapshot(false, result);
                }

                try
                {
                    foreach (ProcessModule module in process!.Modules)
                    {
                        string name = Path.GetFileName(module.ModuleName);
                        string path = module.FileName;
                        if (!result.TryGetValue(name, out List<string>? paths))
                        {
                            paths = new List<string>();
                            result[name] = paths;
                        }

                        paths.Add(path);
                    }

                    return new LoadedModuleSnapshot(true, result);
                }
                catch
                {
                    return new LoadedModuleSnapshot(false, result);
                }
            }
        }
    }

    internal sealed class RuntimeDllItem
    {
        public string DisplayName { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string EnabledPath { get; set; } = string.Empty;
        public string DisabledPath { get; set; } = string.Empty;
        public string RepoSourcePath { get; set; } = string.Empty;
        public bool IsKnown { get; set; }
        public bool IsCore { get; set; }
        public bool HasManifest { get; set; }
        public string VersionText { get; set; } = string.Empty;
        public string ManifestPath { get; set; } = string.Empty;
        public bool IsEnabledOnDisk { get; set; }
        public bool IsDisabledOnDisk { get; set; }
        public bool IsRuntimeActive { get; set; }
        public bool CanToggle { get; set; }
        public int SortGroup { get; set; }
        public string RuntimeStatusText { get; set; } = string.Empty;
        public string DiskStatusText { get; set; } = string.Empty;

        public bool IsMissing => !IsEnabledOnDisk && !IsDisabledOnDisk && string.IsNullOrWhiteSpace(RepoSourcePath);
        public string ToggleLabel => IsEnabledOnDisk ? "Ligado" : IsDisabledOnDisk ? "Desligado" : !string.IsNullOrWhiteSpace(RepoSourcePath) ? "Instalar" : "Ausente";
        // Jarvis-UI (audit 2026-06-20): nome acessível do botão de toggle — descreve a ação, não só o estado.
        public string ToggleA11yName => $"{DisplayName} — {ToggleLabel}";

        // Jarvis-UI (Sprint E 2026-06-20, OPT-E3): hex do toggle pill promovido a tokens do StudioTokens
        // (RuntimeToggle*Fill/Border/Foreground + RuntimeRuntimeActive/Inactive). As props devolvem o IBrush
        // resolvido via TryFindResource; se faltar o resource (ex.: designer), caem num fallback estático com
        // a MESMA cor do token — zero mudança visual, mas nenhum hex vive no módulo. Os glows (BoxShadows)
        // não têm token natural (são composição), então derivam da cor do token Fill + transparência.
        public IBrush ToggleBrush => ResolveBrush(IsEnabledOnDisk ? "RuntimeToggleOnFillBrush"
                                                                  : IsDisabledOnDisk ? "RuntimeToggleOffFillBrush"
                                                                  : !string.IsNullOrWhiteSpace(RepoSourcePath) ? "RuntimeToggleInstallFillBrush"
                                                                                                                 : "RuntimeToggleMissingFillBrush");
        public IBrush ToggleForeground => ResolveBrush(IsDisabledOnDisk ? "RuntimeToggleOffForegroundBrush"
                                                                        : IsEnabledOnDisk || !string.IsNullOrWhiteSpace(RepoSourcePath) ? "RuntimeToggleOnForegroundBrush"
                                                                                                                                     : "RuntimeToggleMissingForegroundBrush");
        public IBrush ToggleBorderBrush => ResolveBrush(IsEnabledOnDisk ? "RuntimeToggleOnBorderBrush"
                                                                        : IsDisabledOnDisk ? "RuntimeToggleOffBorderBrush"
                                                                        : !string.IsNullOrWhiteSpace(RepoSourcePath) ? "RuntimeToggleInstallBorderBrush"
                                                                                                                       : "RuntimeToggleMissingBorderBrush");
        public BoxShadows ToggleGlow => IsEnabledOnDisk
            ? BuildGlow("on")
            : IsDisabledOnDisk
                ? BuildGlow("off")
                : !string.IsNullOrWhiteSpace(RepoSourcePath)
                    ? BuildGlow("install")
                    : default;
        public IBrush RuntimeBrush => ResolveBrush(IsRuntimeActive ? "RuntimeRuntimeActiveBrush" : "RuntimeRuntimeInactiveBrush");

        static IBrush ResolveBrush(string key)
        {
            if (Application.Current?.Resources.TryGetValue(key, out object? brush) == true && brush is IBrush resolved)
                return resolved;

            // ponytail: fallbacks com as cores neon reais — nunca cair no cinza TextMuted nos toggles
            return key switch
            {
                "RuntimeToggleOnFillBrush" => new SolidColorBrush(Color.Parse("#00E87A")),
                "RuntimeToggleOnBorderBrush" => new SolidColorBrush(Color.Parse("#7DFFB8")),
                "RuntimeToggleOnForegroundBrush" => new SolidColorBrush(Color.Parse("#FFFFFF")),
                "RuntimeToggleOffFillBrush" => new SolidColorBrush(Color.Parse("#FF2A4D")),
                "RuntimeToggleOffBorderBrush" => new SolidColorBrush(Color.Parse("#FF8A9E")),
                "RuntimeToggleOffForegroundBrush" => new SolidColorBrush(Color.Parse("#FFFFFF")),
                "RuntimeToggleInstallFillBrush" => new SolidColorBrush(Color.Parse("#FFD24A")),
                "RuntimeToggleInstallBorderBrush" => new SolidColorBrush(Color.Parse("#FFE8A0")),
                "RuntimeToggleInstallForegroundBrush" => new SolidColorBrush(Color.Parse("#1A1200")),
                "RuntimeRuntimeActiveBrush" => new SolidColorBrush(Color.Parse("#00FF88")),
                _ => new SolidColorBrush(Color.Parse("#9EB0C2")),
            };
        }

        static BoxShadows BuildGlow(string state) => state switch
        {
            "on" => BoxShadows.Parse("0 0 20 0 #A000E87A, 0 0 44 0 #5000E87A"),
            "off" => BoxShadows.Parse("0 0 22 0 #B0FF2A4D, 0 0 48 0 #50FF2A4D"),
            "install" => BoxShadows.Parse("0 0 18 0 #80FFD24A, 0 0 38 0 #30FFD24A"),
            _ => default,
        };
        public string BadgeText => HasManifest ? "plugin" : IsCore ? "core" : IsKnown ? "known" : "extra";
        public string MetadataSummary
        {
            get
            {
                List<string> parts = new();
                if (!string.IsNullOrWhiteSpace(VersionText))
                {
                    parts.Add($"v{VersionText}");
                }

                if (HasManifest)
                {
                    parts.Add("manifest");
                }

                return parts.Count == 0 ? string.Empty : string.Join(" · ", parts);
            }
        }
        public string PathSummary => !string.IsNullOrWhiteSpace(EnabledPath) ? EnabledPath : FileName;
    }
}

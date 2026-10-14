using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.AuroraOverlayLab
{
    internal partial class AuroraOverlayLab_DataModel : ObservableObject
    {
        private static readonly string KnownSteamRoot = Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.SteamCommonRoot, @"FINAL FANTASY FFX&FFX-2 HD Remaster");
        private const string SteamAppId = "359870";
        private static readonly char[] PathTrimChars = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ModulesPath))]
        [NotifyPropertyChangedFor(nameof(ConfigPath))]
        [NotifyPropertyChangedFor(nameof(ConfigDirectory))]
        [NotifyPropertyChangedFor(nameof(GameRootReady))]
        [NotifyPropertyChangedFor(nameof(GameRootHasFfxExe))]
        [NotifyPropertyChangedFor(nameof(GameRootHasModules))]
        private string gameRoot = string.Empty;

        [ObservableProperty] private bool overlayEnabled = true;
        [ObservableProperty] private bool d3d11Overlay = true;
        [ObservableProperty] private bool detailLabels;
        [ObservableProperty] private bool w2sScan = true;
        [ObservableProperty] private bool d3dSniffW2S = true;
        [ObservableProperty] private bool d3dSniffLightAfterW2S = true;
        [ObservableProperty] private bool d3dSniffProjectWithW2S;

        [ObservableProperty] private string w2sAddress = "0";
        [ObservableProperty] private string w2sScanStart = "0x40000000";
        [ObservableProperty] private string w2sScanBudgetMs = "2";
        [ObservableProperty] private string w2sScanCooldownMs = "100";
        [ObservableProperty] private string w2sScanMinRoots = "4";
        [ObservableProperty] private string d3dUpdateMs = "33";
        [ObservableProperty] private string d3dSniffMatrixMaxAgeMs = "3000";
        [ObservableProperty] private string d3dSniffProjectRefreshMs = "250";
        [ObservableProperty] private string d3dSniffAutoPauseHits = "3";

        [ObservableProperty] private bool gameOpen;
        [ObservableProperty] private bool dllDeployed;
        [ObservableProperty] private bool configExists;
        [ObservableProperty] private bool overlayFlagPresent;
        [ObservableProperty] private bool d3dFlagPresent;
        [ObservableProperty] private bool sniffFlagPresent;
        [ObservableProperty] private bool tempLogPresent;
        [ObservableProperty] private string runtimeSummary = "Refresh pendente.";
        [ObservableProperty] private string fileSummary = Strings.F2_no_target_loaded_0f8cc9e5;
        [ObservableProperty] private string statusSummary = Strings.F2_detect_the_game_directory_adjust_the_wor_3588a5f0;
        [ObservableProperty] private string lastWrittenSummary = "-";

        public Process_Service ProcService => Process_Service.Instance;
        public Project_Service ProjService => Project_Service.Instance;

        public string ModulesPath => string.IsNullOrWhiteSpace(GameRoot) ? string.Empty : Path.Combine(GameRoot, "modules");
        public string ConfigDirectory => string.IsNullOrWhiteSpace(ModulesPath) ? string.Empty : Path.Combine(ModulesPath, "config");
        public string ConfigPath => string.IsNullOrWhiteSpace(ConfigDirectory) ? string.Empty : Path.Combine(ConfigDirectory, "aurora_overlay.ini");
        public bool GameRootReady => IsLikelyGameRoot(GameRoot);
        public bool GameRootHasFfxExe => HasFfxExe(GameRoot);
        public bool GameRootHasModules => HasModules(GameRoot);
        public string HooksLogPath => Path.Combine(Path.GetTempPath(), "ffx-hooks.log");

        public AuroraOverlayLab_DataModel()
        {
            DetectGameRoot();
            LoadExistingConfig();
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
                StatusSummary = Strings.F2_target_detected_by_the_known_steam_path_3d8e85cc;
                return;
            }

            StatusSummary = Strings.F2_could_not_detect_the_game_directory_past_cde7a7f5;
            RefreshStatus();
        }

        public void LoadExistingConfig()
        {
            GameRoot = NormalizeGameRootInput(GameRoot);
            if (string.IsNullOrWhiteSpace(ConfigPath) || !File.Exists(ConfigPath))
            {
                RefreshStatus();
                return;
            }

            Dictionary<string, string> values = ReadIni(ConfigPath);
            OverlayEnabled = ReadBool(values, "enabled", OverlayEnabled);
            D3d11Overlay = string.Equals(ReadString(values, "mode", D3d11Overlay ? "d3d11" : "gdi"), "d3d11", StringComparison.OrdinalIgnoreCase);
            DetailLabels = ReadBool(values, "detail", DetailLabels);
            W2sScan = ReadBool(values, "w2s_scan", W2sScan);
            D3dSniffW2S = ReadBool(values, "d3d_sniff_w2s", D3dSniffW2S);
            D3dSniffLightAfterW2S = ReadBool(values, "d3d_sniff_light_after_w2s", D3dSniffLightAfterW2S);
            D3dSniffProjectWithW2S = ReadBool(values, "d3d_sniff_project_with_w2s", D3dSniffProjectWithW2S);
            W2sAddress = ReadString(values, "w2s_addr", W2sAddress);
            W2sScanStart = ReadString(values, "w2s_scan_start", W2sScanStart);
            W2sScanBudgetMs = ReadString(values, "w2s_scan_budget_ms", W2sScanBudgetMs);
            W2sScanCooldownMs = ReadString(values, "w2s_scan_cooldown_ms", W2sScanCooldownMs);
            W2sScanMinRoots = ReadString(values, "w2s_scan_min_roots", W2sScanMinRoots);
            D3dUpdateMs = ReadString(values, "d3d_update_ms", D3dUpdateMs);
            D3dSniffMatrixMaxAgeMs = ReadString(values, "d3d_sniff_matrix_max_age_ms", D3dSniffMatrixMaxAgeMs);
            D3dSniffProjectRefreshMs = ReadString(values, "d3d_sniff_project_refresh_ms", D3dSniffProjectRefreshMs);
            D3dSniffAutoPauseHits = ReadString(values, "d3d_sniff_autopause_hits", D3dSniffAutoPauseHits);

            StatusSummary = $"Config carregada de {ConfigPath}.";
            RefreshStatus();
        }

        public void ApplyConfig()
        {
            GameRoot = NormalizeGameRootInput(GameRoot);
            if (!GameRootReady)
            {
                StatusSummary = Strings.F2_invalid_target_specify_the_directory_con_2718156e;
                RefreshStatus();
                return;
            }

            try
            {
                Directory.CreateDirectory(ConfigDirectory);

                List<string> notes = new();
                int scanBudget = ClampInt(W2sScanBudgetMs, 2, 1, 20, "w2s_scan_budget_ms", notes);
                int scanCooldown = ClampInt(W2sScanCooldownMs, 100, 0, 5000, "w2s_scan_cooldown_ms", notes);
                int minRoots = ClampInt(W2sScanMinRoots, 4, 1, 32, "w2s_scan_min_roots", notes);
                int updateMs = ClampInt(D3dUpdateMs, 33, 0, 1000, "d3d_update_ms", notes);
                int matrixMaxAge = ClampInt(D3dSniffMatrixMaxAgeMs, 3000, 100, 10000, "d3d_sniff_matrix_max_age_ms", notes);
                int projectRefresh = ClampInt(D3dSniffProjectRefreshMs, 250, 0, 5000, "d3d_sniff_project_refresh_ms", notes);
                int autoPauseHits = ClampInt(D3dSniffAutoPauseHits, 3, 0, 100, "d3d_sniff_autopause_hits", notes);
                string scanStart = NormalizeAddress(W2sScanStart, "0x40000000", "w2s_scan_start", notes);
                string w2sAddr = NormalizeAddress(W2sAddress, "0", "w2s_addr", notes);

                StringBuilder sb = new();
                sb.AppendLine("; Written by FFX Project Editor / Aurora Overlay Lab.");
                sb.AppendLine("; Env vars still override these values for one-off lab runs.");
                sb.AppendLine("[aurora]");
                sb.AppendLine($"enabled={(OverlayEnabled ? 1 : 0)}");
                sb.AppendLine($"mode={(D3d11Overlay ? "d3d11" : "gdi")}");
                sb.AppendLine($"detail={(DetailLabels ? 1 : 0)}");
                sb.AppendLine($"w2s_scan={(W2sScan ? 1 : 0)}");
                sb.AppendLine($"w2s_addr={w2sAddr}");
                sb.AppendLine($"w2s_scan_start={scanStart}");
                sb.AppendLine($"w2s_scan_budget_ms={scanBudget}");
                sb.AppendLine($"w2s_scan_cooldown_ms={scanCooldown}");
                sb.AppendLine($"w2s_scan_min_roots={minRoots}");
                sb.AppendLine($"d3d_update_ms={updateMs}");
                sb.AppendLine($"d3d_sniff_w2s={(D3dSniffW2S ? 1 : 0)}");
                sb.AppendLine($"d3d_sniff_matrix_max_age_ms={matrixMaxAge}");
                sb.AppendLine($"d3d_sniff_project_refresh_ms={projectRefresh}");
                sb.AppendLine($"d3d_sniff_autopause_hits={autoPauseHits}");
                sb.AppendLine($"d3d_sniff_project_with_w2s={(D3dSniffProjectWithW2S ? 1 : 0)}");
                sb.AppendLine($"d3d_sniff_light_after_w2s={(D3dSniffLightAfterW2S ? 1 : 0)}");
                File.WriteAllText(ConfigPath, sb.ToString(), Encoding.ASCII);

                SyncFlag("aurora_overlay.flag", OverlayEnabled);
                SyncFlag("aurora_overlay_d3d11.flag", OverlayEnabled && D3d11Overlay);
                SyncFlag("aurora_w2s_sniff.flag", OverlayEnabled && D3dSniffW2S);

                LastWrittenSummary = notes.Count == 0
                    ? "Config aplicada sem ajustes."
                    : "Config aplicada com clamp: " + string.Join("; ", notes);
                StatusSummary = OverlayEnabled
                    ? Strings.F2_aurora_armed_restart_open_ffx_for_the_dl_4bb61480
                    : Strings.F2_aurora_disabled_flags_removed_the_ini_fi_968f5684;
            }
            catch (Exception ex)
            {
                LastWrittenSummary = "Config nao aplicada.";
                StatusSummary = $"Falha ao gravar config Aurora: {ex.Message}";
            }
            finally
            {
                RefreshStatus();
            }
        }

        public void DisableOverlay()
        {
            OverlayEnabled = false;
            ApplyConfig();
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
                StatusSummary = Strings.F2_launch_via_steam_sent_appid_359870_confi_e42e12c1;
            }
            catch (Exception ex)
            {
                StatusSummary = $"Falha ao abrir FFX pelo Steam: {ex.Message}";
            }

            RefreshStatus();
        }

        public void OpenConfigFolder()
        {
            try
            {
                GameRoot = NormalizeGameRootInput(GameRoot);
                if (!GameRootReady)
                {
                    StatusSummary = Strings.F2_invalid_target_first_specify_the_game_di_d676703e;
                    RefreshStatus();
                    return;
                }

                if (!Directory.Exists(ConfigDirectory))
                {
                    Directory.CreateDirectory(ConfigDirectory);
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = ConfigDirectory,
                    UseShellExecute = true
                });
                StatusSummary = "Pasta de config aberta.";
            }
            catch (Exception ex)
            {
                StatusSummary = $"Falha ao abrir pasta: {ex.Message}";
            }
        }

        public void OpenTempLog()
        {
            try
            {
                if (!File.Exists(HooksLogPath))
                {
                    StatusSummary = $"Log ainda nao existe em {HooksLogPath}. Abra o jogo com a DLL deployada.";
                    RefreshStatus();
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = HooksLogPath,
                    UseShellExecute = true
                });
                StatusSummary = "Log ffx-hooks aberto.";
            }
            catch (Exception ex)
            {
                StatusSummary = $"Falha ao abrir log: {ex.Message}";
            }

            RefreshStatus();
        }

        public void RefreshStatus()
        {
            Process_Service.Instance.AutoDetect();
            Process? liveProcess = TryGetLiveFfxProcess();
            string? liveRoot = TryGetProcessRoot(liveProcess);
            bool targetMismatch = GameRootReady &&
                                  !string.IsNullOrWhiteSpace(liveRoot) &&
                                  !PathsEqual(GameRoot, liveRoot);
            GameOpen = liveProcess != null;
            DllDeployed = File.Exists(Path.Combine(ModulesPath, "ffx-hooks.dll"));
            ConfigExists = File.Exists(ConfigPath);
            OverlayFlagPresent = File.Exists(Path.Combine(ConfigDirectory, "aurora_overlay.flag"));
            D3dFlagPresent = File.Exists(Path.Combine(ConfigDirectory, "aurora_overlay_d3d11.flag"));
            SniffFlagPresent = File.Exists(Path.Combine(ConfigDirectory, "aurora_w2s_sniff.flag"));
            TempLogPresent = File.Exists(HooksLogPath);

            RuntimeSummary = GameOpen
                ? targetMismatch
                    ? $"FFX.exe PID {liveProcess?.Id} vivo{WindowTitleLabel(liveProcess)}, mas esta em {liveRoot}. O alvo selecionado e outro: {GameRoot}."
                    : $"FFX.exe PID {liveProcess?.Id} vivo{WindowTitleLabel(liveProcess)}. Mudancas do ini entram no proximo boot da DLL."
                : $"FFX.exe nao detectado. Aplique a config e abra/reinicie o jogo. Log: {HooksLogPath}";

            string dllState = DllDeployed
                ? $"ffx-hooks.dll encontrado ({FileSizeLabel(Path.Combine(ModulesPath, "ffx-hooks.dll"))})."
                : Strings.F2_ffx_hooks_dll_not_found_in_modules_8e4423c4;
            string configState = ConfigExists ? "ini presente" : "ini ausente";
            string flags = $"flags overlay={Flag(OverlayFlagPresent)} d3d={Flag(D3dFlagPresent)} sniff={Flag(SniffFlagPresent)}";
            string rootShape = GameRootHasFfxExe ? string.Empty : "modules-only; ";
            FileSummary = GameRootReady
                ? $"{rootShape}{dllState} {configState}; {flags}. Alvo: {GameRoot}"
                : "Alvo invalido ou vazio.";
        }

        private void SyncFlag(string name, bool enabled)
        {
            string path = Path.Combine(ConfigDirectory, name);
            if (enabled)
            {
                File.WriteAllText(path, "1\r\n", Encoding.ASCII);
                return;
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static int ClampInt(string text, int fallback, int min, int max, string name, List<string> notes)
        {
            if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                notes.Add($"{name} invalido -> {fallback}");
                return fallback;
            }

            int clamped = Math.Clamp(value, min, max);
            if (clamped != value)
            {
                notes.Add($"{name} {value} -> {clamped}");
            }

            return clamped;
        }

        private static string NormalizeAddress(string text, string fallback, string name, List<string> notes)
        {
            string value = text.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                notes.Add($"{name} vazio -> {fallback}");
                return fallback;
            }

            try
            {
                string digits = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? value[2..]
                    : value;
                _ = Convert.ToUInt32(digits, value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 16 : 10);
                return value;
            }
            catch
            {
                notes.Add($"{name} invalido -> {fallback}");
                return fallback;
            }
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

        private static Dictionary<string, string> ReadIni(string path)
        {
            Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#") || line.StartsWith("["))
                {
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                values[line[..eq].Trim()] = line[(eq + 1)..].Trim();
            }

            return values;
        }

        private static bool ReadBool(Dictionary<string, string> values, string key, bool fallback)
        {
            if (!values.TryGetValue(key, out string? raw))
            {
                return fallback;
            }

            return raw.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   raw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   raw.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadString(Dictionary<string, string> values, string key, string fallback)
        {
            return values.TryGetValue(key, out string? raw) && !string.IsNullOrWhiteSpace(raw)
                ? raw
                : fallback;
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

        private static string WindowTitleLabel(Process? process)
        {
            try
            {
                string title = process?.MainWindowTitle ?? string.Empty;
                return string.IsNullOrWhiteSpace(title) ? string.Empty : $" ({title})";
            }
            catch
            {
                return string.Empty;
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
    }
}

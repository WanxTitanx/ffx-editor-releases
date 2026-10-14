using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Services;

namespace FFXProjectEditor.Core
{
    /// <summary>
    /// P3-L2 (docs/ai/P3_RUNTIME_CONTRACTS_2026-07-31.md): health check of the RUNTIME environment —
    /// the installed FFX HD Steam game (FFX.exe + version + region), the modules\ payload
    /// (ffx-probe.dll / FfxHooksDll.dll + .flag files) and the live DINPUT8 probe (MMF Command Block).
    ///
    /// Contract (P3 prompt L410-416): detector de versão/health check · contrato de instalação/
    /// configuração, permissão e path · fallback quando ausente · erro acionável, sem stack trace cru ·
    /// matriz de compatibilidade. This probe NEVER writes to the game and NEVER throws a raw exception to
    /// the UI: every failure becomes a typed <see cref="EnvironmentIssue"/> with an actionable message.
    /// </summary>
    public sealed class GameEnvironmentProbe
    {
        /// <summary>Steam app folder name of FFX HD (both games ship in one folder).</summary>
        public const string SteamAppName = "FINAL FANTASY FFX&FFX-2 HD Remaster";

        /// <summary>Environment override: FFX_GAME_ROOT=&lt;dir with FFX.exe&gt; (also used by tests).</summary>
        public const string GameRootEnvVar = "FFX_GAME_ROOT";

        // Compatibility matrix (P3 doc §3): FFX.exe = the single supported runtime target.
        const string ExeName = "FFX.exe";
        const string ProbeModuleName = "ffx-probe.dll";
        const string HooksModuleName = "ffx-hooks.dll";
        // Legacy spellings seen on real installs — the shipped DLL is lowercase
        // ffx-hooks.dll, but older packages used FfxHooksDll.dll.
        static readonly string[] HooksModuleAliases = { HooksModuleName, "FfxHooksDll.dll" };
        const string ConfigDirName = "config";

        /// <summary>
        /// Full environment report. Never throws: all failures are collected as typed issues
        /// (state Ok / Warning / Error via <see cref="State"/>).
        /// </summary>
        public GameEnvironmentReport Detect(string? gameRootOverride = null)
        {
            var issues = new List<EnvironmentIssue>();
            // WHY: env vars and persisted settings can carry a trailing space (e.g. a `set X=Y `
            // line in a .bat). Directory.Exists tolerates it but Path.Combine produces
            // "D:\game \FFX.exe" which File.Exists rejects -> false FFX_EXE_MISSING. Trim first.
            string? gameRoot = (gameRootOverride ?? DiscoverGameRoot())?.Trim();

            if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
            {
                issues.Add(new EnvironmentIssue(
                    "GAME_NOT_FOUND",
                    Strings.F2_could_not_locate_the_ffx_hd_installation_deacc222,
                    string.Format(Strings.U_Env_GameNotFoundAction, SteamAppName)));
                return Report(gameRoot, issues);
            }

            string exePath = Path.Combine(gameRoot, ExeName);
            if (!File.Exists(exePath))
            {
                issues.Add(new EnvironmentIssue(
                    "FFX_EXE_MISSING",
                    Strings.F2_the_specified_folder_exists_but_does_not_0713f6fb,
                    Strings.U_Env_ExeMissingAction));
                return Report(gameRoot, issues);
            }

            string? gameVersion = ReadFileVersion(exePath);
            GameRegion region = DetectRegion(gameRoot);
            string modulesDir = Path.Combine(gameRoot, "modules");
            bool probePresent = ModuleFileExists(modulesDir, ProbeModuleName);
            bool hooksPresent = ModuleFileExists(modulesDir, HooksModuleAliases);
            var activeFlags = EnumerateActiveFlags(gameRoot);

            if (!probePresent || !hooksPresent)
            {
                issues.Add(new EnvironmentIssue(
                    "RUNTIME_MODULES_MISSING",
                    Strings.U_Env_ModulesMissingMsg,
                    Strings.U_Env_ModulesMissingAction));
            }

            // Live probe (MMF Command Block) — read-only inspection; nothing is armed here.
            bool probeAttached = FfxProbe_Service.Instance.IsAttached;
            bool probeHooked = false;
            uint heartbeat = 0;
            if (probeAttached)
            {
                probeHooked = FfxProbe_Service.Instance.IsHooked;
                heartbeat = FfxProbe_Service.Instance.Heartbeat;
            }
            else if (probePresent)
            {
                issues.Add(new EnvironmentIssue(
                    "PROBE_NOT_ATTACHED",
                    Strings.U_Env_ProbeNotAttachedMsg,
                    Strings.U_Env_ProbeNotAttachedAction));
            }

            return new GameEnvironmentReport
            {
                State = issues.Count == 0 ? EnvironmentCheckState.Ok : EnvironmentCheckState.Warning,
                GameRoot = Path.GetFullPath(gameRoot),
                GameFound = true,
                GameVersion = gameVersion,
                Region = region,
                ProbeModulePresent = probePresent,
                HooksModulePresent = hooksPresent,
                ActiveHookFlags = activeFlags,
                ProbeAttached = probeAttached,
                ProbeHooked = probeHooked,
                Heartbeat = heartbeat,
                Issues = issues,
                ScanTimestamp = DateTimeOffset.UtcNow,
            };
        }



        /// <summary>
        /// Discover the game root: FFX_GAME_ROOT env → explicit/workspace-derived root
        /// (<see cref="Project_Service.Path_GameInstallRoot"/>) → Steam registry →
        /// libraryfolders.vdf. Returns null when nothing is found (fallback: caller shows
        /// the actionable error). The project step fixes a Linux false negative where a
        /// workspace nested inside the install still reported "Game Not Found" because
        /// the registry probe is a Windows no-op (dashboard regression 2026-09-14).
        /// </summary>
        public string? DiscoverGameRoot()
        {
            string? envRoot = Environment.GetEnvironmentVariable(GameRootEnvVar);
            if (!string.IsNullOrWhiteSpace(envRoot) && Directory.Exists(envRoot))
                return envRoot;

            string? projectRoot = Project_Service.Instance.Path_GameInstallRoot;
            if (!string.IsNullOrWhiteSpace(projectRoot) && Directory.Exists(projectRoot))
                return projectRoot;

            string? steamPath = TryReadSteamPathFromRegistry();
            if (!string.IsNullOrWhiteSpace(steamPath))
            {
                string? fromLibraries = FindInLibraryFolders(steamPath, SteamAppName, out _);
                if (fromLibraries != null)
                    return fromLibraries;
                string direct = Path.Combine(steamPath, "steamapps", "common", SteamAppName);
                if (Directory.Exists(direct))
                    return direct;
            }

            return null;
        }

        /// <summary>
        /// Parse steam's libraryfolders.vdf and look for the app folder. Testable with a synthetic vdf.
        /// </summary>
        public static string? FindInLibraryFolders(string steamRoot, string appName, out string? libraryFileUsed)
        {
            libraryFileUsed = null;
            string vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf))
                return null;
            libraryFileUsed = vdf;

            foreach (string line in File.ReadAllLines(vdf))
            {
                string trimmed = line.Trim();
                int keyIdx = trimmed.IndexOf("\"path\"", StringComparison.OrdinalIgnoreCase);
                if (keyIdx < 0)
                    continue;
                int valueOpen = trimmed.IndexOf('"', keyIdx + 6);
                if (valueOpen < 0)
                    continue;
                int valueClose = trimmed.IndexOf('"', valueOpen + 1);
                if (valueClose <= valueOpen)
                    continue;
                string candidate = trimmed.Substring(valueOpen + 1, valueClose - valueOpen - 1)
                    .Replace("\\\\", "\\"); // vdf escapes backslashes; Steam real files use them
                if (string.IsNullOrWhiteSpace(candidate) || !Path.IsPathRooted(candidate))
                    continue;
                string appDir = Path.Combine(candidate, "steamapps", "common", appName);
                if (Directory.Exists(appDir))
                    return appDir;
            }
            return null;
        }

        // ── helpers ───────────────────────────────────────────────────────────────────────────────

        // Module DLL filenames drift in casing across releases (ffx-hooks.dll vs FfxHooksDll.dll).
        // File.Exists is case-sensitive on Linux filesystems, so match directory contents
        // case-insensitively instead of probing a single literal name — Windows NTFS folded
        // the mismatch silently, which is why the false negative only showed on Linux.
        static bool ModuleFileExists(string modulesDir, params string[] acceptedNames)
        {
            if (!Directory.Exists(modulesDir))
                return false;
            foreach (string file in Directory.EnumerateFiles(modulesDir))
            {
                string name = Path.GetFileName(file);
                foreach (string accepted in acceptedNames)
                {
                    if (string.Equals(name, accepted, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        static GameEnvironmentReport Report(string? gameRoot, List<EnvironmentIssue> issues)
        {
            return new GameEnvironmentReport
            {
                State = EnvironmentCheckState.Error,
                GameRoot = string.IsNullOrWhiteSpace(gameRoot) ? null : Path.GetFullPath(gameRoot),
                GameFound = false, // Report is only reached when FFX.exe was NOT confirmed
                GameVersion = null,
                Region = GameRegion.Unknown,
                ProbeModulePresent = false,
                HooksModulePresent = false,
                ActiveHookFlags = Array.Empty<string>(),
                ProbeAttached = false,
                ProbeHooked = false,
                Heartbeat = 0,
                Issues = issues,
                ScanTimestamp = DateTimeOffset.UtcNow,
            };
        }

        static string? TryReadSteamPathFromRegistry()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                return key?.GetValue("SteamPath") as string;
            }
            catch
            {
                return null; // registry access can fail on locked-down machines — fallback to candidates
            }
        }

        static string? ReadFileVersion(string exePath)
        {
            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(exePath);
                return string.IsNullOrWhiteSpace(info.FileVersion) ? null : info.FileVersion;
            }
            catch
            {
                return null;
            }
        }

        static GameRegion DetectRegion(string gameRoot)
        {
            string dataDir = Path.Combine(gameRoot, "data");
            if (!Directory.Exists(dataDir))
                return GameRegion.Unknown;
            if (Directory.Exists(Path.Combine(dataDir, "jppc")))
                return GameRegion.JP;
            if (Directory.Exists(Path.Combine(dataDir, "intl")) || Directory.Exists(Path.Combine(dataDir, "us")))
                return GameRegion.US;
            if (Directory.Exists(Path.Combine(dataDir, "eu")))
                return GameRegion.EU;
            return GameRegion.Unknown;
        }

        static IReadOnlyList<string> EnumerateActiveFlags(string gameRoot)
        {
            var flags = new List<string>();
            foreach (string dir in new[] { Path.Combine(gameRoot, ConfigDirName), gameRoot })
            {
                if (!Directory.Exists(dir))
                    continue;
                try
                {
                    flags.AddRange(Directory.EnumerateFiles(dir, "*.flag")
                        .Select(Path.GetFileName)
                        .OrderBy(static n => n, StringComparer.OrdinalIgnoreCase));
                }
                catch (UnauthorizedAccessException) { }
            }
            return flags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>Aggregate state of the runtime environment check.</summary>
    public enum EnvironmentCheckState
    {
        Ok = 0,
        Warning = 1,
        Error = 2,
    }

    /// <summary>Region heuristic for the installed game (data\ subfolders).</summary>
    public enum GameRegion
    {
        Unknown = 0,
        JP = 1,
        US = 2,
        EU = 3,
    }

    /// <summary>Typed, actionable failure — never a raw stack trace (P3 contract).</summary>
    public sealed record EnvironmentIssue(string Code, string Message, string ActionableMessage);

    /// <summary>Read-only environment report consumable by the UI (UiBridge pattern).</summary>
    public sealed record GameEnvironmentReport
    {
        public required EnvironmentCheckState State { get; init; }
        public required string? GameRoot { get; init; }
        public required bool GameFound { get; init; }
        public required string? GameVersion { get; init; }
        public required GameRegion Region { get; init; }
        public required bool ProbeModulePresent { get; init; }
        public required bool HooksModulePresent { get; init; }
        public required IReadOnlyList<string> ActiveHookFlags { get; init; }
        public required bool ProbeAttached { get; init; }
        public required bool ProbeHooked { get; init; }
        public required uint Heartbeat { get; init; }
        public required IReadOnlyList<EnvironmentIssue> Issues { get; init; }
        public required DateTimeOffset ScanTimestamp { get; init; }

        public bool IsHealthy => State == EnvironmentCheckState.Ok;
    }
}


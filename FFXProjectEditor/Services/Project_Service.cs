using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Ps3;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Utils;
using System;
using System.Diagnostics;
using System.IO;

namespace FFXProjectEditor.Services
{
    public partial class Project_Service : SingletonBase<Project_Service>
    {
        /******************************************
         * STATE
         ******************************************/
        // master folder
        [ObservableProperty][NotifyPropertyChangedFor(nameof(IsProjectLoaded))] public string? projectPath; // This should be private set but can't do it with ObservableProperty
        public bool IsProjectLoaded => ProjectPath != null && ProjectPath != "" && Directory.Exists(ProjectPath);

        /******************************************
         * Public functions
         ******************************************/

        public static bool IsPathValid(string projectPath)
        {
            if (File.Exists(projectPath))
            {
                Debug.WriteLine(Strings.F2_this_is_a_file_90b8de62);
                return false;
            }
            if (!Directory.Exists(projectPath))
            {
                Debug.WriteLine("Directory doesn't exist");
                return false;
            }

            string normalizedPath = NormalizePath(projectPath);
            if (!string.Equals(Path.GetFileName(normalizedPath), "master", StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine("Directory is not the master folder");
                return false;
            }
            return true;
        }

        public void LoadProject(string projectPath)
        {
            string normalizedPath = NormalizePath(projectPath);

            if (!Directory.Exists(normalizedPath))
                throw new System.Exception("[Project_Service] Provided folder doesn't exist");

            if (!string.Equals(Path.GetFileName(normalizedPath), "master", StringComparison.OrdinalIgnoreCase))
                throw new System.Exception("[Project_Service] Provided folder is not master folder");

            ProjectPath = normalizedPath;
            SaveLastProject(normalizedPath);
            // The derived game install root may move with the workspace — notify so the
            // health card / badges refresh even when the user never picks a game folder.
            OnPropertyChanged(nameof(Path_GameInstallRoot));
            OnPropertyChanged(nameof(Path_OutputRoot));
            KernelMonsterMagicLiveSync.SyncProject();
        }

        /******************************************
         * Persistence (load once, remembered forever)
         ******************************************/
        private static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FFXProjectEditor", "last-project.txt");

        // Persist the chosen master folder so the next launch reopens it automatically.
        // Best-effort: a failed settings write must never block loading the project.
        private static void SaveLastProject(string projectPath)
        {
            try
            {
                string? dir = Path.GetDirectoryName(SettingsPath);
                if (dir != null) Directory.CreateDirectory(dir);
                File.WriteAllText(SettingsPath, projectPath);
            }
            catch { /* ignore — persistence is a convenience, not a requirement */ }
        }

        // The last workspace the user loaded, if it is still a valid master folder; else null.
        public static string? LoadLastProject()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return null;
                string saved = File.ReadAllText(SettingsPath).Trim();
                return IsPathValid(saved) ? NormalizePath(saved) : null;
            }
            catch { return null; }
        }

        // Manual root overrides (session-scoped). Let the user point Extras at the EXTRACTED
        // ffx_ps2 / ps3data tree when the loaded project is the Steam install (data\mods, empty of source assets).
        public static string? FfxPs2RootOverride { get; set; }
        public static string? Ps3DataRootOverride { get; set; }
        public string? Path_FfxPs2Root => (!string.IsNullOrWhiteSpace(FfxPs2RootOverride) && Directory.Exists(FfxPs2RootOverride)) ? FfxPs2RootOverride : TryResolveFfxPs2Root(ProjectPath);

        // FfxPs2RootOverride is a static session override; callers notify so listeners
        // (auto music import, health cards) re-check the newly reachable extraction tree.
        public void NotifyFfxPs2RootChanged() => OnPropertyChanged(nameof(Path_FfxPs2Root));
        public string? Path_Ps3DataRoot => (!string.IsNullOrWhiteSpace(Ps3DataRootOverride) && Directory.Exists(Ps3DataRootOverride)) ? Ps3DataRootOverride : TryResolvePs3DataRoot(ProjectPath);
        public bool HasFfxPs2Root => Path_FfxPs2Root != null && Directory.Exists(Path_FfxPs2Root);
        public bool HasPs3DataRoot => Path_Ps3DataRoot != null && Directory.Exists(Path_Ps3DataRoot);

        /// <summary>
        /// Steam/game install root (parent of <c>data\mods\...</c>). The explicit pick wins over
        /// the workspace-derived root: game folder and master workspace are independent
        /// selections — the workspace is not required to live inside the install.
        /// </summary>
        public string? Path_GameInstallRoot =>
            (!string.IsNullOrWhiteSpace(GameRootOverride) && Directory.Exists(GameRootOverride))
                ? GameRootOverride
                : TryResolveGameInstallRoot(ProjectPath);

        /******************************************
         * Game install root — explicit pick (persisted)
         ******************************************/
        // User-chosen FFX HD install folder. Session-scoped static like the other root
        // overrides; hydrated once at startup via LoadLastGameRoot (App.OnFrameworkInitializationCompleted).
        public static string? GameRootOverride { get; set; }

        private static string GameRootSettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FFXProjectEditor", "game-root.txt");

        // A folder "is" the FFX HD install when it holds FFX.exe or the loader's modules\
        // dir — same heuristic as RuntimeDllManager_DataModel.IsLikelyGameRoot.
        public static bool IsGameRootPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return false;
            string normalized = NormalizePath(path);
            return File.Exists(Path.Combine(normalized, "FFX.exe"))
                || Directory.Exists(Path.Combine(normalized, "modules"));
        }

        // Picker candidates often land inside the install (data\, data\mods\, even the
        // master workspace under data\mods\ffx_ps2\ffx\master). Climbing ancestors and
        // taking the first plausible root keeps the pick usable instead of rejecting it.
        public static string? ResolveGameRootCandidate(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return null;
            var cursor = new DirectoryInfo(NormalizePath(path));
            while (cursor != null)
            {
                if (IsGameRootPath(cursor.FullName))
                    return cursor.FullName;
                cursor = cursor.Parent;
            }
            return null;
        }

        // UI boundary for the game-folder picker: validates the pick (itself or an
        // ancestor must look like the install), stores it, persists it. Returns false
        // so async-void handlers can show a dialog instead of throwing.
        public bool SetGameRoot(string folder)
        {
            string? resolved = ResolveGameRootCandidate(folder);
            if (resolved == null)
                return false;
            GameRootOverride = resolved;
            SaveLastGameRoot(resolved);
            OnPropertyChanged(nameof(Path_GameInstallRoot));
            OnPropertyChanged(nameof(Path_OutputRoot));
            return true;
        }

        public void ClearGameRoot()
        {
            GameRootOverride = null;
            try
            {
                if (File.Exists(GameRootSettingsPath))
                    File.Delete(GameRootSettingsPath);
            }
            catch { /* persistence is best-effort */ }
            OnPropertyChanged(nameof(Path_GameInstallRoot));
            OnPropertyChanged(nameof(Path_OutputRoot));
        }

        private static void SaveLastGameRoot(string gameRoot)
        {
            try
            {
                string? dir = Path.GetDirectoryName(GameRootSettingsPath);
                if (dir != null) Directory.CreateDirectory(dir);
                File.WriteAllText(GameRootSettingsPath, gameRoot);
            }
            catch { /* ignore — persistence is a convenience, not a requirement */ }
        }

        // The last game folder the user picked, if it still exists; else null.
        public static string? LoadLastGameRoot()
        {
            try
            {
                if (!File.Exists(GameRootSettingsPath)) return null;
                string saved = File.ReadAllText(GameRootSettingsPath).Trim();
                return Directory.Exists(saved) ? saved : null;
            }
            catch { return null; }
        }

        /// <summary>ff10-file-loader PS3 magic overlay (<c>data\mods\FFX_Data\...\magic</c>) — LAB clones 716+ live here.</summary>
        public string? Path_ModsPs3MagicRoot
        {
            get
            {
                string? gameRoot = Path_GameInstallRoot;
                return gameRoot == null
                    ? null
                    : Path.Combine(gameRoot, "data", "mods", MagicEffectClonePipeline.VirtualPs3MagicRoot);
            }
        }

        public string? Path_MagicDllRoot
        {
            get
            {
                string? gameRoot = Path_GameInstallRoot;
                return gameRoot == null
                    ? null
                    : Path.Combine(gameRoot, "magicFiles", "FFX");
            }
        }

        /******************************************
         * Output root — where generated/deployed content lands
         ******************************************/
        // WHY: the FFX External File Loader (RuntimeDllManager catalog) reads loose mod
        // files from <c>data\mods</c> under the install root, mirroring the master tree
        // (e.g. data\mods\FFX_Data\GameData\PS3Data\magic). When that loader is deployed
        // (enabled DLL present, not .disabled) generated content must go there — with the
        // full relative subpath preserved by each writer — or the game never sees it.
        public const string ExternalFileLoaderDllName = "ff10-file-loader.dll";

        /// <summary>The ff10 external file loader is deployed and enabled under <c>modules\</c>.</summary>
        public bool IsExternalFileLoaderEnabled =>
            Path_GameInstallRoot is string root
            && File.Exists(Path.Combine(root, "modules", ExternalFileLoaderDllName));

        /// <summary>
        /// Deploy root for generated content. The explicit user pick wins; otherwise it is
        /// auto-resolved: the safe <c>output_staging</c> beside the install by default, or
        /// <c>data\mods</c> when the external loader is enabled — or the workspace itself
        /// already lives inside the loader's scan tree (<c>data\mods\ffx_ps2\ffx\master</c>) —
        /// so output lands exactly where the loader picks it up. Per-module subpaths
        /// (FFX_Data\GameData\PS3Data\…, ffx_ps2\ffx\…) are appended by the individual
        /// writers on top of this root, preserving the full path structure.
        /// </summary>
        public string? Path_OutputRoot =>
            (!string.IsNullOrWhiteSpace(OutputRootOverride) && Directory.Exists(OutputRootOverride))
                ? OutputRootOverride
                : ResolveAutoOutputRoot();

        private string? ResolveAutoOutputRoot()
        {
            string? gameRoot = Path_GameInstallRoot;
            if (gameRoot == null)
                return null;

            string modsRoot = Path.Combine(gameRoot, "data", "mods");
            if (IsExternalFileLoaderEnabled
                || (ProjectPath != null && IsSubPathOf(ProjectPath, modsRoot)))
            {
                return modsRoot;
            }
            return Path.Combine(gameRoot, "output_staging");
        }

        /******************************************
         * Output root — explicit pick (persisted)
         ******************************************/
        // User-chosen deploy folder. Session-scoped static like GameRootOverride; hydrated at
        // startup via LoadLastOutputRoot. An explicit pick pins the destination — use
        // ClearOutputRoot to return to auto-resolution.
        public static string? OutputRootOverride { get; set; }

        private static string OutputRootSettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FFXProjectEditor", "output-root.txt");

        // UI boundary for the output-folder picker: the folder must already exist (the
        // picker only offers existing dirs; the check guards programmatic callers).
        public bool SetOutputRoot(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return false;
            OutputRootOverride = NormalizePath(folder);
            SaveLastOutputRoot(OutputRootOverride);
            OnPropertyChanged(nameof(Path_OutputRoot));
            return true;
        }

        public void ClearOutputRoot()
        {
            OutputRootOverride = null;
            try
            {
                if (File.Exists(OutputRootSettingsPath))
                    File.Delete(OutputRootSettingsPath);
            }
            catch { /* persistence is best-effort */ }
            OnPropertyChanged(nameof(Path_OutputRoot));
        }

        private static void SaveLastOutputRoot(string outputRoot)
        {
            try
            {
                string? dir = Path.GetDirectoryName(OutputRootSettingsPath);
                if (dir != null) Directory.CreateDirectory(dir);
                File.WriteAllText(OutputRootSettingsPath, outputRoot);
            }
            catch { /* ignore — persistence is a convenience, not a requirement */ }
        }

        // The last output folder the user picked, if it still exists; else null.
        public static string? LoadLastOutputRoot()
        {
            try
            {
                if (!File.Exists(OutputRootSettingsPath)) return null;
                string saved = File.ReadAllText(OutputRootSettingsPath).Trim();
                return Directory.Exists(saved) ? saved : null;
            }
            catch { return null; }
        }

        private static bool IsSubPathOf(string path, string root)
        {
            string full = Path.GetFullPath(path);
            string basePath = Path.GetFullPath(root);
            return full.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        /******************************************
         * Files
         ******************************************/
        public string Path_Btl => Path.Combine(ProjectPath, "jppc", "battle", "btl");
        public string Path_BattleMon => Path.Combine(ProjectPath, "jppc", "battle", "mon"); // monster_*.bin (AI/worker/stats)
        public string Path_Event => Path.Combine(ProjectPath, "jppc", "event", "obj");
        public string Path_Kernel => Path.Combine(ProjectPath, "jppc", "battle", "kernel");
        public string Path_Menu => Path.Combine(ProjectPath, "jppc", "menu");
        public string Path_Abmap => Path.Combine(Path_Menu, "abmap");
        public string Path_KernelEncounterTable => Path.Combine(Path_Kernel, "btl.bin");
        public string Path_KernelTreasure => Path.Combine(Path_Kernel, "takara.bin");
        public string Path_KernelKaizou => Path.Combine(Path_Kernel, "kaizou.bin");
        public string Path_KernelSumGrow => Path.Combine(Path_Kernel, "sum_grow.bin");
        public string Path_KernelSphere => Path.Combine(Path_Kernel, "sphere.bin");
        public string Path_KernelPanel => Path.Combine(Path_Kernel, "panel.bin");
        public string Path_KernelCtbBase => Path.Combine(Path_Kernel, "ctb_base.bin");
        public string Path_KernelItemShop => Path.Combine(Path_Kernel, "item_shop.bin");
        public string Path_KernelArmsShop => Path.Combine(Path_Kernel, "arms_shop.bin");
        public string Path_KernelShopArmsCatalog => Path.Combine(Path_Kernel, "shop_arms.bin");
        public string Path_KernelBukiGet => Path.Combine(Path_Kernel, "buki_get.bin");
        public string Path_KernelPrepare => Path.Combine(Path_Kernel, "prepare.bin");
        public string Path_KernelPlySave => Path.Combine(Path_Kernel, "ply_save.bin");
        public string Path_KernelPlyRom => Path.Combine(Path_Kernel, "ply_rom.bin");
        public string Path_KernelUs => Path.Combine(ProjectPath, "new_uspc", "battle", "kernel");
        public string Path_KernelImportantUs => Path.Combine(Path_KernelUs, "important.bin");
        public string Path_KernelWeaponNamesUs => Path.Combine(Path_KernelUs, "w_name.bin");
        public string Path_KernelAAbilityUs => Path.Combine(Path_KernelUs, "a_ability.bin");
        public string Path_KernelAAbilityJp => Path.Combine(Path_Kernel, "a_ability.bin");
        public string Path_KernelPlySaveUs => Path.Combine(Path_KernelUs, "ply_save.bin");
        public string Path_KernelPlyRomUs => Path.Combine(Path_KernelUs, "ply_rom.bin");
        public string Path_KernelSphereUs => Path.Combine(Path_KernelUs, "sphere.bin");
        public string Path_KernelPanelUs => Path.Combine(Path_KernelUs, "panel.bin");
        public string Path_MenuUs => Path.Combine(ProjectPath, "new_uspc", "menu");
        public string Path_MacroDictionaryUs => Path.Combine(Path_MenuUs, "macrodic.dcp");
        public string Path_KernelArmsRate => Path.Combine(Path_Kernel, "arms_rate.bin");
        public string Path_KernelCommand => Path.Combine(Path_Kernel, "command.bin");
        public string Path_KernelCommandUs => Path.Combine(Path_KernelUs, "command.bin");
        public string Path_KernelItemUs => Path.Combine(Path_KernelUs, "item.bin");
        public string Path_KernelMonMagic1Us => Path.Combine(Path_KernelUs, "monmagic1.bin");
        public string Path_KernelMonMagic2Us => Path.Combine(Path_KernelUs, "monmagic2.bin");
        public string Path_KernelMonster1Us => Path.Combine(Path_KernelUs, "monster1.bin");
        public string Path_KernelMonster2Us => Path.Combine(Path_KernelUs, "monster2.bin");
        public string Path_KernelMonster3Us => Path.Combine(Path_KernelUs, "monster3.bin");
        public string Path_Mon => Path.Combine(ProjectPath, "jppc", "battle", "mon");

        public string GetPathMon(int monsterId)
        {
            CheckProject();

            if (monsterId < 0 || monsterId > 999)
                throw new System.Exception("[Project_Service] Invalid monster id");

            string folder = "_m" + monsterId.ToString("D3");
            string file = "m" + monsterId.ToString("D3") + ".bin";
            return Path.Combine(Path_Mon, folder, file);
        }

        public string GetPathBattle(string battleId)
        {
            CheckProject();

            if (string.IsNullOrWhiteSpace(battleId))
                throw new System.Exception("[Project_Service] Invalid battle id");

            return Path.Combine(Path_Btl, battleId, battleId + ".bin");
        }

        private void CheckProject()
        {
            if (!IsProjectLoaded)
                throw new System.Exception("[Project_Service] Project is not loaded!");
        }

        private static string NormalizePath(string projectPath)
        {
            return projectPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        // Known FFX extraction roots (full SOURCE-ASSET trees). The Steam-mod master
        // (...\data\mods\ffx_ps2\ffx\master) has NO source assets — only the modded .bin files — so the read-only
        // asset browsers (Textures TM2 / PS3 Magic HD / PS2 Models RSD / PS2 Audio / Magic Effects) must point at the
        // EXTRACTED reference, not the mod. SHARED_CONTEXT: jogo extraido de referencia = D:\FFX Extracted\FFX.
        // These are auto-detected (Directory.Exists guards each) so other machines fall back to the project-derived
        // path or the manual "Set ffx_ps2 Root..." override. The kernel WRITERS use ProjectPath (the master), NOT
        // these — so pointing them at the extracted reference is read-only-safe.
        private static readonly string[] KnownExtractionRoots =
        {
            Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @""),
            Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ModsRoot, @"FFXDataParser-master\FFX_Data_VBF"),
        };

        private static string? TryResolveFfxPs2Root(string? projectPath)
        {
            // 1) Prefer a known extraction root — it always has the full source assets, so the asset browsers work
            //    even when the loaded workspace is the asset-empty Steam mod.
            foreach (string root in KnownExtractionRoots)
            {
                string candidate = Path.Combine(root, "ffx_ps2");
                if (Directory.Exists(candidate))
                    return candidate;
            }
            // 2) Fallback: derive from the loaded project (when the workspace IS an extracted master).
            return ResolveFfxPs2FromProject(projectPath);
        }

        private static string? ResolveFfxPs2FromProject(string? projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
                return null;

            DirectoryInfo? masterDirectory = new DirectoryInfo(projectPath);
            DirectoryInfo? ffxDirectory = masterDirectory.Parent;
            DirectoryInfo? ffxPs2Directory = ffxDirectory?.Parent;

            if (ffxDirectory == null
                || ffxPs2Directory == null
                || !string.Equals(masterDirectory.Name, "master", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(ffxDirectory.Name, "ffx", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(ffxPs2Directory.Name, "ffx_ps2", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return ffxPs2Directory.FullName;
        }

        private static string? TryResolvePs3DataRoot(string? projectPath)
        {
            // 1) Prefer a known extraction root's ps3data (the mod tree has none).
            foreach (string root in KnownExtractionRoots)
            {
                string candidate = Path.Combine(root, "ffx_data", "gamedata", "ps3data");
                if (Directory.Exists(candidate))
                    return candidate;
            }
            // 2) Fallback: derive from the loaded project (extracted-master case).
            string? ffxPs2Root = ResolveFfxPs2FromProject(projectPath);
            if (ffxPs2Root == null)
                return null;

            DirectoryInfo? extractionRoot = Directory.GetParent(ffxPs2Root);
            if (extractionRoot == null)
                return null;

            string candidate2 = Path.Combine(extractionRoot.FullName, "ffx_data", "gamedata", "ps3data");
            return Directory.Exists(candidate2)
                ? candidate2
                : null;
        }

        /// <summary>
        /// <c>...\data\mods\ffx_ps2\ffx\master</c> → game install root (parent of <c>data</c>).
        /// </summary>
        public static string? TryResolveGameInstallRoot(string? projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
                return null;

            DirectoryInfo? master = new DirectoryInfo(projectPath);
            DirectoryInfo? ffx = master.Parent;
            DirectoryInfo? ffxPs2 = ffx?.Parent;
            DirectoryInfo? mods = ffxPs2?.Parent;
            DirectoryInfo? data = mods?.Parent;

            if (!string.Equals(master.Name, "master", StringComparison.OrdinalIgnoreCase)
                || ffx == null || !string.Equals(ffx.Name, "ffx", StringComparison.OrdinalIgnoreCase)
                || ffxPs2 == null || !string.Equals(ffxPs2.Name, "ffx_ps2", StringComparison.OrdinalIgnoreCase)
                || mods == null || !string.Equals(mods.Name, "mods", StringComparison.OrdinalIgnoreCase)
                || data == null || !string.Equals(data.Name, "data", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return data.Parent?.FullName;
        }
    }
}

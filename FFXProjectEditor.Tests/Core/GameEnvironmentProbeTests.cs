using System;
using FFXProjectEditor.Tests.Infrastructure;
using System.IO;
using System.Linq;
using FFXProjectEditor.Core;
using Xunit;

namespace FFXProjectEditor.Tests.Core
{
    /// <summary>
    /// P3-L2: GameEnvironmentProbe contract. Tests use temp synthetic directory trees;
    /// no real FFX installation is required. The Steam discovery (registry + libraryfolders.vdf)
    /// is tested with a synthetic vdf file.
    /// </summary>
    public class GameEnvironmentProbeTests : IDisposable
    {
        private readonly string _temp;
        private readonly GameEnvironmentProbe _probe;

        public GameEnvironmentProbeTests()
        {
            _temp = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "GameEnvironmentProbeTests_" + Guid.NewGuid().ToString("N"));
            _probe = new GameEnvironmentProbe();
        }

        public void Dispose()
        {
            if (Directory.Exists(_temp))
                Directory.Delete(_temp, recursive: true);
        }

        string Root(params string[] parts) => Path.Combine(new[] { _temp }.Concat(parts).ToArray());

        void Touch(string path)
        {
            string dir = Path.GetDirectoryName(path)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, Array.Empty<byte>());
        }

        // --- (a) Detect with explicit complete root → modules + flags detected ---------

        [Fact]
        public void Detect_CompleteRoot_DetectsModulesAndFlags()
        {
            Touch(Root("FFX.exe"));
            Touch(Root("modules", "ffx-probe.dll"));
            Touch(Root("modules", "FfxHooksDll.dll"));
            Touch(Root("config", "music.flag"));

            GameEnvironmentReport r = _probe.Detect(_temp);

            Assert.Equal(Path.GetFullPath(_temp), r.GameRoot, ignoreCase: true);
            Assert.True(r.GameFound);
            Assert.True(r.ProbeModulePresent);
            Assert.True(r.HooksModulePresent);
            Assert.Contains("music.flag", r.ActiveHookFlags);
            // MMF não existe em teste → probe não attachado → Warning honesto com issue acionável
            Assert.False(r.ProbeAttached);
            Assert.Equal(EnvironmentCheckState.Warning, r.State);
            Assert.Contains(r.Issues, i => i.Code == "PROBE_NOT_ATTACHED");
            Assert.NotEmpty(r.Issues[0].ActionableMessage);
        }

        // Regression (2026-09-14): the shipped module DLL is lowercase ffx-hooks.dll.
        // File.Exists with the legacy "FfxHooksDll.dll" spelling silently failed on
        // case-sensitive filesystems (Linux ext4) while NTFS folded it — the dashboard
        // reported "Missing in modules\" with the DLL right there.
        [Fact]
        public void Detect_CanonicalLowercaseHooksDll_StillDetected()
        {
            Touch(Root("FFX.exe"));
            Touch(Root("modules", "ffx-probe.dll"));
            Touch(Root("modules", "ffx-hooks.dll")); // shipped spelling, not FfxHooksDll.dll

            GameEnvironmentReport r = _probe.Detect(_temp);

            Assert.True(r.HooksModulePresent);
        }

        // --- (b) Root without FFX.exe → Error with actionable message ----------------

        [Fact]
        public void Detect_RootWithoutExe_ReturnsError()
        {
            Directory.CreateDirectory(_temp); // empty dir, no FFX.exe

            GameEnvironmentReport r = _probe.Detect(_temp);

            Assert.Equal(EnvironmentCheckState.Error, r.State);
            Assert.False(r.GameFound);
            Assert.Contains(r.Issues, i => i.Code == "FFX_EXE_MISSING");
            Assert.NotEmpty(r.Issues[0].ActionableMessage);
        }

        // --- (e) DiscoverGameRoot via env var → root found ---------------------------

        [Fact]
        public void DiscoverGameRoot_EnvVar_ReturnsRoot()
        {
            Touch(Root("FFX.exe"));
            Environment.SetEnvironmentVariable(GameEnvironmentProbe.GameRootEnvVar, _temp);

            try
            {
                string? root = _probe.DiscoverGameRoot();
                Assert.Equal(Path.GetFullPath(_temp), root, ignoreCase: true);
            }
            finally
            {
                Environment.SetEnvironmentVariable(GameEnvironmentProbe.GameRootEnvVar, null);
            }
        }

        // --- (f) FindInLibraryFolders with synthetic vdf → finds path ----------------

        [Fact]
        public void FindInLibraryFolders_SyntheticVdf_FindsPath()
        {
            string steamRoot = Root("steam");
            string libPath = Root("steam_lib");
            string appDir = Path.Combine(libPath, "steamapps", "common", GameEnvironmentProbe.SteamAppName);
            Directory.CreateDirectory(appDir);

            string vdfPath = Root("steam", "steamapps", "libraryfolders.vdf");
            Touch(vdfPath);
            File.WriteAllText(
                vdfPath,
                "\"libraryfolders\"\n{\n  \"0\"\n  {\n    \"path\" \"" + libPath.Replace("\\", "\\\\") + "\"\n  }\n}\n");

            string? found = GameEnvironmentProbe.FindInLibraryFolders(
                steamRoot, GameEnvironmentProbe.SteamAppName, out string? libraryFile);

            Assert.NotNull(found);
            Assert.Equal(appDir, found, ignoreCase: true);
            Assert.NotNull(libraryFile);
        }

        // --- (g) No flag files → empty list -----------------------------------------

        [Fact]
        public void Detect_NoFlags_EmptyList()
        {
            Touch(Root("FFX.exe"));
            Touch(Root("modules", "ffx-probe.dll"));
            Touch(Root("modules", "FfxHooksDll.dll"));

            GameEnvironmentReport r = _probe.Detect(_temp);

            Assert.Empty(r.ActiveHookFlags);
        }

        // --- (h) Version and region are unknown when absent -------------------------

        [Fact]
        public void Detect_VersionAndRegion_UnknownOnFakeExe()
        {
            Touch(Root("FFX.exe")); // no real PE → FileVersion stays null
            GameEnvironmentReport r = _probe.Detect(_temp);

            Assert.Null(r.GameVersion);
            Assert.Equal(GameRegion.Unknown, r.Region);
        }

        // --- (i) DetectRegion heuristic with data/jppc → JP --------------------------

        [Fact]
        public void Detect_DataJppc_RegionJP()
        {
            Touch(Root("FFX.exe"));
            Touch(Root("data", "jppc", ".keep"));

            GameEnvironmentReport r = _probe.Detect(_temp);

            Assert.Equal(GameRegion.JP, r.Region);
        }

        // --- (j) No crash when steam discovery finds nothing -------------------------

        [Fact]
        public void FindInLibraryFolders_MissingVdf_ReturnsNull()
        {
            string steamRoot = Root("steam_no_vdf");
            Directory.CreateDirectory(steamRoot);

            string? found = GameEnvironmentProbe.FindInLibraryFolders(
                steamRoot, GameEnvironmentProbe.SteamAppName, out string? libraryFile);

            Assert.Null(found);
            Assert.Null(libraryFile);
        }

        // --- (c) Nonexistent root → Error with GAME_NOT_FOUND -------------------------

        [Fact]
        public void Detect_NonexistentRoot_ReturnsError()
        {
            string bad = Path.Combine(_temp, "does_not_exist");
            GameEnvironmentReport r = _probe.Detect(bad);

            Assert.Equal(EnvironmentCheckState.Error, r.State);
            Assert.False(r.GameFound);
            Assert.Contains(r.Issues, i => i.Code == "GAME_NOT_FOUND");
            Assert.NotEmpty(r.Issues[0].ActionableMessage);
        }

        // --- (d) FFX.exe present but no modules → Warning -----------------------------

        [Fact]
        public void Detect_ExeOnly_ReturnsWarning()
        {
            Touch(Root("FFX.exe"));

            GameEnvironmentReport r = _probe.Detect(_temp);

            Assert.Equal(EnvironmentCheckState.Warning, r.State);
            Assert.True(r.GameFound);
            Assert.False(r.ProbeModulePresent);
            Assert.False(r.HooksModulePresent);
            Assert.Contains(r.Issues, i => i.Code == "RUNTIME_MODULES_MISSING");
        }
    }
}

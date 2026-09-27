using System;
using System.IO;
using FFXProjectEditor.Core;
using FFXProjectEditor.Modules.Main;
using FFXProjectEditor.Services;
using Xunit;

namespace FFXProjectEditor.Tests.Services;

// ── Separate game-folder selection (feature 2.241.0.0) ──
// The game install root (folder with FFX.exe/modules\) and the master workspace are
// independent picks: the workspace may live outside the install. The explicit pick is
// persisted (game-root.txt, same "load once, remembered forever" pattern as
// last-project.txt), wins over the workspace-derived root, and feeds
// GameEnvironmentProbe.DiscoverGameRoot — which previously ignored the project-derived
// root entirely, so on Linux (no Steam registry) a workspace nested inside the install
// still reported "Game Not Found" (dashboard regression 2026-09-14).
[Collection(ProjectServiceCollection.Name)]
public sealed class ProjectServiceGameRootTests
{
    // SetGameRoot persists to %LOCALAPPDATA%/FFXProjectEditor/game-root.txt; snapshot/restore
    // so tests never clobber the user's real setting (same pattern as last-project.txt tests).
    private static string GameRootFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FFXProjectEditor", "game-root.txt");

    private static string OutputRootFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FFXProjectEditor", "output-root.txt");

    private sealed class GameRootState : IDisposable
    {
        private readonly string? _override;
        private readonly string? _env;
        private readonly string? _projectPath;
        private readonly string? _fileContent;
        private readonly bool _hadFile;
        private readonly string? _outputOverride;
        private readonly string? _outputFileContent;
        private readonly bool _hadOutputFile;

        public GameRootState()
        {
            _override = Project_Service.GameRootOverride;
            _env = Environment.GetEnvironmentVariable(GameEnvironmentProbe.GameRootEnvVar);
            _projectPath = Project_Service.Instance.ProjectPath;
            _hadFile = File.Exists(GameRootFile);
            _fileContent = _hadFile ? File.ReadAllText(GameRootFile) : null;
            _outputOverride = Project_Service.OutputRootOverride;
            _hadOutputFile = File.Exists(OutputRootFile);
            _outputFileContent = _hadOutputFile ? File.ReadAllText(OutputRootFile) : null;
        }

        public void Dispose()
        {
            Project_Service.GameRootOverride = _override;
            Project_Service.Instance.ProjectPath = _projectPath;
            Environment.SetEnvironmentVariable(GameEnvironmentProbe.GameRootEnvVar, _env);
            if (_hadFile)
                File.WriteAllText(GameRootFile, _fileContent);
            else if (File.Exists(GameRootFile))
                File.Delete(GameRootFile);
            Project_Service.OutputRootOverride = _outputOverride;
            if (_hadOutputFile)
                File.WriteAllText(OutputRootFile, _outputFileContent);
            else if (File.Exists(OutputRootFile))
                File.Delete(OutputRootFile);
        }
    }

    [Fact]
    public void IsGameRootPath_False_WhenNoExeOrModules()
    {
        string dir = NewTempDir("plain");
        try
        {
            Assert.False(Project_Service.IsGameRootPath(dir));
            Assert.False(Project_Service.IsGameRootPath(null));
            Assert.False(Project_Service.IsGameRootPath(Path.Combine(dir, "missing")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void IsGameRootPath_True_WithFfxExe_OrModules()
    {
        string dir = NewTempDir("game");
        try
        {
            File.WriteAllText(Path.Combine(dir, "FFX.exe"), "stub");
            Assert.True(Project_Service.IsGameRootPath(dir));

            string dir2 = NewTempDir("game-modules");
            Directory.CreateDirectory(Path.Combine(dir2, "modules"));
            Assert.True(Project_Service.IsGameRootPath(dir2));
            Directory.Delete(dir2, recursive: true);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ResolveGameRootCandidate_ClimbsFromNestedWorkspace()
    {
        // Picking the master workspace (or any subdir of the install) in the game picker
        // must resolve to the enclosing install root — the master under data\mods IS
        // inside the game folder.
        string root = NewTempDir("game");
        string master = Path.Combine(root, "data", "mods", "ffx_ps2", "ffx", "master");
        Directory.CreateDirectory(master);
        try
        {
            File.WriteAllText(Path.Combine(root, "FFX.exe"), "stub");
            Assert.Equal(root, Project_Service.ResolveGameRootCandidate(master));
            Assert.Equal(root, Project_Service.ResolveGameRootCandidate(Path.Combine(root, "data")));
            Assert.Null(Project_Service.ResolveGameRootCandidate(Path.GetTempPath().TrimEnd('/')));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void SetGameRoot_InvalidFolder_ReturnsFalseAndKeepsOverrideUnset()
    {
        using var _ = new GameRootState();
        Project_Service.GameRootOverride = null;
        string dir = NewTempDir("not-a-game");
        try
        {
            Assert.False(Project_Service.Instance.SetGameRoot(dir));
            Assert.Null(Project_Service.GameRootOverride);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void SetGameRoot_ValidDir_SetsOverrideAndPersists()
    {
        using var _ = new GameRootState();
        Project_Service.GameRootOverride = null;
        string dir = NewTempDir("game");
        try
        {
            File.WriteAllText(Path.Combine(dir, "FFX.exe"), "stub");

            Assert.True(Project_Service.Instance.SetGameRoot(dir));
            Assert.Equal(dir, Project_Service.GameRootOverride);
            Assert.Equal(dir, Project_Service.Instance.Path_GameInstallRoot);
            Assert.True(File.Exists(GameRootFile));
            Assert.Equal(dir, File.ReadAllText(GameRootFile).Trim());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Path_GameInstallRoot_ExplicitOverrideBeatsWorkspaceDerived()
    {
        using var _ = new GameRootState();
        string gameA = NewTempDir("game-a");       // workspace nested inside this install
        string gameB = NewTempDir("game-b");       // explicitly picked elsewhere
        string master = Path.Combine(gameA, "data", "mods", "ffx_ps2", "ffx", "master");
        Directory.CreateDirectory(master);
        try
        {
            File.WriteAllText(Path.Combine(gameA, "FFX.exe"), "stub");
            File.WriteAllText(Path.Combine(gameB, "FFX.exe"), "stub");
            Project_Service.Instance.ProjectPath = master;
            Project_Service.GameRootOverride = gameB;

            Assert.Equal(gameB, Project_Service.Instance.Path_GameInstallRoot);
        }
        finally
        {
            Directory.Delete(gameA, recursive: true);
            Directory.Delete(gameB, recursive: true);
        }
    }

    [Fact]
    public void Path_GameInstallRoot_DerivesFromWorkspace_WhenNoOverride()
    {
        using var _ = new GameRootState();
        Project_Service.GameRootOverride = null;
        string game = NewTempDir("game");
        string master = Path.Combine(game, "data", "mods", "ffx_ps2", "ffx", "master");
        Directory.CreateDirectory(master);
        try
        {
            File.WriteAllText(Path.Combine(game, "FFX.exe"), "stub");
            Project_Service.Instance.ProjectPath = master;

            Assert.Equal(game, Project_Service.Instance.Path_GameInstallRoot);
        }
        finally { Directory.Delete(game, recursive: true); }
    }

    [Fact]
    public void LoadLastGameRoot_RoundTrip_AndStalePathIgnored()
    {
        using var _ = new GameRootState();
        Project_Service.GameRootOverride = null;
        string dir = NewTempDir("game");
        try
        {
            File.WriteAllText(Path.Combine(dir, "FFX.exe"), "stub");
            Assert.True(Project_Service.Instance.SetGameRoot(dir));
            Assert.Equal(dir, Project_Service.LoadLastGameRoot());

            // A persisted path that no longer exists must be ignored, not resurrected.
            File.WriteAllText(GameRootFile, Path.Combine(dir, "gone"));
            Assert.Null(Project_Service.LoadLastGameRoot());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ClearGameRoot_RemovesOverrideAndFile()
    {
        using var _ = new GameRootState();
        string dir = NewTempDir("game");
        try
        {
            File.WriteAllText(Path.Combine(dir, "FFX.exe"), "stub");
            Assert.True(Project_Service.Instance.SetGameRoot(dir));
            Project_Service.Instance.ClearGameRoot();
            Assert.Null(Project_Service.GameRootOverride);
            Assert.False(File.Exists(GameRootFile));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void DiscoverGameRoot_UsesProjectDerivedRoot_OnNonWindows()
    {
        // Regression: DiscoverGameRoot used to rely solely on env + Windows Steam registry,
        // so on Linux a workspace nested inside the install still produced
        // "Game Not Found" even with FFX.exe right there.
        using var _ = new GameRootState();
        Environment.SetEnvironmentVariable(GameEnvironmentProbe.GameRootEnvVar, null);
        Project_Service.GameRootOverride = null;
        string game = NewTempDir("game");
        string master = Path.Combine(game, "data", "mods", "ffx_ps2", "ffx", "master");
        Directory.CreateDirectory(master);
        try
        {
            File.WriteAllText(Path.Combine(game, "FFX.exe"), "stub");
            Project_Service.Instance.ProjectPath = master;

            Assert.Equal(game, new GameEnvironmentProbe().DiscoverGameRoot());
        }
        finally { Directory.Delete(game, recursive: true); }
    }

    [Fact]
    public void DiscoverGameRoot_EnvVarStillWins_OverProjectRoot()
    {
        // FFX_GAME_ROOT is the documented test/CI override and keeps top precedence.
        using var _ = new GameRootState();
        string envGame = NewTempDir("env-game");
        string projGame = NewTempDir("proj-game");
        string master = Path.Combine(projGame, "data", "mods", "ffx_ps2", "ffx", "master");
        Directory.CreateDirectory(master);
        try
        {
            File.WriteAllText(Path.Combine(envGame, "FFX.exe"), "stub");
            File.WriteAllText(Path.Combine(projGame, "FFX.exe"), "stub");
            Environment.SetEnvironmentVariable(GameEnvironmentProbe.GameRootEnvVar, envGame);
            Project_Service.Instance.ProjectPath = master;

            Assert.Equal(envGame, new GameEnvironmentProbe().DiscoverGameRoot());
        }
        finally
        {
            Directory.Delete(envGame, recursive: true);
            Directory.Delete(projGame, recursive: true);
        }
    }

    [Fact]
    public void SelectGameFolder_Invalid_ReturnsFalseWithError()
    {
        using var _ = new GameRootState();
        Project_Service.GameRootOverride = null;
        string dir = NewTempDir("not-a-game");
        try
        {
            var dm = new Main_DataModel();
            Assert.False(dm.SelectGameFolder(dir));
            Assert.Equal(dir, dm.LastGameRootError);
            Assert.Null(Project_Service.GameRootOverride);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void SelectGameFolder_Valid_SetsRootAndUpdatesBadge()
    {
        using var _ = new GameRootState();
        Project_Service.GameRootOverride = null;
        string dir = NewTempDir("game");
        try
        {
            File.WriteAllText(Path.Combine(dir, "FFX.exe"), "stub");
            var dm = new Main_DataModel();

            Assert.True(dm.SelectGameFolder(dir));
            Assert.Null(dm.LastGameRootError);
            Assert.Equal(dir, Project_Service.GameRootOverride);
            Assert.Equal("game", dm.GameRootBadgeLabel);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ── Path_OutputRoot: staging by default, data\mods under the external loader ──
    // "Output" means where generated/deployed content lands. ff10-file-loader.dll reads
    // loose files from data\mods under the install — so with the loader enabled (or a
    // workspace that already lives inside that tree) the deploy root is data\mods;
    // otherwise the safe output_staging beside the game.

    [Fact]
    public void Path_OutputRoot_Null_WhenNoGameRoot()
    {
        using var _ = new GameRootState();
        Project_Service.GameRootOverride = null;
        Project_Service.Instance.ProjectPath = null;
        Assert.Null(Project_Service.Instance.Path_OutputRoot);
    }

    [Fact]
    public void Path_OutputRoot_OutputStaging_WhenLoaderAbsent()
    {
        using var _ = new GameRootState();
        Project_Service.GameRootOverride = null;
        Project_Service.Instance.ProjectPath = null;
        string game = NewTempDir("game");
        try
        {
            File.WriteAllText(Path.Combine(game, "FFX.exe"), "stub");
            Assert.True(Project_Service.Instance.SetGameRoot(game));

            Assert.False(Project_Service.Instance.IsExternalFileLoaderEnabled);
            Assert.Equal(Path.Combine(game, "output_staging"), Project_Service.Instance.Path_OutputRoot);
        }
        finally { Directory.Delete(game, recursive: true); }
    }

    [Fact]
    public void Path_OutputRoot_DataMods_WhenExternalLoaderEnabled()
    {
        using var _ = new GameRootState();
        Project_Service.GameRootOverride = null;
        Project_Service.Instance.ProjectPath = null;
        string game = NewTempDir("game");
        try
        {
            File.WriteAllText(Path.Combine(game, "FFX.exe"), "stub");
            string modules = Path.Combine(game, "modules");
            Directory.CreateDirectory(modules);
            File.WriteAllText(Path.Combine(modules, Project_Service.ExternalFileLoaderDllName), "stub");
            Assert.True(Project_Service.Instance.SetGameRoot(game));

            Assert.True(Project_Service.Instance.IsExternalFileLoaderEnabled);
            Assert.Equal(Path.Combine(game, "data", "mods"), Project_Service.Instance.Path_OutputRoot);
        }
        finally { Directory.Delete(game, recursive: true); }
    }

    [Fact]
    public void Path_OutputRoot_OutputStaging_WhenLoaderDisabled()
    {
        // A .disabled marker means the loader is parked, not active — staging must win.
        using var _ = new GameRootState();
        Project_Service.GameRootOverride = null;
        Project_Service.Instance.ProjectPath = null;
        string game = NewTempDir("game");
        try
        {
            File.WriteAllText(Path.Combine(game, "FFX.exe"), "stub");
            string modules = Path.Combine(game, "modules");
            Directory.CreateDirectory(modules);
            File.WriteAllText(Path.Combine(modules, Project_Service.ExternalFileLoaderDllName + ".disabled"), "stub");
            Assert.True(Project_Service.Instance.SetGameRoot(game));

            Assert.False(Project_Service.Instance.IsExternalFileLoaderEnabled);
            Assert.Equal(Path.Combine(game, "output_staging"), Project_Service.Instance.Path_OutputRoot);
        }
        finally { Directory.Delete(game, recursive: true); }
    }

    [Fact]
    public void Path_OutputRoot_DataMods_WhenWorkspaceInsideLoaderTree()
    {
        // The extracted master under data\mods\ffx_ps2\ffx\master IS the loader's scan
        // tree — even without the DLL present yet, that is where deploy output belongs.
        using var _ = new GameRootState();
        Project_Service.GameRootOverride = null;
        string game = NewTempDir("game");
        string master = Path.Combine(game, "data", "mods", "ffx_ps2", "ffx", "master");
        Directory.CreateDirectory(master);
        try
        {
            File.WriteAllText(Path.Combine(game, "FFX.exe"), "stub");
            Project_Service.Instance.ProjectPath = master;

            Assert.Equal(game, Project_Service.Instance.Path_GameInstallRoot);
            Assert.Equal(Path.Combine(game, "data", "mods"), Project_Service.Instance.Path_OutputRoot);
        }
        finally { Directory.Delete(game, recursive: true); }
    }

    // ── Output root override: the user pick pins the deploy folder ──

    [Fact]
    public void SetOutputRoot_PersistsAndBeatsAutoResolution()
    {
        using var _ = new GameRootState();
        Project_Service.GameRootOverride = null;
        Project_Service.OutputRootOverride = null;
        Project_Service.Instance.ProjectPath = null;
        string game = NewTempDir("game");
        string custom = NewTempDir("my-output");
        try
        {
            File.WriteAllText(Path.Combine(game, "FFX.exe"), "stub");
            string modules = Path.Combine(game, "modules");
            Directory.CreateDirectory(modules);
            File.WriteAllText(Path.Combine(modules, Project_Service.ExternalFileLoaderDllName), "stub");
            Assert.True(Project_Service.Instance.SetGameRoot(game));

            // Auto would be data\mods (loader enabled); the explicit pick must win.
            Assert.True(Project_Service.Instance.SetOutputRoot(custom));
            Assert.Equal(custom, Project_Service.Instance.Path_OutputRoot);
            Assert.Equal(custom, File.ReadAllText(OutputRootFile).Trim());
            Assert.Equal(custom, Project_Service.LoadLastOutputRoot());
        }
        finally
        {
            Directory.Delete(game, recursive: true);
            Directory.Delete(custom, recursive: true);
        }
    }

    [Fact]
    public void ClearOutputRoot_RestoresAutoAndDeletesFile()
    {
        using var _ = new GameRootState();
        Project_Service.GameRootOverride = null;
        Project_Service.OutputRootOverride = null;
        Project_Service.Instance.ProjectPath = null;
        string game = NewTempDir("game");
        string custom = NewTempDir("my-output");
        try
        {
            File.WriteAllText(Path.Combine(game, "FFX.exe"), "stub");
            Assert.True(Project_Service.Instance.SetGameRoot(game));
            Assert.True(Project_Service.Instance.SetOutputRoot(custom));

            Project_Service.Instance.ClearOutputRoot();
            Assert.Null(Project_Service.OutputRootOverride);
            Assert.False(File.Exists(OutputRootFile));
            Assert.Equal(Path.Combine(game, "output_staging"), Project_Service.Instance.Path_OutputRoot);
        }
        finally
        {
            Directory.Delete(game, recursive: true);
            Directory.Delete(custom, recursive: true);
        }
    }

    [Fact]
    public void SetOutputRoot_Nonexistent_Rejected()
    {
        using var _ = new GameRootState();
        Project_Service.OutputRootOverride = null;
        string dir = NewTempDir("exists");
        try
        {
            Assert.False(Project_Service.Instance.SetOutputRoot(Path.Combine(dir, "gone")));
            Assert.Null(Project_Service.OutputRootOverride);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static string NewTempDir(string leaf)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"ffxed-test-{Guid.NewGuid():N}", leaf);
        Directory.CreateDirectory(dir);
        return dir;
    }
}

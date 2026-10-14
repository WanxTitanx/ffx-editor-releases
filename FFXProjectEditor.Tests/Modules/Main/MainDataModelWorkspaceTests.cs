using System;
using System.IO;
using FFXProjectEditor.Modules.Main;
using FFXProjectEditor.Services;
using Xunit;

namespace FFXProjectEditor.Tests.Modules.Main;

// ── Workspace folder selection regression (crash.log 2026-09-14 20:21) ──
// Project_Service.LoadProject throws on a folder that is not named "master"; that exception
// used to escape Main_DataModel.LoadProjectFolder into the async-void picker handler and kill
// the process via AppDomain.UnhandledException. The DataModel boundary must now convert any
// load failure into a result (bool + LastWorkspaceError) so the UI can show an actionable
// dialog instead of crashing. The service keeps throwing — that contract is pinned here too.
[Collection(Services.ProjectServiceCollection.Name)]
public sealed class MainDataModelWorkspaceTests
{
    // SaveLastProject persists to %LOCALAPPDATA%/FFXProjectEditor/last-project.txt on every
    // successful load; snapshot/restore so the happy-path test never clobbers the user's file.
    private static string LastProjectFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FFXProjectEditor", "last-project.txt");

    [Fact]
    public void LoadProject_NonMasterFolder_Throws()
    {
        // Service-layer invariant: invalid folders are rejected loudly. The crash fix lives in
        // the DataModel boundary, not by weakening this validation.
        string dir = NewTempDir("not-a-master");
        try
        {
            Assert.Throws<Exception>(() => Project_Service.Instance.LoadProject(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadProjectFolder_NonMasterFolder_ReturnsFalseWithoutThrowing()
    {
        string dir = NewTempDir("not-a-master");
        try
        {
            var dm = new Main_DataModel();

            bool ok = dm.LoadProjectFolder(dir);

            Assert.False(ok);
            Assert.NotNull(dm.LastWorkspaceError);
            Assert.False(Project_Service.Instance.IsProjectLoaded);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadProjectFolder_MissingFolder_ReturnsFalseWithoutThrowing()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"ffxed-test-{Guid.NewGuid():N}", "master");
        var dm = new Main_DataModel();

        bool ok = dm.LoadProjectFolder(dir);

        Assert.False(ok);
        Assert.NotNull(dm.LastWorkspaceError);
        Assert.False(Project_Service.Instance.IsProjectLoaded);
    }

    [Fact]
    public void LoadProjectFolder_MasterFolder_LoadsAndClearsError()
    {
        string root = Path.Combine(Path.GetTempPath(), $"ffxed-test-{Guid.NewGuid():N}");
        string master = Path.Combine(root, "ffx", "master");
        Directory.CreateDirectory(master);
        string? savedLastProject = File.Exists(LastProjectFile) ? File.ReadAllText(LastProjectFile) : null;
        bool hadFile = File.Exists(LastProjectFile);
        try
        {
            var dm = new Main_DataModel();

            bool ok = dm.LoadProjectFolder(master);

            Assert.True(ok);
            Assert.Null(dm.LastWorkspaceError);
            Assert.Equal(master, Project_Service.Instance.ProjectPath);
            Assert.True(Project_Service.Instance.IsProjectLoaded);
        }
        finally
        {
            Project_Service.Instance.ProjectPath = null; // singleton is shared across the suite
            if (hadFile)
                File.WriteAllText(LastProjectFile, savedLastProject);
            else if (File.Exists(LastProjectFile))
                File.Delete(LastProjectFile);
            Directory.Delete(root, recursive: true);
        }
    }

    private static string NewTempDir(string leaf)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"ffxed-test-{Guid.NewGuid():N}", leaf);
        Directory.CreateDirectory(dir);
        return dir;
    }
}

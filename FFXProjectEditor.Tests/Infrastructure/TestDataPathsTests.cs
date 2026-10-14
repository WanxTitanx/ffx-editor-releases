using System;
using System.IO;
using Xunit;

namespace FFXProjectEditor.Tests.Infrastructure;

// ── TestDataPaths contract ──
// These tests use only owned scratch inputs so resolver behavior is independent from
// process-wide environment variables and from the private FFX corpus.
public sealed class TestDataPathsTests : IDisposable
{
    private readonly string _scratch = Directory.CreateTempSubdirectory("ffx-test-paths-").FullName;

    public void Dispose()
    {
        if (Directory.Exists(_scratch))
            Directory.Delete(_scratch, recursive: true);
    }

    [Fact]
    public void RequiredDirectory_MissingValueNamesConfigurationVariable()
    {
        DirectoryNotFoundException error = Assert.Throws<DirectoryNotFoundException>(() =>
            TestDataPaths.RequiredDirectory("FFX_TEST_MAGIC_CORPUS", null));

        Assert.Contains("FFX_TEST_MAGIC_CORPUS", error.Message);
    }

    [Fact]
    public void RequiredDirectory_RejectsRelativePath()
    {
        Assert.Throws<DirectoryNotFoundException>(() =>
            TestDataPaths.RequiredDirectory("input", "relative"));
    }

    [Fact]
    public void RequiredDirectory_NonexistentPathNamesConfigurationVariable()
    {
        string missing = Path.Combine(_scratch, "missing-directory");

        DirectoryNotFoundException error = Assert.Throws<DirectoryNotFoundException>(() =>
            TestDataPaths.RequiredDirectory("FFX_TEST_MASTER_ROOT", missing));

        Assert.Contains("FFX_TEST_MASTER_ROOT", error.Message);
    }

    [Fact]
    public void RequiredDirectory_ReturnsNormalizedExistingPath()
    {
        string existingDirectory = Directory.CreateDirectory(Path.Combine(_scratch, "input")).FullName;

        Assert.Equal(
            Path.GetFullPath(existingDirectory),
            TestDataPaths.RequiredDirectory("input", existingDirectory));
    }

    [Fact]
    public void RequiredFile_MissingValueNamesConfigurationVariable()
    {
        FileNotFoundException error = Assert.Throws<FileNotFoundException>(() =>
            TestDataPaths.RequiredFile("audit", null));

        Assert.Contains("audit", error.Message);
    }

    [Fact]
    public void RequiredFile_RejectsRelativePath()
    {
        Assert.Throws<FileNotFoundException>(() =>
            TestDataPaths.RequiredFile("audit", "relative.json"));
    }

    [Fact]
    public void RequiredFile_NonexistentPathNamesConfigurationVariable()
    {
        string missing = Path.Combine(_scratch, "missing-audit.json");

        FileNotFoundException error = Assert.Throws<FileNotFoundException>(() =>
            TestDataPaths.RequiredFile("FFX_TEST_MAGIC_AUDIT", missing));

        Assert.Contains("FFX_TEST_MAGIC_AUDIT", error.Message);
    }

    [Fact]
    public void RequiredFile_ReturnsNormalizedExistingPath()
    {
        string existingFile = Path.Combine(_scratch, "audit.json");
        File.WriteAllText(existingFile, "{}");

        Assert.Equal(
            Path.GetFullPath(existingFile),
            TestDataPaths.RequiredFile("audit", existingFile));
    }

    [Fact]
    public void FindRepoRoot_FindsAncestorContainingBothProjects()
    {
        string fakeRepo = Directory.CreateDirectory(Path.Combine(_scratch, "repo")).FullName;
        CreateProjectFile(fakeRepo, "FFXProjectEditor", "FFXProjectEditor.csproj");
        CreateProjectFile(fakeRepo, "FFXProjectEditor.Tests", "FFXProjectEditor.Tests.csproj");
        string deepChild = Directory.CreateDirectory(Path.Combine(fakeRepo, "a", "b", "c")).FullName;

        Assert.Equal(fakeRepo, TestDataPaths.FindRepoRoot(deepChild));
    }

    [Fact]
    public void FindRepoRoot_RejectsAncestorMissingEitherProject()
    {
        string incompleteRepo = Directory.CreateDirectory(Path.Combine(_scratch, "incomplete-repo")).FullName;
        CreateProjectFile(incompleteRepo, "FFXProjectEditor", "FFXProjectEditor.csproj");
        string noRepoChild = Directory.CreateDirectory(Path.Combine(incompleteRepo, "a", "b")).FullName;

        Assert.Throws<DirectoryNotFoundException>(() => TestDataPaths.FindRepoRoot(noRepoChild));
    }

    [Theory]
    [InlineData("../CORPUS_AUDIT.json")]
    [InlineData(@"..\CORPUS_AUDIT.json")]
    [InlineData("nested/result.json")]
    [InlineData(@"nested\result.json")]
    [InlineData("drive:result.json")]
    [InlineData(".")]
    [InlineData("..")]
    public void ReportPath_RejectsAnythingExceptOneFilenameComponent(string leaf)
    {
        Assert.Throws<ArgumentException>(() => TestDataPaths.ReportPath(leaf));
    }

    [Fact]
    public void ReportPath_ReturnsLeafInsideUniqueTemporaryReportDirectory()
    {
        string reportPath = TestDataPaths.ReportPath("result.json");

        Assert.Equal("result.json", Path.GetFileName(reportPath));
        Assert.StartsWith(Path.GetTempPath(), reportPath, StringComparison.Ordinal);
        Assert.Contains("ffx-test-reports-", Path.GetDirectoryName(reportPath));
    }

    private static void CreateProjectFile(string root, string directory, string fileName)
    {
        string projectDirectory = Directory.CreateDirectory(Path.Combine(root, directory)).FullName;
        File.WriteAllText(Path.Combine(projectDirectory, fileName), "<Project />");
    }
}

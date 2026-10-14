using System;
using System.IO;

namespace FFXProjectEditor.Tests.Infrastructure;

// ── Private test inputs and per-run reports ──
// Corpus files are not shipped or rewritten. Configuration is explicit after workstation migration.
// MAINT: keep output independent from corpus/audit paths; missing inputs are failures, not skips.
internal static class TestDataPaths
{
    private static readonly Lazy<string> Reports = new(() =>
        Directory.CreateTempSubdirectory("ffx-test-reports-").FullName);

    internal static string MagicCorpus =>
        RequiredDirectory("FFX_TEST_MAGIC_CORPUS", Get("FFX_TEST_MAGIC_CORPUS"));

    internal static string MasterRoot =>
        RequiredDirectory("FFX_TEST_MASTER_ROOT", Get("FFX_TEST_MASTER_ROOT"));

    internal static string MagicAudit =>
        RequiredFile("FFX_TEST_MAGIC_AUDIT", Get("FFX_TEST_MAGIC_AUDIT"));

    internal static string? DatOvRoot => OptionalDirectory("FFX_TEST_DAT_OV_ROOT");

    internal static string? PreparedMagicCorpus => OptionalDirectory("FFX_TEST_PREPARED_MAGIC_CORPUS");

    internal static string RepoRoot => Get("FFX_TEST_REPO_ROOT") is string root
        ? ValidateRepo(root)
        : FindRepoRoot(AppContext.BaseDirectory);

    private static string? Get(string name) => Environment.GetEnvironmentVariable(name);

    private static string? OptionalDirectory(string name) => Get(name) is string value
        ? RequiredDirectory(name, value)
        : null;

    internal static string RequiredDirectory(string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Path.IsPathFullyQualified(value) ||
            !Directory.Exists(value))
        {
            throw new DirectoryNotFoundException(
                $"Set {name} to an existing absolute input directory.");
        }

        return Path.GetFullPath(value);
    }

    internal static string RequiredFile(string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Path.IsPathFullyQualified(value) ||
            !File.Exists(value))
        {
            throw new FileNotFoundException(
                $"Set {name} to an existing absolute input file.",
                value);
        }

        return Path.GetFullPath(value);
    }

    private static bool IsRepo(string root) =>
        File.Exists(Path.Combine(root, "FFXProjectEditor", "FFXProjectEditor.csproj")) &&
        File.Exists(Path.Combine(root, "FFXProjectEditor.Tests", "FFXProjectEditor.Tests.csproj"));

    private static string ValidateRepo(string root)
    {
        root = RequiredDirectory("FFX_TEST_REPO_ROOT", root);
        return IsRepo(root)
            ? root
            : throw new DirectoryNotFoundException(
                "FFX_TEST_REPO_ROOT must contain both editor project files.");
    }

    internal static string FindRepoRoot(string start)
    {
        for (DirectoryInfo? directory = new(start); directory != null; directory = directory.Parent)
        {
            if (IsRepo(directory.FullName))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException(
            "Repository not found; set FFX_TEST_REPO_ROOT explicitly.");
    }

    internal static string ReportPath(string leaf)
    {
        if (string.IsNullOrWhiteSpace(leaf) ||
            leaf is "." or ".." ||
            leaf.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 ||
            leaf.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException(
                "Reports require one filename component.",
                nameof(leaf));
        }

        return Path.Combine(Reports.Value, leaf);
    }
}

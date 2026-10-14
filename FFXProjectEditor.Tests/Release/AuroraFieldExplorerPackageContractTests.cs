using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using FFXProjectEditor.Modules.AuroraFieldExplorer;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.Release;

// ── Aurora Field Explorer catalog packaging contract ──
// WHY: a clean-machine v2.235.1.0 smoke exposed that the public module fell back to a
// developer-only RuntimeTools path because its tracked 299-map catalog was absent from publish.
// MAINT: keep the source path, app-relative Link and resolver destination aligned; the package
// must remain self-contained while user-owned extracted map data stays outside the application.
public sealed class AuroraFieldExplorerPackageContractTests
{
    private const string CatalogSource =
        "../RuntimeTools/FFXMapViewerWeb/prompts/2026-06-03-map-batch-web-ready/map-entities.csv";
    private const string CatalogDestination = "AuroraFieldExplorer/map-entities.csv";

    [Fact]
    public void Project_MapsTheTrackedCatalog_ToTheAppRelativeResolverPath()
    {
        var project = XDocument.Load(ProductPath("FFXProjectEditor.csproj"));
        var content = Assert.Single(project.Descendants("Content").Where(item =>
            Normalize((string?)item.Attribute("Include") ?? string.Empty) == CatalogSource));

        Assert.Null(content.Attribute("Condition"));
        Assert.Equal(CatalogDestination, Normalize(Assert.Single(content.Elements("Link")).Value));
        Assert.Equal("PreserveNewest", Assert.Single(content.Elements("CopyToOutputDirectory")).Value);
    }

    [Fact]
    public void ReleaseVerifier_RequiresTheCatalog_AndGeneratorPreservesTheGate()
    {
        using JsonDocument policy = JsonDocument.Parse(File.ReadAllText(
            RepoPath("release", "package-allowlist.json")));
        string[] requiredFiles = policy.RootElement
            .GetProperty("components")
            .GetProperty("studio-core-win-x64")
            .GetProperty("requiredFiles")
            .EnumerateArray()
            .Select(element => element.GetString() ?? string.Empty)
            .ToArray();

        Assert.Single(requiredFiles, path =>
            string.Equals(Normalize(path), CatalogDestination, StringComparison.Ordinal));

        string generator = File.ReadAllText(RepoPath(
            "scripts", "release", "generate_release_contracts.ps1"));
        Assert.Contains($"'{CatalogDestination}'", generator, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildOutput_ContainsTheCompleteTrackedCatalog()
    {
        string publishedCatalog = Path.Combine(
            AppContext.BaseDirectory,
            "AuroraFieldExplorer",
            "map-entities.csv");
        Assert.True(
            File.Exists(publishedCatalog),
            $"Required public catalog is missing from output: {publishedCatalog}");
        Assert.Equal(
            File.ReadAllBytes(RepoPath(
                "RuntimeTools", "FFXMapViewerWeb", "prompts", "2026-06-03-map-batch-web-ready", "map-entities.csv")),
            File.ReadAllBytes(publishedCatalog));

        FieldMapRow[] rows = File.ReadLines(publishedCatalog)
            .Select(FieldMapRow.TryParseCsvLine)
            .Where(row => row is not null)
            .Cast<FieldMapRow>()
            .ToArray();
        Assert.Equal(299, rows.Length);
        Assert.Equal(299, rows.Select(row => row.MapEntity).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static string ProductPath(params string[] parts) =>
        RepoPath(new[] { "FFXProjectEditor" }.Concat(parts).ToArray());

    private static string RepoPath(params string[] parts) =>
        Path.Combine(new[] { TestDataPaths.RepoRoot }.Concat(parts).ToArray());
}

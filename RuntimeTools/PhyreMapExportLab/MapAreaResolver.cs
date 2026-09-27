using System.Text;
using System.Text.RegularExpressions;
using PhyreModelExportLab;

namespace PhyreMapExportLab;

public static class MapAreaResolver
{
    private static readonly Regex QuotedPathPattern = new("\"(?<path>[^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex FileNamesCountPattern = new(@"g_fileNamesCount\s*=\s*(?<count>\d+)", RegexOptions.Compiled);
    private static readonly Regex NamedIndexPattern = new(@"const\s+Phyre::PUInt32\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<index>\d+)\s*;", RegexOptions.Compiled);

    public static MapExportManifest Resolve(string ps3Root, string area, string platform, bool include2d, bool portable)
    {
        area = Program.NormalizePath(area);
        var areaRoot = Path.Combine(ps3Root, area.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(areaRoot))
        {
            throw new DirectoryNotFoundException($"Map area root not found: {areaRoot}");
        }

        var leaf = area.Split('/').Last();
        var allFiles = Directory.EnumerateFiles(areaRoot, "*", SearchOption.AllDirectories)
            .Select(path => new AreaFile(path, Program.NormalizePath(Path.GetRelativePath(ps3Root, path)), new FileInfo(path).Length))
            .OrderBy(static file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var manifestFiles = allFiles
            .Where(static file => file.RelativePath.EndsWith(".ahwin32", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var manifests = manifestFiles
            .Select(file => ParseManifest(ps3Root, file, platform, portable))
            .OrderBy(static item => item.RelativePath.Contains("/2d/", StringComparison.OrdinalIgnoreCase))
            .ThenBy(static item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var rootManifest = manifests.FirstOrDefault(item => !item.RelativePath.Contains("/2d/", StringComparison.OrdinalIgnoreCase));
        var twoDManifest = manifests.FirstOrDefault(item => item.RelativePath.Contains("/2d/", StringComparison.OrdinalIgnoreCase));
        if (rootManifest is null && twoDManifest is null)
        {
            throw new InvalidOperationException($"Root or 2d .ahwin32 manifest not found for {area}");
        }

        var primaryManifest = rootManifest ?? twoDManifest!;
        var primaryAssetLayer = rootManifest is null ? "2d_prerendered" : "root_3d";

        var rootDae = $"{area}/mdl/d3d11/{leaf}.dae.phyre";
        var twoDDae = $"{area}/2d/mdl/d3d11/{leaf}.dae.phyre";
        var primaryDae = rootManifest is null ? twoDDae : rootDae;
        var textureAnimationAgs = $"{area}/2d/mdl/d3d11/textureanimation.ags.phyre";

        var texlists = allFiles
            .Where(static file => Path.GetFileName(file.FullPath).Equals("texlist.txt", StringComparison.OrdinalIgnoreCase))
            .Select(file => ReadTexlist(ps3Root, file, portable))
            .ToArray();

        var gates = BuildGates(area, leaf, allFiles, primaryManifest, rootManifest, twoDManifest, texlists, primaryDae, twoDDae, textureAnimationAgs, primaryAssetLayer).ToArray();

        var includedManifests = include2d
            ? manifests
            : rootManifest is null
                ? new[] { primaryManifest }
                : manifests.Where(static item => !item.RelativePath.Contains("/2d/", StringComparison.OrdinalIgnoreCase)).ToArray();
        var candidateReferences = includedManifests
            .SelectMany(static item => item.References)
            .Where(static item => item.Kind is ".dae.phyre" or ".dds.phyre" or ".fx#hash.phyre" or ".ags.phyre")
            .OrderBy(static item => item.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new MapExportManifest(
            DateTimeOffset.UtcNow,
            portable ? "<ps3data>" : ps3Root,
            area,
            platform,
            portable ? Program.ToPortable(areaRoot) : areaRoot,
            allFiles.Length,
            allFiles.Sum(static file => file.Length),
            allFiles.Select(static file => new MapAreaFile(file.RelativePath, file.Length, Classify(file.RelativePath))).ToArray(),
            manifests,
            texlists,
            primaryDae,
            twoDDae,
            textureAnimationAgs,
            primaryAssetLayer,
            candidateReferences,
            gates,
            gates.All(static gate => gate.Passed)
                ? rootManifest is null ? "map_2d_prerendered_export_manifest_ready" : "map_static_export_manifest_ready"
                : "blocked_manifest_gates_failed",
            rootManifest is null
                ? "2d-only map slice: export prerendered descriptor candidate and keep AGS/runtime composition separate"
                : include2d ? "include_2d_requested_but_ags_still_blocked" : "export_root_3d_first; 2d and AGS are cataloged but excluded from MVP");
    }

    private static MapManifest ParseManifest(string ps3Root, AreaFile manifestFile, string platform, bool portable)
    {
        var text = File.ReadAllText(manifestFile.FullPath, Encoding.UTF8);
        var platformBlock = ExtractPlatformBlock(text, platform);
        var fileNamesCount = 0;
        var countMatch = FileNamesCountPattern.Match(platformBlock);
        if (countMatch.Success)
        {
            fileNamesCount = int.Parse(countMatch.Groups["count"].Value);
        }

        var namedIndices = NamedIndexPattern.Matches(platformBlock)
            .Where(static match => !string.Equals(match.Groups["name"].Value, "g_fileNamesCount", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                static match => match.Groups["name"].Value,
                static match => int.Parse(match.Groups["index"].Value),
                StringComparer.OrdinalIgnoreCase);

        var references = QuotedPathPattern.Matches(platformBlock)
            .Select(match => NormalizeGameDataPath(match.Groups["path"].Value))
            .Where(static path => path.Length > 0)
            .Select(path =>
            {
                var full = Path.Combine(ps3Root, path.Replace('/', Path.DirectorySeparatorChar));
                var exists = File.Exists(full);
                var length = exists ? new FileInfo(full).Length : (long?)null;
                return new MapManifestReference(path, Classify(path), exists, length);
            })
            .ToArray();

        return new MapManifest(
            manifestFile.RelativePath,
            manifestFile.Length,
            platform,
            fileNamesCount,
            namedIndices,
            references,
            references.Count(static reference => reference.Exists),
            references.Count(static reference => !reference.Exists));
    }

    private static MapTexlist ReadTexlist(string ps3Root, AreaFile texlistFile, bool portable)
    {
        var parent = Path.GetDirectoryName(texlistFile.RelativePath)?.Replace('\\', '/') ?? "";
        var entries = File.ReadLines(texlistFile.FullPath)
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0 && line.EndsWith(".dds.phyre", StringComparison.OrdinalIgnoreCase))
            .Select(line =>
            {
                var relative = Program.NormalizePath(Path.Combine(parent, "d3d11", line));
                var full = Path.Combine(ps3Root, relative.Replace('/', Path.DirectorySeparatorChar));
                var exists = File.Exists(full);
                var length = exists ? new FileInfo(full).Length : (long?)null;
                return new MapTexlistEntry(relative, exists, length);
            })
            .ToArray();

        return new MapTexlist(texlistFile.RelativePath, texlistFile.Length, entries);
    }

    private static IEnumerable<MapGateCheck> BuildGates(
        string area,
        string leaf,
        AreaFile[] allFiles,
        MapManifest primaryManifest,
        MapManifest? rootManifest,
        MapManifest? twoDManifest,
        MapTexlist[] texlists,
        string primaryDae,
        string twoDDae,
        string textureAnimationAgs,
        string primaryAssetLayer)
    {
        var primaryReferences = primaryManifest.References;
        yield return Gate("primary_manifest_layer", true, "root_3d or 2d_prerendered", primaryAssetLayer);
        yield return Gate("primary_manifest_has_platform_block", primaryManifest.FileNamesCount > 0 && primaryReferences.Length > 0, "g_fileNamesCount > 0 and references > 0", $"{primaryManifest.FileNamesCount} names / {primaryReferences.Length} refs");
        yield return Gate("primary_manifest_file_names_count_matches_refs", primaryManifest.FileNamesCount == primaryReferences.Length, "g_fileNamesCount == reference count", $"{primaryManifest.FileNamesCount} names / {primaryReferences.Length} refs");
        yield return Gate("primary_manifest_all_dependencies_exist", primaryManifest.MissingReferenceCount == 0, "0 missing refs", $"{primaryManifest.MissingReferenceCount} missing / {primaryReferences.Length} refs");

        var primaryDaeFile = allFiles.FirstOrDefault(file => string.Equals(file.RelativePath, primaryDae, StringComparison.OrdinalIgnoreCase));
        yield return Gate("primary_dae_exists", primaryDaeFile is not null && primaryDaeFile.Length > 0, $"{primaryDae} exists", primaryDaeFile is null ? "missing" : $"{primaryDaeFile.Length} bytes");
        yield return Gate("primary_manifest_references_primary_dae", primaryReferences.Any(reference => string.Equals(reference.Path, primaryDae, StringComparison.OrdinalIgnoreCase)), primaryDae, string.Join("|", primaryReferences.Where(reference => reference.Kind == ".dae.phyre").Select(reference => reference.Path)));

        var texlist = texlists.FirstOrDefault(item => item.RelativePath.EndsWith("/fp/tex/texlist.txt", StringComparison.OrdinalIgnoreCase));
        var missingTexlistEntries = texlists.SelectMany(static item => item.Entries).Count(static entry => !entry.Exists);
        yield return Gate("texlist_entries_resolve_when_present", missingTexlistEntries == 0, "0 missing texlist refs", $"{missingTexlistEntries} missing / {texlists.Sum(static item => item.Entries.Length)} texlist refs");

        if (!area.Equals("map/azit/azit00", StringComparison.OrdinalIgnoreCase) || rootManifest is null)
        {
            yield break;
        }

        rootManifest.NamedIndices.TryGetValue(leaf, out var leafIndex);
        yield return Gate("azit00_pilot_root_manifest_file_names_count", rootManifest.FileNamesCount == 14, "14", rootManifest.FileNamesCount.ToString());
        yield return Gate("azit00_pilot_root_manifest_area_symbol_index", leafIndex == 4, $"{leaf}=4", $"{leaf}={leafIndex}");
        yield return Gate("azit00_pilot_primary_dae_size", primaryDaeFile?.Length == 417_515, $"{primaryDae} length 417515", primaryDaeFile is null ? "missing" : primaryDaeFile.Length.ToString());

        yield return Gate("two_d_manifest_file_names_count", twoDManifest?.FileNamesCount == 45, "45", twoDManifest?.FileNamesCount.ToString() ?? "missing");
        yield return Gate("two_d_manifest_all_dependencies_exist", twoDManifest is not null && twoDManifest.MissingReferenceCount == 0 && twoDManifest.References.Length == 45, "45/45 exists", twoDManifest is null ? "missing" : $"{twoDManifest.ExistingReferenceCount}/{twoDManifest.References.Length} exists");

        var twoDDaeFile = allFiles.FirstOrDefault(file => string.Equals(file.RelativePath, twoDDae, StringComparison.OrdinalIgnoreCase));
        yield return Gate("azit00_pilot_two_d_dae_exists_and_size", twoDDaeFile?.Length == 92_566, $"{twoDDae} length 92566", twoDDaeFile is null ? "missing" : twoDDaeFile.Length.ToString());

        var agsFile = allFiles.FirstOrDefault(file => string.Equals(file.RelativePath, textureAnimationAgs, StringComparison.OrdinalIgnoreCase));
        yield return Gate("azit00_pilot_two_d_texture_animation_ags_exists_and_size", agsFile?.Length == 3_523, $"{textureAnimationAgs} length 3523", agsFile is null ? "missing" : agsFile.Length.ToString());

        var expectedSizes = new long[] { 35_452, 24_556, 35_452, 90_092 };
        var actualSizes = texlist?.Entries.Select(static entry => entry.Length ?? -1).ToArray() ?? Array.Empty<long>();
        yield return Gate("azit00_pilot_fp_texlist_four_entries", texlist is not null && texlist.Entries.Length == 4, "4", texlist?.Entries.Length.ToString() ?? "missing");
        yield return Gate("azit00_pilot_fp_texlist_expected_sizes", actualSizes.SequenceEqual(expectedSizes), string.Join(",", expectedSizes), string.Join(",", actualSizes));

        var tempFile = allFiles.FirstOrDefault(file => string.Equals(file.RelativePath, $"{area}/tex/d3d11/temp", StringComparison.OrdinalIgnoreCase));
        yield return Gate("azit00_pilot_temp_file_is_zero_byte_and_excluded", tempFile?.Length == 0, "0 byte temp exists but excluded", tempFile is null ? "missing" : $"{tempFile.Length} bytes");
    }

    private static MapGateCheck Gate(string name, bool passed, string expected, string actual) =>
        new(name, passed, expected, actual);

    private static string ExtractPlatformBlock(string text, string platform)
    {
        var marker = $"#ifdef PHYRE_RENDERING_PLATFORM_{platform}";
        var start = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return "";
        }

        var endMarker = $"#endif //! PHYRE_RENDERING_PLATFORM_{platform}";
        var end = text.IndexOf(endMarker, start, StringComparison.OrdinalIgnoreCase);
        return end < 0 ? text[start..] : text[start..end];
    }

    private static string NormalizeGameDataPath(string raw)
    {
        var path = raw.Replace('\\', '/');
        const string marker = "/GameData/PS3Data/";
        var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return index < 0 ? "" : Program.NormalizePath(path[(index + marker.Length)..]);
    }

    private static string Classify(string path)
    {
        var lower = path.ToLowerInvariant();
        if (lower.EndsWith(".dds.phyre", StringComparison.Ordinal)) return ".dds.phyre";
        if (lower.EndsWith(".dae.phyre", StringComparison.Ordinal)) return ".dae.phyre";
        if (lower.EndsWith(".ags.phyre", StringComparison.Ordinal)) return ".ags.phyre";
        if (Regex.IsMatch(lower, @"\.fx#[0-9a-f]+\.phyre$")) return ".fx#hash.phyre";
        if (lower.EndsWith(".fx.phyre", StringComparison.Ordinal)) return ".fx.phyre";
        if (lower.EndsWith(".ahwin32", StringComparison.Ordinal)) return ".ahwin32";
        if (lower.EndsWith(".txt", StringComparison.Ordinal)) return ".txt";
        if (lower.EndsWith(".bin", StringComparison.Ordinal)) return ".bin";
        var extension = Path.GetExtension(lower);
        return extension.Length == 0 ? "<no-ext>" : extension;
    }

    private sealed record AreaFile(string FullPath, string RelativePath, long Length);
}

public sealed record MapExportManifest(
    DateTimeOffset GeneratedAtUtc,
    string Ps3Root,
    string Area,
    string Platform,
    string AreaRoot,
    int FileCount,
    long TotalBytes,
    MapAreaFile[] Files,
    MapManifest[] Manifests,
    MapTexlist[] Texlists,
    string PrimaryDaePath,
    string TwoDDaePath,
    string TextureAnimationAgsPath,
    string PrimaryAssetLayer,
    MapManifestReference[] CandidateReferences,
    MapGateCheck[] Gates,
    string DecisionBand,
    string NextStrike);

public sealed record MapAreaFile(string Path, long Length, string Kind);

public sealed record MapManifest(
    string RelativePath,
    long Length,
    string Platform,
    int FileNamesCount,
    IReadOnlyDictionary<string, int> NamedIndices,
    MapManifestReference[] References,
    int ExistingReferenceCount,
    int MissingReferenceCount);

public sealed record MapManifestReference(string Path, string Kind, bool Exists, long? Length);

public sealed record MapTexlist(string RelativePath, long Length, MapTexlistEntry[] Entries);

public sealed record MapTexlistEntry(string Path, bool Exists, long? Length);

public sealed record MapGateCheck(string Name, bool Passed, string Expected, string Actual);

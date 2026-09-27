using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

return Run(args);

static int Run(string[] args)
{
    var options = Options.Parse(args);
    if (options.ShowHelp)
    {
        PrintHelp();
        return 0;
    }

    try
    {
        return options.Command switch
        {
            "catalog" => RunCatalog(options),
            "ida-seeds" => RunIdaSeeds(options),
            _ => Fail($"Unknown command '{options.Command}'. Use --help for usage.")
        };
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Ps3MapViewerLab failed: {ex.Message}");
        return 1;
    }
}

static int RunCatalog(Options options)
{
    var ps3Root = Path.GetFullPath(options.Ps3Root);
    if (!Directory.Exists(ps3Root))
        return Fail($"ps3data root not found: {ps3Root}");

    var outputRoot = Path.GetFullPath(options.OutputRoot);
    Directory.CreateDirectory(outputRoot);

    var areas = new List<MapViewerArea>();
    foreach (var family in options.Families)
    {
        var familyRoot = Path.Combine(ps3Root, family);
        if (!Directory.Exists(familyRoot))
        {
            Console.Error.WriteLine($"Skipping missing family: {familyRoot}");
            continue;
        }

        areas.AddRange(ScanFamily(ps3Root, familyRoot, family, options));
    }

    var catalog = new MapViewerCatalog(
        GeneratedAt: DateTimeOffset.Now,
        SourceRoot: options.Portable ? "<ps3data>" : ps3Root,
        Platform: options.Platform,
        Families: options.Families,
        Areas: areas.OrderBy(static x => x.Family, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static x => x.AreaKey, StringComparer.OrdinalIgnoreCase)
            .ToArray(),
        Summary: BuildSummary(areas));

    var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
    var jsonPath = Path.Combine(outputRoot, "ps3-mapviewer-catalog.json");
    var csvPath = Path.Combine(outputRoot, "ps3-mapviewer-areas.csv");
    File.WriteAllText(jsonPath, JsonSerializer.Serialize(catalog, jsonOptions), Encoding.UTF8);
    File.WriteAllText(csvPath, ToCsv(catalog.Areas), Encoding.UTF8);

    Console.WriteLine($"Catalog areas: {catalog.Areas.Length}");
    Console.WriteLine($"Files indexed: {catalog.Summary.TotalFiles.ToString(CultureInfo.InvariantCulture)}");
    Console.WriteLine($"JSON: {jsonPath}");
    Console.WriteLine($"CSV: {csvPath}");
    return 0;
}

static IReadOnlyList<MapViewerArea> ScanFamily(string ps3Root, string familyRoot, string family, Options options)
{
    var files = Directory.EnumerateFiles(familyRoot, "*", SearchOption.AllDirectories)
        .Select(path => new MapFile(path, NormalizePath(Path.GetRelativePath(ps3Root, path)), NormalizePath(Path.GetRelativePath(familyRoot, path))))
        .ToArray();

    IEnumerable<IGrouping<string, MapFile>> groups = files
        .GroupBy(file => GetAreaKey(file.FamilyRelativePath), StringComparer.OrdinalIgnoreCase)
        .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

    if (options.MaxAreas is { } maxAreas)
        groups = groups.Take(maxAreas).ToArray();

    var result = new List<MapViewerArea>();
    foreach (var group in groups)
    {
        var groupFiles = group.ToArray();
        var countByKind = CountByKind(groupFiles);
        var manifestFiles = groupFiles.Where(static file => file.FullPath.EndsWith(".ahwin32", StringComparison.OrdinalIgnoreCase)).ToArray();
        var texlistFiles = groupFiles.Where(static file => Path.GetFileName(file.FullPath).Equals("texlist.txt", StringComparison.OrdinalIgnoreCase)).ToArray();

        var manifestRefs = manifestFiles
            .SelectMany(file => ExtractManifestReferences(ps3Root, file.FullPath, file.RelativePath, options.Platform))
            .OrderBy(static x => x.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var texlistEntries = texlistFiles
            .SelectMany(file => ReadTexlistEntries(ps3Root, file.FullPath, file.RelativePath))
            .OrderBy(static x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var area = new MapViewerArea(
            Family: family,
            AreaKey: group.Key,
            RelativeRoot: GetAreaRelativeRoot(family, group.Key),
            FileCount: groupFiles.Length,
            TotalBytes: groupFiles.Sum(static file => new FileInfo(file.FullPath).Length),
            CountsByKind: countByKind,
            Ahwin32Files: Relatives(groupFiles, ".ahwin32"),
            AhFiles: Relatives(groupFiles, ".ah"),
            DaeFiles: Relatives(groupFiles, ".dae.phyre"),
            DdsFiles: Relatives(groupFiles, ".dds.phyre"),
            AgsFiles: Relatives(groupFiles, ".ags.phyre"),
            TexlistFiles: texlistFiles.Select(static x => x.RelativePath).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            ManifestReferences: manifestRefs,
            TexlistEntries: texlistEntries,
            DecisionBand: DetermineDecisionBand(countByKind),
            NextStrike: DetermineNextStrike(countByKind, manifestRefs, texlistEntries));

        result.Add(area);
    }

    return result;
}

static Dictionary<string, int> CountByKind(IEnumerable<MapFile> files)
{
    var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    foreach (var file in files)
    {
        var kind = Classify(file.FullPath);
        counts[kind] = counts.GetValueOrDefault(kind) + 1;
    }

    return counts.OrderBy(static x => x.Key, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(static x => x.Key, static x => x.Value, StringComparer.OrdinalIgnoreCase);
}

static string[] Relatives(IEnumerable<MapFile> files, string suffix) =>
    files.Where(file => file.FullPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        .Select(static file => file.RelativePath)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();

static IReadOnlyList<ManifestReference> ExtractManifestReferences(string ps3Root, string manifestPath, string manifestRelativePath, string platform)
{
    var refs = new List<ManifestReference>();
    var text = File.ReadAllText(manifestPath, Encoding.UTF8);
    foreach (Match match in Regex.Matches(text, "\"(?<path>[^\"]+)\""))
    {
        var raw = match.Groups["path"].Value.Replace('\\', '/');
        if (!raw.Contains("/GameData/PS3Data/", StringComparison.OrdinalIgnoreCase))
            continue;

        if (!raw.Contains($"/{platform}/", StringComparison.OrdinalIgnoreCase))
            continue;

        var relative = NormalizeGameDataPath(raw);
        var full = Path.Combine(ps3Root, relative.Replace('/', Path.DirectorySeparatorChar));
        refs.Add(new ManifestReference(
            Manifest: manifestRelativePath,
            Kind: Classify(relative),
            Path: relative,
            Exists: File.Exists(full)));
    }

    return refs;
}

static IReadOnlyList<TexlistEntry> ReadTexlistEntries(string ps3Root, string texlistPath, string texlistRelativePath)
{
    var parent = NormalizePath(Path.GetDirectoryName(texlistRelativePath) ?? "");
    var entries = new List<TexlistEntry>();
    foreach (var rawLine in File.ReadLines(texlistPath))
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || !line.EndsWith(".dds.phyre", StringComparison.OrdinalIgnoreCase))
            continue;

        var d3d11Relative = NormalizePath(Path.Combine(parent, "d3d11", line));
        var full = Path.Combine(ps3Root, d3d11Relative.Replace('/', Path.DirectorySeparatorChar));
        entries.Add(new TexlistEntry(texlistRelativePath, d3d11Relative, File.Exists(full)));
    }

    return entries;
}

static MapViewerSummary BuildSummary(IReadOnlyList<MapViewerArea> areas)
{
    var countsByFamily = areas
        .GroupBy(static area => area.Family, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(static group => group.Key, static group => group.Sum(static area => area.FileCount), StringComparer.OrdinalIgnoreCase);

    var countsByKind = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    foreach (var area in areas)
    {
        foreach (var (kind, count) in area.CountsByKind)
            countsByKind[kind] = countsByKind.GetValueOrDefault(kind) + count;
    }

    var manifestRefsByKind = areas
        .SelectMany(static area => area.ManifestReferences)
        .GroupBy(static item => item.Kind, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.OrdinalIgnoreCase);

    return new MapViewerSummary(
        TotalAreas: areas.Count,
        TotalFiles: areas.Sum(static area => area.FileCount),
        TotalBytes: areas.Sum(static area => area.TotalBytes),
        CountsByFamily: countsByFamily.OrderBy(static x => x.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(static x => x.Key, static x => x.Value),
        CountsByKind: countsByKind.OrderBy(static x => x.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(static x => x.Key, static x => x.Value),
        ManifestReferencesByKind: manifestRefsByKind.OrderBy(static x => x.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(static x => x.Key, static x => x.Value),
        MissingManifestReferences: areas.SelectMany(static area => area.ManifestReferences).Count(static item => !item.Exists),
        MissingTexlistEntries: areas.SelectMany(static area => area.TexlistEntries).Count(static item => !item.Exists));
}

static string ToCsv(IReadOnlyList<MapViewerArea> areas)
{
    var builder = new StringBuilder();
    builder.AppendLine("family,area_key,relative_root,file_count,total_bytes,ahwin32,ah,dae_phyre,dds_phyre,ags_phyre,texlist_files,texlist_entries,manifest_dae_refs,manifest_dds_refs,manifest_ags_refs,manifest_shader_refs,missing_manifest_refs,missing_texlist_entries,decision_band,next_strike");
    foreach (var area in areas)
    {
        var manifestDae = area.ManifestReferences.Count(static x => x.Kind.Equals(".dae.phyre", StringComparison.OrdinalIgnoreCase));
        var manifestDds = area.ManifestReferences.Count(static x => x.Kind.Equals(".dds.phyre", StringComparison.OrdinalIgnoreCase));
        var manifestAgs = area.ManifestReferences.Count(static x => x.Kind.Equals(".ags.phyre", StringComparison.OrdinalIgnoreCase));
        var manifestShaders = area.ManifestReferences.Count(static x => x.Kind.Contains(".fx", StringComparison.OrdinalIgnoreCase));
        var row = new[]
        {
            area.Family,
            area.AreaKey,
            area.RelativeRoot,
            area.FileCount.ToString(CultureInfo.InvariantCulture),
            area.TotalBytes.ToString(CultureInfo.InvariantCulture),
            Count(area, ".ahwin32"),
            Count(area, ".ah"),
            Count(area, ".dae.phyre"),
            Count(area, ".dds.phyre"),
            Count(area, ".ags.phyre"),
            area.TexlistFiles.Length.ToString(CultureInfo.InvariantCulture),
            area.TexlistEntries.Length.ToString(CultureInfo.InvariantCulture),
            manifestDae.ToString(CultureInfo.InvariantCulture),
            manifestDds.ToString(CultureInfo.InvariantCulture),
            manifestAgs.ToString(CultureInfo.InvariantCulture),
            manifestShaders.ToString(CultureInfo.InvariantCulture),
            area.ManifestReferences.Count(static x => !x.Exists).ToString(CultureInfo.InvariantCulture),
            area.TexlistEntries.Count(static x => !x.Exists).ToString(CultureInfo.InvariantCulture),
            area.DecisionBand,
            area.NextStrike
        };

        builder.AppendLine(string.Join(",", row.Select(Csv)));
    }

    return builder.ToString();

    static string Count(MapViewerArea area, string kind) => area.CountsByKind.GetValueOrDefault(kind).ToString(CultureInfo.InvariantCulture);
}

static int RunIdaSeeds(Options options)
{
    var outputRoot = Path.GetFullPath(options.OutputRoot);
    Directory.CreateDirectory(outputRoot);
    var seedPath = Path.Combine(outputRoot, "ida-mapviewer-seeds.txt");
    var csvPath = Path.Combine(outputRoot, "ida-mapviewer-seeds.csv");
    var seeds = new[]
    {
        new IdaSeed(".ahwin32", "Find Phyre asset-header loader and generated manifest references."),
        new IdaSeed(".dae.phyre", "Find geometry carrier load path for map and btlmap."),
        new IdaSeed(".dds.phyre", "Find texture carrier load path and texture cache handoff."),
        new IdaSeed(".ags.phyre", "Find texture animation/atlas load path."),
        new IdaSeed("texlist.txt", "Find texture-list loader and folder-relative path resolution."),
        new IdaSeed("GameData/PS3Data/map", "Find field map path construction."),
        new IdaSeed("GameData/PS3Data/btlmap", "Find battle-map path construction."),
        new IdaSeed("g_fileNames", "Find auto-generated Phyre manifest symbol use."),
        new IdaSeed("D3D11", "Confirm platform branch used by the PC runtime."),
        new IdaSeed("Phyre", "Find generic Phyre reader and object constructors.")
    };

    File.WriteAllLines(seedPath, seeds.Select(static x => x.Pattern), Encoding.UTF8);
    File.WriteAllLines(csvPath, new[] { "pattern,purpose" }.Concat(seeds.Select(static x => $"{Csv(x.Pattern)},{Csv(x.Purpose)}")), Encoding.UTF8);
    Console.WriteLine($"IDA seeds: {seedPath}");
    Console.WriteLine($"IDA seed CSV: {csvPath}");
    return 0;
}

static string Classify(string path)
{
    var normalized = path.Replace('\\', '/');
    var lower = normalized.ToLowerInvariant();
    if (lower.EndsWith(".dds.phyre", StringComparison.Ordinal)) return ".dds.phyre";
    if (lower.EndsWith(".dae.phyre", StringComparison.Ordinal)) return ".dae.phyre";
    if (lower.EndsWith(".ags.phyre", StringComparison.Ordinal)) return ".ags.phyre";
    if (Regex.IsMatch(lower, @"\.fx#[0-9a-f]+\.phyre$")) return ".fx#hash.phyre";
    if (lower.EndsWith(".fx.phyre", StringComparison.Ordinal)) return ".fx.phyre";
    if (lower.EndsWith(".ahwin32", StringComparison.Ordinal)) return ".ahwin32";
    if (lower.EndsWith(".ah", StringComparison.Ordinal)) return ".ah";
    if (lower.EndsWith(".txt", StringComparison.Ordinal)) return ".txt";
    if (lower.EndsWith(".bin", StringComparison.Ordinal)) return ".bin";

    var extension = Path.GetExtension(normalized);
    return extension.Length == 0 ? "<no-ext>" : extension.ToLowerInvariant();
}

static string GetAreaKey(string relativePath)
{
    var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
    return parts.Length switch
    {
        >= 2 => $"{parts[0]}/{parts[1]}",
        1 => parts[0],
        _ => "(root)"
    };
}

static string GetAreaRelativeRoot(string family, string areaKey) =>
    areaKey == "(root)" ? family : $"{family}/{areaKey}";

static string DetermineDecisionBand(IReadOnlyDictionary<string, int> counts)
{
    var hasManifest = counts.GetValueOrDefault(".ahwin32") > 0;
    var hasDae = counts.GetValueOrDefault(".dae.phyre") > 0;
    var hasDds = counts.GetValueOrDefault(".dds.phyre") > 0;
    var hasAgs = counts.GetValueOrDefault(".ags.phyre") > 0;

    if (hasManifest && hasDae && hasDds) return hasAgs ? "static_viewer_plus_texture_animation_candidate" : "static_viewer_candidate";
    if (hasDae && hasDds) return "static_viewer_no_manifest_candidate";
    if (hasDae) return "geometry_only_candidate";
    if (hasDds) return "texture_only_or_slice";
    return "catalog_only";
}

static string DetermineNextStrike(IReadOnlyDictionary<string, int> counts, IReadOnlyList<ManifestReference> manifestRefs, IReadOnlyList<TexlistEntry> texlistEntries)
{
    if (manifestRefs.Any(static x => !x.Exists)) return "resolve_manifest_missing_paths";
    if (texlistEntries.Any(static x => !x.Exists)) return "resolve_texlist_missing_paths";
    if (counts.GetValueOrDefault(".dae.phyre") > 0) return "export_dae_phyre_to_gltf_static_candidate";
    if (counts.GetValueOrDefault(".dds.phyre") > 0) return "texture_gallery_or_material_probe";
    return "keep_cataloged_until_loader_xref";
}

static string NormalizeGameDataPath(string raw)
{
    var path = raw.Replace('\\', '/');
    const string marker = "/GameData/PS3Data/";
    var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
    if (index >= 0)
        path = path[(index + marker.Length)..];

    return NormalizePath(path);
}

static string NormalizePath(string path) =>
    path.Replace('\\', '/').TrimStart('/');

static string Csv(string value)
{
    if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        return value;

    return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static void PrintHelp()
{
    Console.WriteLine("""
Ps3MapViewerLab

Read-only map/btlmap catalog and IDA seed generator for FFX HD ps3data assets.

Usage:
  dotnet run --project RuntimeTools\Ps3MapViewerLab\Ps3MapViewerLab.csproj -- catalog [options]
  dotnet run --project RuntimeTools\Ps3MapViewerLab\Ps3MapViewerLab.csproj -- ida-seeds [options]

Options:
  --ps3-root <path>    ps3data root. Default: D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data
  --output <path>      Output folder. Default: work\ps3_mapviewer_lab
  --families <csv>     Families to scan. Default: map,btlmap
  --platform <name>    Manifest platform filter. Default: D3D11
  --max-areas <n>      Optional cap for fast probes.
  --portable           Do not write absolute ps3data root into JSON.
  --help               Show this help.

No game asset is edited, converted, deleted, or moved.
""");
}

internal sealed record Options(
    string Command,
    string Ps3Root,
    string OutputRoot,
    string[] Families,
    string Platform,
    int? MaxAreas,
    bool Portable,
    bool ShowHelp)
{
    public static Options Parse(string[] args)
    {
        var command = "catalog";
        var index = 0;
        if (args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal))
        {
            command = args[0].ToLowerInvariant();
            index = 1;
        }

        var ps3Root = Defaults.Ps3Root;
        var outputRoot = Defaults.OutputRoot;
        var families = new[] { "map", "btlmap" };
        var platform = "D3D11";
        int? maxAreas = null;
        var portable = false;
        var showHelp = false;

        while (index < args.Length)
        {
            var arg = args[index++];
            switch (arg)
            {
                case "--ps3-root":
                    ps3Root = RequireValue(args, ref index, arg);
                    break;
                case "--output":
                    outputRoot = RequireValue(args, ref index, arg);
                    break;
                case "--families":
                    families = RequireValue(args, ref index, arg)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    break;
                case "--platform":
                    platform = RequireValue(args, ref index, arg);
                    break;
                case "--max-areas":
                    maxAreas = int.Parse(RequireValue(args, ref index, arg), CultureInfo.InvariantCulture);
                    break;
                case "--portable":
                    portable = true;
                    break;
                case "--help":
                case "-h":
                case "/?":
                    showHelp = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {arg}");
            }
        }

        return new Options(command, ps3Root, outputRoot, families, platform, maxAreas, portable, showHelp);
    }

    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (index >= args.Length)
            throw new ArgumentException($"Missing value for {option}");

        return args[index++];
    }
}

internal sealed record MapFile(string FullPath, string RelativePath, string FamilyRelativePath);

internal sealed record MapViewerCatalog(
    DateTimeOffset GeneratedAt,
    string SourceRoot,
    string Platform,
    string[] Families,
    MapViewerArea[] Areas,
    MapViewerSummary Summary);

internal sealed record MapViewerArea(
    string Family,
    string AreaKey,
    string RelativeRoot,
    int FileCount,
    long TotalBytes,
    IReadOnlyDictionary<string, int> CountsByKind,
    string[] Ahwin32Files,
    string[] AhFiles,
    string[] DaeFiles,
    string[] DdsFiles,
    string[] AgsFiles,
    string[] TexlistFiles,
    ManifestReference[] ManifestReferences,
    TexlistEntry[] TexlistEntries,
    string DecisionBand,
    string NextStrike);

internal sealed record MapViewerSummary(
    int TotalAreas,
    int TotalFiles,
    long TotalBytes,
    IReadOnlyDictionary<string, int> CountsByFamily,
    IReadOnlyDictionary<string, int> CountsByKind,
    IReadOnlyDictionary<string, int> ManifestReferencesByKind,
    int MissingManifestReferences,
    int MissingTexlistEntries);

internal sealed record ManifestReference(string Manifest, string Kind, string Path, bool Exists);

internal sealed record TexlistEntry(string Texlist, string Path, bool Exists);

internal sealed record IdaSeed(string Pattern, string Purpose);

internal static class Defaults
{
    public const string Ps3Root = @"D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data";
    public const string OutputRoot = @"work\ps3_mapviewer_lab";
}

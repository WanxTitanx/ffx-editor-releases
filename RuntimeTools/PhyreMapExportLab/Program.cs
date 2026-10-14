using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PhyreModelExportLab;

namespace PhyreMapExportLab;

public static class Program
{
    private const string DefaultArea = "map/azit/azit00";

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static int Main(string[] args)
    {
        try
        {
            var command = args.Length == 0
                ? "export"
                : args[0] is "--help" or "-h" or "/?"
                    ? args[0]
                    : args[0].StartsWith("--", StringComparison.Ordinal)
                        ? "export"
                        : args[0].ToLowerInvariant();
            var options = ParseOptions(command == args.FirstOrDefault() ? args.Skip(1).ToArray() : args);

            return command switch
            {
                "manifest" => RunManifest(options),
                "export" => RunExport(options),
                "batch" => RunBatch(options),
                "--help" or "-h" or "/?" => Help(),
                _ => Unknown(command),
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int RunManifest(IReadOnlyDictionary<string, string> options)
    {
        var context = BuildContext(options);
        WriteJson(Path.Combine(context.OutputRoot, $"{context.AssetId}.map-export-manifest.json"), context.Manifest);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            context.AssetId,
            context.Manifest.DecisionBand,
            outputRoot = context.OutputRoot,
            gatePass = context.Manifest.Gates.All(static gate => gate.Passed),
        }, JsonOptions));
        return context.Manifest.Gates.All(static gate => gate.Passed) ? 0 : 2;
    }

    private static int RunExport(IReadOnlyDictionary<string, string> options)
    {
        var result = ExportArea(options);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            result.AssetId,
            manifest = result.ManifestPath,
            descriptor = result.DescriptorPath,
            linkDump = result.LinkDumpPath,
            stringIndex = result.StringIndexPath,
            gltf = result.GltfPath,
            result.DebugGltfPath,
            result.TexturedGltfPath,
            result.TexturedVertexColorGltfPath,
            result.TexturedPhyreSlotsGltfPath,
            result.TexturedPhyreSlotsVertexColorGltfPath,
            textureBinding = result.TextureBindingPath,
            materialSlotAnalysis = result.MaterialSlotAnalysisPath,
            result.Report.DescriptorDecisionBand,
            result.Report.ExportDecisionBand,
            result.Report.TextureStatus,
            result.Report.TextureCount,
            result.Report.PrimitiveCount,
            result.Report.VertexCount,
            result.Report.IndexCount,
            result.Report.TriangleCount,
            gatePass = result.Report.Gates.All(static gate => gate.Passed),
        }, JsonOptions));

        return result.GltfPath is not null && File.Exists(result.GltfPath) ? 0 : 2;
    }

    private static int RunBatch(IReadOnlyDictionary<string, string> options)
    {
        var listPath = Path.GetFullPath(GetRequiredOption(options, "list", "batch input CSV"));
        var outputRoot = Path.GetFullPath(GetOption(
            options,
            "output-root",
            GetOption(options, "output", GetLocalOutputRoot("maps", "batch"))));
        var viewerRoot = Path.GetFullPath(GetRequiredOption(options, "viewer-root", "viewer root"));
        var skipFailed = IsEnabled(options, "skip-failed");
        var areas = ReadBatchAreas(listPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var entries = new List<MapBatchCatalogEntry>();

        Directory.CreateDirectory(outputRoot);
        for (var index = 0; index < areas.Length; index++)
        {
            var area = areas[index];
            var areaOptions = new Dictionary<string, string>(options, StringComparer.OrdinalIgnoreCase)
            {
                ["area"] = area,
                ["output"] = Path.Combine(outputRoot, AreaToFolder(area)),
            };

            Console.WriteLine($"[{index + 1}/{areas.Length}] {area}");
            try
            {
                var result = ExportArea(areaOptions);
                entries.Add(ToCatalogEntry(result, viewerRoot, null));
            }
            catch (Exception exception)
            {
                entries.Add(ToCatalogEntry(area, viewerRoot, outputRoot, exception));
                Console.Error.WriteLine($"  blocked: {exception.Message}");
                if (!skipFailed)
                {
                    break;
                }
            }
        }

        var successCount = entries.Count(static entry => entry.Status == "success");
        var catalog = new MapBatchCatalog(
            DateTimeOffset.UtcNow,
            listPath,
            ToViewerPath(viewerRoot, outputRoot),
            areas.Length,
            successCount,
            entries.Count(static entry => entry.Status != "success"),
            entries.ToArray());
        var status = new MapBatchStatus(DateTimeOffset.UtcNow, catalog.Entries.Select(static entry => new MapBatchStatusRow(
            entry.Area,
            entry.PrimaryAssetLayer,
            entry.Status,
            entry.DecisionBand,
            entry.TextureStatus,
            entry.TextureCount,
            entry.TriangleCount,
            entry.Gltf,
            entry.Error)).ToArray());

        WriteJson(Path.Combine(outputRoot, "catalog.json"), catalog);
        WriteJson(Path.Combine(outputRoot, "status.json"), status);
        WriteStatusCsv(Path.Combine(outputRoot, "status.csv"), status.Rows);

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            total = areas.Length,
            success = successCount,
            blocked = entries.Count(static entry => entry.Status != "success"),
            catalog = Path.Combine(outputRoot, "catalog.json"),
            status = Path.Combine(outputRoot, "status.csv"),
        }, JsonOptions));

        return successCount > 0 ? 0 : 2;
    }

    private static MapAreaExportResult ExportArea(IReadOnlyDictionary<string, string> options)
    {
        var context = BuildContext(options);
        Directory.CreateDirectory(context.OutputRoot);

        var manifestPath = Path.Combine(context.OutputRoot, $"{context.AssetId}.map-export-manifest.json");
        WriteJson(manifestPath, context.Manifest);

        var descriptor = PhyreDescriptorParser.Parse(context.AssetId, context.PrimaryDaePath, context.Portable);
        var descriptorPath = Path.Combine(context.OutputRoot, $"{context.AssetId}.phyre-descriptor-report.json");
        WriteJson(descriptorPath, descriptor);
        var linkDump = PhyreDescriptorParser.DumpLinks(context.AssetId, context.PrimaryDaePath, context.Portable);
        var linkDumpPath = Path.Combine(context.OutputRoot, $"{context.AssetId}.phyre-link-dump.json");
        WriteJson(linkDumpPath, linkDump);
        var stringIndexPath = Path.Combine(context.OutputRoot, $"{context.AssetId}.phyre-string-index.json");
        WriteJson(stringIndexPath, PhyreDescriptorParser.BuildStringIndex(context.AssetId, context.PrimaryDaePath, context.Portable));
        var sceneGraph = PhyreDescriptorParser.ParseSceneGraph(context.AssetId, context.PrimaryDaePath, context.Portable);
        var sceneGraphPath = Path.Combine(context.OutputRoot, $"{context.AssetId}.phyre-scene-graph-report.json");
        WriteJson(sceneGraphPath, sceneGraph);
        var instanceMap = PhyreDescriptorParser.ParseInstanceMap(context.AssetId, context.PrimaryDaePath, context.Portable);
        var instanceMapPath = Path.Combine(context.OutputRoot, $"{context.AssetId}.phyre-instance-map.json");
        WriteJson(instanceMapPath, instanceMap);

        var nodePerObject = IsEnabled(options, "node-per-object");
        var groupByMeshInstance = IsEnabled(options, "instance-group");
        var submeshToInstance = instanceMap.SubmeshToInstance;
        var instanceNames = instanceMap.Instances.ToDictionary(
            static entry => entry.InstanceId,
            static entry => entry.Name);
        MapTextureBindingResult? textureBinding = null;
        string? textureBindingPath = null;
        string? materialSlotAnalysisPath = null;
        string? texturedPhyreSlotsGltfPath = null;
        string? texturedPhyreSlotsVertexColorGltfPath = null;
        DescriptorStaticGltfExportReport exportReport;
        DescriptorStaticGltfExportReport? debugExportReport = null;
        string? debugGltfPath = null;
        string? texturedGltfPath = null;
        string? texturedVertexColorGltfPath = null;
        if (descriptor.DecisionBand == "mesh_descriptor_decode_candidate")
        {
            var source = File.ReadAllBytes(context.PrimaryDaePath);
            debugGltfPath = Path.Combine(context.OutputRoot, $"{context.AssetId}.static-debug.gltf");
            debugExportReport = DescriptorStaticGltfWriter.Write(
                debugGltfPath,
                context.AssetId,
                source,
                descriptor,
                nodePerObject: nodePerObject && !groupByMeshInstance,
                groupByMeshInstance: groupByMeshInstance,
                submeshToInstance: groupByMeshInstance ? submeshToInstance : null,
                instanceNames: groupByMeshInstance ? instanceNames : null);
            exportReport = debugExportReport;

            textureBinding = MapTextureBinder.ExtractAndBind(
                context.Ps3Root,
                context.OutputRoot,
                context.Area,
                context.AssetId,
                context.Manifest.PrimaryAssetLayer,
                descriptor,
                context.Portable,
                IsEnabled(options, "flip-v"));
            textureBindingPath = Path.Combine(context.OutputRoot, $"{context.AssetId}.texture-binding-report.json");
            WriteJson(textureBindingPath, textureBinding.Report);

            /* Decode textureanimation.ags.phyre if present (atlas texture + frame data) */
            var agsPath = Path.Combine(context.Ps3Root, context.Area, "2d/mdl/d3d11/textureanimation.ags.phyre");
            Console.Error.WriteLine($"  AGS path: {agsPath} exists={File.Exists(agsPath)}");
            var agsAtlas = PhyreTextureAnimationDecoder.Decode(agsPath, context.OutputRoot, context.AssetId);
            if (agsAtlas is not null)
            {
                Console.Error.WriteLine($"  AGS atlas: {agsAtlas.Frames.Count} frames, {agsAtlas.Frames.Count(f => f.FrameIndex == 0)} pages");
                /* Write AGS frame data as JSON for the viewer */
                var agsJsonPath = Path.Combine(context.OutputRoot, $"{context.AssetId}.ags-animation.json");
                if (!File.Exists(agsJsonPath))
                {
                    var agsJson = new
                    {
                        schemaVersion = 1,
                        totalFrames = agsAtlas.Frames.Count,
                        frames = agsAtlas.Frames.Select(f => new
                        {
                            page = f.AtlasIndex,
                            frame = f.FrameIndex,
                            w = f.Width,
                            h = f.Height,
                            png = $"{context.AssetId}_ags_{f.AtlasIndex}_{f.FrameIndex}_{f.Width}_{f.Height}.png"
                        }),
                        sequences = agsAtlas.Sequences.Select(s => new
                        {
                            name = s.Name,
                            intervalMs = s.TimeInterval,
                            frameIds = s.FrameIds,
                            subTextureIds = s.SubTextureIds
                        })
                    };
                    WriteJson(agsJsonPath, agsJson);
                    Console.Error.WriteLine($"  AGS JSON: {agsJsonPath}");
                }
            }

            if (textureBinding.MaterialTextures.Length > 0)
            {
                texturedGltfPath = Path.Combine(context.OutputRoot, $"{context.AssetId}.static-textured.gltf");
                exportReport = DescriptorStaticGltfWriter.Write(
                    texturedGltfPath,
                    context.AssetId,
                    source,
                    descriptor,
                    texture: null,
                    materialTextures: textureBinding.MaterialTextures,
                    nodePerObject: nodePerObject && !groupByMeshInstance,
                    groupByMeshInstance: groupByMeshInstance,
                    submeshToInstance: groupByMeshInstance ? submeshToInstance : null,
                    instanceNames: groupByMeshInstance ? instanceNames : null);

                texturedVertexColorGltfPath = Path.Combine(context.OutputRoot, $"{context.AssetId}.static-textured-vertex-color.gltf");
                DescriptorStaticGltfWriter.Write(
                    texturedVertexColorGltfPath,
                    context.AssetId,
                    source,
                    descriptor,
                    texture: null,
                    materialTextures: textureBinding.MaterialTextures,
                    includeVertexColors: true,
                    nodePerObject: nodePerObject && !groupByMeshInstance,
                    groupByMeshInstance: groupByMeshInstance,
                    submeshToInstance: groupByMeshInstance ? submeshToInstance : null,
                    instanceNames: groupByMeshInstance ? instanceNames : null);
            }

            if (textureBinding.Root3DTextures.Length > 0)
            {
                var materialSlotAnalysis = MapTextureBinder.BuildPmeshMaterialSlotCandidate(
                    descriptor,
                    linkDump,
                    context.PrimaryDaePath,
                    textureBinding,
                    IsEnabled(options, "flip-v"));
                materialSlotAnalysisPath = Path.Combine(context.OutputRoot, $"{context.AssetId}.material-slot-analysis.json");
                WriteJson(materialSlotAnalysisPath, materialSlotAnalysis.Report);

                if (materialSlotAnalysis.MaterialTextures.Length > 0)
                {
                    texturedPhyreSlotsGltfPath = Path.Combine(context.OutputRoot, $"{context.AssetId}.static-textured-phyre-slots.gltf");
                    // This is the PRIMARY glTF the map viewer loads. For the SHADER materials whose base-color
                    // PAssetReference points at a PhyreDefaultLitShader .fx# (status blocked_texture_import_path_not_dds,
                    // azit03's 18) — which have NO texture at all — render the ACTUAL baked per-vertex Color/VtxColor
                    // Float4 stream instead of the garish DebugColors pastel. The flat MaterialColour shader constant
                    // is NOT serialized in-file (its CB offset 1008/1168 is beyond the 400/608-byte PParameterBuffer),
                    // so the per-vertex Color stream is the only real, file-resident albedo for these surfaces.
                    // The local-object-ref unbound materials (blocked_field172_local_object_ref_not_a_texture, the 23)
                    // are intentionally LEFT pastel — deferred to the in-game RenderDoc ground-truth protocol
                    // (docs/ai/FFX_MAP_RENDERDOC_CAPTURE_PROTOCOL_2026-06-06.md), not guessed offline.
                    // RE: docs/reverse/FFX_PHYRE_MAP_SHADER_MATERIAL_VERTEXCOLOR_AZIT03_2026-06-06.md.
                    // For Aurora/btlmap: every UNBOUND submesh with a per-vertex Color stream should use it
                    // (not only PhyreDefaultLitShader .fx# surfaces). Remaining unbound without Color stay neutral gray.
                    exportReport = DescriptorStaticGltfWriter.Write(
                        texturedPhyreSlotsGltfPath,
                        context.AssetId,
                        source,
                        descriptor,
                        texture: null,
                        materialTextures: materialSlotAnalysis.MaterialTextures,
                        unboundUseVertexColor: true,
                        unboundVertexColorSubmeshIds: null,
                        unboundNeutralGrayFallback: context.Area.StartsWith("btlmap/", StringComparison.OrdinalIgnoreCase),
                        nodePerObject: nodePerObject && !groupByMeshInstance,
                        groupByMeshInstance: groupByMeshInstance,
                        submeshToInstance: groupByMeshInstance ? submeshToInstance : null,
                        instanceNames: groupByMeshInstance ? instanceNames : null);

                    texturedPhyreSlotsVertexColorGltfPath = Path.Combine(context.OutputRoot, $"{context.AssetId}.static-textured-phyre-slots-vertex-color.gltf");
                    DescriptorStaticGltfWriter.Write(
                        texturedPhyreSlotsVertexColorGltfPath,
                        context.AssetId,
                        source,
                        descriptor,
                        texture: null,
                        materialTextures: materialSlotAnalysis.MaterialTextures,
                        includeVertexColors: true,
                        nodePerObject: nodePerObject && !groupByMeshInstance,
                        groupByMeshInstance: groupByMeshInstance,
                        submeshToInstance: groupByMeshInstance ? submeshToInstance : null,
                        instanceNames: groupByMeshInstance ? instanceNames : null);
                }
            }
        }
        else
        {
            exportReport = DescriptorStaticGltfExportReport.Blocked(
                context.AssetId,
                descriptor.DecisionBand,
                "descriptor gate did not pass for map pilot; keep manifest only and inspect report");
        }

        var exportPath = Path.Combine(context.OutputRoot, $"{context.AssetId}.static-export-report.json");
        var mapReport = new MapStaticExportReport(
            DateTimeOffset.UtcNow,
            context.Manifest.Area,
            context.AssetId,
            context.Manifest.PrimaryAssetLayer,
            context.Portable ? ToPortable(context.PrimaryDaePath) : context.PrimaryDaePath,
            manifestPath,
            descriptorPath,
            linkDumpPath,
            stringIndexPath,
            texturedPhyreSlotsGltfPath ?? texturedGltfPath ?? debugGltfPath,
            debugGltfPath,
            texturedGltfPath,
            texturedVertexColorGltfPath,
            texturedPhyreSlotsGltfPath,
            texturedPhyreSlotsVertexColorGltfPath,
            textureBindingPath,
            materialSlotAnalysisPath,
            context.Manifest.Gates,
            descriptor.DecisionBand,
            exportReport.DecisionBand,
            exportReport.PromotionStatus,
            exportReport.PrimitiveCount,
            exportReport.VertexCount,
            exportReport.IndexCount,
            exportReport.TriangleCount,
            exportReport.PositionBounds,
            exportReport.NextStrike,
            exportReport.TextureStatus ?? "texture_not_requested",
            exportReport.TextureCount,
            exportReport.TexturePaths ?? Array.Empty<string>(),
            textureBinding?.Report.DecisionBand ?? "texture_binding_not_attempted",
            "2d slice, AGS, real Phyre material slots, shader state, collision, and runtime exact behavior remain unresolved");
        WriteJson(exportPath, mapReport);

        var finalGltfPath = texturedPhyreSlotsGltfPath ?? texturedGltfPath ?? debugGltfPath;
        return new MapAreaExportResult(
            context.Area,
            context.AssetId,
            context.OutputRoot,
            manifestPath,
            descriptorPath,
            linkDumpPath,
            stringIndexPath,
            exportPath,
            finalGltfPath,
            debugGltfPath,
            texturedGltfPath,
            texturedVertexColorGltfPath,
            texturedPhyreSlotsGltfPath,
            texturedPhyreSlotsVertexColorGltfPath,
            textureBindingPath,
            materialSlotAnalysisPath,
            mapReport);
    }

    private static ExportContext BuildContext(IReadOnlyDictionary<string, string> options)
    {
        var ps3Root = GetRequiredRoot(options, "ps3-root", "FFX_PS3DATA_ROOT");
        var outputRoot = Path.GetFullPath(GetOption(options, "output", GetLocalOutputRoot("maps", "single")));
        var area = NormalizePath(GetOption(options, "area", DefaultArea));
        var platform = GetOption(options, "platform", "D3D11");
        var portable = IsEnabled(options, "portable");
        var include2d = IsEnabled(options, "include-2d");

        var manifest = MapAreaResolver.Resolve(ps3Root, area, platform, include2d, portable);
        var assetId = AreaToAssetId(area);
        var primaryDaePath = Path.Combine(ps3Root, manifest.PrimaryDaePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(primaryDaePath))
        {
            throw new InvalidOperationException($"Primary DAE not found: {primaryDaePath}");
        }

        return new ExportContext(ps3Root, outputRoot, area, assetId, platform, portable, primaryDaePath, manifest);
    }

    internal static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine, new UTF8Encoding(false));
    }

    internal static string ToPortable(string path) => PhyreModelExportLab.Program.ToPortablePath(path);

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = arg[2..];
            if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[key] = args[++index];
            }
            else
            {
                options[key] = "true";
            }
        }

        return options;
    }

    private static string GetOption(IReadOnlyDictionary<string, string> options, string key, string defaultValue) =>
        options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : defaultValue;

    private static string GetRequiredOption(
        IReadOnlyDictionary<string, string> options,
        string key,
        string description)
    {
        if (options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        throw new InvalidOperationException($"--{key} is required ({description}).");
    }

    private static string GetRequiredRoot(
        IReadOnlyDictionary<string, string> options,
        string key,
        string environmentVariable)
    {
        string value = GetOption(options, key, Environment.GetEnvironmentVariable(environmentVariable) ?? string.Empty);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"--{key} is required unless {environmentVariable} points to the user's extracted FFX data.");
        }

        return Path.GetFullPath(value);
    }

    private static string GetLocalOutputRoot(params string[] segments)
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FFXProjectEditor",
            "PhyreExports");
        return Path.Combine(new[] { root }.Concat(segments).ToArray());
    }

    private static bool IsEnabled(IReadOnlyDictionary<string, string> options, string key) =>
        options.TryGetValue(key, out var value)
        && (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase));

    private static string AreaToAssetId(string area) =>
        NormalizePath(area).Replace('/', '_').Replace('\\', '_');

    private static string AreaToFolder(string area)
    {
        var normalized = NormalizePath(area);
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3 && string.Equals(parts[0], "map", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(parts[1], parts[2]);
        }

        // btlmap/azit/azit03_a -> public/maps/btlmap/azit/azit03_a (keep the btlmap prefix)
        if (parts.Length >= 3 && string.Equals(parts[0], "btlmap", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(parts[0], parts[1], parts[2]);
        }

        return normalized.Replace('/', Path.DirectorySeparatorChar);
    }

    internal static string NormalizePath(string path) =>
        path.Replace('\\', '/').Trim('/');

    private static string[] ReadBatchAreas(string listPath)
    {
        if (!File.Exists(listPath))
        {
            throw new FileNotFoundException("Batch list not found", listPath);
        }

        var lines = File.ReadLines(listPath).Where(static line => !string.IsNullOrWhiteSpace(line)).ToArray();
        if (lines.Length == 0)
        {
            return Array.Empty<string>();
        }

        var header = SplitCsvLine(lines[0]);
        var entityIndex = Array.FindIndex(header, static column => column.Equals("entity", StringComparison.OrdinalIgnoreCase));
        if (entityIndex < 0)
        {
            throw new InvalidOperationException("Batch list must contain an entity column.");
        }

        return lines.Skip(1)
            .Select(SplitCsvLine)
            .Where(values => entityIndex < values.Length)
            .Select(values => NormalizePath(values[entityIndex]))
            .Where(static area =>
                (area.StartsWith("map/", StringComparison.OrdinalIgnoreCase)
                 || area.StartsWith("btlmap/", StringComparison.OrdinalIgnoreCase))
                && area.Count(static ch => ch == '/') >= 2)
            .ToArray();
    }

    private static string[] SplitCsvLine(string line)
    {
        var values = new List<string>();
        var value = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var ch = line[index];
            if (ch == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    value.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (ch == ',' && !quoted)
            {
                values.Add(value.ToString());
                value.Clear();
            }
            else
            {
                value.Append(ch);
            }
        }

        values.Add(value.ToString());
        return values.ToArray();
    }

    private static MapBatchCatalogEntry ToCatalogEntry(MapAreaExportResult result, string viewerRoot, string? error)
    {
        var failedGates = result.Report.Gates.Where(static gate => !gate.Passed).Select(static gate => gate.Name).ToArray();
        var status = result.GltfPath is not null && File.Exists(result.GltfPath) ? "success" : "blocked";
        return new MapBatchCatalogEntry(
            result.Area,
            result.Area.Replace("map/", "", StringComparison.OrdinalIgnoreCase),
            result.AssetId,
            result.Report.PrimaryAssetLayer,
            status,
            result.Report.ExportDecisionBand,
            result.Report.TextureStatus,
            result.Report.TextureCount,
            result.Report.TriangleCount,
            result.Report.VertexCount,
            ToViewerPath(viewerRoot, result.OutputRoot),
            ToViewerPath(viewerRoot, result.GltfPath),
            ToViewerPath(viewerRoot, result.ManifestPath),
            ToViewerPath(viewerRoot, result.ReportPath),
            ToViewerPath(viewerRoot, result.TextureBindingPath),
            ToViewerPath(viewerRoot, result.MaterialSlotAnalysisPath),
            null,
            error,
            failedGates);
    }

    private static MapBatchCatalogEntry ToCatalogEntry(string area, string viewerRoot, string outputRoot, Exception exception)
    {
        var output = Path.Combine(outputRoot, AreaToFolder(area));
        return new MapBatchCatalogEntry(
            area,
            area.Replace("map/", "", StringComparison.OrdinalIgnoreCase),
            AreaToAssetId(area),
            "unknown",
            ClassifyException(exception),
            "blocked_before_export",
            "texture_not_attempted",
            0,
            0,
            0,
            ToViewerPath(viewerRoot, output),
            null,
            null,
            null,
            null,
            null,
            null,
            exception.Message,
            Array.Empty<string>());
    }

    private static string ClassifyException(Exception exception)
    {
        var message = exception.Message;
        if (exception is DirectoryNotFoundException || message.Contains("root not found", StringComparison.OrdinalIgnoreCase))
        {
            return "missing";
        }

        if (message.Contains("Primary DAE not found", StringComparison.OrdinalIgnoreCase))
        {
            return "blocked_no_primary_dae";
        }

        if (message.Contains("descriptor", StringComparison.OrdinalIgnoreCase))
        {
            return "descriptor_failed";
        }

        return "blocked";
    }

    private static string? ToViewerPath(string viewerRoot, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var relative = Path.GetRelativePath(viewerRoot, Path.GetFullPath(path));
        return NormalizePath(relative);
    }

    private static void WriteStatusCsv(string path, IReadOnlyList<MapBatchStatusRow> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var builder = new StringBuilder();
        builder.AppendLine("area,primaryAssetLayer,status,decisionBand,textureStatus,textureCount,triangleCount,gltf,error");
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(",", new[]
            {
                Csv(row.Area),
                Csv(row.PrimaryAssetLayer),
                Csv(row.Status),
                Csv(row.DecisionBand),
                Csv(row.TextureStatus),
                row.TextureCount.ToString(),
                row.TriangleCount.ToString(),
                Csv(row.Gltf),
                Csv(row.Error),
            }));
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
    }

    private static string Csv(string? value)
    {
        value ??= "";
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static int Help()
    {
        Console.WriteLine("""
PhyreMapExportLab

Read-only pilot exporter for FFX HD ps3data map assets.

Usage:
  PhyreMapExportLab.exe manifest [options]
  PhyreMapExportLab.exe export [options]
  PhyreMapExportLab.exe batch [options]

Options:
  --area <path>       Default: map/azit/azit00
  --ps3-root <path>   Extracted ps3data root (or set FFX_PS3DATA_ROOT).
  --output <path>     Default: the user's LocalAppData PhyreExports folder.
  --list <csv>        Required for batch; input with an entity column.
  --output-root <dir> Batch output root (defaults under LocalAppData).
  --viewer-root <dir> Required for batch catalog-relative paths.
  --skip-failed       Keep batch running when one map is blocked.
  --platform <name>   Default: D3D11
  --include-2d        Include 2d manifest in candidate export set. Default: false.
  --node-per-object   Emit one glTF node per submesh batch (identity transform; click-select granularity).
  --instance-group    Group glTF nodes by PMeshInstance (multi-primitive mesh per instance; verts stay world-baked).
  --flip-v            Flip ST/TEXCOORD_0 V when writing textured candidates. Default: false.
  --portable          Use portable paths in reports.

Generated export variants:
  static-debug.gltf                  Descriptor mesh with debug colors.
  static-textured.gltf               Root 3D .dds.phyre PNGs bound by material-id candidate.
  static-textured-vertex-color.gltf  Same texture candidate plus Color Float4 -> COLOR_0 candidate.
  static-textured-phyre-slots.gltf   Root 3D PNGs bound by PMesh/PMaterial/PParameterBuffer link-table candidate.
  static-textured-phyre-slots-vertex-color.gltf
                                      Same link-table candidate plus Color Float4 -> COLOR_0 candidate.
  phyre-scene-graph-report.json      Offline PNode local + parent-chain world compose (structural candidate).
  phyre-instance-map.json            PMeshInstance -> PMeshSegment span bridge (Tier C1 structural candidate).

No game asset is edited, moved, deleted, or repacked.
""");
        return 0;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        return Help() == 0 ? 1 : 1;
    }

    private sealed record ExportContext(
        string Ps3Root,
        string OutputRoot,
        string Area,
        string AssetId,
        string Platform,
        bool Portable,
        string PrimaryDaePath,
        MapExportManifest Manifest);

    private sealed record MapAreaExportResult(
        string Area,
        string AssetId,
        string OutputRoot,
        string ManifestPath,
        string DescriptorPath,
        string LinkDumpPath,
        string StringIndexPath,
        string ReportPath,
        string? GltfPath,
        string? DebugGltfPath,
        string? TexturedGltfPath,
        string? TexturedVertexColorGltfPath,
        string? TexturedPhyreSlotsGltfPath,
        string? TexturedPhyreSlotsVertexColorGltfPath,
        string? TextureBindingPath,
        string? MaterialSlotAnalysisPath,
        MapStaticExportReport Report);
}

public sealed record MapBatchCatalog(
    DateTimeOffset GeneratedAtUtc,
    string SourceList,
    string? OutputRoot,
    int TotalCount,
    int SuccessCount,
    int BlockedCount,
    MapBatchCatalogEntry[] Entries);

public sealed record MapBatchCatalogEntry(
    string Area,
    string AreaKey,
    string AssetId,
    string PrimaryAssetLayer,
    string Status,
    string DecisionBand,
    string TextureStatus,
    int TextureCount,
    int TriangleCount,
    int VertexCount,
    string? Folder,
    string? Gltf,
    string? Manifest,
    string? Report,
    string? TextureBindingReport,
    string? MaterialSlotAnalysisReport,
    string? Validator,
    string? Error,
    string[] FailedGates);

public sealed record MapBatchStatus(DateTimeOffset GeneratedAtUtc, MapBatchStatusRow[] Rows);

public sealed record MapBatchStatusRow(
    string Area,
    string PrimaryAssetLayer,
    string Status,
    string DecisionBand,
    string TextureStatus,
    int TextureCount,
    int TriangleCount,
    string? Gltf,
    string? Error);

public sealed record MapStaticExportReport(
    DateTimeOffset GeneratedAtUtc,
    string Area,
    string AssetId,
    string PrimaryAssetLayer,
    string PrimaryDae,
    string ManifestPath,
    string DescriptorReportPath,
    string PhyreLinkDumpPath,
    string PhyreStringIndexPath,
    string? GltfPath,
    string? DebugGltfPath,
    string? TexturedGltfPath,
    string? TexturedVertexColorGltfPath,
    string? TexturedPhyreSlotsGltfPath,
    string? TexturedPhyreSlotsVertexColorGltfPath,
    string? TextureBindingReportPath,
    string? MaterialSlotAnalysisReportPath,
    MapGateCheck[] Gates,
    string DescriptorDecisionBand,
    string ExportDecisionBand,
    string PromotionStatus,
    int PrimitiveCount,
    int VertexCount,
    int IndexCount,
    int TriangleCount,
    PositionBounds? PositionBounds,
    string NextStrike,
    string TextureStatus,
    int TextureCount,
    string[] TexturePaths,
    string TextureBindingDecisionBand,
    string UnresolvedScope);

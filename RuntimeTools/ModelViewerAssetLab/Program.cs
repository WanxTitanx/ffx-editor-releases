using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MotionLinkerLab;

namespace ModelViewerAssetLab;

public static class Program
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions OutputJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly string[] DefaultCatalogIds =
    {
        "m018", "m059", "m063", "m105", "m106", "m112", "m134",
        "m153", "m211", "m046", "m064", "m130", "m020", "m021",
    };

    private static readonly HashSet<string> DefaultExportIds = new(
        new[] { "m018", "m063", "m106", "m211", "m046", "m064" },
        StringComparer.OrdinalIgnoreCase);

    private const string DefaultTexturePngRoot = @"D:\FFX Mods\ps3data_textures_png";

    public static int Main(string[] args)
    {
        try
        {
            var command = args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal)
                ? "export"
                : args[0].ToLowerInvariant();
            var options = ParseOptions(command == "export" ? args.Skip(command == args.FirstOrDefault() ? 1 : 0).ToArray() : args.Skip(1).ToArray());

            return command switch
            {
                "export" => RunExport(options),
                _ => UnknownCommand(command),
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int RunExport(IReadOnlyDictionary<string, string> options)
    {
        var manifestRoot = Path.GetFullPath(GetOption(options, "manifest-root", @"work\motion_linker_lab_all"));
        var outputRoot = Path.GetFullPath(GetOption(options, "output", @"work\modelviewer_assets_v0"));
        var texturePngRoot = Path.GetFullPath(GetOption(options, "texture-png-root", DefaultTexturePngRoot));
        var portable = IsEnabled(options, "portable");
        var exportStructureMode = GetOption(options, "export-structure", "pilots").ToLowerInvariant();
        var monsterIds = ResolveCatalogMonsterIds(manifestRoot, GetOption(options, "monsters", string.Join(',', DefaultCatalogIds)));

        Directory.CreateDirectory(outputRoot);

        var labStatuses = ReadLabStatuses();
        var entries = new List<CatalogEntry>();
        var exportedAssets = new List<ExportedAsset>();
        var bindingRows = new List<BindingCandidateRow>();

        foreach (var monsterId in monsterIds)
        {
            var manifestPath = Path.Combine(manifestRoot, $"{monsterId}.motion-link.json");
            if (!File.Exists(manifestPath))
            {
                entries.Add(CatalogEntry.BlockedMissingManifest(monsterId, manifestPath));
                continue;
            }

            var manifest = ReadManifest(manifestPath);
            var labStatus = labStatuses.TryGetValue(monsterId, out var status)
                ? status
                : InferLabStatus(manifest);

            var shouldExport = ShouldExportStructure(exportStructureMode, monsterId, manifest.Ps2.Chr.Exists);
            string? openableAssetPath = null;
            int nodeCount = 0;
            string structureLayout = "not_exported";

            if (shouldExport)
            {
                var bindPose = ChrBindPoseReader.Parse(manifest.Ps2.Chr.Path);
                var asset = StructureGltfBuilder.Build(manifest, bindPose, labStatus);
                if (portable)
                {
                    asset = asset with { Extras = MakePortable(asset.Extras) };
                }

                var fileName = $"{monsterId}_structure.gltf";
                var outputPath = Path.Combine(outputRoot, fileName);
                StructureGltfWriter.Write(outputPath, asset);

                openableAssetPath = fileName.Replace('\\', '/');
                nodeCount = asset.SourceNodeCount;
                structureLayout = asset.LayoutMode;

                exportedAssets.Add(new ExportedAsset(
                    monsterId,
                    openableAssetPath,
                    "proved_structure_openable",
                    asset.DecisionBand,
                    nodeCount,
                    asset.PrimitiveCount,
                    structureLayout));

                bindingRows.AddRange(BuildBindingRows(manifest, bindPose, openableAssetPath));
            }

            var entry = BuildCatalogEntry(
                manifest,
                manifestPath,
                texturePngRoot,
                labStatus,
                shouldExport,
                openableAssetPath,
                nodeCount,
                structureLayout);

            entries.Add(portable ? MakePortable(entry) : entry);
        }

        var catalog = new ModelViewerCatalog(
            DateTimeOffset.UtcNow,
            portable ? "modelviewer-catalog-v0-portable" : "modelviewer-catalog-v0",
            "RuntimeTools/ModelViewerAssetLab",
            entries.FirstOrDefault(item => item.MonsterId == "m211" && item.OpenableAssetPath is not null)?.MonsterId
                ?? entries.FirstOrDefault(item => item.OpenableAssetPath is not null)?.MonsterId
                ?? string.Empty,
            new CatalogSummary(entries.Count, exportedAssets.Count, entries.Count(item => item.DecisionBand == "proved_structure_openable"), entries.Count(item => item.DecisionBand == "partial_embedded_clip_visible"), entries.Count(item => item.DecisionBand == "unsafe_motion_overlay")),
            entries.ToArray());

        WriteJson(Path.Combine(outputRoot, "modelviewer-catalog.json"), catalog);
        if (entries.Count > DefaultCatalogIds.Length)
        {
            WriteJson(Path.Combine(outputRoot, "modelviewer-catalog-all.json"), catalog);
        }

        WriteCatalogCsv(Path.Combine(outputRoot, "modelviewer-catalog.csv"), entries);
        WriteCoverageCsv(Path.Combine(outputRoot, "modelviewer-coverage.csv"), entries);
        WriteBindingCsv(Path.Combine(outputRoot, "node-binding-candidates.csv"), bindingRows);

        var report = new BatchExportReport(
            DateTimeOffset.UtcNow,
            portable ? ToPortablePath(manifestRoot) : manifestRoot,
            portable ? ToPortablePath(outputRoot) : outputRoot,
            string.Join(',', monsterIds),
            exportStructureMode,
            portable,
            catalog.Entries.Length,
            exportedAssets.Count,
            exportedAssets.ToArray(),
            "blocked_mgrp_playback",
            "Generated assets are structural glTF proxies from FFX .chr bind pose and .mgrp metadata. They are not decoded monster meshes.");
        WriteJson(Path.Combine(outputRoot, "batch-export-report.json"), report);
        WriteJson(Path.Combine(outputRoot, "catalog-regeneration-report.json"), report);

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            catalogPath = Path.Combine(outputRoot, "modelviewer-catalog.json"),
            coveragePath = Path.Combine(outputRoot, "modelviewer-coverage.csv"),
            exportedAssets = exportedAssets.Count,
            entries = entries.Count,
            portable,
            exportStructureMode,
            decisionBands = entries.GroupBy(item => item.DecisionBand).ToDictionary(group => group.Key, group => group.Count()),
        }, OutputJsonOptions));

        return 0;
    }

    private static CatalogEntry BuildCatalogEntry(
        MonsterMotionManifest manifest,
        string manifestPath,
        string texturePngRoot,
        string labStatus,
        bool exported,
        string? openableAssetPath,
        int nodeCount,
        string structureLayout)
    {
        var assetStatus = exported ? "proved_structure_openable" : "partial_model_linked";
        var motionStatus = InferMotionStatus(manifest, labStatus);
        var decisionBand = InferDecisionBand(manifest, labStatus, exported);
        var tags = BuildTags(manifest, labStatus, exported);
        var topology = BuildTopology(manifest, manifestPath, texturePngRoot);

        return new CatalogEntry(
            manifest.MonsterId,
            $"{manifest.MonsterId} FFX monster structure",
            new CatalogSourcePaths(
                manifest.Ps2.Chr.Path,
                manifest.Ps2.Motion.Residents.Where(item => item.Exists).Select(item => item.Path).ToArray(),
                manifest.Ps3.Model.Path,
                manifest.Ps3.Texture.Path,
                manifestPath),
            assetStatus,
            motionStatus,
            openableAssetPath,
            new[]
            {
                "docs/history/MONSTER_MOTION_LINKER_LAB_2026-06-02.md",
                "docs/history/MGRP_MODELVIEWER_READONLY_PLAN_2026-06-02.md",
                "docs/history/MGRP_NEXT_CAMPAIGN_CLOSEOUT_2026-06-02.md",
            },
            decisionBand,
            labStatus,
            manifest.Linkage.IdentityStatus,
            manifest.Linkage.MotionReadiness,
            manifest.Ps2.Motion.NonEmptyResidentCount,
            manifest.Ps3.EmbeddedAnimationClipCount,
            manifest.Ps3.SkeletonNodeMarkerCount,
            nodeCount,
            structureLayout,
            tags,
            topology);
    }

    private static AssetTopology BuildTopology(MonsterMotionManifest manifest, string manifestPath, string texturePngRoot)
    {
        var nodes = new List<FileGraphNode>();
        var edges = new List<FileGraphEdge>();
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        AddVirtualNode(nodes, seen, "monster", manifest.MonsterId, "monster_root", "proved", $"{manifest.MonsterId} root");

        var chrId = AddFileNode(nodes, seen, "ps2_chr", manifest.Ps2.Chr.Path, "ps2_chr_bind_pose", manifest.Ps2.Chr.Exists ? "proved_structure_openable" : "blocked_env_missing_path");
        AddEdge(edges, "monster", chrId, "bind pose / node table", "structural");

        foreach (var resident in manifest.Ps2.Motion.Residents.Where(item => item.Exists))
        {
            var role = resident.IsNonEmpty ? "ps2_mgrp_resident_nonempty" : "ps2_mgrp_resident_empty_stub";
            var band = resident.IsNonEmpty ? "blocked_mgrp_playback" : "partial_model_linked";
            var residentId = AddFileNode(nodes, seen, $"resident{resident.Slot}", resident.Path, role, band);
            AddEdge(edges, chrId, residentId, resident.IsNonEmpty ? "resident motion carrier" : "empty resident stub", band);
        }

        var ahId = AddFileNode(nodes, seen, "ps3_ah", manifest.Ps3.AhWin32.Path, "ps3_ah_manifest", manifest.Ps3.AhWin32.Exists ? "proved" : "blocked_env_missing_path");
        var daeId = AddFileNode(nodes, seen, "ps3_dae", manifest.Ps3.Model.Path, "ps3_dae_phyre_geometry_carrier", manifest.Ps3.Model.Exists ? "blocked_converter" : "blocked_env_missing_path");
        var ddsId = AddFileNode(nodes, seen, "ps3_dds", manifest.Ps3.Texture.Path, "ps3_dds_phyre_texture_carrier", manifest.Ps3.Texture.Exists ? "partial_model_linked" : "blocked_env_missing_path");
        AddEdge(edges, "monster", ahId, "PS3 asset manifest", "proved");
        AddEdge(edges, ahId, daeId, "model carrier reference", "structural");
        AddEdge(edges, ahId, ddsId, "texture carrier reference", "structural");

        var cdfPath = Path.Combine(manifest.Ps3.Root, $"{manifest.MonsterId}.cdf");
        if (File.Exists(cdfPath))
        {
            var cdfId = AddFileNode(nodes, seen, "ps3_cdf", cdfPath, "ps3_cdf_bounds_definition", "structural");
            AddEdge(edges, ahId, cdfId, "bounds/definition sidecar", "structural");
        }

        foreach (var file in EnumerateSmallFileSet(manifest.Ps3.Root))
        {
            if (seen.ContainsKey(file))
            {
                continue;
            }

            var role = ClassifyPs3Role(file);
            var band = role switch
            {
                "ps3_ags_texture_animation" => "partial_model_linked",
                "ps3_shader" => "structural",
                _ => "partial_model_linked",
            };
            var id = AddFileNode(nodes, seen, $"ps3_extra_{nodes.Count}", file, role, band);
            AddEdge(edges, ahId, id, "listed/co-located PS3 asset", band);
        }

        var textureRoot = Path.Combine(texturePngRoot, "chr", "mon", manifest.MonsterId);
        var extractedTextures = EnumerateSmallFileSet(textureRoot, "*.png");
        foreach (var texture in extractedTextures)
        {
            var texId = AddFileNode(nodes, seen, $"png_{nodes.Count}", texture, "extracted_png_texture", "partial_model_linked");
            AddEdge(edges, ddsId, texId, "decoded texture output", "partial_model_linked");
        }

        return new AssetTopology(
            nodes.ToArray(),
            edges.ToArray(),
            BuildPhyreInspection(manifest),
            BuildMgrpInspection(manifest),
            new TextureInventory(textureRoot, Directory.Exists(textureRoot), extractedTextures.Length, extractedTextures.Take(16).ToArray()));
    }

    private static PhyreInspection BuildPhyreInspection(MonsterMotionManifest manifest)
    {
        var markers = manifest.Ps3.Model.Exists
            ? new[]
            {
                new PhyreMarkerCount("PAnimationSet", CountAsciiOccurrences(manifest.Ps3.Model.Path, "PAnimationSet")),
                new PhyreMarkerCount("PAnimationClip", CountAsciiOccurrences(manifest.Ps3.Model.Path, "PAnimationClip")),
                new PhyreMarkerCount("PAnimationClipBinding", CountAsciiOccurrences(manifest.Ps3.Model.Path, "PAnimationClipBinding")),
                new PhyreMarkerCount("PAnimationChannelTarget", CountAsciiOccurrences(manifest.Ps3.Model.Path, "PAnimationChannelTarget")),
                new PhyreMarkerCount("SkeletonNode_", CountAsciiOccurrences(manifest.Ps3.Model.Path, "SkeletonNode_")),
                new PhyreMarkerCount("PNode", CountAsciiOccurrences(manifest.Ps3.Model.Path, "PNode")),
            }
            : Array.Empty<PhyreMarkerCount>();

        return new PhyreInspection(
            manifest.Ps3.Model.Path,
            manifest.Ps3.Model.Exists,
            manifest.Ps3.Model.Length,
            manifest.Ps3.AhWin32.Path,
            ExtractAhReferences(manifest.Ps3.AhWin32.Path),
            markers,
            manifest.Ps3.HasEmbeddedAnimationClip ? "partial_embedded_clip_visible" : "blocked_converter");
    }

    private static MgrpInspection BuildMgrpInspection(MonsterMotionManifest manifest)
    {
        var residents = manifest.Ps2.Motion.Residents.Select(resident => new MgrpResidentSummary(
            resident.Slot,
            resident.Path,
            resident.Exists,
            resident.Length,
            resident.IsEmptyStub,
            resident.IsNonEmpty,
            resident.RecordCount,
            resident.PayloadEnd,
            resident.SizeLawOk,
            resident.Records.Select(record => new MgrpRecordSummary(
                record.Index,
                record.SubId,
                record.SubIdMonByte,
                record.ChannelCount,
                record.GroupCount,
                record.OffA,
                record.OffB,
                record.FirstOffX,
                record.GroupToFirstOffXLawOk,
                record.ChannelRegionLawOk,
                record.AllChannelTagsAre000A,
                record.GroupPointersAbsoluteAndInRange)).ToArray())).ToArray();

        return new MgrpInspection(
            manifest.Ps2.Motion.NonEmptyResidentCount,
            residents.Sum(item => item.Records.Length),
            "blocked_mgrp_playback",
            residents);
    }

    private static void AddVirtualNode(List<FileGraphNode> nodes, Dictionary<string, string> seen, string id, string label, string role, string band, string note)
    {
        seen[id] = id;
        nodes.Add(new FileGraphNode(id, label, role, null, true, null, band, note));
    }

    private static string AddFileNode(List<FileGraphNode> nodes, Dictionary<string, string> seen, string hint, string path, string role, string band)
    {
        if (seen.TryGetValue(path, out var existing))
        {
            return existing;
        }

        var id = SanitizeId(hint);
        var suffix = 1;
        while (nodes.Any(item => item.Id == id))
        {
            id = $"{SanitizeId(hint)}_{suffix++}";
        }

        var exists = File.Exists(path);
        var length = exists ? new System.IO.FileInfo(path).Length : null as long?;
        var label = string.IsNullOrWhiteSpace(path) ? hint : Path.GetFileName(path);
        nodes.Add(new FileGraphNode(id, label, role, path, exists, length, band, null));
        seen[path] = id;
        return id;
    }

    private static void AddEdge(List<FileGraphEdge> edges, string from, string to, string label, string band)
    {
        if (!string.IsNullOrWhiteSpace(from) && !string.IsNullOrWhiteSpace(to))
        {
            edges.Add(new FileGraphEdge(from, to, label, band));
        }
    }

    private static string[] EnumerateSmallFileSet(string root, string pattern = "*")
    {
        if (!Directory.Exists(root))
        {
            return Array.Empty<string>();
        }

        return Directory.GetFiles(root, pattern, SearchOption.AllDirectories)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .Take(80)
            .ToArray();
    }

    private static string ClassifyPs3Role(string path)
    {
        var lower = path.ToLowerInvariant();
        if (lower.EndsWith(".ags.phyre", StringComparison.Ordinal)) return "ps3_ags_texture_animation";
        if (lower.Contains("shader", StringComparison.Ordinal)) return "ps3_shader";
        if (lower.EndsWith(".cdf", StringComparison.Ordinal)) return "ps3_cdf_bounds_definition";
        if (lower.EndsWith(".dds.phyre", StringComparison.Ordinal)) return "ps3_dds_phyre_texture_carrier";
        if (lower.EndsWith(".dae.phyre", StringComparison.Ordinal)) return "ps3_dae_phyre_geometry_carrier";
        if (lower.EndsWith(".ahwin32", StringComparison.Ordinal)) return "ps3_ah_manifest";
        return "ps3_other_sidecar";
    }

    private static int CountAsciiOccurrences(string path, string needle)
    {
        if (!File.Exists(path))
        {
            return 0;
        }

        var text = Encoding.ASCII.GetString(File.ReadAllBytes(path));
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static string[] ExtractAhReferences(string path)
    {
        if (!File.Exists(path))
        {
            return Array.Empty<string>();
        }

        var bytes = File.ReadAllBytes(path);
        var strings = new List<string>();
        var current = new StringBuilder();
        foreach (var value in bytes)
        {
            if (value is >= 32 and <= 126)
            {
                current.Append((char)value);
                continue;
            }

            FlushAsciiString(strings, current);
        }

        FlushAsciiString(strings, current);
        return strings
            .Where(item => item.Contains("phyre", StringComparison.OrdinalIgnoreCase) || item.Contains("d3d11", StringComparison.OrdinalIgnoreCase) || item.Contains("mdl/", StringComparison.OrdinalIgnoreCase) || item.Contains("tex/", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(40)
            .ToArray();
    }

    private static void FlushAsciiString(List<string> strings, StringBuilder current)
    {
        if (current.Length >= 4)
        {
            strings.Add(current.ToString());
        }

        current.Clear();
    }

    private static string SanitizeId(string value)
    {
        var builder = new StringBuilder();
        foreach (var ch in value)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }

        return builder.Length == 0 ? "node" : builder.ToString();
    }

    private static IEnumerable<BindingCandidateRow> BuildBindingRows(MonsterMotionManifest manifest, ChrBindPoseSummary bindPose, string gltfPath)
    {
        foreach (var node in bindPose.Nodes.Take(256))
        {
            yield return new BindingCandidateRow(
                manifest.MonsterId,
                node.TableIndex,
                node.NodeIndex,
                $"SkeletonNode_{node.NodeIndex:D3}",
                $"{manifest.MonsterId}_chr_node_{node.TableIndex:D3}",
                gltfPath,
                "structural_index_only",
                "partial_model_linked");
        }
    }

    private static string[] BuildTags(MonsterMotionManifest manifest, string labStatus, bool exported)
    {
        var tags = new SortedSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            labStatus,
            InferDecisionBand(manifest, labStatus, exported),
        };

        if (exported)
        {
            tags.Add("proved_structure_openable");
        }

        if (manifest.Ps3.EmbeddedAnimationClipCount > 0)
        {
            tags.Add("ps3_clip_control_positive");
        }

        if (manifest.Ps3.Model.Exists)
        {
            tags.Add("blocked_converter");
            tags.Add("no_real_mesh_yet");
        }

        if (manifest.Ps2.Motion.NonEmptyResidentCount > 0)
        {
            tags.Add("ps2_codec_blocked_or_phase2_candidate");
            tags.Add("blocked_mgrp_playback");
        }

        if (labStatus == "unsafe_lab_candidate_motion")
        {
            tags.Add("unsafe_lab_candidate_motion");
            tags.Add("unsafe_motion_overlay");
        }

        return tags.ToArray();
    }

    private static string InferDecisionBand(MonsterMotionManifest manifest, string labStatus, bool exported)
    {
        if (labStatus == "unsafe_lab_candidate_motion")
        {
            return "unsafe_motion_overlay";
        }

        if (manifest.Ps3.EmbeddedAnimationClipCount > 0)
        {
            return "partial_embedded_clip_visible";
        }

        if (exported)
        {
            return "proved_structure_openable";
        }

        if (manifest.Ps2.Motion.NonEmptyResidentCount > 0)
        {
            return "blocked_mgrp_playback";
        }

        return "partial_model_linked";
    }

    private static string InferMotionStatus(MonsterMotionManifest manifest, string labStatus)
    {
        if (labStatus == "unsafe_lab_candidate_motion")
        {
            return "unsafe_motion_overlay";
        }

        if (manifest.Ps3.EmbeddedAnimationClipCount > 0)
        {
            return "partial_embedded_clip_visible";
        }

        if (manifest.Ps2.Motion.NonEmptyResidentCount > 0)
        {
            return "blocked_mgrp_playback";
        }

        return "partial_model_linked";
    }

    private static string InferLabStatus(MonsterMotionManifest manifest)
    {
        if (manifest.Ps3.EmbeddedAnimationClipCount > 0)
        {
            return "ps3_clip_control_positive";
        }

        if (manifest.Ps2.Motion.NonEmptyResidentCount > 0)
        {
            return "ps2_codec_blocked_or_phase2_candidate";
        }

        return "static_or_no_resident_motion";
    }

    private static Dictionary<string, string> ReadLabStatuses()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var statusPaths = new[]
        {
            Path.Combine("work", "mgrp_unsafe_lab_package", "modelviewer-readonly-status.csv"),
            Path.Combine("work", "mgrp_pose_lab_v2", "modelviewer-readonly-status.csv"),
        };

        foreach (var path in statusPaths.Where(File.Exists))
        {
            foreach (var line in File.ReadLines(path).Skip(1))
            {
                var columns = line.Split(',');
                if (columns.Length < 5)
                {
                    continue;
                }

                result[NormalizeMonsterId(columns[0])] = columns[4];
            }
        }

        return result;
    }

    private static MonsterMotionManifest ReadManifest(string path)
    {
        return JsonSerializer.Deserialize<MonsterMotionManifest>(File.ReadAllText(path), ManifestJsonOptions)
            ?? throw new InvalidOperationException($"Unable to read manifest: {path}");
    }

    private static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, JsonSerializer.Serialize(value, OutputJsonOptions) + Environment.NewLine, ProgramOutput.Utf8NoBom);
    }

    private static void WriteCatalogCsv(string path, IReadOnlyList<CatalogEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("monsterId,label,assetStatus,motionStatus,decisionBand,labStatus,openableAssetPath,nonEmptyResidentCount,embeddedAnimationClipCount,nodeCount,structureLayout,tags");
        foreach (var entry in entries)
        {
            AppendCsvRow(
                builder,
                entry.MonsterId,
                entry.Label,
                entry.AssetStatus,
                entry.MotionStatus,
                entry.DecisionBand,
                entry.LabStatus,
                entry.OpenableAssetPath ?? string.Empty,
                entry.NonEmptyResidentCount.ToString(CultureInfo.InvariantCulture),
                entry.EmbeddedAnimationClipCount.ToString(CultureInfo.InvariantCulture),
                entry.NodeCount.ToString(CultureInfo.InvariantCulture),
                entry.StructureLayout,
                string.Join('|', entry.Tags));
        }

        File.WriteAllText(path, builder.ToString(), ProgramOutput.Utf8NoBom);
    }

    private static void WriteCoverageCsv(string path, IReadOnlyList<CatalogEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("monsterId,hasPs2Chr,ps2ResidentFileCount,ps2NonEmptyResidentCount,hasPs3AhWin32,hasDaePhyre,hasDdsPhyre,hasAgsPhyre,hasCdf,extractedPngCount,meshStatus,textureStatus,embeddedAnimationStatus,ps2MgrpStatus,validationStatus,decisionBand");
        foreach (var entry in entries)
        {
            var hasChr = HasExistingRole(entry, "ps2_chr_bind_pose");
            var hasAh = HasExistingRole(entry, "ps3_ah_manifest");
            var hasDae = HasExistingRole(entry, "ps3_dae_phyre_geometry_carrier");
            var hasDds = HasExistingRole(entry, "ps3_dds_phyre_texture_carrier");
            var hasAgs = HasExistingRole(entry, "ps3_ags_texture_animation");
            var hasCdf = HasExistingRole(entry, "ps3_cdf_bounds_definition");
            var ps2ResidentFileCount = entry.Topology.Mgrp.Residents.Count(item => item.Exists);
            var extractedPngCount = entry.Topology.Textures.ExtractedPngCount;

            AppendCsvRow(
                builder,
                entry.MonsterId,
                hasChr.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
                ps2ResidentFileCount.ToString(CultureInfo.InvariantCulture),
                entry.NonEmptyResidentCount.ToString(CultureInfo.InvariantCulture),
                hasAh.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
                hasDae.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
                hasDds.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
                hasAgs.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
                hasCdf.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
                extractedPngCount.ToString(CultureInfo.InvariantCulture),
                hasDae ? "present_unparsed_blocked_converter" : "missing",
                extractedPngCount > 0 ? "decoded_png_present" : hasDds ? "present_unparsed" : "missing",
                entry.EmbeddedAnimationClipCount > 0 ? "present_unexported_control_lane" : "missing",
                entry.NonEmptyResidentCount > 0 ? "present_codec_blocked" : "missing_or_empty_stub",
                entry.OpenableAssetPath is not null ? "local_structure_proxy_openable" : "not_validated",
                entry.DecisionBand);
        }

        File.WriteAllText(path, builder.ToString(), ProgramOutput.Utf8NoBom);
    }

    private static bool HasExistingRole(CatalogEntry entry, string role) =>
        entry.Topology.Nodes.Any(item => item.Exists && string.Equals(item.Role, role, StringComparison.OrdinalIgnoreCase));

    private static void WriteBindingCsv(string path, IEnumerable<BindingCandidateRow> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("monsterId,chrNodeIndex,chrBoneId,ps3NodeName,gltfNodeName,gltfPath,confidence,decisionBand");
        foreach (var row in rows)
        {
            AppendCsvRow(
                builder,
                row.MonsterId,
                row.ChrNodeIndex.ToString(CultureInfo.InvariantCulture),
                row.ChrBoneId.ToString(CultureInfo.InvariantCulture),
                row.Ps3NodeName,
                row.GltfNodeName,
                row.GltfPath,
                row.Confidence,
                row.DecisionBand);
        }

        File.WriteAllText(path, builder.ToString(), ProgramOutput.Utf8NoBom);
    }

    private static void AppendCsvRow(StringBuilder builder, params string[] values)
    {
        builder.AppendLine(string.Join(',', values.Select(CsvEscape)));
    }

    private static string CsvEscape(string value)
    {
        if (!value.Contains('"') && !value.Contains(',') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static string NormalizeMonsterId(string value)
    {
        var trimmed = value.Trim().ToLowerInvariant();
        if (trimmed.StartsWith('m') && trimmed.Length == 4)
        {
            return trimmed;
        }

        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        if (int.TryParse(digits, out var number))
        {
            return $"m{number:D3}";
        }

        throw new InvalidOperationException($"Invalid monster id: {value}");
    }

    private static string[] ResolveCatalogMonsterIds(string manifestRoot, string rawMonsters)
    {
        var trimmed = rawMonsters.Trim();
        if (string.Equals(trimmed, "all", StringComparison.OrdinalIgnoreCase) || trimmed == "*")
        {
            var indexPath = Path.Combine(manifestRoot, "index.motion-link.json");
            if (File.Exists(indexPath))
            {
                var index = JsonSerializer.Deserialize<MotionLinkerIndex>(File.ReadAllText(indexPath), ManifestJsonOptions)
                    ?? throw new InvalidOperationException($"Unable to read manifest index: {indexPath}");
                return index.Entries
                    .Select(item => NormalizeMonsterId(item.MonsterId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            if (Directory.Exists(manifestRoot))
            {
                var ids = Directory.GetFiles(manifestRoot, "m*.motion-link.json")
                    .Select(path => Path.GetFileName(path).Split('.', 2)[0])
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(NormalizeMonsterId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (ids.Length > 0)
                {
                    return ids;
                }
            }

            throw new InvalidOperationException($"No monster manifests found under {manifestRoot}.");
        }

        return trimmed
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeMonsterId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool ShouldExportStructure(string mode, string monsterId, bool chrExists)
    {
        if (!chrExists)
        {
            return false;
        }

        return mode switch
        {
            "all" or "true" => true,
            "none" or "false" => false,
            "pilots" or "pilot" or "default" => DefaultExportIds.Contains(monsterId),
            _ => throw new InvalidOperationException($"Unknown --export-structure mode: {mode}. Use pilots, all, or none."),
        };
    }

    private static bool IsEnabled(IReadOnlyDictionary<string, string> options, string key) =>
        options.TryGetValue(key, out var value)
        && (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase));

    private static StructureExtras MakePortable(StructureExtras extras) =>
        extras with
        {
            Ps2ChrPath = ToPortablePath(extras.Ps2ChrPath),
            Ps2ResidentMotionPaths = extras.Ps2ResidentMotionPaths.Select(ToPortablePath).ToArray(),
            Ps3ModelPath = ToPortablePath(extras.Ps3ModelPath),
        };

    private static CatalogEntry MakePortable(CatalogEntry entry) =>
        entry with
        {
            SourcePaths = entry.SourcePaths with
            {
                Ps2Chr = ToPortablePath(entry.SourcePaths.Ps2Chr),
                Ps2Residents = entry.SourcePaths.Ps2Residents.Select(ToPortablePath).ToArray(),
                Ps3Model = ToPortablePath(entry.SourcePaths.Ps3Model),
                Ps3Texture = ToPortablePath(entry.SourcePaths.Ps3Texture),
                Manifest = ToPortablePath(entry.SourcePaths.Manifest),
            },
            Topology = MakePortable(entry.Topology),
        };

    private static AssetTopology MakePortable(AssetTopology topology) =>
        topology with
        {
            Nodes = topology.Nodes.Select(node => node with { Path = node.Path is null ? null : ToPortablePath(node.Path) }).ToArray(),
            Phyre = topology.Phyre with
            {
                DaePath = ToPortablePath(topology.Phyre.DaePath),
                AhWin32Path = ToPortablePath(topology.Phyre.AhWin32Path),
                AhReferences = topology.Phyre.AhReferences.Select(ToPortablePath).ToArray(),
            },
            Mgrp = topology.Mgrp with
            {
                Residents = topology.Mgrp.Residents.Select(resident => resident with { Path = ToPortablePath(resident.Path) }).ToArray(),
            },
            Textures = topology.Textures with
            {
                TexturePngRoot = ToPortablePath(topology.Textures.TexturePngRoot),
                ExtractedPngPaths = topology.Textures.ExtractedPngPaths.Select(ToPortablePath).ToArray(),
            },
        };

    private static string ToPortablePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var normalized = path.Replace('\\', '/');
        var markers = new[]
        {
            "ffx_data/gamedata/ps3data/",
            "ffx_ps2/ffx/master/jppc/",
            "ps3data_textures_png/",
            "chr/mon/",
            "work/",
        };

        foreach (var marker in markers)
        {
            var index = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                return normalized[index..];
            }
        }

        return Path.IsPathFullyQualified(path) ? Path.GetFileName(path) : normalized;
    }

    private static IReadOnlyDictionary<string, string> ParseOptions(string[] args)
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
            var value = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++index]
                : "true";
            options[key] = value;
        }

        return options;
    }

    private static string GetOption(IReadOnlyDictionary<string, string> options, string key, string defaultValue) =>
        options.TryGetValue(key, out var value) ? value : defaultValue;

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        Console.Error.WriteLine("Usage: dotnet run --project RuntimeTools/ModelViewerAssetLab/ModelViewerAssetLab.csproj -- export --manifest-root work/motion_linker_lab_all --output work/modelviewer_assets_v0 [--monsters all|m018,m211] [--export-structure pilots|all|none] [--portable]");
        return 1;
    }
}

public static class StructureGltfBuilder
{
    public static StructureAsset Build(MonsterMotionManifest manifest, ChrBindPoseSummary bindPose, string labStatus)
    {
        var points = NormalizePoints(bindPose, out var layoutMode);
        var primitives = new List<GltfLinePrimitive>
        {
            new("axis_x", new[] { new Vec3(-4, 0, 0), new Vec3(4, 0, 0) }, new[] { 0.9, 0.16, 0.12, 1.0 }),
            new("axis_y", new[] { new Vec3(0, -4, 0), new Vec3(0, 4, 0) }, new[] { 0.2, 0.65, 0.26, 1.0 }),
            new("axis_z", new[] { new Vec3(0, 0, -4), new Vec3(0, 0, 4) }, new[] { 0.22, 0.37, 0.9, 1.0 }),
            new("chr_node_chain", BuildNodeChain(points), new[] { 0.0, 0.72, 0.82, 1.0 }),
            new("chr_node_markers", BuildMarkers(points, 0.08), new[] { 1.0, 0.86, 0.36, 1.0 }),
            new("mgrp_metadata_graph", BuildMotionGraph(manifest), new[] { 1.0, 0.45, 0.16, 1.0 }),
        };

        primitives = primitives.Where(item => item.Positions.Length >= 2).ToList();

        var decisionBand = labStatus == "unsafe_lab_candidate_motion"
            ? "unsafe_motion_overlay"
            : "proved_structure_openable";

        return new StructureAsset(
            manifest.MonsterId,
            $"{manifest.MonsterId} .chr/.mgrp structure proxy",
            decisionBand,
            layoutMode,
            points.Length,
            primitives.Count,
            primitives.ToArray(),
            new StructureExtras(
                manifest.MonsterId,
                manifest.Ps2.Chr.Path,
                manifest.Ps2.Motion.Residents.Where(item => item.Exists).Select(item => item.Path).ToArray(),
                manifest.Ps3.Model.Path,
                manifest.Linkage.MotionReadiness,
                labStatus,
                manifest.Ps2.Motion.NonEmptyResidentCount,
                manifest.Ps3.EmbeddedAnimationClipCount,
                "blocked_mgrp_playback",
                "No PS2 .mgrp decoder/remap/timing is applied. This is a structural proxy for viewing and routing."));
    }

    private static Vec3[] NormalizePoints(ChrBindPoseSummary bindPose, out string layoutMode)
    {
        var raw = bindPose.Nodes
            .Where(item => double.IsFinite(item.X) && double.IsFinite(item.Y) && double.IsFinite(item.Z))
            .Take(256)
            .Select(item => new Vec3(item.X, item.Y, item.Z))
            .ToArray();

        if (raw.Length >= 2)
        {
            var min = Vec3.Min(raw);
            var max = Vec3.Max(raw);
            var span = max - min;
            var maxSpan = Math.Max(span.X, Math.Max(span.Y, span.Z));
            if (maxSpan > 0.000001)
            {
                var center = (min + max) * 0.5;
                var scale = 5.0 / maxSpan;
                layoutMode = "chr_bind_pose_normalized";
                return raw.Select(item => (item - center) * scale).ToArray();
            }
        }

        var count = Math.Max(2, Math.Min(128, (int)(bindPose.NodeCount ?? 16)));
        layoutMode = "chr_node_index_spiral_fallback";
        return Enumerable.Range(0, count)
            .Select(index =>
            {
                var angle = index * Math.PI * 0.45;
                var radius = 0.35 + index * 0.025;
                return new Vec3(Math.Cos(angle) * radius, (index - count / 2.0) * 0.075, Math.Sin(angle) * radius);
            })
            .ToArray();
    }

    private static Vec3[] BuildNodeChain(IReadOnlyList<Vec3> points)
    {
        var vertices = new List<Vec3>();
        for (var index = 1; index < points.Count; index++)
        {
            vertices.Add(points[index - 1]);
            vertices.Add(points[index]);
        }

        return vertices.ToArray();
    }

    private static Vec3[] BuildMarkers(IReadOnlyList<Vec3> points, double size)
    {
        var vertices = new List<Vec3>();
        foreach (var point in points)
        {
            vertices.Add(point + new Vec3(-size, 0, 0));
            vertices.Add(point + new Vec3(size, 0, 0));
            vertices.Add(point + new Vec3(0, -size, 0));
            vertices.Add(point + new Vec3(0, size, 0));
            vertices.Add(point + new Vec3(0, 0, -size));
            vertices.Add(point + new Vec3(0, 0, size));
        }

        return vertices.ToArray();
    }

    private static Vec3[] BuildMotionGraph(MonsterMotionManifest manifest)
    {
        var vertices = new List<Vec3>();
        var records = manifest.Ps2.Motion.Residents
            .Where(item => item.IsNonEmpty)
            .SelectMany(item => item.Records.Select(record => (item.Slot, Record: record)))
            .Take(4)
            .ToArray();

        var row = 0;
        foreach (var (slot, record) in records)
        {
            var y = -2.8 - row * 0.55;
            var channels = Math.Max(1, (int)record.ChannelCount);
            var groups = Math.Max(1, (int)record.GroupCount);
            var width = Math.Min(5.5, Math.Max(1.5, channels * 0.2));
            var startX = -width * 0.5;
            var groupStep = width / groups;
            var channelStep = width / channels;

            for (var group = 0; group < groups; group++)
            {
                var x = startX + group * groupStep;
                vertices.Add(new Vec3(x, y, -0.5 - slot * 0.2));
                vertices.Add(new Vec3(x + groupStep * 0.75, y, -0.5 - slot * 0.2));
            }

            for (var channel = 0; channel < Math.Min(channels, 48); channel++)
            {
                var x = startX + channel * channelStep;
                vertices.Add(new Vec3(x, y - 0.12, 0.35 + slot * 0.15));
                vertices.Add(new Vec3(x, y + 0.12, 0.35 + slot * 0.15));
            }

            row++;
        }

        return vertices.ToArray();
    }
}

public static class StructureGltfWriter
{
    public static void Write(string path, StructureAsset asset)
    {
        var buffer = new List<byte>();
        var bufferViews = new List<object>();
        var accessors = new List<object>();
        var meshes = new List<object>();
        var materials = new List<object>();
        var nodes = new List<object>();
        var childNodeIndexes = new List<int>();

        for (var index = 0; index < asset.Primitives.Length; index++)
        {
            var primitive = asset.Primitives[index];
            Align(buffer, 4);
            var byteOffset = buffer.Count;
            foreach (var position in primitive.Positions)
            {
                AppendFloat(buffer, (float)position.X);
                AppendFloat(buffer, (float)position.Y);
                AppendFloat(buffer, (float)position.Z);
            }

            var byteLength = buffer.Count - byteOffset;
            bufferViews.Add(new { buffer = 0, byteOffset, byteLength, target = 34962 });

            var min = Vec3.Min(primitive.Positions);
            var max = Vec3.Max(primitive.Positions);
            accessors.Add(new
            {
                bufferView = index,
                byteOffset = 0,
                componentType = 5126,
                count = primitive.Positions.Length,
                type = "VEC3",
                min = new[] { min.X, min.Y, min.Z },
                max = new[] { max.X, max.Y, max.Z },
            });

            materials.Add(new
            {
                name = primitive.Name,
                pbrMetallicRoughness = new
                {
                    baseColorFactor = primitive.Color,
                    metallicFactor = 0,
                    roughnessFactor = 0.75,
                },
                doubleSided = true,
                extensions = new Dictionary<string, object>
                {
                    ["KHR_materials_unlit"] = new { },
                },
            });

            meshes.Add(new
            {
                name = primitive.Name,
                primitives = new[]
                {
                    new
                    {
                        attributes = new Dictionary<string, int> { ["POSITION"] = index },
                        mode = 1,
                        material = index,
                    },
                },
            });

            nodes.Add(new { name = primitive.Name, mesh = index });
            childNodeIndexes.Add(nodes.Count);
        }

        var rootNode = new
        {
            name = asset.Name,
            children = childNodeIndexes.ToArray(),
            extras = asset.Extras,
        };
        nodes.Insert(0, rootNode);

        var gltf = new
        {
            asset = new
            {
                version = "2.0",
                generator = "RuntimeTools/ModelViewerAssetLab",
                copyright = "Derived structural metadata only. Game assets are not embedded.",
            },
            extensionsUsed = new[] { "KHR_materials_unlit" },
            scene = 0,
            scenes = new[] { new { name = "FFX structure proxy", nodes = new[] { 0 } } },
            nodes = nodes.ToArray(),
            meshes = meshes.ToArray(),
            materials = materials.ToArray(),
            buffers = new[]
            {
                new
                {
                    uri = "data:application/octet-stream;base64," + Convert.ToBase64String(buffer.ToArray()),
                    byteLength = buffer.Count,
                },
            },
            bufferViews = bufferViews.ToArray(),
            accessors = accessors.ToArray(),
            extras = asset.Extras,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, JsonSerializer.Serialize(gltf, ProgramOutput.JsonOptions) + Environment.NewLine, ProgramOutput.Utf8NoBom);
    }

    private static void AppendFloat(List<byte> buffer, float value)
    {
        var bytes = BitConverter.GetBytes(value);
        buffer.AddRange(bytes);
    }

    private static void Align(List<byte> buffer, int alignment)
    {
        while (buffer.Count % alignment != 0)
        {
            buffer.Add(0);
        }
    }
}

public static class ProgramOutput
{
    public static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public readonly record struct Vec3(double X, double Y, double Z)
{
    public static Vec3 operator +(Vec3 left, Vec3 right) => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    public static Vec3 operator -(Vec3 left, Vec3 right) => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
    public static Vec3 operator *(Vec3 value, double scale) => new(value.X * scale, value.Y * scale, value.Z * scale);

    public static Vec3 Min(IReadOnlyList<Vec3> values) => new(values.Min(item => item.X), values.Min(item => item.Y), values.Min(item => item.Z));
    public static Vec3 Max(IReadOnlyList<Vec3> values) => new(values.Max(item => item.X), values.Max(item => item.Y), values.Max(item => item.Z));
}

public sealed record GltfLinePrimitive(string Name, Vec3[] Positions, double[] Color);

public sealed record StructureAsset(
    string MonsterId,
    string Name,
    string DecisionBand,
    string LayoutMode,
    int SourceNodeCount,
    int PrimitiveCount,
    GltfLinePrimitive[] Primitives,
    StructureExtras Extras);

public sealed record StructureExtras(
    string MonsterId,
    string Ps2ChrPath,
    string[] Ps2ResidentMotionPaths,
    string Ps3ModelPath,
    string MotionReadiness,
    string LabStatus,
    int NonEmptyResidentCount,
    int EmbeddedAnimationClipCount,
    string PlaybackStatus,
    string Note);

public sealed record ModelViewerCatalog(
    DateTimeOffset GeneratedAtUtc,
    string SchemaVersion,
    string Generator,
    string DefaultAssetId,
    CatalogSummary Summary,
    CatalogEntry[] Entries);

public sealed record CatalogSummary(
    int EntryCount,
    int OpenableAssetCount,
    int ProvedStructureOpenableCount,
    int PartialEmbeddedClipVisibleCount,
    int UnsafeMotionOverlayCount);

public sealed record CatalogEntry(
    string MonsterId,
    string Label,
    CatalogSourcePaths SourcePaths,
    string AssetStatus,
    string MotionStatus,
    string? OpenableAssetPath,
    string[] EvidenceDocs,
    string DecisionBand,
    string LabStatus,
    string IdentityStatus,
    string MotionReadiness,
    int NonEmptyResidentCount,
    int EmbeddedAnimationClipCount,
    int SkeletonNodeMarkerCount,
    int NodeCount,
    string StructureLayout,
    string[] Tags,
    AssetTopology Topology)
{
    public static CatalogEntry BlockedMissingManifest(string monsterId, string manifestPath) =>
        new(
            monsterId,
            $"{monsterId} missing manifest",
            new CatalogSourcePaths(string.Empty, Array.Empty<string>(), string.Empty, string.Empty, manifestPath),
            "blocked_env_missing_path",
            "blocked_env_missing_path",
            null,
            Array.Empty<string>(),
            "blocked_env_missing_path",
            "missing_manifest",
            "missing_manifest",
            "missing_manifest",
            0,
            0,
            0,
            0,
            "not_exported",
            new[] { "blocked_env_missing_path" },
            AssetTopology.Empty);
}

public sealed record AssetTopology(
    FileGraphNode[] Nodes,
    FileGraphEdge[] Edges,
    PhyreInspection Phyre,
    MgrpInspection Mgrp,
    TextureInventory Textures)
{
    public static AssetTopology Empty { get; } = new(
        Array.Empty<FileGraphNode>(),
        Array.Empty<FileGraphEdge>(),
        PhyreInspection.Empty,
        MgrpInspection.Empty,
        TextureInventory.Empty);
}

public sealed record FileGraphNode(
    string Id,
    string Label,
    string Role,
    string? Path,
    bool Exists,
    long? Length,
    string DecisionBand,
    string? Note);

public sealed record FileGraphEdge(
    string From,
    string To,
    string Label,
    string DecisionBand);

public sealed record PhyreInspection(
    string DaePath,
    bool DaeExists,
    long? DaeLength,
    string AhWin32Path,
    string[] AhReferences,
    PhyreMarkerCount[] MarkerCounts,
    string DecisionBand)
{
    public static PhyreInspection Empty { get; } = new(string.Empty, false, null, string.Empty, Array.Empty<string>(), Array.Empty<PhyreMarkerCount>(), "blocked_env_missing_path");
}

public sealed record PhyreMarkerCount(string Marker, int Count);

public sealed record MgrpInspection(
    int NonEmptyResidentCount,
    int RecordCount,
    string PlaybackStatus,
    MgrpResidentSummary[] Residents)
{
    public static MgrpInspection Empty { get; } = new(0, 0, "blocked_env_missing_path", Array.Empty<MgrpResidentSummary>());
}

public sealed record MgrpResidentSummary(
    int Slot,
    string Path,
    bool Exists,
    long? Length,
    bool IsEmptyStub,
    bool IsNonEmpty,
    uint? RecordCount,
    uint? PayloadEnd,
    bool? SizeLawOk,
    MgrpRecordSummary[] Records);

public sealed record MgrpRecordSummary(
    int Index,
    uint SubId,
    int? SubIdMonByte,
    ushort ChannelCount,
    ushort GroupCount,
    uint OffA,
    uint OffB,
    uint? FirstOffX,
    bool GroupToFirstOffXLawOk,
    bool ChannelRegionLawOk,
    bool AllChannelTagsAre000A,
    bool GroupPointersAbsoluteAndInRange);

public sealed record TextureInventory(
    string TexturePngRoot,
    bool RootExists,
    int ExtractedPngCount,
    string[] ExtractedPngPaths)
{
    public static TextureInventory Empty { get; } = new(string.Empty, false, 0, Array.Empty<string>());
}

public sealed record CatalogSourcePaths(
    string Ps2Chr,
    string[] Ps2Residents,
    string Ps3Model,
    string Ps3Texture,
    string Manifest);

public sealed record ExportedAsset(
    string MonsterId,
    string OpenableAssetPath,
    string AssetStatus,
    string DecisionBand,
    int NodeCount,
    int PrimitiveCount,
    string LayoutMode);

public sealed record BatchExportReport(
    DateTimeOffset GeneratedAtUtc,
    string ManifestRoot,
    string OutputRoot,
    string MonsterSelection,
    string ExportStructureMode,
    bool Portable,
    int CatalogEntryCount,
    int ExportedAssetCount,
    ExportedAsset[] ExportedAssets,
    string MgrpPlaybackStatus,
    string Note);

public sealed record BindingCandidateRow(
    string MonsterId,
    int ChrNodeIndex,
    int ChrBoneId,
    string Ps3NodeName,
    string GltfNodeName,
    string GltfPath,
    string Confidence,
    string DecisionBand);

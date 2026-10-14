using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PhyreModelExportLab;

public static class Program
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly string[] MeshMarkers =
    {
        "PMesh",
        "PMeshSegment",
        "PMeshInstance",
        "PVertexStream",
        "PVertexFormat",
        "PIndexBuffer",
        "PMaterial",
        "PTexture2D",
        "PSkinBoneRemap",
        "PSkeletonJointBounds",
        "PNode",
        "SkeletonNode_",
        "PAnimationSet",
        "PAnimationClip",
        "PAnimationClipBinding",
        "PAnimationChannelTarget",
    };

    public static int Main(string[] args)
    {
        try
        {
            var command = args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal)
                ? "probe"
                : args[0].ToLowerInvariant();
            var options = ParseOptions(command == "probe" ? args.Skip(command == args.FirstOrDefault() ? 1 : 0).ToArray() : args.Skip(1).ToArray());

            return command switch
            {
                "probe" => RunProbe(options),
                "export-static" => RunExportStatic(options),
                "oracle-index" => RunOracleIndex(options),
                "descriptor-report" => RunDescriptorReport(options),
                "extract-texture" => RunExtractTexture(options),
                "extract-texture-file" => RunExtractTextureFile(options),
                "extract-texture-batch" => RunExtractTextureBatch(options),
                "export-descriptor-static" => RunExportDescriptorStatic(options),
                "glb" => RunGltfToGlb(options),
                _ => UnknownCommand(command),
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    /// <summary>F5 asset-loading: convert a self-contained glTF (single data-URI buffer) into a .glb container.
    /// Usage: glb --input &lt;file.gltf&gt; [--output &lt;file.glb&gt;] (default: same path with .glb).</summary>
    private static int RunGltfToGlb(IReadOnlyDictionary<string, string> options)
    {
        var input = GetOption(options, "input", string.Empty);
        if (string.IsNullOrWhiteSpace(input))
        {
            Console.Error.WriteLine("glb: --input <file.gltf> required");
            return 2;
        }
        string gltfPath = Path.GetFullPath(input);
        if (!File.Exists(gltfPath))
        {
            Console.Error.WriteLine($"glb: input not found: {gltfPath}");
            return 2;
        }
        var output = GetOption(options, "output", Path.ChangeExtension(gltfPath, ".glb"));
        string glbPath = Path.GetFullPath(output!);
        if (GltfContainerWriter.TryConvertToGlb(gltfPath, glbPath))
        {
            long gltfBytes = new FileInfo(gltfPath).Length;
            long glbBytes = new FileInfo(glbPath).Length;
            string delta = gltfBytes > 0
                ? (glbBytes < gltfBytes
                    ? $"{glbBytes - gltfBytes:N0} bytes menor ({(1 - glbBytes / (double)gltfBytes) * 100:0.#}%)"
                    : $"{glbBytes - gltfBytes:N0} bytes maior (overhead p/ arquivos pequenos; mapas reais ficam ~25% menores)")
                : "n/a";
            Console.WriteLine($"glb: ok {glbPath} ({gltfBytes:N0} → {glbBytes:N0} bytes, {delta})");
            return 0;
        }
        Console.Error.WriteLine("glb: conversion not possible (glTF must be single-buffer data-URI; no file written)");
        return 1;
    }

    private static int RunProbe(IReadOnlyDictionary<string, string> options)
    {
        var outputRoot = Path.GetFullPath(GetOption(options, "output", GetLocalOutputRoot("model-probe")));
        var ps3Root = GetRequiredRoot(options, "ps3-root", "FFX_PS3DATA_ROOT");
        var texturePngRoot = GetRequiredRoot(options, "texture-png-root", "FFX_PHYRE_TEXTURE_PNG_ROOT");
        var portable = IsEnabled(options, "portable");

        var monsterIds = GetOption(options, "monsters", GetOption(options, "monster", "m020,m018"))
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeMonsterId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Directory.CreateDirectory(outputRoot);

        var reports = new List<PhyreMeshProbeReport>();
        foreach (var monsterId in monsterIds)
        {
            var report = ProbeMonster(ps3Root, texturePngRoot, monsterId, portable);
            reports.Add(report);
            WriteJson(Path.Combine(outputRoot, $"{monsterId}.phyre-mesh-probe.json"), report);
        }

        var index = new PhyreMeshProbeIndex(
            DateTimeOffset.UtcNow,
            portable ? ToPortablePath(ps3Root) : ps3Root,
            portable ? ToPortablePath(texturePngRoot) : texturePngRoot,
            reports.Count,
            reports.Count(item => item.MeshDescriptorStatus == "mesh_descriptor_markers_present"),
            reports.Count(item => item.EmbeddedAnimationStatus == "embedded_animation_markers_present"),
            reports.ToArray());
        WriteJson(Path.Combine(outputRoot, "phyre-model-probe-index.json"), index);

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            outputRoot,
            indexPath = Path.Combine(outputRoot, "phyre-model-probe-index.json"),
            reports = reports.Select(item => new
            {
                item.MonsterId,
                item.MeshDescriptorStatus,
                item.EmbeddedAnimationStatus,
                item.DecisionBand,
                item.NextStrike,
            }),
        }, JsonOptions));

        return 0;
    }

    private static int RunExportDescriptorStatic(IReadOnlyDictionary<string, string> options)
    {
        var outputRoot = Path.GetFullPath(GetOption(options, "output", GetLocalOutputRoot("descriptor-exports")));
        var ps3Root = GetRequiredRoot(options, "ps3-root", "FFX_PS3DATA_ROOT");
        var portable = IsEnabled(options, "portable");
        var withOfficialTexture = IsEnabled(options, "with-official-texture");
        var flipV = IsEnabled(options, "flip-v");
        var monsterIds = ResolveMonsterIds(ps3Root, GetOption(options, "monsters", GetOption(options, "monster", "m020")))
            .ToArray();

        Directory.CreateDirectory(outputRoot);

        var exports = new List<DescriptorStaticGltfExportReport>();
        foreach (var monsterId in monsterIds)
        {
            try
            {
                var daePath = Path.Combine(ps3Root, "chr", "mon", monsterId, "mdl", "d3d11", $"{monsterId}.dae.phyre");
                var descriptorReport = PhyreDescriptorParser.Parse(monsterId, daePath, portable);
                WriteJson(Path.Combine(outputRoot, $"{monsterId}.phyre-descriptor-report.json"), descriptorReport);
                if (descriptorReport.DecisionBand != "mesh_descriptor_decode_candidate")
                {
                    var blocked = DescriptorStaticGltfExportReport.Blocked(monsterId, descriptorReport.DecisionBand, "descriptor report did not pass mesh candidate gates");
                    exports.Add(blocked);
                    WriteJson(Path.Combine(outputRoot, $"{monsterId}.descriptor-static-export-report.json"), blocked);
                    continue;
                }

                var source = File.ReadAllBytes(daePath);
                var assetPath = Path.Combine(outputRoot, $"{monsterId}_descriptor_static.gltf");
                DescriptorGltfTexture? texture = null;
                if (withOfficialTexture)
                {
                    var textureReport = PhyreTextureExtractor.ExtractPrimaryTexture(ps3Root, monsterId, Path.Combine(outputRoot, "textures"), portable);
                    WriteJson(Path.Combine(outputRoot, $"{monsterId}.texture-extraction-report.json"), textureReport);
                    if (textureReport.DecisionBand == "texture_png_extracted_candidate" && textureReport.PngPath is not null && textureReport.Format is not null)
                    {
                        texture = new DescriptorGltfTexture(
                            Path.GetFullPath(textureReport.PngPath),
                            textureReport.Format,
                            textureReport.Width,
                            textureReport.Height,
                            flipV);
                    }
                }

                var exportReport = DescriptorStaticGltfWriter.Write(assetPath, monsterId, source, descriptorReport, texture);
                exports.Add(exportReport);
                WriteJson(Path.Combine(outputRoot, $"{monsterId}.descriptor-static-export-report.json"), exportReport);
            }
            catch (Exception exception)
            {
                var blocked = DescriptorStaticGltfExportReport.Blocked(monsterId, "blocked_descriptor_static_export_exception", exception.Message);
                exports.Add(blocked);
                WriteJson(Path.Combine(outputRoot, $"{monsterId}.descriptor-static-export-report.json"), blocked);
            }
        }

        var index = new DescriptorStaticGltfExportIndex(
            DateTimeOffset.UtcNow,
            portable ? ToPortablePath(ps3Root) : ps3Root,
            exports.Count,
            exports.Count(item => item.DecisionBand == "mesh_descriptor_static_gltf_candidate"),
            exports.ToArray());
        var indexPath = Path.Combine(outputRoot, "descriptor-static-export-index.json");
        WriteJson(indexPath, index);

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            outputRoot,
            indexPath,
            exports = exports.Select(item => new
            {
                item.MonsterId,
                item.DecisionBand,
                item.PrimitiveCount,
                item.VertexCount,
                item.IndexCount,
                item.TriangleCount,
                item.PositionBounds,
                item.AssetPath,
            }),
        }, JsonOptions));

        return 0;
    }

    private static int RunExtractTexture(IReadOnlyDictionary<string, string> options)
    {
        var outputRoot = Path.GetFullPath(GetOption(options, "output", GetLocalOutputRoot("texture-exports")));
        var ps3Root = GetRequiredRoot(options, "ps3-root", "FFX_PS3DATA_ROOT");
        var portable = IsEnabled(options, "portable");
        var monsterIds = ResolveMonsterIds(ps3Root, GetOption(options, "monsters", GetOption(options, "monster", "m020")))
            .ToArray();

        Directory.CreateDirectory(outputRoot);

        var reports = new List<PhyreTextureExtractionReport>();
        foreach (var monsterId in monsterIds)
        {
            var report = PhyreTextureExtractor.ExtractPrimaryTexture(ps3Root, monsterId, outputRoot, portable);
            reports.Add(report);
            WriteJson(Path.Combine(outputRoot, $"{monsterId}.texture-extraction-report.json"), report);
        }

        var indexPath = Path.Combine(outputRoot, "texture-extraction-index.json");
        WriteJson(indexPath, new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            ps3Root = portable ? ToPortablePath(ps3Root) : ps3Root,
            reportCount = reports.Count,
            candidateCount = reports.Count(report => report.DecisionBand == "texture_png_extracted_candidate"),
            reports,
        });

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            outputRoot,
            indexPath,
            reports = reports.Select(report => new
            {
                report.MonsterId,
                report.DecisionBand,
                report.Format,
                report.Width,
                report.Height,
                report.PngPath,
                report.NextStrike,
            }),
        }, JsonOptions));

        return 0;
    }

    // Generic single-file texture decode (any category: pc/npc/sum/obj/wep), not just chr/mon.
    // Reuses the proven RYHPT decoder. usage: extract-texture-file --in <dds.phyre> --out <png> [--id name]
    private static int RunExtractTextureFile(IReadOnlyDictionary<string, string> options)
    {
        var inPath = GetOption(options, "in", "");
        var outPath = GetOption(options, "out", "");
        if (string.IsNullOrWhiteSpace(inPath) || string.IsNullOrWhiteSpace(outPath))
        {
            Console.Error.WriteLine("usage: extract-texture-file --in <dds.phyre> --out <png> [--id name]");
            return 1;
        }
        inPath = Path.GetFullPath(inPath);
        outPath = Path.GetFullPath(outPath);
        var assetId = GetOption(options, "id", Path.GetFileNameWithoutExtension(outPath));
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

        var report = PhyreTextureExtractor.ExtractTextureFile(inPath, outPath, assetId, portable: false);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            report.AssetId,
            report.DecisionBand,
            report.Format,
            report.Width,
            report.Height,
            report.PngPath,
            report.NextStrike,
        }, JsonOptions));
        return report.DecisionBand.StartsWith("blocked", StringComparison.Ordinal) ? 2 : 0;
    }

    // Batch texture decode across whole chr categories (pc/npc/sum/obj/wep/mon/npc...).
    // Default: walks chr/<cat>/<id>/tex/d3d11/<id>.dds.phyre (filename heuristic).
    // --from-manifest: reads each model's <id>.ahwin32 g_fileNames[] (D3D11) = AUTHORITATIVE asset list, so
    //   it picks the exact body texture(s) the game loads — catches cross-dir refs (c906<-c106) AND secondary
    //   textures (c101+c101_01, n238+n238_hair) the filename heuristic misses; writes a provenance map.
    // usage: extract-texture-batch [--cats pc,npc,sum] [--output <folder>]
    //   [--ps3-root <root>] [--from-manifest] [--include-anim] [--overwrite] [--portable]
    private static int RunExtractTextureBatch(IReadOnlyDictionary<string, string> options)
    {
        var outputRoot = Path.GetFullPath(GetOption(options, "output", GetLocalOutputRoot("character-textures")));
        var ps3Root = GetRequiredRoot(options, "ps3-root", "FFX_PS3DATA_ROOT");
        var portable = IsEnabled(options, "portable");
        var includeAnim = IsEnabled(options, "include-anim");
        var overwrite = IsEnabled(options, "overwrite");
        var fromManifest = IsEnabled(options, "from-manifest");
        var cats = GetOption(options, "cats", "pc,npc,sum")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Directory.CreateDirectory(outputRoot);
        var reports = new List<PhyreTextureFileExtractionReport>();
        var perCat = new List<object>();
        var provenance = new List<object>();
        var provMultiTexture = 0;
        var provUntextured = 0;

        foreach (var cat in cats)
        {
            var catRoot = Path.Combine(ps3Root, "chr", cat);
            if (!Directory.Exists(catRoot))
            {
                perCat.Add(new { category = cat, status = "blocked_missing_category_dir", ids = 0, ok = 0, blocked = 0 });
                continue;
            }

            var ids = Directory.EnumerateDirectories(catRoot)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var catOut = Path.Combine(outputRoot, cat);
            var ok = 0;
            var blocked = 0;
            foreach (var id in ids)
            {
                var texDir = Path.Combine(catRoot, id, "tex", "d3d11");
                var sources = new List<(string src, string pngName)>();

                if (fromManifest)
                {
                    // AUTHORITATIVE: read the model's ahwin32 manifest and bind exactly the textures it loads.
                    var ahPath = Path.Combine(catRoot, id, $"{id}.ahwin32");
                    var texEntries = ParseAhwin32Textures(ahPath);
                    var bodies = texEntries.Where(t => !t.IsAnim).ToList();
                    var primaryIndex = bodies.FindIndex(t => string.Equals(t.File, id, StringComparison.OrdinalIgnoreCase));
                    if (primaryIndex < 0 && bodies.Count > 0)
                    {
                        primaryIndex = 0; // cross-ref donor (e.g. c906 -> c106) is the primary body
                    }

                    var bodyProv = new List<object>();
                    for (var bi = 0; bi < bodies.Count; bi++)
                    {
                        var t = bodies[bi];
                        var src = Path.Combine(ps3Root, "chr", t.Cat, t.SrcId, "tex", "d3d11", $"{t.File}.dds.phyre");
                        var pngName = bi == primaryIndex ? $"{id}.png" : $"{id}__{t.File}.png";
                        sources.Add((src, pngName));
                        bodyProv.Add(new { role = bi == primaryIndex ? "primary" : "secondary", srcId = t.SrcId, file = t.File, png = $"{cat}/{pngName}", crossRef = !string.Equals(t.SrcId, id, StringComparison.OrdinalIgnoreCase) });
                    }

                    if (includeAnim)
                    {
                        foreach (var t in texEntries.Where(t => t.IsAnim))
                        {
                            var src = Path.Combine(ps3Root, "chr", t.Cat, t.SrcId, "tex", "d3d11", $"{t.File}.dds.phyre");
                            sources.Add((src, $"{id}__{t.File}.png"));
                        }
                    }

                    if (bodies.Count > 1)
                    {
                        provMultiTexture++;
                    }

                    if (bodies.Count == 0)
                    {
                        provUntextured++;
                    }

                    provenance.Add(new
                    {
                        id,
                        cat,
                        ahwin32 = File.Exists(ahPath),
                        bodyCount = bodies.Count,
                        faceVariantCount = texEntries.Count(t => t.IsAnim),
                        primaryFile = primaryIndex >= 0 ? bodies[primaryIndex].File : null,
                        untextured = bodies.Count == 0,
                        bodyTextures = bodyProv,
                    });
                }
                else
                {
                    var primary = Path.Combine(texDir, $"{id}.dds.phyre");
                    if (File.Exists(primary))
                    {
                        sources.Add((primary, $"{id}.png"));
                    }
                    else if (Directory.Exists(texDir))
                    {
                        // Some ids ship a sibling's texture in their own tex dir (e.g. c806 carries c805.dds.phyre).
                        // Bind it under <id>.png so the model->texture lookup still resolves.
                        var alias = Directory.EnumerateFiles(texDir, "*.dds.phyre")
                            .Where(p => !Path.GetFileName(p).Contains("_anim", StringComparison.OrdinalIgnoreCase))
                            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                            .FirstOrDefault();
                        if (alias is not null)
                        {
                            sources.Add((alias, $"{id}.png"));
                        }
                    }

                    if (includeAnim && Directory.Exists(texDir))
                    {
                        foreach (var extra in Directory.EnumerateFiles(texDir, "*.dds.phyre")
                                     .Where(p => !string.Equals(Path.GetFileName(p), $"{id}.dds.phyre", StringComparison.OrdinalIgnoreCase))
                                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                        {
                            var stem = Path.GetFileName(extra);
                            stem = stem[..^".dds.phyre".Length];
                            sources.Add((extra, $"{stem}.png"));
                        }
                    }
                }

                if (sources.Count == 0)
                {
                    blocked++;
                    reports.Add(PhyreTextureFileExtractionReport.Blocked(
                        $"{cat}/{id}", FileSnapshot.FromPath(Path.Combine(texDir, $"{id}.dds.phyre"), portable),
                        fromManifest ? "untextured_by_manifest" : "blocked_missing_texture_phyre",
                        fromManifest ? "ahwin32 manifest lists no body texture (model is untextured by design)" : "no primary .dds.phyre for this id"));
                    continue;
                }

                foreach (var (src, pngName) in sources)
                {
                    var pngPath = Path.Combine(catOut, pngName);
                    if (!overwrite && File.Exists(pngPath))
                    {
                        ok++;
                        continue;
                    }

                    try
                    {
                        var report = PhyreTextureExtractor.ExtractTextureFile(src, pngPath, $"{cat}/{Path.GetFileNameWithoutExtension(pngName)}", portable);
                        reports.Add(report);
                        if (report.DecisionBand.StartsWith("blocked", StringComparison.Ordinal))
                        {
                            blocked++;
                        }
                        else
                        {
                            ok++;
                        }
                    }
                    catch (Exception exception)
                    {
                        blocked++;
                        reports.Add(PhyreTextureFileExtractionReport.Blocked(
                            $"{cat}/{Path.GetFileNameWithoutExtension(pngName)}", FileSnapshot.FromPath(src, portable),
                            "blocked_decode_exception", exception.Message));
                    }
                }
            }

            perCat.Add(new { category = cat, status = "scanned", ids = ids.Length, ok, blocked });
        }

        var formatHistogram = reports
            .Where(r => r.Format is not null)
            .GroupBy(r => r.Format!)
            .ToDictionary(g => g.Key, g => g.Count());

        var indexPath = Path.Combine(outputRoot, "texture-batch-index.json");
        WriteJson(indexPath, new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            ps3Root = portable ? ToPortablePath(ps3Root) : ps3Root,
            categories = cats,
            includeAnim,
            totalExtracted = reports.Count(r => !r.DecisionBand.StartsWith("blocked", StringComparison.Ordinal)),
            totalBlocked = reports.Count(r => r.DecisionBand.StartsWith("blocked", StringComparison.Ordinal)),
            formatHistogram,
            perCategory = perCat,
            reports,
        });

        string? provenancePath = null;
        if (fromManifest)
        {
            provenancePath = Path.Combine(outputRoot, "texture-provenance-map.json");
            WriteJson(provenancePath, new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                note = "Authoritative model->texture map parsed from each <id>.ahwin32 g_fileNames[] (D3D11). Source of truth for which texture the game loads.",
                modelCount = provenance.Count,
                multiTexture = provMultiTexture,
                untextured = provUntextured,
                models = provenance,
            });
        }

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            outputRoot,
            indexPath,
            provenancePath,
            perCategory = perCat,
            formatHistogram,
            totalExtracted = reports.Count(r => !r.DecisionBand.StartsWith("blocked", StringComparison.Ordinal)),
            totalBlocked = reports.Count(r => r.DecisionBand.StartsWith("blocked", StringComparison.Ordinal)),
        }, JsonOptions));

        return 0;
    }

    private readonly record struct Ahwin32Texture(string Cat, string SrcId, string File, bool IsAnim);

    // Parse a model's <id>.ahwin32 (auto-generated Phyre C header) for the D3D11 g_fileNames[] texture entries.
    // Returns deduped (cat, srcId, file, isAnim) for every chr/<cat>/<src>/tex/D3D11/<file>.dds.phyre reference.
    // The ahwin32 is the authoritative asset manifest (see docs/reverse/FFX_AHWIN32_ASSET_MANIFEST_2026-06-05.md).
    private static List<Ahwin32Texture> ParseAhwin32Textures(string ahwin32Path)
    {
        var result = new List<Ahwin32Texture>();
        if (!File.Exists(ahwin32Path))
        {
            return result;
        }

        var text = Encoding.ASCII.GetString(File.ReadAllBytes(ahwin32Path));
        var rx = new Regex(@"chr/([^/""]+)/([^/""]+)/tex/D3D11/([^""]+?)\.dds\.phyre", RegexOptions.IgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in rx.Matches(text))
        {
            var cat = m.Groups[1].Value;
            var src = m.Groups[2].Value;
            var file = m.Groups[3].Value;
            if (!seen.Add($"{cat}/{src}/{file}"))
            {
                continue;
            }

            result.Add(new Ahwin32Texture(cat, src, file, file.Contains("_anim", StringComparison.OrdinalIgnoreCase)));
        }

        return result;
    }

    private static int RunOracleIndex(IReadOnlyDictionary<string, string> options)
    {
        var outputRoot = Path.GetFullPath(GetOption(options, "output", GetLocalOutputRoot("oracle-index")));
        var roots = GetRequiredOption(options, "roots", "semicolon-separated model folders")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Path.GetFullPath)
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Directory.CreateDirectory(outputRoot);

        var assets = roots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            .Where(IsModelOracleCandidate)
            .Select(path => OracleAssetSnapshot.FromPath(path, portable: false))
            .Where(asset => asset is not null)
            .Cast<OracleAssetSnapshot>()
            .OrderByDescending(asset => asset.SourceKind == "fbx_oracle")
            .ThenBy(asset => asset.MonsterId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(asset => asset.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var index = new OracleIndexReport(
            DateTimeOffset.UtcNow,
            roots,
            assets.Length,
            assets.Count(asset => string.Equals(asset.Extension, ".fbx", StringComparison.OrdinalIgnoreCase)),
            assets.Count(asset => string.Equals(asset.Extension, ".glb", StringComparison.OrdinalIgnoreCase)
                || string.Equals(asset.Extension, ".gltf", StringComparison.OrdinalIgnoreCase)),
            assets.Count(asset => string.Equals(asset.Extension, ".dae", StringComparison.OrdinalIgnoreCase)),
            assets);

        var indexPath = Path.Combine(outputRoot, "fbx-oracle-index.json");
        WriteJson(indexPath, index);

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            outputRoot,
            indexPath,
            index.AssetCount,
            index.FbxCount,
            index.GltfCount,
            index.DaeCount,
            monsters = assets.Select(asset => asset.MonsterId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        }, JsonOptions));

        return 0;
    }

    private static int RunDescriptorReport(IReadOnlyDictionary<string, string> options)
    {
        var outputRoot = Path.GetFullPath(GetOption(options, "output", GetLocalOutputRoot("descriptor-reports")));
        var ps3Root = GetRequiredRoot(options, "ps3-root", "FFX_PS3DATA_ROOT");
        var portable = IsEnabled(options, "portable");
        var monsterIds = ResolveMonsterIds(ps3Root, GetOption(options, "monsters", GetOption(options, "monster", "m020")))
            .ToArray();

        Directory.CreateDirectory(outputRoot);

        var reports = new List<PhyreDescriptorReport>();
        foreach (var monsterId in monsterIds)
        {
            var daePath = Path.Combine(ps3Root, "chr", "mon", monsterId, "mdl", "d3d11", $"{monsterId}.dae.phyre");
            var report = PhyreDescriptorParser.Parse(monsterId, daePath, portable);
            reports.Add(report);
            WriteJson(Path.Combine(outputRoot, $"{monsterId}.phyre-descriptor-report.json"), report);
        }

        var index = new PhyreDescriptorIndex(
            DateTimeOffset.UtcNow,
            portable ? ToPortablePath(ps3Root) : ps3Root,
            reports.Count,
            reports.Count(report => report.DecisionBand == "mesh_descriptor_decode_candidate"),
            reports.Count(report => report.DecisionBand.Contains("blocked", StringComparison.OrdinalIgnoreCase)),
            reports.ToArray());
        var indexPath = Path.Combine(outputRoot, "phyre-descriptor-report-index.json");
        WriteJson(indexPath, index);

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            outputRoot,
            indexPath,
            reports = reports.Select(report => new
            {
                report.MonsterId,
                report.DecisionBand,
                report.MeshSegmentCount,
                report.TotalVertexCount,
                report.TotalIndexCount,
                report.PositionBounds,
                report.NextStrike,
            }),
        }, JsonOptions));

        return 0;
    }

    private static int RunExportStatic(IReadOnlyDictionary<string, string> options)
    {
        var outputRoot = Path.GetFullPath(GetOption(options, "output", GetLocalOutputRoot("static-exports")));
        var ps3Root = GetRequiredRoot(options, "ps3-root", "FFX_PS3DATA_ROOT");
        var monsterId = NormalizeMonsterId(GetOption(options, "monster", "m020"));
        var catalogPath = options.TryGetValue("catalog", out var rawCatalog)
            ? Path.GetFullPath(rawCatalog)
            : null;

        Directory.CreateDirectory(outputRoot);

        var monsterRoot = Path.Combine(ps3Root, "chr", "mon", monsterId);
        var daePath = Path.Combine(monsterRoot, "mdl", "d3d11", $"{monsterId}.dae.phyre");
        if (!File.Exists(daePath))
        {
            throw new InvalidOperationException($"Missing .dae.phyre: {daePath}");
        }

        var bytes = File.ReadAllBytes(daePath);
        var layout = PhyreStaticMeshLayout.FromDae(bytes);
        var assetName = $"{monsterId}_static_mesh.gltf";
        var assetPath = Path.Combine(outputRoot, assetName);
        var report = StaticMeshGltfWriter.Write(assetPath, monsterId, bytes, layout);
        var reportPath = Path.Combine(outputRoot, $"{monsterId}.static-mesh-export-report.json");
        WriteJson(reportPath, report);

        if (!string.IsNullOrWhiteSpace(catalogPath))
        {
            UpdateCatalogForStaticMesh(catalogPath, monsterId, assetName, report);
        }

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            assetPath,
            reportPath,
            catalogPath,
            report.DecisionBand,
            report.VertexCount,
            report.IndexCount,
            report.TriangleCount,
            report.PositionBounds,
        }, JsonOptions));

        return 0;
    }

    private static PhyreMeshProbeReport ProbeMonster(string ps3Root, string texturePngRoot, string monsterId, bool portable)
    {
        var monsterRoot = Path.Combine(ps3Root, "chr", "mon", monsterId);
        var ahPath = Path.Combine(monsterRoot, $"{monsterId}.ahwin32");
        var daePath = Path.Combine(monsterRoot, "mdl", "d3d11", $"{monsterId}.dae.phyre");
        var ddsPath = Path.Combine(monsterRoot, "tex", "d3d11", $"{monsterId}.dds.phyre");
        var pngRoot = Path.Combine(texturePngRoot, "chr", "mon", monsterId);
        var pngPaths = Directory.Exists(pngRoot)
            ? Directory.GetFiles(pngRoot, "*.png", SearchOption.AllDirectories)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .Take(32)
                .Select(path => portable ? ToPortablePath(path) : path)
                .ToArray()
            : Array.Empty<string>();

        var markerCounts = File.Exists(daePath)
            ? CountMarkers(daePath, MeshMarkers)
            : Array.Empty<AsciiMarkerProbe>();
        var markerLookup = markerCounts.ToDictionary(item => item.Marker, item => item.Count, StringComparer.OrdinalIgnoreCase);
        var hasMeshDescriptors =
            markerLookup.GetValueOrDefault("PMesh") > 0
            && markerLookup.GetValueOrDefault("PVertexStream") > 0
            && markerLookup.GetValueOrDefault("PMeshSegment") > 0;
        var hasEmbeddedAnimation =
            markerLookup.GetValueOrDefault("PAnimationClip") > 0
            && markerLookup.GetValueOrDefault("PAnimationChannelTarget") > 0;

        var decisionBand = hasMeshDescriptors
            ? "mesh_carrier_present_unparsed"
            : File.Exists(daePath)
                ? "blocked_mesh_descriptor_markers_missing"
                : "blocked_missing_dae_phyre";
        var nextStrike = hasMeshDescriptors
            ? "decode PMeshSegment/PVertexStream and locate the index-buffer descriptor before emitting any GLB"
            : "recover or rebuild the Phyre mesh descriptor parser";

        return new PhyreMeshProbeReport(
            DateTimeOffset.UtcNow,
            monsterId,
            portable,
            FileSnapshot.FromPath(daePath, portable),
            FileSnapshot.FromPath(ahPath, portable),
            FileSnapshot.FromPath(ddsPath, portable),
            new TextureProbe(portable ? ToPortablePath(pngRoot) : pngRoot, Directory.Exists(pngRoot), pngPaths.Length, pngPaths),
            markerCounts,
            ExtractAhReferences(ahPath).Select(item => portable ? ToPortablePath(item) : item).ToArray(),
            hasMeshDescriptors ? "mesh_descriptor_markers_present" : "mesh_descriptor_markers_unproved",
            hasEmbeddedAnimation ? "embedded_animation_markers_present" : "embedded_animation_markers_absent",
            decisionBand,
            "blocked_converter_descriptor_decode_required",
            nextStrike);
    }

    private static AsciiMarkerProbe[] CountMarkers(string path, IEnumerable<string> markers)
    {
        var text = Encoding.ASCII.GetString(File.ReadAllBytes(path));
        return markers
            .Select(marker =>
            {
                var offsets = new List<int>();
                var count = 0;
                var index = 0;
                while ((index = text.IndexOf(marker, index, StringComparison.Ordinal)) >= 0)
                {
                    count++;
                    if (offsets.Count < 24)
                    {
                        offsets.Add(index);
                    }

                    index += marker.Length;
                }

                return new AsciiMarkerProbe(marker, count, offsets.ToArray());
            })
            .ToArray();
    }

    private static string[] ExtractAhReferences(string path)
    {
        if (!File.Exists(path))
        {
            return Array.Empty<string>();
        }

        var strings = new List<string>();
        var current = new StringBuilder();
        foreach (var value in File.ReadAllBytes(path))
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
            .Where(item => item.Contains("phyre", StringComparison.OrdinalIgnoreCase)
                || item.Contains("d3d11", StringComparison.OrdinalIgnoreCase)
                || item.Contains("mdl/", StringComparison.OrdinalIgnoreCase)
                || item.Contains("tex/", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(64)
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

    private static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine, new UTF8Encoding(false));
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
                $"--{key} is required unless {environmentVariable} points to a compatible local folder.");
        }

        return Path.GetFullPath(value);
    }

    private static string GetLocalOutputRoot(string leaf) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FFXProjectEditor",
        "PhyreExports",
        leaf);

    private static bool IsEnabled(IReadOnlyDictionary<string, string> options, string key) =>
        options.TryGetValue(key, out var value)
        && (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase));

    public static string ToPortablePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var normalized = path.Replace('\\', '/');
        var markers = new[]
        {
            "ffx_data/gamedata/ps3data/",
            "ps3data_textures_png/",
            "chr/mon/",
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

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  PhyreModelExportLab.exe probe --ps3-root <folder> --texture-png-root <folder> [--monsters m020,m018] [--output <folder>] [--portable]");
        Console.Error.WriteLine("  PhyreModelExportLab.exe export-static --ps3-root <folder> --monster m020 [--output <folder>] [--catalog <file.json>]");
        Console.Error.WriteLine("  PhyreModelExportLab.exe oracle-index --roots <folder1;folder2> [--output <folder>]");
        Console.Error.WriteLine("  PhyreModelExportLab.exe descriptor-report --ps3-root <folder> [--monsters m020,m018|all] [--output <folder>] [--portable]");
        Console.Error.WriteLine("  PhyreModelExportLab.exe export-descriptor-static --ps3-root <folder> [--monsters m020,m018|all] [--output <folder>] [--portable]");
        Console.Error.WriteLine("  PhyreModelExportLab.exe extract-texture-file --in <dds.phyre> --out <png> [--id name]");
        Console.Error.WriteLine("  PhyreModelExportLab.exe extract-texture-batch --ps3-root <folder> [--cats pc,npc,sum] [--output <folder>] [--from-manifest] [--include-anim] [--overwrite]");
        return 1;
    }

    private static bool IsModelOracleCandidate(string path)
    {
        var extension = Path.GetExtension(path);
        if (!extension.Equals(".fbx", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".glb", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".gltf", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".dae", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = Path.GetFileNameWithoutExtension(path);
        if (fileName.Length < 4 || fileName[0] != 'm' || !fileName.Skip(1).Take(3).All(char.IsDigit))
        {
            return false;
        }

        var normalized = path.Replace('/', '\\');
        return !normalized.Contains(@"\3rdParty\assimp\test\", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains(@"\mgrp_", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains(@"\motion_", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ResolveMonsterIds(string ps3Root, string raw)
    {
        if (string.Equals(raw, "all", StringComparison.OrdinalIgnoreCase))
        {
            var root = Path.Combine(ps3Root, "chr", "mon");
            if (!Directory.Exists(root))
            {
                throw new InvalidOperationException($"Monster root not found: {root}");
            }

            return Directory.EnumerateDirectories(root, "m???", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .Select(NormalizeMonsterId)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase);
        }

        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeMonsterId)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static void UpdateCatalogForStaticMesh(string catalogPath, string monsterId, string assetName, StaticMeshExportReport report)
    {
        if (!File.Exists(catalogPath))
        {
            throw new InvalidOperationException($"Catalog not found: {catalogPath}");
        }

        var root = JsonNode.Parse(File.ReadAllText(catalogPath))
            ?? throw new InvalidOperationException($"Unable to parse catalog: {catalogPath}");
        var entries = root["entries"] as JsonArray
            ?? throw new InvalidOperationException("Catalog has no entries array.");
        foreach (var entry in entries.OfType<JsonObject>())
        {
            if (!string.Equals(entry["monsterId"]?.GetValue<string>(), monsterId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entry["assetStatus"] = "mesh_static_lab_candidate";
            entry["openableAssetPath"] = assetName.Replace('\\', '/');
            entry["decisionBand"] = report.DecisionBand;
            entry["structureLayout"] = "phyre_static_mesh_positions_indices";
            entry["nodeCount"] = report.VertexCount;
            var tags = entry["tags"] as JsonArray ?? new JsonArray();
            AddTag(tags, "mesh_static_lab_candidate");
            AddTag(tags, "mesh_real_untextured_lab");
            entry["tags"] = tags;
            break;
        }

        root["defaultAssetId"] = monsterId;
        var summary = root["summary"] as JsonObject;
        if (summary is not null)
        {
            summary["openableAssetCount"] = Math.Max(summary["openableAssetCount"]?.GetValue<int>() ?? 0, 1);
        }

        File.WriteAllText(catalogPath, root.ToJsonString(JsonOptions) + Environment.NewLine, new UTF8Encoding(false));
    }

    private static void AddTag(JsonArray tags, string tag)
    {
        if (!tags.Any(item => string.Equals(item?.GetValue<string>(), tag, StringComparison.OrdinalIgnoreCase)))
        {
            tags.Add(tag);
        }
    }
}

public sealed record PhyreMeshProbeIndex(
    DateTimeOffset GeneratedAtUtc,
    string Ps3Root,
    string TexturePngRoot,
    int ReportCount,
    int MeshDescriptorMarkerPositiveCount,
    int EmbeddedAnimationMarkerPositiveCount,
    PhyreMeshProbeReport[] Reports);

public sealed record PhyreMeshProbeReport(
    DateTimeOffset GeneratedAtUtc,
    string MonsterId,
    bool Portable,
    FileSnapshot DaePhyre,
    FileSnapshot AhWin32,
    FileSnapshot DdsPhyre,
    TextureProbe Textures,
    AsciiMarkerProbe[] MarkerCounts,
    string[] AhReferences,
    string MeshDescriptorStatus,
    string EmbeddedAnimationStatus,
    string DecisionBand,
    string PromotionStatus,
    string NextStrike);

public sealed record TextureProbe(
    string Root,
    bool RootExists,
    int PngCountListed,
    string[] PngPaths);

public sealed record AsciiMarkerProbe(
    string Marker,
    int Count,
    int[] FirstOffsets);

public sealed record FileSnapshot(
    string Path,
    bool Exists,
    long? Length,
    string? Sha256)
{
    public static FileSnapshot FromPath(string path, bool portable)
    {
        if (!File.Exists(path))
        {
            return new FileSnapshot(portable ? Program.ToPortablePath(path) : path, false, null, null);
        }

        using var stream = File.OpenRead(path);
        return new FileSnapshot(
            portable ? Program.ToPortablePath(path) : path,
            true,
            stream.Length,
            Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
    }
}

public sealed record PhyreStaticMeshLayout(
    uint IndexBufferLength,
    uint VertexBufferLength,
    uint TextureBufferLength,
    int BufferStart,
    int IndexStart,
    int VertexStart,
    int VertexCount,
    int IndexCount,
    int VertexStride,
    int PositionStride)
{
    public static PhyreStaticMeshLayout FromDae(byte[] bytes)
    {
        if (bytes.Length < 84)
        {
            throw new InvalidOperationException("DAE Phyre file is too short for the known buffer length header.");
        }

        var indexBufferLength = BitConverter.ToUInt32(bytes, 72);
        var vertexBufferLength = BitConverter.ToUInt32(bytes, 76);
        var textureBufferLength = BitConverter.ToUInt32(bytes, 80);
        const int vertexStride = 56;
        const int positionStride = 12;

        if (indexBufferLength == 0 || vertexBufferLength == 0)
        {
            throw new InvalidOperationException($"Known buffer lengths are empty: idx={indexBufferLength}, vtx={vertexBufferLength}.");
        }

        if (indexBufferLength % 2 != 0)
        {
            throw new InvalidOperationException($"Index buffer length is not u16-aligned: {indexBufferLength}.");
        }

        if (vertexBufferLength % vertexStride != 0)
        {
            throw new InvalidOperationException($"Vertex buffer length does not match pilot stride {vertexStride}: {vertexBufferLength}.");
        }

        var totalBufferLength = checked((long)indexBufferLength + vertexBufferLength + textureBufferLength);
        var bufferStart = checked((int)(bytes.LongLength - totalBufferLength));
        if (bufferStart < 0)
        {
            throw new InvalidOperationException("Known buffer lengths exceed file length.");
        }

        var indexStart = bufferStart;
        var vertexStart = checked(indexStart + (int)indexBufferLength);
        var vertexCount = (int)(vertexBufferLength / vertexStride);
        var indexCount = (int)(indexBufferLength / 2);

        return new PhyreStaticMeshLayout(
            indexBufferLength,
            vertexBufferLength,
            textureBufferLength,
            bufferStart,
            indexStart,
            vertexStart,
            vertexCount,
            indexCount,
            vertexStride,
            positionStride);
    }
}

public static class StaticMeshGltfWriter
{
    public static StaticMeshExportReport Write(string path, string monsterId, byte[] source, PhyreStaticMeshLayout layout)
    {
        var positions = new byte[layout.VertexCount * layout.PositionStride];
        Buffer.BlockCopy(source, layout.VertexStart, positions, 0, positions.Length);

        var indices = new byte[layout.IndexCount * 2];
        Buffer.BlockCopy(source, layout.IndexStart, indices, 0, indices.Length);

        var min = new[] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
        var max = new[] { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
        for (var index = 0; index < layout.VertexCount; index++)
        {
            var sourceOffset = layout.VertexStart + index * layout.VertexStride;
            for (var axis = 0; axis < 3; axis++)
            {
                var value = BitConverter.ToSingle(source, sourceOffset + axis * 4);
                if (!float.IsFinite(value))
                {
                    throw new InvalidOperationException($"Non-finite position at vertex {index}, axis {axis}: {value}");
                }

                min[axis] = Math.Min(min[axis], value);
                max[axis] = Math.Max(max[axis], value);
            }
        }

        var maxIndex = 0;
        var outOfRange = 0;
        for (var index = 0; index < layout.IndexCount; index++)
        {
            var value = BitConverter.ToUInt16(source, layout.IndexStart + index * 2);
            maxIndex = Math.Max(maxIndex, value);
            if (value >= layout.VertexCount)
            {
                outOfRange++;
            }
        }

        if (outOfRange > 0)
        {
            throw new InvalidOperationException($"Index buffer has {outOfRange} indices outside vertex count {layout.VertexCount}.");
        }

        var buffer = new List<byte>();
        var positionOffset = buffer.Count;
        buffer.AddRange(positions);
        Align(buffer, 4);
        var indexOffset = buffer.Count;
        buffer.AddRange(indices);
        Align(buffer, 4);

        var gltf = new
        {
            asset = new
            {
                version = "2.0",
                generator = "FFX Mod Studio Phyre model exporter",
                copyright = "Read-only lab export from decoded Phyre buffer slices. No game files were modified.",
            },
            extensionsUsed = new[] { "KHR_materials_unlit" },
            scene = 0,
            scenes = new[] { new { nodes = new[] { 0 }, name = $"{monsterId} static Phyre mesh lab" } },
            nodes = new[] { new { name = $"{monsterId}_static_mesh", mesh = 0 } },
            meshes = new[]
            {
                new
                {
                    name = $"{monsterId}_static_mesh",
                    primitives = new[]
                    {
                        new
                        {
                            attributes = new Dictionary<string, int> { ["POSITION"] = 0 },
                            indices = 1,
                            mode = 4,
                            material = 0,
                        },
                    },
                },
            },
            materials = new[]
            {
                new
                {
                    name = "lab_untextured_debug",
                    pbrMetallicRoughness = new
                    {
                        baseColorFactor = new[] { 0.35, 0.68, 0.92, 1.0 },
                        metallicFactor = 0,
                        roughnessFactor = 0.7,
                    },
                    doubleSided = true,
                    extensions = new Dictionary<string, object> { ["KHR_materials_unlit"] = new { } },
                },
            },
            buffers = new[]
            {
                new
                {
                    uri = "data:application/octet-stream;base64," + Convert.ToBase64String(buffer.ToArray()),
                    byteLength = buffer.Count,
                },
            },
            bufferViews = new[]
            {
                new { buffer = 0, byteOffset = positionOffset, byteLength = positions.Length, target = 34962 },
                new { buffer = 0, byteOffset = indexOffset, byteLength = indices.Length, target = 34963 },
            },
            accessors = new object[]
            {
                new
                {
                    bufferView = 0,
                    byteOffset = 0,
                    componentType = 5126,
                    count = layout.VertexCount,
                    type = "VEC3",
                    min,
                    max,
                },
                new
                {
                    bufferView = 1,
                    byteOffset = 0,
                    componentType = 5123,
                    count = layout.IndexCount,
                    type = "SCALAR",
                    min = new[] { 0 },
                    max = new[] { maxIndex },
                },
            },
            extras = new
            {
                monsterId,
                decisionBand = "mesh_static_lab_candidate",
                status = "positions_and_indices_from_known_phyre_buffers",
                layout,
                note = "Static untextured lab mesh. UV/material/skin/segment descriptors are not promoted yet.",
            },
        };

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, JsonSerializer.Serialize(gltf, Program.JsonOptions) + Environment.NewLine, new UTF8Encoding(false));

        return new StaticMeshExportReport(
            DateTimeOffset.UtcNow,
            monsterId,
            path,
            "mesh_static_lab_candidate",
            layout.VertexCount,
            layout.IndexCount,
            layout.IndexCount / 3,
            new PositionBounds(min, max),
            layout,
            "uv_material_skin_segments_pending");
    }

    private static void Align(List<byte> buffer, int alignment)
    {
        while (buffer.Count % alignment != 0)
        {
            buffer.Add(0);
        }
    }
}

public sealed record StaticMeshExportReport(
    DateTimeOffset GeneratedAtUtc,
    string MonsterId,
    string AssetPath,
    string DecisionBand,
    int VertexCount,
    int IndexCount,
    int TriangleCount,
    PositionBounds PositionBounds,
    PhyreStaticMeshLayout Layout,
    string RemainingBlocker);

public sealed record PositionBounds(
    double[] Min,
    double[] Max);

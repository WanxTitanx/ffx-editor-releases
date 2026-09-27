using FFXProjectEditor.FfxLib.BattleMap;
using FFXProjectEditor.Modules.AuroraChamber;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Resources;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FFXProjectEditor.Modules.AuroraFieldExplorer
{
    /// <summary>Exports overworld <c>map/</c> geometry and deep-links MapViewer (separate from Aurora Chamber btlmap flow).</summary>
    internal static class AuroraFieldExplorer_FieldRenderer
    {
        public const string MapPrefix = "map";
        public const string MiniCatalogName = "field-explorer-catalog.json";
        public const string EncountersOverlayName = "field-encounters.json";

        public sealed class RenderResult
        {
            public required bool Ok { get; init; }
            public required string Message { get; init; }
            public string? DeepLinkUrl { get; init; }
            public string? OutputDir { get; init; }
            public int ExitCode { get; init; }
        }

        public static string AssetId(FieldMapRow field) => $"{MapPrefix}_{field.Area}_{field.FieldToken}";

        public static string? GetOutputDir(FieldMapRow field)
        {
            if (AuroraSceneRenderer.ResolveMapViewerIndex() == null)
                return null;
            return Path.Combine(AuroraSceneRenderer.ViewerDataRoot, "maps", MapPrefix, field.Area, field.FieldToken);
        }

        public static bool IsFieldMounted(FieldMapRow field)
        {
            string? dir = GetOutputDir(field);
            if (dir == null)
                return false;
            return BattleMap_ExportQuality.TryResolveBestGltf(dir, AssetId(field), out _, out _);
        }

        public static RenderResult ExportFieldGeometry(FieldMapRow field, bool forceReexport)
        {
            ArgumentNullException.ThrowIfNull(field);

            string? ps3Root = AuroraSceneRenderer.ResolvePs3DataRoot();
            if (ps3Root == null)
                return Fail(Strings.U_Au_FieldRendererPs3DataRoot);

            string? indexPath = AuroraSceneRenderer.ResolveMapViewerIndex();
            if (indexPath == null)
                return Fail(Strings.U_Au_FieldRendererMapViewerNotFound);

            string outputDir = GetOutputDir(field)!;
            string assetId = AssetId(field);
            string relBase = $"/viewer-data/map/maps/{MapPrefix}/{field.Area}/{field.FieldToken}";
            Directory.CreateDirectory(outputDir);

            int exitCode = 0;
            if (forceReexport || !BattleMap_ExportQuality.TryResolveBestGltf(outputDir, assetId, out _, out _))
            {
                ExportInvocation? inv = BuildExportInvocation(ps3Root, field.MapEntity, outputDir);
                if (inv == null)
                    return Fail(Strings.U_Au_FieldRendererPhyreNotFound);

                exitCode = RunProcess(inv, out string tail);
                if (!BattleMap_ExportQuality.TryResolveBestGltf(outputDir, assetId, out string gltfFull, out _))
                    return new RenderResult { Ok = false, ExitCode = exitCode, OutputDir = outputDir, Message = string.Format(Strings.U_Au_FieldRendererExportFailed, exitCode, tail) };

                /* Btlmap fallback: if primitives == 0, redirect to map version */
                string manifestPath = Path.Combine(outputDir, $"{assetId}.map-export-manifest.json");
                if (exitCode == 0 && field.MapEntity.StartsWith("btlmap/", StringComparison.OrdinalIgnoreCase)
                    && File.Exists(manifestPath))
                {
                    try
                    {
                        var manifest = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                            File.ReadAllText(manifestPath));
                        if (manifest.TryGetProperty("primitiveCount", out var pc) && pc.GetInt32() == 0
                            && manifest.TryGetProperty("exportDecisionBand", out var edb)
                            && edb.GetString()?.Contains("blocked") == true)
                        {
                            /* Convert btlmap/kino/kino05_a → map/kino/kino05 */
                            string fallbackEntity = "map/" + field.MapEntity.Substring("btlmap/".Length);
                            /* Strip _a, _b suffixes for btlmap arena variants */
                            int suffixPos = fallbackEntity.LastIndexOf('_');
                            if (suffixPos > fallbackEntity.LastIndexOf('/'))
                                fallbackEntity = fallbackEntity.Substring(0, suffixPos);
                            var fallbackInv = BuildExportInvocation(ps3Root, fallbackEntity, outputDir);
                            if (fallbackInv != null)
                            {
                                exitCode = RunProcess(fallbackInv, out tail);
                                if (BattleMap_ExportQuality.TryResolveBestGltf(outputDir, assetId, out gltfFull, out _))
                                    WriteMiniCatalog(Path.Combine(outputDir, MiniCatalogName), field, assetId, relBase, gltfFull);
                            }
                        }
                    }
                    catch { /* fallback failure is non-fatal */ }
                }

                WriteMiniCatalog(Path.Combine(outputDir, MiniCatalogName), field, assetId, relBase, gltfFull);
            }
            else
            {
                BattleMap_ExportQuality.TryResolveBestGltf(outputDir, assetId, out string gltfFull, out _);
                WriteMiniCatalog(Path.Combine(outputDir, MiniCatalogName), field, assetId, relBase, gltfFull);
            }

            return new RenderResult
            {
                Ok = true,
                ExitCode = exitCode,
                OutputDir = outputDir,
                Message = IsFieldMounted(field) ? Strings.U_Au_FieldMounted : Strings.U_Au_ExportComplete,
            };
        }

        public static RenderResult RenderField(FieldMapRow field, FieldEncounterIndexResult encounters, bool forceReexport)
        {
            RenderResult geom = ExportFieldGeometry(field, forceReexport);
            if (!geom.Ok)
                return geom;

            string? indexPath = AuroraSceneRenderer.ResolveMapViewerIndex();
            if (indexPath == null)
                return geom;

            string? url = BuildFieldViewerUrl(field, encounters, geom.OutputDir!);
            BattleMap_ExportQuality? quality = BattleMap_ExportQuality.TryRead(geom.OutputDir!, AssetId(field));
            string qualityNote = quality?.QualityLabel is { Length: > 0 } q ? $" · {q}" : "";

            return new RenderResult
            {
                Ok = url != null,
                ExitCode = geom.ExitCode,
                OutputDir = geom.OutputDir,
                DeepLinkUrl = url,
                Message = url != null
                    ? string.Format(Strings.U_Au_FieldReady, qualityNote)
                       + (encounters.TotalGroups > 0 ? string.Format(Strings.U_Au_EncounterGroupsCount, encounters.TotalGroups) : Strings.U_Au_NoBtlGroups)
                    : Strings.U_Au_MapViewerUrlFailed,
            };
        }

        /// <summary>Deep-link for an already-mounted field (no re-export).</summary>
        public static string? TryBuildViewerUrl(FieldMapRow field, FieldEncounterIndexResult encounters)
        {
            if (!IsFieldMounted(field))
                return null;

            string? indexPath = AuroraSceneRenderer.ResolveMapViewerIndex();
            if (indexPath == null)
                return null;

            string? dir = GetOutputDir(field);
            if (dir == null)
                return null;

            return BuildFieldViewerUrl(field, encounters, dir);
        }

        public static string Launch(string url) => AuroraSceneRenderer.Launch(url);

        /// <summary>
        /// Rebuilds <c>field-encounters.json</c> for every walked field that already has a mounted glTF export,
        /// pulling NPC/chest/trigger data from the current WalkManifest without re-exporting geometry.
        /// </summary>
        public static int RefreshWalkEncountersOverlays(BattleMapCatalog_File? btlmapCatalog = null)
        {
            AuroraFieldExplorer_WalkManifest.WalkBundle? bundle = AuroraFieldExplorer_WalkManifest.TryLoad();
            if (bundle == null)
                return 0;

            IReadOnlyList<FieldMapRow> catalog = AuroraFieldExplorer_FieldCatalog.LoadAll(out _);
            int refreshed = 0;

            foreach (FieldMapRow field in catalog)
            {
                if (!AuroraFieldExplorer_WalkManifest.IsFieldWalked(bundle, field))
                    continue;
                if (!IsFieldMounted(field))
                    continue;

                string? outputDir = GetOutputDir(field);
                if (outputDir == null)
                    continue;

                FieldEncounterIndexResult enc = AuroraFieldExplorer_EncounterIndex.Build(field, btlmapCatalog);
                string relBase = $"./public/maps/{MapPrefix}/{field.Area}/{field.FieldToken}";
                string encPath = Path.Combine(outputDir, EncountersOverlayName);
                if (enc.TotalGroups == 0 && TryPatchWalkMarkersOnly(encPath, field, enc))
                {
                    refreshed++;
                    continue;
                }

                WriteEncountersOverlay(outputDir, field, enc, relBase);
                refreshed++;
            }

            return refreshed;
        }

        static string? BuildFieldViewerUrl(
            FieldMapRow field,
            FieldEncounterIndexResult encounters,
            string outputDir)
        {
            string relBase = $"/viewer-data/map/maps/{MapPrefix}/{field.Area}/{field.FieldToken}";
            string assetId = AssetId(field);
            if (!BattleMap_ExportQuality.TryResolveBestGltf(outputDir, assetId, out string gltfFull, out _))
                return null;

            WriteMiniCatalog(Path.Combine(outputDir, MiniCatalogName), field, assetId, relBase, gltfFull);
            string miniCatalogRel = $"{relBase}/{MiniCatalogName}";
            string encountersRel = WriteEncountersOverlay(outputDir, field, encounters, relBase);
            string mapToken = $"{MapPrefix}/{field.Area}/{field.FieldToken}";
            string encountersPath = Path.Combine(outputDir, EncountersOverlayName);
            string cacheV = File.Exists(encountersPath)
                ? File.GetLastWriteTimeUtc(encountersPath).ToString("yyyyMMddHHmmss")
                : DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            string encountersWithBust = $"{encountersRel}?v={cacheV}";
            string query = $"catalog={Uri.EscapeDataString(miniCatalogRel)}" +
                           $"&map={Uri.EscapeDataString(mapToken)}" +
                           "&fieldExplorer=1" +
                           $"&encounters={Uri.EscapeDataString(encountersWithBust)}";
            // forExternalBrowser: o controle abre o deep-link no navegador do sistema fora do Windows.
            return ViewerHubService.BuildUrl("map-scene-editor", query, forExternalBrowser: true);
        }

        static RenderResult Fail(string message) => new() { Ok = false, Message = message };

        static string WriteEncountersOverlay(string outputDir, FieldMapRow field, FieldEncounterIndexResult enc, string relBase)
        {
            string path = Path.Combine(outputDir, EncountersOverlayName);
            if (enc.TotalGroups == 0 && TryPatchWalkMarkersOnly(path, field, enc))
                return $"{relBase}/{EncountersOverlayName}";

            MapoutVpa_EncounterZones.ParseResult zones = AuroraFieldExplorer_EncounterZones.TryParse(
                field, enc.ExactGroups.Count > 0 ? enc.ExactGroups.Count : null);

            AuroraFieldExplorer_WalkManifest.FieldWalkShard? walkShard =
                AuroraFieldExplorer_WalkManifest.TryLoadFieldShard(field);

            object walkMarkers = BuildWalkMarkerPayload(walkShard);

            var payload = new Dictionary<string, object?>
            {
                ["overlaySchemaVersion"] = 2,
                ["freshness"] = new { generatedAtUtc = DateTime.UtcNow.ToString("o"), source = "aurora-field-explorer" },
                ["mapEntity"] = field.MapEntity,
                ["fieldToken"] = field.FieldToken,
                ["area"] = field.Area,
                ["honesty"] = BuildHonesty(zones, walkShard),
                ["exactGroupCount"] = enc.ExactGroups.Count,
                ["areaRelatedGroupCount"] = enc.AreaRelatedGroups.Count,
                ["suggestedArenaScene"] = enc.SuggestedArenaScene?.SceneId,
                ["groups"] = BuildGroupPayload(enc).ToList(),
                ["zoneMeta"] = new
                {
                    source = "mapout.vpa",
                    status = zones.Status.ToString(),
                    family = zones.Family,
                    fileSize = zones.FileSize,
                    metaBlockOffset = zones.MetaBlockOffset,
                    geometryBlockOffset = zones.GeometryBlockOffset,
                    coordScale = zones.CoordScale,
                    note = zones.Note,
                },
                ["zones"] = BuildZonePayload(zones).ToList(),
            };

            foreach (KeyValuePair<string, object?> kv in (Dictionary<string, object?>)walkMarkers)
                payload[kv.Key] = kv.Value;

            File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonOpts));
            return $"{relBase}/{EncountersOverlayName}";
        }

        /// <summary>
        /// Updates only Field Scout walk markers in an existing overlay, preserving btl.bin groups/zones.
        /// Used when btl.bin is unavailable (headless refresh) or to avoid wiping a good overlay.
        /// </summary>
        static bool TryPatchWalkMarkersOnly(string path, FieldMapRow field, FieldEncounterIndexResult enc)
        {
            if (!File.Exists(path))
                return false;

            AuroraFieldExplorer_WalkManifest.FieldWalkShard? walkShard =
                AuroraFieldExplorer_WalkManifest.TryLoadFieldShard(field);

            try
            {
                JsonNode? node = JsonNode.Parse(File.ReadAllText(path));
                if (node is not JsonObject obj)
                    return false;

                int? groupHint = null;
                if (obj["groups"] is JsonArray groups && groups.Count > 0)
                    groupHint = groups.Count;
                else if (enc.TotalGroups > 0)
                    groupHint = enc.TotalGroups;

                MapoutVpa_EncounterZones.ParseResult zones = AuroraFieldExplorer_EncounterZones.TryParse(field, groupHint);

                obj["honesty"] = BuildHonesty(zones, walkShard);
                obj["overlaySchemaVersion"] = 2;
                obj["freshness"] = JsonSerializer.SerializeToNode(new
                {
                    generatedAtUtc = DateTime.UtcNow.ToString("o"),
                    source = "aurora-field-explorer",
                }, JsonOpts);

                foreach (KeyValuePair<string, object?> kv in BuildWalkMarkerPayload(walkShard))
                {
                    obj[kv.Key] = JsonSerializer.SerializeToNode(kv.Value, JsonOpts);
                }

                File.WriteAllText(path, obj.ToJsonString(JsonOpts));
                return true;
            }
            catch
            {
                return false;
            }
        }

        static IEnumerable<object> BuildGroupPayload(FieldEncounterIndexResult enc)
        {
            foreach (FieldEncounterGroupRow g in enc.ExactGroups)
                yield return GroupJson(g, "exact");
            foreach (FieldEncounterGroupRow g in enc.AreaRelatedGroups)
                yield return GroupJson(g, "area_related");
        }

        static object GroupJson(FieldEncounterGroupRow g, string scope) => new
        {
            scope,
            mapBucket = g.MapBucket,
            groupIndex = g.GroupIndex,
            battlefield = g.Battlefield,
            danger = g.Danger,
            totalWeight = g.TotalWeight,
            formations = g.Formations.Select(f => new
            {
                battleId = f.BattleId,
                formationId = f.FormationId,
                weight = f.Weight,
                hasBattleFile = f.HasBattleFile,
            }),
        };

        static string BuildHonesty(
            MapoutVpa_EncounterZones.ParseResult zones,
            AuroraFieldExplorer_WalkManifest.FieldWalkShard? walkShard)
        {
            string baseHonesty;
            if (zones.Status == MapoutVpa_EncounterZones.MapoutZoneStatus.Ok && zones.Zones.Count > 0)
            {
                baseHonesty = Strings.U_Au_HonestyZonesOk;
            }
            else if (zones.Status == MapoutVpa_EncounterZones.MapoutZoneStatus.Stub)
            {
                baseHonesty = Strings.U_Au_HonestyZonesStub;
            }
            else
            {
                baseHonesty = string.Format(Strings.U_Au_HonestyZonesOther, zones.Status, zones.Note ?? Strings.U_Au_NoGeometryParsed);
            }

            if (walkShard?.Chests.Count > 0)
                baseHonesty += string.Format(Strings.U_Au_HonestyChests, walkShard.Chests.Count);

            AuroraFieldExplorer_EncounterOverlayCompiler.CompiledWalkOverlay? compiled = walkShard != null
                ? AuroraFieldExplorer_EncounterOverlayCompiler.Compile(walkShard)
                : null;

            if (compiled != null && compiled.Counts.RawTotal > 0)
            {
                baseHonesty += string.Format(Strings.U_Au_HonestyChrWalk, compiled.Counts.RawTotal, compiled.Counts.DedupedTotal,
                    compiled.Counts.StoryNpc, compiled.Counts.Party, compiled.Counts.FieldEnemy, compiled.Counts.FieldProp);
            }

            if (walkShard?.Triggers.Count > 0)
                baseHonesty += string.Format(Strings.U_Au_HonestyTriggers, walkShard.Triggers.Count);

            if (walkShard?.SceneNodesPlaced.Count > 0)
                baseHonesty += string.Format(Strings.U_Au_HonestySceneNodes, walkShard.SceneNodesPlaced.Count);

            if (compiled != null && compiled.Counts.FieldProp > 0)
                baseHonesty += Strings.U_Au_HonestyProps;

            return baseHonesty;
        }

        static Dictionary<string, object?> BuildWalkMarkerPayload(AuroraFieldExplorer_WalkManifest.FieldWalkShard? walkShard)
        {
            AuroraFieldExplorer_EncounterOverlayCompiler.CompiledWalkOverlay compiled =
                AuroraFieldExplorer_EncounterOverlayCompiler.Compile(walkShard);

            var displayList = BuildEntityPayload(compiled.DisplayEntities).ToList();
            var entityList = BuildEntityPayload(compiled.Entities).ToList();

            return new Dictionary<string, object?>
            {
                ["chestMeta"] = new
                {
                    source = "field-scout-walk",
                    count = walkShard?.Chests.Count ?? 0,
                    note = walkShard?.Chests.Count > 0
                        ? Strings.U_Au_ChestNoteFound
                        : Strings.U_Au_ChestNoteEmpty,
                },
                ["chests"] = BuildChestPayload(walkShard).ToList(),
                ["npcMeta"] = new
                {
                    source = "field-scout-walk",
                    count = displayList.Count,
                    rawCount = compiled.Counts.RawTotal,
                    dedupedCount = compiled.Counts.DedupedTotal,
                    note = compiled.Counts.RawTotal > 0
                        ? Strings.U_Au_NpcNoteFound
                        : Strings.U_Au_NpcNoteEmpty,
                },
                ["npcs"] = displayList,
                ["entities"] = entityList,
                ["chrCounts"] = new
                {
                    rawTotal = compiled.Counts.RawTotal,
                    dedupedTotal = compiled.Counts.DedupedTotal,
                    storyNpc = compiled.Counts.StoryNpc,
                    party = compiled.Counts.Party,
                    fieldEnemy = compiled.Counts.FieldEnemy,
                    fieldProp = compiled.Counts.FieldProp,
                    summon = compiled.Counts.Summon,
                    weapon = compiled.Counts.Weapon,
                    unknown = compiled.Counts.Unknown,
                },
                ["triggerMeta"] = new
                {
                    source = "field-scout-walk",
                    count = walkShard?.Triggers.Count ?? 0,
                    note = walkShard?.Triggers.Count > 0
                        ? Strings.U_Au_TriggerNoteFound
                        : Strings.U_Au_TriggerNoteEmpty,
                },
                ["triggers"] = BuildTriggerPayload(walkShard).ToList(),
                ["sceneNodeMeta"] = new
                {
                    source = "field-scout-walk",
                    count = walkShard?.SceneNodesPlaced.Count ?? 0,
                    note = walkShard?.SceneNodesPlaced.Count > 0
                        ? Strings.U_Au_SceneNodeNoteFound
                        : Strings.U_Au_SceneNodeNoteEmpty,
                },
                ["sceneNodes"] = BuildSceneNodePayload(walkShard).ToList(),
            };
        }

        static IEnumerable<object> BuildSceneNodePayload(AuroraFieldExplorer_WalkManifest.FieldWalkShard? walkShard)
        {
            if (walkShard == null)
                yield break;

            foreach (AuroraFieldExplorer_WalkManifest.WalkSceneNode sn in walkShard.SceneNodesPlaced)
            {
                yield return new
                {
                    name = sn.Name,
                    source = sn.Source,
                    index = sn.Index,
                    hasWorld = sn.HasWorld,
                    proxyKind = sn.ProxyKind,
                    x = sn.X,
                    y = sn.Y,
                    z = sn.Z,
                };
            }
        }

        static IEnumerable<object> BuildEntityPayload(IEnumerable<AuroraFieldExplorer_EncounterOverlayCompiler.WalkEntity> entities)
        {
            foreach (AuroraFieldExplorer_EncounterOverlayCompiler.WalkEntity e in entities)
            {
                yield return new
                {
                    name = e.Name,
                    chrId = e.ChrId,
                    chrCategory = e.ChrCategory,
                    layer = e.Layer,
                    confidence = e.Confidence,
                    source = e.Source,
                    sampleCount = e.SampleCount,
                    defaultVisible = e.DefaultVisible,
                    modelUrl = AuroraFieldExplorer_ChrModelResolver.TryResolveModelUrl(e.Name),
                    x = e.X,
                    y = e.Y,
                    z = e.Z,
                };
            }
        }

        static IEnumerable<object> BuildChestPayload(AuroraFieldExplorer_WalkManifest.FieldWalkShard? walkShard)
        {
            if (walkShard == null)
                yield break;

            foreach (AuroraFieldExplorer_WalkManifest.WalkChest c in walkShard.Chests)
            {
                yield return new
                {
                    name = c.Name,
                    source = c.Source,
                    x = c.X,
                    y = c.Y,
                    z = c.Z,
                };
            }
        }

        static IEnumerable<object> BuildNpcPayload(AuroraFieldExplorer_WalkManifest.FieldWalkShard? walkShard)
        {
            AuroraFieldExplorer_EncounterOverlayCompiler.CompiledWalkOverlay compiled =
                AuroraFieldExplorer_EncounterOverlayCompiler.Compile(walkShard);
            foreach (object row in BuildEntityPayload(compiled.DisplayEntities))
                yield return row;
        }

        static IEnumerable<object> BuildTriggerPayload(AuroraFieldExplorer_WalkManifest.FieldWalkShard? walkShard)
        {
            if (walkShard == null)
                yield break;

            foreach (AuroraFieldExplorer_WalkManifest.WalkTrigger t in walkShard.Triggers)
            {
                yield return new
                {
                    name = t.Name,
                    triggerClass = t.TriggerClass,
                    source = t.Source,
                    x = t.X,
                    y = t.Y,
                    z = t.Z,
                };
            }
        }

        static IEnumerable<object> BuildZonePayload(MapoutVpa_EncounterZones.ParseResult zones)
        {
            foreach (MapoutVpa_EncounterZones.EncounterZone z in zones.Zones)
            {
                yield return new
                {
                    entryKey = z.EntryKey,
                    tag = z.Tag,
                    groupIndex = z.GroupIndex,
                    linkConfidence = z.LinkConfidence,
                    bounds = new { minX = z.MinX, maxX = z.MaxX, minZ = z.MinZ, maxZ = z.MaxZ },
                    polygon = z.Polygons.Count > 0
                        ? z.Polygons[0].Vertices.Select(v => new[] { v.X, v.Z })
                        : Array.Empty<float[]>(),
                    polygonCount = z.Polygons.Count,
                };
            }
        }

        static void WriteMiniCatalog(string path, FieldMapRow field, string assetId, string relBase, string gltfFull)
        {
            string gltfRel = $"{relBase}/{Path.GetFileName(gltfFull)}";
            var entry = new Dictionary<string, object?>
            {
                ["area"] = field.MapEntity,
                ["areaKey"] = $"{field.Area}/{field.FieldToken}",
                ["assetId"] = assetId,
                ["primaryAssetLayer"] = "root_3d",
                ["status"] = "success",
                ["decisionBand"] = "aurora_field_explorer_overworld",
                ["textureStatus"] = "decoded_png_bound_by_submesh_candidate",
                ["folder"] = relBase,
                ["gltf"] = gltfRel,
            };

            var catalog = new Dictionary<string, object?>
            {
                ["generatedAtUtc"] = "aurora-field-explorer",
                ["sourceList"] = "field-explorer-per-map",
                ["outputRoot"] = relBase,
                ["totalCount"] = 1,
                ["successCount"] = 1,
                ["entries"] = new[] { entry },
            };

            File.WriteAllText(path, JsonSerializer.Serialize(catalog, JsonOpts));
        }

        static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        sealed class ExportInvocation
        {
            public required string FileName { get; init; }
            public required List<string> Args { get; init; }
            public required string WorkingDir { get; init; }
        }

        static ExportInvocation? BuildExportInvocation(string ps3Root, string areaArg, string outputDir)
        {
            string? exe = Path.Combine(AppContext.BaseDirectory, "tools", "PhyreMapExportLab", "PhyreMapExportLab.exe");
            if (!File.Exists(exe)) exe = null;
#if FFX_INCLUDE_DEVTOOLS
            exe ??= FindUpwards(Path.Combine("RuntimeTools", "PhyreMapExportLab", "bin", "Release", "net8.0", "PhyreMapExportLab.exe"))
                 ?? FindUpwards(Path.Combine("RuntimeTools", "PhyreMapExportLab", "bin", "Debug", "net8.0", "PhyreMapExportLab.exe"));
#endif
            if (exe != null)
            {
                return new ExportInvocation
                {
                    FileName = exe,
                    Args = new List<string> { "export", "--ps3-root", ps3Root, "--area", areaArg, "--output", outputDir },
                    WorkingDir = Path.GetDirectoryName(exe)!,
                };
            }

#if FFX_INCLUDE_DEVTOOLS
            string? csproj = FindUpwards(Path.Combine("RuntimeTools", "PhyreMapExportLab", "PhyreMapExportLab.csproj"));
            if (csproj != null)
            {
                return new ExportInvocation
                {
                    FileName = "dotnet",
                    Args = new List<string> { "run", "--project", csproj, "-c", "Debug", "--",
                        "export", "--ps3-root", ps3Root, "--area", areaArg, "--output", outputDir },
                    WorkingDir = Path.GetDirectoryName(csproj)!,
                };
            }
#endif

            return null;
        }

        static int RunProcess(ExportInvocation inv, out string stdoutTail)
        {
            var psi = new ProcessStartInfo
            {
                FileName = inv.FileName,
                WorkingDirectory = inv.WorkingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (string a in inv.Args)
                psi.ArgumentList.Add(a);

            using var p = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null.");
            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            string stderr = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(180_000))
            {
                try { p.Kill(true); } catch { }
                stdoutTail = "(timeout 180s)";
                return -1;
            }

            string stdout = stdoutTask.GetAwaiter().GetResult();
            string tail = (stdout + "\n" + stderr).Trim();
            stdoutTail = tail.Length > 400 ? "…" + tail[^400..] : tail;
            return p.ExitCode;
        }

#if FFX_INCLUDE_DEVTOOLS
        static string? FindUpwards(string relativePath)
        {
            foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                if (string.IsNullOrEmpty(start))
                    continue;
                DirectoryInfo? dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, relativePath);
                    if (File.Exists(candidate))
                        return candidate;
                    dir = dir.Parent;
                }
            }
            return null;
        }
#endif
    }
}

using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Ai.Sin;
using FFXProjectEditor.FfxLib.BattleMap;
using FFXProjectEditor.FfxLib.Monster;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace FFXProjectEditor.Modules.AuroraChamber
{
    /// <summary>
    /// 🌅 AURORA CHAMBER — Layer-3 render bridge. Consumes the bundled MapViewer and read-only Phyre exporter:
    /// the export exe to turn a BIANCA btlmap scene into a glTF, writes a SELF-CONTAINED per-scene mini-catalog +
    /// actor-anchor JSON next to it, and deep-links the viewer via its existing <c>?catalog=</c>/<c>?map=</c>/
    /// <c>?actors=</c> query overrides — so the shared <c>public/maps/catalog.json</c> is never mutated (no clobber/
    /// race with other chats) and <c>app.js</c> is never touched.
    ///
    /// Strictly read-only on game assets (the export tool itself never repacks); it only WRITES generated viewer
    /// artifacts (glTF + json) under the per-user LocalAppData viewer-data tree.
    /// </summary>
    internal static class AuroraSceneRenderer
    {
        /// <summary>btlmap area subfolder under a MapViewer export output root.</summary>
        public const string BtlmapPrefix = "btlmap";

        /// <summary>Writable generated-viewer root, separated from immutable bundled frontend assets.</summary>
        public static string ViewerDataRoot { get; } = Path.Combine(
            ViewerHubService.ViewerDataRoot,
            "map");

        public sealed class RenderResult
        {
            public required bool Ok { get; init; }
            public required string Message { get; init; }
            public string? GltfFullPath { get; init; }
            public string? DeepLinkUrl { get; init; }
            public int ExitCode { get; init; }
            public string? OutputDir { get; init; }
            public string? ResolvedAreaArg { get; init; }
        }

        /// <summary>True when the Phyre exporter binary AND a ps3data root are both resolvable —
        /// i.e. the MapViewer geometry path can actually produce a glTF on this machine. When false,
        /// the EditViewer falls back to the rendered PS2-native battle stage (noclip) instead of an
        /// empty grid. WHY: the export exe only ships with the runtime-tools payload and ps3data only
        /// exists in a loaded project — dev VMs and minimal installs have neither.</summary>
        internal static bool CanExportSceneGeometry =>
            ResolveExporterExe() != null && ResolvePs3DataRoot() != null;

        /// <summary>The ps3data root that PhyreMapExportLab's <c>--ps3-root</c> expects (NOT the btlmap subdir).
        /// Prefers the loaded project's extracted ps3data; falls back to deriving it from the BIANCA btlmap root.</summary>
        public static string? ResolvePs3DataRoot()
        {
            string? fromProject = Project_Service.Instance.Path_Ps3DataRoot;
            if (!string.IsNullOrWhiteSpace(fromProject) && Directory.Exists(fromProject))
                return fromProject;

            // Derive from the default btlmap root: <...>/ps3data/btlmap -> <...>/ps3data.
            string btlmap = ResolveBtlmapRoot();
            string? parent = Directory.Exists(btlmap) ? Directory.GetParent(btlmap)?.FullName : null;
            return (parent != null && Directory.Exists(parent)) ? parent : null;
        }

        /// <summary>The btlmap root BIANCA scans. Prefers the loaded project's ps3data\btlmap; else the lab default.</summary>
        public static string ResolveBtlmapRoot()
        {
            string? ps3 = Project_Service.Instance.Path_Ps3DataRoot;
            if (!string.IsNullOrWhiteSpace(ps3))
            {
                string candidate = Path.Combine(ps3, BtlmapPrefix);
                if (Directory.Exists(candidate))
                    return candidate;
            }
            return BattleMapCatalog_File.DefaultBtlmapRoot;
        }

        /// <summary>True if a usable primary glTF already exists under the MapViewer output tree.</summary>
        public static bool IsSceneMounted(BattleMap_Scene scene)
        {
            string? outputDir = GetSceneOutputDir(scene);
            if (outputDir == null) return false;
            string assetId = $"{BtlmapPrefix}_{scene.AreaCode}_{scene.SceneId}";
            return BattleMap_ExportQuality.TryResolveBestGltf(outputDir, assetId, out _, out _);
        }

        /// <summary>Texture bind snapshot for a mounted scene (null if never exported).</summary>
        public static BattleMap_ExportQuality? GetSceneExportQuality(BattleMap_Scene scene)
        {
            string? outputDir = GetSceneOutputDir(scene);
            if (outputDir == null) return null;
            string assetId = $"{BtlmapPrefix}_{scene.AreaCode}_{scene.SceneId}";
            return BattleMap_ExportQuality.TryRead(outputDir, assetId);
        }

        /// <summary>Export geometry + mini-catalog for every BIANCA scene that is not yet mounted. Skips map-only
        /// virtual rows and already-cached scenes unless <paramref name="forceReexport"/> is true. Does not open the viewer.</summary>
        public static async Task<MountAllResult> MountAllMissingAsync(bool forceReexport = false, IProgress<string>? progress = null)
        {
            string btlmapRoot = ResolveBtlmapRoot();
            BattleMapCatalog_File catalog = BattleMapCatalog_File.Scan(btlmapRoot);
            var scenes = catalog.Scenes.Where(s => !s.IsMapOnlyVirtual).ToList();

            int ok = 0, fail = 0, skipped = 0;
            var errors = new List<string>();

            for (int i = 0; i < scenes.Count; i++)
            {
                BattleMap_Scene scene = scenes[i];
                progress?.Report($"[{i + 1}/{scenes.Count}] {scene.SceneId}…");

                if (!forceReexport && IsSceneMounted(scene))
                {
                    string? outputDir = GetSceneOutputDir(scene);
                    if (outputDir != null && File.Exists(Path.Combine(outputDir, "aurora-catalog.json")))
                    {
                        skipped++;
                        continue;
                    }
                }

                RenderResult result = await Task.Run(() => ExportSceneGeometry(scene, forceReexport)).ConfigureAwait(false);
                if (result.Ok) ok++;
                else
                {
                    fail++;
                    if (errors.Count < 12)
                        errors.Add($"{scene.SceneId}: {result.Message}");
                }
            }

            return new MountAllResult(scenes.Count, ok, skipped, fail, errors);
        }

        public sealed record MountAllResult(int Total, int Mounted, int Skipped, int Failed, IReadOnlyList<string> Errors);

        /// <summary>Export glTF + mini-catalog only (no HTTP server, no browser deep-link). Used by on-demand render
        /// and batch mount.</summary>
        public static RenderResult ExportSceneGeometry(BattleMap_Scene scene, bool forceReexport)
        {
            ArgumentNullException.ThrowIfNull(scene);

            string? ps3Root = ResolvePs3DataRoot();
            if (ps3Root == null)
            {
                FFXProjectEditor.Diagnostics.DebugLog.Error("Aurora.Export", $"ps3data root nao resolvido (scene={scene.SceneId})");
                return new RenderResult { Ok = false, Message = Strings.U_Au_Ps3DataRootNotResolved };
            }

            string? indexPath = ResolveMapViewerIndex();
            if (indexPath == null)
            {
                FFXProjectEditor.Diagnostics.DebugLog.Error("Aurora.Export", $"MapViewer index.html nao encontrado (scene={scene.SceneId})");
                return new RenderResult { Ok = false, Message = Strings.U_Au_MapViewerIndexNotFound };
            }

            string area = scene.AreaCode;
            string sceneId = scene.SceneId;
            string assetId = $"{BtlmapPrefix}_{area}_{sceneId}";
            string outputDir = Path.Combine(ViewerDataRoot, "maps", BtlmapPrefix, area, sceneId);
            string relBase = $"/viewer-data/map/maps/{BtlmapPrefix}/{area}/{sceneId}";

            /* Determine the actual export target — for IsMapOnlyVirtual or stub scenes, use map/ fallback.
               Also adjust outputDir and assetId so the glTF filenames match what the viewer expects. */
            string effectiveAreaArg;
            string resolvedAssetId;
            string resolvedOutputDir;
            if (scene.IsMapOnlyVirtual || scene.IsStub || !scene.HasPrimaryDae)
            {
                string mapToken = scene.MapKey;
                effectiveAreaArg = $"map/{area}/{mapToken}";
                resolvedAssetId = $"map_{area}_{mapToken}";
                resolvedOutputDir = Path.Combine(ViewerDataRoot, "maps", "map", area, mapToken);
            }
            else
            {
                effectiveAreaArg = $"{BtlmapPrefix}/{area}/{sceneId}";
                resolvedAssetId = assetId;
                resolvedOutputDir = outputDir;
            }

            int exitCode = 0;
            string gltfFull;
            string exportDir = resolvedOutputDir;
            string exportAssetId = resolvedAssetId;
            if (forceReexport || !ResolveExistingGltf(exportDir, exportAssetId, out gltfFull))
            {
                ExportInvocation? inv = BuildExportInvocation(ps3Root, effectiveAreaArg, exportDir);
                if (inv == null)
                    return new RenderResult { Ok = false, Message = Strings.U_Au_PhyreMapExportLabNotFound };

                Directory.CreateDirectory(exportDir);
                try
                {
                    exitCode = RunProcess(inv, out string stdoutTail);
                    if (!ResolveExistingGltf(exportDir, exportAssetId, out gltfFull))
                    {
                        FFXProjectEditor.Diagnostics.DebugLog.Error("Aurora.Export",
                            $"export falhou exit={exitCode} arg={effectiveAreaArg} :: {stdoutTail}");
                        return new RenderResult { Ok = false, ExitCode = exitCode, OutputDir = exportDir,
                            Message = string.Format(Strings.U_Au_ExportFailedExit, exitCode, effectiveAreaArg, stdoutTail) };
                    }
                }
                catch (Exception ex)
                {
                    FFXProjectEditor.Diagnostics.DebugLog.Error("Aurora.Export", "excecao no exporter", ex);
                    return new RenderResult { Ok = false, Message = string.Format(Strings.U_Au_ExporterRunFailed, ex.Message), OutputDir = exportDir };
                }
            }

            relBase = $"/viewer-data/map/maps/{effectiveAreaArg}";
            string gltfRel = $"{relBase}/{Path.GetFileName(gltfFull)}";
            string miniCatalogName = "aurora-catalog.json";
            try
            {
                BattleMap_ExportQuality? quality = GetSceneExportQuality(scene);
                WriteMiniCatalog(Path.Combine(exportDir, miniCatalogName), scene, exportAssetId, area, sceneId, gltfRel, relBase, quality, effectiveAreaArg);
            }
            catch (Exception ex) { return new RenderResult { Ok = false, Message = string.Format(Strings.U_Au_MiniCatalogWriteFailed, ex.Message), OutputDir = exportDir }; }


            return new RenderResult
            {
                Ok = true,
                ExitCode = exitCode,
                GltfFullPath = gltfFull,
                OutputDir = exportDir,
                ResolvedAreaArg = effectiveAreaArg != $"{BtlmapPrefix}/{area}/{sceneId}" ? effectiveAreaArg : null,
                Message = string.Format(Strings.U_Au_SceneMountedGltf, Path.GetFileName(gltfFull)),
            };
        }

        /// <summary>Export the scene to glTF (if not already cached), write the per-scene mini-catalog + the optional
        /// actors overlay JSON, and return the viewer deep-link. <paramref name="anchors"/> may be null/empty (render
        /// without an actor overlay). Pass <paramref name="forceReexport"/> to ignore a cached glTF.</summary>
        public static RenderResult RenderScene(BattleMap_Scene scene, AuroraActorsOverlay? anchors, bool forceReexport)
        {
            ArgumentNullException.ThrowIfNull(scene);

            string? indexPath = ResolveMapViewerIndex();
            if (indexPath == null)
                return new RenderResult { Ok = false, Message = Strings.U_Au_MapViewerIndexNotFound };

            RenderResult geom = ExportSceneGeometry(scene, forceReexport);
            if (!geom.Ok)
            {
                // A battle editor without its arena cannot place actors reliably. Preserve the
                // export failure so the caller can use a proven battle preview or show recovery.
                return geom;
            }

            string areaG = scene.AreaCode;
            string sceneIdG = scene.SceneId;
            string outputDir = geom.OutputDir!;
            string relBase = geom.ResolvedAreaArg is not null
                ? $"/viewer-data/map/maps/{geom.ResolvedAreaArg}"
                : $"/viewer-data/map/maps/{BtlmapPrefix}/{areaG}/{sceneIdG}";
            string miniCatalogRel = $"{relBase}/aurora-catalog.json";
            string gltfFull = geom.GltfFullPath!;
            string gltfRel = $"{relBase}/{Path.GetFileName(gltfFull)}";
            string? actorsRel = null;
            if (anchors != null && anchors.Anchors.Count > 0)
            {
                string actorsName = "aurora-actors.json";
                try
                {
                    File.WriteAllText(Path.Combine(outputDir, actorsName),
                        JsonSerializer.Serialize(anchors, JsonOpts));
                    actorsRel = $"{relBase}/{actorsName}";
                }
                catch { /* overlay is best-effort; render still proceeds */ }
            }

            // The in-process ViewerHub serves immutable frontend assets and the generated data root on one
            // dynamic loopback port. No Python, repository root, fixed port, or external browser is required.
            string token = geom.ResolvedAreaArg ?? $"{BtlmapPrefix}/{areaG}/{sceneIdG}";
            string query = $"catalog={Uri.EscapeDataString(miniCatalogRel)}" +
                           $"&map={Uri.EscapeDataString(token)}" +
                           "&aurora=1";
            if (actorsRel != null)
                query += $"&actors={Uri.EscapeDataString(actorsRel)}";
            // forExternalBrowser: o DeepLinkUrl abre no navegador do sistema fora do Windows.
            string? url = ViewerHubService.BuildUrl("map-scene-editor", query, forExternalBrowser: true);
            if (url == null)
                return new RenderResult { Ok = false, Message = ViewerHubService.StatusText };

            BattleMap_ExportQuality? quality = GetSceneExportQuality(scene);
            string qualityNote = quality?.QualityLabel is { Length: > 0 } ql ? $" · {ql}" : "";

            return new RenderResult
            {
                Ok = true,
                ExitCode = geom.ExitCode,
                GltfFullPath = gltfFull,
                OutputDir = outputDir,
                DeepLinkUrl = url,
                Message = string.Format(Strings.U_Au_SceneReady, Path.GetFileName(gltfFull), qualityNote)
                          + (actorsRel != null ? string.Format(Strings.U_Au_AnchorsCountSuffix, anchors!.Anchors.Count) : ""),
            };
        }

        /// <summary>Open a ViewerHub deep-link in the default browser when explicitly requested.</summary>
        public static string Launch(string url)
        {
            return Modules.Common.ViewerShell.ExternalBrowserLauncher.Open(url)
                ? Strings.U_Au_MapViewerOpened
                : string.Format(Strings.U_Au_MapViewerOpenFailed, "no browser found");
        }

        /******************************************
         * Internals
         ******************************************/

        /// <summary>Probes the in-process ViewerHub endpoint used by the embedded MapViewer.</summary>
        public static string? ProbeViewerHttp(string? indexPath = null)
        {
            indexPath ??= ResolveMapViewerIndex();
            if (indexPath == null) return Strings.U_Au_MapViewerIndexHtmlNotFound;
            try
            {
                // forExternalBrowser: o probe valida o mesmo endpoint que o navegador externo abre.
                string? url = ViewerHubService.BuildUrl("map-scene-editor", forExternalBrowser: true);
                if (url == null) return ViewerHubService.StatusText;
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                using var response = client.GetAsync(url).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                    return string.Format(Strings.U_Au_MapViewerProbeHttpError, ViewerHubService.Server?.Port ?? 0, (int)response.StatusCode);
                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!body.Contains("FFXMapViewerWeb", StringComparison.OrdinalIgnoreCase)
                    && !body.Contains("ffxMapViewerDebug", StringComparison.OrdinalIgnoreCase)
                    && !body.Contains("app.js", StringComparison.Ordinal))
                    return string.Format(Strings.U_Au_PortOccupiedByOtherServer, ViewerHubService.Server?.Port ?? 0);
            }
            catch (Exception ex)
            {
                return string.Format(Strings.U_Au_MapViewerNotResponding, ViewerHubService.Server?.Port ?? 0, ex.Message);
            }

            return null;
        }

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        private sealed class ExportInvocation
        {
            public required string FileName { get; init; }
            public required List<string> Args { get; init; }
            public required string WorkingDir { get; init; }
        }

        private static ExportInvocation? BuildExportInvocation(string ps3Root, string areaArg, string outputDir)
        {
            string? exe = ResolveExporterExe();
            if (exe != null)
            {
                return new ExportInvocation
                {
                    FileName = exe,
                    Args = new List<string> { "export", "--ps3-root", ps3Root, "--area", areaArg, "--output", outputDir, "--portable" },
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
                                              "export", "--ps3-root", ps3Root, "--area", areaArg, "--output", outputDir, "--portable" },
                    WorkingDir = Path.GetDirectoryName(csproj)!,
                };
            }
#endif

            return null;
        }

        private static int RunProcess(ExportInvocation inv, out string stdoutTail)
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
            foreach (string a in inv.Args) psi.ArgumentList.Add(a);

            using var p = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null.");
            // Drain both pipes CONCURRENTLY to avoid the classic redirected-pipe deadlock: 'dotnet run' is verbose on
            // BOTH stdout and stderr, and reading one to end before the other can wedge if the child fills the other
            // pipe's OS buffer. Read stdout async while draining stderr, then join.
            System.Threading.Tasks.Task<string> stdoutTask = p.StandardOutput.ReadToEndAsync();
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

        /// <summary>True if a usable primary glTF already exists; sets <paramref name="gltfFull"/> to the best one.
        /// Prefers <see cref="BattleMap_ExportQuality.PreferredGltfSuffix"/> (vertex-color) so unbound Phyre shader
        /// surfaces use baked per-vertex Color instead of debug pastels.</summary>
        private static bool ResolveExistingGltf(string outputDir, string assetId, out string gltfFull)
            => BattleMap_ExportQuality.TryResolveBestGltf(outputDir, assetId, out gltfFull, out _);

        private static void WriteMiniCatalog(
            string path, BattleMap_Scene scene, string assetId, string area, string sceneId, string gltfRel, string relBase,
            BattleMap_ExportQuality? quality = null, string? areaToken = null)
        {
            string textureStatus = quality?.SubmeshCount > 0
                ? $"aurora_vertex_color_gltf · {quality.BoundSubmeshCount}/{quality.SubmeshCount} submeshes bound"
                : scene.TextureCount > 0
                    ? "decoded_png_bound_by_submesh_candidate"
                    : "texture_not_requested";

            var entry = new Dictionary<string, object?>
            {
                ["area"] = areaToken ?? $"{BtlmapPrefix}/{area}/{sceneId}",
                ["areaKey"] = $"{area}/{sceneId}",
                ["assetId"] = assetId,
                ["primaryAssetLayer"] = "root_3d",
                ["status"] = "success",
                ["decisionBand"] = "aurora_chamber_btlmap_scene",
                ["textureStatus"] = textureStatus,
                ["textureCount"] = scene.TextureCount,
                ["triangleCount"] = 0,
                ["vertexCount"] = 0,
                ["folder"] = relBase,
                ["gltf"] = gltfRel,
                ["manifest"] = $"{relBase}/{assetId}.map-export-manifest.json",
                ["report"] = $"{relBase}/{assetId}.static-export-report.json",
                ["textureBindingReport"] = $"{relBase}/{assetId}.texture-binding-report.json",
                ["materialSlotAnalysisReport"] = $"{relBase}/{assetId}.material-slot-analysis.json",
                ["failedGates"] = Array.Empty<string>(),
            };

            var catalog = new Dictionary<string, object?>
            {
                ["generatedAtUtc"] = "aurora-chamber",
                ["sourceList"] = "aurora-chamber-per-scene",
                ["outputRoot"] = relBase,
                ["totalCount"] = 1,
                ["successCount"] = 1,
                ["blockedCount"] = 0,
                ["entries"] = new[] { entry },
            };

            File.WriteAllText(path, JsonSerializer.Serialize(catalog, JsonOpts));
        }

        public static string? ResolveMapViewerIndex()
        {
            string? root = ViewerHubService.ResolveBundledViewerRoot("map");
            return root == null ? null : Path.Combine(root, "index.html");
        }

        /// <summary>The MapViewer output dir for a scene (created lazily by callers). Null if the viewer is absent.</summary>
        public static string? GetSceneOutputDir(BattleMap_Scene scene)
        {
            if (ResolveMapViewerIndex() == null) return null;
            return Path.Combine(ViewerDataRoot, "maps", BtlmapPrefix, scene.AreaCode, scene.SceneId);
        }

        /// <summary>Writable product-data root used for durable generated viewer artifacts.</summary>
        public static string? ResolveRepoRoot()
        {
            Directory.CreateDirectory(ViewerDataRoot);
            return ViewerDataRoot;
        }

        /// <summary>Serialize an actors overlay to a path (camelCase, indented). Returns the path on success.</summary>
        public static string WriteActorsOverlay(string path, AuroraActorsOverlay overlay)
        {
            string? d = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(d)) Directory.CreateDirectory(d);
            File.WriteAllText(path, JsonSerializer.Serialize(overlay, JsonOpts));
            return path;
        }


        private static string? ResolveExporterExe()
        {
            string bundled = Path.Combine(AppContext.BaseDirectory, "tools", "PhyreMapExportLab", "PhyreMapExportLab.exe");
            if (File.Exists(bundled)) return bundled;
#if FFX_INCLUDE_DEVTOOLS
            foreach (string config in new[] { "Debug", "Release" })
            {
                string rel = Path.Combine("RuntimeTools", "PhyreMapExportLab", "bin", config, "net8.0", "PhyreMapExportLab.exe");
                string? found = FindUpwards(rel);
                if (found != null) return found;
            }
#endif
            return null;
        }

#if FFX_INCLUDE_DEVTOOLS
        private static string? FindUpwards(string relativePath)
        {
            foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                if (string.IsNullOrEmpty(start)) continue;
                DirectoryInfo? dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, relativePath);
                    if (File.Exists(candidate)) return candidate;
                    dir = dir.Parent;
                }
            }
            return null;
        }
#endif

        static Dictionary<int, bool>? _monsterFlipMap;
        static void EnsureMonsterFlipMap()
        {
            if (_monsterFlipMap != null) return;
            _monsterFlipMap = new();
            try
            {
                string mapPath = Path.Combine(
                    Project_Service.Instance?.ProjectPath ?? ".",
                    "work", "monster_flip.json");
                if (File.Exists(mapPath))
                {
                    string json = File.ReadAllText(mapPath);
                    var raw = JsonSerializer.Deserialize<Dictionary<string, bool>>(json);
                    if (raw != null)
                        foreach (var kv in raw)
                            if (int.TryParse(kv.Key, out int id))
                                _monsterFlipMap[id] = kv.Value;
                }
            }
            catch { }
        }
        /// <summary>Returns true if the monster's .glb was exported Y-down and needs rotation.x = π.</summary>
        internal static bool GetMonsterFlipX(int monsterId)
        {
            EnsureMonsterFlipMap();
            return _monsterFlipMap!.TryGetValue(monsterId, out bool flip) && flip;
        }

        /// <summary>Extract visual scale from a monster's ATEL scaleOwnSize (0x7028) call.
        /// Returns uniform scale factor (1.0 = raw .glb size) or null if not found.</summary>
        internal static float? GetMonsterScale(int monsterId)
        {
            try
            {
                Project_Service? svc = Project_Service.Instance;
                if (svc == null) return null;
                string monPath = svc.GetPathMon(monsterId);
                if (!File.Exists(monPath)) return null;
                byte[] bin = File.ReadAllBytes(monPath);
                Monster_File mon = Monster_File.Read(bin);
                if (mon.AiFile == null || mon.AiFile.Length < 0x30) return null;
                FfxLib.Ai.AiScriptFile script = FfxLib.Ai.AiScript_File.Read(mon.AiFile);
                if (script == null) return null;

                const byte PUSHF = 0xAF, PUSHII = 0xAE, CALLPOPA = 0xD8;
                const ushort ScaleOwnSize = 0x7028;
                IReadOnlyList<AiInstruction> instrs = script.Instructions;
                float? bestScale = null;

                if (script.FloatPoolOffset < 0)
                    return null;
                byte[] rawAi = script.OriginalAiFileBytes;

                for (int i = 3; i < instrs.Count; i++)
                {
                    if (instrs[i].Opcode != CALLPOPA || instrs[i].Operand != ScaleOwnSize)
                        continue;
                    float[] vals = new float[3];
                    bool ok = true;
                    for (int j = 0; j < 3; j++)
                    {
                        AiInstruction pi = instrs[i - 3 + j];
                        if (pi.Opcode == PUSHF)
                        {
                            int off = script.FloatPoolOffset + 4 * (int)pi.Operand;
                            vals[j] = off + 4 <= rawAi.Length
                                ? BitConverter.ToSingle(rawAi, off) : 0;
                        }
                        else if (pi.Opcode == PUSHII)
                            vals[j] = (short)pi.Operand;
                        else { ok = false; break; }
                    }
                    if (!ok) continue;
                    float s = Math.Max(Math.Abs(vals[0]), Math.Max(Math.Abs(vals[1]), Math.Abs(vals[2])));
                    if (s < 0.001f) continue;
                    if (bestScale == null || s > bestScale) bestScale = s;
                }
                return bestScale;
            }
            catch { return null; }
        }
    }

    /// <summary>Serializable actor-anchor overlay consumed by aurora-overlay.js. Battle-local, UNCALIBRATED coords.</summary>
    internal sealed class AuroraActorsOverlay
    {
        public string Scene { get; set; } = "";
        public string MapKey { get; set; } = "";
        public string BattleId { get; set; } = "";
        public string CoordSpace { get; set; } = "scene_world_identity_ida_proven";
        public string Honesty { get; set; } =
            "Battle→scene transform = IDENTITY (IDA-proven: the battle engine copies chunk3 coords verbatim into the " +
            "actor's world node — no flip-Z/scale/rotation; only Y gains a per-actor model-height). Anchors are plotted " +
            "directly in scene coordinates. See docs/reverse/FFX_AURORA_BATTLE_TO_SCENE_TRANSFORM_IDA_PROVEN_2026-06-05.md. " +
            "Live (probe) confirmation of the exact Y term is pending the game being open.";
        public List<AuroraActorAnchor> Anchors { get; set; } = new();
        /// <summary>Texture bind snapshot for the overlay panel (from material-slot-analysis.json).</summary>
        public AuroraExportQualitySnapshot? ExportQuality { get; set; }
        /// <summary>Best-effort establishing-camera marker (eye + look-at ref + line). Null when none. camSetPolar
        /// 0x6004 uses the IDA-proven polar convention; target-aware camSetBtlPolar* stays out of this marker.</summary>
        public AuroraCameraMarker? Camera { get; set; }
        /// <summary>PS2 field encounter zones (mapout.vpa MAP1) for the scene's map key. Null = vpa missing/unparsed.
        /// Field-local coordinates — the overlay draws them on the debug plane, never claims scene alignment.</summary>
        public List<AuroraEncounterZoneSnapshot>? Zones { get; set; }
        /// <summary>Human-readable note about the zones source (path + status). Empty when no vpa attempted.</summary>
        public string ZonesNote { get; set; } = "";
    }

    /// <summary>A 3D camera marker: the look-at REF (exact, scene frame) + a camSetPolar eye (IDA-proven) + a line.</summary>
    internal sealed class AuroraCameraMarker
    {
        public bool HasRef { get; set; }
        public float RefX { get; set; }
        public float RefY { get; set; }
        public float RefZ { get; set; }
        public bool HasEye { get; set; }
        public float EyeX { get; set; }
        public float EyeY { get; set; }
        public float EyeZ { get; set; }
        public bool EyeDraggable { get; set; }
        public float Angle { get; set; }
        public float Distance { get; set; }
        public string Label { get; set; } = "";
    }

    /// <summary>A single encounter-zone polygon from the PS2 field <c>mapout.vpa</c> (MAP1), snapped to the overlay
    /// JSON so the viewer can draw it. Coordinates are PS2 field-local (X/Z, already /256) — NOT the HD btlmap
    /// scene frame; the overlay draws them on the y=0 debug plane with an honest "PS2 field" badge.</summary>
    internal sealed class AuroraEncounterZoneSnapshot
    {
        public int EntryKey { get; set; }
        public int Tag { get; set; }
        public int? GroupIndex { get; set; }
        public float MinX { get; set; }
        public float MaxX { get; set; }
        public float MinZ { get; set; }
        public float MaxZ { get; set; }
        public List<AuroraZonePolygonSnapshot> Polygons { get; set; } = new();
    }

    internal sealed class AuroraZonePolygonSnapshot
    {
        public List<float> Xs { get; set; } = new();
        public List<float> Zs { get; set; } = new();
    }

    internal sealed class AuroraActorAnchor
    {
        public string Role { get; set; } = "";
        public int Index { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float W { get; set; }
        public string Label { get; set; } = "";

        // Monster model binding (monster_live anchors): the formation lineup (chunk2) slot i drops into anchor i.
        // Model is a server-root-absolute URL the MapViewer overlay can GLTFLoader.load() at (X,Y,Z). null = no model.
        public int MonsterId { get; set; } = -1;
        public string? MonsterName { get; set; }
        public string? Model { get; set; }
        /// <summary>Fator de escala visual (PPP SclMove). 1.0 = raw .glb. O jogo aplica escala via efeitos PPP no runtime;
        /// sem isso o boneco aparece muito maior/menor que no jogo. Popule com o fator conhecido ou 1.0 como fallback.</summary>
        public float Scale { get; set; } = 1.0f;
        /// <summary>Apply individual X-flip (rotation.x = π) for models with baked Y-down orientation.
        /// Some .glb exports use Y-down world-space while others use Y-up; the per-monster flag fixes
        /// upside-down monsters without the old blunt group-level rotation.</summary>
        public bool FlipX { get; set; }
        /// <summary>Named animation cycles for this monster (from the .ath catalog — see
        /// <see cref="FfxLib.Monster.MonsterAthAnimCatalog"/>). The overlay uses them for the anim dropdown and
        /// clip matching. Empty = no imported table for this monster.</summary>
        public List<AuroraAnimEntry> Anims { get; set; } = new();
    }

    /// <summary>A named animation cycle from the .ath catalog (game symbol name + numeric id).</summary>
    internal sealed class AuroraAnimEntry
    {
        public string Name { get; set; } = "";
        public int Id { get; set; }
    }

    internal sealed class AuroraExportQualitySnapshot
    {
        public int SubmeshCount { get; set; }
        public int BoundSubmeshCount { get; set; }
        public string Label { get; set; } = "";
        public string Breakdown { get; set; } = "";
    }
}

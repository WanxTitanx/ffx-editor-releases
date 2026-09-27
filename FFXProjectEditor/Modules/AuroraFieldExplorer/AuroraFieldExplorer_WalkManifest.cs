using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FFXProjectEditor.Modules.AuroraFieldExplorer
{
    /// <summary>
    /// Field Scout walk data promoted via <c>publish-scout-to-editor.ps1</c>.
    /// Heavy lab output lives under <c>work/field_scout/</c> (gitignored); only this folder ships with the editor.
    /// </summary>
    internal static class AuroraFieldExplorer_WalkManifest
    {
        public const string BundleFileName = "walk-bundle.json";
        public const string ManifestFolderName = "WalkManifest";
        public const string FieldsSubfolder = "fields";

        public sealed class WalkBundle
        {
            public int FieldCount { get; init; }
            public HashSet<string> WalkedAreaPaths { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        }

        public sealed class WalkChest
        {
            public required string Name { get; init; }
            public required string Source { get; init; }
            public required float X { get; init; }
            public required float Y { get; init; }
            public required float Z { get; init; }
        }

        public sealed class WalkNpc
        {
            public required string Name { get; init; }
            public required string Source { get; init; }
            public uint ChrId { get; init; }
            public required float X { get; init; }
            public required float Y { get; init; }
            public required float Z { get; init; }
        }

        public sealed class WalkChr
        {
            public required string Name { get; init; }
            public required string Source { get; init; }
            public uint ChrId { get; init; }
            public required float X { get; init; }
            public required float Y { get; init; }
            public required float Z { get; init; }
        }

        public sealed class WalkTrigger
        {
            public required string Name { get; init; }
            public required string Source { get; init; }
            public string TriggerClass { get; init; } = "heuristic";
            public required float X { get; init; }
            public required float Y { get; init; }
            public required float Z { get; init; }
        }

        public sealed class WalkSceneNode
        {
            public required string Name { get; init; }
            public required string Source { get; init; }
            public int Index { get; init; } = -1;
            public bool HasWorld { get; init; } = true;
            public required float X { get; init; }
            public required float Y { get; init; }
            public required float Z { get; init; }
            public string ProxyKind { get; init; } = "marker";
        }

        public sealed class FieldWalkShard
        {
            public required string AreaPath { get; init; }
            public IReadOnlyList<WalkChest> Chests { get; init; } = Array.Empty<WalkChest>();
            public IReadOnlyList<WalkNpc> Npcs { get; init; } = Array.Empty<WalkNpc>();
            public IReadOnlyList<WalkChr> ChrSpawns { get; init; } = Array.Empty<WalkChr>();
            public IReadOnlyList<WalkTrigger> Triggers { get; init; } = Array.Empty<WalkTrigger>();
            public IReadOnlyList<WalkSceneNode> SceneNodesPlaced { get; init; } = Array.Empty<WalkSceneNode>();
        }

        public static WalkBundle? TryLoad()
        {
            string? dir = ResolveManifestDirectory();
            if (dir == null)
                return null;

            string bundlePath = Path.Combine(dir, BundleFileName);
            if (!File.Exists(bundlePath))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(bundlePath));
                JsonElement root = doc.RootElement;
                if (root.TryGetProperty("fieldCount", out JsonElement fc) && fc.GetInt32() <= 0)
                    return null;

                var walked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("fields", out JsonElement fields) && fields.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement entry in fields.EnumerateArray())
                    {
                        if (entry.TryGetProperty("areaPath", out JsonElement ap))
                        {
                            string? path = ap.GetString();
                            if (!string.IsNullOrWhiteSpace(path))
                                walked.Add(path);
                        }
                    }
                }

                int count = root.TryGetProperty("fieldCount", out JsonElement c) ? c.GetInt32() : walked.Count;
                if (walked.Count == 0)
                    return null;

                return new WalkBundle { FieldCount = count, WalkedAreaPaths = walked };
            }
            catch
            {
                return null;
            }
        }

        public static FieldWalkShard? TryLoadFieldShard(FieldMapRow field)
        {
            string? dir = ResolveManifestDirectory();
            if (dir == null)
                return null;

            string areaPath = $"{AuroraFieldExplorer_FieldRenderer.MapPrefix}/{field.Area}/{field.FieldToken}";
            string shardPath = Path.Combine(dir, FieldsSubfolder, $"{field.Area}_{field.FieldToken}.json");
            if (!File.Exists(shardPath))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(shardPath));
                JsonElement root = doc.RootElement;
                var chests = new List<WalkChest>();
                var npcs = new List<WalkNpc>();
                var chrSpawns = new List<WalkChr>();
                var triggers = new List<WalkTrigger>();
                var sceneNodes = new List<WalkSceneNode>();

                if (root.TryGetProperty("chestSpawns", out JsonElement chestArr) &&
                    chestArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement c in chestArr.EnumerateArray())
                    {
                        string name = c.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "";
                        if (string.IsNullOrWhiteSpace(name) &&
                            c.TryGetProperty("path", out JsonElement p))
                            name = p.GetString() ?? "";

                        float x = ReadFloat(c, "x", "wx");
                        float y = ReadFloat(c, "y", "wy");
                        float z = ReadFloat(c, "z", "wz");
                        if (string.IsNullOrWhiteSpace(name))
                            continue;

                        string source = c.TryGetProperty("source", out JsonElement s) ? s.GetString() ?? "walk" : "walk";
                        chests.Add(new WalkChest
                        {
                            Name = name,
                            Source = source,
                            X = x,
                            Y = y,
                            Z = z,
                        });
                    }
                }

                if (root.TryGetProperty("npcSpawns", out JsonElement npcArr) &&
                    npcArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement n in npcArr.EnumerateArray())
                    {
                        if (n.ValueKind != JsonValueKind.Object)
                            continue;

                        string name = n.TryGetProperty("chrName", out JsonElement cn) ? cn.GetString() ?? "" : "";
                        if (string.IsNullOrWhiteSpace(name) &&
                            n.TryGetProperty("name", out JsonElement nn))
                            name = nn.GetString() ?? "";

                        uint chrId = ReadChrId(n);
                        float x = ReadFloat(n, "x", "wx");
                        float y = ReadFloat(n, "y", "wy");
                        float z = ReadFloat(n, "z", "wz");
                        if (string.IsNullOrWhiteSpace(name) && chrId == 0)
                            continue;

                        string source = n.TryGetProperty("source", out JsonElement s) ? s.GetString() ?? "walk" : "walk";
                        npcs.Add(new WalkNpc
                        {
                            Name = name,
                            Source = source,
                            ChrId = chrId,
                            X = x,
                            Y = y,
                            Z = z,
                        });
                    }
                }

                if (root.TryGetProperty("chrSpawns", out JsonElement chrArr) &&
                    chrArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement c in chrArr.EnumerateArray())
                    {
                        if (c.ValueKind != JsonValueKind.Object)
                            continue;

                        string name = c.TryGetProperty("chrName", out JsonElement cn) ? cn.GetString() ?? "" : "";
                        if (string.IsNullOrWhiteSpace(name) &&
                            c.TryGetProperty("name", out JsonElement nn))
                            name = nn.GetString() ?? "";

                        uint chrId = ReadChrId(c);
                        float x = ReadFloat(c, "x", "wx");
                        float y = ReadFloat(c, "y", "wy");
                        float z = ReadFloat(c, "z", "wz");
                        if (string.IsNullOrWhiteSpace(name) && chrId == 0)
                            continue;

                        string source = c.TryGetProperty("source", out JsonElement s) ? s.GetString() ?? "walk" : "walk";
                        chrSpawns.Add(new WalkChr
                        {
                            Name = name,
                            Source = source,
                            ChrId = chrId,
                            X = x,
                            Y = y,
                            Z = z,
                        });
                    }
                }

                if (root.TryGetProperty("triggerSpawns", out JsonElement trgArr) &&
                    trgArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement t in trgArr.EnumerateArray())
                    {
                        string name = t.TryGetProperty("name", out JsonElement tn) ? tn.GetString() ?? "" : "";
                        float x = ReadFloat(t, "x", "wx");
                        float y = ReadFloat(t, "y", "wy");
                        float z = ReadFloat(t, "z", "wz");
                        if (string.IsNullOrWhiteSpace(name))
                            continue;

                        string source = t.TryGetProperty("source", out JsonElement s) ? s.GetString() ?? "walk" : "walk";
                        string triggerClass = t.TryGetProperty("triggerClass", out JsonElement tc) ? tc.GetString() ?? "heuristic" : "heuristic";
                        triggers.Add(new WalkTrigger
                        {
                            Name = name,
                            Source = source,
                            TriggerClass = triggerClass,
                            X = x,
                            Y = y,
                            Z = z,
                        });
                    }
                }

                if (root.TryGetProperty("sceneNodesPlaced", out JsonElement sceneArr) &&
                    sceneArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement sn in sceneArr.EnumerateArray())
                    {
                        if (sn.ValueKind != JsonValueKind.Object)
                            continue;

                        string name = sn.TryGetProperty("path", out JsonElement pn) ? pn.GetString() ?? "" : "";
                        if (string.IsNullOrWhiteSpace(name) &&
                            sn.TryGetProperty("name", out JsonElement nn))
                            name = nn.GetString() ?? "";

                        float x = ReadFloat(sn, "x", "wx");
                        float y = ReadFloat(sn, "y", "wy");
                        float z = ReadFloat(sn, "z", "wz");
                        if (string.IsNullOrWhiteSpace(name))
                            continue;

                        string source = sn.TryGetProperty("source", out JsonElement s) ? s.GetString() ?? "walk" : "walk";
                        int index = sn.TryGetProperty("index", out JsonElement ix) && ix.TryGetInt32(out int idx) ? idx : -1;
                        bool hasWorld = !sn.TryGetProperty("hasWorld", out JsonElement hw) || hw.ValueKind != JsonValueKind.False;
                        sceneNodes.Add(new WalkSceneNode
                        {
                            Name = name,
                            Source = source,
                            Index = index,
                            HasWorld = hasWorld,
                            X = x,
                            Y = y,
                            Z = z,
                            ProxyKind = InferSceneNodeProxyKind(name),
                        });
                    }
                }

                return new FieldWalkShard
                {
                    AreaPath = areaPath,
                    Chests = chests,
                    Npcs = npcs,
                    ChrSpawns = chrSpawns,
                    Triggers = triggers,
                    SceneNodesPlaced = sceneNodes,
                };
            }
            catch
            {
                return null;
            }
        }

        static float ReadFloat(JsonElement el, string primary, string fallback)
        {
            if (el.TryGetProperty(primary, out JsonElement v) && v.TryGetSingle(out float f))
                return f;
            if (el.TryGetProperty(fallback, out JsonElement v2) && v2.TryGetSingle(out float f2))
                return f2;
            return 0f;
        }

        static string InferSceneNodeProxyKind(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "marker";

            ReadOnlySpan<char> n = name.AsSpan().Trim();
            if (n.Length >= 2 && (n[0] == 'f' || n[0] == 'F') && char.IsDigit(n[1]))
                return "building";

            string lower = name.ToLowerInvariant();
            string[] buildingHints =
            {
                "shop", "loja", "cabana", "hut", "house", "tent", "store", "building",
                "mdl", "prop", "stall", "vendor", "inn", "house", "shack",
            };

            foreach (string hint in buildingHints)
            {
                if (lower.Contains(hint, StringComparison.Ordinal))
                    return "building";
            }

            return "marker";
        }

        static uint ReadChrId(JsonElement el)
        {
            if (!el.TryGetProperty("chrId", out JsonElement cid))
                return 0;
            if (cid.TryGetUInt32(out uint u))
                return u;
            if (cid.TryGetInt32(out int i) && i >= 0)
                return (uint)i;
            if (cid.TryGetInt64(out long l) && l >= 0 && l <= uint.MaxValue)
                return (uint)l;
            return 0;
        }

        public static bool IsFieldWalked(WalkBundle? bundle, FieldMapRow field)
        {
            if (bundle == null)
                return false;
            string key = $"{AuroraFieldExplorer_FieldRenderer.MapPrefix}/{field.Area}/{field.FieldToken}";
            return bundle.WalkedAreaPaths.Contains(key);
        }

        public static string? ResolveManifestDirectory()
        {
            var candidates = new List<string>();

            string copied = Path.Combine(AppContext.BaseDirectory, "AuroraFieldExplorer", ManifestFolderName);
            if (Directory.Exists(copied))
                candidates.Add(copied);

            foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                if (string.IsNullOrEmpty(start))
                    continue;
                DirectoryInfo? dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    string candidate = Path.Combine(
                        dir.FullName,
                        "FFXProjectEditor",
                        "Modules",
                        "AuroraFieldExplorer",
                        ManifestFolderName);
                    if (Directory.Exists(candidate) && !candidates.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                        candidates.Add(candidate);
                    dir = dir.Parent;
                }
            }

            return candidates
                .OrderByDescending(CountFieldShards)
                .ThenByDescending(GetBundleWriteTimeUtc)
                .FirstOrDefault();
        }

        static int CountFieldShards(string dir)
        {
            string fields = Path.Combine(dir, FieldsSubfolder);
            return Directory.Exists(fields)
                ? Directory.GetFiles(fields, "*.json").Length
                : 0;
        }

        static DateTime GetBundleWriteTimeUtc(string dir)
        {
            string bundle = Path.Combine(dir, BundleFileName);
            return File.Exists(bundle) ? File.GetLastWriteTimeUtc(bundle) : DateTime.MinValue;
        }
    }
}


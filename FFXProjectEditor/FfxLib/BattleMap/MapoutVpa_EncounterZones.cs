using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FFXProjectEditor.FfxLib.BattleMap
{
    /// <summary>Read-only decode of encounter trigger polygons from PS2 <c>mapout.vpa</c> (MAP1).</summary>
    public sealed class MapoutVpa_EncounterZones
    {
        public const int HeaderSize = 0x80;
        public const int MetaBlockHeaderOffset = 0x38;
        public const int GeometryBlockHeaderOffset = 0x18;
        public const ushort PolygonSentinelA = 0x0080;
        public const short PolygonSentinelB = -2;
        public const float DefaultCoordScale = 1f / 256f;

        public enum MapoutZoneStatus
        {
            Ok,
            MissingFile,
            Stub,
            InvalidMagic,
            NoMetaBlock,
            NoDispatchTable,
            NoZonesParsed,
        }

        public sealed class ZonePolygon
        {
            public required IReadOnlyList<(float X, float Z)> Vertices { get; init; }
            public required float MinX { get; init; }
            public required float MaxX { get; init; }
            public required float MinZ { get; init; }
            public required float MaxZ { get; init; }
        }

        public sealed class EncounterZone
        {
            public required int EntryKey { get; init; }
            public required int Tag { get; init; }
            public int? GroupIndex { get; init; }
            public required string LinkConfidence { get; init; }
            public required IReadOnlyList<ZonePolygon> Polygons { get; init; }
            public required float MinX { get; init; }
            public required float MaxX { get; init; }
            public required float MinZ { get; init; }
            public required float MaxZ { get; init; }
        }

        public sealed class ParseResult
        {
            public required MapoutZoneStatus Status { get; init; }
            public required string Family { get; init; }
            public required int FileSize { get; init; }
            public required float CoordScale { get; init; }
            public required IReadOnlyList<EncounterZone> Zones { get; init; }
            public string? Note { get; init; }
            public int MetaBlockOffset { get; init; }
            public int GeometryBlockOffset { get; init; }
        }

        public static ParseResult ParseFile(string mapoutPath, int? btlGroupCount = null)
        {
            if (!File.Exists(mapoutPath))
            {
                return Empty(MapoutZoneStatus.MissingFile, family: "missing", note: mapoutPath);
            }

            byte[] bytes = File.ReadAllBytes(mapoutPath);
            return ParseBytes(bytes, btlGroupCount, mapoutPath);
        }

        public static ParseResult ParseBytes(byte[] bytes, int? btlGroupCount = null, string? sourceHint = null)
        {
            if (bytes.Length < 4)
                return Empty(MapoutZoneStatus.InvalidMagic, "short", note: "file too small");

            string magic = Encoding.ASCII.GetString(bytes, 0, 4);
            if (!magic.Equals("MAP1", StringComparison.Ordinal))
                return Empty(MapoutZoneStatus.InvalidMagic, magic, bytes.Length, note: sourceHint);

            if (bytes.Length <= 128)
            {
                return new ParseResult
                {
                    Status = MapoutZoneStatus.Stub,
                    Family = ClassifyFamily(bytes),
                    FileSize = bytes.Length,
                    CoordScale = DefaultCoordScale,
                    Zones = Array.Empty<EncounterZone>(),
                    Note = "MAP1 stub (no spatial payload)",
                    MetaBlockOffset = 0,
                    GeometryBlockOffset = 0,
                };
            }

            if (bytes.Length < HeaderSize)
                return Empty(MapoutZoneStatus.InvalidMagic, "MAP1", bytes.Length, note: "truncated header");

            int metaOff = ReadU32(bytes, MetaBlockHeaderOffset);
            int geomOff = ReadU32(bytes, GeometryBlockHeaderOffset);
            if (metaOff <= 0 || metaOff >= bytes.Length || geomOff <= 0 || geomOff >= bytes.Length)
            {
                return new ParseResult
                {
                    Status = MapoutZoneStatus.NoMetaBlock,
                    Family = ClassifyFamily(bytes),
                    FileSize = bytes.Length,
                    CoordScale = DefaultCoordScale,
                    Zones = Array.Empty<EncounterZone>(),
                    Note = $"meta=0x{metaOff:X} geom=0x{geomOff:X} out of range",
                    MetaBlockOffset = metaOff,
                    GeometryBlockOffset = geomOff,
                };
            }

            int dispatchRel = ReadU32(bytes, metaOff + 0x1C);
            int dispatchAbs = metaOff + dispatchRel;
            if (dispatchRel <= 0 || dispatchAbs + 8 > bytes.Length)
            {
                return new ParseResult
                {
                    Status = MapoutZoneStatus.NoDispatchTable,
                    Family = ClassifyFamily(bytes),
                    FileSize = bytes.Length,
                    CoordScale = DefaultCoordScale,
                    Zones = Array.Empty<EncounterZone>(),
                    Note = "hdr+0x38 +0x1C dispatch missing",
                    MetaBlockOffset = metaOff,
                    GeometryBlockOffset = geomOff,
                };
            }

            var rawEntries = new List<(int Key, int Tag, int BlobOff)>();
            int pos = dispatchAbs;
            for (int i = 0; i < 128 && pos + 8 <= bytes.Length; i++)
            {
                int key = ReadU16(bytes, pos);
                int tag = ReadU16(bytes, pos + 2);
                int blobOff = ReadU32(bytes, pos + 4);

                if (key == 0 && tag == 0 && blobOff == 0)
                    break;

                if (tag is 0x0019 or 0x0071 or 0x0004)
                    rawEntries.Add((key, tag, blobOff));

                if (tag >= 0x0020 && tag <= 0x0040 && key >= 0x0020)
                    break;

                pos += 8;
            }

            var zones = new List<EncounterZone>();
            foreach ((int key, int tag, int blobOff) in rawEntries)
            {
                int blobAbs = geomOff + blobOff;
                if (blobAbs < 0 || blobAbs >= bytes.Length)
                    continue;

                IReadOnlyList<ZonePolygon> polys = ExtractPolygons(bytes, blobAbs, DefaultCoordScale);
                polys = polys.Where(p => p.Vertices.Count >= 3 && p.Vertices.Count <= 32).ToList();
                if (polys.Count == 0)
                    continue;

                float minX = polys.Min(p => p.MinX);
                float maxX = polys.Max(p => p.MaxX);
                float minZ = polys.Min(p => p.MinZ);
                float maxZ = polys.Max(p => p.MaxZ);

                zones.Add(new EncounterZone
                {
                    EntryKey = key,
                    Tag = tag,
                    GroupIndex = null,
                    LinkConfidence = "entry_key_only",
                    Polygons = polys,
                    MinX = minX,
                    MaxX = maxX,
                    MinZ = minZ,
                    MaxZ = maxZ,
                });
            }

            zones = DedupeZones(zones);
            ApplyGroupGuesses(zones, btlGroupCount);

            MapoutZoneStatus status = zones.Count > 0 ? MapoutZoneStatus.Ok : MapoutZoneStatus.NoZonesParsed;
            return new ParseResult
            {
                Status = status,
                Family = ClassifyFamily(bytes),
                FileSize = bytes.Length,
                CoordScale = DefaultCoordScale,
                Zones = zones,
                Note = zones.Count > 0
                    ? $"{zones.Count} zone(s) from MAP1 meta@0x{metaOff:X} dispatch@0x{dispatchAbs:X}"
                    : "dispatch table present but no polygons decoded",
                MetaBlockOffset = metaOff,
                GeometryBlockOffset = geomOff,
            };
        }

        static List<EncounterZone> DedupeZones(List<EncounterZone> zones)
        {
            var seen = new HashSet<(int Key, int Tag, int AreaBucket)>();
            var list = new List<EncounterZone>();
            foreach (EncounterZone z in zones.OrderBy(v => v.EntryKey).ThenBy(v => v.Tag))
            {
                int areaBucket = (int)((z.MaxX - z.MinX) * 1000 + (z.MaxZ - z.MinZ));
                var id = (z.EntryKey, z.Tag, areaBucket);
                if (!seen.Add(id))
                    continue;
                list.Add(z);
            }

            return list;
        }

        static void ApplyGroupGuesses(List<EncounterZone> zones, int? btlGroupCount)
        {
            if (btlGroupCount is not > 0 || zones.Count == 0)
                return;

            var keys = zones
                .Select(z => z.EntryKey)
                .Distinct()
                .OrderBy(k => k)
                .ToList();

            bool countAligned = keys.Count == btlGroupCount || zones.Count == btlGroupCount;
            var keyToGroup = new Dictionary<int, int>();
            for (int i = 0; i < keys.Count; i++)
                keyToGroup[keys[i]] = Math.Min(i, btlGroupCount.Value - 1);

            for (int i = 0; i < zones.Count; i++)
            {
                EncounterZone z = zones[i];
                int? group = keyToGroup.TryGetValue(z.EntryKey, out int gi) ? gi : Math.Min(i, btlGroupCount.Value - 1);
                zones[i] = new EncounterZone
                {
                    EntryKey = z.EntryKey,
                    Tag = z.Tag,
                    GroupIndex = group,
                    LinkConfidence = countAligned ? "key_sorted_guess" : "ordinal_guess",
                    Polygons = z.Polygons,
                    MinX = z.MinX,
                    MaxX = z.MaxX,
                    MinZ = z.MinZ,
                    MaxZ = z.MaxZ,
                };
            }
        }

        static IReadOnlyList<ZonePolygon> ExtractPolygons(byte[] bytes, int blobAbs, float scale)
        {
            var polys = new List<ZonePolygon>();
            int cursor = blobAbs;
            int end = Math.Min(blobAbs + 2048, bytes.Length - 4);

            while (cursor + 12 <= end && polys.Count < 16)
            {
                var verts = new List<(float X, float Z)>();
                while (cursor + 4 <= bytes.Length)
                {
                    short a = ReadI16(bytes, cursor);
                    short b = ReadI16(bytes, cursor + 2);
                    if (a == PolygonSentinelA && b == PolygonSentinelB)
                    {
                        cursor += 4;
                        break;
                    }

                    verts.Add((a * scale, b * scale));
                    cursor += 4;
                    if (verts.Count > 64)
                        break;
                }

                if (verts.Count < 3)
                    break;

                polys.Add(BuildPoly(verts));
            }

            return polys;
        }

        static ZonePolygon BuildPoly(List<(float X, float Z)> verts)
        {
            return new ZonePolygon
            {
                Vertices = verts,
                MinX = verts.Min(v => v.X),
                MaxX = verts.Max(v => v.X),
                MinZ = verts.Min(v => v.Z),
                MaxZ = verts.Max(v => v.Z),
            };
        }

        static string ClassifyFamily(byte[] bytes)
        {
            if (bytes.Length < 0x84)
                return "MAP1";
            string at80 = Encoding.ASCII.GetString(bytes, 0x80, 4);
            if (at80.StartsWith("eC", StringComparison.Ordinal))
                return "eC!+YNDT";
            if (at80.StartsWith("YN", StringComparison.Ordinal))
                return "YNDT";
            return "MAP1";
        }

        static ParseResult Empty(MapoutZoneStatus status, string family, int size = 0, string? note = null) =>
            new()
            {
                Status = status,
                Family = family,
                FileSize = size,
                CoordScale = DefaultCoordScale,
                Zones = Array.Empty<EncounterZone>(),
                Note = note,
            };

        static int ReadU16(byte[] b, int o) => b[o] | (b[o + 1] << 8);
        static short ReadI16(byte[] b, int o) => (short)(b[o] | (b[o + 1] << 8));
        static int ReadU32(byte[] b, int o) => b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);
    }
}

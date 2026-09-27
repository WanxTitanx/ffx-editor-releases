using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.Modules.AuroraFieldExplorer
{
    /// <summary>
    /// Compiles raw WalkManifest shard spawns into honest overlay entities (dedupe + chrClass).
    /// </summary>
    internal static class AuroraFieldExplorer_EncounterOverlayCompiler
    {
        public sealed record WalkEntity
        {
            public required string Name { get; init; }
            public required string Layer { get; init; }
            public required string ChrCategory { get; init; }
            public required string Confidence { get; init; }
            public required string Source { get; init; }
            public uint ChrId { get; init; }
            public required float X { get; init; }
            public required float Y { get; init; }
            public required float Z { get; init; }
            public int SampleCount { get; init; } = 1;
            public bool DefaultVisible { get; init; }
        }

        public sealed class ChrLayerCounts
        {
            public int StoryNpc { get; init; }
            public int Party { get; init; }
            public int FieldEnemy { get; init; }
            public int FieldProp { get; init; }
            public int Summon { get; init; }
            public int Weapon { get; init; }
            public int Unknown { get; init; }
            public int RawTotal { get; init; }

            public int DedupedTotal => StoryNpc + Party + FieldEnemy + FieldProp + Summon + Weapon + Unknown;
        }

        public sealed class CompiledWalkOverlay
        {
            public required IReadOnlyList<WalkEntity> Entities { get; init; }
            public required IReadOnlyList<WalkEntity> DisplayEntities { get; init; }
            public required ChrLayerCounts Counts { get; init; }
        }

        public static CompiledWalkOverlay Compile(AuroraFieldExplorer_WalkManifest.FieldWalkShard? shard)
        {
            if (shard == null)
            {
                return new CompiledWalkOverlay
                {
                    Entities = Array.Empty<WalkEntity>(),
                    DisplayEntities = Array.Empty<WalkEntity>(),
                    Counts = new ChrLayerCounts(),
                };
            }

            var raw = new List<(string name, uint chrId, string source, float x, float y, float z)>();
            foreach (AuroraFieldExplorer_WalkManifest.WalkNpc n in shard.Npcs)
                raw.Add((n.Name, n.ChrId, n.Source, n.X, n.Y, n.Z));
            foreach (AuroraFieldExplorer_WalkManifest.WalkChr c in shard.ChrSpawns)
                raw.Add((c.Name, c.ChrId, c.Source, c.X, c.Y, c.Z));

            // Dedupe by actor identity (chrName preferred, else chrId) — collapse movement trail.
            var byActor = new Dictionary<string, (WalkEntity entity, int samples)>(StringComparer.OrdinalIgnoreCase);
            int rawTotal = raw.Count;

            foreach ((string name, uint chrId, string source, float x, float y, float z) row in raw)
            {
                AuroraFieldExplorer_ChrClassifier.WalkLayer layer =
                    AuroraFieldExplorer_ChrClassifier.ClassifyLayer(row.name, row.chrId);
                string dedupeKey = !string.IsNullOrWhiteSpace(row.name)
                    ? row.name.ToLowerInvariant()
                    : $"id:{row.chrId}";

                if (byActor.TryGetValue(dedupeKey, out (WalkEntity entity, int samples) existing))
                {
                    byActor[dedupeKey] = (
                        existing.entity with
                        {
                            X = row.x,
                            Y = row.y,
                            Z = row.z,
                            SampleCount = existing.samples + 1,
                        },
                        existing.samples + 1);
                    continue;
                }

                byActor[dedupeKey] = (new WalkEntity
                {
                    Name = row.name,
                    Layer = AuroraFieldExplorer_ChrClassifier.LayerToCategory(layer),
                    ChrCategory = AuroraFieldExplorer_ChrClassifier.LayerToCategory(layer),
                    Confidence = "heuristic",
                    Source = row.source,
                    ChrId = row.chrId,
                    X = row.x,
                    Y = row.y,
                    Z = row.z,
                    SampleCount = 1,
                    DefaultVisible = AuroraFieldExplorer_ChrClassifier.DefaultVisible(layer),
                }, 1);
            }

            var entities = byActor.Values
                .Select(v => v.entity with { SampleCount = v.samples })
                .OrderBy(e => e.Layer, StringComparer.Ordinal)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var counts = new ChrLayerCounts
            {
                StoryNpc = entities.Count(e => e.Layer == "story_npc"),
                Party = entities.Count(e => e.Layer == "party"),
                FieldEnemy = entities.Count(e => e.Layer == "field_enemy"),
                FieldProp = entities.Count(e => e.Layer == "field_prop"),
                Summon = entities.Count(e => e.Layer == "summon"),
                Weapon = entities.Count(e => e.Layer == "weapon"),
                Unknown = entities.Count(e => e.Layer == "unknown" || e.Layer == "rig"),
                RawTotal = rawTotal,
            };

            var display = entities.Where(e => e.DefaultVisible).ToList();

            return new CompiledWalkOverlay
            {
                Entities = entities,
                DisplayEntities = display,
                Counts = counts,
            };
        }
    }
}

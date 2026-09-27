using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.TreasureMap;

// ── EventPosition / ProjectedChestLocation / ChestLocationIndexBuilder ─────────────────
// Projects the (few) recovered ATEL chest world positions into guide-map (YNGM) coordinate
// space and then into pixel space for the canvas.
//
// SCALE (2026-08-06 RE): the world->guide divisor is NOT a fixed 10. Each YNGM model has a
// local scale (LocalTransform.M11). A chest world coordinate must be multiplied by that
// per-model scale and divided by BaseWorldToGuideScale to land inside the model's bounds
// (e.g. Besaid scale=0.29 -> effective divisor ~34.5). The old hardcoded 10 produced
// positions far outside the map.
//
// CAUTION: only a few chests have a recovered position (ATEL constant PUSH_IM). The rest
// are Unresolved (GuideX null) and the editor draws a bounds placeholder instead.
// ──────────────────────────────────────────────────────────────────────────────────────

public sealed record EventPosition(float X, float Y, float Z, int ScriptOffset, int FunctionIndex);

public enum ChestLocationConfidence { NotAConfirmedChest, Unresolved, Conditional, Exact }

public sealed record EventTreasureCandidate(
    string EventPath, string EventId, string FieldId, int WorkerIndex,
    IReadOnlyList<int> TreasureIds, IReadOnlyList<EventPosition> Positions,
    IReadOnlyList<int> ModelIds, bool UsesSilentGrant)
{
    public const int StandardChestModelId = 0x5002;
    public const int BlueChestModelId = 0x50AA;
    public bool HasSinglePosition => Positions.Count == 1;
    public IReadOnlyList<EventPosition> InitialPositions
    {
        get
        {
            EventPosition[] f0 = Positions.Where(p => p.FunctionIndex == 0).ToArray();
            if (f0.Length > 0 || Positions.Count == 0) return f0;
            int initFn = Positions.Max(p => p.FunctionIndex);
            return Positions.Where(p => p.FunctionIndex == initFn).ToArray();
        }
    }
    public bool HasSingleInitialPosition => InitialPositions.Count == 1;
    public bool HasSingleTreasure => TreasureIds.Count == 1;
    public bool HasChestModel => ModelIds.Any(id => id is StandardChestModelId or BlueChestModelId);
}

public sealed record ProjectedChestLocation(
    string FieldId, string EventId, string EventPath, int WorkerIndex,
    IReadOnlyList<int> TreasureIds, int? ModelIndex, EventPosition? WorldPosition,
    float? GuideX, float? GuideZ, float? PixelX, float? PixelY,
    ChestLocationConfidence Confidence, string Evidence);

public sealed record ChestLocationIndex(
    IReadOnlyList<ProjectedChestLocation> Locations, int Width, int Height);

public static class ChestLocationIndexBuilder
{
    public const float BaseWorldToGuideScale = 10f;
    // Deprecated: use per-model scale (LocalTransform.M11) for accurate coordinate projection.
    // public const float FieldWorldUnitsPerGuideUnit = 10f;

    public static ChestLocationIndex Build(TreasureMapIndex treasureIndex, int width = 900, int height = 700)
    {
        var fields = treasureIndex.Fields.ToDictionary(f => f.FieldId, StringComparer.OrdinalIgnoreCase);
        var geometry = new Dictionary<string, GuideMapGeometry>(StringComparer.OrdinalIgnoreCase);
        var results = new List<ProjectedChestLocation>();

        foreach (EventTreasureCandidate c in treasureIndex.ConfirmedChestCandidates)
        {
            if (!fields.TryGetValue(c.FieldId, out FieldMapAsset? field))
            { results.Add(Unresolved(c, "No matching MAP1 field.")); continue; }
            if (!geometry.TryGetValue(field.FieldId, out GuideMapGeometry? guide))
            { guide = GuideMapGeometry.Read(Map1Archive.Read(field.MapPath)); geometry.Add(field.FieldId, guide); }
            if (guide.Models.Count == 0)
            { results.Add(Unresolved(c, "The field has no guide-map model.")); continue; }
            if (c.InitialPositions.Count == 0)
            { results.Add(Unresolved(c, "No constant init position recovered.")); continue; }

            bool added = false;
            foreach (EventPosition pos in c.InitialPositions)
            {
                for (int mi = 0; mi < guide.Models.Count; mi++)
                {
                    GuideMapModel m = guide.Models[mi];
                    float scale = m.LocalTransform.M11;
                    float gx = pos.X * scale / BaseWorldToGuideScale;
                    float gz = pos.Z * scale / BaseWorldToGuideScale;
                    if (!Contains(m, gx, gz)) continue;
                    GuideMapProjection proj = GuideMapProjection.Fit(m, width, height);
                    (float px, float py) = proj.Project(gx, gz);
                    ChestLocationConfidence conf = c.HasSingleTreasure && c.InitialPositions.Count == 1
                        ? ChestLocationConfidence.Exact : c.HasSingleTreasure ? ChestLocationConfidence.Conditional : ChestLocationConfidence.Unresolved;
                    results.Add(new ProjectedChestLocation(c.FieldId, c.EventId, c.EventPath, c.WorkerIndex,
                        c.TreasureIds, mi, pos, gx, gz, px, py, conf,
                        $"ATEL w{c.WorkerIndex:X2} init @ 0x{pos.ScriptOffset:X}; world*{scale:F2}/10; MAP1 YNGM state {mi}"));
                    added = true;
                }
            }
            if (!added) results.Add(Unresolved(c, "Positions fall outside every guide-map state."));
        }
        return new ChestLocationIndex(results, width, height);
    }

    private static bool Contains(GuideMapModel m, float x, float z)
    {
        float mx = Math.Max(1f, (m.BoundsMax.X - m.BoundsMin.X) * 0.03f);
        float mz = Math.Max(1f, (m.BoundsMax.Z - m.BoundsMin.Z) * 0.03f);
        return x >= m.BoundsMin.X - mx && x <= m.BoundsMax.X + mx && z >= m.BoundsMin.Z - mz && z <= m.BoundsMax.Z + mz;
    }

    private static ProjectedChestLocation Unresolved(EventTreasureCandidate c, string evidence) =>
        new(c.FieldId, c.EventId, c.EventPath, c.WorkerIndex, c.TreasureIds, null, null, null, null, null, null, ChestLocationConfidence.Unresolved, evidence);
}

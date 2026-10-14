using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.TreasureMap;

// ── EventPosition / ProjectedChestLocation / ChestLocationIndexBuilder ─────────────────
// Projects the (few) recovered ATEL chest world positions into guide-map (YNGM) coordinate
// space and then into pixel space for the canvas.
//
// YNGM LocalTransform decodes quantized int16 mesh vertices. ATEL setPosition values
// already use world units, so only the shared world-to-guide divisor applies here.
// Unknown positions remain unresolved; map bounds are not evidence of a chest location.
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
            return Positions.Where(p => p.FunctionIndex == 0).ToArray();
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
                    float gx = pos.X / BaseWorldToGuideScale;
                    float gz = pos.Z / BaseWorldToGuideScale;
                    if (!Contains(m, gx, gz)) continue;
                    GuideMapProjection proj = GuideMapProjection.Fit(m, width, height);
                    (float px, float py) = proj.Project(gx, gz);
                    ChestLocationConfidence conf = c.HasSingleTreasure && c.InitialPositions.Count == 1
                        ? ChestLocationConfidence.Exact : ChestLocationConfidence.Conditional;
                    results.Add(new ProjectedChestLocation(c.FieldId, c.EventId, c.EventPath, c.WorkerIndex,
                        c.TreasureIds, mi, pos, gx, gz, px, py, conf,
                        $"ATEL w{c.WorkerIndex:X2} init @ 0x{pos.ScriptOffset:X}; world/10; MAP1 YNGM state {mi}"));
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

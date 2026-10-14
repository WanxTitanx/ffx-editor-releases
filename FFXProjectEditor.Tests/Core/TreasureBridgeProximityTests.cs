using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.TreasureMap;
using Xunit;
using Xunit.Abstractions;

namespace FFXProjectEditor.Tests.Core;

public class TreasureBridgeProximityTests
{
    private readonly ITestOutputHelper _out;
    public TreasureBridgeProximityTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Bridge_Proximity_Bika02()
    {
        string master = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master";
        if (!Directory.Exists(master)) return;

        var index = TreasureMapIndexBuilder.Build(master);
        var field = index.Fields.FirstOrDefault(f => f.FieldId == "bika02");
        if (field == null) return;

        var archive = Map1Archive.Read(field.MapPath);
        var guide = GuideMapGeometry.Read(archive);
        var m0 = guide.Models[0];

        var candidates = index.ConfirmedChestCandidates.Where(c => c.FieldId == "bika02").ToList();
        var mapObjs = MapObjectExtractor.ExtractFromObjectTable(field.MapPath).Where(o => o.IsChestRange).ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"bika02: {candidates.Count} candidatos, {mapObjs.Count} objetos baú");

        var positioned = candidates.Where(c => c.Positions.Count > 0).ToList();
        var unpositioned = candidates.Where(c => c.Positions.Count == 0).ToList();
        sb.AppendLine($"Com posição ATEL: {positioned.Count}, sem posição: {unpositioned.Count}");

        float scale = m0.LocalTransform.M11;
        for (int mi = 0; mi < mapObjs.Count; mi++)
        {
            var mo = mapObjs[mi];
            var (gx, gz) = MapObjectExtractor.ProjectToGuide(mo, m0);
            int? tid = null;
            string note = "";

            if (positioned.Count > 0)
            {
                var best = positioned
                    .Select(c => (C: c, D: c.Positions.Min(p =>
                    {
                        float cgx = p.X * scale / ChestLocationIndexBuilder.BaseWorldToGuideScale;
                        float cgz = p.Z * scale / ChestLocationIndexBuilder.BaseWorldToGuideScale;
                        double dx = cgx - gx, dz = cgz - gz;
                        return dx * dx + dz * dz;
                    })))
                    .OrderBy(x => x.D).First();
                if (best.D < 200 * 200)
                {
                    tid = best.C.TreasureIds.FirstOrDefault();
                    note = $"proximity w{best.C.WorkerIndex:X2}";
                    positioned.Remove(best.C);
                }
            }
            if (tid is null && unpositioned.Count > 0 && mi < unpositioned.Count)
            {
                tid = unpositioned[mi].TreasureIds.FirstOrDefault();
                note = $"order w{unpositioned[mi].WorkerIndex:X2}";
            }

            sb.AppendLine($"  slot={mo.Slot} obj=({mo.X},{mo.Z}) -> treasure={tid?.ToString() ?? "?"} ({note})");
        }

        File.WriteAllText(@"C:\Users\wande\Documents\ffx-editor-main\work\_bridge_proximity.txt", sb.ToString());
        _out.WriteLine(sb.ToString());
    }
}
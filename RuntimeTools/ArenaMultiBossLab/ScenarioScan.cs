using FFXProjectEditor.FfxLib.BattleMap;

namespace ArenaMultiBossLab;

/// <summary>Offline scan: battles with grow headroom + vanilla anchor span (space for multi-aeon).</summary>
internal static class ScenarioScan
{
    private static readonly string[] PriorityPrefixes =
    [
        "mcyt00", "bika03", "nagi05", "kino00", "kino01", "dome02", "znkd09", "kami03", "hiku15", "bvyt09",
    ];

    public static int Run(string? vanillaRoot, int minMaxCount)
    {
        if (string.IsNullOrEmpty(vanillaRoot) || !Directory.Exists(vanillaRoot))
        {
            Console.Error.WriteLine("missing --vanilla-root for --scan-scenarios");
            return 1;
        }

        var rows = new List<Row>();
        foreach (string path in Directory.EnumerateFiles(vanillaRoot, "*.bin", SearchOption.AllDirectories))
        {
            string id = Path.GetFileNameWithoutExtension(path);
            if (!string.Equals(id, Path.GetFileName(Path.GetDirectoryName(path)), StringComparison.OrdinalIgnoreCase))
                continue;

            byte[] bin;
            try { bin = File.ReadAllBytes(path); }
            catch { continue; }

            BattleArenaGrowWriter.GrowPlan plan;
            try { plan = BattleArenaGrowWriter.Plan(bin); }
            catch { continue; }
            if (!plan.CanGrow || plan.MaxCount < minMaxCount)
                continue;

            var anchors = BattleArenaAnchors_File.ReadFromBattleBin(id, bin);
            var live = anchors.PrimaryMonsterAnchors;
            float xMin = 0, xMax = 0, zMin = 0, zMax = 0;
            if (live.Count > 0)
            {
                xMin = live.Min(a => a.X); xMax = live.Max(a => a.X);
                zMin = live.Min(a => a.Z); zMax = live.Max(a => a.Z);
            }
            float partyZ = 0;
            var party = anchors.Areas.Count > 0
                ? anchors.Areas[0].Groups.FirstOrDefault(g => g.Role == BattleArena_AnchorRole.PartyFront)
                : null;
            if (party != null && party.Anchors.Count > 0)
                partyZ = party.Anchors.Average(a => a.Z);

            rows.Add(new Row(id, plan.OldCount, plan.MaxCount, xMax - xMin, zMax - zMin, partyZ, live.Count));
        }

        Console.WriteLine($"=== Arena scenario scan (MaxCount>={minMaxCount}, grow-eligible) ===");
        Console.WriteLine($"root: {vanillaRoot}");
        Console.WriteLine($"matches: {rows.Count}");
        Console.WriteLine();
        Console.WriteLine("id           old max | xSpan zSpan | partyZ | notes");
        Console.WriteLine("-------------+-------+-------------+--------+------");

        foreach (var r in rows
                     .OrderByDescending(r => PriorityScore(r.Id))
                     .ThenByDescending(r => r.XSpan)
                     .ThenByDescending(r => r.MaxCount)
                     .Take(80))
        {
            string note = NoteFor(r);
            Console.WriteLine($"{r.Id,-12} {r.OldCount,3} {r.MaxCount,3} | {r.XSpan,5:0.0} {r.ZSpan,5:0.0} | {r.PartyZ,6:0.0} | {note}");
        }

        Console.WriteLine();
        Console.WriteLine("=== Tier picks (manual RT2 queue) ===");
        PrintTierPicks(rows, 3);
        PrintTierPicks(rows, 4);
        PrintTierPicks(rows, 5);
        return 0;
    }

    private static void PrintTierPicks(List<Row> rows, int need)
    {
        var picks = rows
            .Where(r => r.MaxCount >= need)
            .OrderByDescending(r => PriorityScore(r.Id))
            .ThenByDescending(r => r.XSpan >= 30 ? 1 : 0)
            .ThenByDescending(r => r.MaxCount)
            .Take(8)
            .ToList();
        Console.WriteLine($"--- need>={need} actors ({picks.Count} shown) ---");
        foreach (var p in picks)
            Console.WriteLine($"  {p.Id}  max={p.MaxCount}  xSpan={p.XSpan:0.0}  {NoteFor(p)}");
    }

    private static int PriorityScore(string id)
    {
        for (int i = 0; i < PriorityPrefixes.Length; i++)
            if (id.StartsWith(PriorityPrefixes[i], StringComparison.OrdinalIgnoreCase))
                return PriorityPrefixes.Length - i;
        return 0;
    }

    private static string NoteFor(Row r)
    {
        string map = r.Id.Length >= 6 ? r.Id[..6] : r.Id;
        string area = map switch
        {
            "mcyt00" => "Macalania",
            "bika03" => "Bikanel",
            "nagi05" => "Calm Cavern",
            "kino00" or "kino01" or "kino05" => "Remiem/Kilika",
            "dome02" => "Djose/Summoner",
            "znkd09" => "Zanarkand",
            _ => map,
        };
        string space = r.XSpan >= 40 ? "wide" : r.XSpan >= 28 ? "ok" : "tight";
        if (r.Id is "mcyt00_21" or "mcyt00_22") return $"{area} · preset/compose x3 · {space}";
        if (r.Id is "nagi05_24" or "nagi05_23") return $"{area} · preset/compose x4 · {space}";
        if (r.Id is "nagi05_50" or "nagi05_22") return $"{area} · preset/compose x5 · {space}";
        if (r.Id is "bika03_00") return $"{area} · preset Duo · {space}";
        if (r.Id is "kino00_70" or "kino01_70") return $"{area} · RT2 trio template · {space}";
        if (r.Id is "dome02_00") return $"{area} · 8 monPos research · {space}";
        if (r.Id is "nagi05_70") return $"{area} · Yojimbo solo · 6 monPos · {space}";
        return $"{area} · {space}";
    }

    private sealed record Row(string Id, int OldCount, int MaxCount, float XSpan, float ZSpan, float PartyZ, int LiveCount);
}

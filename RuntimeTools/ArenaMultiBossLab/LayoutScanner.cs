using System.Text.Json;
using FFXProjectEditor.FfxLib.BattleMap;

namespace ArenaMultiBossLab;

/// <summary>
/// Dataset de treino do organizador automatico (sugestao do usuario 2026-08-02):
/// varre TODOS os bins de um btlRoot e extrai o layout real de cada batalha
/// (party front/back + monster live com X/Y/Z + camera chunk0) + marca se o bin
/// difere do vanilla (editado) — para aprender os padroes reais das batalhas
/// ja organizadas (ex.: Mi'ihen) e melhorar o auto-layout.
/// </summary>
internal static class LayoutScanner
{
    public static int Run(string btlRoot, string? vanillaRoot, string outPath)
    {
        var rows = new List<object>();
        int skipped = 0;

        foreach (string bin in Directory.EnumerateFiles(btlRoot, "*.bin", SearchOption.AllDirectories))
        {
            string? dir = Path.GetDirectoryName(bin);
            string battleId = dir != null ? Path.GetFileName(dir) : Path.GetFileNameWithoutExtension(bin);
            byte[] bytes;
            try { bytes = File.ReadAllBytes(bin); }
            catch { skipped++; continue; }

            try
            {
                var anchors = BattleArenaAnchors_File.ReadFromBattleBin(battleId, bytes);
                if (anchors.Areas.Count == 0) { skipped++; continue; }
                var area = anchors.Areas[0];
                var party = area.Groups
                    .Where(g => g.Role is BattleArena_AnchorRole.PartyFront or BattleArena_AnchorRole.PartyBack)
                    .SelectMany(g => g.Anchors).ToList();
                var live = area.Groups.FirstOrDefault(g => g.Role == BattleArena_AnchorRole.MonsterLive)?.Anchors
                    ?? new List<BattleArena_Anchor>();
                if (party.Count == 0 && live.Count == 0) { skipped++; continue; }

                float h = 0f, e = 0f, d = 0f;
                try
                {
                    var setup = BattleCameraSetup_File.ReadFromBattleBin(battleId, bytes);
                    if (setup.Establishing != null)
                    {
                        h = setup.Establishing.PolarHorizontalAngle;
                        e = setup.Establishing.PolarElevationAngle;
                        d = setup.Establishing.PolarDistance;
                    }
                }
                catch { }

                bool edited = false;
                if (!string.IsNullOrEmpty(vanillaRoot))
                {
                    string vPath = Path.Combine(vanillaRoot, battleId, battleId + ".bin");
                    if (File.Exists(vPath))
                    {
                        byte[] vb = File.ReadAllBytes(vPath);
                        edited = vb.Length != bytes.Length || !vb.SequenceEqual(bytes);
                    }
                }

                rows.Add(new Dictionary<string, object>
                {
                    ["battle"] = battleId,
                    ["edited"] = edited,
                    ["party_count"] = party.Count,
                    ["party"] = party.Select(a => new[] { Math.Round(a.X, 2), Math.Round(a.Y, 2), Math.Round(a.Z, 2) }).ToArray(),
                    ["mon_count"] = live.Count,
                    ["monsters"] = live.Select(a => new[] { Math.Round(a.X, 2), Math.Round(a.Y, 2), Math.Round(a.Z, 2) }).ToArray(),
                    ["camera"] = new Dictionary<string, object>
                    {
                        ["h_deg"] = Math.Round(h, 1),
                        ["elev_deg"] = Math.Round(e, 1),
                        ["dist"] = Math.Round(d, 1),
                    },
                });
            }
            catch
            {
                skipped++;
            }
        }

        string json = JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(outPath, json);
        Console.WriteLine($"layout-scan : {rows.Count} batalhas extraidas, {skipped} ignoradas -> {outPath}");
        return 0;
    }
}

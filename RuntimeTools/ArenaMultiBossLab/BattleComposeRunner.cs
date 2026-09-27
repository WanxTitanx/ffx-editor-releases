using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FFXProjectEditor.FfxLib.Battle;
using FFXProjectEditor.FfxLib.BattleMap;

namespace ArenaMultiBossLab;

/// <summary>
/// Phase 1 custom mix: player pick list → spread template by actor count → deploy on HD carrier.
/// Phase 2 (F7 checklist) will call the same builder via manifest.
/// </summary>
internal static class BattleComposeRunner
{
    private sealed record PickEntry(string Key, string Label, ushort SlotId, bool IsMagusTriple);

    private static readonly PickEntry[] Catalog =
    [
        new("valefor", "Dark Valefor (m334)", 0x114E, false),
        new("ifrit", "Dark Ifrit (m335)", 0x114F, false),
        new("ixion", "Dark Ixion (m336)", 0x1150, false),
        new("shiva", "Dark Shiva (m337)", 0x1151, false),
        new("bahamut", "Dark Bahamut (m338)", 0x1152, false),
        new("yojimbo", "Dark Yojimbo (m340)", 0x1154, false),
        new("anima", "Dark Anima (m339)", 0x1153, false),
        new("magus", "Dark Magus Sisters (m341..343)", 0x1155, true),
    ];

    private static readonly ushort[] MagusSlots = [0x1155, 0x1156, 0x1157];

    private sealed record CarrierPlan(
        int ActorCount,
        string BattleId,
        string TokenF7,
        int Field,
        int Group,
        int Formation,
        float[][] Spread,
        /// <summary>When set, chunk0/camera/party anchors come from this vanilla bin; output still <see cref="BattleId"/>.</summary>
        string? SourceTemplateId = null);

    private static readonly CarrierPlan[] Carriers =
    [
        /* x3: mcyt00_21 = proven Trio camera (chunk0 ATEL behind party); deploy alias mcyt00_22. */
        new(3, "mcyt00_22", "0x01540016", 42, 0, 22, BuildRoleGrid(3), SourceTemplateId: "mcyt00_21"),
        new(4, "nagi05_23", "0x01AE0017", 430, 2, 23, BuildRoleGrid(4), SourceTemplateId: "nagi05_24"),
        new(5, "nagi05_22", "0x01AE0016", 430, 2, 22, BuildRoleGrid(5), SourceTemplateId: "nagi05_50"),
    ];

    /* Custom Mix only — presets (Gauntlet) keep fixed routes in dllmain.
     * Field/group/formation drive btlmap backdrop; SourceTemplateId = chunk0/camera donor only. */
    private sealed record ScenarioOption(
        string Key,
        string Label,
        string SourceTemplateId,
        int Field,
        int Group,
        int Formation,
        int BattlefieldId,
        /// <summary>
        /// Cross-map only: proven quad/trio camera bin (e.g. <c>nagi05_24</c>). Grafts chunk0 ATEL after compose and
        /// orients chunk3 spread from this template's party row — same pattern as x3 <c>mcyt00_21</c> @ Macalania Open.
        /// </summary>
        string? CameraChunk0TemplateId = null,
        /// <summary>Default party→monster distance for the auto-layout (corpus: ~80).</summary>
        float MonDistBase = 80f,
        /// <summary>Default wrap half-arc for the auto-layout (corpus: ~30).</summary>
        float MonArcHalfDeg = 30f);

    private static readonly ScenarioOption[] ScenariosX3 =
    [
        /* RT2 Macalania Forest (2026-06-18): mcfr00 @ tableIndex 36 — NOT mcyt00/id 340 (lake). */
        new("macalania_forest", "Macalania Forest", "mcfr00_00", 36, 0, 0, 1044, CameraChunk0TemplateId: "mcyt00_21"),
        new("macalania_open", "Macalania Open", "mcyt00_00", 42, 0, 0, 1046),
        new("macalania_open2", "Macalania Open 2", "mcyt00_21", 42, 0, 21, 1046),
        /* key kept as remiem for compatibility; kino00_00 is Mushroom Rock Road / Operation Mi'ihen. */
        new("remiem", "Mushroom Rock Road", "kino00_00", 24, 0, 0, 1035, CameraChunk0TemplateId: "mcyt00_21", MonDistBase: 130f, MonArcHalfDeg: 45f),
    ];

    private static readonly ScenarioOption[] ScenariosX4 =
    [
        new("cavern", "Calm Lands Cavern", "nagi05_24", 63, 2, 24, 1080),
        new("bikanel", "Bikanel Desert", "bika02_01", 47, 0, 1, 1049, CameraChunk0TemplateId: "nagi05_24"),
        new("remiem", "Mushroom Rock Road", "kino00_00", 24, 0, 0, 1035, CameraChunk0TemplateId: "nagi05_24", MonDistBase: 140f, MonArcHalfDeg: 50f),
    ];

    private static readonly ScenarioOption[] ScenariosX5 =
    [
        new("cavern", "Calm Lands Cavern (wide)", "nagi05_50", 63, 2, 50, 1080),
        new("cavern_alt", "Calm Lands (alt)", "nagi05_25", 63, 2, 25, 1080),
        new("remiem", "Mushroom Rock Road", "kino00_00", 24, 0, 0, 1035),
    ];

    private static ScenarioOption[] ScenariosForActorCount(int actorCount) => actorCount switch
    {
        3 => ScenariosX3,
        4 => ScenariosX4,
        5 => ScenariosX5,
        _ => Array.Empty<ScenarioOption>(),
    };

    /// <summary>Encounter table index of the dedicated compose carrier (nagi cavern / mcyt lake).</summary>
    private static int NativeCarrierFieldIndex(int actorCount) => actorCount switch
    {
        3 => 42,
        4 => 63,
        5 => 63,
        _ => -1,
    };

    /// <summary>Cross-map scenario (Bikanel, Remiem, Macalania Forest) deploys on the scene bin, not the nagi/mcyt carrier.</summary>
    private static bool IsScenarioCrossMap(ScenarioOption scenario, int actorCount) =>
        scenario.Field != NativeCarrierFieldIndex(actorCount);

    private static string ResolveOutputBattleId(ScenarioOption scenario, CarrierPlan carrier, int actorCount) =>
        carrier.BattleId;   // NEVER overwrite scenario SourceTemplateId — carriers are dedicated alias bins

    private static ScenarioOption ResolveScenario(int actorCount, string? scenarioKey)
    {
        ScenarioOption[] list = ScenariosForActorCount(actorCount);
        if (list.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(actorCount), actorCount, "compose requires 3, 4, or 5 actors");
        if (string.IsNullOrWhiteSpace(scenarioKey))
            return list.FirstOrDefault(s => s.Key == "macalania_open2") ?? list[0];
        if (string.Equals(scenarioKey, "macalania", StringComparison.OrdinalIgnoreCase))
            scenarioKey = "macalania_open2";
        return list.FirstOrDefault(s => string.Equals(s.Key, scenarioKey, StringComparison.OrdinalIgnoreCase)) ?? list[0];
    }

    /// <summary>Size tier for camera-safe spread (higher = push back / center-back first).</summary>
    private static int SizeTier(string key) => key.ToLowerInvariant() switch
    {
        "anima" or "bahamut" => 3,
        "valefor" or "magus" => 2,
        "yojimbo" or "ixion" or "ifrit" => 1,
        _ => 0,
    };

    /// <summary>Proven nagi05_24 quad footprint — must match chunk0 ATEL calibration (do not upscale).</summary>
    private const float ProvenQuadSpreadScale = 1.0f;

    /// <summary>Proven quartet recipe coords (absolute world space @ nagi05_24 party row).</summary>
    private static float[][] BuildRecipeAbsoluteQuad(float scale) =>
    [
        [-35.0f * scale, 0.0f, 54.0f * scale], /* back left */
        [ 35.0f * scale, 0.0f, 54.0f * scale], /* back right */
        [ 28.0f * scale, 0.0f, 30.0f * scale], /* front right */
        [-28.0f * scale, 0.0f, 30.0f * scale], /* front left */
    ];

    /// <summary>Scale vanilla donor monLive outward from centroid (keeps proven trapezoid shape).</summary>
    private static float[][]? TryScaleDonorMonLiveGrid(byte[] donorBin, string donorId, int count, float scale)
    {
        var live = BattleArenaAnchors_File.ReadFromBattleBin(donorId, donorBin).PrimaryMonsterAnchors;
        if (live.Count < count)
            return null;
        var pts = live.Take(count).ToList();
        float cx = pts.Average(p => p.X);
        float cz = pts.Average(p => p.Z);
        return pts.Select(p => new[]
        {
            cx + (p.X - cx) * scale,
            p.Y,
            cz + (p.Z - cz) * scale,
        }).ToArray();
    }

    /// <summary>
    /// Depth magnitudes for cavern cross-map (oriented via party row). Bikanel uses absolute quad instead.
    /// </summary>
    private static float[][] BuildQuadTrapezoidGrid(float scale = 1f) =>
    [
        [-35.0f * scale, 0.0f, 70.0f * scale], /* back left wing */
        [ 35.0f * scale, 0.0f, 70.0f * scale], /* back right wing */
        [-28.0f * scale, 0.0f, 46.0f * scale], /* front left */
        [ 28.0f * scale, 0.0f, 46.0f * scale], /* front right */
    ];

    private static float[][] BuildSpreadGrid(int actorCount, ScenarioOption scenario, CarrierPlan carrier)
    {
        if (actorCount == 4 && !string.IsNullOrEmpty(scenario.CameraChunk0TemplateId))
        {
            float scale = string.Equals(scenario.Key, "bikanel", StringComparison.OrdinalIgnoreCase) ? 1.32f : 1.0f;
            return BuildQuadTrapezoidGrid(scale);
        }
        return carrier.Spread;
    }

    /// <summary>
    /// Default role grid for carriers (cavern etc.). Bikanel x4 overrides via <see cref="BuildSpreadGrid"/>.
    /// </summary>
    private static float[][] BuildRoleGrid(int actorCount) => actorCount switch
    {
        3 =>
        [
            [-55.0f, 0.0f, 60.0f],  /* left wing */
            [ 55.0f, 0.0f, 60.0f],  /* right wing */
            [  0.0f, 0.0f, 105.0f], /* center back (tall aeon) */
        ],
        4 =>
        [
            [-70.0f, 0.0f, 60.0f],
            [ 70.0f, 0.0f, 60.0f],
            [-32.0f, 0.0f, 150.0f],
            [ 32.0f, 0.0f, 150.0f],
        ],
        5 =>
        [
            [-75.0f, 0.0f, 55.0f],
            [ 75.0f, 0.0f, 55.0f],
            [-35.0f, 0.0f, 110.0f],
            [ 35.0f, 0.0f, 110.0f],
            [  0.0f, 0.0f, 150.0f],
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(actorCount)),
    };

    private static List<string> ExpandPickKeysForSpread(IReadOnlyList<string> pickKeys)
    {
        var keys = new List<string>();
        foreach (string key in pickKeys)
        {
            PickEntry? entry = Catalog.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
                throw new ArgumentException($"unknown pick '{key}' — use --list-picks.");
            if (entry.IsMagusTriple)
            {
                keys.Add("magus");
                keys.Add("magus");
                keys.Add("magus");
            }
            else
            {
                keys.Add(entry.Key);
            }
        }
        return keys;
    }

    private static float[][] AssignSpreadPositions(
        IReadOnlyList<string> actorKeys,
        float[][] roleGrid,
        bool quadTrapezoid = false,
        bool applyBossNudge = true)
    {
        int n = actorKeys.Count;
        if (n != roleGrid.Length)
            throw new InvalidOperationException($"role grid mismatch: {n} actors vs {roleGrid.Length} roles");

        var indexed = actorKeys.Select((key, i) => (key, i, tier: SizeTier(key))).ToList();
        /* x4 trapezoid: largest on back wings, smallest on front row (matches nagi05_24 quad).
         * x4 default grid: smallest on wide wings. x3/x5: largest center-back. */
        indexed.Sort((a, b) =>
        {
            int c = n == 4
                ? (quadTrapezoid ? b.tier.CompareTo(a.tier) : a.tier.CompareTo(b.tier))
                : b.tier.CompareTo(a.tier);
            return c != 0 ? c : a.i.CompareTo(b.i);
        });

        var result = new float[n][];
        for (int rank = 0; rank < n; rank++)
        {
            int orig = indexed[rank].i;
            float[] role = roleGrid[rank];
            result[orig] = applyBossNudge
                ? NudgeForBoss(indexed[rank].key, role)
                : [role[0], role[1], role[2]];
        }
        return result;
    }

    private static float[] NudgeForBoss(string key, float[] role)
    {
        float x = role[0], y = role[1], z = role[2];
        switch (key.ToLowerInvariant())
        {
            case "anima":
            case "bahamut":
                z += 8.0f;
                break;
            case "valefor":
                z += 4.0f;
                break;
            case "magus":
                /* Sisters wing-out when triple-spread shares one tier key. */
                z += 4.0f;
                break;
        }
        return [x, y, z];
    }

    /// <summary>Party-front centroid Z from the vanilla carrier — defines which way "forward" is.</summary>
    private static float ReadPartyCentroidZ(byte[] battleBin, string battleId)
    {
        var anchors = BattleArenaAnchors_File.ReadFromBattleBin(battleId, battleBin);
        if (anchors.Areas.Count == 0)
            return 0f;
        var party = anchors.Areas[0].Groups.FirstOrDefault(g => g.Role == BattleArena_AnchorRole.PartyFront);
        if (party == null || party.Anchors.Count == 0)
            return 0f;
        return party.Anchors.Average(a => a.Z);
    }

    /// <summary>
    /// Place monsters on the party's facing side (toward battle origin from the party row).
    /// mcyt00_22 has party Z≈+2 and needs monsters at negative Z; mcyt00_21 / nagi05_23 use positive Z.
    /// </summary>
    private static float MonsterZForParty(float partyCentroidZ, float spreadDepth)
    {
        float depth = MathF.Abs(spreadDepth);
        return partyCentroidZ >= 0f ? partyCentroidZ - depth : partyCentroidZ + depth;
    }

    private static float[][] OrientSpreadToCarrierParty(float[][] spread, float partyCentroidZ) =>
        spread.Select(p => new[] { p[0], p[1], MonsterZForParty(partyCentroidZ, p[2]) }).ToArray();

    // ── Camera Auto-Layout (2026-08-02, Jarvis-HOOK) ──────────────────────────────────────
    // Deriva a fila da party + o spread dos monstros da CÂMERA do chunk0 do cenário escolhido
    // (refSetPos + camSetPolar → eye → forward). Funciona em QUALQUER cenário (sem doador).
    public sealed record AutoLayoutResult(
        float PartyX, float PartyZ,
        float RightX, float RightZ,
        float ForwardX, float ForwardZ,
        float[][] Monsters);

    public static AutoLayoutResult? ComputeAutoLayoutFromCamera(
        byte[] battleBin, string battleId, int actorCount)
    {
        try
        {
            // Posição real da câmera: prefere o anchor Camera do chunk3 (+0x2C); fallback PolarToEye (chunk0).
            // Alvo: centro real do MonsterLive do chunk3; fallback: o ref do chunk0.
            var anchors = BattleArenaAnchors_File.ReadFromBattleBin(battleId, battleBin);
            float? camX = null, camY = null, camZ = null;
            float? monCx = null, monCz = null;
            if (anchors.Areas.Count > 0)
            {
                var camGrp = anchors.Areas[0].Groups.FirstOrDefault(g => g.Role == BattleArena_AnchorRole.Camera);
                if (camGrp != null && camGrp.Anchors.Count > 0)
                {
                    camX = camGrp.Anchors[0].X; camY = camGrp.Anchors[0].Y; camZ = camGrp.Anchors[0].Z;
                }
                var live = anchors.Areas[0].Groups.FirstOrDefault(g => g.Role == BattleArena_AnchorRole.MonsterLive);
                if (live != null && live.Anchors.Count > 0)
                {
                    monCx = live.Anchors.Average(a => a.X);
                    monCz = live.Anchors.Average(a => a.Z);
                }
            }

            BattleCameraSetup_File setup = BattleCameraSetup_File.ReadFromBattleBin(battleId, battleBin);
            CameraEstablishingShot? e = setup.Establishing;
            if (camX == null && (e == null || !e.HasRef)) return null;

            float eyeX = camX ?? e!.RefX, eyeY = camY ?? e!.RefY, eyeZ = camZ ?? e!.RefZ;
            if (camX == null && e!.HasPolar && e.PolarDistance > 1f)
            {
                (eyeX, eyeY, eyeZ) = BattleCameraSetup_File.PolarToEye(
                    e.RefX, e.RefY, e.RefZ, e.PolarHorizontalAngle, e.PolarElevationAngle, e.PolarDistance);
            }

            float tX = monCx ?? e?.RefX ?? eyeX;
            float tZ = monCz ?? e?.RefZ ?? eyeZ;
            float tY = e?.RefY ?? eyeY;

            // forward = normalize(target - eye), projetado no plano XZ (o Y é altura da câmera)
            float fx = tX - eyeX, fy = tY - eyeY, fz = tZ - eyeZ;
            float flen = MathF.Sqrt(fx * fx + fy * fy + fz * fz);
            if (flen < 1e-4f) { fx = 0f; fy = 0f; fz = -1f; flen = 1f; }
            float planar = MathF.Sqrt(fx * fx + fz * fz);
            if (planar < 1e-4f) { fx = 0f; fz = -1f; planar = 1f; }
            fx /= planar; fz /= planar;   // forward plano normalizado

            // lateral perpendicular no plano XZ
            float rx = fz, rz = -fx;
            float rlen = MathF.Sqrt(rx * rx + rz * rz);
            if (rlen < 1e-4f) { rx = 1f; rz = 0f; rlen = 1f; }
            rx /= rlen; rz /= rlen;

            // Distância do padrão medido (RT2-validado): cam→party ≈ 11.
            const float wParty = 11f;
            float span = actorCount switch { 3 => 44f, 4 => 70f, _ => 72f };   // scans (x3/x4/x5)

            float partyX = eyeX + fx * wParty, partyZ = eyeZ + fz * wParty;
            float monCxx = tX, monCzz = tZ;   // o centro real dos monstros

            // Grid trapezoidal (back wings + front row) rotacionado pelo forward:
            // back = +forward (mais longe da câmera), front = mais perto dela.
            const float back = 12f, front = -8f;
            float[][] grid;
            if (actorCount <= 3)
            {
                grid =
                [
                    [monCxx + rx * (-span / 2f) + fx * back,     0f, monCzz + rz * (-span / 2f) + fz * back],
                    [monCxx + rx * ( span / 2f) + fx * back,     0f, monCzz + rz * ( span / 2f) + fz * back],
                    [monCxx + fx * (back + 10f),                 0f, monCzz + fz * (back + 10f)],
                ];
            }
            else
            {
                grid =
                [
                    [monCxx + rx * (-span / 2f) + fx * back,      0f, monCzz + rz * (-span / 2f) + fz * back],
                    [monCxx + rx * ( span / 2f) + fx * back,      0f, monCzz + rz * ( span / 2f) + fz * back],
                    [monCxx + rx * (-span * 0.4f) + fx * front,   0f, monCzz + rz * (-span * 0.4f) + fz * front],
                    [monCxx + rx * ( span * 0.4f) + fx * front,   0f, monCzz + rz * ( span * 0.4f) + fz * front],
                ];
            }
            return new AutoLayoutResult(partyX, partyZ, rx, rz, fx, fz, grid);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Escreve o auto-layout no bin (party row + monster live), preservando Y/W.
    /// Null se a câmera não for extraível (o chamador mantém o fallback do doador).</summary>
    public static byte[]? ApplyAutoLayoutPositions(byte[] battleBin, string battleId, int actorCount)
    {
        AutoLayoutResult? r = ComputeAutoLayoutFromCamera(battleBin, battleId, actorCount);
        if (r == null) return null;

        byte[] output = battleBin;

        // Party row: fila lateral de 3 (PartyFront), PartyBack a -forward*0.6 da frente
        var partyFront = new List<(float X, float Z)>
        {
            (r.PartyX + r.RightX * -0.9f, r.PartyZ + r.RightZ * -0.9f),
            (r.PartyX,                    r.PartyZ),
            (r.PartyX + r.RightX *  0.9f, r.PartyZ + r.RightZ *  0.9f),
        };
        var partyBack = partyFront.Select(p => (p.X - r.ForwardX * 0.6f, p.Z - r.ForwardZ * 0.6f)).ToList();

        byte[]? withFront = BattleArenaPositionWriter.WritePositions(
            output, 0, BattleArena_AnchorRole.PartyFront, partyFront);
        if (withFront != null) output = withFront;
        byte[]? withBack = BattleArenaPositionWriter.WritePositions(
            output, 0, BattleArena_AnchorRole.PartyBack, partyBack);
        if (withBack != null) output = withBack;

        var monsters = r.Monsters.Take(actorCount)
            .Select(m => (m[0], m[2])).ToList();
        byte[]? withMon = BattleArenaPositionWriter.WritePositions(
            output, 0, BattleArena_AnchorRole.MonsterLive, monsters);
        if (withMon != null) output = withMon;

        return output;
    }
/// <summary>
    /// PARTE 2 (2026-08-03) — elo do "live position edit": le o arena_positions.json (o grid 16x8
    /// editado no hook) e aplica via BattleArenaPositionWriter (X/Z, preserva Y/W). O grid e fornecido
    /// na ordem do preview (os P do mapa): os primeiros actorCount = monstros (MonsterLive), o resto = party.
    /// WHY: o jogo le os bichos das AN CORAS do chunk3 (pos = bin + chunk3 + 16*a5); aplicar o grid aqui
    /// faz a batalha iniciar com o posicionamento custom salvo no hook (E -> move -> ENTER).
    /// </summary>
    public static List<(int gx, int gy, int ogx, int ogy)>? ReadCustomPositionsJson(string? positionsRoot)
    {
        if (string.IsNullOrEmpty(positionsRoot)) return null;
        string jsonPath = positionsRoot.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? positionsRoot
            : Path.Combine(positionsRoot, "arena_positions.json");
        if (!File.Exists(jsonPath)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
            var list = new List<(int, int, int, int)>();
            foreach (var e in doc.RootElement.GetProperty("bichos").EnumerateArray())
            {
                int gx = e.GetProperty("gx").GetInt32();
                int gy = e.GetProperty("gy").GetInt32();
                int ogx = e.TryGetProperty("ogx", out var o1) ? o1.GetInt32() : gx;
                int ogy = e.TryGetProperty("ogy", out var o2) ? o2.GetInt32() : gy;
                list.Add((gx, gy, ogx, ogy));
            }
            return list.Count > 0 ? list : null;
        }
        catch { return null; }
    }

    public static byte[]? ApplyCustomPositions(byte[] battleBin, string battleId, int actorCount,
        List<(int gx, int gy, int ogx, int ogy)> grid)
    {
        byte[] output = battleBin;
        var monsters = new List<(float X, float Z)>();
        var party = new List<(float X, float Z)>();

        // LINK refine (2026-08-04): the grid DELTAS the foundation, it never replaces it.
        // The auto-layout/template has already placed the party row + monsters (the foundation
        // with camera orientation + W rotation). This apply only MOVES each bicho by the delta
        // (new grid cell - original grid cell) x camera axes, on top of the anchor's current
        // world position. Everything the user did NOT touch stays exactly as the auto-layout.
        // Before, this wrote absolute grid positions (baseX + gx*cell) which replaced the whole
        // layout -> "sum became substitution" and rotations/layout were lost.
        float rightX = 1f, rightZ = 0f, fwdX = 0f, fwdZ = -1f;
        AutoLayoutResult? r = ComputeAutoLayoutFromCamera(battleBin, battleId, actorCount);
        if (r != null)
        {
            rightX = r.RightX; rightZ = r.RightZ;
            fwdX = r.ForwardX; fwdZ = r.ForwardZ;
        }

        // Foundation anchors (what the auto-layout/template placed on the bin):
        // first actors = MonsterLive (monsters), the rest = party (PartyFront/Back).
        var anchors = BattleArenaAnchors_File.ReadFromBattleBin(battleId, battleBin);
        var monAnchor = anchors.Areas.Count > 0
            ? anchors.Areas[0].Groups.FirstOrDefault(g => g.Role == BattleArena_AnchorRole.MonsterLive)?.Anchors
            : null;
        var partyAnchors = anchors.Areas.Count > 0
            ? anchors.Areas[0].Groups
                .Where(g => g.Role is BattleArena_AnchorRole.PartyFront or BattleArena_AnchorRole.PartyBack)
                .SelectMany(g => g.Anchors).ToList()
            : null;

        const float cellX = 10.0f;  // world units per grid cell (lateral) — 2026-08-04: 2.5x more responsive
        const float cellZ = 15.0f;  // world units per grid cell (front/back)
        for (int idx = 0; idx < grid.Count; ++idx)
        {
            int dx = grid[idx].gx - grid[idx].ogx;   // grid cell delta (user movement)
            int dy = grid[idx].gy - grid[idx].ogy;
            // world delta along the camera axes
            float deltaX = dx * cellX * rightX + dy * cellZ * fwdX;
            float deltaZ = dx * cellX * rightZ + dy * cellZ * fwdZ;

            float baseX, baseZ;
            if (idx < actorCount)
            {
                // monster: anchor on the current MonsterLive position
                if (monAnchor != null && idx < monAnchor.Count) { baseX = monAnchor[idx].X; baseZ = monAnchor[idx].Z; }
                else { baseX = r?.PartyX ?? 0f; baseZ = r?.PartyZ ?? 0f; }
                monsters.Add((baseX + deltaX, baseZ + deltaZ));
            }
            else
            {
                // party: anchor on the current party row position
                int pi = idx - actorCount;
                if (partyAnchors != null && pi < partyAnchors.Count) { baseX = partyAnchors[pi].X; baseZ = partyAnchors[pi].Z; }
                else { baseX = r?.PartyX ?? 0f; baseZ = r?.PartyZ ?? 0f; }
                party.Add((baseX + deltaX, baseZ + deltaZ));
            }
        }

        if (monsters.Count > 0)
        {
            byte[]? withMon = BattleArenaPositionWriter.WritePositions(output, 0, BattleArena_AnchorRole.MonsterLive, monsters);
            if (withMon != null) output = withMon;
        }
        if (party.Count >= 1)
        {
            // write the party row (front), keeping the back row untouched (front/back share the row)
            byte[]? withFront = BattleArenaPositionWriter.WritePositions(output, 0, BattleArena_AnchorRole.PartyFront, party);
            if (withFront != null) output = withFront;
        }
        return output;
    }

    /// <summary>
    /// Posicoes NATIVAS do monLive do proprio bin (a verdade do cenario — Mushroom Rock):
    /// os anchors vanilla sao preservados; para counts acima da capacidade fisica, os extras
    /// derivam do centro do grupo na direcao party-&gt;monstros. Nada derivado da camera.
    /// </summary>
    private static float[][] BuildVanillaMonsterPositions(byte[] templateBin, string battleId, int actorCount)
    {
        var anchors = BattleArenaAnchors_File.ReadFromBattleBin(battleId, templateBin);
        var live = anchors.Areas.Count > 0
            ? anchors.Areas[0].Groups.FirstOrDefault(g => g.Role == BattleArena_AnchorRole.MonsterLive)?.Anchors
            : null;
        var result = new List<float[]>();
        if (live == null || live.Count == 0)
        {
            Console.WriteLine("vanilla pos  : monLive ausente no bin — fallback do grid padrao");
            return Array.Empty<float[]>();
        }

        foreach (var a in live)
            result.Add(new[] { a.X, 0f, a.Z });

        if (result.Count < actorCount)
        {
            float cx = result.Average(p => p[0]), cz = result.Average(p => p[2]);
            // direcao party -> monstros (do proprio bin)
            var party = anchors.Areas[0].Groups
                .FirstOrDefault(g => g.Role is BattleArena_AnchorRole.PartyFront or BattleArena_AnchorRole.PartyBack);
            float px = party != null && party.Anchors.Count > 0 ? party.Anchors.Average(a => a.X) : cx;
            float pz = party != null && party.Anchors.Count > 0 ? party.Anchors.Average(a => a.Z) : cz;
            float fx = cx - px, fz = cz - pz;
            float fl = MathF.Sqrt(fx * fx + fz * fz);
            if (fl > 1e-3f) { fx /= fl; fz /= fl; } else { fx = 0f; fz = -1f; }

            int extra = 1;
            while (result.Count < actorCount)
            {
                // o extra: centro do grupo + pequeno offset lateral (nunca sobre os vanilla)
                float ox = MathF.Sin(extra * 1.7f) * 9f;
                float oz = MathF.Cos(extra * 1.7f) * 9f;
                result.Add(new[] { cx + fx * 7f + ox, 0f, cz + fz * 7f + oz });
                extra++;
            }
            Console.WriteLine($"vanilla pos  : monLive {live.Count} -> {actorCount} (extras derivados do centro do grupo)");
        }
        else
        {
            Console.WriteLine($"vanilla pos  : monLive nativo preservado ({live.Count} posicoes)");
        }
        return result.ToArray();
    }

    private static bool ShouldApplyRemiemX4CameraFix(ScenarioOption scenario, int actorCount) =>
        actorCount == 4 && string.Equals(scenario.Key, "remiem", StringComparison.OrdinalIgnoreCase);

    private static byte[] ApplyRemiemX4BehindPartyCamera(byte[] battleBin, string battleId)
    {
        BattleCameraSetup_File setup = BattleCameraSetup_File.ReadFromBattleBin(battleId, battleBin);
        CameraEstablishingShot? e = setup.Establishing;
        if (e == null || !e.CanEditPolarEye)
        {
            Console.WriteLine("camera fix   : Mushroom Rock x4 skipped (no editable camSetPolar establishing shot)");
            return battleBin;
        }

        // Geometria REAL do chunk3: party -> monstros (a direcao do campo). A camera fica
        // ATRAS da party olhando o ref — o h CALCULADO (o -104.6 fixo era top-down arbitrario).
        // Elev/dist: os do vanilla (o -25 fixo deixava a camera de cima pra baixo).
        var anchors = BattleArenaAnchors_File.ReadFromBattleBin(battleId, battleBin);
        float? pCx = null, pCz = null, mCx = null, mCz = null;
        if (anchors.Areas.Count > 0)
        {
            var party = anchors.Areas[0].Groups
                .FirstOrDefault(g => g.Role is BattleArena_AnchorRole.PartyFront or BattleArena_AnchorRole.PartyBack);
            if (party != null && party.Anchors.Count > 0)
            {
                pCx = party.Anchors.Average(a => a.X);
                pCz = party.Anchors.Average(a => a.Z);
            }
            var live = anchors.Areas[0].Groups.FirstOrDefault(g => g.Role == BattleArena_AnchorRole.MonsterLive);
            if (live != null && live.Anchors.Count > 0)
            {
                mCx = live.Anchors.Average(a => a.X);
                mCz = live.Anchors.Average(a => a.Z);
            }
        }

        float hDeg = e.PolarHorizontalAngle; // default: manter o vanilla
        if (pCx != null && mCx != null)
        {
            float fx = mCx.Value - pCx.Value, fz = mCz.Value - pCz.Value;
            float fl = MathF.Sqrt(fx * fx + fz * fz);
            if (fl > 1e-3f)
            {
                fx /= fl;
                fz /= fl;
                // eye = ref + (cos(h), sin(h))*cos(e)*d → cos(h)=-fx, sin(h)=-fz → camera em -forward do ref
                hDeg = MathF.Atan2(-fz, -fx) * 180f / MathF.PI;
            }
        }

        byte[] output = setup.WithFloat(e.PolarHorizontalIndex, hDeg);
        Console.WriteLine(
            $"camera fix   : Mushroom Rock x4 polar h {e.PolarHorizontalAngle:F1}->{hDeg:F1} " +
            $"(camera atras da party, geometria chunk3), e {e.PolarElevationAngle:F1} + dist {e.PolarDistance:F1} kept (vanilla)");
        return output;
    }

    /// <summary>
    /// Fase 2 — preview ingame: imprime o mapa ASCII do layout final (P=party, 1..9=monstros)
    /// com o prefixo PREV| (o hook captura e mostra no menu antes do Launch).
    /// Projecao XZ no grid 16x8 (a orientacao da camera top-down: X horizontal, Z vertical).
    /// </summary>
    private static void PrintLayoutPreview(byte[]? battleBin, string battleId)
    {
        if (battleBin == null) return;
        try
        {
            var anchors = BattleArenaAnchors_File.ReadFromBattleBin(battleId, battleBin);
            if (anchors.Areas.Count == 0) return;
            var area = anchors.Areas[0];
            var party = area.Groups
                .Where(g => g.Role is BattleArena_AnchorRole.PartyFront or BattleArena_AnchorRole.PartyBack)
                .SelectMany(g => g.Anchors).ToList();
            var live = area.Groups.FirstOrDefault(g => g.Role == BattleArena_AnchorRole.MonsterLive)?.Anchors
                ?? new List<BattleArena_Anchor>();
            if (party.Count == 0 && live.Count == 0) return;

            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var a in party.Concat(live))
            {
                minX = Math.Min(minX, a.X); maxX = Math.Max(maxX, a.X);
                minZ = Math.Min(minZ, a.Z); maxZ = Math.Max(maxZ, a.Z);
            }
            float pad = Math.Max(maxX - minX, maxZ - minZ) * 0.12f + 1f;
            minX -= pad; maxX += pad; minZ -= pad; maxZ += pad;

            const int W = 16, H = 8;
            char[,] grid = new char[H, W];
            for (int r = 0; r < H; r++)
                for (int c = 0; c < W; c++)
                    grid[r, c] = '.';

            void Mark(float x, float z, char ch)
            {
                int c = (int)Math.Clamp((x - minX) / (maxX - minX) * (W - 1) + 0.5f, 0, W - 1);
                int r = (int)Math.Clamp((z - minZ) / (maxZ - minZ) * (H - 1) + 0.5f, 0, H - 1);
                if (grid[r, c] == '.') { grid[r, c] = ch; return; }
                // Occupied cell -> spiral search for the nearest free cell. Keeps ALL marks visible
                // (LINK 2026-08-04: before, only the first mark was placed -> the map collapsed to a
                // single "P" and the hook grid captured only a few monsters, losing the layout).
                for (int d = 1; d < Math.Max(H, W); d++)
                {
                    for (int dr = -d; dr <= d; dr++)
                    {
                        for (int dc = -d; dc <= d; dc++)
                        {
                            if (Math.Abs(dr) != d && Math.Abs(dc) != d) continue;
                            int nr = r + dr, nc = c + dc;
                            if (nr >= 0 && nr < H && nc >= 0 && nc < W && grid[nr, nc] == '.')
                            {
                                grid[nr, nc] = ch;
                                return;
                            }
                        }
                    }
                }
            }

            foreach (var a in party) Mark(a.X, a.Z, 'P');
            for (int i = 0; i < live.Count; i++)
                Mark(live[i].X, live[i].Z, i < 9 ? (char)('1' + i) : '*');

            float hDeg = 0f;
            try
            {
                var setup = BattleCameraSetup_File.ReadFromBattleBin(battleId, battleBin);
                if (setup.Establishing != null) hDeg = setup.Establishing.PolarHorizontalAngle;
            }
            catch { }

            Console.WriteLine("--- layout preview ---");
            for (int r = 0; r < H; r++)
            {
                var sb = new StringBuilder("PREV| ");
                for (int c = 0; c < W; c++) sb.Append(grid[r, c]);
                Console.WriteLine(sb.ToString());
            }
            float pcx = party.Count > 0 ? party.Average(a => a.X) : 0f;
            float pcz = party.Count > 0 ? party.Average(a => a.Z) : 0f;
            Console.WriteLine($"PREV| party({pcx:F0},{pcz:F0}) x{party.Count}  mon x{live.Count}");
            Console.WriteLine($"PREV| cam h {hDeg:F0}  P=party  1..9=monstros");
        }
        catch
        {
            // preview e best-effort — nunca quebra o compose
        }
    }

    /// <summary>
    /// Fase 5 — gerador de layout dos monstros (dataset-driven, 2026-08-02):
    /// padrao aprendido das 14 batalhas editadas (usuario+IA) + corpus vanilla:
    ///   - dist party-&gt;monstros ~105u (faixa 60-164)
    ///   - arco de envolvimento ~+-60..90° (cone max ~127° — os monstros envolvem a party)
    ///   - lateral (spread) ~90-100u
    ///   - Y = 0 (o engine usa o chao do mapa)
    /// Party e camera sao as CONSTANTES (nunca mexidas); os monstros sao a variavel:
    /// o padrao base + uma randomizacao SEEDADA por battleId+count (o padrao muda por
    /// batalha, mas e reproduzivel — "divertido que muda").
    /// </summary>
    public static float[][] BuildMonsterPattern(
        float partyX, float partyZ, float cameraH, int count, string seedKey,
        float distBase = 80f, float arcHalf = 30f)   // corpus-calibrated (2026-08-04): dist~80, arc~30
    {
        int seed = seedKey.GetHashCode();
        var rnd = new Random(seed);
        float dist = distBase * (0.9f + (float)rnd.NextDouble() * 0.3f);   // ~72-96 (corpus party->mon dist)
        float arc = arcHalf + (float)rnd.NextDouble() * 0f;                 // half-arc (corpus ~30)
        double h = cameraH * Math.PI / 180.0;
        float fx = (float)-Math.Cos(h), fz = (float)-Math.Sin(h);
        float rx = fz, rz = -fx;
        float fl = MathF.Sqrt(fx * fx + fz * fz);
        if (fl > 1e-4f) { fx /= fl; fz /= fl; }
        float rl = MathF.Sqrt(rx * rx + rz * rz);
        if (rl > 1e-4f) { rx /= rl; rz /= rl; }

        var result = new float[count][];
        for (int i = 0; i < count; i++)
        {
            float t = count > 1 ? (float)i / (count - 1) : 0.5f;
            float ang = (t - 0.5f) * 2f * arc * (float)Math.PI / 180f;
            ang += (float)(rnd.NextDouble() - 0.5) * 0.35f;                 // jitter angular
            float di = dist * (0.85f + (float)rnd.NextDouble() * 0.3f); // jitter de distancia
            float x = partyX + (float)(Math.Cos(ang) * fx * di + Math.Sin(ang) * rx * di);
            float z = partyZ + (float)(Math.Cos(ang) * fz * di + Math.Sin(ang) * rz * di);
            result[i] = new[] { x, 0f, z };
        }
        Console.WriteLine(
            $"pattern      : {count} monstros, dist ~{dist:F0} (+-15%), arco +-{arc:F0}°, seed {seedKey} (dataset-driven)");
        return result;
    }

    private sealed class LayoutProfile
    {
        public float CamElevOffsetDeg { get; init; }
        public float CamDistScale { get; init; }
        public float MonSpreadScale { get; init; }
        public float MonShiftForward { get; init; }
        public float MonDistBase { get; init; } = 80f;   // party->monster distance (corpus-calibrated ~80)
        public float MonArcHalfDeg { get; init; } = 30f;  // +- wrap arc (corpus-calibrated ~30)
        public string? LayoutMode { get; init; }   // "preserve" (default) | "pattern" (gerador dataset-driven)
        public bool HasAdjustments =>
            CamElevOffsetDeg != 0f || CamDistScale != 1f || MonSpreadScale != 1f || MonShiftForward != 0f
            || LayoutMode == "pattern";

        public static LayoutProfile? Load(string? path, string scenarioKey)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                LayoutProfile From(JsonElement e)
                {
                    return new LayoutProfile
                    {
                        CamElevOffsetDeg = e.TryGetProperty("cam_elev_offset_deg", out var v1) ? v1.GetSingle() : 0f,
                        CamDistScale = e.TryGetProperty("cam_dist_scale", out var v2) ? v2.GetSingle() : 1f,
                        MonSpreadScale = e.TryGetProperty("mon_spread_scale", out var v3) ? v3.GetSingle() : 1f,
                        MonShiftForward = e.TryGetProperty("mon_shift_forward", out var v4) ? v4.GetSingle() : 0f,
                        MonDistBase = e.TryGetProperty("mon_dist_base", out var v4b) ? v4b.GetSingle() : 80f,
                        MonArcHalfDeg = e.TryGetProperty("mon_arc_deg", out var v4c) ? v4c.GetSingle() : 30f,
                        LayoutMode = (e.TryGetProperty("layout_mode_pattern", out var v5p) && v5p.ValueKind == JsonValueKind.Number
                            && v5p.GetSingle() >= 0.5f)
                            ? "pattern"
                            : (e.TryGetProperty("layout_mode", out var v5) && v5.ValueKind == JsonValueKind.String
                                ? v5.GetString()
                                : null),
                    };
                }
                if (root.TryGetProperty(scenarioKey, out var sp)) return From(sp);
                if (root.TryGetProperty("_default", out var dp)) return From(dp);
                return null;
            }
            catch
            {
                return null;
            }
        }
    }

    private static float[][] ApplyMonsterLayoutProfile(float[][] grid, LayoutProfile profile)
    {
        if (grid.Length == 0) return grid;
        if (profile.MonSpreadScale == 1f && profile.MonShiftForward == 0f) return grid;
        float cx = grid.Average(p => p[0]), cz = grid.Average(p => p[2]);
        float fx = grid[grid.Length - 1][0] - cx, fz = grid[grid.Length - 1][2] - cz;
        float fl = MathF.Sqrt(fx * fx + fz * fz);
        if (fl > 1e-4f) { fx /= fl; fz /= fl; } else { fx = 0f; fz = -1f; }
        var result = new float[grid.Length][];
        for (int i = 0; i < grid.Length; i++)
        {
            result[i] = new[]
            {
                cx + (grid[i][0] - cx) * profile.MonSpreadScale + fx * profile.MonShiftForward,
                0f,
                cz + (grid[i][2] - cz) * profile.MonSpreadScale + fz * profile.MonShiftForward,
            };
        }
        Console.WriteLine($"profile      : mon spread x{profile.MonSpreadScale:F2} shift fwd {profile.MonShiftForward:+#.#;-#.#}");
        return result;
    }

    private static byte[] ApplyCameraLayoutProfile(byte[] battleBin, string battleId, LayoutProfile profile)
    {
        if (!profile.HasAdjustments) return battleBin;
        try
        {
            var setup = BattleCameraSetup_File.ReadFromBattleBin(battleId, battleBin);
            var e = setup.Establishing;
            if (e == null || !e.CanEditPolarEye) return battleBin;
            byte[] output = battleBin;
            if (profile.CamElevOffsetDeg != 0f && e.PolarElevationIndex >= 0)
            {
                float newElev = e.PolarElevationAngle + profile.CamElevOffsetDeg;
                output = setup.WithFloat(e.PolarElevationIndex, newElev);
                Console.WriteLine($"profile      : cam elev {e.PolarElevationAngle:F1}->{newElev:F1} (offset {profile.CamElevOffsetDeg:+#.#;-#.#})");
                setup = BattleCameraSetup_File.ReadFromBattleBin(battleId, output);
                e = setup.Establishing;
            }
            if (profile.CamDistScale != 1f && e.PolarDistanceIndex >= 0)
            {
                float newDist = e.PolarDistance * profile.CamDistScale;
                output = setup.WithFloat(e.PolarDistanceIndex, newDist);
                Console.WriteLine($"profile      : cam dist {e.PolarDistance:F1}->{newDist:F1} (x{profile.CamDistScale:F2})");
            }
            return output;
        }
        catch
        {
            return battleBin;
        }
    }
/// <summary>
    /// LINK camera (2026-08-04): applies the camera YAW edited in the hook's CAM mode.
    /// The hook saves camera.yaw in arena_positions.json; the yaw is the horizontal polar angle
    /// (PolarHorizontalIndex) that orients the camera -> the whole layout (party row + monsters).
    /// Before, only the layout profile (elev/dist) was applied, never the yaw, so moving the CAM
    /// point did nothing in-game.
    /// </summary>
    private static byte[] ApplyCameraYawFromPositions(byte[] battleBin, string battleId, string? positionsRoot)
    {
        if (string.IsNullOrEmpty(positionsRoot)) return battleBin;
        try
        {
            string jsonPath = positionsRoot.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? positionsRoot
                : Path.Combine(positionsRoot, "arena_positions.json");
            if (!File.Exists(jsonPath)) return battleBin;
            using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
            if (!doc.RootElement.TryGetProperty("camera", out var cam) ||
                !cam.TryGetProperty("yaw", out var yaw)) return battleBin;
            float yawDeg = yaw.GetSingle();
            if (yawDeg < 0f) yawDeg += 360f;

            var setup = BattleCameraSetup_File.ReadFromBattleBin(battleId, battleBin);
            var e = setup.Establishing;
            if (e == null || !e.CanEditPolarEye || e.PolarHorizontalIndex < 0) return battleBin;
            byte[] output = setup.WithFloat(e.PolarHorizontalIndex, yawDeg);
            Console.WriteLine($"camera yaw  : {e.PolarHorizontalAngle:F1}->{yawDeg:F1} (from CAM mode)");
            return output;
        }
        catch
        {
            return battleBin;
        }
    }

    /// <summary>Fase 3 — export do layout real (posicoes + camera + info) em JSON, ao lado do manifest
    /// (modules\compose_last_layout.json) — para o usuario/comunidade/agentes verem e trocarem.</summary>
    private static void WriteLayoutExport(
        string manifestOut, byte[]? battleBin, string battleId, string scenarioKey,
        int actorCount, IReadOnlyList<string> pickKeys, LayoutProfile? profile)
    {
        if (battleBin == null || string.IsNullOrEmpty(manifestOut)) return;
        try
        {
            string exportPath = Path.Combine(Path.GetDirectoryName(manifestOut) ?? ".", "compose_last_layout.json");
            var anchors = BattleArenaAnchors_File.ReadFromBattleBin(battleId, battleBin);
            float[]? party = null;
            var monsters = new List<float[]>();
            float hDeg = 0f, elevDeg = 0f, dist = 0f;
            if (anchors.Areas.Count > 0)
            {
                var area = anchors.Areas[0];
                var p = area.Groups
                    .Where(g => g.Role is BattleArena_AnchorRole.PartyFront or BattleArena_AnchorRole.PartyBack)
                    .SelectMany(g => g.Anchors).ToList();
                var m = area.Groups.FirstOrDefault(g => g.Role == BattleArena_AnchorRole.MonsterLive)?.Anchors
                    ?? new List<BattleArena_Anchor>();
                party = new[] { p.Count > 0 ? p.Average(a => a.X) : 0f, p.Count > 0 ? p.Average(a => a.Z) : 0f };
                foreach (var a in m) monsters.Add(new[] { (float)Math.Round(a.X, 1), (float)Math.Round(a.Z, 1) });
            }
            try
            {
                var setup = BattleCameraSetup_File.ReadFromBattleBin(battleId, battleBin);
                if (setup.Establishing != null)
                {
                    hDeg = setup.Establishing.PolarHorizontalAngle;
                    elevDeg = setup.Establishing.PolarElevationAngle;
                    dist = setup.Establishing.PolarDistance;
                }
            }
            catch { }

            var json = new Dictionary<string, object>
            {
                ["schema"] = "arena-layout-export-v1",
                ["scenario"] = scenarioKey,
                ["battle"] = battleId,
                ["actor_count"] = actorCount,
                ["picks"] = pickKeys,
                ["camera"] = new Dictionary<string, object>
                {
                    ["h_deg"] = Math.Round(hDeg, 1),
                    ["elev_deg"] = Math.Round(elevDeg, 1),
                    ["dist"] = Math.Round(dist, 1),
                },
                ["party_center"] = party != null ? new[] { Math.Round(party[0], 1), Math.Round(party[1], 1) } : null,
                ["monsters"] = monsters,
                ["profile_active"] = profile?.HasAdjustments ?? false,
                ["profile"] = profile != null && profile.HasAdjustments
                    ? new Dictionary<string, object>
                    {
                        ["cam_elev_offset_deg"] = Math.Round(profile.CamElevOffsetDeg, 1),
                        ["cam_dist_scale"] = Math.Round(profile.CamDistScale, 2),
                        ["mon_spread_scale"] = Math.Round(profile.MonSpreadScale, 2),
                        ["mon_shift_forward"] = Math.Round(profile.MonShiftForward, 1),
                    }
                    : null,
            };
            File.WriteAllText(exportPath, JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"layout export: {exportPath}");
        }
        catch
        {
            // export e best-effort
        }
    }

    public static int Run(
        string? pickCsv,
        string? idsCsv,
        string? scenarioKey,
        string? vanillaRoot,
        string? modRoot,
        string? positionsRoot,
        string? manifestOut,
        string? cacheDir,
        bool dryRun,
        bool listPicks,
        bool useAutoLayout,
        string? layoutProfile)
    {
        if (listPicks)
        {
            PrintPickCatalog();
            return 0;
        }

        List<string> pickKeys;
        try
        {
            pickKeys = ParsePickKeys(pickCsv, idsCsv);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        List<ushort> actorSlots;
        List<string> bossLabels;
        try
        {
            (actorSlots, bossLabels) = ExpandPicks(pickKeys);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        int actorCount = actorSlots.Count;
        CarrierPlan? carrier = Carriers.FirstOrDefault(c => c.ActorCount == actorCount);
        if (carrier == null)
        {
            Console.Error.WriteLine($"compose requires 3, 4, or 5 actors total (got {actorCount}). Magus counts as +3.");
            return 1;
        }

        ScenarioOption scenario;
        try
        {
            scenario = ResolveScenario(actorCount, scenarioKey);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        if (string.IsNullOrEmpty(vanillaRoot))
        {
            Console.Error.WriteLine("missing --vanilla-root <btlRoot>");
            return 1;
        }

        /* Cross-map w/ CameraChunk0TemplateId: Macalania pattern — compose FROM quad donor (chunk0+chunk1+chunk3),
         * deploy AS scene bin (bika02_01). NOT bika 3-mob + chunk0 graft (chunk0/chunk1 mismatch clusters spawns). */
        string sceneTemplateId = scenario.SourceTemplateId;
        string? cameraChunk0TemplateId = scenario.CameraChunk0TemplateId;
        bool graftCameraChunk0 = !string.IsNullOrEmpty(cameraChunk0TemplateId);
        bool crossMap = IsScenarioCrossMap(scenario, actorCount);
        bool composeFromQuadDonor = graftCameraChunk0 && crossMap && actorCount == 4;
        string composeSourceId = composeFromQuadDonor ? cameraChunk0TemplateId! : sceneTemplateId;

        /* Mushroom Rock (remiem/kino00_00): as posicoes NATIVAS do proprio bin (party + monLive)
         * sao a verdade do cenario — o layout derivado da camera (camera top-down: direcao
         * horizontal indefinida) NAO se aplica. O compose preserva as posicoes vanilla e so
         * ajusta count/chunk2/camera. O auto-layout derivado da camera fica como ferramenta
         * de inspecao/sugestao (--auto-layout-check); o hook passa --auto-layout, o lab decide. */
        bool remiemKinoPreserve = string.Equals(scenario.Key, "remiem", StringComparison.OrdinalIgnoreCase)
            || string.Equals(scenario.Key, "kino00_00", StringComparison.OrdinalIgnoreCase);
        bool preserveVanillaPositions = useAutoLayout && remiemKinoPreserve;
        // Mushroom Rock (remiem/kino00_00): the vanilla camera is top-down (no clear horizontal direction),
        // but with CameraChunk0TemplateId injected, we now use the donor's camera (mcyt00_21 / nagi05_24).
        // The auto-layout follows the donor camera orientation — no need to preserve vanilla positions.
        if (remiemKinoPreserve)
            useAutoLayout = true;   // auto-layout now follows the injected camera (2026-08-04 fix)

        var layoutProfileObj = LayoutProfile.Load(layoutProfile, scenario.Key);
        if (layoutProfileObj != null)
            Console.WriteLine(
                $"profile      : {scenario.Key} ativo (elev {layoutProfileObj.CamElevOffsetDeg:+#.#;-#.#} dist x{layoutProfileObj.CamDistScale:F2} " +
                $"spread x{layoutProfileObj.MonSpreadScale:F2} shift {layoutProfileObj.MonShiftForward:+#.#;-#.#})");

        string sourcePath = Path.Combine(vanillaRoot, composeSourceId, composeSourceId + ".bin");
        if (!File.Exists(sourcePath))
        {
            Console.Error.WriteLine($"vanilla source not found: {sourcePath}");
            return 2;
        }

        byte[] templateBin = File.ReadAllBytes(sourcePath);
        float partyCentroidZ = ReadPartyCentroidZ(templateBin, composeSourceId);
        byte[]? cameraDonorBin = null;
        if (graftCameraChunk0 && !composeFromQuadDonor)
        {
            string donorPath = Path.Combine(vanillaRoot, cameraChunk0TemplateId!, cameraChunk0TemplateId + ".bin");
            if (!File.Exists(donorPath))
            {
                Console.Error.WriteLine($"camera chunk0 template not found: {donorPath}");
                return 2;
            }
            cameraDonorBin = File.ReadAllBytes(donorPath);
        }

        float[][] spreadGrid;
        bool provenQuadSpread = composeFromQuadDonor;
        if (preserveVanillaPositions)
        {
            if (layoutProfileObj is { LayoutMode: "pattern" })
            {
                // Fase 5 — plug do gerador dataset-driven: party/camera CONSTANTES (vanilla) +
                // monstros no padrao aprendido (dist ~105, arco +-55-90°) com seed por batalha.
                var anchorsP = BattleArenaAnchors_File.ReadFromBattleBin(composeSourceId, templateBin);
                float pcx = 0f, pcz = 0f, camH = 0f;
                if (anchorsP.Areas.Count > 0)
                {
                    var partyP = anchorsP.Areas[0].Groups
                        .Where(g => g.Role is BattleArena_AnchorRole.PartyFront or BattleArena_AnchorRole.PartyBack)
                        .SelectMany(g => g.Anchors).ToList();
                    if (partyP.Count > 0)
                    {
                        pcx = partyP.Average(a => a.X);
                        pcz = partyP.Average(a => a.Z);
                    }
                }
                try
                {
                    var setupP = BattleCameraSetup_File.ReadFromBattleBin(composeSourceId, templateBin);
                    if (setupP.Establishing != null) camH = setupP.Establishing.PolarHorizontalAngle;
                }
                catch { }
                spreadGrid = BuildMonsterPattern(pcx, pcz, camH, actorCount, composeSourceId + "-" + actorCount,
                        layoutProfileObj?.MonDistBase ?? scenario.MonDistBase,
                        layoutProfileObj?.MonArcHalfDeg ?? scenario.MonArcHalfDeg);
            }
            else
            {
                // Posicoes NATIVAS do proprio bin (o remiem vanilla: party direita, monstros esquerda).
                // Para counts > capacidade, os extras derivam do centro do grupo.
                spreadGrid = BuildVanillaMonsterPositions(templateBin, composeSourceId, actorCount);
            }
            if (layoutProfileObj != null)
                spreadGrid = ApplyMonsterLayoutProfile(spreadGrid, layoutProfileObj);
        }
        else if (provenQuadSpread)
        {
            spreadGrid = TryScaleDonorMonLiveGrid(templateBin, composeSourceId, 4, ProvenQuadSpreadScale)
                ?? BuildRecipeAbsoluteQuad(ProvenQuadSpreadScale);
        }
        else
        {
            spreadGrid = BuildSpreadGrid(actorCount, scenario, carrier);
        }

        var growPlan = BattleArenaGrowWriter.Plan(templateBin);
        // position-only only for TIGHT monLive (declared count == physical capacity) that already holds
        // actorCount — the legacy path, byte-safe for the proven corpus. Non-tight layouts (e.g. kino00_00
        // remiem: count 4 vs cap 3) always go through grow: it splices on the PHYSICAL capacity (x4 inserts
        // 1 entry) or just re-stamps count + positions (x3: count 4 -> 3, no splice).
        string chunk3Mode = growPlan.CanGrow
            && growPlan.OldCount == growPlan.MonLiveCapacity
            && growPlan.OldCount >= actorCount
            ? "position-only"
            : "grow";

        var chunk2 = new List<string>(8);
        for (int i = 0; i < 8; i++)
            chunk2.Add(i < actorCount ? HexUtil.FormatU16(actorSlots[i]) : "0xFFFF");

        var spreadKeys = ExpandPickKeysForSpread(pickKeys);
        bool quadTrapezoid = actorCount == 4 && !string.IsNullOrEmpty(scenario.CameraChunk0TemplateId);
        /* FIX 2026-08-02 (cavern RT2): o boss nudge por tier COLIDIA os aeons do meio
         * (mon1-mon2 29u) — o grid padrao do carrier ja e calibrado por tier. Nudge OFF. */
        float[][] assigned = AssignSpreadPositions(spreadKeys, spreadGrid, quadTrapezoid,
            applyBossNudge: false);
        var positions = (provenQuadSpread || preserveVanillaPositions
                ? assigned
                : OrientSpreadToCarrierParty(assigned, partyCentroidZ))
            .Select(p => new[] { p[0], p[1], p[2] }).ToList();

        string hash = ComputeHash(actorSlots);
        string composeId = $"compose-{hash[..8]}";
        string outputBattleId = ResolveOutputBattleId(scenario, carrier, actorCount);

        var recipe = new Recipe
        {
            Id = composeId,
            Tier = $"compose-{actorCount}",
            SourceBattleIdRaw = composeSourceId,
            OutputBattleIdRaw = outputBattleId,
            TokenF7 = carrier.TokenF7,
            BaseTemplate = composeSourceId,
            Bosses = bossLabels,
            Chunk2Slots = chunk2,
            Chunk3Mode = chunk3Mode,
            Chunk3MonsterLive = positions,
            Note = $"Arena+ custom compose ({actorCount} actors) — scenario={scenario.Key} ({scenario.Label}) bf={scenario.BattlefieldId}; picks: {string.Join(", ", pickKeys)}; compose {composeSourceId} → deploy {outputBattleId}" +
                   (crossMap ? " (cross-map 781D60)" : "") +
                   (composeFromQuadDonor ? $" (quad donor {cameraChunk0TemplateId}, no chunk0 graft)" :
                    graftCameraChunk0 ? $"; chunk0 graft {cameraChunk0TemplateId}" : ""),
        };

        Console.WriteLine("compose      : custom mix (phase 1 lab)");
        Console.WriteLine($"scenario     : {scenario.Key} ({scenario.Label})");
        Console.WriteLine($"picks        : {string.Join(", ", pickKeys)}");
        Console.WriteLine($"actors       : {actorCount}  slots: {string.Join(", ", chunk2.Take(actorCount))}");
        Console.WriteLine($"carrier      : {carrier.BattleId}  token={carrier.TokenF7}{(crossMap ? " (launch via scenario FGF, not token)" : "")}");
        Console.WriteLine($"launch route : field={scenario.Field} group={scenario.Group} formation={scenario.Formation}  ({scenario.Label})");
        Console.WriteLine($"compose src  : {composeSourceId}{(composeFromQuadDonor ? " (quad donor — Macalania pattern)" : "")}");
        Console.WriteLine($"deploy bin   : {outputBattleId}  (backdrop {sceneTemplateId} @ FGF {scenario.Field}/{scenario.Group}/{scenario.Formation})");
        if (composeFromQuadDonor)
            Console.WriteLine($"camera tmpl  : {cameraChunk0TemplateId} native chunk0+chunk1 (no graft) + proven quad monLive");
        else if (graftCameraChunk0)
            Console.WriteLine($"camera tmpl  : {cameraChunk0TemplateId} chunk0 graft + trapezoid spread");
        Console.WriteLine($"chunk3 mode  : {chunk3Mode}{(growPlan.CanGrow ? $" (vanilla mon count {growPlan.OldCount})" : "")}");
        Console.WriteLine($"party Z      : {partyCentroidZ:F1}  from {composeSourceId}");
        if (actorCount == 4 && !string.IsNullOrEmpty(scenario.CameraChunk0TemplateId))
        {
            for (int i = 0; i < positions.Count; i++)
                Console.WriteLine($"monLive[{i}]   : ({positions[i][0]:F1}, {positions[i][1]:F1}, {positions[i][2]:F1})");
        }
        Console.WriteLine($"hash         : {hash[..8]}");

        bool remiemX4CameraFix = ShouldApplyRemiemX4CameraFix(scenario, actorCount);

        var apply = BattleRecipeApplicator.Apply(recipe, sourcePath, vanillaRoot, modRoot, dryRun);
        if (apply.ExitCode != 0)
            return apply.ExitCode;

        byte[]? outputBytes = apply.OutputBytes;
        if (graftCameraChunk0 && !composeFromQuadDonor && outputBytes != null)
        {
            try
            {
                byte[] cameraBin = cameraDonorBin!;
                int before = outputBytes.Length;
                outputBytes = BattleChunk0GraftWriter.GraftChunk0(outputBytes, cameraBin);
                Console.WriteLine($"chunk0 graft : {outputBattleId} ← {cameraChunk0TemplateId} ATEL camera ({before} → {outputBytes.Length} bytes)");
                if (!dryRun && !string.IsNullOrEmpty(apply.DeployPath))
                {
                    File.WriteAllBytes(apply.DeployPath, outputBytes);
                    Console.WriteLine($"re-deployed  : {apply.DeployPath} (post chunk0 graft)");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"chunk0 graft failed: {ex.Message}");
                return 3;
            }
        }

        if (remiemX4CameraFix && outputBytes != null)
        {
            try
            {
                outputBytes = ApplyRemiemX4BehindPartyCamera(outputBytes, outputBattleId);
                if (!dryRun && !string.IsNullOrEmpty(apply.DeployPath))
                {
                    File.WriteAllBytes(apply.DeployPath, outputBytes);
                    Console.WriteLine($"re-deployed  : {apply.DeployPath} (post Mushroom Rock camera fix)");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Mushroom Rock x4 camera fix failed: {ex.Message}");
                return 3;
            }
        }

        if (useAutoLayout && outputBytes != null)
        {
            try
            {
                // PARTE 2 (2026-08-03): elo do live position edit — se o arena_positions.json existe
                // (grid editado no hook: E -> move -> ENTER), usa o custom; senao, o auto-layout da camera.
                var customGrid = ReadCustomPositionsJson(positionsRoot ?? modRoot);
                byte[]? pos = customGrid != null
                    ? ApplyCustomPositions(outputBytes, outputBattleId, actorCount, customGrid)
                    : outputBytes;   // no user edits -> keep the template's native positions (scenario
                                     // original). The auto-layout (recalculate with small constants) was
                                     // making the first build "all clumped together"; now it stays as the
                                     // original scenario unless the user explicitly edits or picks pattern.
                if (pos != null)
                {
                    outputBytes = pos;
                    // LINK camera (2026-08-04): apos aplicar o grid, aplica o yaw da camera do modo CAM
                    // (o custom redefine os eixos; o yaw orienta o campo todo).
                    outputBytes = ApplyCameraYawFromPositions(outputBytes, outputBattleId, positionsRoot ?? modRoot);
                    Console.WriteLine(customGrid != null
                        ? $"[custom-positions] grid do hook aplicado em {outputBattleId} ({customGrid.Count} bichos)"
                        : $"[template] cenário original preservado (sem edicao do usuario)");
                    if (!dryRun && !string.IsNullOrEmpty(apply.DeployPath))
                    {
                        File.WriteAllBytes(apply.DeployPath, outputBytes);
                        Console.WriteLine($"re-deployed  : {apply.DeployPath} (post auto-layout)");
                    }
                }
                else
                {
                    Console.WriteLine("[auto-layout] camera nao extraivel (sem ref/polar) — fallback do layout padrao");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"auto-layout failed: {ex.Message}");
                return 3;
            }
        }

        // Fase 2 — preview ingame: o mapa ASCII do layout final (o hook captura o PREV| e mostra no menu)
        PrintLayoutPreview(outputBytes, outputBattleId);

        // Fase 3 — aplica o perfil de layout (camera) ao bin final + export JSON do layout real
        if (layoutProfileObj != null && outputBytes != null)
        {
            outputBytes = ApplyCameraLayoutProfile(outputBytes, outputBattleId, layoutProfileObj);
            if (!dryRun && !string.IsNullOrEmpty(apply.DeployPath))
            {
                File.WriteAllBytes(apply.DeployPath, outputBytes);
                Console.WriteLine($"re-deployed  : {apply.DeployPath} (post layout profile)");
            }
        }
        WriteLayoutExport(manifestOut ?? "", outputBytes, outputBattleId, scenario.Key, actorCount, pickKeys, layoutProfileObj);

        if (!string.IsNullOrEmpty(cacheDir) && outputBytes != null)
        {
            try
            {
                string cacheRoot = Path.Combine(cacheDir, hash[..8]);
                string cacheDirPath = Path.Combine(cacheRoot, carrier.BattleId);
                Directory.CreateDirectory(cacheDirPath);
                string cacheBin = Path.Combine(cacheDirPath, carrier.BattleId + ".bin");
                File.WriteAllBytes(cacheBin, outputBytes);
                Console.WriteLine($"cache        : {cacheBin}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"cache write failed: {ex.Message}");
                return 2;
            }
        }

        string manifestPath = manifestOut ?? DefaultManifestPath();
        try
        {
            var manifest = new ComposeManifest
            {
                Schema = "arena-compose-v1",
                ActorCount = actorCount,
                ScenarioKey = scenario.Key,
                ScenarioLabel = scenario.Label,
                SourceTemplateId = composeSourceId,
                CameraChunk0TemplateId = cameraChunk0TemplateId ?? "",
                Picks = pickKeys,
                BossLabels = bossLabels,
                Chunk2Slots = chunk2,
                BattleId = outputBattleId,
                TokenF7 = carrier.TokenF7,
                Field = scenario.Field,
                Group = scenario.Group,
                Formation = scenario.Formation,
                BattlefieldId = scenario.BattlefieldId,
                Hash = hash[..8],
                DeployPath = apply.DeployPath ?? "",
                DryRun = dryRun,
                ComposedAt = DateTime.UtcNow.ToString("o"),
            };

            string? manifestDir = Path.GetDirectoryName(manifestPath);
            if (!string.IsNullOrEmpty(manifestDir))
                Directory.CreateDirectory(manifestDir);

            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOpts.Instance));
            Console.WriteLine($"manifest     : {manifestPath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"manifest write failed: {ex.Message}");
            return 2;
        }

        return 0;
    }

    private static void PrintPickCatalog()
    {
        Console.WriteLine("Arena+ compose picks (8 ticks; magus = +3 actors):");
        foreach (PickEntry p in Catalog)
            Console.WriteLine($"  {p.Key,-10} {p.Label}  slot={HexUtil.FormatU16(p.SlotId)}{(p.IsMagusTriple ? " (+2 sisters)" : "")}");
        Console.WriteLine();
        Console.WriteLine("Custom Mix scenarios (--scenario key; presets use fixed routes, no picker):");
        PrintScenarioTier("x3", ScenariosX3);
        PrintScenarioTier("x4", ScenariosX4);
        PrintScenarioTier("x5", ScenariosX5);
        Console.WriteLine();
        Console.WriteLine("Output carriers (unchanged by scenario; presets never written):");
        foreach (CarrierPlan c in Carriers)
            Console.WriteLine($"  {c.ActorCount} actors -> {c.BattleId}  token={c.TokenF7}  default tmpl={c.SourceTemplateId}");
    }

    private static void PrintScenarioTier(string tier, ScenarioOption[] options)
    {
        Console.WriteLine($"  {tier}:");
        foreach (ScenarioOption s in options)
        {
            string cam = string.IsNullOrEmpty(s.CameraChunk0TemplateId) ? "" : $"  camChunk0={s.CameraChunk0TemplateId}";
            Console.WriteLine($"    {s.Key,-14} {s.Label,-24} scene={s.SourceTemplateId}{cam}  FGF={s.Field}/{s.Group}/{s.Formation}  bf={s.BattlefieldId}");
        }
    }

    private static List<string> ParsePickKeys(string? pickCsv, string? idsCsv)
    {
        if (!string.IsNullOrWhiteSpace(pickCsv) && !string.IsNullOrWhiteSpace(idsCsv))
            throw new ArgumentException("use --pick OR --ids, not both.");

        if (!string.IsNullOrWhiteSpace(pickCsv))
        {
            return pickCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(NormalizePickKey)
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(idsCsv))
        {
            var keys = new List<string>();
            foreach (string part in idsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                ushort id = HexUtil.ParseU16(part);
                if (id == 0x1158)
                    throw new ArgumentException("0x1158 is Penance (m344) — out of scope for custom compose.");
                PickEntry? entry = Catalog.FirstOrDefault(p => p.SlotId == id || (p.IsMagusTriple && MagusSlots.Contains(id)));
                if (entry == null)
                    throw new ArgumentException($"unknown slot id {HexUtil.FormatU16(id)} — use --list-picks.");
                if (entry.IsMagusTriple && id != 0x1155)
                    throw new ArgumentException("pass magus once (0x1155) — it expands to Cindy/Sandy/Mindy automatically.");
                keys.Add(entry.Key);
            }
            return keys;
        }

        throw new ArgumentException("missing --pick <valefor,ifrit,...> or --ids <0x114E,0x114F,...>");
    }

    private static string NormalizePickKey(string raw)
    {
        raw = raw.Trim().ToLowerInvariant();
        if (raw.StartsWith("m33") && raw.Length == 4 && int.TryParse(raw.AsSpan(1), out int num) && num is >= 334 and <= 343)
        {
            return num switch
            {
                334 => "valefor",
                335 => "ifrit",
                336 => "ixion",
                337 => "shiva",
                338 => "bahamut",
                339 => "anima",
                340 => "yojimbo",
                341 or 342 or 343 => "magus",
                _ => raw,
            };
        }
        if (raw.StartsWith("dark ", StringComparison.Ordinal))
            raw = raw[5..].Trim();
        return raw;
    }

    private static (List<ushort> Slots, List<string> Labels) ExpandPicks(IReadOnlyList<string> pickKeys)
    {
        if (pickKeys.Count == 0)
            throw new ArgumentException("empty pick list.");

        var slots = new List<ushort>();
        var labels = new List<string>();

        foreach (string key in pickKeys)
        {
            PickEntry? entry = Catalog.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
                throw new ArgumentException($"unknown pick '{key}' — use --list-picks.");
            if (entry.IsMagusTriple)
            {
                slots.AddRange(MagusSlots);
                labels.Add("Dark Cindy (m341)");
                labels.Add("Dark Sandy (m342)");
                labels.Add("Dark Mindy (m343)");
            }
            else
            {
                slots.Add(entry.SlotId);
                labels.Add(entry.Label);
            }
        }

        if (slots.Any(s => s == 0x1158))
            throw new ArgumentException("Penance (0x1158) is out of scope.");

        return (slots, labels);
    }

    private static string ComputeHash(IReadOnlyList<ushort> slots)
    {
        var sb = new StringBuilder();
        foreach (ushort s in slots)
            sb.Append(s.ToString("X4"));
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string DefaultManifestPath()
    {
        string? dir = FindRepoRoot(Directory.GetCurrentDirectory());
        if (dir != null)
            return Path.Combine(dir, "mods", "Spira Reforge", "arena", "compose_last.json");
        return Path.Combine(AppContext.BaseDirectory, "compose_last.json");
    }

    private static string? FindRepoRoot(string start)
    {
        for (string? d = start; d != null; d = Directory.GetParent(d)?.FullName)
        {
            if (File.Exists(Path.Combine(d, "FFXProjectEditor", "FFXProjectEditor.csproj")))
                return d;
        }
        return null;
    }


    // ═══════════════════════════════════════════════════════════════
    // CustomMix Ultra — free-form battle composer (1-8 monsters)
    // ═══════════════════════════════════════════════════════════════

    private sealed class UltraManifest
    {
        [JsonPropertyName("schema")] public string Schema { get; set; } = "ultra-v1";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("monsters")] public List<UltraSlot> Monsters { get; set; } = new();
        [JsonPropertyName("music_track")] public int MusicTrack { get; set; } = -1;
        [JsonPropertyName("scenario_key")] public string ScenarioKey { get; set; } = "";
        [JsonPropertyName("battle_id")] public string BattleId { get; set; } = "";
        [JsonPropertyName("hash")] public string Hash { get; set; } = "";
        [JsonPropertyName("deploy_path")] public string DeployPath { get; set; } = "";
        [JsonPropertyName("dry_run")] public bool DryRun { get; set; }
    }

    private sealed class UltraSlot
    {
        [JsonPropertyName("monster_id")] public ushort MonsterId { get; set; }
        [JsonPropertyName("label")] public string Label { get; set; } = "";
    }

    /// <summary>CustomMix Ultra: compose a battle .bin with 1-8 monsters from a manifest.</summary>
    public static int RunUltra(
        string ultraManifestPath,
        string? vanillaRoot,
        string? modRoot,
        string? templateBattleId,
        bool dryRun)
    {
        if (!File.Exists(ultraManifestPath))
        {
            Console.Error.WriteLine($"Ultra manifest not found: {ultraManifestPath}");
            return 1;
        }

        UltraManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<UltraManifest>(File.ReadAllText(ultraManifestPath))
                       ?? throw new InvalidOperationException("null manifest");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Ultra manifest parse failed: {ex.Message}");
            return 1;
        }

        if (manifest.Monsters.Count < 1 || manifest.Monsters.Count > 8)
        {
            Console.Error.WriteLine($"Ultra requires 1-8 monsters (got {manifest.Monsters.Count})");
            return 1;
        }

        string battleId = manifest.BattleId;
        if (string.IsNullOrEmpty(battleId))
            battleId = manifest.Name;

        if (string.IsNullOrEmpty(battleId))
        {
            Console.Error.WriteLine("Ultra manifest needs battle_id or name");
            return 1;
        }

        string sourceBattleId = templateBattleId ?? "kino00_00";
        string sourcePath = vanillaRoot != null
            ? Path.Combine(vanillaRoot, sourceBattleId, sourceBattleId + ".bin")
            : "";

        if (!File.Exists(sourcePath))
        {
            Console.Error.WriteLine($"Ultra template not found: {sourcePath}");
            return 1;
        }

        Console.WriteLine($"ultra        : {manifest.Name}  ({manifest.Monsters.Count} monsters)");
        Console.WriteLine($"template     : {sourceBattleId}");
        Console.WriteLine($"output       : {battleId}");

        byte[] sourceBytes = File.ReadAllBytes(sourcePath);
        Battle_File battle = Battle_File.Read(sourceBattleId, sourceBytes);

        if (!battle.CanWriteFormation)
        {
            Console.Error.WriteLine($"{sourceBattleId}: no writable formation chunk2");
            return 2;
        }

        ushort[] slots = new ushort[8];
        for (int i = 0; i < 8; i++)
            slots[i] = 0xFFFF;
        for (int i = 0; i < manifest.Monsters.Count && i < 8; i++)
            slots[i] = manifest.Monsters[i].MonsterId;

        byte[] afterChunk2;
        try
        {
            afterChunk2 = battle.WriteWithFormationSlots(slots);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"chunk2 write failed: {ex.Message}");
            return 3;
        }

        Console.WriteLine("chunk2 ok    : {0} monsters + {1} empty",
            manifest.Monsters.Count, 8 - manifest.Monsters.Count);

        float[][] pattern = BuildMonsterPattern(0f, -80f, 180f, manifest.Monsters.Count, manifest.Name);
        var positions = pattern.Select(p => (p[0], p[2])).ToList();
        byte[] afterChunk3;
        try
        {
            afterChunk3 = BattleArenaPositionWriter.WritePositions(
                afterChunk2, 0, BattleArena_AnchorRole.MonsterLive, positions);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"chunk3 write failed: {ex.Message}");
            return 3;
        }

        Battle_File reread = Battle_File.Read(battleId, afterChunk3);
        var reSlots = reread.Formation?.Slots.Select(s => (ushort)s.RawMonsterId).ToArray() ?? Array.Empty<ushort>();
        for (int i = 0; i < 8; i++)
        {
            ushort got = i < reSlots.Length ? reSlots[i] : (ushort)0xFFFF;
            if (got != slots[i])
            {
                Console.Error.WriteLine($"re-read mismatch slot {i}: got 0x{got:X4}, expected 0x{slots[i]:X4}");
                return 3;
            }
        }
        Console.WriteLine("re-read ok   : chunk2 slots match manifest");

        manifest.Hash = Convert.ToHexString(SHA256.HashData(afterChunk3))[..8].ToLowerInvariant();

        if (dryRun)
        {
            Console.WriteLine("dry-run      : NOT writing. Done.");
            manifest.DryRun = true;
            return 0;
        }

        if (string.IsNullOrEmpty(modRoot))
        {
            Console.Error.WriteLine("missing --mod-root for deploy");
            return 1;
        }

        string deployDir = Path.Combine(modRoot, battleId);
        string deployPath = Path.Combine(deployDir, battleId + ".bin");
        string backupPath = deployPath + ".ultra.bak";

        try
        {
            Directory.CreateDirectory(deployDir);
            if (!File.Exists(backupPath))
            {
                string outputVanilla = !string.IsNullOrEmpty(vanillaRoot)
                    ? Path.Combine(vanillaRoot, battleId, battleId + ".bin")
                    : "";
                string backupSource = File.Exists(deployPath) ? deployPath
                    : (!string.IsNullOrEmpty(outputVanilla) && File.Exists(outputVanilla) ? outputVanilla : sourcePath);
                File.Copy(backupSource, backupPath, overwrite: false);
                Console.WriteLine($"backup       : {backupPath}");
            }
            File.WriteAllBytes(deployPath, afterChunk3);
            manifest.DeployPath = deployPath;
            Console.WriteLine($"deployed     : {deployPath} ({afterChunk3.Length} bytes)");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"deploy failed: {ex.Message}");
            return 2;
        }

        string manifestOut = Path.Combine(deployDir, battleId + ".json");
        manifest.BattleId = battleId;
        File.WriteAllText(manifestOut, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"manifest     : {manifestOut}");
        Console.WriteLine($"Next: F7 -> Arena+ -> CustomMix Ultra -> Load -> {battleId}");
        return 0;
    }

    private sealed class ComposeManifest
    {
        [JsonPropertyName("schema")] public string Schema { get; set; } = "";
        [JsonPropertyName("actor_count")] public int ActorCount { get; set; }
        [JsonPropertyName("scenario_key")] public string ScenarioKey { get; set; } = "";
        [JsonPropertyName("scenario_label")] public string ScenarioLabel { get; set; } = "";
        [JsonPropertyName("source_template_id")] public string SourceTemplateId { get; set; } = "";
        [JsonPropertyName("camera_chunk0_template_id")] public string CameraChunk0TemplateId { get; set; } = "";
        [JsonPropertyName("picks")] public List<string> Picks { get; set; } = new();
        [JsonPropertyName("boss_labels")] public List<string> BossLabels { get; set; } = new();
        [JsonPropertyName("chunk2_slots")] public List<string> Chunk2Slots { get; set; } = new();
        [JsonPropertyName("battle_id")] public string BattleId { get; set; } = "";
        [JsonPropertyName("token_f7")] public string TokenF7 { get; set; } = "";
        [JsonPropertyName("field")] public int Field { get; set; }
        [JsonPropertyName("group")] public int Group { get; set; }
        [JsonPropertyName("formation")] public int Formation { get; set; }
        [JsonPropertyName("battlefield_id")] public int BattlefieldId { get; set; }
        [JsonPropertyName("hash")] public string Hash { get; set; } = "";
        [JsonPropertyName("deploy_path")] public string DeployPath { get; set; } = "";
        [JsonPropertyName("dry_run")] public bool DryRun { get; set; }
        [JsonPropertyName("composed_at")] public string ComposedAt { get; set; } = "";
    }
}

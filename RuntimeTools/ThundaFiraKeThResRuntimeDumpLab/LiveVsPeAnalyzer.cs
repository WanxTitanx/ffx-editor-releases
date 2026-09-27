using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

/// <summary>
/// Offline: diff live <c>ppp_dataA</c> (and optional tint region) against on-disk PE slice.
/// </summary>
internal static class LiveVsPeAnalyzer
{
    public const uint DataARva = 0x94B0;
    public const int DataASize = 0x1000;
    public const uint TintVec4Rva = 0x37710;
    public const int TintVec4Size = 0x40;

    public sealed record Vec4Hit(int Offset, int RelToDataA, float R, float G, float B, float A, string Source);

    public sealed record Report(
        string LiveDataAPath,
        string? PeDllPath,
        int DiffBytes,
        int CyanStripDiffs,
        IReadOnlyList<Vec4Hit> LiveTintCandidates,
        IReadOnlyList<Vec4Hit> StaticTintCandidates,
        IReadOnlyList<(int Off, float Live, float Static, float Delta)> TopFloatDeltas,
        IReadOnlyList<string> Notes);

    public static int Run(string sessionRoot, string? peDllPath)
    {
        string? phaseDir = FindBestPhaseDir(sessionRoot);
        if (phaseDir == null)
        {
            Console.WriteLine("FAIL: no phase_* with ppp_dataA.bin under " + sessionRoot);
            return 5;
        }

        string livePath = Path.Combine(phaseDir, "ppp_dataA.bin");
        peDllPath ??= ResolvePeDll(sessionRoot);
        if (peDllPath == null || !File.Exists(peDllPath))
        {
            Console.WriteLine("FAIL: magic_0716.dll not found — pass --pe-dll <path>");
            return 6;
        }

        var report = Analyze(livePath, peDllPath);
        string outDir = Path.GetDirectoryName(phaseDir) ?? sessionRoot;
        string json = Path.Combine(outDir, "live_vs_pe_dataA.json");
        string md = Path.Combine(outDir, "LIVE_VS_PE_DATAA.md");
        WriteJson(report, json);
        WriteMarkdown(report, md);

        Console.WriteLine($"DIFF bytes={report.DiffBytes} cyan={report.CyanStripDiffs}");
        Console.WriteLine($"Live tint hits={report.LiveTintCandidates.Count} static={report.StaticTintCandidates.Count}");
        Console.WriteLine($"wrote: {json}");
        Console.WriteLine($"wrote: {md}");
        foreach (string n in report.Notes)
            Console.WriteLine($"  {n}");
        return 0;
    }

    public static Report Analyze(string liveDataAPath, string peDllPath)
    {
        byte[] live = File.ReadAllBytes(liveDataAPath);
        byte[] pe = File.ReadAllBytes(peDllPath);
        byte[] staticSlice = new byte[DataASize];
        if (DataARva + DataASize <= pe.Length)
            Array.Copy(pe, (int)DataARva, staticSlice, 0, DataASize);

        int diffs = 0;
        int cyanDiffs = 0;
        int len = Math.Min(live.Length, staticSlice.Length);
        for (int i = 0; i < len; i++)
        {
            if (live[i] == staticSlice[i])
                continue;
            diffs++;
            if (i is >= MagicAbilityDataADiffAnalyzer.CyanRelStart and <= MagicAbilityDataADiffAnalyzer.CyanRelEnd)
                cyanDiffs++;
        }

        var liveHits = ScanThundaFiraTint(live, "live_dataA");
        var staticHits = ScanThundaFiraTint(staticSlice, "pe_dataA_slice");

        var floatDeltas = new List<(int, float, float, float)>();
        for (int o = 0; o + 4 <= len; o += 4)
        {
            float fl = BitConverter.ToSingle(live, o);
            float fs = BitConverter.ToSingle(staticSlice, o);
            if (float.IsNaN(fl) || float.IsNaN(fs) || float.IsInfinity(fl) || float.IsInfinity(fs))
                continue;
            float d = Math.Abs(fl - fs);
            if (d < 0.001f)
                continue;
            if (fl is < -0.05f or > 1.6f && fs is < -0.05f or > 1.6f)
                continue;
            floatDeltas.Add((o, fl, fs, d));
        }

        var notes = new List<string>
        {
            $"live nz={live.Count(b => b != 0)} static nz={staticSlice.Count(b => b != 0)}",
            diffs > 0
                ? $"runtime ppp_dataA differs from PE on-disk by {diffs} bytes — reloc/decode before draw."
                : "IDENTICAL — rare; PE slice matches live at capture instant.",
        };

        if (liveHits.Count == 0)
            notes.Add("No (1,0.3,0.5,0.8) ThundaFira tint in live 4KiB dataA window — tint likely outside RVA 0x94B0..0xA4B0 (see tint_vec4 @ 0x37710).");

        if ((int)TintVec4Rva + 16 <= pe.Length)
        {
            float[] peTint = ReadVec4(pe, (int)TintVec4Rva);
            notes.Add($"PE file @0x{TintVec4Rva:X}: ({peTint[0]:F3},{peTint[1]:F3},{peTint[2]:F3},{peTint[3]:F3}) — NOT in dataA dump; needs tint_vec4_canonical region capture.");
        }

        string? tintLive = Path.Combine(Path.GetDirectoryName(liveDataAPath) ?? "", "tint_vec4_canonical.bin");
        if (File.Exists(tintLive))
        {
            byte[] lt = File.ReadAllBytes(tintLive);
            if (lt.Length >= 16)
            {
                float[] lv = ReadVec4(lt, 0);
                float[] pv = ReadVec4(pe, (int)TintVec4Rva);
                notes.Add($"LIVE tint_vec4 @0x{TintVec4Rva:X}: ({lv[0]:F3},{lv[1]:F3},{lv[2]:F3},{lv[3]:F3}) vs PE ({pv[0]:F3},{pv[1]:F3},{pv[2]:F3},{pv[3]:F3})");
            }
        }

        return new Report(
            liveDataAPath,
            peDllPath,
            diffs,
            cyanDiffs,
            liveHits,
            staticHits,
            floatDeltas.OrderByDescending(x => x.Item4).Take(24).ToList(),
            notes);
    }

    static string? FindBestPhaseDir(string sessionRoot)
    {
        string? best = null;
        int bestNz = -1;
        foreach (string path in Directory.EnumerateFiles(sessionRoot, "ppp_dataA.bin", SearchOption.AllDirectories))
        {
            byte[] data = File.ReadAllBytes(path);
            int nz = data.Count(b => b != 0);
            if (nz > bestNz)
            {
                bestNz = nz;
                best = Path.GetDirectoryName(path);
            }
        }

        return best;
    }

    static string? ResolvePeDll(string sessionRoot)
    {
        string? manifest = Directory.GetFiles(sessionRoot, "ability_dump_manifest.json", SearchOption.AllDirectories)
            .FirstOrDefault();
        if (manifest != null)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
                if (doc.RootElement.TryGetProperty("magicId", out var id))
                {
                    int magicId = id.GetInt32();
                    string? steam = FindSteamMagicDll(magicId);
                    if (steam != null)
                        return steam;
                }
            }
            catch
            {
                // ignore
            }
        }

        return FindSteamMagicDll(716);
    }

    static string? FindSteamMagicDll(int magicId)
    {
        string name = $"magic_{magicId:D4}.dll";
        string[] roots =
        [
            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\magicFiles\FFX",
            @"C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY X&X-2 HD Remaster\magicFiles\FFX",
        ];
        foreach (string root in roots)
        {
            string path = Path.Combine(root, name);
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    static List<Vec4Hit> ScanThundaFiraTint(byte[] buf, string source)
    {
        var hits = new List<Vec4Hit>();
        for (int o = 0; o + 16 <= buf.Length; o += 4)
        {
            float r = BitConverter.ToSingle(buf, o);
            float g = BitConverter.ToSingle(buf, o + 4);
            float b = BitConverter.ToSingle(buf, o + 8);
            float a = BitConverter.ToSingle(buf, o + 12);
            if (IsThundaFiraTint(r, g, b, a))
                hits.Add(new Vec4Hit(o, o, r, g, b, a, source));
        }

        return hits;
    }

    static bool IsThundaFiraTint(float r, float g, float b, float a) =>
        r is >= 0.95f and <= 1.05f
        && g is >= 0.25f and <= 0.35f
        && b is >= 0.45f and <= 0.55f
        && a is >= 0.75f and <= 0.85f;

    static float[] ReadVec4(byte[] buf, int off) =>
        [BitConverter.ToSingle(buf, off), BitConverter.ToSingle(buf, off + 4),
         BitConverter.ToSingle(buf, off + 8), BitConverter.ToSingle(buf, off + 12)];

    static void WriteJson(Report r, string path)
    {
        var obj = new
        {
            liveDataA = r.LiveDataAPath,
            peDll = r.PeDllPath,
            diffBytes = r.DiffBytes,
            cyanStripDiffs = r.CyanStripDiffs,
            liveTint = r.LiveTintCandidates.Select(h => Vec4Json(h)),
            staticTint = r.StaticTintCandidates.Select(h => Vec4Json(h)),
            topFloatDeltas = r.TopFloatDeltas.Select(t => new
            {
                rel = $"0x{t.Off:X}",
                live = t.Live,
                pe = t.Static,
                delta = t.Delta,
            }),
            notes = r.Notes,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
    }

    static object Vec4Json(Vec4Hit h) => new
    {
        rel = $"0x{h.RelToDataA:X}",
        h.R,
        h.G,
        h.B,
        h.A,
        h.Source,
    };

    static void WriteMarkdown(Report r, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Live ppp_dataA vs PE on-disk — ThundaFira phase_00");
        sb.AppendLine();
        sb.AppendLine($"- **Live:** `{r.LiveDataAPath}`");
        sb.AppendLine($"- **PE:** `{r.PeDllPath}`");
        sb.AppendLine($"- **Diff bytes:** {r.DiffBytes} / {DataASize} (cyan strip: {r.CyanStripDiffs})");
        sb.AppendLine();
        sb.AppendLine("## Verdict");
        foreach (string n in r.Notes)
            sb.AppendLine($"- {n}");
        sb.AppendLine();
        sb.AppendLine("## Top float deltas (live vs PE slice @ RVA 0x94B0)");
        sb.AppendLine();
        sb.AppendLine("| Rel | Live | PE | Δ |");
        sb.AppendLine("|-----|------|----|---|");
        foreach (var t in r.TopFloatDeltas)
            sb.AppendLine($"| `+0x{t.Off:X}` | {t.Live:F4} | {t.Static:F4} | {t.Delta:F4} |");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }
}

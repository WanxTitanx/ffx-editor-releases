using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FFXProjectEditor.Services;

/// <summary>Diff runtime <c>ppp_dataA</c> snapshots (716 vs 94) — find bytes the engine actually mutates.</summary>
internal static class MagicAbilityDataADiffAnalyzer
{
    public const int CyanRelStart = 0x58C;
    public const int CyanRelEnd = 0x620;

    public sealed record RegionDiff(
        int Offset,
        int RelToDataA,
        byte Left,
        byte Right,
        string Note);

    public sealed record FloatDiff(
        int Offset,
        int RelToDataA,
        float Left,
        float Right,
        string Note);

    public sealed record DiffReport(
        string LeftLabel,
        string RightLabel,
        string LeftPath,
        string RightPath,
        int Length,
        int DiffByteCount,
        int CyanStripDiffs,
        IReadOnlyList<RegionDiff> ByteDiffs,
        IReadOnlyList<FloatDiff> FloatDiffs,
        IReadOnlyList<string> Notes);

    public static DiffReport CompareFiles(string leftPath, string rightPath, string leftLabel, string rightLabel)
    {
        byte[] left = File.ReadAllBytes(leftPath);
        byte[] right = File.ReadAllBytes(rightPath);
        int len = Math.Min(left.Length, right.Length);
        var notes = new List<string>();
        var byteDiffs = new List<RegionDiff>();
        var floatDiffs = new List<FloatDiff>();

        for (int i = 0; i < len; i++)
        {
            if (left[i] == right[i])
                continue;
            int rel = i;
            byteDiffs.Add(new RegionDiff(i, rel, left[i], right[i], ClassifyByteDiff(rel)));
        }

        for (int off = 0; off + 16 <= len; off += 4)
        {
            float fl = BitConverter.ToSingle(left, off);
            float fr = BitConverter.ToSingle(right, off);
            if (float.IsNaN(fl) || float.IsNaN(fr) || float.IsInfinity(fl) || float.IsInfinity(fr))
                continue;
            if (Math.Abs(fl - fr) < 0.0001f)
                continue;
            if (!IsPlausibleColorComponent(fl) && !IsPlausibleColorComponent(fr))
                continue;
            floatDiffs.Add(new FloatDiff(off, off, fl, fr, ClassifyByteDiff(off)));
        }

        int cyan = byteDiffs.Count(d => d.RelToDataA is >= CyanRelStart and <= CyanRelEnd);
        if (byteDiffs.Count == 0)
            notes.Add("IDENTICAL — runtime ppp_dataA same for both casts (color not in static PPP payload at cast).");
        else
            notes.Add($"diff bytes: {byteDiffs.Count}, cyan strip: {cyan}, float vec4 hits: {floatDiffs.Count}");

        return new DiffReport(
            leftLabel,
            rightLabel,
            leftPath,
            rightPath,
            len,
            byteDiffs.Count,
            cyan,
            byteDiffs.Take(128).ToList(),
            floatDiffs.OrderByDescending(f => Math.Abs(f.Left - f.Right)).Take(32).ToList(),
            notes);
    }

    public static string? FindBestDataABin(string castStepDir)
    {
        string? bestPath = null;
        int bestNz = -1;
        foreach (string path in Directory.EnumerateFiles(castStepDir, "ppp_dataA.bin", SearchOption.AllDirectories))
        {
            byte[] data = File.ReadAllBytes(path);
            int nz = data.Count(b => b != 0);
            if (nz > bestNz)
            {
                bestNz = nz;
                bestPath = path;
            }
        }

        return bestPath;
    }

    public static void WriteJson(DiffReport r, string path)
    {
        var obj = new
        {
            left = r.LeftLabel,
            right = r.RightLabel,
            leftPath = r.LeftPath,
            rightPath = r.RightPath,
            length = r.Length,
            diffBytes = r.DiffByteCount,
            cyanStripDiffs = r.CyanStripDiffs,
            floatDiffs = r.FloatDiffs.Select(f => new
            {
                off = $"0x{f.Offset:X}",
                rel = $"0x{f.RelToDataA:X}",
                left = f.Left,
                right = f.Right,
                f.Note,
            }),
            byteDiffs = r.ByteDiffs.Take(64).Select(d => new
            {
                off = $"0x{d.Offset:X}",
                rel = $"0x{d.RelToDataA:X}",
                left = d.Left,
                right = d.Right,
                d.Note,
            }),
            notes = r.Notes,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void WriteMarkdown(DiffReport r, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# ppp_dataA runtime diff — {r.LeftLabel} vs {r.RightLabel}");
        sb.AppendLine();
        sb.AppendLine($"- **Left:** `{r.LeftPath}`");
        sb.AppendLine($"- **Right:** `{r.RightPath}`");
        sb.AppendLine($"- **Diff bytes:** {r.DiffByteCount} / {r.Length}");
        sb.AppendLine($"- **Cyan strip (`0x{CyanRelStart:X}`..`0x{CyanRelEnd:X}`):** {r.CyanStripDiffs}");
        sb.AppendLine();
        sb.AppendLine("## Float vec4 deltas (color candidates)");
        sb.AppendLine();
        if (r.FloatDiffs.Count == 0)
            sb.AppendLine("_No plausible float vec4 deltas._");
        else
        {
            sb.AppendLine("| Rel | L | R | Note |");
            sb.AppendLine("|-----|---|---|------|");
            foreach (FloatDiff f in r.FloatDiffs)
                sb.AppendLine($"| `+0x{f.RelToDataA:X}` | {f.Left:F3} | {f.Right:F3} | {f.Note} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Notes");
        foreach (string n in r.Notes)
            sb.AppendLine($"- {n}");

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    static string ClassifyByteDiff(int rel)
    {
        if (rel is >= CyanRelStart and <= CyanRelEnd)
            return "cyan_strip";
        if (rel is >= 0x350 and <= 0x430)
            return "pe_strings";
        if (rel < 0x100)
            return "header";
        return "dataA";
    }

    static bool IsPlausibleColorComponent(float v) => v is >= -0.05f and <= 1.6f;
}

internal static class DataADiffSequenceLab
{
    const uint KeThResHandleRva = 0xB64D4;
    const uint DataARva = 0x94B0;
    const int DataASize = 0x1000;

    public static async Task<int> RunSequenceThenDiffAsync(
        FfxProbe_Service probe,
        string sessionDir,
        int windowMs,
        int snapshotMs,
        int maxWaitSec,
        int pollMs)
    {
        Directory.CreateDirectory(sessionDir);

        FfxProbe_Service.ProbeResult hook = probe.KeThResAttachLogStart();
        if (!hook.Ok || hook.Ret == 0)
        {
            Console.WriteLine("FAIL: KeThRes hook nao instalou — FFX precisa de ffx-probe.dll em modules\\");
            Console.WriteLine("      rode: RuntimeTools\\FfxDinput8Probe\\build.ps1 -Deploy (jogo FECHADO)");
            return 3;
        }

        Console.WriteLine($"HOOK: sub_72C570 OK (ret={hook.Ret})");
        Console.WriteLine("=== ppp_dataA DIFF via KeThRes attach (716 -> 94 -> 716) ===");
        Console.WriteLine("ARMED — batalha + fila 716 -> 94 -> 716 (1x cada)");
        Console.WriteLine();

        try
        {
            int code = await CaptureViaKeThResAsync(probe, sessionDir, maxWaitSec, pollMs);
            if (code != 0)
                return code;
            return DiffSession(sessionDir);
        }
        finally
        {
            probe.KeThResAttachLogStop();
        }
    }

    static async Task<int> CaptureViaKeThResAsync(
        FfxProbe_Service probe,
        string sessionDir,
        int maxWaitSec,
        int pollMs)
    {
        int[] sequence = [716, 94, 716];
        string[] labels = ["ThundaFira", "Thundaga", "ThundaFira"];
        int step = 0;
        bool sawMiddle = false;
        uint drainCursor = 0;
        uint frameWatermark = probe.Heartbeat;
        var deadline = DateTime.UtcNow.AddSeconds(maxWaitSec);
        int pass = 0;
        var steps = new List<object>();

        while (step < sequence.Length && DateTime.UtcNow < deadline)
        {
            int expectId = sequence[step];
            string label = labels[step];

            (IReadOnlyList<FfxProbe_Service.KeThResAttachRecord> batch, uint total) =
                probe.KeThResAttachLogDrain(drainCursor);
            bool advanced = false;

            foreach (FfxProbe_Service.KeThResAttachRecord rec in batch)
            {
                drainCursor++;
                if (rec.Frame <= frameWatermark)
                    continue;
                if (!IsMagicDllKeThResAttach(rec))
                    continue;

                uint derivedBase = rec.HandlePtr - KeThResHandleRva;
                ProcessMagicModuleResolver.MagicModule? mod =
                    ProcessMagicModuleResolver.ListMagicModules()
                        .FirstOrDefault(m => m.Base == derivedBase)
                    ?? ProcessMagicModuleResolver.TryFindMagicModule(expectId);

                if (mod == null || mod.MagicId != expectId)
                    continue;
                if (step == sequence.Length - 1 && sequence[0] == sequence[^1] && !sawMiddle)
                    continue;

                Console.WriteLine();
                Console.WriteLine($"CAST {step + 1}/3: {label} magic_{mod.MagicId:D4} @ 0x{mod.Base:X8} frame={rec.Frame}");

                string castDir = Path.Combine(sessionDir, $"{step + 1:D2}_{expectId:D4}_{label.ToLowerInvariant()}");
                int c = await DumpDataAAtCastAsync(probe, mod, castDir, rec);
                if (c == 0)
                {
                    pass++;
                    steps.Add(new { step = step + 1, label, magicId = expectId, mod.Base, code = c });
                }

                if (step == 1)
                    sawMiddle = true;
                step++;
                advanced = true;
                break;
            }

            if (!advanced)
            {
                Console.Write(
                    $"\r  cast {step + 1}/3 aguardando magic_{expectId:D4} | hb={probe.Heartbeat} ring={total}   ");
            }

            await Task.Delay(pollMs);
        }

        Console.WriteLine();
        string manifestPath = Path.Combine(sessionDir, "dataa_diff_capture_manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            mode = "dataa_kethres_attach",
            sequence,
            pass,
            steps,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"wrote: {manifestPath}");
        Console.WriteLine(pass >= 2 ? $"CAPTURE OK {pass}/3" : $"CAPTURE FAIL {pass}/3 — probe anexou? castou na fila?");
        return pass >= 2 ? 0 : 4;
    }

    static async Task<int> DumpDataAAtCastAsync(
        FfxProbe_Service probe,
        ProcessMagicModuleResolver.MagicModule mod,
        string castDir,
        FfxProbe_Service.KeThResAttachRecord rec)
    {
        Directory.CreateDirectory(castDir);
        uint addr = mod.Base + DataARva;
        FfxProbe_Service.ProbeResult read = probe.ReadAbsoluteBuffered(addr, DataASize);
        byte[] data = read.Data ?? Array.Empty<byte>();
        int nz = data.Count(b => b != 0);
        string sha = data.Length == 0
            ? ""
            : Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

        string path = Path.Combine(castDir, "ppp_dataA.bin");
        if (data.Length > 0)
            await File.WriteAllBytesAsync(path, data);

        var manifest = new
        {
            mod.MagicId,
            moduleBase = $"0x{mod.Base:X8}",
            dataARva = $"0x{DataARva:X}",
            dataASize = DataASize,
            nonZero = nz,
            sha256 = sha,
            probeOk = read.Ok,
            probeError = read.Error,
            attach = new
            {
                frame = rec.Frame,
                buffer = $"0x{rec.BufferPtr:X8}",
                size = $"0x{rec.Size:X}",
                handle = $"0x{rec.HandlePtr:X8}",
            },
        };
        await File.WriteAllTextAsync(
            Path.Combine(castDir, "dataa_cast_manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"  dataA -> {path} nz={nz} probeOk={read.Ok} sha={sha[..Math.Min(16, sha.Length)]}");
        return read.Ok && nz > 64 ? 0 : 4;
    }

    static bool IsMagicDllKeThResAttach(FfxProbe_Service.KeThResAttachRecord r) =>
        r.BufferPtr != 0
        && r.HandlePtr != 0
        && r.Size == 0x100000
        && r.BufferPtr == r.HandlePtr + 0x3C;

    public static int DiffLatestSession(string sessionRoot)
    {
        string? session = Directory.GetDirectories(sessionRoot, "session_*")
            .OrderByDescending(Directory.GetCreationTimeUtc)
            .FirstOrDefault();
        if (session == null)
        {
            Console.WriteLine("FAIL: no session_* under " + sessionRoot);
            return 5;
        }

        return DiffSession(session);
    }

    public static int DiffSession(string sessionDir)
    {
        string? leftDir = Directory.GetDirectories(sessionDir, "01_0716_*").FirstOrDefault()
            ?? Directory.GetDirectories(sessionDir, "*_0716_*").FirstOrDefault();
        string? rightDir = Directory.GetDirectories(sessionDir, "02_0094_*").FirstOrDefault()
            ?? Directory.GetDirectories(sessionDir, "*_0094_*").FirstOrDefault();

        if (leftDir == null || rightDir == null)
        {
            Console.WriteLine($"FAIL: need 0716 + 0094 step dirs in {sessionDir}");
            return 6;
        }

        string? leftBin = MagicAbilityDataADiffAnalyzer.FindBestDataABin(leftDir);
        string? rightBin = MagicAbilityDataADiffAnalyzer.FindBestDataABin(rightDir);
        if (leftBin == null || rightBin == null)
        {
            Console.WriteLine("FAIL: ppp_dataA.bin not found — capture nao rodou ou probe read falhou");
            return 7;
        }

        var report = MagicAbilityDataADiffAnalyzer.CompareFiles(
            leftBin, rightBin, "ThundaFira_716", "Thundaga_94");

        string json = Path.Combine(sessionDir, "ppp_dataA_diff_716_vs_94.json");
        string md = Path.Combine(sessionDir, "PPP_DATAA_DIFF_716_VS_94.md");
        MagicAbilityDataADiffAnalyzer.WriteJson(report, json);
        MagicAbilityDataADiffAnalyzer.WriteMarkdown(report, md);

        Console.WriteLine($"DIFF bytes={report.DiffByteCount} cyan={report.CyanStripDiffs} float={report.FloatDiffs.Count}");
        Console.WriteLine($"wrote: {json}");
        Console.WriteLine($"wrote: {md}");
        if (report.DiffByteCount == 0)
            Console.WriteLine("VERDICT: IDENTICAL — cor do raio NAO mora em ppp_dataA estatico no cast; proximo: sub_71B980 runtime tint");
        else
            Console.WriteLine("VERDICT: PASS — offsets diferentes entre 716 e 94");
        return report.DiffByteCount > 0 ? 0 : 8;
    }
}

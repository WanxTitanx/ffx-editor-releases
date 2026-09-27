using System.Globalization;
using System.Text;
using System.Text.Json;
using FFXProjectEditor.Services;

/// <summary>Arms ffx-probe PPP draw tint hook; logs/forces vec4 on <c>sub_71B980</c> path.</summary>
internal static class PppDrawTintCaptureLab
{
    public static async Task<int> RunAsync(
        FfxProbe_Service probe,
        string outputDir,
        bool forceTint,
        int maxWaitSec,
        int pollMs,
        float r = 1f,
        float g = 0.35f,
        float b = 0.05f)
    {
        Directory.CreateDirectory(outputDir);

        if (FfxProbe_Service.PppDrawTintRetired)
        {
            Console.WriteLine("APOSENTADO: hook inline EXE sub_71B980 (PPPDRAW opcodes 14-16).");
            Console.WriteLine("  Ver: docs/reverse/FFX_THUNDAFIRA_DEAD_ENDS_INDEX_2026-06-15.md (H7-H9)");
            Console.WriteLine("  Use: --ability-dump / --dataa-diff-sequence / IDA magic_0716.dll");
            return 4;
        }

        FfxProbe_Service.ProbeResult start = default;
        uint hookAddr = 0;
        var hookDeadline = DateTime.UtcNow.AddSeconds(Math.Min(120, maxWaitSec));
        while (DateTime.UtcNow < hookDeadline)
        {
            start = probe.PppDrawTintLogStart(forceTint, r, g, b);
            if (start.Ok && start.Ret != 0)
            {
                hookAddr = probe.PppDrawHookAddress;
                break;
            }
            Console.Write($"\r  aguardando sub_71B980 hook... hb={probe.Heartbeat}   ");
            await Task.Delay(500);
        }

        if (!start.Ok || start.Ret == 0)
        {
            Console.WriteLine();
            Console.WriteLine("FAIL: hook sub_71B980 nao instalou — FFX em batalha com probe?");
            Console.WriteLine("       NAO use --force-tint ate log-only registrar hits.");
            return 3;
        }

        if (forceTint)
            Console.WriteLine("WARN: force-tint so e seguro apos log-only com hits > 0");

        Console.WriteLine();
        Console.WriteLine("=== PPP draw tint capture ===");
        Console.WriteLine($"hook: 0x{hookAddr:X8} (sub_71B980 tint a3+4, force={forceTint} rgb={r:F2},{g:F2},{b:F2})");
        Console.WriteLine("ARMED — casta ThundaFira (716) AGORA.");
        Console.WriteLine();

        var all = new List<FfxProbe_Service.PppDrawTintRecord>();
        uint cursor = 0;
        var deadline = DateTime.UtcNow.AddSeconds(maxWaitSec);
        DateTime? lastNewHit = null;

        while (DateTime.UtcNow < deadline)
        {
            (IReadOnlyList<FfxProbe_Service.PppDrawTintRecord> batch, uint total) =
                probe.PppDrawTintLogDrain(cursor);
            int prev = all.Count;
            foreach (FfxProbe_Service.PppDrawTintRecord rec in batch)
                all.Add(rec);
            cursor = total;

            if (all.Count > prev)
                lastNewHit = DateTime.UtcNow;

            if (all.Count > 0)
                Console.Write($"\r  hits={all.Count} ring={total} hb={probe.Heartbeat}   ");
            else
                Console.Write($"\r  aguardando cast... ring={total} hb={probe.Heartbeat}   ");

            /* Wait for spell tail (explosion) — do not STOP while PPP draws still fire. */
            if (all.Count >= 4 && lastNewHit.HasValue &&
                (DateTime.UtcNow - lastNewHit.Value).TotalSeconds >= 3.0)
                break;

            await Task.Delay(pollMs);
        }

        probe.PppDrawTintLogStop();
        Console.WriteLine("(hook permanece ate fechar FFX — evita crash pos-explosao)");
        Console.WriteLine();

        var blueHits = all.Where(IsBlueish).ToList();
        var manifest = new
        {
            mode = forceTint ? "pppdraw_force_tint" : "pppdraw_log",
            hookRva = $"0x{hookAddr:X8}",
            forceTint,
            rgb = new { r, g, b },
            recordCount = all.Count,
            blueishCount = blueHits.Count,
            records = all.Take(64).Select(x => new
            {
                frame = x.Frame,
                a0 = $"0x{x.A0:X8}",
                a1 = $"0x{x.A1:X8}",
                a2 = $"0x{x.A2:X8}",
                a3 = $"0x{x.A3:X8}",
                tint = $"{x.TintR:F3},{x.TintG:F3},{x.TintB:F3},{x.TintA:F3}",
            }),
        };

        string jsonPath = Path.Combine(outputDir, "pppdraw_tint_capture.json");
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        await File.WriteAllTextAsync(Path.Combine(outputDir, "PPPDRAW_TINT_CAPTURE.md"), BuildMarkdown(all, blueHits, forceTint));

        Console.WriteLine($"records: {all.Count} (blueish={blueHits.Count})");
        Console.WriteLine($"wrote: {jsonPath}");
        if (all.Count == 0)
        {
            Console.WriteLine("VERDICT: FAIL — nenhum hit no hook (castou 716 em batalha?)");
            return 5;
        }

        if (forceTint)
            Console.WriteLine("VERDICT: PARTIAL — olhe os raios; laranja = path provado");
        else
            Console.WriteLine("VERDICT: PASS — log capturado; use --force-tint para RT2 visual");
        return 0;
    }

    static bool IsBlueish(FfxProbe_Service.PppDrawTintRecord r) =>
        r.TintB >= 0.45f && r.TintR <= 0.55f;

    static string BuildMarkdown(
        IReadOnlyList<FfxProbe_Service.PppDrawTintRecord> all,
        IReadOnlyList<FfxProbe_Service.PppDrawTintRecord> blue,
        bool forced)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# PPP draw tint capture — `FFX+0x31B980` (sub_71B980 ApplyPppDrawableColors)");
        sb.AppendLine();
        sb.AppendLine($"- **Records:** {all.Count}");
        sb.AppendLine($"- **Blueish tint hits:** {blue.Count}");
        sb.AppendLine($"- **Force mode:** {forced}");
        sb.AppendLine();
        sb.AppendLine("| Frame | a2 | Tint RGB |");
        sb.AppendLine("|-------|-----|----------|");
        foreach (FfxProbe_Service.PppDrawTintRecord r in all.Take(32))
            sb.AppendLine($"| {r.Frame} | `0x{r.A2:X8}` | {r.TintR:F3}, {r.TintG:F3}, {r.TintB:F3} |");
        return sb.ToString();
    }
}

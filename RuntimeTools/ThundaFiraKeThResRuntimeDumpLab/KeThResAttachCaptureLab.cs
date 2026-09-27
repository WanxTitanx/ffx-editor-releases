using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using FFXProjectEditor.Services;

/// <summary>
/// Arms ffx-probe KeThRes attach hook; dumps decoded buffer on the same poll tick (no delay).
/// </summary>
internal static class KeThResAttachCaptureLab
{
    public const uint KeThResBlobRva = 0xB6510;
    public const uint KeThResHandleRva = 0xB64D4;
    public const uint MinBoltBufferSize = 0x80000;
    public const int DefaultDumpBytes = 0x4000;

    public static Task<int> RunAsync(
        FfxProbe_Service probe,
        int magicId,
        string outputDir,
        int maxWaitSec,
        int pollMs,
        int dumpBytes = DefaultDumpBytes,
        int[]? sequence = null) =>
        sequence is { Length: > 0 }
            ? RunSequenceAsync(probe, sequence, outputDir, maxWaitSec, pollMs, dumpBytes)
            : RunSingleAsync(probe, magicId, outputDir, maxWaitSec, pollMs, dumpBytes);

    static async Task<int> RunSingleAsync(
        FfxProbe_Service probe,
        int magicId,
        string outputDir,
        int maxWaitSec,
        int pollMs,
        int dumpBytes)
    {
        Console.WriteLine("=== KeThRes attach capture (single) ===");
        return await RunCaptureWindowAsync(probe, magicId, "cast", outputDir, maxWaitSec, pollMs, dumpBytes, armHook: true, disarmHook: true);
    }

    static async Task<int> RunSequenceAsync(
        FfxProbe_Service probe,
        int[] sequence,
        string outputDir,
        int maxWaitSec,
        int pollMs,
        int dumpBytes)
    {
        string[] labels = ["ThundaFira", "Thundaga", "ThundaFira"];
        Console.WriteLine("=== KeThRes attach capture (sequence) ===");
        Console.WriteLine($"queue: {string.Join(" -> ", sequence.Select((id, i) => i < labels.Length ? labels[i] : $"cast{i + 1}"))}");
        Console.WriteLine("ARMED — casta as 3 na mesma fila AGORA.");
        Console.WriteLine();

        FfxProbe_Service.ProbeResult start = probe.KeThResAttachLogStart();
        if (!start.Ok || start.Ret == 0)
        {
            Console.WriteLine("FAIL: KeThRes hook did not install — rebuild ffx-probe.dll?");
            return 3;
        }

        Console.WriteLine($"hook: installed @ FFX+0x32C570 (ret={start.Ret})");

        int pass = 0;
        var steps = new List<object>();
        int? blockAbsent = null;
        bool sawMiddle = false;
        var presentLast = GetPresentIds();
        var deadline = DateTime.UtcNow.AddSeconds(maxWaitSec);

        for (int step = 0; step < sequence.Length && DateTime.UtcNow < deadline; )
        {
            int expectId = sequence[step];
            string label = step < labels.Length ? labels[step] : $"cast{step + 1}";

            if (step == sequence.Length - 1 && sequence[0] == sequence[^1] && !sawMiddle)
            {
                await Task.Delay(pollMs);
                continue;
            }

            if (blockAbsent.HasValue && ProcessMagicModuleResolver.TryFindMagicModule(blockAbsent.Value) != null)
            {
                presentLast = GetPresentIds();
                await Task.Delay(pollMs);
                continue;
            }

            blockAbsent = null;
            var presentNow = GetPresentIds();
            bool justLoaded = presentNow.Contains(expectId) && !presentLast.Contains(expectId);

            if (!justLoaded)
            {
                presentLast = presentNow;
                await Task.Delay(pollMs);
                continue;
            }

            Console.WriteLine($"CAST {step + 1}/{sequence.Length}: {label} — magic_{expectId:D4}.dll");
            string castDir = Path.Combine(outputDir, $"{step + 1:D2}_{expectId:D4}_{label.ToLowerInvariant()}");
            probe.KeThResAttachLogStop();
            probe.KeThResAttachLogStart();
            int code = await RunCaptureWindowAsync(
                probe, expectId, label, castDir, Math.Min(45, maxWaitSec), pollMs, dumpBytes,
                armHook: false, disarmHook: false);

            if (code == 0) pass++;
            steps.Add(new { step = step + 1, label, magicId = expectId, code });
            if (step == 1) sawMiddle = true;
            blockAbsent = expectId;
            presentLast = GetPresentIds();
            step++;
        }

        probe.KeThResAttachLogStop();

        string manifestPath = Path.Combine(outputDir, "kethres_sequence_manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            mode = "kethres_attach_sequence",
            sequence,
            pass,
            steps,
        }, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"wrote: {manifestPath}");
        Console.WriteLine(pass > 0 ? $"VERDICT: PASS ({pass}/{sequence.Length})" : "VERDICT: FAIL");
        return pass > 0 ? 0 : 5;
    }

    static async Task<int> RunCaptureWindowAsync(
        FfxProbe_Service probe,
        int magicId,
        string label,
        string outputDir,
        int maxWaitSec,
        int pollMs,
        int dumpBytes,
        bool armHook,
        bool disarmHook)
    {
        Directory.CreateDirectory(outputDir);

        if (armHook)
        {
            FfxProbe_Service.ProbeResult start = probe.KeThResAttachLogStart();
            if (!start.Ok || start.Ret == 0)
            {
                Console.WriteLine("FAIL: KeThRes hook did not install — rebuild ffx-probe.dll?");
                return 3;
            }

            Console.WriteLine($"hook: installed @ FFX+0x32C570 (ret={start.Ret})");
            Console.WriteLine($"wait : magic_{magicId:D4}.dll | dump 0x{dumpBytes:X} on attach tick");
            Console.WriteLine($"out  : {outputDir}");
            Console.WriteLine();
            Console.WriteLine("ARMED — casta AGORA.");
            Console.WriteLine();
        }

        var records = new List<FfxProbe_Service.KeThResAttachRecord>();
        uint drainCursor = 0;
        uint ringTotal = 0;
        ProcessMagicModuleResolver.MagicModule? mod = null;
        byte[]? dumped = null;
        uint dumpAddr = 0;
        string dumpVia = "";
        var deadline = DateTime.UtcNow.AddSeconds(maxWaitSec);
        var initial = ProcessMagicModuleResolver.ListMagicModules().Select(m => m.MagicId).ToHashSet();
        uint frameWatermark = probe.Heartbeat;
        byte[]? bestBurst = null;
        int bestBurstNz = 0;
        uint bestBurstAddr = 0;

        while (DateTime.UtcNow < deadline && dumped == null)
        {
            mod = ProcessMagicModuleResolver.TryFindMagicModule(magicId);
            bool active = mod != null && (!initial.Contains(magicId) || initial.Count == 0);

            if (active && mod != null)
            {
                if (TryDumpBuffer(probe, mod, mod.Base + KeThResBlobRva, dumpBytes, out byte[]? burst, out _))
                {
                    int nz = burst.Count(b => b != 0);
                    if (nz > bestBurstNz)
                    {
                        bestBurstNz = nz;
                        bestBurst = burst;
                        bestBurstAddr = mod.Base + KeThResBlobRva;
                    }
                }

                (IReadOnlyList<FfxProbe_Service.KeThResAttachRecord> batch, uint total) =
                    probe.KeThResAttachLogDrain(drainCursor);
                ringTotal = total;
                foreach (FfxProbe_Service.KeThResAttachRecord r in batch)
                {
                    if (r.Frame <= frameWatermark)
                        continue;
                    if (!IsMagicDllKeThResAttach(r))
                        continue;
                    records.Add(r);
                    drainCursor++;
                }

                FfxProbe_Service.KeThResAttachRecord? hit = records
                    .Where(r => r.Size >= MinBoltBufferSize && IsMagicDllKeThResAttach(r))
                    .OrderByDescending(r => r.Frame)
                    .FirstOrDefault();

                if (hit != null)
                {
                    uint derivedBase = hit.HandlePtr - KeThResHandleRva;
                    mod = new ProcessMagicModuleResolver.MagicModule(magicId, derivedBase, mod?.Size ?? 0x200000, $"magic_{magicId:D4}.dll");
                    Console.WriteLine($"HIT  : magic attach frame={hit.Frame} buf=0x{hit.BufferPtr:X8} mod=0x{derivedBase:X8}");
                    if (TryDumpBuffer(probe, mod, hit.BufferPtr, dumpBytes, out byte[]? snap, out dumpVia))
                    {
                        int nz = snap.Count(b => b != 0);
                        if (nz > 64)
                        {
                            dumped = snap;
                            dumpAddr = hit.BufferPtr;
                        }
                    }
                }

                if (dumped == null && bestBurstNz > 64 && bestBurst != null)
                {
                    dumped = bestBurst;
                    dumpAddr = bestBurstAddr;
                    dumpVia = $"burst_poll module+0x{KeThResBlobRva:X} nz={bestBurstNz}";
                    Console.WriteLine($"HIT  : {dumpVia}");
                }

                if (dumped == null)
                {
                    byte[]? snap = probe.KeThResSnapReadFull(out uint snapBuf, out uint snapCount, out int snapNz);
                    if (snap != null && snapNz > 0)
                    {
                        dumped = snap;
                        dumpAddr = snapBuf;
                        dumpVia = $"in_hook_snap events={snapCount} nz={snapNz}";
                        Console.WriteLine($"HIT  : {dumpVia} @ 0x{snapBuf:X8}");
                    }
                }
            }

            if (dumped == null)
                await Task.Delay(pollMs);
        }

        if (dumped == null)
        {
            byte[]? snap = probe.KeThResSnapReadFull(out uint snapBuf, out uint snapCount, out int snapNz);
            if (snap != null && snapNz > 0)
            {
                dumped = snap;
                dumpAddr = snapBuf;
                dumpVia = $"in_hook_snap_tail events={snapCount} nz={snapNz}";
            }
        }

        if (disarmHook)
            probe.KeThResAttachLogStop();

        if (dumped == null)
        {
            (IReadOnlyList<FfxProbe_Service.KeThResAttachRecord> tail, uint t) = probe.KeThResAttachLogDrain(drainCursor);
            ringTotal = t;
            records.AddRange(tail);
        }

        mod ??= ProcessMagicModuleResolver.TryFindMagicModule(magicId);

        var manifest = new
        {
            mode = armHook ? "kethres_attach_capture" : "kethres_attach_step",
            label,
            magicId,
            moduleBase = mod == null ? null : $"0x{mod.Base:X8}",
            blobRva = $"0x{KeThResBlobRva:X}",
            hookRva = "0x32C570",
            recordTotal = ringTotal,
            records = records.Select(r => new
            {
                frame = r.Frame,
                buffer = $"0x{r.BufferPtr:X8}",
                size = $"0x{r.Size:X}",
                handle = $"0x{r.HandlePtr:X8}",
            }),
            dump = dumped == null ? null : new
            {
                address = $"0x{dumpAddr:X8}",
                bytes = dumped.Length,
                via = dumpVia,
                nonZero = dumped.Count(b => b != 0),
            },
        };

        string manifestPath = Path.Combine(outputDir, "kethres_attach_manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"records: {records.Count} | wrote: {manifestPath}");

        if (dumped == null)
        {
            Console.WriteLine("VERDICT: FAIL (no attach / empty blob during window)");
            return 5;
        }

        int nzFinal = dumped.Count(b => b != 0);
        string sha = Convert.ToHexString(SHA256.HashData(dumped)).ToLowerInvariant();
        string dumpPath = Path.Combine(outputDir, $"kethres_decoded_{label}_{dumpAddr:X8}_{dumped.Length:X}.bin");
        await File.WriteAllBytesAsync(dumpPath, dumped);
        Console.WriteLine($"dump : {dumpPath} ({dumped.Length} B, nz={nzFinal}, via={dumpVia}, sha={sha[..16]}…)");
        Console.WriteLine(nzFinal > 0 ? "VERDICT: PASS" : "VERDICT: PARTIAL (all zero)");
        return nzFinal > 0 ? 0 : 7;
    }

    static bool TryDumpBuffer(
        FfxProbe_Service probe,
        ProcessMagicModuleResolver.MagicModule mod,
        uint address,
        int totalLen,
        out byte[]? data,
        out string via)
    {
        data = null;
        via = "";

        if (address == mod.Base + KeThResBlobRva)
            via = $"module+0x{KeThResBlobRva:X}";
        else if (address >= mod.Base && address < mod.Base + mod.Size)
            via = $"module+0x{address - mod.Base:X}";
        else
            via = "absolute";

        var buf = new byte[totalLen];
        int offset = 0;
        while (offset < totalLen)
        {
            int chunk = Math.Min(512, totalLen - offset);
            FfxProbe_Service.ProbeResult r = probe.ReadAbsolute(address + (uint)offset, chunk);
            if (!r.Ok || r.Data == null || r.Data.Length == 0)
            {
                if (offset == 0)
                    return false;
                Array.Resize(ref buf, offset);
                data = buf;
                return true;
            }

            Buffer.BlockCopy(r.Data, 0, buf, offset, r.Data.Length);
            offset += r.Data.Length;
        }

        data = buf;
        return true;
    }

    static bool IsMagicDllKeThResAttach(FfxProbe_Service.KeThResAttachRecord r) =>
        r.BufferPtr != 0
        && r.HandlePtr != 0
        && r.Size == 0x100000
        && r.BufferPtr == r.HandlePtr + 0x3C;

    static HashSet<int> GetPresentIds() =>
        ProcessMagicModuleResolver.ListMagicModules().Select(m => m.MagicId).ToHashSet();
}

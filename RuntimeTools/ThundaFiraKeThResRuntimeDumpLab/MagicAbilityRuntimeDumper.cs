using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FFXProjectEditor.Services;

/// <summary>
/// Full in-game dump of a loaded <c>magic_####.dll</c> during spell execution.
/// Snapshots the mapped module image, IDA-known globals, and pointer-chase targets.
/// </summary>
internal static class MagicAbilityRuntimeDumper
{
    public const uint PreferredImageBase = 0x10000000;

    public sealed record RegionSpec(string Id, uint Rva, int Size, string Note);

    public static readonly RegionSpec[] KnownRegions =
    [
        new("pe_header", 0x0000, 0x400, "MZ/PE headers"),
        new("ppp_dataA", 0x094B0, 0x1000, "pppKeThRes32x4 dataA"),
        new("ppp_dataB", 0x086B0, 0x1000, "pppKeThRes dataB"),
        new("ppp_handlerA", 0xB44E8, 0x200, "PPP handlerA"),
        new("host_ctx_ptr", 0xB6490, 0x40, "dword_100B6490 host context"),
        new("kethres_handle", 0xB64D4, 0x100, "Thundaga_KeThResHandle"),
        new("kethres_blob_inline", 0xB6510, 0x100000, "Thundaga_KeThResBlob inline 1MiB"),
        new("kethres_alt_ptr", 0xB5C2C, 0x10, "dword_100B5C2C alt blob pointer"),
        new("ego_host1072_block", 0xB5FC4, 0x200, "unk_100B5FC4 host+1072 PPP block"),
        new("ppp_catalog", 0x08294, 0x400, "PPP catalog cluster in PE"),
        new("tint_vec4_canonical", 0x37710, 0x40, "ThundaFira tint vec4 (1,0.3,0.5,0.8) + phyre ID tail"),
        new("dataA_cyan_strip", 0x094B0 + 0x58C, 0x98, "dataA cyan vec4 strip post-reloc candidate"),
    ];

    public sealed record DumpedRegion(
        string Id,
        uint Absolute,
        uint Rva,
        int Requested,
        int Dumped,
        int NonZero,
        string Sha256,
        bool ProbeOk,
        uint ProbeError,
        string? File);

    public sealed record Snapshot(
        string Label,
        DateTime Utc,
        uint ProbeHeartbeat,
        uint ModuleBase,
        uint ModuleSize,
        bool PeMagicOk,
        IReadOnlyList<DumpedRegion> Regions,
        IReadOnlyList<PointerChase> PointerChases);

    public sealed record PointerChase(
        string Source,
        uint PointerValue,
        int Dumped,
        int NonZero,
        string Sha256,
        string? File);

    public static async Task<Snapshot> CaptureSnapshotAsync(
        FfxProbe_Service probe,
        ProcessMagicModuleResolver.MagicModule module,
        string outputDir,
        string label)
    {
        Directory.CreateDirectory(outputDir);
        uint b = module.Base;
        var regions = new List<DumpedRegion>();
        var chases = new List<PointerChase>();

        bool peOk = false;
        FfxProbe_Service.ProbeResult mz = probe.ReadAbsolute(b, 2);
        if (mz.Ok && mz.Data is { Length: >= 2 })
            peOk = mz.Data[0] == 0x4D && mz.Data[1] == 0x5A;

        int moduleDumpSize = (int)Math.Min(module.Size, 0x200000u);
        regions.Add(await DumpAbsoluteAsync(probe, outputDir, "module_image_full",
            b, 0, moduleDumpSize, $"toolhelp size 0x{module.Size:X}"));

        foreach (RegionSpec spec in KnownRegions)
        {
            if (spec.Rva == 0 && spec.Id == "pe_header")
                continue;
            regions.Add(await DumpAbsoluteAsync(probe, outputDir, spec.Id,
                b + spec.Rva, spec.Rva, spec.Size, spec.Note));
        }

        chases.AddRange(await ChasePointersAsync(probe, outputDir, b, label));

        return new Snapshot(
            label,
            DateTime.UtcNow,
            probe.Heartbeat,
            b,
            module.Size,
            peOk,
            regions,
            chases);
    }

    static async Task<List<PointerChase>> ChasePointersAsync(
        FfxProbe_Service probe,
        string outputDir,
        uint moduleBase,
        string label)
    {
        var results = new List<PointerChase>();
        var sources = new (string name, uint addr)[]
        {
            ("dword_B5C2C_alt", moduleBase + 0xB5C2C),
            ("dword_B6490_host", moduleBase + 0xB6490),
            ("dword_B6510_head", moduleBase + 0xB6510),
        };

        foreach ((string name, uint addr) in sources)
        {
            uint? ptr = ProbeReadU32(probe, addr);
            if (!IsLikelyHeapPointer(ptr))
                continue;

            string fileName = $"chase_{name}_0x{ptr!.Value:X8}.bin";
            DumpedRegion dump = await DumpAbsoluteAsync(probe, outputDir, fileName,
                ptr.Value, 0, 0x10000, $"chase from {name} @ 0x{addr:X8}");
            results.Add(new PointerChase(
                name,
                ptr.Value,
                dump.Dumped,
                dump.NonZero,
                dump.Sha256,
                dump.File));
        }

        return results;
    }

    static bool IsLikelyHeapPointer(uint? ptr) =>
        ptr is >= 0x01000000 and < 0x7FFE0000;

    static uint? ProbeReadU32(FfxProbe_Service probe, uint addr)
    {
        FfxProbe_Service.ProbeResult r = probe.ReadAbsolute(addr, 4);
        if (!r.Ok || r.Data == null || r.Data.Length < 4)
            return null;
        return BitConverter.ToUInt32(r.Data, 0);
    }

    static async Task<DumpedRegion> DumpAbsoluteAsync(
        FfxProbe_Service probe,
        string outputDir,
        string id,
        uint absolute,
        uint rva,
        int size,
        string note)
    {
        string safeId = id.Replace(' ', '_');
        string path = Path.Combine(outputDir, $"{safeId}.bin");

        FfxProbe_Service.ProbeResult r = probe.ReadAbsoluteBuffered(absolute, size);
        byte[] data = r.Data ?? Array.Empty<byte>();
        int nz = data.Count(b => b != 0);
        string sha = data.Length == 0
            ? Convert.ToHexString(SHA256.HashData(Array.Empty<byte>())).ToLowerInvariant()
            : Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

        if (data.Length > 0)
            await File.WriteAllBytesAsync(path, data);

        return new DumpedRegion(
            id,
            absolute,
            rva,
            size,
            data.Length,
            nz,
            sha,
            r.Ok,
            r.Error,
            data.Length > 0 ? Path.GetFileName(path) : null);
    }

    public static async Task<int> RunAbilityWindowAsync(
        FfxProbe_Service probe,
        ProcessMagicModuleResolver.MagicModule module,
        string sessionDir,
        int windowMs,
        int snapshotMs)
    {
        string castDir = Path.Combine(sessionDir, $"magic_{module.MagicId:D4}_0x{module.Base:X8}");
        Directory.CreateDirectory(castDir);

        int shots = Math.Max(1, windowMs / Math.Max(100, snapshotMs));
        var snapshots = new List<object>();
        int bestNz = 0;
        string? bestLabel = null;

        Console.WriteLine($"  ability window {windowMs}ms / {shots} snapshots -> {castDir}");

        for (int i = 0; i < shots; i++)
        {
            string label = $"phase_{i:D2}";
            string phaseDir = Path.Combine(castDir, label);
            Snapshot snap = await CaptureSnapshotAsync(probe, module, phaseDir, label);

            DumpedRegion? blob = snap.Regions.FirstOrDefault(r => r.Id == "kethres_blob_inline");
            DumpedRegion? dataA = snap.Regions.FirstOrDefault(r => r.Id == "ppp_dataA");
            int blobNz = blob?.NonZero ?? 0;
            int dataANz = dataA?.NonZero ?? 0;
            if (dataANz > bestNz)
            {
                bestNz = dataANz;
                bestLabel = label;
            }

            Console.WriteLine($"    {label} pe={snap.PeMagicOk} dataA_nz={dataANz} blob_nz={blobNz} hb={snap.ProbeHeartbeat}");

            snapshots.Add(new
            {
                label,
                snap.PeMagicOk,
                dataANz,
                blobNz,
                dataASha = dataA?.Sha256,
                blobSha = blob?.Sha256,
                moduleSha = snap.Regions.FirstOrDefault(r => r.Id == "module_image_full")?.Sha256,
                chases = snap.PointerChases.Select(c => new { c.Source, ptr = $"0x{c.PointerValue:X8}", c.NonZero, c.Sha256 })
            });

            if (i + 1 < shots)
                await Task.Delay(snapshotMs);
        }

        var manifest = new
        {
            magicId = module.MagicId,
            moduleBase = $"0x{module.Base:X8}",
            moduleSize = module.Size,
            windowMs,
            snapshotMs,
            bestDataAPhase = bestLabel,
            bestDataANonZero = bestNz,
            snapshots
        };

        string manifestPath = Path.Combine(castDir, "ability_dump_manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        await File.WriteAllTextAsync(Path.Combine(castDir, "ABILITY_DUMP.md"), BuildMarkdown(module, manifest));

        Console.WriteLine($"  wrote: {manifestPath}");
        Console.WriteLine($"  best dataA phase: {bestLabel ?? "none"} nz={bestNz}");
        return bestNz > 64 ? 0 : 4;
    }

    static string BuildMarkdown(ProcessMagicModuleResolver.MagicModule module, object manifest)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Magic ability runtime dump — `magic_{module.MagicId:D4}.dll`");
        sb.AppendLine();
        sb.AppendLine($"- **Base:** `0x{module.Base:X8}`");
        sb.AppendLine($"- **Size:** `0x{module.Size:X}`");
        sb.AppendLine();
        sb.AppendLine("## Known regions (RVA from preferred `0x10000000`)");
        sb.AppendLine();
        sb.AppendLine("| Id | RVA | Size |");
        sb.AppendLine("|----|-----|------|");
        foreach (RegionSpec r in KnownRegions)
            sb.AppendLine($"| `{r.Id}` | `0x{r.Rva:X}` | `0x{r.Size:X}` |");
        sb.AppendLine();
        sb.AppendLine("## Manifest");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        sb.AppendLine("```");
        return sb.ToString();
    }
}

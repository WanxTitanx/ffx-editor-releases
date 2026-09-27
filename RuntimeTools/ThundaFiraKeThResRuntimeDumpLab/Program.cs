// Magic ability in-game dump lab (ffx-probe required).
//
//   --ability-dump --magic-id 716          wait load, snapshot spell window
//   --ability-sequence 716,94,716          3 casts same queue
//   --sniff                                list magic_*.dll while casting

using System.Globalization;
using System.Text.Json;
using FFXProjectEditor.Services;

string outputDir = Path.Combine("work", "thundafira_ability_runtime");
int magicId = 716;
int[]? sequence = null;
bool abilityDump = false;
bool abilitySequence = false;
bool kethresAttachCapture = false;
bool dataaDiffSequence = false;
bool liveVsPe = false;
string? peDllPath = null;
bool pppdrawTintCapture = false;
bool forceTint = false;
float tintR = 1f, tintG = 0.35f, tintB = 0.05f;
int windowMs = 8000;
int snapshotMs = 500;
int maxWaitSec = 120;
int pollMs = 50;

for (int i = 0; i < args.Length; i++)
{
    string a = args[i];
    if (a.Equals("--output", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        outputDir = args[++i];
    else if (a.Equals("--magic-id", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        magicId = int.Parse(args[++i], CultureInfo.InvariantCulture);
    else if (a.Equals("--ability-sequence", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
    {
        sequence = ParseSequence(args[++i]);
        abilitySequence = true;
    }
    else if (a.Equals("--ability-dump", StringComparison.OrdinalIgnoreCase))
        abilityDump = true;
    else if (a.Equals("--window-ms", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        windowMs = int.Parse(args[++i], CultureInfo.InvariantCulture);
    else if (a.Equals("--snapshot-ms", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        snapshotMs = int.Parse(args[++i], CultureInfo.InvariantCulture);
    else if (a.Equals("--max-wait-s", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        maxWaitSec = int.Parse(args[++i], CultureInfo.InvariantCulture);
    else if (a.Equals("--poll-ms", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        pollMs = int.Parse(args[++i], CultureInfo.InvariantCulture);
    else if (a.Equals("--kethres-attach-capture", StringComparison.OrdinalIgnoreCase))
        kethresAttachCapture = true;
    else if (a.Equals("--dataa-diff-sequence", StringComparison.OrdinalIgnoreCase))
        dataaDiffSequence = true;
    else if (a.Equals("--dataa-diff-only", StringComparison.OrdinalIgnoreCase))
        return DataADiffSequenceLab.DiffLatestSession(outputDir);
    else if (a.Equals("--live-vs-pe", StringComparison.OrdinalIgnoreCase))
        liveVsPe = true;
    else if (a.Equals("--pe-dll", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        peDllPath = args[++i];
    else if (a.Equals("--pppdraw-tint-capture", StringComparison.OrdinalIgnoreCase))
        pppdrawTintCapture = true;
    else if (a.Equals("--force-tint", StringComparison.OrdinalIgnoreCase))
        forceTint = true;
    else if (a.Equals("--tint-rgb", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
    {
        string[] p = args[++i].Split(',', StringSplitOptions.TrimEntries);
        tintR = float.Parse(p[0], CultureInfo.InvariantCulture);
        tintG = float.Parse(p[1], CultureInfo.InvariantCulture);
        tintB = float.Parse(p[2], CultureInfo.InvariantCulture);
    }
    else if (a.Equals("--help", StringComparison.OrdinalIgnoreCase) || a.Equals("-h", StringComparison.OrdinalIgnoreCase))
    {
        PrintHelp();
        return 0;
    }
}

if (!abilityDump && !abilitySequence && !kethresAttachCapture && !dataaDiffSequence && !pppdrawTintCapture && !liveVsPe)
    abilityDump = true;

if (liveVsPe)
{
    string session = Directory.Exists(outputDir) && outputDir.Contains("session_")
        ? outputDir
        : Directory.GetDirectories(outputDir, "session_*").OrderByDescending(Directory.GetCreationTimeUtc).FirstOrDefault()
          ?? outputDir;
    return LiveVsPeAnalyzer.Run(session, peDllPath);
}

var probe = FfxProbe_Service.Instance;
if (!probe.IsAttached)
{
    Console.Error.WriteLine("FAIL: probe not attached — FFX + ffx-probe.dll required.");
    Console.Error.WriteLine(ProcessMagicModuleResolver.DescribeFfxProcesses());
    return 2;
}

Directory.CreateDirectory(outputDir);
string sessionDir = Path.Combine(outputDir, $"session_{DateTime.UtcNow:yyyyMMdd_HHmmss}");
Directory.CreateDirectory(sessionDir);

if (dataaDiffSequence)
{
    return await DataADiffSequenceLab.RunSequenceThenDiffAsync(
        probe, sessionDir, windowMs, snapshotMs, maxWaitSec, pollMs);
}

if (pppdrawTintCapture)
{
    return await PppDrawTintCaptureLab.RunAsync(
        probe, sessionDir, forceTint, maxWaitSec, pollMs, tintR, tintG, tintB);
}

if (kethresAttachCapture)
{
    return await KeThResAttachCaptureLab.RunAsync(
        probe, magicId, sessionDir, maxWaitSec, pollMs,
        sequence: abilitySequence ? sequence : null);
}

if (abilitySequence && sequence != null)
    return await RunAbilitySequenceAsync(probe, sequence, sessionDir, windowMs, snapshotMs, maxWaitSec, pollMs);

return await RunSingleAbilityDumpAsync(probe, magicId, sessionDir, windowMs, snapshotMs, maxWaitSec, pollMs);

static async Task<int> RunSingleAbilityDumpAsync(
    FfxProbe_Service probe,
    int magicId,
    string sessionDir,
    int windowMs,
    int snapshotMs,
    int maxWaitSec,
    int pollMs)
{
    Console.WriteLine("=== Magic ABILITY runtime dump (single) ===");
    Console.WriteLine($"probe: hooked={probe.IsHooked} heartbeat={probe.Heartbeat}");
    Console.WriteLine($"wait : magic_{magicId:D4}.dll | window {windowMs}ms | snap {snapshotMs}ms");
    Console.WriteLine($"out  : {sessionDir}");
    Console.WriteLine();
    Console.WriteLine("ARMED — casta a habilidade AGORA.");
    Console.WriteLine();

    ProcessMagicModuleResolver.MagicModule? mod = await WaitForModuleLoadAsync(magicId, maxWaitSec, pollMs);
    if (mod == null)
    {
        Console.WriteLine($"FAIL: magic_{magicId:D4}.dll not seen in {maxWaitSec}s");
        return 4;
    }

    Console.WriteLine($"LOAD: {mod.Name} @ 0x{mod.Base:X8} size=0x{mod.Size:X}");
    return await MagicAbilityRuntimeDumper.RunAbilityWindowAsync(probe, mod, sessionDir, windowMs, snapshotMs);
}

static async Task<int> RunAbilitySequenceAsync(
    FfxProbe_Service probe,
    int[] sequence,
    string sessionDir,
    int windowMs,
    int snapshotMs,
    int maxWaitSec,
    int pollMs)
{
    string[] labels = ["ThundaFira", "Thundaga", "ThundaFira"];
    int pass = 0;
    var steps = new List<object>();
    int? blockAbsent = null;
    bool sawMiddle = false;
    var presentLast = new HashSet<int>();

    Console.WriteLine("=== Magic ABILITY runtime dump (sequence) ===");
    Console.WriteLine($"probe: hooked={probe.IsHooked}");
    Console.WriteLine($"queue: {string.Join(" -> ", sequence.Select((id, i) => i < labels.Length ? labels[i] : $"cast{i + 1}"))}");
    Console.WriteLine($"window: {windowMs}ms per cast | snap {snapshotMs}ms");
    Console.WriteLine($"out  : {sessionDir}");
    Console.WriteLine();
    Console.WriteLine("ARMED — casta as 3 na mesma fila AGORA.");
    Console.WriteLine();

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
            Console.Write($"\rcast {step + 1}/{sequence.Length} unload magic_{blockAbsent.Value:D4}...   ");
            presentLast = GetPresentIds();
            await Task.Delay(pollMs);
            continue;
        }
        blockAbsent = null;

        var presentNow = GetPresentIds();
        bool justLoaded = presentNow.Contains(expectId) && !presentLast.Contains(expectId);

        if (!justLoaded)
        {
            Console.Write($"\rcast {step + 1}/{sequence.Length} aguardando magic_{expectId:D4}.dll...   ");
            presentLast = presentNow;
            await Task.Delay(pollMs);
            continue;
        }

        ProcessMagicModuleResolver.MagicModule? mod = ProcessMagicModuleResolver.TryFindMagicModule(expectId);
        if (mod == null)
        {
            await Task.Delay(pollMs);
            continue;
        }

        Console.WriteLine();
        Console.WriteLine($"CAST {step + 1}/{sequence.Length}: {label} — {mod.Name} @ 0x{mod.Base:X8} size=0x{mod.Size:X}");

        string castDir = Path.Combine(sessionDir, $"{step + 1:D2}_{expectId:D4}_{label.ToLowerInvariant()}");
        int code = await MagicAbilityRuntimeDumper.RunAbilityWindowAsync(probe, mod, castDir, windowMs, snapshotMs);
        if (code == 0) pass++;

        steps.Add(new { step = step + 1, label, magicId = expectId, mod.Base, mod.Size, code });
        if (step == 1) sawMiddle = true;
        blockAbsent = expectId;
        presentLast = GetPresentIds();
        step++;
    }

    string manifestPath = Path.Combine(sessionDir, "ability_sequence_manifest.json");
    await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
    {
        mode = "ability_full_dump",
        sequence,
        windowMs,
        snapshotMs,
        pass,
        steps
    }, new JsonSerializerOptions { WriteIndented = true }));

    Console.WriteLine();
    Console.WriteLine($"wrote: {manifestPath}");
    Console.WriteLine(pass > 0 ? $"VERDICT: PASS ({pass}/{sequence.Length})" : "VERDICT: FAIL");
    return pass > 0 ? 0 : 4;
}

static HashSet<int> GetPresentIds() =>
    ProcessMagicModuleResolver.ListMagicModules().Select(m => m.MagicId).ToHashSet();

static async Task<ProcessMagicModuleResolver.MagicModule?> WaitForModuleLoadAsync(int magicId, int maxWaitSec, int pollMs)
{
    var deadline = DateTime.UtcNow.AddSeconds(maxWaitSec);
    var initial = ProcessMagicModuleResolver.ListMagicModules().Select(m => m.MagicId).ToHashSet();

    while (DateTime.UtcNow < deadline)
    {
        ProcessMagicModuleResolver.MagicModule? mod = ProcessMagicModuleResolver.TryFindMagicModule(magicId);
        if (mod != null && !initial.Contains(magicId))
            return mod;
        if (mod != null && initial.Count == 0)
            return mod;
        await Task.Delay(pollMs);
    }

    return ProcessMagicModuleResolver.TryFindMagicModule(magicId);
}

static int RunSniff(int pollMs, int maxWaitSec)
{
    Console.WriteLine($"sniff magic_*.dll ({maxWaitSec}s)...");
    var deadline = DateTime.UtcNow.AddSeconds(maxWaitSec);
    while (DateTime.UtcNow < deadline)
    {
        string list = ProcessMagicModuleResolver.ListMagicDlls();
        Console.WriteLine(string.IsNullOrEmpty(list) ? $"{DateTime.Now:HH:mm:ss} (none)" : $"{DateTime.Now:HH:mm:ss} {list}");
        Thread.Sleep(pollMs);
    }
    return 0;
}

static int[] ParseSequence(string csv) =>
    csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();

static void PrintHelp()
{
    Console.WriteLine("ThundaFiraKeThResRuntimeDumpLab — full magic ability in-game dump");
    Console.WriteLine("  --kethres-attach-capture  hook sub_72C570, dump on attach tick");
    Console.WriteLine("  --kethres-attach-capture --ability-sequence 716,94,716");
    Console.WriteLine("  --ability-dump --magic-id 716");
    Console.WriteLine("  --ability-sequence 716,94,716");
    Console.WriteLine("  --dataa-diff-sequence  KeThRes + diff ppp_dataA 716 vs 94");
    Console.WriteLine("  --live-vs-pe [--pe-dll path]  offline diff live dump vs PE (no probe)");
    Console.WriteLine("  --pppdraw-tint-capture  APOSENTADO (ver DEAD_ENDS_INDEX H7-H9)");
    Console.WriteLine("  --window-ms 8000       spell capture window per cast");
    Console.WriteLine("  --snapshot-ms 500      phase interval inside window");
    Console.WriteLine("  --sniff                debug module loads");
}

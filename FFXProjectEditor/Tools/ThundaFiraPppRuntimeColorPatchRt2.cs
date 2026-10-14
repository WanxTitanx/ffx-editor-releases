using FFXProjectEditor.FfxLib.Ps3;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Tools
{
    /// <summary>
    /// RT2 lab (RETIRED apply sites 2026-06-14): attempted runtime imm32 patches in <c>magic_0716.dll</c>.
    /// RT2 Halyson: <b>no color change</b>; animation faster/flattened (timing side effect).
    /// Targets were misidentified — <c>host+0x3EC</c> / <c>unk_100B5FC4</c> = Ego curve/timing, not bolt tint.
    /// Gate now supports <c>--restore</c> only; do not re-apply color sites.
    /// </summary>
    internal static class ThundaFiraPppRuntimeColorPatchRt2
    {
        const string DefaultGameRoot =
            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster";
        const string DllBackupSuffix = ".backup_thundafira_dll";

        public static int Run(string[] args)
        {
            try
            {
                bool restore = args.Any(a => a.Equals("--restore", StringComparison.OrdinalIgnoreCase));
                bool dryRun = args.Any(a => a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
                string? site = ArgValue(args, "--site");
                string gameRoot = ArgValue(args, "--game-root") ?? DefaultGameRoot;
                string dll = Path.Combine(gameRoot, "magicFiles", "FFX", "magic_0716.dll");
                string backup = dll + DllBackupSuffix;

                if (restore)
                {
                    if (!File.Exists(backup))
                    {
                        Console.WriteLine("FAIL: no DLL backup");
                        return 1;
                    }

                    try
                    {
                        File.Copy(backup, dll, overwrite: true);
                    }
                    catch (IOException ex) when (ex.Message.Contains("being used", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine("FAIL: close FFX (game locks magic_0716.dll) then re-run --restore");
                        return 5;
                    }

                    Console.WriteLine("VERDICT: PASS — magic_0716.dll restored");
                    return 0;
                }

                if (!File.Exists(dll))
                {
                    Console.WriteLine($"FAIL: {dll} missing");
                    return 2;
                }

                if (string.IsNullOrEmpty(site))
                {
                    Console.WriteLine("FAIL: --site required. RT2 retired all color sites — use --restore only.");
                    Console.WriteLine("  Doc: docs/reverse/FFX_THUNDAFIRA_PHASE1_RUNTIME_PATCH_RT2_FAIL_2026-06-14.md");
                    return 3;
                }

                RuntimePatchPlan plan = ResolvePlan(site, r: 0, g: 0, b: 0);
                if (plan.Retired)
                {
                    Console.WriteLine($"FAIL: site '{site}' RETIRED after RT2 (timing side effect, no color change).");
                    Console.WriteLine("  Use: --thundafira-ppp-runtime-color --restore");
                    Console.WriteLine("  Doc: docs/reverse/FFX_THUNDAFIRA_PHASE1_RUNTIME_PATCH_RT2_FAIL_2026-06-14.md");
                    return 4;
                }

                (float r, float g, float b) = ParseRgb(ArgValue(args, "--rgb") ?? "1,0.48,0.1");
                plan = ResolvePlan(site, r, g, b);

                if (!File.Exists(backup))
                    File.Copy(dll, backup, overwrite: false);

                byte[] bytes = File.ReadAllBytes(dll);
                Console.WriteLine("=== ThundaFira PPP RUNTIME COLOR PATCH (IDA 0094 Phase1) ===");
                Console.WriteLine($"dll    : {dll}");
                Console.WriteLine($"site   : {plan.SiteId} — {plan.Note}");
                Console.WriteLine($"rgb    : ({r:F3}, {g:F3}, {b:F3})");
                Console.WriteLine($"dryRun : {dryRun}");

                foreach (RuntimeFloatPatch p in plan.Patches)
                {
                    int off = MagicDllPeFileOffset.VaToFileOffset(bytes, p.ImageVa);
                    float old = BitConverter.ToSingle(bytes, off);
                    Console.WriteLine($"  VA 0x{p.ImageVa:X} file 0x{off:X}: ({old:F3}) -> ({p.Value:F3}) {p.Label}");
                    if (!dryRun)
                        BitConverter.TryWriteBytes(bytes.AsSpan(off), p.Value);
                }

                if (dryRun)
                {
                    Console.WriteLine("VERDICT: PASS — dry-run only");
                    return 0;
                }

                File.WriteAllBytes(dll, bytes);
                Console.WriteLine("VERDICT: PASS — cast ThundaFira; if bolts break use --restore");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static RuntimePatchPlan ResolvePlan(string site, float r, float g, float b)
        {
            string id = site.ToLowerInvariant();
            if (RetiredSites.Contains(id))
                return new RuntimePatchPlan(id, "RT2 FAIL — timing/curve params, not bolt color", [], Retired: true);

            var label9 = new[]
            {
                new RuntimeFloatPatch(0x10006E12, r, "setup LABEL_9 host+1072 R"),
                new RuntimeFloatPatch(0x10006E1C, g, "setup LABEL_9 host+1072 G"),
                new RuntimeFloatPatch(0x10006E26, b, "setup LABEL_9 host+1072 B"),
            };
            var defaultBranch = new[]
            {
                new RuntimeFloatPatch(0x10006E5B, r, "setup default host+1072 R imm"),
                new RuntimeFloatPatch(0x10006E89, g, "setup default host+1072 G imm (was ~0.24)"),
                new RuntimeFloatPatch(0x10006E93, b, "setup default host+1072 B imm (was 1.0)"),
            };
            var boltSpawnA = new[]
            {
                new RuntimeFloatPatch(0x10006AA2, r, "tick bolt spawn A R imm @ [ebx+0x3EC]"),
                new RuntimeFloatPatch(0x10006AAC, g, "tick bolt spawn A G imm @ [ebx+0x3F0]"),
                new RuntimeFloatPatch(0x10006AB6, b, "tick bolt spawn A B imm @ [ebx+0x3F4]"),
            };
            var boltSpawnB = new[]
            {
                new RuntimeFloatPatch(0x10006980, r, "tick bolt spawn B R imm @ [ebx+0x418]"),
                new RuntimeFloatPatch(0x1000698A, g, "tick bolt spawn B G imm @ [ebx+0x41C]"),
                new RuntimeFloatPatch(0x10006994, b, "tick bolt spawn B B imm @ [ebx+0x420]"),
            };

            return site.ToLowerInvariant() switch
            {
                "full_phase1" => new RuntimePatchPlan(
                    "full_phase1",
                    "setup both branches + EgoTaskTick bolt spawn RGBA (monster cast path)",
                    label9.Concat(defaultBranch).Concat(boltSpawnA).Concat(boltSpawnB).ToArray(),
                    Retired: true),
                "host1072_rgb_both" => new RuntimePatchPlan(
                    "host1072_rgb_both",
                    "Thundaga_EgoTaskSetup_0094: LABEL_9 + default switch branches",
                    label9.Concat(defaultBranch).ToArray(),
                    Retired: true),
                "host1072_rgb" => new RuntimePatchPlan(
                    "host1072_rgb",
                    "Thundaga_EgoTaskSetup_0094 LABEL_9 only (player Thundaga IDs)",
                    label9,
                    Retired: true),
                "host1072_rgb_default" => new RuntimePatchPlan(
                    "host1072_rgb_default",
                    "Thundaga_EgoTaskSetup_0094 default branch (likely monster magic)",
                    defaultBranch,
                    Retired: true),
                "bolt_spawn_rgb" => new RuntimePatchPlan(
                    "bolt_spawn_rgb",
                    "Thundaga_EgoTaskTick_0094: mov imm32 RGB at bolt EgoNdPart spawn",
                    boltSpawnA.Concat(boltSpawnB).ToArray(),
                    Retired: true),
                "host1072_g" => new RuntimePatchPlan(
                    "host1072_g",
                    "legacy: single imm @ 0x10006E1C (LABEL_9 G only)",
                    new[] { new RuntimeFloatPatch(0x10006E1C, g, "host1072 float1 only") },
                    Retired: true),
                _ => throw new ArgumentException(
                    $"Unknown --site '{site}'. Use full_phase1, host1072_rgb_both, host1072_rgb_default, bolt_spawn_rgb, host1072_rgb, host1072_g."),
            };
        }

        static readonly HashSet<string> RetiredSites = new(StringComparer.OrdinalIgnoreCase)
        {
            "full_phase1", "host1072_rgb_both", "host1072_rgb", "host1072_rgb_default",
            "bolt_spawn_rgb", "host1072_g",
        };

        static (float R, float G, float B) ParseRgb(string text)
        {
            string[] parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
                throw new FormatException("RGB must be r,g,b");
            return (float.Parse(parts[0], CultureInfo.InvariantCulture),
                float.Parse(parts[1], CultureInfo.InvariantCulture),
                float.Parse(parts[2], CultureInfo.InvariantCulture));
        }

        static string? ArgValue(string[] args, string key)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }

            return null;
        }

        sealed record RuntimeFloatPatch(uint ImageVa, float Value, string Label);

        sealed record RuntimePatchPlan(string SiteId, string Note, RuntimeFloatPatch[] Patches, bool Retired = false);
    }
}

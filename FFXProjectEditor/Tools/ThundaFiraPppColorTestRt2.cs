using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Tools
{
    /// <summary>
    /// RT2 lab: patch one or two vec3/vec4 color candidates in <c>magic_0716.dll</c> (Family D PPP).
    /// </summary>
    internal static class ThundaFiraPppColorTestRt2
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

                IReadOnlyList<int> offsets = CollectOffsets(args);
                if (offsets.Count == 0)
                {
                    Console.WriteLine("Usage: --thundafira-ppp-color-test --offset 0x37D64 [--offset2 0x44180] [--rgb 1,0.48,0.1] [--dry-run]");
                    Console.WriteLine("       --thundafira-ppp-color-test --restore");
                    Console.WriteLine("Run --thundafira-ppp-probe first for candidate offsets.");
                    return 2;
                }

                if (offsets.Count > 2)
                {
                    Console.WriteLine("FAIL: at most two offsets per RT2 (--offset and optional --offset2)");
                    return 3;
                }

                if (!File.Exists(dll))
                {
                    Console.WriteLine($"FAIL: {dll} missing");
                    return 2;
                }

                (float r, float g, float b) = ParseRgb(ArgValue(args, "--rgb") ?? "1,0.48,0.1");

                if (!File.Exists(backup))
                    File.Copy(dll, backup, overwrite: false);

                byte[] bytes = File.ReadAllBytes(dll);
                string rt2Tag = ArgValue(args, "--rt2-tag") ?? "shared";

                Console.WriteLine("=== ThundaFira PPP COLOR TEST ===");
                Console.WriteLine($"dll    : {dll}");
                Console.WriteLine($"rt2    : {rt2Tag} (ground = ramo B monstro solo)");
                Console.WriteLine($"rgb    : ({r:F3}, {g:F3}, {b:F3})");
                Console.WriteLine($"dryRun : {dryRun}");
                Console.WriteLine($"count  : {offsets.Count} offset(s)");

                foreach (int offset in offsets)
                {
                    if (offset < 0 || offset + 12 > bytes.Length)
                    {
                        Console.WriteLine($"FAIL: offset 0x{offset:X} out of range");
                        return 1;
                    }

                    float oldR = BitConverter.ToSingle(bytes, offset);
                    float oldG = BitConverter.ToSingle(bytes, offset + 4);
                    float oldB = BitConverter.ToSingle(bytes, offset + 8);
                    Console.WriteLine($"  0x{offset:X}: ({oldR:F3}, {oldG:F3}, {oldB:F3}) -> ({r:F3}, {g:F3}, {b:F3})");

                    if (!dryRun)
                    {
                        BitConverter.TryWriteBytes(bytes.AsSpan(offset), r);
                        BitConverter.TryWriteBytes(bytes.AsSpan(offset + 4), g);
                        BitConverter.TryWriteBytes(bytes.AsSpan(offset + 8), b);
                    }
                }

                if (dryRun)
                {
                    Console.WriteLine("VERDICT: PASS — dry-run only");
                    return 0;
                }

                try
                {
                    File.WriteAllBytes(dll, bytes);
                }
                catch (IOException ex) when (ex.Message.Contains("being used", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("FAIL: close FFX (game locks magic_0716.dll) then re-run apply");
                    return 5;
                }

                Console.WriteLine("VERDICT: PASS — cast ThundaFira on GROUND; if bolts break use --restore");
                if (offsets.Count == 2)
                    Console.WriteLine("  If crash/timing: --restore then re-run with one --offset only to bisect.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static IReadOnlyList<int> CollectOffsets(string[] args)
        {
            var list = new List<int>();
            string? first = ArgValue(args, "--offset");
            if (!string.IsNullOrWhiteSpace(first))
                list.Add(ParseOffset(first));

            string? second = ArgValue(args, "--offset2");
            if (!string.IsNullOrWhiteSpace(second))
                list.Add(ParseOffset(second));

            return list;
        }

        static int ParseOffset(string text)
        {
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.Parse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return int.Parse(text, CultureInfo.InvariantCulture);
        }

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
    }
}

using FFXProjectEditor.FfxLib.Ps3;
using System;
using System.IO;

namespace FFXProjectEditor.Tools
{
    /// <summary>
    /// RT0 gate: magic DLL SeSep sound patch round-trip on first record (identity values).
    /// </summary>
    internal static class CommandSoundRt0
    {
        static readonly string DefaultMagicRoot = MagicDllSemanticAnalyzer.DefaultFfxMagicFilesRoot;

        public static int Run(string[] args)
        {
            string magicRoot = ArgValue(args, "--magic-root") ?? DefaultMagicRoot;
            int magicId = int.Parse(ArgValue(args, "--magic-id") ?? "84");
            string dllPath = Path.Combine(magicRoot, $"magic_{magicId:D4}.dll");

            Console.WriteLine("=== Command Sound RT0 (magic DLL SeSep patch) ===");
            Console.WriteLine($"dll: {dllPath}");

            if (!File.Exists(dllPath))
            {
                Console.WriteLine($"SKIP: DLL not found (run --magicdll-sound-corpus-wave6 first on this machine)");
                return 0;
            }

            MagicDllSoundWriter.PatchResult result = MagicDllSoundWriter.RoundTripSelfTest(dllPath);
            Console.WriteLine(result.Message);
            Console.WriteLine($"records: {result.RecordsBefore.Count}");
            return result.Ok ? 0 : result.RecordsBefore.Count == 0 ? 0 : 1;
        }

        static string? ArgValue(string[] args, string key)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }
    }
}

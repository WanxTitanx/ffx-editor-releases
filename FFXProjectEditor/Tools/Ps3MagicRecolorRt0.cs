using FFXProjectEditor.FfxLib.Ps3;
using System;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Tools
{
    internal static class Ps3MagicRecolorRt0
    {
        const string PreferredFixture = @"D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data\magic\magic_0714\tex\d3d11\13312_19_0_0_128_64.dds.phyre";
        const string FallbackFixture = @"D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data\magic\magic_0082\tex\d3d11\13312_19_0_0_128_64.dds.phyre";

        public static int Run(string[] args)
        {
            try
            {
                string source = args.Length > 1 ? args[1] : ResolveFixture();
                string outputDir = args.Length > 2 ? args[2] : @"work\ps3magic_recolor_rt0";

                Console.WriteLine("=== PS3 Magic Recolor RT0 ===");
                Console.WriteLine($"source : {source}");
                Console.WriteLine($"output : {outputDir}");

                if (!File.Exists(source))
                {
                    Console.WriteLine("FAIL: source .dds.phyre not found.");
                    return 2;
                }

                Directory.CreateDirectory(outputDir);
                if (!Ps3MagicTextureWriter.TryReadMip0Layout(source, out Ps3PhyreMip0Layout? layout, out string note) || layout == null)
                {
                    Console.WriteLine($"FAIL: {note}");
                    return 1;
                }

                string identityPath = Path.Combine(outputDir, "identity.dds.phyre");
                string recolorPath = Path.Combine(outputDir, "recolor_prism.dds.phyre");

                Ps3MagicRecolorResult identity = Ps3MagicTextureColorWriter.WriteRecoloredMip0(
                    source,
                    identityPath,
                    new Ps3MagicColorTransform(1.0, 1.0, 1.0, 1.0));

                Ps3MagicRecolorResult recolor = Ps3MagicTextureColorWriter.WriteRecoloredMip0(
                    source,
                    recolorPath,
                    new Ps3MagicColorTransform(1.25, 0.65, 1.55, 1.0));

                byte[] original = File.ReadAllBytes(source);
                byte[] identityBytes = File.ReadAllBytes(identityPath);
                byte[] recolorBytes = File.ReadAllBytes(recolorPath);

                int identityDiffs = CountDiffs(original, identityBytes);
                int recolorDiffs = CountDiffs(original, recolorBytes);
                int outsideMip0Diffs = CountOutsideDiffs(original, recolorBytes, layout.BufferStart, layout.Mip0Size);

                Console.WriteLine($"layout : {layout.Summary}");
                Console.WriteLine($"identity diffs : {identityDiffs}");
                Console.WriteLine($"recolor diffs  : {recolorDiffs}");
                Console.WriteLine($"outside mip0   : {outsideMip0Diffs}");
                Console.WriteLine($"identity       : {identity.OutputPath}");
                Console.WriteLine($"recolor        : {recolor.OutputPath}");

                bool pass = identityDiffs == 0
                    && recolor.WriteResult.SameLayout
                    && recolorDiffs > 0
                    && outsideMip0Diffs == 0;

                Console.WriteLine(pass
                    ? "VERDICT: PASS - recolor keeps layout and confines diffs to mip0."
                    : "VERDICT: FAIL - recolor drifted or did not mutate expected mip0 bytes.");
                return pass ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static string ResolveFixture()
        {
            if (File.Exists(PreferredFixture))
                return PreferredFixture;
            return FallbackFixture;
        }

        static int CountDiffs(byte[] a, byte[] b)
        {
            int count = Math.Abs(a.Length - b.Length);
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++)
                if (a[i] != b[i])
                    count++;
            return count;
        }

        static int CountOutsideDiffs(byte[] a, byte[] b, int start, int length)
        {
            int count = Math.Abs(a.Length - b.Length);
            int n = Math.Min(a.Length, b.Length);
            int end = start + length;
            for (int i = 0; i < n; i++)
            {
                if (i >= start && i < end)
                    continue;
                if (a[i] != b[i])
                    count++;
            }
            return count;
        }
    }
}

using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Save;

namespace FFXProjectEditor.Tools
{
    /// <summary>
    /// Regenerates SGM RT0/RT2 fixtures under work/_samples/sgm (Lane E).
    /// </summary>
    internal static class SgmFixturesBootstrap
    {
        const string DefaultExtractedAbmap =
            @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\menu\abmap";

        public static int Run(string[] args)
        {
            if (args.Length < 1 || args[0] != "--sgm-fixtures-bootstrap")
            {
                Console.WriteLine("usage: --sgm-fixtures-bootstrap [--out-dir <dir>] [--vanilla-abmap <dir>]");
                Console.WriteLine("       [--square-layout <dat02>] [--square-contents <dat10>] [--force]");
                return 2;
            }

            string repo = FindRepoRoot() ?? Directory.GetCurrentDirectory();
            string outDir = ArgValue(args, "--out-dir")
                ?? Path.Combine(repo, "work", "_samples", "sgm");
            string vanillaAbmap = ArgValue(args, "--vanilla-abmap") ?? DefaultExtractedAbmap;
            string? squareLayout = ArgValue(args, "--square-layout");
            string? squareContents = ArgValue(args, "--square-contents");
            bool force = args.Contains("--force");

            try
            {
                Directory.CreateDirectory(outDir);

                string minimalSave = Path.Combine(outDir, "minimal_25848.bin");
                if (force || !File.Exists(minimalSave))
                {
                    byte[] payload = FfxSaveMemoryCardFixture.CreateMinimalPayload("SGM RT0 minimal");
                    File.WriteAllBytes(minimalSave, payload);
                    Console.WriteLine($"wrote: {minimalSave}");
                }

                string vanillaLayout = Path.Combine(vanillaAbmap, "dat02.dat");
                string vanillaContents = Path.Combine(vanillaAbmap, "dat10.dat");
                if (!File.Exists(vanillaLayout) || !File.Exists(vanillaContents))
                    throw new FileNotFoundException($"vanilla abmap missing under {vanillaAbmap}");

                string outVanillaLayout = Path.Combine(outDir, "vanilla_dat02.dat");
                string outVanillaContents = Path.Combine(outDir, "vanilla_dat10.dat");
                CopyIfNeeded(vanillaLayout, outVanillaLayout, force);
                CopyIfNeeded(vanillaContents, outVanillaContents, force);

                string squareLayoutDest = Path.Combine(outDir, "square861_dat02.dat");
                string squareContentsDest = Path.Combine(outDir, "square861_dat10.dat");
                if (squareLayout is not null && squareContents is not null)
                {
                    CopyIfNeeded(squareLayout, squareLayoutDest, force);
                    CopyIfNeeded(squareContents, squareContentsDest, force);
                }
                else if (!File.Exists(squareLayoutDest) || !File.Exists(squareContentsDest))
                {
                    throw new FileNotFoundException(
                        "square861 fixture missing. Pass --square-layout/--square-contents (861 nodes export) " +
                        "or place square861_dat02.dat + square861_dat10.dat manually.");
                }

                FfxSaveSphereGridMigrationReport report =
                    FfxSaveSphereGridMigrationAnalyzer.Analyze(minimalSave, squareLayoutDest, squareContentsDest);

                if (report.CountsFitVanillaSave)
                    throw new InvalidOperationException("square fixture must report extraNodes>=1 for SGM gates.");

                FfxSaveSphereGridExtraStateSidecar sidecar =
                    FfxSaveSphereGridExtraStateSidecarIO.BuildEmptyFromAnalyzer(report);
                string sidecarSample = Path.Combine(outDir, "sidecar_v1_minimal.json");
                FfxSaveSphereGridExtraStateSidecarIO.Save(sidecarSample, sidecar);

                string createdSidecar = Path.Combine(outDir, "created_sidecar.json");
                FfxSaveSphereGridExtraStateSidecarIO.Save(createdSidecar, sidecar);

                Console.WriteLine("fixture summary:");
                Console.WriteLine($"  save: {minimalSave}");
                Console.WriteLine($"  vanilla: {outVanillaLayout} + {outVanillaContents}");
                Console.WriteLine($"  square861: {squareLayoutDest} + {squareContentsDest}");
                Console.WriteLine($"  asset_counts: {report.AssetClusters}/{report.AssetNodes}/{report.AssetLinks}");
                Console.WriteLine($"  extra_nodes: {report.ExtraNodes} extra_links: {report.ExtraLinks}");
                Console.WriteLine($"  sidecar_sample: {sidecarSample}");
                Console.WriteLine($"  sidecar extra_nodes[0]: id={(sidecar.ExtraNodes.Count > 0 ? sidecar.ExtraNodes[0].NodeId : -1)}");
                Console.WriteLine("SGM fixtures bootstrap: OK");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static void CopyIfNeeded(string source, string dest, bool force)
        {
            if (!force && File.Exists(dest))
            {
                Console.WriteLine($"skip (exists): {dest}");
                return;
            }

            File.Copy(source, dest, overwrite: true);
            Console.WriteLine($"copied: {source} -> {dest}");
        }

        static string? ArgValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }

            return null;
        }

        static string? FindRepoRoot()
        {
            string? dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && dir is not null; i++)
            {
                if (File.Exists(Path.Combine(dir, "FFXProjectEditor", "FFXProjectEditor.csproj")))
                    return dir;
                dir = Directory.GetParent(dir)?.FullName;
            }

            return null;
        }
    }
}

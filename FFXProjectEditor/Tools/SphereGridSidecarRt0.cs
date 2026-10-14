using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FFXProjectEditor.FfxLib.Save;

namespace FFXProjectEditor.Tools
{
    internal static class SphereGridSidecarRt0
    {
        public static int RunValidate(string[] args)
        {
            if (args.Length < 2 || args[0] != "--spheregrid-sidecar-validate")
            {
                Console.WriteLine("usage: --spheregrid-sidecar-validate <sidecar.json>");
                return 2;
            }

            try
            {
                string json = File.ReadAllText(args[1]);
                FfxSaveSphereGridExtraStateSidecar? sidecar =
                    JsonSerializer.Deserialize<FfxSaveSphereGridExtraStateSidecar>(json);
                if (sidecar is null)
                {
                    Console.WriteLine("validation: FAIL (null document)");
                    return 3;
                }

                var errors = FfxSaveSphereGridExtraStateSidecarIO.Validate(sidecar);
                if (errors.Count > 0)
                {
                    Console.WriteLine("validation: FAIL");
                    foreach (string error in errors)
                        Console.WriteLine($"  - {error}");
                    return 3;
                }

                Console.WriteLine("validation: PASS");
                Console.WriteLine($"profile_key: {sidecar.ProfileKey}");
                Console.WriteLine($"extra_nodes: {sidecar.ExtraNodes.Count}");
                Console.WriteLine($"extra_links: {sidecar.ExtraLinks.Count}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        public static int RunCreate(string[] args)
        {
            if (args.Length < 5 || args[0] != "--spheregrid-sidecar-create")
            {
                Console.WriteLine("usage: --spheregrid-sidecar-create <save> <dat0X.dat> <dat1X.dat> <out.json> [--profile-key <key>]");
                return 2;
            }

            try
            {
                FfxSaveSphereGridMigrationReport report =
                    FfxSaveSphereGridMigrationAnalyzer.Analyze(args[1], args[2], args[3]);

                FfxSaveSphereGridExtraStateSidecar sidecar =
                    FfxSaveSphereGridExtraStateSidecarIO.BuildEmptyFromAnalyzer(report);
                string? profileKey = ArgValue(args, "--profile-key");
                if (!string.IsNullOrWhiteSpace(profileKey))
                    sidecar = sidecar with { ProfileKey = profileKey.Trim() };

                byte[] contentsBytes = File.ReadAllBytes(args[3]);
                foreach (int nodeId in FfxSaveSphereGridExtraStateSidecarIO.DiffExtraNodes(report.AssetNodes))
                    FfxSaveSphereGridExtraStateSidecarIO.ApplyDefaultsFromContents(sidecar, contentsBytes, nodeId);

                FfxSaveSphereGridExtraStateSidecarIO.Save(args[4], sidecar);

                Console.WriteLine("sidecar: created");
                Console.WriteLine($"save: {report.SavePath}");
                Console.WriteLine($"asset: {report.LayoutPath} + {report.ContentsPath}");
                Console.WriteLine($"extra_nodes: {sidecar.ExtraNodes.Count}");
                Console.WriteLine($"extra_links: {sidecar.ExtraLinks.Count}");
                if (sidecar.ExtraNodes.Count > 0)
                    Console.WriteLine($"first_extra_node: id={sidecar.ExtraNodes[0].NodeId} content=0x{sidecar.ExtraNodes[0].Content:X2}");

                string conventionPath = FfxSaveSphereGridExtraStateSidecarIO.DefaultPath(
                    sidecar.ProfileKey,
                    sidecar.SaveSha256);
                Console.WriteLine($"convention_path: {conventionPath}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
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

        public static int RunInfo(string[] args)
        {
            if (args.Length < 2 || args[0] != "--spheregrid-sidecar-info")
            {
                Console.WriteLine("usage: --spheregrid-sidecar-info <sidecar.json> [--match <save> <dat0X> <dat1X>]");
                return 2;
            }

            try
            {
                FfxSaveSphereGridExtraStateSidecar sidecar = FfxSaveSphereGridExtraStateSidecarIO.Load(args[1]);

                Console.WriteLine("sidecar: info");
                Console.WriteLine($"schema: {sidecar.Schema}");
                Console.WriteLine($"profile_key: {sidecar.ProfileKey}");
                Console.WriteLine($"grid_kind: {sidecar.GridKind}");
                Console.WriteLine($"save_sha256: {sidecar.SaveSha256}");
                Console.WriteLine($"layout_sha256: {sidecar.LayoutSha256}");
                Console.WriteLine($"contents_sha256: {sidecar.ContentsSha256}");
                Console.WriteLine($"vanilla_capacity: nodes={sidecar.VanillaCapacity.Nodes} links={sidecar.VanillaCapacity.Links} activation_bytes={sidecar.VanillaCapacity.ActivationBytes}");
                Console.WriteLine($"asset_counts: clusters={sidecar.AssetCounts.Clusters} nodes={sidecar.AssetCounts.Nodes} links={sidecar.AssetCounts.Links}");
                Console.WriteLine($"extra_nodes: {sidecar.ExtraNodes.Count}");
                Console.WriteLine($"extra_links: {sidecar.ExtraLinks.Count}");
                Console.WriteLine($"convention_path: {FfxSaveSphereGridExtraStateSidecarIO.DefaultPath(sidecar.ProfileKey, sidecar.SaveSha256)}");

                if (args.Length >= 6 && string.Equals(args[2], "--match", StringComparison.OrdinalIgnoreCase))
                {
                    FfxSaveSphereGridMigrationReport report =
                        FfxSaveSphereGridMigrationAnalyzer.Analyze(args[3], args[4], args[5]);

                    bool matches = FfxSaveSphereGridExtraStateSidecarIO.Matches(
                        sidecar,
                        report.SaveSha256,
                        report.AssetLayoutSha256,
                        report.AssetContentsSha256);

                    Console.WriteLine($"matches_current_asset: {matches}");
                    return matches ? 0 : 3;
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using FFXProjectEditor.FfxLib.Magic;
using FFXProjectEditor.FfxLib.Ps3;

namespace FFXProjectEditor.Tools
{
    // Stages cloned assets only; it never writes to the game or extracted donor roots.
    internal static class CalmCavernSinVfxStageRt0
    {
        sealed class ClonePlan
        {
            public int Row { get; set; }
            public string Name { get; set; } = "";
            public int Clone { get; set; }
            public int Donor { get; set; }
            public double[] Rgb { get; set; } = Array.Empty<double>();
        }

        public static int Run(string planPath, string ps3MagicRoot, string donorDllRoot, string stageRoot)
        {
            if (!File.Exists(planPath) || !Directory.Exists(ps3MagicRoot) || !Directory.Exists(donorDllRoot))
            {
                Console.Error.WriteLine("Missing VFX plan, PS3 donor root or DLL donor root.");
                return 2;
            }
            if (Directory.Exists(stageRoot) || File.Exists(stageRoot))
            {
                Console.Error.WriteLine($"Stage destination already exists: {stageRoot}");
                return 2;
            }

            List<ClonePlan>? plan = JsonSerializer.Deserialize<List<ClonePlan>>(
                File.ReadAllText(planPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (plan == null || plan.Count == 0 || plan.Select(p => p.Clone).Distinct().Count() != plan.Count)
            {
                Console.Error.WriteLine("VFX plan is empty, incomplete or has duplicate target IDs.");
                return 2;
            }
            foreach (ClonePlan item in plan)
            {
                if (item.Row is < 250 or > 340 || item.Clone is < 800 or > 9999 ||
                    item.Donor is < 0 or > 9999 || item.Clone == item.Donor ||
                    string.IsNullOrWhiteSpace(item.Name) || item.Rgb.Length != 3 ||
                    item.Rgb.Any(x => !double.IsFinite(x) || x <= 0 || x > 5) ||
                    !File.Exists(Path.Combine(donorDllRoot, $"magic_{item.Donor:D4}.dll")) ||
                    !Directory.Exists(Path.Combine(ps3MagicRoot, $"magic_{item.Donor:D4}")))
                {
                    Console.Error.WriteLine($"Invalid or unavailable VFX donor for {item.Name} / {item.Clone}.");
                    return 2;
                }
            }

            string stagedDllRoot = Path.Combine(stageRoot, "magicFiles", "FFX");
            Directory.CreateDirectory(stagedDllRoot);
            var donors = plan.Select(p => p.Donor).Distinct().ToArray();
            foreach (int donor in donors)
                File.Copy(Path.Combine(donorDllRoot, $"magic_{donor:D4}.dll"),
                    Path.Combine(stagedDllRoot, $"magic_{donor:D4}.dll"), overwrite: false);

            var results = new List<object>();
            foreach (ClonePlan item in plan)
            {
                MagicEffectCloneDeployResult clone = MagicEffectClonePipeline.DeployClone(
                    item.Donor, item.Clone, ps3MagicRoot, stageRoot, stagedDllRoot);
                var tint = new Ps3MagicColorTransform(item.Rgb[0], item.Rgb[1], item.Rgb[2], 1);
                int recolored = 0;
                foreach (string texture in Directory.EnumerateFiles(
                    clone.DeployedPs3Folder, "*.dds.phyre", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
                {
                    Ps3MagicTextureColorWriter.WriteRecoloredMip0(texture, texture, tint);
                    recolored++;
                }
                MagicDllFile parsed = MagicDllReader.Read(clone.TargetDll);
                string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(clone.TargetDll))).ToLowerInvariant();
                results.Add(new
                {
                    item.Row, item.Name, item.Clone, item.Donor, item.Rgb,
                    textures = clone.DeployedTextureCount, recolored,
                    dllSha256 = hash, dllSize = new FileInfo(clone.TargetDll).Length,
                    dataSectionLength = parsed.DataSection.Length
                });
                Console.WriteLine($"{item.Clone:D4} <- {item.Donor:D4} row {item.Row} {item.Name}: {recolored} textures, DLL {hash[..12]}");
            }

            foreach (int donor in donors)
                File.Delete(Path.Combine(stagedDllRoot, $"magic_{donor:D4}.dll"));
            File.WriteAllText(Path.Combine(stageRoot, "stage-results.json"),
                JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
    }
}

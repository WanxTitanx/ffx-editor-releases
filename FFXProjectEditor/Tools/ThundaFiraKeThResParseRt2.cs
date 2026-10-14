using FFXProjectEditor.FfxLib.Ps3;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FFXProjectEditor.Tools
{
    /// <summary>
    /// RT2 lab: parse PPP KeThRes opcode catalog in <c>magic_0094.dll</c> + score PS3 phyre matches.
    /// </summary>
    internal static class ThundaFiraKeThResParseRt2
    {
        const string DefaultMagicRoot =
            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\magicFiles\FFX";
        const string DefaultPs3Root =
            @"D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data\magic";
        const string DefaultOutputDir = @"work\thundafira_kethres_parse";

        public static int Run(string[] args)
        {
            try
            {
                int magicId = ParseMagicId(ArgValue(args, "--magic-id") ?? "94");
                string magicRoot = ArgValue(args, "--magic-root") ?? DefaultMagicRoot;
                string ps3Root = ArgValue(args, "--ps3-root") ?? DefaultPs3Root;
                string outputDir = ArgValue(args, "--output") ?? DefaultOutputDir;
                string repoRoot = MagicDllLogicalDecompileBatchRt2.FindRepoRootPublic();

                string dllPath = Path.Combine(magicRoot, $"magic_{magicId:D4}.dll");
                if (!File.Exists(dllPath))
                {
                    Console.WriteLine($"FAIL: {dllPath} missing");
                    return 2;
                }

                Console.WriteLine("=== ThundaFira KeThRes PPP PARSE ===");
                Console.WriteLine($"dll   : {dllPath}");
                Console.WriteLine($"ps3   : {ps3Root}");
                Console.WriteLine($"out   : {outputDir}");

                MagicDllInspection inspection = MagicDllDecompiler.Inspect(dllPath, repoRoot);
                MagicDllKeThResParser.MagicDllKeThResParseResult result = MagicDllKeThResParser.Parse(inspection, ps3Root);

                Directory.CreateDirectory(outputDir);
                string jsonPath = Path.Combine(outputDir, $"magic_{magicId:D4}_kethres_parse.json");
                string mdPath = Path.Combine(outputDir, $"magic_{magicId:D4}_KETHRES_PARSE.md");

                var jsonObj = new
                {
                    magicId = result.MagicId,
                    family = result.Family,
                    dll = result.DllPath,
                    opcodeCount = result.Opcodes.Count,
                    keThRes = result.KeThResResources.Select(k => new
                    {
                        k.Name,
                        k.TileWidth,
                        k.PackHeight,
                        k.Entry.TypeId,
                        k.Entry.StructSize,
                        k.Entry.DataRvaAHex,
                        k.Entry.DataRvaBHex,
                        k.Entry.HandlerAHex,
                        k.Entry.HandlerBHex,
                        k.Entry.StaticDataNote,
                    }),
                    primary = result.PrimaryBoltResource == null ? null : new
                    {
                        result.PrimaryBoltResource.Name,
                        result.PrimaryBoltResource.TileWidth,
                        result.PrimaryBoltResource.PackHeight,
                    },
                    phyreMatches = result.PhyreMatches.Select(m => new
                    {
                        m.FileName,
                        m.RelativePath,
                        nameDims = m.NameWidth > 0 ? $"{m.NameWidth}x{m.NameHeight}" : "?",
                        decoded = m.DecodedWidth is > 0 ? $"{m.DecodedWidth}x{m.DecodedHeight}" : null,
                        m.Format,
                        m.Score,
                        m.Note,
                    }),
                    notes = result.Notes,
                };

                File.WriteAllText(jsonPath, JsonSerializer.Serialize(jsonObj, new JsonSerializerOptions { WriteIndented = true }));
                File.WriteAllText(mdPath, MagicDllKeThResParser.BuildMarkdown(result), Encoding.UTF8);

                Console.WriteLine($"family     : {result.Family}");
                Console.WriteLine($"PPP opcodes: {result.Opcodes.Count} (inline names)");
                Console.WriteLine($"KeThRes    : {string.Join(", ", result.KeThResResources.Select(k => k.Name))}");
                if (result.PrimaryBoltResource != null)
                {
                    var p = result.PrimaryBoltResource;
                    Console.WriteLine($"primary    : {p.Name} tile={p.TileWidth}x{p.PackHeight} dataA={p.Entry.DataRvaAHex} ({p.Entry.StaticDataNote})");
                }

                Console.WriteLine("phyre top:");
                foreach (MagicDllKeThResParser.PhyreMatch m in result.PhyreMatches.Take(6))
                {
                    string dims = m.DecodedWidth is > 0 ? $"{m.DecodedWidth}x{m.DecodedHeight}" : $"{m.NameWidth}x{m.NameHeight}";
                    Console.WriteLine($"  [{m.Score,3}] {m.FileName} ({dims}) — {m.Note}");
                }

                if (result.PhyreMatches.Count == 0)
                    Console.WriteLine("  (none — check --ps3-root)");

                Console.WriteLine($"wrote: {jsonPath}");
                Console.WriteLine($"wrote: {mdPath}");
                Console.WriteLine("VERDICT: PASS");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static int ParseMagicId(string text) =>
            int.Parse(text.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture);

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

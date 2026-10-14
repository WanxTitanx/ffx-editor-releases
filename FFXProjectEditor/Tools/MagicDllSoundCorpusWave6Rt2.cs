using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Ps3;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Tools
{
    /// <summary>
    /// Wave 6: offline sound corpus — scan all <c>magic_*.dll</c> for SeSep records,
    /// sound-related strings, and correlate with <c>command.bin</c> Anim1Id rows.
    /// </summary>
    internal static class MagicDllSoundCorpusWave6Rt2
    {
        static readonly string DefaultMagicRoot = MagicDllSemanticAnalyzer.DefaultFfxMagicFilesRoot;
        const string DefaultOutputDir = @"work\magic_dll_sound_corpus_wave6";

        public static int Run(string[] args)
        {
            try
            {
                string magicRoot = ArgValue(args, "--magic-root") ?? DefaultMagicRoot;
                string repoRoot = MagicDllLogicalDecompileBatchRt2.FindRepoRootPublic();
                string outDir = Path.IsPathRooted(ArgValue(args, "--out") ?? DefaultOutputDir)
                    ? ArgValue(args, "--out")!
                    : Path.Combine(repoRoot, ArgValue(args, "--out") ?? DefaultOutputDir);
                string? kernelPath = ArgValue(args, "--kernel");

                Console.WriteLine("=== Magic DLL Sound Corpus — Wave 6 (inferno offline) ===");
                Console.WriteLine($"magic root : {magicRoot}");
                Console.WriteLine($"output     : {outDir}");

                if (!Directory.Exists(magicRoot))
                {
                    Console.WriteLine($"FAIL: magic root not found: {magicRoot}");
                    return 2;
                }

                string[] dlls = Directory.GetFiles(magicRoot, "magic_*.dll", SearchOption.TopDirectoryOnly)
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (dlls.Length == 0)
                {
                    Console.WriteLine("FAIL: no magic_*.dll files");
                    return 3;
                }

                var rows = new List<DllSoundRow>(dlls.Length);
                int n = 0;
                foreach (string dllPath in dlls)
                {
                    n++;
                    if (n % 50 == 0)
                        Console.WriteLine($"  scan {n}/{dlls.Length}...");
                    rows.Add(ScanDll(dllPath, repoRoot));
                }

                IReadOnlyList<CommandLinkRow> commandLinks = BuildCommandLinks(rows, kernelPath, repoRoot);

                Directory.CreateDirectory(outDir);
                var summary = new
                {
                    total_dlls = rows.Count,
                    with_sound_strings = rows.Count(r => r.SoundStringCount > 0),
                    with_sesep_records = rows.Count(r => r.SeSepRecordCount > 0),
                    unique_se_ids = rows.SelectMany(r => r.SeSepRecords.Select(s => s.SeId)).Distinct().Count(),
                    unique_wave_ids = rows.SelectMany(r => r.SeSepRecords.Select(s => s.WaveDataId)).Distinct().Count(),
                    command_links = commandLinks.Count,
                };

                WriteJson(outDir, "sound_corpus.json", rows);
                WriteJson(outDir, "wave6_summary.json", summary);
                WriteJson(outDir, "command_magic_sound_matrix.json", commandLinks);
                File.WriteAllText(Path.Combine(outDir, "WAVE6_SOUND_CORPUS.md"), BuildMarkdown(rows, summary, commandLinks), new UTF8Encoding(false));

                Console.WriteLine($"DLLs scanned          : {rows.Count}");
                Console.WriteLine($"With sound strings    : {summary.with_sound_strings}");
                Console.WriteLine($"With SeSep records    : {summary.with_sesep_records}");
                Console.WriteLine($"Unique seId           : {summary.unique_se_ids}");
                Console.WriteLine($"Unique waveDataId     : {summary.unique_wave_ids}");
                Console.WriteLine($"Command links         : {summary.command_links}");
                Console.WriteLine($"output                : {outDir}");
                Console.WriteLine("VERDICT: PASS — wave6 sound corpus ready");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return 1;
            }
        }

        sealed record SeSepRecordDto(int FileOffset, int Rva, uint SeId, uint RecLen, ushort WaveDataId, byte VoiceCount);

        sealed record DllSoundRow(
            int MagicId,
            string Family,
            int SoundStringCount,
            IReadOnlyList<string> SoundStringExamples,
            IReadOnlyList<string> StringFamilies,
            int SeSepRecordCount,
            IReadOnlyList<SeSepRecordDto> SeSepRecords,
            IReadOnlyList<int> HostOffsetsFromLogicalDecompile);

        sealed record CommandLinkRow(
            int CommandIndex,
            short Anim1Id,
            short Anim2Id,
            int MagicIdPrimary,
            uint? SeId,
            ushort? WaveDataId,
            string Evidence);

        static DllSoundRow ScanDll(string dllPath, string repoRoot)
        {
            MagicDllInspection ins = MagicDllDecompiler.Inspect(dllPath, repoRoot);
            int magicId = ins.MagicId ?? 0;
            MagicDllEffectFamily family = MagicDllSemanticAnalyzer.DetectFamily(ins);
            byte[] bytes = File.ReadAllBytes(dllPath);

            var soundStrings = ins.Strings
                .Where(s => IsSoundRelated(s.Value))
                .Select(s => s.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();

            var families = MagicDllSemanticAnalyzer.AnalyzeStringFamilies(ins)
                .Where(f => f.Family.Contains("sound", StringComparison.OrdinalIgnoreCase)
                    || f.Family.Contains("Sound", StringComparison.Ordinal)
                    || f.Family.Contains("SeSep", StringComparison.Ordinal))
                .Select(f => f.Family)
                .ToList();

            IReadOnlyList<MagicDllSoundRecordScanner.SeSepHit> seSep = MagicDllSoundRecordScanner.Scan(bytes);
            var seSepDtos = seSep.Select(h => new SeSepRecordDto(h.FileOffset, h.Rva, h.SeId, h.RecLen, h.WaveDataId, h.VoiceCount)).ToList();

            var hostOffsets = new List<int>();
            try
            {
                MagicDllLogicalDecompileResult? dec = MagicDllLogicalDecompiler.Decompile(ins);
                if (dec?.Slots != null)
                {
                    foreach (var slot in dec.Slots)
                    {
                        foreach (int off in slot.HostOffsets.Take(8))
                        {
                            if (!hostOffsets.Contains(off))
                                hostOffsets.Add(off);
                        }
                    }
                }
            }
            catch
            {
                // optional
            }

            return new DllSoundRow(
                magicId,
                family.ToString(),
                soundStrings.Count,
                soundStrings,
                families,
                seSepDtos.Count,
                seSepDtos,
                hostOffsets);
        }

        static bool IsSoundRelated(string value) =>
            value.Contains("SeSep", StringComparison.Ordinal)
            || value.Contains("sound", StringComparison.OrdinalIgnoreCase)
            || (value.StartsWith("Ego", StringComparison.Ordinal) && value.Contains("Se", StringComparison.Ordinal));

        static IReadOnlyList<CommandLinkRow> BuildCommandLinks(IReadOnlyList<DllSoundRow> rows, string? kernelPath, string repoRoot)
        {
            var byMagic = rows.ToDictionary(r => r.MagicId);
            string path = kernelPath ?? TryFindKernel(repoRoot);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return Array.Empty<CommandLinkRow>();

            List<Ability_Command> commands = Ability_Command.ReadList(File.ReadAllBytes(path), hasExtraInfo: true);
            var links = new List<CommandLinkRow>();
            for (int i = 0; i < commands.Count; i++)
            {
                Ability_Command c = commands[i];
                if (c.Anim1Id < 0 && c.Anim2Id < 0)
                    continue;

                foreach (short anim in new[] { c.Anim1Id, c.Anim2Id })
                {
                    if (anim < 0)
                        continue;
                    if (!byMagic.TryGetValue(anim, out DllSoundRow? row))
                        continue;

                    SeSepRecordDto? first = row.SeSepRecords.FirstOrDefault();
                    string evidence = row.SeSepRecordCount > 0
                        ? $"SeSep×{row.SeSepRecordCount}"
                        : row.SoundStringCount > 0
                            ? $"strings×{row.SoundStringCount}"
                            : "anim link only";

                    links.Add(new CommandLinkRow(
                        i,
                        c.Anim1Id,
                        c.Anim2Id,
                        anim,
                        first?.SeId,
                        first?.WaveDataId,
                        evidence));
                }
            }

            return links
                .GroupBy(l => (l.CommandIndex, l.MagicIdPrimary))
                .Select(g => g.First())
                .OrderBy(l => l.CommandIndex)
                .ThenBy(l => l.MagicIdPrimary)
                .ToList();
        }

        static string? TryFindKernel(string repoRoot)
        {
            string[] candidates =
            [
                @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\new_uspc\battle\kernel\command.bin",
                Path.Combine(repoRoot, "work", "kernel", "command.bin"),
            ];
            return candidates.FirstOrDefault(File.Exists);
        }

        static string BuildMarkdown(IReadOnlyList<DllSoundRow> rows, object summary, IReadOnlyList<CommandLinkRow> links)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Magic DLL Sound Corpus — Wave 6");
            sb.AppendLine();
            sb.AppendLine($"Generated UTC: `{DateTimeOffset.UtcNow:O}`");
            sb.AppendLine();
            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine(JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
            sb.AppendLine();
            sb.AppendLine("## Top SeSep DLLs");
            sb.AppendLine();
            sb.AppendLine("| magic_id | family | SeSep | seId (first) | waveDataId | sound strings |");
            sb.AppendLine("| ---: | --- | ---: | ---: | ---: | --- |");
            foreach (DllSoundRow r in rows.Where(r => r.SeSepRecordCount > 0).OrderByDescending(r => r.SeSepRecordCount).Take(40))
            {
                SeSepRecordDto? f = r.SeSepRecords.FirstOrDefault();
                sb.AppendLine($"| {r.MagicId:D4} | {r.Family} | {r.SeSepRecordCount} | {f?.SeId} | {f?.WaveDataId} | {r.SoundStringCount} |");
            }
            sb.AppendLine();
            sb.AppendLine("## Command matrix (sample)");
            sb.AppendLine();
            sb.AppendLine("| cmd | anim1 | magic | seId | waveId | evidence |");
            sb.AppendLine("| ---: | ---: | ---: | ---: | ---: | --- |");
            foreach (CommandLinkRow l in links.Take(50))
                sb.AppendLine($"| {l.CommandIndex} | {l.Anim1Id} | {l.MagicIdPrimary:D4} | {l.SeId} | {l.WaveDataId} | {l.Evidence} |");
            return sb.ToString();
        }

        static void WriteJson(string dir, string name, object payload)
        {
            string path = Path.Combine(dir, name);
            File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
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

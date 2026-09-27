using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FFXProjectEditor.Tools
{
    /// <summary>
    /// Wave 7: offline corpus for battle SFX bank 9999 (FEV + FSB + sidecar txt).
    /// </summary>
    internal static class Fev9999CorpusWave7Rt2
    {
        const string DefaultSfxRoot =
            @"D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data\sound_pc\sfx\us";
        const string DefaultOutputDir = @"work\fev9999_corpus_wave7";

        public static int Run(string[] args)
        {
            try
            {
                string sfxRoot = ArgValue(args, "--sfx-root") ?? DefaultSfxRoot;
                string repo = MagicDllLogicalDecompileBatchRt2.FindRepoRootPublic();
                string outDir = Path.IsPathRooted(ArgValue(args, "--out") ?? DefaultOutputDir)
                    ? ArgValue(args, "--out")!
                    : Path.Combine(repo, ArgValue(args, "--out") ?? DefaultOutputDir);
                string bankId = ArgValue(args, "--bank-id") ?? "9999";

                Console.WriteLine("=== FEV 9999 Corpus — Wave 7 ===");
                Console.WriteLine($"sfx root : {sfxRoot}");
                Console.WriteLine($"bank id  : {bankId}");
                Console.WriteLine($"output   : {outDir}");

                string fevPath = Path.Combine(sfxRoot, $"{bankId}.fev");
                string fsbPath = Path.Combine(sfxRoot, $"{bankId}_bank00.fsb");
                string commonPath = Path.Combine(sfxRoot, $"{bankId}_common.txt");
                string loopPath = Path.Combine(sfxRoot, $"{bankId}_loop.txt");

                var fevProbe = ProbeFev(fevPath);
                var fsbProbe = ProbeFsb(fsbPath);
                var commonLines = ParseSidecar(commonPath);
                var loopLines = ParseSidecar(loopPath);

                // Cross-link wave6 seIds when corpus exists
                var seIdHints = LoadWave6SeIds(repo);

                var index = new List<object>();
                for (int i = 0; i < commonLines.Count; i++)
                {
                    SidecarLine line = commonLines[i];
                    index.Add(new
                    {
                        index = i,
                        sidecar = "common",
                        raw = line.Raw,
                        tokens = line.Tokens,
                        guessed_name = line.GuessedName,
                        guessed_se_id = GuessSeIdForIndex(i, seIdHints),
                    });
                }

                Directory.CreateDirectory(outDir);
                var payload = new
                {
                    generated_utc = DateTimeOffset.UtcNow.ToString("O"),
                    bank_id = bankId,
                    paths = new { fev = fevPath, fsb = fsbPath, common = commonPath, loop = loopPath },
                    fev_probe = fevProbe,
                    fsb_probe = fsbProbe,
                    common_line_count = commonLines.Count,
                    loop_line_count = loopLines.Count,
                    sequence_index = index,
                    wave6_unique_se_ids = seIdHints.Count,
                    note = "seId→sequence name correlation requires RT2 hook log or FMOD Studio export; sidecar lines are structural hints only",
                };

                string jsonPath = Path.Combine(outDir, "fev9999_sequence_index.json");
                File.WriteAllText(jsonPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
                File.WriteAllText(Path.Combine(outDir, "WAVE7_FEV9999_CORPUS.md"), BuildMarkdown(payload, fevProbe, fsbProbe, commonLines.Count, loopLines.Count));
                File.WriteAllText(
                    Path.Combine(repo, @"docs\reverse\FFX_FEV9999_SEQUENCE_INDEX_INFERNO_2026-06-15.md"),
                    BuildInfernoDoc(payload, fevProbe, fsbProbe));

                Console.WriteLine($"FEV exists : {fevProbe.Exists} ({fevProbe.Size} bytes)");
                Console.WriteLine($"FSB exists : {fsbProbe.Exists} ({fsbProbe.Size} bytes)");
                Console.WriteLine($"common.txt : {commonLines.Count} lines");
                Console.WriteLine($"loop.txt   : {loopLines.Count} lines");
                Console.WriteLine($"wave6 seIds: {seIdHints.Count} unique");
                Console.WriteLine($"json       : {jsonPath}");
                Console.WriteLine(fevProbe.Exists ? "VERDICT: PASS — wave7 corpus written" : "VERDICT: PARTIAL — FEV missing; point --sfx-root at extracted ps3data");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        sealed record FevProbe(bool Exists, long Size, string? HeaderHex, string? FormatHint, string? Error);
        sealed record FsbProbe(bool Exists, long Size, string? HeaderHex, string? FormatHint, string? Error);
        sealed record SidecarLine(string Raw, string[] Tokens, string? GuessedName);

        static FevProbe ProbeFev(string path)
        {
            if (!File.Exists(path))
                return new FevProbe(false, 0, null, null, "file not found");

            byte[] head = File.ReadAllBytes(path).AsSpan(0, Math.Min(64, (int)new FileInfo(path).Length)).ToArray();
            string hex = Convert.ToHexString(head.AsSpan(0, Math.Min(16, head.Length)));
            string hint = head.Length >= 4
                ? BinaryPrimitives.ReadUInt32LittleEndian(head) switch
                {
                    0x46455620 => "FEV space-padded header (FMOD legacy)",
                    _ => "unknown — use FMOD Studio / vgmstream for decode",
                }
                : "too small";
            return new FevProbe(true, new FileInfo(path).Length, hex, hint, null);
        }

        static FsbProbe ProbeFsb(string path)
        {
            if (!File.Exists(path))
                return new FsbProbe(false, 0, null, null, "file not found");

            byte[] head = File.ReadAllBytes(path).AsSpan(0, Math.Min(64, (int)new FileInfo(path).Length)).ToArray();
            string hex = Convert.ToHexString(head.AsSpan(0, Math.Min(16, head.Length)));
            string hint = head.Length >= 4 && Encoding.ASCII.GetString(head, 0, 4) == "FSB4"
                ? "FSB4 (FMOD sample bank)"
                : "unknown — try vgmstream";
            return new FsbProbe(true, new FileInfo(path).Length, hex, hint, null);
        }

        static List<SidecarLine> ParseSidecar(string path)
        {
            var lines = new List<SidecarLine>();
            if (!File.Exists(path))
                return lines;

            foreach (string raw in File.ReadLines(path))
            {
                string trimmed = raw.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                    continue;
                string[] tokens = Regex.Split(trimmed, @"\s+").Where(t => t.Length > 0).ToArray();
                string? name = tokens.Length > 0 ? tokens[0] : null;
                lines.Add(new SidecarLine(raw, tokens, name));
            }
            return lines;
        }

        static HashSet<uint> LoadWave6SeIds(string repo)
        {
            string path = Path.Combine(repo, @"work\magic_dll_sound_corpus_wave6\sound_corpus.json");
            var set = new HashSet<uint>();
            if (!File.Exists(path))
                return set;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (JsonElement el in doc.RootElement.EnumerateArray())
                {
                    if (!el.TryGetProperty("SeSepRecords", out JsonElement recs))
                        continue;
                    foreach (JsonElement rec in recs.EnumerateArray())
                    {
                        if (rec.TryGetProperty("SeId", out JsonElement se))
                            set.Add(se.GetUInt32());
                    }
                }
            }
            catch { /* ignore */ }

            return set;
        }

        static uint? GuessSeIdForIndex(int index, HashSet<uint> seIds)
        {
            // Without FMOD event table decode, we cannot map index→seId offline.
            // Return null; RT2 log correlation is the proof path.
            return null;
        }

        static string BuildMarkdown(object payload, FevProbe fev, FsbProbe fsb, int common, int loop)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# FEV 9999 — Wave 7 Corpus");
            sb.AppendLine();
            sb.AppendLine($"- FEV: {(fev.Exists ? $"{fev.Size} bytes · {fev.FormatHint}" : "missing")}");
            sb.AppendLine($"- FSB: {(fsb.Exists ? $"{fsb.Size} bytes · {fsb.FormatHint}" : "missing")}");
            sb.AppendLine($"- common.txt lines: {common}");
            sb.AppendLine($"- loop.txt lines: {loop}");
            sb.AppendLine();
            sb.AppendLine("Gate: `--fev9999-corpus-wave7 --sfx-root <ps3data/sound_pc/sfx/us>`");
            return sb.ToString();
        }

        static string BuildInfernoDoc(object payload, FevProbe fev, FsbProbe fsb)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# I33 — FEV 9999 sequence index — RE Inferno");
            sb.AppendLine();
            sb.AppendLine("Lane: **Jarvis-MAGIC** · Generated: 2026-06-15");
            sb.AppendLine();
            sb.AppendLine("## Verdict");
            sb.AppendLine();
            sb.AppendLine("Battle magic `seId` values resolve through `FFX_FmodSfx_ResolveSequence` @ `0x70FB60` into `SFX/US/9999.fev` (+ `9999_bank00.fsb`).");
            sb.AppendLine("Offline index: sidecar `_common.txt` / `_loop.txt` parsed structurally; **seId→clip name** still needs RT2 hook correlation or FMOD Studio.");
            sb.AppendLine();
            sb.AppendLine("## IDA anchors (I31 extension)");
            sb.AppendLine();
            sb.AppendLine("| Addr | Name (proposed) | Role |");
            sb.AppendLine("| --- | --- | --- |");
            sb.AppendLine("| `0x70F840` | `FFX_FmodSfx_LoadFevWithSidecars` | loads FEV + `%s_common.txt` / `%s_loop.txt` |");
            sb.AppendLine("| `0x7105A0` | `FFX_FmodSfx_SequenceGroupLookup` | group lookup for multi-event play |");
            sb.AppendLine("| `0x70FB60` | `FFX_FmodSfx_ResolveSequence` | single sequence id → FMOD event |");
            sb.AppendLine();
            sb.AppendLine("## Artifacts");
            sb.AppendLine();
            sb.AppendLine("- `work/fev9999_corpus_wave7/fev9999_sequence_index.json`");
            sb.AppendLine("- Gate: `--fev9999-corpus-wave7`");
            sb.AppendLine();
            sb.AppendLine("## Toolchain (custom audio Tier 2)");
            sb.AppendLine();
            sb.AppendLine("1. Export/replace WAV in FMOD Studio targeting bank `9999`");
            sb.AppendLine("2. Rebuild `9999.fev` + `9999_bank00.fsb` via `fsbankcl`");
            sb.AppendLine("3. Deploy with `--command-sound-custom-wizard` manifest");
            sb.AppendLine();
            if (fev.Exists)
                sb.AppendLine($"FEV probe: {fev.Size} bytes, header `{fev.HeaderHex}`, hint: {fev.FormatHint}");
            if (fsb.Exists)
                sb.AppendLine($"FSB probe: {fsb.Size} bytes, header `{fsb.HeaderHex}`, hint: {fsb.FormatHint}");
            return sb.ToString();
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

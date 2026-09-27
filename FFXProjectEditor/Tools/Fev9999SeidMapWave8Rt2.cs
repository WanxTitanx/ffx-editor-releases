using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Audio;
using FFXProjectEditor.Services.Extras;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FFXProjectEditor.Tools
{
    /// <summary>
    /// Wave 8: build seId + magicId → FSB sample index map from 9999_common.txt binary sidecar + wave6 corpus.
    /// </summary>
    internal static class Fev9999SeidMapWave8Rt2
    {
        const string DefaultSfxRoot =
            @"D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data\sound_pc\sfx\us";
        const string DefaultOutputDir = @"work\fev9999_corpus_wave8";

        public static int Run(string[] args)
        {
            try
            {
                string repo = CommandSoundCorpusLoader.FindRepoRoot()
                    ?? MagicDllLogicalDecompileBatchRt2.FindRepoRootPublic();
                string sfxRoot = ArgValue(args, "--sfx-root") ?? DefaultSfxRoot;
                string outDir = Path.IsPathRooted(ArgValue(args, "--out") ?? DefaultOutputDir)
                    ? ArgValue(args, "--out")!
                    : Path.Combine(repo, ArgValue(args, "--out") ?? DefaultOutputDir);
                string bankId = ArgValue(args, "--bank-id") ?? "9999";
                string locale = ArgValue(args, "--locale") ?? "US";

                string fsbPath = Path.Combine(sfxRoot, $"{bankId}_bank00.fsb");
                string commonPath = Path.Combine(sfxRoot, $"{bankId}_common.txt");
                string fevPath = Path.Combine(sfxRoot, $"{bankId}.fev");

                Console.WriteLine("=== FEV 9999 seId map — Wave 8 ===");
                Console.WriteLine($"sfx root : {sfxRoot}");
                Console.WriteLine($"output   : {outDir}");

                int? subsongCount = File.Exists(fsbPath) && FfxFsbVgmStream_Service.IsAvailable
                    ? FfxFsbVgmStream_Service.TryCountSubsongs(fsbPath)
                    : null;

                IReadOnlyList<FevLegacySidecarReader.CommonRow> commonRows = FevLegacySidecarReader.ReadCommonRows(commonPath);
                Dictionary<uint, int> keyToFsb = FevLegacySidecarReader.BuildKeyToFsbIndexMap(commonRows);

                var seIds = LoadWave6SeIds(repo);
                IReadOnlyList<CommandSoundCorpusLoader.DonorOption> donors = CommandSoundCorpusLoader.GetDonorOptions();

                var mapRows = new List<object>();
                int resolved = 0;
                foreach (CommandSoundCorpusLoader.DonorOption d in donors.OrderBy(x => x.MagicId).ThenBy(x => x.RecordIndex))
                {
                    SeidFsbMapResolver.ResolveResult? hit = keyToFsb.Count > 0
                        ? SeidFsbMapResolver.TryResolve(d.MagicId, d.SeId, d.WaveDataId, keyToFsb)
                        : null;

                    if (hit != null)
                        resolved++;

                    mapRows.Add(new
                    {
                        seId = d.SeId,
                        magicId = d.MagicId,
                        waveDataId = d.WaveDataId,
                        recordIndex = d.RecordIndex,
                        fsbSampleIndex = hit?.FsbSampleIndex,
                        confidence = hit?.Confidence,
                        evidence = hit?.Evidence ?? "unresolved — seId not in 9000..9121 FEV event key range",
                        donorDisplay = d.Display,
                    });
                }

                var payload = new
                {
                    generated_utc = DateTimeOffset.UtcNow.ToString("O"),
                    locale,
                    bank_id = bankId,
                    paths = new { fev = (object)null, fsb = (object)null, common = (object)null },
                    fsb_subsong_count = subsongCount,
                    common_txt_rows = commonRows.Count,
                    common_txt_keys_sample = commonRows.Take(8).Select(r => new { r.Key, r.FsbSampleIndex }).ToList(),
                    wave6_unique_se_ids = seIds.Count,
                    map_mode = "common_txt_seId_low_byte_then_event_key",
                    resolved_rows = resolved,
                    total_rows = mapRows.Count,
                    rows = mapRows,
                    rt2_protocol = "ability-sfx-lab: cast spell; confirm fsbSampleIndex via Play original in Kernel Commands",
                };

                Directory.CreateDirectory(outDir);
                string jsonPath = Path.Combine(outDir, "seid_to_fsb_sample.json");
                string jsonText = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(jsonPath, jsonText);

                string bundledPath = Path.Combine(repo, @"FFXProjectEditor\FfxLib\Audio\Data\seid_to_fsb_sample.json");
                Directory.CreateDirectory(Path.GetDirectoryName(bundledPath)!);
                File.WriteAllText(bundledPath, jsonText);
                SeidFsbMapLoader.InvalidateCache();

                string docPath = Path.Combine(repo, @"docs\reverse\FFX_FEV9999_SEID_TO_FSB_SAMPLE_INFERNO_2026-06-15.md");
                File.WriteAllText(docPath, BuildI34Doc(payload, subsongCount, resolved, mapRows.Count, commonRows.Count));

                Console.WriteLine($"donor rows : {mapRows.Count}");
                Console.WriteLine($"resolved   : {resolved} ({(mapRows.Count > 0 ? 100.0 * resolved / mapRows.Count : 0):F1}%)");
                Console.WriteLine($"FSB subsongs: {subsongCount?.ToString() ?? "n/a"}");
                Console.WriteLine($"common rows: {commonRows.Count}");
                Console.WriteLine($"json      : {jsonPath}");
                Console.WriteLine($"bundled   : {bundledPath}");
                Console.WriteLine(resolved > 0
                    ? "VERDICT: PASS — wave8 seId→FSB map written"
                    : "VERDICT: PARTIAL — map written but no rows resolved (check --sfx-root common.txt)");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.Message}");
                return 1;
            }
        }

        static HashSet<uint> LoadWave6SeIds(string repo)
        {
            var set = new HashSet<uint>();
            string path = Path.Combine(repo, @"work\magic_dll_sound_corpus_wave6\sound_corpus.json");
            if (!File.Exists(path)) return set;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (JsonElement el in doc.RootElement.EnumerateArray())
            {
                if (!el.TryGetProperty("SeSepRecords", out JsonElement recs)) continue;
                foreach (JsonElement rec in recs.EnumerateArray())
                    if (rec.TryGetProperty("SeId", out JsonElement se))
                        set.Add(se.GetUInt32());
            }
            return set;
        }

        static string BuildI34Doc(object payload, int? subsongs, int resolved, int total, int commonRows) => $"""
            # I34 — FEV 9999 seId → FSB sample index — RE Inferno

            Lane: **Jarvis-MAGIC** · Generated: 2026-06-15

            ## Verdict

            Wave8 maps **wave6 donor rows** (`magicId` + `seId`) → `fsbSampleIndex` via binary `9999_common.txt`,
            matching EXE `FFX_FmodSfx_ResolveSequence@0x70FB60` → `sub_710370` (**`common` key = `seId & 0xFF`**),
            then `sub_710BC0` fallback (`seId - 9000` when low-byte key missing, e.g. `9121`).

            Resolved: **{resolved}/{total}** donor rows · common.txt rows: **{commonRows}**

            ## FSB subsongs (vgmstream)

            Count: {subsongCountLabel(subsongs)}

            ## Artifacts

            - `work/fev9999_corpus_wave8/seid_to_fsb_sample.json`
            - Gate: `--fev9999-seid-map-wave8 --sfx-root <ps3data/sound_pc/sfx/us>`

            ## Example

            `magic_0036` · `seId=9066` → low byte `106` → **FSB sample #9** (NOT `seId-9000`→#85 nor `magicId`→#52)

            ## RT2

            Cast with ability-sfx-lab; confirm index via Kernel Commands **Play original**.

            """;

        static string subsongCountLabel(int? n) => n.HasValue ? n.Value.ToString() : "unknown (vgmstream missing)";

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

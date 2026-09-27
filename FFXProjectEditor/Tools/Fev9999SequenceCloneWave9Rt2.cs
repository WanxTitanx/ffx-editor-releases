using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Audio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FFXProjectEditor.Tools
{
    internal static class Fev9999SequenceCloneWave9Rt2
    {
        const string DefaultSfxRoot =
            @"D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data\sound_pc\sfx\us";

        public static int Run(string[] args)
        {
            try
            {
                string repo = CommandSoundCorpusLoader.FindRepoRoot()
                    ?? MagicDllLogicalDecompileBatchRt2.FindRepoRootPublic();
                string sfxRoot = ArgValue(args, "--sfx-root") ?? DefaultSfxRoot;
                string outDir = Path.Combine(repo, @"work\fev9999_corpus_wave9");
                uint donorSeId = uint.Parse(ArgValue(args, "--donor-se-id") ?? "9006");
                uint newSeId = uint.Parse(ArgValue(args, "--new-se-id") ?? "8010");
                int fsbIndex = int.TryParse(ArgValue(args, "--fsb-sample-index"), out int si) ? si : 0;
                int? donorFsbIndex = int.TryParse(ArgValue(args, "--donor-fsb-index"), out int di) ? di : fsbIndex;

                string fevPath = Path.Combine(sfxRoot, "9999.fev");
                Console.WriteLine("=== FEV 9999 sequence clone — Wave 9 ===");
                Console.WriteLine($"fev      : {fevPath}");
                Console.WriteLine($"donor    : {donorSeId}");
                Console.WriteLine($"new seId : {newSeId}");

                Directory.CreateDirectory(outDir);

                FevLegacyReader.SequenceBlobHit? hit =
                    FevLegacyReader.TryLocateSequenceBlob(fevPath, donorSeId)
                    ?? FevLegacyReader.TryLocateSequenceByFsbIndex(fevPath, donorFsbIndex ?? 0);

                var clone = FevLegacySequenceWriter.CloneSequenceToNewSeId(
                    fevPath,
                    new FevLegacySequenceWriter.ClonePlan(donorSeId, newSeId, fsbIndex, donorFsbIndex),
                    Path.Combine(outDir, "clone_work"));

                var payload = new
                {
                    generated_utc = DateTimeOffset.UtcNow.ToString("O"),
                    donor_se_id = donorSeId,
                    new_se_id = newSeId,
                    donor_blob = hit == null
                        ? null
                        : (object)new { hit.AbsoluteOffset, hit.BlobLength, hit.ChunkFourCc },
                    clone_ok = clone.Ok,
                    clone_message = clone.Message,
                    clone_method = clone.Method,
                    output_fev = clone.OutputFevPath,
                    rt2_protocol = "ability-sfx-lab: cast target spell; expect sequenceId=" + newSeId,
                };

                string jsonPath = Path.Combine(outDir, "sequence_clone_wave9.json");
                File.WriteAllText(jsonPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));

                string docPath = Path.Combine(repo, @"docs\reverse\FFX_FEV9999_SEQUENCE_CLONE_INFERNO_2026-06-15.md");
                File.WriteAllText(docPath, BuildI36Doc(hit, clone));

                if (hit != null)
                    Console.WriteLine($"blob @   : {hit.AbsoluteOffset} len {hit.BlobLength} chunk {hit.ChunkFourCc}");
                else
                    Console.WriteLine("blob @   : (no u32 seId in FEV — registration trailer path)");
                Console.WriteLine($"clone    : {clone.Message} [{clone.Method}]");
                Console.WriteLine($"json     : {jsonPath}");
                Console.WriteLine(clone.Ok ? "VERDICT: PASS — wave9 offline clone" : "VERDICT: FAIL");
                return clone.Ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.Message}");
                return 1;
            }
        }

        static string BuildI36Doc(FevLegacyReader.SequenceBlobHit? hit, FevLegacySequenceWriter.CloneResult clone) => $"""
            # I36 — FEV 9999 sequence clone (donor → new seId) — RE Inferno

            Lane: **Jarvis-MAGIC** · Generated: 2026-06-15

            ## Verdict

            Phase 2 **donor-clone** ships offline with three paths:

            1. **seId u32 blob** in `9999.fev` (when present)
            2. **FSB sample index** blob locate (`TryLocateSequenceByFsbIndex`)
            3. **`FFX2SEID` registration trailer** + `9999_common.txt` row append (default for PC `9999.fev`)

            **Does not overwrite** donor sequences or shared FSB samples.

            ## RE note

            Battle `seId` values (e.g. `9006`) are **not** stored as raw u32 in PC `9999.fev`. ResolveSequence @ `0x70FB60` maps at runtime via FMOD PROJECT + `_common.txt` (8 bytes × subsong).

            ## Wave9 result

            Method: **{clone.Method}** · OK: **{clone.Ok}** · {clone.Message}

            {(hit == null ? "Donor blob: n/a (trailer path)" : $"Donor blob: offset {hit.AbsoluteOffset}, len {hit.BlobLength}, chunk {hit.ChunkFourCc}")}

            ## Gate

            `--fev9999-sequence-clone-wave9 --donor-se-id 9006 --new-se-id 8010`

            ## RT2

            Deploy triple pack + cast → log `sequenceId=<new>`; donor spell unchanged.
            """;

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

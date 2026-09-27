using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FFXProjectEditor.FfxLib.Ability
{
    /// <summary>
    /// Loads wave6 sound corpus JSON for Commands UI and donor browser.
    /// </summary>
    public static class CommandSoundCorpusLoader
    {
        public sealed record CommandSoundInfo(
            int CommandIndex,
            int MagicId,
            uint? SeId,
            ushort? WaveDataId,
            string Evidence);

        public sealed record SeSepRecordInfo(
            int FileOffset,
            uint SeId,
            ushort WaveDataId,
            int RecordIndex);

        public sealed record MagicSoundEntry(
            int MagicId,
            IReadOnlyList<SeSepRecordInfo> Records);

        public sealed record DonorOption(
            string Display,
            int MagicId,
            uint SeId,
            ushort WaveDataId,
            int FileOffset,
            int RecordIndex);

        public sealed record SeIdReverseRow(
            uint SeId,
            IReadOnlyList<int> MagicIds,
            int UseCount);

        static IReadOnlyDictionary<int, CommandSoundInfo>? s_byCommandIndex;
        static IReadOnlyDictionary<int, CommandSoundInfo>? s_byMagicId;
        static IReadOnlyList<MagicSoundEntry>? s_magicEntries;
        static IReadOnlyList<DonorOption>? s_donors;
        static IReadOnlyList<SeIdReverseRow>? s_seIdReverse;

        public static void InvalidateCache()
        {
            s_byCommandIndex = null;
            s_byMagicId = null;
            s_magicEntries = null;
            s_donors = null;
            s_seIdReverse = null;
        }

        public static CommandSoundInfo? TryGetForCommand(int commandIndex)
        {
            EnsureMatrixLoaded();
            return s_byCommandIndex != null && s_byCommandIndex.TryGetValue(commandIndex, out CommandSoundInfo? info)
                ? info
                : null;
        }

        public static CommandSoundInfo? TryGetForMagicId(int magicId)
        {
            EnsureMatrixLoaded();
            return s_byMagicId != null && s_byMagicId.TryGetValue(magicId, out CommandSoundInfo? info)
                ? info
                : null;
        }

        public static IReadOnlyList<DonorOption> GetDonorOptions()
        {
            EnsureCorpusLoaded();
            return s_donors ?? [];
        }

        public static IReadOnlyList<SeIdReverseRow> GetSeIdReverseIndex()
        {
            EnsureCorpusLoaded();
            return s_seIdReverse ?? [];
        }

        public static MagicSoundEntry? TryGetMagicEntry(int magicId)
        {
            EnsureCorpusLoaded();
            return s_magicEntries?.FirstOrDefault(e => e.MagicId == magicId);
        }

        public static bool IsCorpusAvailable()
        {
            string? repo = FindRepoRoot();
            return repo != null
                && File.Exists(Path.Combine(repo, @"work\magic_dll_sound_corpus_wave6\sound_corpus.json"));
        }

        public static string ReadRt2Status()
        {
            string? repo = FindRepoRoot();
            if (repo == null)
                return "RT2: unknown (repo root not found)";

            string verdictPath = Path.Combine(repo, @"RuntimeTools\AbilitySfxLab\ability_sfx_verdict.json");
            if (!File.Exists(verdictPath))
                return "RT2: pending (no verdict file)";

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(verdictPath));
                string rt2 = doc.RootElement.TryGetProperty("rt2_in_game", out JsonElement el)
                    ? el.GetString() ?? "pending"
                    : "pending";
                return rt2 switch
                {
                    "pass" => "RT2: pass",
                    "pending" => "RT2: pending (cast + ability-sfx-lab log)",
                    _ => $"RT2: {rt2}",
                };
            }
            catch
            {
                return "RT2: pending";
            }
        }

        static void EnsureMatrixLoaded() => EnsureCorpusLoaded();

        static void EnsureCorpusLoaded()
        {
            if (s_byCommandIndex != null && s_magicEntries != null)
                return;

            string? repo = FindRepoRoot();
            if (repo == null)
            {
                ClearCaches();
                return;
            }

            LoadMatrix(repo);
            LoadSoundCorpus(repo);
        }

        static void ClearCaches()
        {
            s_byCommandIndex = new Dictionary<int, CommandSoundInfo>();
            s_byMagicId = new Dictionary<int, CommandSoundInfo>();
            s_magicEntries = [];
            s_donors = [];
            s_seIdReverse = [];
        }

        static void LoadMatrix(string repo)
        {
            string path = Path.Combine(repo, @"work\magic_dll_sound_corpus_wave6\command_magic_sound_matrix.json");
            if (!File.Exists(path))
            {
                s_byCommandIndex = new Dictionary<int, CommandSoundInfo>();
                s_byMagicId = new Dictionary<int, CommandSoundInfo>();
                return;
            }

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var byCmd = new Dictionary<int, CommandSoundInfo>();
                var byMagic = new Dictionary<int, CommandSoundInfo>();
                foreach (JsonElement el in doc.RootElement.EnumerateArray())
                {
                    int cmd = el.GetProperty("CommandIndex").GetInt32();
                    int magic = el.GetProperty("MagicIdPrimary").GetInt32();
                    uint? seId = el.TryGetProperty("SeId", out JsonElement se) && se.ValueKind == JsonValueKind.Number
                        ? se.GetUInt32()
                        : null;
                    ushort? wave = el.TryGetProperty("WaveDataId", out JsonElement w) && w.ValueKind == JsonValueKind.Number
                        ? w.GetUInt16()
                        : null;
                    string evidence = el.TryGetProperty("Evidence", out JsonElement ev) ? ev.GetString() ?? "" : "";
                    var row = new CommandSoundInfo(cmd, magic, seId, wave, evidence);
                    byCmd.TryAdd(cmd, row);
                    byMagic.TryAdd(magic, row);
                }
                s_byCommandIndex = byCmd;
                s_byMagicId = byMagic;
            }
            catch
            {
                s_byCommandIndex = new Dictionary<int, CommandSoundInfo>();
                s_byMagicId = new Dictionary<int, CommandSoundInfo>();
            }
        }

        static void LoadSoundCorpus(string repo)
        {
            string path = Path.Combine(repo, @"work\magic_dll_sound_corpus_wave6\sound_corpus.json");
            if (!File.Exists(path))
            {
                s_magicEntries = [];
                s_donors = [];
                s_seIdReverse = [];
                return;
            }

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var entries = new List<MagicSoundEntry>();
                var donors = new List<DonorOption>();
                var seIdToMagic = new Dictionary<uint, List<int>>();

                foreach (JsonElement el in doc.RootElement.EnumerateArray())
                {
                    if (!el.TryGetProperty("MagicId", out JsonElement midEl))
                        continue;
                    int magicId = midEl.GetInt32();
                    if (!el.TryGetProperty("SeSepRecords", out JsonElement recs) || recs.ValueKind != JsonValueKind.Array)
                        continue;

                    var records = new List<SeSepRecordInfo>();
                    int idx = 0;
                    foreach (JsonElement rec in recs.EnumerateArray())
                    {
                        int offset = rec.GetProperty("FileOffset").GetInt32();
                        uint seId = rec.GetProperty("SeId").GetUInt32();
                        ushort wave = rec.TryGetProperty("WaveDataId", out JsonElement w) ? w.GetUInt16() : (ushort)0;
                        records.Add(new SeSepRecordInfo(offset, seId, wave, idx));
                        donors.Add(new DonorOption(
                            $"magic_{magicId:D4} · seId={seId} · wave={wave} · off=0x{offset:X}",
                            magicId,
                            seId,
                            wave,
                            offset,
                            idx));
                        if (!seIdToMagic.TryGetValue(seId, out List<int>? list))
                        {
                            list = [];
                            seIdToMagic[seId] = list;
                        }
                        if (!list.Contains(magicId))
                            list.Add(magicId);
                        idx++;
                    }

                    if (records.Count > 0)
                        entries.Add(new MagicSoundEntry(magicId, records));
                }

                s_magicEntries = entries;
                s_donors = donors.OrderBy(d => d.MagicId).ThenBy(d => d.RecordIndex).ToList();
                s_seIdReverse = seIdToMagic
                    .OrderBy(kv => kv.Key)
                    .Select(kv => new SeIdReverseRow(kv.Key, kv.Value.OrderBy(x => x).ToList(), kv.Value.Count))
                    .ToList();
            }
            catch
            {
                s_magicEntries = [];
                s_donors = [];
                s_seIdReverse = [];
            }
        }

        public static string? FindRepoRoot()
        {
            string? dir = AppContext.BaseDirectory;
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "PORT_STATUS.md")))
                    return dir;
                dir = Directory.GetParent(dir)?.FullName;
            }

            dir = Directory.GetCurrentDirectory();
            for (int i = 0; i < 8 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "PORT_STATUS.md")))
                    return dir;
                dir = Directory.GetParent(dir)?.FullName;
            }

            return null;
        }
    }
}

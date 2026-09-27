using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FFXProjectEditor.FfxLib.Ability
{
    /// <summary>Loads the bundled seId + magicId → FSB sample map.</summary>
    public static class SeidFsbMapLoader
    {
#if FFX_INCLUDE_DEVTOOLS
        public const string WorkOverrideRelativePath = @"work\fev9999_corpus_wave8\seid_to_fsb_sample.json";
#endif
        public const string BundledRelativePath = @"FfxLib\Audio\Data\seid_to_fsb_sample.json";

        public sealed record MapRow(
            uint SeId,
            int MagicId,
            ushort? WaveDataId,
            int? FsbSampleIndex,
            string Evidence,
            string? DonorDisplay);

        public sealed record MapStats(int ResolvedRows, int TotalRows, string SourceLabel);

        static IReadOnlyList<MapRow>? s_cachedRows;
        static string? s_cachedPath;

        public static bool IsMapAvailable()
        {
            string? path = ResolveMapPath();
            return path != null && File.Exists(path);
        }

        public static string? ResolveMapPath()
        {
#if FFX_INCLUDE_DEVTOOLS
            // Repository fallbacks are authoring conveniences only. Keeping this whole branch
            // outside product builds prevents local checkout names from entering the public PE.
            string? repo = CommandSoundCorpusLoader.FindRepoRoot();

            if (repo != null)
            {
                string work = Path.Combine(repo, WorkOverrideRelativePath);
                if (File.Exists(work))
                    return work;

                string repoBundled = Path.Combine(repo, "FFXProjectEditor", BundledRelativePath);
                if (File.Exists(repoBundled))
                    return repoBundled;
            }
#endif

            string exeBundled = Path.Combine(AppContext.BaseDirectory, BundledRelativePath);
            if (File.Exists(exeBundled))
                return exeBundled;

            return null;
        }

        public static MapStats GetMapStats()
        {
            IReadOnlyList<MapRow> rows = LoadRows();
            int resolved = rows.Count(r => r.FsbSampleIndex.HasValue);
            return new MapStats(resolved, rows.Count, DescribeSourceLabel());
        }

        public static string DescribeMapStatus()
        {
            if (!IsMapAvailable())
                return "FSB sample map: not found (reinstall the editor).";

            MapStats stats = GetMapStats();
            return $"FSB sample map ({stats.SourceLabel}): {stats.ResolvedRows}/{stats.TotalRows} donor rows with index";
        }

        static string DescribeSourceLabel()
        {
            string? path = ResolveMapPath();
            if (path == null)
                return "missing";

            string norm = path.Replace('\\', '/');
#if FFX_INCLUDE_DEVTOOLS
            if (norm.Contains("/work/fev9999_corpus_wave8/", StringComparison.OrdinalIgnoreCase))
                return "dev override";
#endif
            return "bundled";
        }

        public static void InvalidateCache()
        {
            s_cachedRows = null;
            s_cachedPath = null;
        }

        public static IReadOnlyList<MapRow> LoadRows()
        {
            string? path = ResolveMapPath();
            if (path == null || !File.Exists(path))
                return [];

            if (s_cachedRows != null && s_cachedPath == path)
                return s_cachedRows;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (!doc.RootElement.TryGetProperty("rows", out JsonElement rowsEl))
                {
                    s_cachedRows = [];
                    s_cachedPath = path;
                    return s_cachedRows;
                }

                var list = new List<MapRow>();
                foreach (JsonElement row in rowsEl.EnumerateArray())
                {
                    uint seId = row.TryGetProperty("seId", out JsonElement se) ? se.GetUInt32() : 0;
                    int magicId = row.TryGetProperty("magicId", out JsonElement mi) ? mi.GetInt32() : 0;
                    ushort? wave = row.TryGetProperty("waveDataId", out JsonElement w) && w.ValueKind == JsonValueKind.Number
                        ? (ushort)w.GetInt32()
                        : null;
                    int? fsbIdx = row.TryGetProperty("fsbSampleIndex", out JsonElement fi) && fi.ValueKind == JsonValueKind.Number
                        ? fi.GetInt32()
                        : null;
                    string evidence = row.TryGetProperty("evidence", out JsonElement ev) ? ev.GetString() ?? "" : "";
                    string? display = row.TryGetProperty("donorDisplay", out JsonElement dd) ? dd.GetString() : null;
                    list.Add(new MapRow(seId, magicId, wave, fsbIdx, evidence, display));
                }

                s_cachedRows = list;
                s_cachedPath = path;
                return list;
            }
            catch
            {
                s_cachedRows = [];
                s_cachedPath = path;
                return s_cachedRows;
            }
        }

        public static MapRow? TryGetForMagicAndSeId(int magicId, uint seId)
            => LoadRows()
                .FirstOrDefault(r => r.MagicId == magicId && r.SeId == seId && r.FsbSampleIndex.HasValue)
                ?? LoadRows()
                    .Where(r => r.SeId == seId && r.FsbSampleIndex.HasValue)
                    .OrderBy(r => r.MagicId == magicId ? 0 : 1)
                    .ThenBy(r => r.MagicId)
                    .FirstOrDefault();

        public static MapRow? TryGetForSeId(uint seId)
            => LoadRows()
                .Where(r => r.SeId == seId && r.FsbSampleIndex.HasValue)
                .OrderBy(r => r.MagicId)
                .FirstOrDefault()
                ?? LoadRows().FirstOrDefault(r => r.SeId == seId);

        public static MapRow? TryGetForMagicId(int magicId)
            => LoadRows()
                .Where(r => r.MagicId == magicId && r.FsbSampleIndex.HasValue)
                .OrderBy(r => r.SeId)
                .FirstOrDefault()
                ?? LoadRows().FirstOrDefault(r => r.MagicId == magicId);
    }
}

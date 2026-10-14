// ============================================================================
// EncounterIndexBridge — resolves battleId (btl/azit03_00/azit03_00.bin) -> noclip encounter id (0e/<hex>.bin)
// PURPOSE : resolves stable identities from the selected NoClip encounter table (0d/0000.bin),
//           with content-hash/similarity fallback for extractions without that metadata.
// WHY     : the noclip FFX viewer renders the SAME battle files the Aurora edits (proven identity 2026-08-01),
//           but the 0e/ numbering follows ISO extraction order, not alphabetical id — resolution must be by
//           content hash, not name. Read-only: never mutates game or noclip data.
// EVIDENCE: docs/proven identity (bin.ts:2494 parseEncounter layout matches Battle_File).
// MAINT   : Default*Root are environment-specific paths — a missing root yields empty/unmatched (honest), so
//           callers must handle 0 matches. Cached index; rebuild when corpus changes.
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace FFXProjectEditor.FfxLib.Battle
{
    /// <summary>
    /// 🐉 AURORA × NOCLIP — EncounterIndexBridge.
    ///
    /// PROVEN IDENTITY (2026-08-01): the noclip.website FFX viewer parses its `data/FinalFantasyX/0e/<id>.bin`
    /// "encounters" with `BIN.parseEncounter` (bin.ts:2494) using EXACTLY the same layout as our
    /// `Battle_File` (`btl/{mapKey}_{NN}/{mapKey}_{NN}.bin`): script chunk pointer @0x4, monster ids i16@+0xC
    /// (8 slots), positions float32 stride 0x10 (chunk3). I.e. the viewer renders the SAME files the Aurora
    /// edits. This bridge resolves `battleId (azit03_00)` → `encounterId (0e/00XX.bin)` by SHA-256 content
    /// match (the 0e/ numbering follows the ISO extraction order, NOT alphabetical map order — verified:
    /// 0e/0002.bin == bjyt02_00). Unmatched battles are reported honestly (the noclip extraction has fewer
    /// entries than the corpus).
    ///
    /// Read-only: builds a cached index; never mutates the game or the noclip data.
    /// </summary>
    public sealed class EncounterIndexBridge
    {
        public static string DefaultBtlRoot
        {
            get
            {
                string? master = FFXProjectEditor.Services.PortablePathResolver.MasterRoot;
                string? ps2 = FFXProjectEditor.Services.PortablePathResolver.FfxPs2Root;
                return FFXProjectEditor.Services.PortablePathResolver.FirstExistingDirectory(
                    Environment.GetEnvironmentVariable("FFX_BTL_ROOT"),
                    ps2 == null ? null : Path.Combine(ps2, "jppc", "battle", "btl"),
                    master == null ? null : Path.Combine(master, "jppc", "battle", "btl")) ?? string.Empty;
            }
        }

        public static string DefaultNoclip0eRoot
        {
            get
            {
                string? ffxData = FFXProjectEditor.Modules.Common.ViewerHub.NoclipLocator.FindFfxDataRoot();
                return ffxData == null ? string.Empty : Path.Combine(ffxData, "0e");
            }
        }

        /// <summary>Tamanho de bloco do fallback por similaridade (estilo rsync).</summary>
        public const int SimilarityBlockBytes = 256;

        /// <summary>Fração mínima de blocos idênticos para aceitar um match por similaridade
        /// (evita casar aleatoriamente; abaixo disso o battle permanece honestamente SEM MATCH).
        /// Validado em work/probe_bika_v5.py: bika02_00→00d0 com score 0.98; 191/244 unmatched destravados.</summary>
        public const double MinSimilarityScore = 0.50;

        public IReadOnlyDictionary<string, int> BattleIdToEncounter { get; }
        /// <summary>Stable identity from the selected NoClip extraction's 0d/0000.bin.
        /// Its formation offsets belong to that extraction, not the edited HD kernel.</summary>
        public IReadOnlyDictionary<string, int> BattleIdToTableEncounter { get; }
        /// <summary>Fallback: battleId → 0e/ por similaridade de conteúdo, quando não há SHA byte-idêntico.
        /// Destrava battles cujo mesmo encontro existe no noclip extraído com poucos bytes diferentes.</summary>
        public IReadOnlyDictionary<string, int> BattleIdToNearestEncounter { get; }
        public IReadOnlyList<string> UnmatchedBattleIds { get; }
        public int CorpusBattleCount { get; }
        public int NoclipEncounterCount { get; }

        private EncounterIndexBridge(
            Dictionary<string, int> map,
            Dictionary<string, int> tableMap,
            Dictionary<string, int> nearest,
            List<string> unmatched,
            int corpus,
            int noclip)
        {
            BattleIdToEncounter = map;
            BattleIdToTableEncounter = tableMap;
            BattleIdToNearestEncounter = nearest;
            UnmatchedBattleIds = unmatched;
            CorpusBattleCount = corpus;
            NoclipEncounterCount = noclip;
        }

        /// <summary>Build the index by hashing every btl bin and every 0e/ bin. Deterministic.
        /// Also computes a similarity fallback: for battles with no byte-identical 0e/ hit, finds the
        /// 0e/ bin sharing the most 256-byte blocks (rsync-style) and records it in <see cref="BattleIdToNearestEncounter"/>
        /// when above <see cref="MinSimilarityScore"/> — so battles whose SAME encounter exists in the noclip
        /// extraction with a few differing bytes (SHA misses) still open in the RealGame.</summary>
        public static EncounterIndexBridge Build(string? btlRoot = null, string? noclip0eRoot = null)
        {
            string btl = btlRoot ?? DefaultBtlRoot;
            string enc = noclip0eRoot ?? DefaultNoclip0eRoot;

            var corpus = new List<(string BattleId, string FullPath)>();
            if (Directory.Exists(btl))
            {
                foreach (string dir in Directory.EnumerateDirectories(btl))
                {
                    string battleId = Path.GetFileName(dir);
                    string bin = Path.Combine(dir, battleId + ".bin");
                    if (File.Exists(bin))
                        corpus.Add((battleId, bin));
                }
            }
            corpus.Sort((a, b) => string.Compare(a.BattleId, b.BattleId, StringComparison.Ordinal));

            Dictionary<string, int> encByHash = new(StringComparer.Ordinal);
            // Block signature index (rsync-style): blockHash -> list of 0e/ encounter ids.
            var encBlockIndex = new Dictionary<ulong, List<int>>();
            if (Directory.Exists(enc))
            {
                foreach (string file in Directory.EnumerateFiles(enc, "*.bin").OrderBy(f => f, StringComparer.Ordinal))
                {
                    string idHex = Path.GetFileNameWithoutExtension(file);
                    if (!int.TryParse(idHex, System.Globalization.NumberStyles.HexNumber, null, out int encId))
                        continue;
                    byte[] data = File.ReadAllBytes(file);
                    string hash = Sha256OfBytes(data);
                    encByHash.TryAdd(hash, encId);
                    foreach (ulong b in HashBlocks(data, SimilarityBlockBytes))
                    {
                        if (!encBlockIndex.TryGetValue(b, out List<int>? list))
                        {
                            list = new List<int>();
                            encBlockIndex[b] = list;
                        }
                        if (!list.Contains(encId)) list.Add(encId);
                    }
                }
            }

            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var tableMap = ReadTableIdentities(enc);
            var nearest = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var unmatched = new List<string>();
            foreach ((string battleId, string path) in corpus)
            {
                byte[] data = File.ReadAllBytes(path);
                string hash = Sha256OfBytes(data);
                if (encByHash.TryGetValue(hash, out int encId))
                {
                    map[battleId] = encId;
                }
                else
                {
                    if (tableMap.ContainsKey(battleId))
                        continue;
                    unmatched.Add(battleId);
                    // Similarity fallback: pick the 0e/ with highest block overlap.
                    List<ulong> battleBlocks = HashBlocks(data, SimilarityBlockBytes).ToList();
                    var scores = new Dictionary<int, int>();
                    foreach (ulong b in battleBlocks)
                    {
                        if (encBlockIndex.TryGetValue(b, out List<int>? list))
                            foreach (int id in list)
                                scores[id] = scores.TryGetValue(id, out int c) ? c + 1 : 1;
                    }
                    int bestId = -1, bestScore = -1;
                    foreach ((int id, int c) in scores)
                    {
                        if (c > bestScore || (c == bestScore && id < bestId))
                        {
                            bestScore = c;
                            bestId = id;
                        }
                    }
                    if (bestId >= 0 && battleBlocks.Count > 0)
                    {
                        double ratio = (double)bestScore / battleBlocks.Count;
                        if (ratio >= MinSimilarityScore)
                            nearest[battleId] = bestId;
                    }
                }
            }

            return new EncounterIndexBridge(map, tableMap, nearest, unmatched, corpus.Count, encByHash.Count);
        }

        private static Dictionary<string, int> ReadTableIdentities(string encounterDirectory)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(encounterDirectory)) return result;
            string? root = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(encounterDirectory));
            if (root == null) return result;
            string path = Path.Combine(root, "0d", "0000.bin");
            if (!File.Exists(path)) return result;
            try
            {
                var table = EncounterTable_File.Read(File.ReadAllBytes(path));
                foreach (var entry in table.Tables)
                {
                    // Mirrors the bundled NoClip battle-list reader: the file number advances
                    // across all groups, independently of the formation's displayed ID.
                    int id = entry.FormationOffset;
                    foreach (var group in entry.Groups)
                    foreach (var formation in group.Formations)
                    {
                        if (id is >= 0 and <= ushort.MaxValue)
                            result.TryAdd(formation.BattleId, id);
                        id++;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                FFXProjectEditor.Diagnostics.DebugLog.Warn("Aurora.EncounterIndex", $"Cannot read NoClip encounter identities: {ex.Message}");
            }
            return result;
        }

        private static IEnumerable<ulong> HashBlocks(byte[] data, int blockSize)
        {
            // Distinct block hashes (FNV-1a over each block) — dedup so repeated identical blocks
            // (padding/sentinels) don't inflate the overlap score toward a file full of zeros.
            var seen = new HashSet<ulong>();
            for (int off = 0; off + blockSize <= data.Length; off += blockSize)
            {
                ulong h = Fnv1a(data, off, blockSize);
                if (seen.Add(h)) yield return h;
            }
        }

        private static ulong Fnv1a(byte[] data, int offset, int count)
        {
            ulong h = 14695981039346656037UL; // FNV-1a 64-bit offset basis
            for (int i = offset; i < offset + count; i++)
            {
                h ^= data[i];
                h *= 1099511628211UL; // FNV prime 64-bit
            }
            return h;
        }

        /// <summary>Try to resolve a battle id (e.g. "azit03_00") to the noclip encounter id (0e/00XX.bin).
        /// Falls back to the similarity match (nearest) when there's no byte-identical 0e/ hit, so battles
        /// whose SAME encounter exists in the noclip extraction with minor byte diffs still resolve.</summary>
        public bool TryResolve(string battleId, out int encounterId)
        {
            encounterId = -1;
            if (battleId == null) return false;
            if (BattleIdToTableEncounter.TryGetValue(battleId, out encounterId)) return true;
            if (BattleIdToEncounter.TryGetValue(battleId, out encounterId)) return true;
            if (BattleIdToNearestEncounter.TryGetValue(battleId, out encounterId)) return true;
            return false;
        }

        /// <summary>True when a battle resolved only via the similarity fallback (not byte-identical).
        /// Lets callers report "match aproximado" honestly instead of silently opening a near-encounter.</summary>
        public bool IsSimilarityResolved(string battleId)
            => battleId != null && !BattleIdToTableEncounter.ContainsKey(battleId) && BattleIdToNearestEncounter.ContainsKey(battleId);

        /// <summary>Serialize the index to JSON (work/ artifact — environment-specific paths are NOT stored).</summary>
        public string ToJson()
        {
            return JsonSerializer.Serialize(new
            {
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                corpusBattleCount = CorpusBattleCount,
                noclipEncounterCount = NoclipEncounterCount,
                matchedCount = BattleIdToEncounter.Count,
                similarityResolvedCount = BattleIdToNearestEncounter.Count,
                tableResolvedCount = BattleIdToTableEncounter.Count,
                unmatchedCount = UnmatchedBattleIds.Count,
                unmatched = UnmatchedBattleIds,
                entries = BattleIdToEncounter.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .ToDictionary(kv => kv.Key, kv => kv.Value),
                similarityEntries = BattleIdToNearestEncounter.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .ToDictionary(kv => kv.Key, kv => kv.Value),
                tableEntries = BattleIdToTableEncounter.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .ToDictionary(kv => kv.Key, kv => kv.Value),
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        private static string Sha256OfBytes(byte[] data)
        {
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(data));
        }
    }
}

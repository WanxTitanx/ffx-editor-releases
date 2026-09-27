using FFXProjectEditor.FfxLib.Ai;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Ps3
{
    /// <summary>Cross-reference spell rows, overlay signatures and DLL siblings for safer visual patching.</summary>
    public static class MagicDllFamilyComparator
    {
        static readonly Lazy<IReadOnlyDictionary<int, List<MagicDllFamilySpellUsage>>> UsageByMagicId =
            new(BuildUsageIndex);

        static readonly Lazy<IReadOnlyDictionary<int, MagicDllOverlayRow>> OverlayByMagicId =
            new(LoadOverlayRows);

        public static IReadOnlyList<MagicDllFamilySpellUsage> GetSpellsUsingMagicId(int magicId)
        {
            if (!UsageByMagicId.Value.TryGetValue(magicId, out List<MagicDllFamilySpellUsage>? list))
                return [];
            return list;
        }

        public static MagicDllFamilySiblingReport BuildSiblingReport(int magicId, string? magicDllRoot = null)
        {
            magicDllRoot ??= MagicDllSemanticAnalyzer.DefaultFfxMagicFilesRoot;
            MagicDllOverlayRow? overlay = OverlayByMagicId.Value.GetValueOrDefault(magicId);
            string signature = overlay?.SlotKindSignature ?? Strings.F2_no_overlay_row_818956bc;
            IReadOnlyList<MagicDllFamilySpellUsage> spells = GetSpellsUsingMagicId(magicId);

            List<int> siblingIds = OverlayByMagicId.Value
                .Where(kv => string.Equals(kv.Value.SlotKindSignature, signature, StringComparison.OrdinalIgnoreCase)
                    && kv.Key != magicId)
                .Select(kv => kv.Key)
                .OrderBy(id => id)
                .ToList();

            string dllPath = Path.Combine(magicDllRoot, $"magic_{magicId:D4}.dll");
            string dllHash = File.Exists(dllPath) ? Sha256Prefix(dllPath) : "(missing)";

            List<MagicDllFamilySiblingMatch> siblings = [];
            foreach (int siblingId in siblingIds.Take(48))
            {
                string siblingDll = Path.Combine(magicDllRoot, $"magic_{siblingId:D4}.dll");
                string siblingHash = File.Exists(siblingDll) ? Sha256Prefix(siblingDll) : "(missing)";
                siblings.Add(new MagicDllFamilySiblingMatch(
                    siblingId,
                    OverlayByMagicId.Value.GetValueOrDefault(siblingId)?.SlotKindSignature ?? signature,
                    siblingHash,
                    siblingHash.Equals(dllHash, StringComparison.OrdinalIgnoreCase),
                    GetSpellsUsingMagicId(siblingId).Take(6).ToList()));
            }

            List<MagicDllFamilySiblingMatch> twinDlls = siblings
                .Where(s => s.SameDllHashAsSelected)
                .OrderBy(s => s.MagicId)
                .Take(12)
                .ToList();

            return new MagicDllFamilySiblingReport(
                magicId,
                signature,
                overlay?.NonzeroSlotCount ?? 0,
                dllHash,
                spells,
                siblings,
                twinDlls);
        }

        public static MagicDllFamilyPairDiff ComparePair(int magicIdA, int magicIdB, string? magicDllRoot = null)
        {
            magicDllRoot ??= MagicDllSemanticAnalyzer.DefaultFfxMagicFilesRoot;
            MagicDllOverlayRow? a = OverlayByMagicId.Value.GetValueOrDefault(magicIdA);
            MagicDllOverlayRow? b = OverlayByMagicId.Value.GetValueOrDefault(magicIdB);

            string dllA = Path.Combine(magicDllRoot, $"magic_{magicIdA:D4}.dll");
            string dllB = Path.Combine(magicDllRoot, $"magic_{magicIdB:D4}.dll");
            bool sameDllHash = File.Exists(dllA) && File.Exists(dllB)
                && string.Equals(Sha256Prefix(dllA), Sha256Prefix(dllB), StringComparison.OrdinalIgnoreCase);

            return new MagicDllFamilyPairDiff(
                magicIdA,
                magicIdB,
                a?.SlotKindSignature ?? "(missing)",
                b?.SlotKindSignature ?? "(missing)",
                string.Equals(a?.SlotKindSignature, b?.SlotKindSignature, StringComparison.OrdinalIgnoreCase),
                sameDllHash,
                GetSpellsUsingMagicId(magicIdA),
                GetSpellsUsingMagicId(magicIdB));
        }

        static IReadOnlyDictionary<int, List<MagicDllFamilySpellUsage>> BuildUsageIndex()
        {
            Dictionary<int, List<MagicDllFamilySpellUsage>> index = new();
            foreach (AiCommandMetadataEntry entry in AiCommandMetadataCatalog.Entries)
            {
                IReadOnlyList<int> animIds = MagicDllMoveAnimParser.EnumerateMagicIds(entry.RawProperties);
                if (animIds.Count == 0)
                    continue;

                int anim1 = animIds[0];
                int anim2 = animIds.Count > 1 ? animIds[1] : animIds[0];
                MagicDllFamilySpellUsage usage = new(
                    entry.DisplayName,
                    entry.SourceFile,
                    entry.Operand,
                    anim1,
                    anim2,
                    entry.Power,
                    entry.HitCount,
                    entry.ElementText);

                AddUsage(index, anim1, usage);
                foreach (int extraId in animIds.Skip(1))
                {
                    if (extraId != anim1)
                        AddUsage(index, extraId, usage);
                }
            }

            return index;
        }

        static void AddUsage(Dictionary<int, List<MagicDllFamilySpellUsage>> index, int magicId, MagicDllFamilySpellUsage usage)
        {
            if (!index.TryGetValue(magicId, out List<MagicDllFamilySpellUsage>? list))
            {
                list = [];
                index[magicId] = list;
            }

            if (!list.Any(row => row.Operand == usage.Operand && row.DisplayName == usage.DisplayName))
                list.Add(usage);
        }

        static bool TryParseMoveAnim(string raw, out int anim1, out int anim2) =>
            MagicDllMoveAnimParser.TryParseMoveAnim(raw, out anim1, out anim2);

        static IReadOnlyDictionary<int, MagicDllOverlayRow> LoadOverlayRows()
        {
            string? repoRoot = FindRepoRoot(AppContext.BaseDirectory)
                ?? FindRepoRoot(Environment.CurrentDirectory);
            if (string.IsNullOrWhiteSpace(repoRoot))
                return new Dictionary<int, MagicDllOverlayRow>();

            string csvPath = Path.Combine(repoRoot, "docs", "reverse", "magicfiles_overlay_2026-06-03", "ffx_magic_overlay_tables.csv");
            if (!File.Exists(csvPath))
                return new Dictionary<int, MagicDllOverlayRow>();

            Dictionary<int, MagicDllOverlayRow> rows = new();
            string[] lines = File.ReadAllLines(csvPath);
            if (lines.Length < 2)
                return rows;

            string[] headers = SplitCsv(lines[0]);
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                    continue;
                string[] values = SplitCsv(lines[i]);
                Dictionary<string, string> row = headers
                    .Select((h, idx) => new { h, v = idx < values.Length ? values[idx] : string.Empty })
                    .ToDictionary(x => x.h, x => x.v, StringComparer.OrdinalIgnoreCase);

                if (!row.TryGetValue("magic_id", out string? idText) || !int.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int magicId))
                    continue;

                int.TryParse(row.GetValueOrDefault("nonzero_slot_count", "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int nonzero);
                rows[magicId] = new MagicDllOverlayRow(
                    magicId,
                    row.GetValueOrDefault("slot_kind_signature", string.Empty),
                    row.GetValueOrDefault("slot_section_signature", string.Empty),
                    nonzero);
            }

            return rows;
        }

        static string[] SplitCsv(string line) =>
            line.Split(',', StringSplitOptions.None);

        static string Sha256Prefix(string path)
        {
            byte[] hash = SHA256.HashData(File.ReadAllBytes(path));
            return Convert.ToHexString(hash).ToLowerInvariant()[..16];
        }

        static string? FindRepoRoot(string start)
        {
            string? current = start;
            for (int i = 0; i < 12 && !string.IsNullOrWhiteSpace(current); i++)
            {
                if (File.Exists(Path.Combine(current, "FFXProjectEditor.csproj"))
                    || File.Exists(Path.Combine(current, "PORT_STATUS.md")))
                    return current;
                current = Directory.GetParent(current)?.FullName;
            }
            return null;
        }
    }

    public sealed record MagicDllFamilySpellUsage(
        string DisplayName,
        string SourceFile,
        ushort Operand,
        int Anim1,
        int Anim2,
        int? Power,
        int? HitCount,
        string ElementText)
    {
        public string MoveAnimSummary => $"magic_{Anim1:D4}/magic_{Anim2:D4}";
        public string OperandHex => $"0x{Operand:X4}";
        public string GameplaySummary => $"{Power?.ToString() ?? "?"}pwr · {HitCount?.ToString() ?? "?"}hits · {ElementText}";
    }

    sealed record MagicDllOverlayRow(int MagicId, string SlotKindSignature, string SlotSectionSignature, int NonzeroSlotCount);

    public sealed record MagicDllFamilySiblingMatch(
        int MagicId,
        string OverlaySignature,
        string DllSha256Prefix,
        bool SameDllHashAsSelected,
        IReadOnlyList<MagicDllFamilySpellUsage> SampleSpells);

    public sealed record MagicDllFamilySiblingReport(
        int MagicId,
        string OverlaySignature,
        int NonzeroSlotCount,
        string DllSha256Prefix,
        IReadOnlyList<MagicDllFamilySpellUsage> SpellsUsingThisDll,
        IReadOnlyList<MagicDllFamilySiblingMatch> OverlaySiblings,
        IReadOnlyList<MagicDllFamilySiblingMatch> TwinDllMatches);

    public sealed record MagicDllFamilyPairDiff(
        int MagicIdA,
        int MagicIdB,
        string OverlayA,
        string OverlayB,
        bool SameOverlaySignature,
        bool SameDllHash,
        IReadOnlyList<MagicDllFamilySpellUsage> SpellsA,
        IReadOnlyList<MagicDllFamilySpellUsage> SpellsB);
}

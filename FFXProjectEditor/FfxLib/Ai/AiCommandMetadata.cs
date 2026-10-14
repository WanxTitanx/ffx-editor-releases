using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai
{
    public enum AiCommandMetadataCategory
    {
        Item = 0x2000,
        Character = 0x3000,
        MonsterMagic1 = 0x4000,
        MonsterMagic2 = 0x6000,
    }

    public sealed record AiCommandMetadataEntry(
        ushort Operand,
        AiCommandMetadataCategory Category,
        string SourceFile,
        string DisplayName,
        string Description,
        string RoleText,
        int? HitCount,
        string Formula,
        string FormulaHex,
        int? Power,
        int? Rank,
        int? MpCost,
        string HitPercent,
        string TargetText,
        string StatusText,
        string ElementText,
        string RawProperties,
        string ProvenanceLabels)
    {
        public ushort LocalId => (ushort)(Operand & AiCommandId.IdMask);
        public string OperandHex => $"0x{Operand:X4}";

        // Character, MonMagic1 and MonMagic2 are the categories already proven for performCommand/forcePerformCommand.
        // Item rows are loaded as read-only metadata, but are not promoted as Monster AI operands yet.
        public bool IsKnownAiPerformOperandCategory =>
            Category is AiCommandMetadataCategory.Character
                or AiCommandMetadataCategory.MonsterMagic1
                or AiCommandMetadataCategory.MonsterMagic2;

        public string AiOperandEvidence => IsKnownAiPerformOperandCategory
            ? "ai-perform-command-category-proved"
            : "metadata-only-not-ai-operand-proved";

        public string SearchBlob => string.Join(" ",
            OperandHex, LocalId.ToString(CultureInfo.InvariantCulture), Category, SourceFile,
            DisplayName, Description, RoleText, Formula, FormulaHex, Power, Rank, MpCost,
            HitPercent, TargetText, StatusText, ElementText, RawProperties, ProvenanceLabels,
            AiOperandEvidence);

        internal static AiCommandMetadataEntry ParsePacked(string packed)
        {
            string[] p = packed.Split('\t');
            if (p.Length != 18)
                throw new FormatException($"AiCommandMetadata packed row has {p.Length} fields, expected 18.");

            return new AiCommandMetadataEntry(
                ParseUShort(p[0]),
                Enum.Parse<AiCommandMetadataCategory>(p[1], ignoreCase: false),
                p[2],
                p[3],
                p[4],
                p[5],
                ParseNullableInt(p[6]),
                p[7],
                p[8],
                ParseNullableInt(p[9]),
                ParseNullableInt(p[10]),
                ParseNullableInt(p[11]),
                p[12],
                p[13],
                p[14],
                p[15],
                p[16],
                p[17]);
        }

        static ushort ParseUShort(string text) =>
            ushort.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);

        static int? ParseNullableInt(string text) =>
            string.IsNullOrWhiteSpace(text)
                ? null
                : int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    public static partial class AiCommandMetadataCatalog
    {
        static readonly object LiveGate = new();
        static readonly Lazy<IReadOnlyList<AiCommandMetadataEntry>> _baseEntries = new(BuildBaseEntries);
        static readonly Lazy<IReadOnlyDictionary<ushort, AiCommandMetadataEntry>> _baseByOperand =
            new(() => _baseEntries.Value.ToDictionary(e => e.Operand));
        static Dictionary<ushort, AiCommandMetadataEntry> _liveByOperand = new();
        static IReadOnlyList<AiCommandMetadataEntry>? _mergedEntries;
        static IReadOnlyDictionary<ushort, AiCommandMetadataEntry>? _mergedByOperand;

        public static IReadOnlyList<AiCommandMetadataEntry> Entries => GetMergedEntries();
        public static IReadOnlyDictionary<ushort, AiCommandMetadataEntry> ByOperand => GetMergedByOperand();

        public static IEnumerable<AiCommandMetadataEntry> ForCategory(AiCommandMetadataCategory category) =>
            Entries.Where(e => e.Category == category);

        public static bool TryGet(ushort operand, [NotNullWhen(true)] out AiCommandMetadataEntry? entry) =>
            GetMergedByOperand().TryGetValue(operand, out entry);

        public static bool HasBaseEntry(ushort operand) => _baseByOperand.Value.ContainsKey(operand);

        public static void ApplyLiveRows(IReadOnlyList<AiCommandMetadataEntry> rows)
        {
            lock (LiveGate)
            {
                _liveByOperand = rows.ToDictionary(e => e.Operand);
                InvalidateMergedCache();
            }
        }

        public static void ClearLiveRows()
        {
            lock (LiveGate)
            {
                _liveByOperand.Clear();
                InvalidateMergedCache();
            }
        }

        static void InvalidateMergedCache()
        {
            _mergedEntries = null;
            _mergedByOperand = null;
        }

        static IReadOnlyList<AiCommandMetadataEntry> GetMergedEntries()
        {
            lock (LiveGate)
            {
                if (_mergedEntries != null)
                    return _mergedEntries;

                if (_liveByOperand.Count == 0)
                {
                    _mergedEntries = _baseEntries.Value;
                    return _mergedEntries;
                }

                Dictionary<ushort, AiCommandMetadataEntry> merged =
                    _baseEntries.Value.ToDictionary(e => e.Operand);
                foreach (KeyValuePair<ushort, AiCommandMetadataEntry> live in _liveByOperand)
                    merged[live.Key] = live.Value;

                _mergedEntries = merged.Values.OrderBy(e => e.Operand).ToArray();
                return _mergedEntries;
            }
        }

        static IReadOnlyDictionary<ushort, AiCommandMetadataEntry> GetMergedByOperand()
        {
            lock (LiveGate)
            {
                if (_mergedByOperand != null)
                    return _mergedByOperand;

                _mergedByOperand = GetMergedEntries().ToDictionary(e => e.Operand);
                return _mergedByOperand;
            }
        }

        private static IReadOnlyList<AiCommandMetadataEntry> BuildBaseEntries() =>
            BuildGeneratedEntries().ToArray();

        private static partial IReadOnlyList<AiCommandMetadataEntry> BuildGeneratedEntries();
    }
}

using System;
using System.Collections.Generic;
using System.IO;

namespace FFXProjectEditor.FfxLib.ThunderPlains
{
    public static class ThunderPlainsLightning_File
    {
        const string KAMI0000_EVENT = @"jppc\event\obj\ka\kami0000\kami0000.ebp";
        const string KAMI0300_EVENT = @"jppc\event\obj\ka\kami0300\kami0300.ebp";

        public const int MIN_THRESHOLD = 1;
        public const int MAX_THRESHOLD = 255;

        public static readonly IReadOnlyList<ThresholdDef> KnownThresholds = new ThresholdDef[]
        {
            new("consec_5",   "kami0000", 0x5961, "kami0300", 0x9248, 5,   "bit0", "2x X-Potion",         false),
            new("consec_10",  "kami0000", 0x5969, "kami0300", 0x9250, 10,  "bit1", "2x Mega-Potion",     false),
            new("consec_20",  "kami0000", 0x5971, "kami0300", 0x9258, 20,  "bit2", "2x MP Sphere",       false),
            new("consec_50",  "kami0000", 0x5979, "kami0300", 0x9260, 50,  "bit3", "3x Strength Sphere", false),
            new("consec_100", "kami0000", 0x5981, "kami0300", 0x9268, 100, "bit4", "3x HP Sphere",       false),
            new("consec_150", "kami0000", 0x5989, "kami0300", 0x9270, 150, "bit5", "4x Megalixir",       false),
            new("consec_200", "kami0000", 0x5991, "kami0300", 0x9278, 200, "prog", "Venus Sigil",        false),

            new("total_30",   "kami0000", 0x59C2, "kami0300", 0x92A9, 30,  "bit6", "Ether",              false),
            new("total_30b",  "kami0000", 0x5A26, "kami0300", 0x930D, 30,  "bit6", "Ether (hit handler)",true),
            new("total_80",   "kami0000", 0x59CA, "kami0300", 0x92B1, 80,  "bit7", "Elixir",             false),
            new("total_80b",  "kami0000", 0x5A2E, "kami0300", 0x9315, 80,  "bit7", "Elixir (hit handler)",true),
        };

        public static bool IsAtelPatternValid(byte[] data, int offset)
        {
            if (offset < 1 || offset + 3 >= data.Length) return false;
            return data[offset - 1] == AtelPattern.OP_AE
                && data[offset + 1] == AtelPattern.OP_00
                && data[offset + 2] == AtelPattern.OP_29
                && data[offset + 3] == AtelPattern.OP_06;
        }

        public static int FindAtelOffsetWithValue(byte[] data, int expectedValue)
        {
            for (int i = 1; i + 3 < data.Length; i++)
            {
                if (data[i - 1] == AtelPattern.OP_AE
                    && data[i] == expectedValue
                    && data[i + 1] == AtelPattern.OP_00
                    && data[i + 2] == AtelPattern.OP_29
                    && data[i + 3] == AtelPattern.OP_06)
                {
                    return i;
                }
            }
            return -1;
        }

        public static string? ResolvePath(string relativePath, string? projectPath, string? ffxPs2Root)
        {
            if (!string.IsNullOrWhiteSpace(projectPath))
            {
                string wsPath = Path.Combine(projectPath, relativePath);
                if (File.Exists(wsPath)) return wsPath;
            }
            if (!string.IsNullOrWhiteSpace(ffxPs2Root))
            {
                string extPath = Path.Combine(ffxPs2Root, "ffx", "master", relativePath);
                if (File.Exists(extPath)) return extPath;
            }
            return null;
        }

        public static (byte[]? kami0000, byte[]? kami0300, string? path0000, string? path0300)
            LoadBoth(string? projectPath, string? ffxPs2Root)
        {
            string? p0000 = ResolvePath(KAMI0000_EVENT, projectPath, ffxPs2Root);
            string? p0300 = ResolvePath(KAMI0300_EVENT, projectPath, ffxPs2Root);

            byte[]? d0000 = p0000 != null ? TryReadAllBytes(p0000) : null;
            byte[]? d0300 = p0300 != null ? TryReadAllBytes(p0300) : null;

            return (d0000, d0300, p0000, p0300);
        }

        static byte[]? TryReadAllBytes(string path)
        {
            try { return File.ReadAllBytes(path); }
            catch { return null; }
        }

        public static ThresholdReadResult? ReadThresholds(string? projectPath, string? ffxPs2Root)
        {
            var (d0000, d0300, p0000, p0300) = LoadBoth(projectPath, ffxPs2Root);
            if (d0000 == null || d0300 == null)
                return null;

            var rows = new List<ThresholdReadRow>(KnownThresholds.Count);
            foreach (ThresholdDef t in KnownThresholds)
            {
                (int v0000, bool ok0000) = ReadValue(d0000, t.Kami0000Offset);
                (int v0300, bool ok0300) = ReadValue(d0300, t.Kami0300Offset);

                rows.Add(new ThresholdReadRow
                {
                    Id = t.Id,
                    Reward = t.Reward,
                    BitFlag = t.BitFlag,
                    DefaultValue = t.DefaultValue,
                    IsSecondaryOccurrence = t.IsSecondaryOccurrence,
                    Kami0000Value = v0000,
                    Kami0300Value = v0300,
                    Divergent = ok0000 && ok0300 && v0000 != v0300,
                    PatternValidKami0000 = IsAtelPatternValid(d0000, t.Kami0000Offset),
                    PatternValidKami0300 = IsAtelPatternValid(d0300, t.Kami0300Offset),
                    OffsetKami0000InBounds = ok0000,
                    OffsetKami0300InBounds = ok0300,
                });
            }
            return new ThresholdReadResult
            {
                Rows = rows,
                PathKami0000 = p0000,
                PathKami0300 = p0300,
                DataKami0000 = d0000,
                DataKami0300 = d0300,
            };
        }

        static (int value, bool ok) ReadValue(byte[] data, int offset)
        {
            if (offset < 0 || offset >= data.Length) return (0, false);
            return (data[offset], true);
        }

        public static ThresholdApplyResult? ApplyThresholds(
            byte[] dataKami0000,
            byte[] dataKami0300,
            IReadOnlyDictionary<string, int> newValues,
            IReadOnlyDictionary<string, int>? canonicalOffsets0000 = null,
            IReadOnlyDictionary<string, int>? canonicalOffsets0300 = null)
        {
            byte[] d0000 = (byte[])dataKami0000.Clone();
            byte[] d0300 = (byte[])dataKami0300.Clone();

            canonicalOffsets0000 ??= BuildOffsetTable(which: 0);
            canonicalOffsets0300 ??= BuildOffsetTable(which: 1);

            int bytesChanged = 0;
            var changes = new List<ThresholdChangeRecord>();

            foreach (ThresholdDef t in KnownThresholds)
            {
                if (!newValues.TryGetValue(t.Id, out int newValue)) continue;

                if (newValue < MIN_THRESHOLD || newValue > MAX_THRESHOLD)
                {
                    return ThresholdApplyResult.Failure(
                        $"Threshold '{t.Id}' value {newValue} is out of range "
                        + $"({MIN_THRESHOLD}..{MAX_THRESHOLD}).");
                }

                byte capped = (byte)newValue;

                if (!canonicalOffsets0000.TryGetValue(t.Id, out int off0000))
                    return ThresholdApplyResult.Failure($"Missing canonical offset for '{t.Id}' (kami0000).");
                if (!canonicalOffsets0300.TryGetValue(t.Id, out int off0300))
                    return ThresholdApplyResult.Failure($"Missing canonical offset for '{t.Id}' (kami0300).");

                if (!IsAtelPatternValid(d0000, off0000))
                    return ThresholdApplyResult.Failure(
                        $"ATEL pattern mismatch at kami0000 offset 0x{off0000:X4} for '{t.Id}'. "
                        + "Refusing to write — file may be a different version.");
                if (!IsAtelPatternValid(d0300, off0300))
                    return ThresholdApplyResult.Failure(
                        $"ATEL pattern mismatch at kami0300 offset 0x{off0300:X4} for '{t.Id}'. "
                        + "Refusing to write — file may be a different version.");

                if (d0000[off0000] != capped)
                {
                    changes.Add(new ThresholdChangeRecord(t.Id, "kami0000", off0000, d0000[off0000], capped));
                    d0000[off0000] = capped;
                    bytesChanged++;
                }
                if (d0300[off0300] != capped)
                {
                    changes.Add(new ThresholdChangeRecord(t.Id, "kami0300", off0300, d0300[off0300], capped));
                    d0300[off0300] = capped;
                    bytesChanged++;
                }
            }

            if (bytesChanged == 0)
                return null;

            return ThresholdApplyResult.Success(d0000, d0300, bytesChanged, changes);
        }

        static Dictionary<string, int> BuildOffsetTable(int which) =>
            new(KnownThresholds.Count, System.StringComparer.Ordinal)
            {
                { "consec_5",   which == 0 ? 0x5961 : 0x9248 },
                { "consec_10",  which == 0 ? 0x5969 : 0x9250 },
                { "consec_20",  which == 0 ? 0x5971 : 0x9258 },
                { "consec_50",  which == 0 ? 0x5979 : 0x9260 },
                { "consec_100", which == 0 ? 0x5981 : 0x9268 },
                { "consec_150", which == 0 ? 0x5989 : 0x9270 },
                { "consec_200", which == 0 ? 0x5991 : 0x9278 },
                { "total_30",   which == 0 ? 0x59C2 : 0x92A9 },
                { "total_30b",  which == 0 ? 0x5A26 : 0x930D },
                { "total_80",   which == 0 ? 0x59CA : 0x92B1 },
                { "total_80b",  which == 0 ? 0x5A2E : 0x9315 },
            };
    }

    public static class AtelPattern
    {
        public const byte OP_AE = 0xAE;
        public const byte OP_00 = 0x00;
        public const byte OP_29 = 0x29;
        public const byte OP_06 = 0x06;
    }

    public sealed record ThresholdDef(
        string Id,
        string FilePrefix,
        int Kami0000Offset,
        string File0300Prefix,
        int Kami0300Offset,
        int DefaultValue,
        string BitFlag,
        string Reward,
        bool IsSecondaryOccurrence);

    public sealed class ThresholdReadRow
    {
        public string Id { get; init; } = "";
        public string Reward { get; init; } = "";
        public string BitFlag { get; init; } = "";
        public int DefaultValue { get; init; }
        public bool IsSecondaryOccurrence { get; init; }

        public int Kami0000Value { get; init; }
        public int Kami0300Value { get; init; }
        public bool Divergent { get; init; }
        public bool PatternValidKami0000 { get; init; }
        public bool PatternValidKami0300 { get; init; }
        public bool OffsetKami0000InBounds { get; init; }
        public bool OffsetKami0300InBounds { get; init; }

        public int DisplayValue => OffsetKami0000InBounds ? Kami0000Value
                                       : OffsetKami0300InBounds ? Kami0300Value
                                       : DefaultValue;
    }

    public sealed class ThresholdReadResult
    {
        public required IReadOnlyList<ThresholdReadRow> Rows { get; init; }
        public string? PathKami0000 { get; init; }
        public string? PathKami0300 { get; init; }
        public byte[]? DataKami0000 { get; init; }
        public byte[]? DataKami0300 { get; init; }
    }

    public readonly record struct ThresholdChangeRecord(
        string ThresholdId,
        string FileTag,
        int Offset,
        byte OldValue,
        byte NewValue);

    public sealed class ThresholdApplyResult
    {
        public bool IsSuccess { get; private init; }
        public string? Error { get; private init; }
        public byte[]? DataKami0000 { get; private init; }
        public byte[]? DataKami0300 { get; private init; }
        public int BytesChanged { get; private init; }
        public IReadOnlyList<ThresholdChangeRecord> Changes { get; private init; }
            = Array.Empty<ThresholdChangeRecord>();

        public static ThresholdApplyResult Success(
            byte[] d0000, byte[] d0300, int bytesChanged, IReadOnlyList<ThresholdChangeRecord> changes) =>
            new()
            {
                IsSuccess = true,
                DataKami0000 = d0000,
                DataKami0300 = d0300,
                BytesChanged = bytesChanged,
                Changes = changes,
            };

        public static ThresholdApplyResult Failure(string message) =>
            new() { IsSuccess = false, Error = message };
    }
}

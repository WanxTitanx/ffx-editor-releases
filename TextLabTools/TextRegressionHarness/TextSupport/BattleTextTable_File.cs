using FFXProjectEditor.Utils.Encoding;

namespace TextRegressionHarness.TextSupport;

public sealed class BattleTextTable_File
{
    static readonly HashSet<byte> PrefixControls = [0x09, 0x12];

    public required int FileSize { get; init; }
    public required int MinIndex { get; init; }
    public required int MaxIndex { get; init; }
    public required int EntryLength { get; init; }
    public required int DataBlockLength { get; init; }
    public required IReadOnlyList<BattleTextTable_Entry> Entries { get; init; }
    public required BattleTextTaxonomy Taxonomy { get; init; }

    public int EntryCount => Entries.Count;
    public string TaxonomySummary => Taxonomy.BuildSummary();

    public static BattleTextTable_File Read(byte[] bytes, Dictionary<byte, char> decoder)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(decoder);

        if (bytes.Length < 0x14)
        {
            throw new InvalidDataException("Battle text table is too small.");
        }

        int minIndex = TextBinary_Util.ReadUInt16(bytes, 0x08);
        int maxIndex = TextBinary_Util.ReadUInt16(bytes, 0x0A);
        int entryLength = TextBinary_Util.ReadUInt16(bytes, 0x0C);
        int dataBlockLength = TextBinary_Util.ReadUInt16(bytes, 0x0E);

        if (entryLength != 0x08)
        {
            throw new InvalidDataException($"Battle text table expected 8-byte entries, got {entryLength:X}h.");
        }

        if (dataBlockLength <= 0 || 0x14 + dataBlockLength > bytes.Length)
        {
            throw new InvalidDataException("Battle text table has an invalid data block length.");
        }

        if (maxIndex < minIndex)
        {
            throw new InvalidDataException("Battle text table has an invalid index range.");
        }

        int entryCount = (maxIndex - minIndex) + 1;
        if (entryCount <= 0)
        {
            throw new InvalidDataException("Battle text table has no entries.");
        }

        if (entryCount * entryLength > dataBlockLength)
        {
            throw new InvalidDataException("Battle text table data block is shorter than its declared entries.");
        }

        int stringDataOffset = 0x14 + dataBlockLength;
        byte[] stringBytes = new byte[bytes.Length - stringDataOffset];
        Array.Copy(bytes, stringDataOffset, stringBytes, 0, stringBytes.Length);

        BattleTextTaxonomy taxonomy = new();
        List<BattleTextTable_Entry> entries = new(entryCount);
        for (int i = 0; i < entryCount; i++)
        {
            int entryOffset = 0x14 + (i * entryLength);
            ushort word0 = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x00);
            ushort word1 = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x02);
            ushort word2 = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x04);
            ushort word3 = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x06);

            BattleStringRead primary = DecodeBattleString(stringBytes, word0, decoder);
            BattleStringRead secondary = DecodeBattleString(stringBytes, word1, decoder);
            BattleStringRead tertiary = DecodeBattleString(stringBytes, word2, decoder);
            BattleStringRead quaternary = DecodeBattleString(stringBytes, word3, decoder);

            taxonomy.Observe(primary);
            taxonomy.Observe(secondary);
            taxonomy.Observe(tertiary);
            taxonomy.Observe(quaternary);

            entries.Add(new BattleTextTable_Entry
            {
                Index = minIndex + i,
                Word0 = word0,
                Word1 = word1,
                Word2 = word2,
                Word3 = word3,
                PrimaryText = primary.Text,
                PrimaryPrefix = primary.PrefixLabel,
                PrimaryResolution = primary.ResolutionLabel,
                SecondaryText = secondary.Text,
                SecondaryPrefix = secondary.PrefixLabel,
                SecondaryResolution = secondary.ResolutionLabel,
                TertiaryText = tertiary.Text,
                TertiaryPrefix = tertiary.PrefixLabel,
                TertiaryResolution = tertiary.ResolutionLabel,
                QuaternaryText = quaternary.Text,
                QuaternaryPrefix = quaternary.PrefixLabel,
                QuaternaryResolution = quaternary.ResolutionLabel
            });
        }

        return new BattleTextTable_File
        {
            FileSize = bytes.Length,
            MinIndex = minIndex,
            MaxIndex = maxIndex,
            EntryLength = entryLength,
            DataBlockLength = dataBlockLength,
            Entries = entries,
            Taxonomy = taxonomy
        };
    }

    static BattleStringRead DecodeBattleString(byte[] stringBytes, ushort rawOffset, Dictionary<byte, char> decoder)
    {
        if (rawOffset >= stringBytes.Length)
        {
            return new BattleStringRead
            {
                Text = string.Empty,
                PrefixLabel = "out-of-range",
                ResolutionLabel = $"raw {rawOffset:X4}h points past the string block",
                IsOutOfRange = true,
                HadLeadingNullPadding = false,
                HadLeadPair = false,
                HadRecognizedPrefix = false,
                HasVisibleLeadingControl = false
            };
        }

        int offset = rawOffset;
        int leadingNullCount = 0;
        while (offset < stringBytes.Length && stringBytes[offset] == 0)
        {
            offset++;
            leadingNullCount++;
        }

        bool hadLeadPair = false;
        string leadPairLabel = string.Empty;
        if (offset + 1 < stringBytes.Length && stringBytes[offset] != 0 && stringBytes[offset + 1] == 0)
        {
            hadLeadPair = true;
            leadPairLabel = $"{stringBytes[offset]:X2} 00";
            offset += 2;

            while (offset < stringBytes.Length && stringBytes[offset] == 0)
            {
                offset++;
                leadingNullCount++;
            }
        }

        string prefixLabel = string.Empty;
        bool hadRecognizedPrefix = false;
        if (offset + 1 < stringBytes.Length && PrefixControls.Contains(stringBytes[offset]))
        {
            prefixLabel = $"{stringBytes[offset]:X2} {stringBytes[offset + 1]:X2}";
            hadRecognizedPrefix = true;
            offset += 2;
        }

        string leadingControlLabel = string.Empty;
        if (offset < stringBytes.Length
            && FfxEncoding.ControlDecoder.TryGetValue(stringBytes[offset], out string? controlLabel)
            && !PrefixControls.Contains(stringBytes[offset]))
        {
            leadingControlLabel = controlLabel;
        }

        if (offset >= stringBytes.Length)
        {
            return new BattleStringRead
            {
                Text = string.Empty,
                PrefixLabel = prefixLabel,
                ResolutionLabel = BuildResolutionSummary(rawOffset, offset, leadingNullCount, hadLeadPair, leadPairLabel, prefixLabel, leadingControlLabel, "no script bytes remain after heuristics"),
                IsOutOfRange = false,
                HadLeadingNullPadding = leadingNullCount > 0,
                HadLeadPair = hadLeadPair,
                HadRecognizedPrefix = hadRecognizedPrefix,
                HasVisibleLeadingControl = !string.IsNullOrWhiteSpace(leadingControlLabel)
            };
        }

        byte[] scriptBytes = TextBinary_Util.ReadNullTerminatedScript(stringBytes, offset);
        string decoded = TextBinary_Util.DecodeScriptToString(scriptBytes, decoder, true);

        return new BattleStringRead
        {
            Text = decoded,
            PrefixLabel = prefixLabel,
            ResolutionLabel = BuildResolutionSummary(rawOffset, offset, leadingNullCount, hadLeadPair, leadPairLabel, prefixLabel, leadingControlLabel, "decoded candidate"),
            IsOutOfRange = false,
            HadLeadingNullPadding = leadingNullCount > 0,
            HadLeadPair = hadLeadPair,
            HadRecognizedPrefix = hadRecognizedPrefix,
            HasVisibleLeadingControl = !string.IsNullOrWhiteSpace(leadingControlLabel)
        };
    }

    static string BuildResolutionSummary(
        int rawOffset,
        int resolvedOffset,
        int leadingNullCount,
        bool hadLeadPair,
        string leadPairLabel,
        string prefixLabel,
        string leadingControlLabel,
        string tailNote)
    {
        List<string> parts = [$"raw {rawOffset:X4}h -> {resolvedOffset:X4}h"];

        if (leadingNullCount > 0)
        {
            parts.Add($"skipped {leadingNullCount} leading NUL byte(s)");
        }

        if (hadLeadPair)
        {
            parts.Add($"skipped lead pair {leadPairLabel}");
        }

        if (!string.IsNullOrWhiteSpace(prefixLabel))
        {
            parts.Add($"recognized prefix {prefixLabel}");
        }

        if (string.IsNullOrWhiteSpace(prefixLabel) && !string.IsNullOrWhiteSpace(leadingControlLabel))
        {
            parts.Add($"script begins with {leadingControlLabel}");
        }

        if (leadingNullCount == 0 && !hadLeadPair && string.IsNullOrWhiteSpace(prefixLabel) && string.IsNullOrWhiteSpace(leadingControlLabel))
        {
            parts.Add("direct script");
        }

        parts.Add(tailNote);
        return string.Join(" · ", parts);
    }

    internal readonly record struct BattleStringRead
    {
        public required string Text { get; init; }
        public required string PrefixLabel { get; init; }
        public required string ResolutionLabel { get; init; }
        public required bool IsOutOfRange { get; init; }
        public required bool HadLeadingNullPadding { get; init; }
        public required bool HadLeadPair { get; init; }
        public required bool HadRecognizedPrefix { get; init; }
        public required bool HasVisibleLeadingControl { get; init; }
    }
}

public sealed class BattleTextTaxonomy
{
    public int WordCount { get; private set; }
    public int DirectScriptCount { get; private set; }
    public int LeadingNullAdjustedCount { get; private set; }
    public int LeadPairAdjustedCount { get; private set; }
    public int Prefix09Count { get; private set; }
    public int Prefix12Count { get; private set; }
    public int VisibleLeadingControlCount { get; private set; }
    public int OutOfRangeCount { get; private set; }

    internal void Observe(BattleTextTable_File.BattleStringRead read)
    {
        WordCount++;

        if (read.IsOutOfRange)
        {
            OutOfRangeCount++;
            return;
        }

        if (read.HadLeadingNullPadding)
        {
            LeadingNullAdjustedCount++;
        }

        if (read.HadLeadPair)
        {
            LeadPairAdjustedCount++;
        }

        if (string.Equals(read.PrefixLabel, "09 00", StringComparison.Ordinal)
            || read.PrefixLabel.StartsWith("09 ", StringComparison.Ordinal))
        {
            Prefix09Count++;
        }
        else if (string.Equals(read.PrefixLabel, "12 00", StringComparison.Ordinal)
            || read.PrefixLabel.StartsWith("12 ", StringComparison.Ordinal))
        {
            Prefix12Count++;
        }

        if (read.HasVisibleLeadingControl)
        {
            VisibleLeadingControlCount++;
        }

        if (!read.HadLeadingNullPadding && !read.HadLeadPair && !read.HadRecognizedPrefix && !read.HasVisibleLeadingControl)
        {
            DirectScriptCount++;
        }
    }

    public string BuildSummary()
    {
        return $"{WordCount} raw word pointers · direct {DirectScriptCount} · leading-NUL adjusted {LeadingNullAdjustedCount} · lead-pair adjusted {LeadPairAdjustedCount} · prefix 09 {Prefix09Count} · prefix 12 {Prefix12Count} · visible leading controls {VisibleLeadingControlCount} · out-of-range {OutOfRangeCount}.";
    }
}

public sealed class BattleTextTable_Entry
{
    public required int Index { get; init; }
    public required ushort Word0 { get; init; }
    public required ushort Word1 { get; init; }
    public required ushort Word2 { get; init; }
    public required ushort Word3 { get; init; }
    public required string PrimaryText { get; init; }
    public required string PrimaryPrefix { get; init; }
    public required string PrimaryResolution { get; init; }
    public required string SecondaryText { get; init; }
    public required string SecondaryPrefix { get; init; }
    public required string SecondaryResolution { get; init; }
    public required string TertiaryText { get; init; }
    public required string TertiaryPrefix { get; init; }
    public required string TertiaryResolution { get; init; }
    public required string QuaternaryText { get; init; }
    public required string QuaternaryPrefix { get; init; }
    public required string QuaternaryResolution { get; init; }

    public string IndexLabel => $"Index {Index:X2}h";
    public string PreferredTitle =>
        FirstNonEmpty(PrimaryText, SecondaryText, TertiaryText, QuaternaryText, "(No decoded battle text)");
    public string PreferredSummary =>
        FirstNonEmpty(SecondaryText, TertiaryText, QuaternaryText, PrimaryText, "(No secondary line)");

    public string SearchBlob =>
        $"{Index:X2} {Word0:X4} {Word1:X4} {Word2:X4} {Word3:X4} {PrimaryText} {SecondaryText} {TertiaryText} {QuaternaryText} {PrimaryResolution} {SecondaryResolution} {TertiaryResolution} {QuaternaryResolution}";

    public string BuildRawSummary()
    {
        return BuildWordSummary("W0", Word0, PrimaryPrefix, PrimaryResolution) + Environment.NewLine +
               BuildWordSummary("W1", Word1, SecondaryPrefix, SecondaryResolution) + Environment.NewLine +
               BuildWordSummary("W2", Word2, TertiaryPrefix, TertiaryResolution) + Environment.NewLine +
               BuildWordSummary("W3", Word3, QuaternaryPrefix, QuaternaryResolution);
    }

    static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    static string BuildWordSummary(string label, ushort rawWord, string prefix, string resolution)
    {
        string prefixFragment = string.IsNullOrWhiteSpace(prefix) ? string.Empty : $" · prefix {prefix}";
        return $"{label} {rawWord:X4}h{prefixFragment}{Environment.NewLine}{resolution}";
    }
}

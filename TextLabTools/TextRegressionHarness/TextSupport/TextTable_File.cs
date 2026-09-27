namespace TextRegressionHarness.TextSupport;

public sealed partial class TextTable_File
{
    public required int FileSize { get; init; }
    public required int HeaderLength { get; init; }
    public required int EntryCount { get; init; }
    public required IReadOnlyList<TextTable_Entry> Entries { get; init; }

    public static TextTable_File Read(byte[] bytes, Dictionary<byte, char> decoder)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(decoder);

        if (bytes.Length <= 1)
        {
            return new TextTable_File
            {
                FileSize = bytes.Length,
                HeaderLength = 0,
                EntryCount = 0,
                Entries = Array.Empty<TextTable_Entry>()
            };
        }

        int first = TextBinary_Util.ReadUInt16(bytes, 0x00);
        if (first <= 0 || first % 0x08 != 0)
        {
            throw new InvalidDataException($"Field-string table has an invalid header length: {first:X}h.");
        }

        int count = first / 0x08;
        int headerLength = count * 0x08;
        if (headerLength > bytes.Length)
        {
            throw new InvalidDataException("Field-string table header runs past EOF.");
        }

        List<TextTable_Entry> entries = new(count);

        for (int i = 0; i < count; i++)
        {
            int baseOffset = i * 0x08;
            byte[] rawEntryBytes = new byte[0x08];
            Array.Copy(bytes, baseOffset, rawEntryBytes, 0, rawEntryBytes.Length);

            int regularHeader = TextBinary_Util.ReadInt32(bytes, baseOffset);
            int simplifiedHeader = TextBinary_Util.ReadInt32(bytes, baseOffset + 0x04);

            ushort regularOffset = (ushort)(regularHeader & 0xFFFF);
            byte regularFlags = (byte)((regularHeader >> 16) & 0xFF);
            byte regularChoices = (byte)((regularHeader >> 24) & 0xFF);

            ushort simplifiedOffset = (ushort)(simplifiedHeader & 0xFFFF);
            byte simplifiedFlags = (byte)((simplifiedHeader >> 16) & 0xFF);
            byte simplifiedChoices = (byte)((simplifiedHeader >> 24) & 0xFF);

            byte[] regularBytes = ReadScriptAt(bytes, regularOffset, headerLength, $"field-string {i:X2}h regular");
            byte[] simplifiedBytes = regularOffset == simplifiedOffset
                ? regularBytes
                : ReadScriptAt(bytes, simplifiedOffset, headerLength, $"field-string {i:X2}h simplified");

            entries.Add(new TextTable_Entry
            {
                Index = i,
                RawEntryBytes = rawEntryBytes,
                RegularOffset = regularOffset,
                RegularFlags = regularFlags,
                RegularChoices = regularChoices,
                SimplifiedOffset = simplifiedOffset,
                SimplifiedFlags = simplifiedFlags,
                SimplifiedChoices = simplifiedChoices,
                RegularScriptBytes = regularBytes,
                SimplifiedScriptBytes = simplifiedBytes,
                RegularText = TextBinary_Util.DecodeScriptToString(regularBytes, decoder, true),
                SimplifiedText = TextBinary_Util.DecodeScriptToString(simplifiedBytes, decoder, true)
            });
        }

        return new TextTable_File
        {
            FileSize = bytes.Length,
            HeaderLength = headerLength,
            EntryCount = entries.Count,
            Entries = entries
        };
    }

    public byte[] Write(Dictionary<byte, char> decoder)
    {
        ArgumentNullException.ThrowIfNull(decoder);

        if (HeaderLength != EntryCount * 0x08)
        {
            throw new InvalidDataException("Text table header length no longer matches the proven 8-byte entry layout.");
        }

        byte[] output = new byte[HeaderLength];
        Dictionary<string, ushort> stringPool = new(StringComparer.Ordinal);
        List<byte> stringBytes = [];

        for (int i = 0; i < Entries.Count; i++)
        {
            TextTable_Entry entry = Entries[i];
            if (entry.RawEntryBytes.Length != 0x08)
            {
                throw new InvalidDataException($"Text table entry {entry.IndexLabel} no longer matches the proven 8-byte layout.");
            }

            ushort regularOffset = AppendInlineString(
                HeaderLength,
                stringBytes,
                stringPool,
                TextBinary_Util.ResolveTextBytes(entry.RegularText, entry.RegularScriptBytes, decoder));
            ushort simplifiedOffset = AppendInlineString(
                HeaderLength,
                stringBytes,
                stringPool,
                TextBinary_Util.ResolveTextBytes(entry.SimplifiedText, entry.SimplifiedScriptBytes, decoder));

            byte[] rawEntryBytes = (byte[])entry.RawEntryBytes.Clone();
            TextBinary_Util.WriteUInt16(rawEntryBytes, 0x00, regularOffset);
            TextBinary_Util.WriteUInt16(rawEntryBytes, 0x04, simplifiedOffset);
            Array.Copy(rawEntryBytes, 0, output, i * 0x08, rawEntryBytes.Length);
        }

        using MemoryStream stream = new();
        stream.Write(output, 0, output.Length);
        stream.Write(stringBytes.ToArray(), 0, stringBytes.Count);
        return stream.ToArray();
    }

    static byte[] ReadScriptAt(byte[] bytes, int offset, int headerLength, string label)
    {
        if (offset < headerLength || offset >= bytes.Length)
        {
            throw new InvalidDataException($"{label} offset {offset:X4}h is outside the string area.");
        }

        if (TextBinary_Util.FindNullTerminator(bytes, offset) < 0)
        {
            throw new InvalidDataException($"{label} is missing a terminating NULL byte.");
        }

        return TextBinary_Util.ReadNullTerminatedScript(bytes, offset);
    }

    static ushort AppendInlineString(int headerLength, List<byte> stringBytes, Dictionary<string, ushort> stringPool, byte[] scriptBytes)
    {
        string key = Convert.ToHexString(scriptBytes);
        if (stringPool.TryGetValue(key, out ushort existingOffset))
        {
            return existingOffset;
        }

        int absoluteOffset = headerLength + stringBytes.Count;
        if (absoluteOffset > ushort.MaxValue)
        {
            throw new InvalidDataException("Text table string pool exceeded the 16-bit offset range.");
        }

        ushort offset = checked((ushort)absoluteOffset);
        stringBytes.AddRange(scriptBytes);
        stringBytes.Add(0);
        stringPool[key] = offset;
        return offset;
    }
}

public sealed class TextTable_Entry
{
    public required int Index { get; init; }
    public required byte[] RawEntryBytes { get; init; }
    public required ushort RegularOffset { get; init; }
    public required byte RegularFlags { get; init; }
    public required byte RegularChoices { get; init; }
    public required ushort SimplifiedOffset { get; init; }
    public required byte SimplifiedFlags { get; init; }
    public required byte SimplifiedChoices { get; init; }
    public required byte[] RegularScriptBytes { get; init; }
    public required byte[] SimplifiedScriptBytes { get; init; }
    public required string RegularText { get; set; }
    public required string SimplifiedText { get; set; }

    public string IndexLabel => $"String {Index:X2}h";
    public bool HasDistinctSimplified => !string.Equals(RegularText, SimplifiedText, StringComparison.Ordinal);
    public string Preview => string.IsNullOrWhiteSpace(RegularText) ? "(Empty)" : RegularText;
    public string FlagsSummary => $"R Ofst {RegularOffset:X4}h · Flags {RegularFlags:X2}h · Choices {RegularChoices:X2}h";
    public string SimplifiedSummary => HasDistinctSimplified
        ? $"S Ofst {SimplifiedOffset:X4}h · Flags {SimplifiedFlags:X2}h · Choices {SimplifiedChoices:X2}h"
        : "Simplified string is shared with regular string.";
    public string SearchBlob => $"{Index:X2} {RegularOffset:X4} {SimplifiedOffset:X4} {RegularText} {SimplifiedText}";
}

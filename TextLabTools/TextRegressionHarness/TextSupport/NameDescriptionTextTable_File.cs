namespace TextRegressionHarness.TextSupport;

public sealed partial class NameDescriptionTextTable_File
{
    public required int FileSize { get; init; }
    public required int MinIndex { get; init; }
    public required int MaxIndex { get; init; }
    public required int EntryLength { get; init; }
    public required int DataBlockLength { get; init; }
    public required byte[] HeaderBytes { get; init; }
    public required byte[] DataBlockPaddingBytes { get; init; }
    public required IReadOnlyList<NameDescriptionTextTable_Entry> Entries { get; init; }

    public int EntryCount => Entries.Count;

    public static NameDescriptionTextTable_File Read(byte[] bytes, Dictionary<byte, char> decoder)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(decoder);

        if (bytes.Length < 0x14)
        {
            throw new InvalidDataException("Name/description text table is too small.");
        }

        int minIndex = TextBinary_Util.ReadUInt16(bytes, 0x08);
        int maxIndex = TextBinary_Util.ReadUInt16(bytes, 0x0A);
        int entryLength = TextBinary_Util.ReadUInt16(bytes, 0x0C);
        int dataBlockLength = TextBinary_Util.ReadUInt16(bytes, 0x0E);

        if (entryLength != 0x10)
        {
            throw new InvalidDataException($"Name/description text table expected 16-byte entries, got {entryLength:X}h.");
        }

        if (dataBlockLength <= 0 || 0x14 + dataBlockLength > bytes.Length)
        {
            throw new InvalidDataException("Name/description text table has an invalid data block length.");
        }

        if (maxIndex < minIndex)
        {
            throw new InvalidDataException("Name/description text table has an invalid index range.");
        }

        int entryCount = (maxIndex - minIndex) + 1;
        if (entryCount <= 0)
        {
            throw new InvalidDataException("Name/description text table has no entries.");
        }

        if (entryCount * entryLength > dataBlockLength)
        {
            throw new InvalidDataException("Name/description text table data block is shorter than its declared entries.");
        }

        int entryTableLength = entryCount * entryLength;
        int paddingLength = dataBlockLength - entryTableLength;
        if (paddingLength < 0)
        {
            throw new InvalidDataException("Name/description text table declared a shorter data block than its entry table.");
        }

        byte[] headerBytes = new byte[0x14];
        Array.Copy(bytes, 0, headerBytes, 0, headerBytes.Length);

        byte[] dataBlockPaddingBytes = new byte[paddingLength];
        if (paddingLength > 0)
        {
            Array.Copy(bytes, 0x14 + entryTableLength, dataBlockPaddingBytes, 0, paddingLength);
        }

        byte[] stringBytes = new byte[bytes.Length - (0x14 + dataBlockLength)];
        Array.Copy(bytes, 0x14 + dataBlockLength, stringBytes, 0, stringBytes.Length);

        List<NameDescriptionTextTable_Entry> entries = new(entryCount);
        for (int i = 0; i < entryCount; i++)
        {
            int entryOffset = 0x14 + (i * entryLength);
            byte[] rawEntryBytes = new byte[entryLength];
            Array.Copy(bytes, entryOffset, rawEntryBytes, 0, entryLength);

            ushort nameOffset = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x00);
            ushort nameKey = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x02);
            ushort simplifiedNameOffset = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x04);
            ushort simplifiedNameKey = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x06);
            ushort descriptionOffset = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x08);
            ushort descriptionKey = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x0A);
            ushort simplifiedDescriptionOffset = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x0C);
            ushort simplifiedDescriptionKey = TextBinary_Util.ReadUInt16(bytes, entryOffset + 0x0E);

            byte[] nameScriptBytes = ReadScriptAt(stringBytes, nameOffset);
            byte[] simplifiedNameScriptBytes = ReadScriptAt(stringBytes, simplifiedNameOffset);
            byte[] descriptionScriptBytes = ReadScriptAt(stringBytes, descriptionOffset);
            byte[] simplifiedDescriptionScriptBytes = ReadScriptAt(stringBytes, simplifiedDescriptionOffset);

            entries.Add(new NameDescriptionTextTable_Entry
            {
                Index = minIndex + i,
                RawEntryBytes = rawEntryBytes,
                NameOffset = nameOffset,
                NameKey = nameKey,
                SimplifiedNameOffset = simplifiedNameOffset,
                SimplifiedNameKey = simplifiedNameKey,
                DescriptionOffset = descriptionOffset,
                DescriptionKey = descriptionKey,
                SimplifiedDescriptionOffset = simplifiedDescriptionOffset,
                SimplifiedDescriptionKey = simplifiedDescriptionKey,
                NameScriptBytes = nameScriptBytes,
                SimplifiedNameScriptBytes = simplifiedNameScriptBytes,
                DescriptionScriptBytes = descriptionScriptBytes,
                SimplifiedDescriptionScriptBytes = simplifiedDescriptionScriptBytes,
                NameText = TextBinary_Util.DecodeScriptToString(nameScriptBytes, decoder, true),
                SimplifiedNameText = TextBinary_Util.DecodeScriptToString(simplifiedNameScriptBytes, decoder, true),
                DescriptionText = TextBinary_Util.DecodeScriptToString(descriptionScriptBytes, decoder, true),
                SimplifiedDescriptionText = TextBinary_Util.DecodeScriptToString(simplifiedDescriptionScriptBytes, decoder, true)
            });
        }

        return new NameDescriptionTextTable_File
        {
            FileSize = bytes.Length,
            MinIndex = minIndex,
            MaxIndex = maxIndex,
            EntryLength = entryLength,
            DataBlockLength = dataBlockLength,
            HeaderBytes = headerBytes,
            DataBlockPaddingBytes = dataBlockPaddingBytes,
            Entries = entries
        };
    }

    public byte[] Write(Dictionary<byte, char> decoder)
    {
        ArgumentNullException.ThrowIfNull(decoder);

        if (HeaderBytes.Length != 0x14)
        {
            throw new InvalidDataException("Name/description text table header proof is incomplete.");
        }

        if (EntryLength < 0x10)
        {
            throw new InvalidDataException("Name/description text table entry length is smaller than the proven 0x10-byte header.");
        }

        int entryTableLength = EntryCount * EntryLength;
        int dataBlockLength = entryTableLength + DataBlockPaddingBytes.Length;
        if (dataBlockLength > ushort.MaxValue)
        {
            throw new InvalidDataException("Name/description text table data block exceeded 64KB.");
        }

        byte[] headerBytes = (byte[])HeaderBytes.Clone();
        TextBinary_Util.WriteUInt16(headerBytes, 0x08, checked((ushort)MinIndex));
        TextBinary_Util.WriteUInt16(headerBytes, 0x0A, checked((ushort)MaxIndex));
        TextBinary_Util.WriteUInt16(headerBytes, 0x0C, checked((ushort)EntryLength));
        TextBinary_Util.WriteUInt16(headerBytes, 0x0E, checked((ushort)dataBlockLength));

        using MemoryStream stream = new();
        stream.Write(headerBytes, 0, headerBytes.Length);

        List<byte> stringBytes = [0];
        Dictionary<string, ushort> stringPool = new(StringComparer.Ordinal);

        foreach (NameDescriptionTextTable_Entry entry in Entries)
        {
            if (entry.RawEntryBytes.Length != EntryLength)
            {
                throw new InvalidDataException($"Name/description entry {entry.IndexLabel} no longer matches the proven entry length.");
            }

            byte[] rawEntryBytes = (byte[])entry.RawEntryBytes.Clone();

            ushort nameOffset = AppendLookupString(
                stringBytes,
                stringPool,
                TextBinary_Util.ResolveTextBytes(entry.NameText, entry.NameScriptBytes, decoder));
            ushort simplifiedNameOffset = AppendLookupString(
                stringBytes,
                stringPool,
                TextBinary_Util.ResolveTextBytes(entry.SimplifiedNameText, entry.SimplifiedNameScriptBytes, decoder));
            ushort descriptionOffset = AppendLookupString(
                stringBytes,
                stringPool,
                TextBinary_Util.ResolveTextBytes(entry.DescriptionText, entry.DescriptionScriptBytes, decoder));
            ushort simplifiedDescriptionOffset = AppendLookupString(
                stringBytes,
                stringPool,
                TextBinary_Util.ResolveTextBytes(entry.SimplifiedDescriptionText, entry.SimplifiedDescriptionScriptBytes, decoder));

            TextBinary_Util.WriteUInt16(rawEntryBytes, 0x00, nameOffset);
            TextBinary_Util.WriteUInt16(rawEntryBytes, 0x04, simplifiedNameOffset);
            TextBinary_Util.WriteUInt16(rawEntryBytes, 0x08, descriptionOffset);
            TextBinary_Util.WriteUInt16(rawEntryBytes, 0x0C, simplifiedDescriptionOffset);

            stream.Write(rawEntryBytes, 0, rawEntryBytes.Length);
        }

        if (DataBlockPaddingBytes.Length > 0)
        {
            stream.Write(DataBlockPaddingBytes, 0, DataBlockPaddingBytes.Length);
        }

        stream.Write(stringBytes.ToArray(), 0, stringBytes.Count);
        return stream.ToArray();
    }

    static byte[] ReadScriptAt(byte[] stringBytes, int offset)
    {
        if (offset <= 0 || offset >= stringBytes.Length)
        {
            return Array.Empty<byte>();
        }

        return TextBinary_Util.ReadNullTerminatedScript(stringBytes, offset);
    }

    static ushort AppendLookupString(List<byte> stringBytes, Dictionary<string, ushort> stringPool, byte[] scriptBytes)
    {
        if (scriptBytes.Length == 0)
        {
            return 0;
        }

        string key = Convert.ToHexString(scriptBytes);
        if (stringPool.TryGetValue(key, out ushort existingOffset))
        {
            return existingOffset;
        }

        if (stringBytes.Count > ushort.MaxValue)
        {
            throw new InvalidDataException("Name/description text table string pool exceeded 64KB.");
        }

        ushort offset = checked((ushort)stringBytes.Count);
        stringBytes.AddRange(scriptBytes);
        stringBytes.Add(0);
        stringPool[key] = offset;
        return offset;
    }
}

public sealed class NameDescriptionTextTable_Entry
{
    public required int Index { get; init; }
    public required byte[] RawEntryBytes { get; init; }
    public required ushort NameOffset { get; init; }
    public required ushort NameKey { get; init; }
    public required ushort SimplifiedNameOffset { get; init; }
    public required ushort SimplifiedNameKey { get; init; }
    public required ushort DescriptionOffset { get; init; }
    public required ushort DescriptionKey { get; init; }
    public required ushort SimplifiedDescriptionOffset { get; init; }
    public required ushort SimplifiedDescriptionKey { get; init; }
    public required byte[] NameScriptBytes { get; init; }
    public required byte[] SimplifiedNameScriptBytes { get; init; }
    public required byte[] DescriptionScriptBytes { get; init; }
    public required byte[] SimplifiedDescriptionScriptBytes { get; init; }
    public required string NameText { get; set; }
    public required string SimplifiedNameText { get; set; }
    public required string DescriptionText { get; set; }
    public required string SimplifiedDescriptionText { get; set; }

    public string IndexLabel => $"Index {Index:X2}h";
    public bool HasDistinctSimplifiedName => !string.Equals(NameText, SimplifiedNameText, StringComparison.Ordinal);
    public bool HasDistinctSimplifiedDescription => !string.Equals(DescriptionText, SimplifiedDescriptionText, StringComparison.Ordinal);
    public string PreviewTitle => string.IsNullOrWhiteSpace(NameText) ? "(Unnamed entry)" : NameText;
    public string PreviewSummary => string.IsNullOrWhiteSpace(DescriptionText) ? "(No description)" : DescriptionText;

    public string SearchBlob =>
        $"{Index:X2} {NameOffset:X4} {DescriptionOffset:X4} {NameText} {SimplifiedNameText} {DescriptionText} {SimplifiedDescriptionText}";

    public string BuildHeadersSummary()
    {
        return $"Name {NameOffset:X4}h key {NameKey:X4}h{Environment.NewLine}" +
               $"Simplified Name {SimplifiedNameOffset:X4}h key {SimplifiedNameKey:X4}h{Environment.NewLine}" +
               $"Description {DescriptionOffset:X4}h key {DescriptionKey:X4}h{Environment.NewLine}" +
               $"Simplified Description {SimplifiedDescriptionOffset:X4}h key {SimplifiedDescriptionKey:X4}h";
    }

    public string BuildVariantSummary()
    {
        string simplifiedName = HasDistinctSimplifiedName ? SimplifiedNameText : "(Shared with regular name)";
        string simplifiedDescription = HasDistinctSimplifiedDescription ? SimplifiedDescriptionText : "(Shared with regular description)";

        return $"Simplified Name:{Environment.NewLine}{(string.IsNullOrWhiteSpace(simplifiedName) ? "(Empty)" : simplifiedName)}{Environment.NewLine}{Environment.NewLine}" +
               $"Simplified Description:{Environment.NewLine}{(string.IsNullOrWhiteSpace(simplifiedDescription) ? "(Empty)" : simplifiedDescription)}{Environment.NewLine}{Environment.NewLine}" +
               $"Headers:{Environment.NewLine}{BuildHeadersSummary()}";
    }
}

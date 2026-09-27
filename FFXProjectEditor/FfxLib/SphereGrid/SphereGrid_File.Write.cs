using FFXProjectEditor.Utils.Encoding;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.SphereGrid
{
    public static partial class SphereGrid_File
    {
        public static void WriteSphereTypes(SphereGridSphereTypeTable table, IReadOnlyList<SphereGridSphereTypeWriteModel> entries)
        {
            ArgumentNullException.ThrowIfNull(table);
            ArgumentNullException.ThrowIfNull(entries);

            Dictionary<int, SphereGridSphereTypeWriteModel> entryMap = entries.ToDictionary(entry => entry.Index);

            if (table.JpOriginalBytes != null)
            {
                byte[] jpBytes = BuildSphereTypesLocaleFile(table, entryMap, table.JpOriginalBytes, SphereGridLocale.Japanese);
                File.WriteAllBytes(table.JpPath, jpBytes);
            }

            if (!string.IsNullOrWhiteSpace(table.UsPath) && table.UsOriginalBytes != null)
            {
                byte[] usBytes = BuildSphereTypesLocaleFile(table, entryMap, table.UsOriginalBytes, SphereGridLocale.English);
                File.WriteAllBytes(table.UsPath, usBytes);
            }
        }

        public static void WriteNodeTypes(SphereGridNodeTypeTable table, IReadOnlyList<SphereGridNodeTypeWriteModel> entries)
        {
            ArgumentNullException.ThrowIfNull(table);
            ArgumentNullException.ThrowIfNull(entries);

            Dictionary<int, SphereGridNodeTypeWriteModel> entryMap = entries.ToDictionary(entry => entry.Index);

            if (table.JpOriginalBytes != null)
            {
                byte[] jpBytes = BuildNodeTypesLocaleFile(table, entryMap, table.JpOriginalBytes, SphereGridLocale.Japanese);
                File.WriteAllBytes(table.JpPath, jpBytes);
            }

            if (!string.IsNullOrWhiteSpace(table.UsPath) && table.UsOriginalBytes != null)
            {
                byte[] usBytes = BuildNodeTypesLocaleFile(table, entryMap, table.UsOriginalBytes, SphereGridLocale.English);
                File.WriteAllBytes(table.UsPath, usBytes);
            }
        }

        /// <summary>
        /// Appends one node-type row at <c>MaxIndex + 1</c> when no entry already owns <paramref name="learnedMove"/>.
        /// Preserves existing data/string pools verbatim and only extends them. Returns the new content index.
        /// </summary>
        public static int AppendNodeTypeForLearnedMove(
            SphereGridNodeTypeTable table,
            ushort learnedMove,
            string jpName,
            string usName,
            string? jpDescription = null,
            string? usDescription = null,
            int? templateIndex = null)
        {
            ArgumentNullException.ThrowIfNull(table);
            jpName ??= string.Empty;
            usName ??= string.Empty;

            SphereGridNodeTypeEntry? existing = table.Entries.FirstOrDefault(entry => entry.LearnedMove == learnedMove);
            if (existing != null)
                return existing.Index;

            SphereGridNodeTypeEntry template = ResolveNodeTypeTemplate(table, templateIndex);
            int newIndex = table.Header.MaxIndex + 1;
            if (newIndex >= SphereGridLayoutBuilder.EmptyContent)
            {
                throw new InvalidOperationException(
                    $"panel.bin cannot grow past index {SphereGridLayoutBuilder.EmptyContent - 1:X2}h (0xFF is reserved for empty grid nodes).");
            }

            SphereGridNodeTypeAppendText jpTexts = ResolveAppendTexts(template, jpName, jpDescription, SphereGridLocale.Japanese);
            SphereGridNodeTypeAppendText usTexts = ResolveAppendTexts(template, usName, usDescription, SphereGridLocale.English);

            if (table.JpOriginalBytes == null)
                throw new InvalidOperationException("panel.bin JP payload is required to append node types.");

            byte[] jpBytes = AppendNodeTypeLocaleBytes(
                table.JpOriginalBytes,
                template.Index,
                newIndex,
                learnedMove,
                jpTexts,
                SphereGridLocale.Japanese);
            File.WriteAllBytes(table.JpPath, jpBytes);

            if (!string.IsNullOrWhiteSpace(table.UsPath) && table.UsOriginalBytes != null)
            {
                byte[] usBytes = AppendNodeTypeLocaleBytes(
                    table.UsOriginalBytes,
                    template.Index,
                    newIndex,
                    learnedMove,
                    usTexts,
                    SphereGridLocale.English);
                File.WriteAllBytes(table.UsPath, usBytes);
            }

            return newIndex;
        }

        static SphereGridNodeTypeAppendText ResolveAppendTexts(
            SphereGridNodeTypeEntry template,
            string name,
            string? descriptionOverride,
            SphereGridLocale locale)
        {
            name ??= string.Empty;

            if (locale == SphereGridLocale.English)
            {
                string desc = string.IsNullOrWhiteSpace(descriptionOverride) ? $"Learn {name}." : descriptionOverride!;
                return new SphereGridNodeTypeAppendText(name, name, desc, desc);
            }

            // JP panel.bin uses a JP script table — never default to ASCII "Learn …".
            string jpName = PickEncodableSphereGridText(
                locale,
                name,
                template.Name.JpText,
                template.SimplifiedName.JpText);

            string jpSimplifiedName = PickEncodableSphereGridText(
                locale,
                name,
                template.SimplifiedName.JpText,
                jpName);

            string jpDescription = !string.IsNullOrWhiteSpace(descriptionOverride)
                ? PickEncodableSphereGridText(locale, descriptionOverride, template.Description.JpText)
                : PickEncodableSphereGridText(
                    locale,
                    template.Description.JpText,
                    template.SimplifiedDescription.JpText);

            string jpSimplifiedDescription = PickEncodableSphereGridText(
                locale,
                jpDescription,
                template.SimplifiedDescription.JpText,
                jpDescription);

            return new SphereGridNodeTypeAppendText(jpName, jpSimplifiedName, jpDescription, jpSimplifiedDescription);
        }

        static string PickEncodableSphereGridText(SphereGridLocale locale, params string?[] candidates)
        {
            foreach (string? candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                if (CanEncodeSphereGridText(candidate, locale))
                    return candidate;
            }

            return string.Empty;
        }

        static bool CanEncodeSphereGridText(string text, SphereGridLocale locale)
        {
            if (string.IsNullOrEmpty(text))
                return true;

            try
            {
                EncodeScriptString(text, locale);
                return true;
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }

        static SphereGridNodeTypeEntry ResolveNodeTypeTemplate(SphereGridNodeTypeTable table, int? templateIndex)
        {
            if (templateIndex is int forced)
            {
                SphereGridNodeTypeEntry? forcedEntry = table.Entries.FirstOrDefault(entry => entry.Index == forced);
                if (forcedEntry != null)
                    return forcedEntry;
            }

            return table.Entries.FirstOrDefault(entry =>
                       entry.LearnedMove >= 0x3000 && entry.LearnedMove < 0x4000 && entry.IncreaseAmount == 0)
                   ?? table.Entries.FirstOrDefault(entry => entry.LearnedMove != 0)
                   ?? throw new InvalidOperationException("panel.bin has no skill node template to clone.");
        }

        static byte[] AppendNodeTypeLocaleBytes(
            byte[] originalBytes,
            int templateIndex,
            int newIndex,
            ushort learnedMove,
            SphereGridNodeTypeAppendText texts,
            SphereGridLocale locale)
        {
            LocalizedDataPayload payload = TryReadLocalizedDataPayload(null, originalBytes)
                ?? throw new InvalidDataException("panel.bin payload is missing.");

            byte[] templateRaw = payload.GetEntryBytes(templateIndex)
                ?? throw new InvalidOperationException($"panel.bin template node type {templateIndex:X2}h is missing.");

            ushort entryLength = payload.Header.EntryLength;
            if (entryLength < 0x18)
                throw new InvalidDataException($"panel.bin entry length 0x{entryLength:X} is too small.");

            int newRelativeIndex = newIndex - payload.Header.MinIndex;
            if (newRelativeIndex < 0)
                throw new InvalidOperationException($"panel.bin new index {newIndex:X2}h is below MinIndex {payload.Header.MinIndex:X2}h.");

            int newEntryCount = payload.Header.EntryCount + 1;
            byte[] dataBytes = new byte[newEntryCount * entryLength];
            Array.Copy(payload.DataBytes, dataBytes, payload.DataBytes.Length);

            byte[] raw = templateRaw.ToArray();
            List<byte> stringBytes = payload.StringBytes.ToList();
            Dictionary<string, ushort> stringPool = new(StringComparer.Ordinal);

            ushort nameOffset = AddEncodedString(stringPool, stringBytes, texts.Name, locale);
            ushort simplifiedNameOffset = AddEncodedString(stringPool, stringBytes, texts.SimplifiedName, locale);
            ushort descriptionOffset = AddEncodedString(stringPool, stringBytes, texts.Description, locale);
            ushort simplifiedDescriptionOffset = AddEncodedString(stringPool, stringBytes, texts.SimplifiedDescription, locale);

            WriteUInt16(raw, 0x00, nameOffset);
            WriteUInt16(raw, 0x02, ReadUInt16(templateRaw, 0x02));
            WriteUInt16(raw, 0x04, simplifiedNameOffset);
            WriteUInt16(raw, 0x06, ReadUInt16(templateRaw, 0x06));
            WriteUInt16(raw, 0x08, descriptionOffset);
            WriteUInt16(raw, 0x0A, ReadUInt16(templateRaw, 0x0A));
            WriteUInt16(raw, 0x0C, simplifiedDescriptionOffset);
            WriteUInt16(raw, 0x0E, ReadUInt16(templateRaw, 0x0E));
            WriteUInt16(raw, 0x10, SphereGridNodeSphereRequirement.AbilityEffectBitfield);
            WriteUInt16(raw, 0x12, learnedMove);
            WriteUInt16(raw, 0x14, 0);
            WriteUInt16(raw, 0x16, SphereGridNodeSphereRequirement.AbilityAppearanceType);

            Array.Copy(raw, 0, dataBytes, newRelativeIndex * entryLength, entryLength);

            byte[] headerBytes = originalBytes[..0x14].ToArray();
            WriteUInt16(headerBytes, 0x0A, (ushort)newIndex);
            WriteUInt16(headerBytes, 0x0E, (ushort)dataBytes.Length);
            return headerBytes.Concat(dataBytes).Concat(stringBytes).ToArray();
        }

        public static void WriteLayoutNodeContents(SphereGridLayoutFile layout, IReadOnlyList<SphereGridLayoutNodeWriteModel> nodes)
        {
            ArgumentNullException.ThrowIfNull(layout);
            ArgumentNullException.ThrowIfNull(nodes);

            byte[] contentsBytes = layout.RawContentsBytes.ToArray();
            int payloadOffset = 0x08;
            int requiredLength = payloadOffset + layout.NodeCount;
            if (contentsBytes.Length < requiredLength)
            {
                Array.Resize(ref contentsBytes, requiredLength);
            }

            foreach (SphereGridLayoutNodeWriteModel node in nodes)
            {
                if (node.Index < 0 || node.Index >= layout.NodeCount)
                    continue;

                contentsBytes[payloadOffset + node.Index] = unchecked((byte)node.ContentIndex);
            }

            File.WriteAllBytes(layout.ContentsPath, contentsBytes);
        }

        // ----------------------------------------------------------------------------------------
        // RT0 byte-faithful TOPOLOGY/LAYOUT writer (dat01/dat02/dat03 = Original/Standard/Expert).
        //
        // Unlike WriteLayoutNodeContents (which only authors the per-node content byte that lives in
        // the SEPARATE contents file dat09/10/11), WriteLayout re-emits the ENTIRE layout struct from
        // the in-memory model: header (0x10) + cluster table (0x10/entry) + node table (0x0C/entry) +
        // link table (0x08/entry). This is what unlocks "build a sphere grid from scratch": construct
        // a SphereGridLayoutFile (clusters/nodes/links) and serialize it.
        //
        // Byte-faithfulness is proven by SphereGridLayoutRt0 (Read -> WriteLayout == original) on all
        // three grids. The whole layout file is consumed by captured fields with ZERO trailing bytes
        // (verified: 0x10 + cc*0x10 + nc*0x0C + lc*0x08 == filesize for Original/Standard/Expert), so
        // no original-byte buffer is needed: every emitted byte comes from a model field.
        //
        // Field/offset map (all little-endian u16; signed PosX/PosY are written via unchecked cast):
        //   Header  @0x00: Unknown1, ClusterCount, NodeCount, LinkCount, Unknown5, Unknown6, Unknown7, Unknown8
        //   Cluster @0x00: PosX, PosY, Unused3, RadiusType, Unused5, Unused6, Unused7, Unused8   (stride 0x10)
        //   Node    @0x00: PosX, PosY, Unused3, RedundantContent, Cluster, Unknown6              (stride 0x0C)
        //   Link    @0x00: Node1, Node2, AnchorNode, Unused                                      (stride 0x08)
        //
        // NOTE: ClusterCount/NodeCount/LinkCount in the header are emitted from layout.ClusterCount/
        // NodeCount/LinkCount (the values captured at Read). For a from-scratch build the caller is
        // responsible for keeping those header counts consistent with the actual list lengths; this
        // method does NOT silently recompute them, so a no-edit Read->WriteLayout is byte-identical
        // even on the (theoretical) chance a file's header count disagreed with its table length.
        public static byte[] WriteLayout(SphereGridLayoutFile layout)
        {
            ArgumentNullException.ThrowIfNull(layout);

            int clusterCount = layout.ClusterCount;
            int nodeCount = layout.NodeCount;
            int linkCount = layout.LinkCount;

            int total = 0x10 + clusterCount * 0x10 + nodeCount * 0x0C + linkCount * 0x08;
            byte[] output = new byte[total];

            // Header (0x00..0x0F)
            WriteUInt16(output, 0x00, layout.Unknown1);
            WriteUInt16(output, 0x02, layout.ClusterCount);
            WriteUInt16(output, 0x04, layout.NodeCount);
            WriteUInt16(output, 0x06, layout.LinkCount);
            WriteUInt16(output, 0x08, layout.Unknown5);
            WriteUInt16(output, 0x0A, layout.Unknown6);
            WriteUInt16(output, 0x0C, layout.Unknown7);
            WriteUInt16(output, 0x0E, layout.Unknown8);

            // Cluster table
            int clusterBase = 0x10;
            for (int i = 0; i < clusterCount; i++)
            {
                SphereGridClusterEntry c = layout.Clusters[i];
                int o = clusterBase + i * 0x10;
                WriteUInt16(output, o + 0x00, unchecked((ushort)c.PosX));
                WriteUInt16(output, o + 0x02, unchecked((ushort)c.PosY));
                WriteUInt16(output, o + 0x04, c.Unused3);
                WriteUInt16(output, o + 0x06, c.RadiusType);
                WriteUInt16(output, o + 0x08, c.Unused5);
                WriteUInt16(output, o + 0x0A, c.Unused6);
                WriteUInt16(output, o + 0x0C, c.Unused7);
                WriteUInt16(output, o + 0x0E, c.Unused8);
            }

            // Node table
            int nodeBase = clusterBase + clusterCount * 0x10;
            for (int i = 0; i < nodeCount; i++)
            {
                SphereGridNodeEntry n = layout.Nodes[i];
                int o = nodeBase + i * 0x0C;
                WriteUInt16(output, o + 0x00, unchecked((ushort)n.PosX));
                WriteUInt16(output, o + 0x02, unchecked((ushort)n.PosY));
                WriteUInt16(output, o + 0x04, n.Unused3);
                WriteUInt16(output, o + 0x06, n.RedundantContent);
                WriteUInt16(output, o + 0x08, n.Cluster);
                WriteUInt16(output, o + 0x0A, n.Unknown6);
            }

            // Link table
            int linkBase = nodeBase + nodeCount * 0x0C;
            for (int i = 0; i < linkCount; i++)
            {
                SphereGridLinkEntry l = layout.Links[i];
                int o = linkBase + i * 0x08;
                WriteUInt16(output, o + 0x00, l.Node1);
                WriteUInt16(output, o + 0x02, l.Node2);
                WriteUInt16(output, o + 0x04, l.AnchorNode);
                WriteUInt16(output, o + 0x06, l.Unused);
            }

            return output;
        }

        static byte[] BuildSphereTypesLocaleFile(
            SphereGridSphereTypeTable table,
            IReadOnlyDictionary<int, SphereGridSphereTypeWriteModel> entries,
            byte[] originalBytes,
            SphereGridLocale locale)
        {
            byte[] dataBytes = new byte[table.Header.EntryCount * table.Header.EntryLength];
            byte[] headerBytes = originalBytes[..0x14].ToArray();
            Dictionary<string, ushort> stringPool = new(StringComparer.Ordinal);
            List<byte> stringBytes = new();

            foreach (SphereGridSphereTypeEntry original in table.Entries)
            {
                if (!entries.TryGetValue(original.Index, out SphereGridSphereTypeWriteModel? entry))
                    continue;

                int relativeIndex = original.Index - table.Header.MinIndex;
                int entryOffset = relativeIndex * table.Header.EntryLength;
                byte[] raw = original.RawBytes.ToArray();
                EnsureLength(ref raw, table.Header.EntryLength);

                string regularText = locale == SphereGridLocale.English ? entry.UsDescription : entry.JpDescription;
                string simplifiedText = locale == SphereGridLocale.English ? entry.UsSimplifiedDescription : entry.JpSimplifiedDescription;
                ushort regularKey = locale == SphereGridLocale.English ? original.Description.UsKey : original.Description.JpKey;
                ushort simplifiedKey = locale == SphereGridLocale.English ? original.SimplifiedDescription.UsKey : original.SimplifiedDescription.JpKey;

                ushort regularOffset = AddEncodedString(stringPool, stringBytes, regularText, locale);
                ushort simplifiedOffset = AddEncodedString(stringPool, stringBytes, simplifiedText, locale);

                WriteUInt16(raw, 0x00, regularOffset);
                WriteUInt16(raw, 0x02, regularKey);
                WriteUInt16(raw, 0x04, simplifiedOffset);
                WriteUInt16(raw, 0x06, simplifiedKey);
                WriteUInt16(raw, 0x08, entry.Behavior);
                WriteUInt16(raw, 0x0A, original.Activates);
                raw[0x0C] = entry.Range;
                raw[0x0D] = original.SpecialRole;
                WriteUInt16(raw, 0x0E, original.Reserved0x0E);

                Array.Copy(raw, 0, dataBytes, entryOffset, table.Header.EntryLength);
            }

            WriteUInt16(headerBytes, 0x0C, table.Header.EntryLength);
            WriteUInt16(headerBytes, 0x0E, (ushort)dataBytes.Length);
            return headerBytes.Concat(dataBytes).Concat(stringBytes).ToArray();
        }

        static byte[] BuildNodeTypesLocaleFile(
            SphereGridNodeTypeTable table,
            IReadOnlyDictionary<int, SphereGridNodeTypeWriteModel> entries,
            byte[] originalBytes,
            SphereGridLocale locale)
        {
            byte[] dataBytes = new byte[table.Header.EntryCount * table.Header.EntryLength];
            byte[] headerBytes = originalBytes[..0x14].ToArray();
            Dictionary<string, ushort> stringPool = new(StringComparer.Ordinal);
            List<byte> stringBytes = new();

            foreach (SphereGridNodeTypeEntry original in table.Entries)
            {
                if (!entries.TryGetValue(original.Index, out SphereGridNodeTypeWriteModel? entry))
                    continue;

                int relativeIndex = original.Index - table.Header.MinIndex;
                int entryOffset = relativeIndex * table.Header.EntryLength;
                byte[] raw = original.RawBytes.ToArray();
                EnsureLength(ref raw, table.Header.EntryLength);

                string nameText = locale == SphereGridLocale.English ? entry.UsName : entry.JpName;
                string simplifiedNameText = locale == SphereGridLocale.English ? entry.UsSimplifiedName : entry.JpSimplifiedName;
                string descriptionText = locale == SphereGridLocale.English ? entry.UsDescription : entry.JpDescription;
                string simplifiedDescriptionText = locale == SphereGridLocale.English ? entry.UsSimplifiedDescription : entry.JpSimplifiedDescription;

                ushort nameOffset = AddEncodedString(stringPool, stringBytes, nameText, locale);
                ushort simplifiedNameOffset = AddEncodedString(stringPool, stringBytes, simplifiedNameText, locale);
                ushort descriptionOffset = AddEncodedString(stringPool, stringBytes, descriptionText, locale);
                ushort simplifiedDescriptionOffset = AddEncodedString(stringPool, stringBytes, simplifiedDescriptionText, locale);

                WriteUInt16(raw, 0x00, nameOffset);
                WriteUInt16(raw, 0x02, locale == SphereGridLocale.English ? original.Name.UsKey : original.Name.JpKey);
                WriteUInt16(raw, 0x04, simplifiedNameOffset);
                WriteUInt16(raw, 0x06, locale == SphereGridLocale.English ? original.SimplifiedName.UsKey : original.SimplifiedName.JpKey);
                WriteUInt16(raw, 0x08, descriptionOffset);
                WriteUInt16(raw, 0x0A, locale == SphereGridLocale.English ? original.Description.UsKey : original.Description.JpKey);
                WriteUInt16(raw, 0x0C, simplifiedDescriptionOffset);
                WriteUInt16(raw, 0x0E, locale == SphereGridLocale.English ? original.SimplifiedDescription.UsKey : original.SimplifiedDescription.JpKey);
                WriteUInt16(raw, 0x10, entry.NodeEffectBitfield);
                WriteUInt16(raw, 0x12, entry.LearnedMove);
                WriteUInt16(raw, 0x14, entry.IncreaseAmount);
                WriteUInt16(raw, 0x16, entry.AppearanceType);

                Array.Copy(raw, 0, dataBytes, entryOffset, table.Header.EntryLength);
            }

            WriteUInt16(headerBytes, 0x0C, table.Header.EntryLength);
            WriteUInt16(headerBytes, 0x0E, (ushort)dataBytes.Length);
            return headerBytes.Concat(dataBytes).Concat(stringBytes).ToArray();
        }

        static ushort AddEncodedString(Dictionary<string, ushort> stringPool, List<byte> stringBytes, string text, SphereGridLocale locale)
        {
            text ??= string.Empty;
            string poolKey = $"{(int)locale}:{text}";
            if (stringPool.TryGetValue(poolKey, out ushort existingOffset))
                return existingOffset;

            if (stringBytes.Count > ushort.MaxValue)
                throw new InvalidDataException("Sphere-grid string table exceeded 64KB.");

            ushort offset = (ushort)stringBytes.Count;
            byte[] encoded = EncodeScriptString(text, locale);
            stringBytes.AddRange(encoded);
            stringBytes.Add(0);
            stringPool[poolKey] = offset;
            return offset;
        }

        static byte[] EncodeScriptString(string text, SphereGridLocale locale)
        {
            Dictionary<char, byte> encoder = locale == SphereGridLocale.English
                ? BuildEncoder(FfxEncoding.UsDecoder)
                : BuildEncoder(FfxEncoding.JpDecoder);

            List<byte> bytes = new();
            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            for (int i = 0; i < normalized.Length; i++)
            {
                char c = normalized[i];
                if (c == '\n')
                {
                    bytes.Add(FfxEncoding.C_NEW_LINE);
                    continue;
                }

                if (TryMatchToken(normalized, i, FfxEncoding.FormatCodes, out byte formatCode, out int formatLength))
                {
                    bytes.Add(FfxEncoding.C_FORMAT);
                    bytes.Add(formatCode);
                    i += formatLength - 1;
                    continue;
                }

                if (TryMatchToken(normalized, i, FfxEncoding.CharacterNameCodes, out byte charCode, out int charLength))
                {
                    bytes.Add(FfxEncoding.C_CHAR_NAME);
                    bytes.Add(charCode);
                    i += charLength - 1;
                    continue;
                }

                if (!encoder.TryGetValue(c, out byte encodedChar))
                    throw new InvalidDataException($"Unsupported {(locale == SphereGridLocale.English ? "US" : "JP")} sphere-grid character: '{c}'");

                bytes.Add(encodedChar);
            }

            return bytes.ToArray();
        }

        static bool TryMatchToken(string input, int offset, IReadOnlyDictionary<byte, string> tokenMap, out byte code, out int length)
        {
            foreach ((byte candidateCode, string token) in tokenMap.OrderByDescending(pair => pair.Value.Length))
            {
                if (string.IsNullOrEmpty(token) || offset + token.Length > input.Length)
                    continue;

                if (string.CompareOrdinal(input, offset, token, 0, token.Length) == 0)
                {
                    code = candidateCode;
                    length = token.Length;
                    return true;
                }
            }

            code = 0;
            length = 0;
            return false;
        }

        static Dictionary<char, byte> BuildEncoder(Dictionary<byte, char> decoder)
        {
            Dictionary<char, byte> encoder = new();
            foreach ((byte key, char value) in decoder)
            {
                encoder.TryAdd(value, key);
            }

            return encoder;
        }

        static void EnsureLength(ref byte[] bytes, int expectedLength)
        {
            if (bytes.Length >= expectedLength)
                return;

            Array.Resize(ref bytes, expectedLength);
        }

        static void WriteUInt16(byte[] bytes, int offset, ushort value)
        {
            if (offset < 0 || offset + 2 > bytes.Length)
                return;

            bytes[offset] = unchecked((byte)(value & 0xFF));
            bytes[offset + 1] = unchecked((byte)(value >> 8));
        }
    }

    public enum SphereGridLocale
    {
        Japanese,
        English
    }

    public sealed class SphereGridSphereTypeWriteModel
    {
        public required int Index { get; init; }
        public required string JpDescription { get; init; }
        public required string UsDescription { get; init; }
        public required string JpSimplifiedDescription { get; init; }
        public required string UsSimplifiedDescription { get; init; }

        /// <summary>+0x08 — <see cref="SphereBehavior"/> (None/Activator/Modifier).</summary>
        public required ushort Behavior { get; init; }

        /// <summary>+0x0C — <see cref="SphereRange"/> (None/Normal/Unlimited).</summary>
        public required byte Range { get; init; }
    }

    public sealed class SphereGridNodeTypeWriteModel
    {
        public required int Index { get; init; }
        public required string JpName { get; init; }
        public required string UsName { get; init; }
        public required string JpSimplifiedName { get; init; }
        public required string UsSimplifiedName { get; init; }
        public required string JpDescription { get; init; }
        public required string UsDescription { get; init; }
        public required string JpSimplifiedDescription { get; init; }
        public required string UsSimplifiedDescription { get; init; }
        public required ushort LearnedMove { get; init; }
        public required ushort IncreaseAmount { get; init; }
        public required ushort NodeEffectBitfield { get; init; }
        public required ushort AppearanceType { get; init; }
    }

    public sealed class SphereGridLayoutNodeWriteModel
    {
        public required int Index { get; init; }
        public required int ContentIndex { get; init; }
    }
}

using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.Utils.Encoding;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.SphereGrid
{
    public static partial class SphereGrid_File
    {
        public static SphereGridSphereTypeTable ReadSphereTypes(string jpPath, string? usPath)
        {
            byte[]? jpBytes = File.Exists(jpPath) ? File.ReadAllBytes(jpPath) : null;
            byte[]? usBytes = !string.IsNullOrWhiteSpace(usPath) && File.Exists(usPath) ? File.ReadAllBytes(usPath) : null;
            return ReadSphereTypes(jpBytes, usBytes, jpPath, usPath);
        }

        public static SphereGridSphereTypeTable ReadSphereTypes(byte[]? jpBytes, byte[]? usBytes, string jpPath, string? usPath)
        {
            LocalizedDataPayload? jp = TryReadLocalizedDataPayload(jpPath, jpBytes);
            LocalizedDataPayload? us = TryReadLocalizedDataPayload(usPath, usBytes);
            LocalizedDataPayload basePayload = jp ?? us ?? throw new FileNotFoundException("Sphere grid sphere-type table not found.");

            List<SphereGridSphereTypeEntry> entries = new();
            for (int index = basePayload.Header.MinIndex; index <= basePayload.Header.MaxIndex; index++)
            {
                byte[]? raw = jp?.GetEntryBytes(index) ?? us?.GetEntryBytes(index);
                if (raw == null || raw.Length < 0x10)
                    continue;

                byte[]? jpEntry = jp?.GetEntryBytes(index);
                byte[]? usEntry = us?.GetEntryBytes(index);

                ushort jpDescriptionOffset = ReadUInt16(jpEntry ?? raw, 0x00);
                ushort jpDescriptionKey = ReadUInt16(jpEntry ?? raw, 0x02);
                ushort jpSimplifiedOffset = ReadUInt16(jpEntry ?? raw, 0x04);
                ushort jpSimplifiedKey = ReadUInt16(jpEntry ?? raw, 0x06);

                ushort usDescriptionOffset = ReadUInt16(usEntry ?? raw, 0x00);
                ushort usDescriptionKey = ReadUInt16(usEntry ?? raw, 0x02);
                ushort usSimplifiedOffset = ReadUInt16(usEntry ?? raw, 0x04);
                ushort usSimplifiedKey = ReadUInt16(usEntry ?? raw, 0x06);

                SphereGridLocalizedText description = ReadLocalizedText(
                    jp?.StringBytes, jpDescriptionOffset, jpDescriptionKey,
                    us?.StringBytes, usDescriptionOffset, usDescriptionKey,
                    FfxEncoding.JpDecoder, FfxEncoding.UsDecoder);

                SphereGridLocalizedText simplifiedDescription = ReadLocalizedText(
                    jp?.StringBytes, jpSimplifiedOffset, jpSimplifiedKey,
                    us?.StringBytes, usSimplifiedOffset, usSimplifiedKey,
                    FfxEncoding.JpDecoder, FfxEncoding.UsDecoder);

                ushort behavior = ReadUInt16(raw, 0x08);
                ushort activates = ReadUInt16(raw, 0x0A);
                byte range = ReadByte(raw, 0x0C);
                byte specialRole = ReadByte(raw, 0x0D);
                ushort reserved0x0E = ReadUInt16(raw, 0x0E);

                entries.Add(new SphereGridSphereTypeEntry
                {
                    Index = index,
                    Description = description,
                    SimplifiedDescription = simplifiedDescription,
                    Behavior = behavior,
                    Activates = activates,
                    Range = range,
                    SpecialRole = specialRole,
                    Reserved0x0E = reserved0x0E,
                    RawBytes = raw.ToArray()
                });
            }

            return new SphereGridSphereTypeTable
            {
                JpPath = jpPath,
                UsPath = usPath,
                JpOriginalBytes = jp?.OriginalBytes,
                UsOriginalBytes = us?.OriginalBytes,
                Header = basePayload.Header,
                Entries = entries
            };
        }

        public static SphereGridNodeTypeTable ReadNodeTypes(string jpPath, string? usPath)
        {
            byte[]? jpBytes = File.Exists(jpPath) ? File.ReadAllBytes(jpPath) : null;
            byte[]? usBytes = !string.IsNullOrWhiteSpace(usPath) && File.Exists(usPath) ? File.ReadAllBytes(usPath) : null;
            return ReadNodeTypes(jpBytes, usBytes, jpPath, usPath);
        }

        public static SphereGridNodeTypeTable ReadNodeTypes(byte[]? jpBytes, byte[]? usBytes, string jpPath, string? usPath)
        {
            LocalizedDataPayload? jp = TryReadLocalizedDataPayload(jpPath, jpBytes);
            LocalizedDataPayload? us = TryReadLocalizedDataPayload(usPath, usBytes);
            LocalizedDataPayload basePayload = jp ?? us ?? throw new FileNotFoundException("Sphere grid node-type table not found.");

            List<SphereGridNodeTypeEntry> entries = new();
            for (int index = basePayload.Header.MinIndex; index <= basePayload.Header.MaxIndex; index++)
            {
                byte[]? raw = jp?.GetEntryBytes(index) ?? us?.GetEntryBytes(index);
                if (raw == null || raw.Length < 0x18)
                    continue;

                byte[]? jpEntry = jp?.GetEntryBytes(index);
                byte[]? usEntry = us?.GetEntryBytes(index);

                SphereGridLocalizedText name = ReadLocalizedText(
                    jp?.StringBytes, ReadUInt16(jpEntry ?? raw, 0x00), ReadUInt16(jpEntry ?? raw, 0x02),
                    us?.StringBytes, ReadUInt16(usEntry ?? raw, 0x00), ReadUInt16(usEntry ?? raw, 0x02),
                    FfxEncoding.JpDecoder, FfxEncoding.UsDecoder);

                SphereGridLocalizedText simplifiedName = ReadLocalizedText(
                    jp?.StringBytes, ReadUInt16(jpEntry ?? raw, 0x04), ReadUInt16(jpEntry ?? raw, 0x06),
                    us?.StringBytes, ReadUInt16(usEntry ?? raw, 0x04), ReadUInt16(usEntry ?? raw, 0x06),
                    FfxEncoding.JpDecoder, FfxEncoding.UsDecoder);

                SphereGridLocalizedText description = ReadLocalizedText(
                    jp?.StringBytes, ReadUInt16(jpEntry ?? raw, 0x08), ReadUInt16(jpEntry ?? raw, 0x0A),
                    us?.StringBytes, ReadUInt16(usEntry ?? raw, 0x08), ReadUInt16(usEntry ?? raw, 0x0A),
                    FfxEncoding.JpDecoder, FfxEncoding.UsDecoder);

                SphereGridLocalizedText simplifiedDescription = ReadLocalizedText(
                    jp?.StringBytes, ReadUInt16(jpEntry ?? raw, 0x0C), ReadUInt16(jpEntry ?? raw, 0x0E),
                    us?.StringBytes, ReadUInt16(usEntry ?? raw, 0x0C), ReadUInt16(usEntry ?? raw, 0x0E),
                    FfxEncoding.JpDecoder, FfxEncoding.UsDecoder);

                entries.Add(new SphereGridNodeTypeEntry
                {
                    Index = index,
                    Name = name,
                    SimplifiedName = simplifiedName,
                    Description = description,
                    SimplifiedDescription = simplifiedDescription,
                    NodeEffectBitfield = ReadUInt16(raw, 0x10),
                    LearnedMove = ReadUInt16(raw, 0x12),
                    IncreaseAmount = ReadUInt16(raw, 0x14),
                    AppearanceType = ReadUInt16(raw, 0x16),
                    RawBytes = raw.ToArray()
                });
            }

            return new SphereGridNodeTypeTable
            {
                JpPath = jpPath,
                UsPath = usPath,
                JpOriginalBytes = jp?.OriginalBytes,
                UsOriginalBytes = us?.OriginalBytes,
                Header = basePayload.Header,
                Entries = entries
            };
        }

        public static SphereGridLayoutFile ReadLayout(string layoutPath, string contentsPath, string displayName)
        {
            byte[] layoutBytes = File.ReadAllBytes(layoutPath);
            byte[] fullContentBytes = File.ReadAllBytes(contentsPath);
            return ReadLayout(layoutBytes, fullContentBytes, layoutPath, contentsPath, displayName);
        }

        public static SphereGridLayoutFile ReadLayout(byte[] layoutBytes, byte[] fullContentBytes, string layoutPath, string contentsPath, string displayName)
        {
            byte[] contentBytes = fullContentBytes.Length > 0x08
                ? fullContentBytes[0x08..]
                : Array.Empty<byte>();

            ushort clusterCount = ReadUInt16(layoutBytes, 0x02);
            ushort nodeCount = ReadUInt16(layoutBytes, 0x04);
            ushort linkCount = ReadUInt16(layoutBytes, 0x06);

            int clusterOffset = 0x10;
            int nodeOffset = clusterOffset + clusterCount * 0x10;
            int linkOffset = nodeOffset + nodeCount * 0x0C;

            List<SphereGridClusterEntry> clusters = new(clusterCount);
            for (int i = 0; i < clusterCount; i++)
            {
                int offset = clusterOffset + i * 0x10;
                ushort radiusType = ReadUInt16(layoutBytes, offset + 0x06);

                clusters.Add(new SphereGridClusterEntry
                {
                    Index = i,
                    PosX = ReadInt16(layoutBytes, offset + 0x00),
                    PosY = ReadInt16(layoutBytes, offset + 0x02),
                    Unused3 = ReadUInt16(layoutBytes, offset + 0x04),
                    RadiusType = radiusType,
                    Unused5 = ReadUInt16(layoutBytes, offset + 0x08),
                    Unused6 = ReadUInt16(layoutBytes, offset + 0x0A),
                    Unused7 = ReadUInt16(layoutBytes, offset + 0x0C),
                    Unused8 = ReadUInt16(layoutBytes, offset + 0x0E)
                });
            }

            List<SphereGridNodeEntry> nodes = new(nodeCount);
            for (int i = 0; i < nodeCount; i++)
            {
                int offset = nodeOffset + i * 0x0C;
                nodes.Add(new SphereGridNodeEntry
                {
                    Index = i,
                    PosX = ReadInt16(layoutBytes, offset + 0x00),
                    PosY = ReadInt16(layoutBytes, offset + 0x02),
                    Unused3 = ReadUInt16(layoutBytes, offset + 0x04),
                    RedundantContent = ReadUInt16(layoutBytes, offset + 0x06),
                    Cluster = ReadUInt16(layoutBytes, offset + 0x08),
                    Unknown6 = ReadUInt16(layoutBytes, offset + 0x0A),
                    ContentIndex = i < contentBytes.Length ? contentBytes[i] : 0xFF
                });
            }

            List<SphereGridLinkEntry> links = new(linkCount);
            for (int i = 0; i < linkCount; i++)
            {
                int offset = linkOffset + i * 0x08;
                links.Add(new SphereGridLinkEntry
                {
                    Index = i,
                    Node1 = ReadUInt16(layoutBytes, offset + 0x00),
                    Node2 = ReadUInt16(layoutBytes, offset + 0x02),
                    AnchorNode = ReadUInt16(layoutBytes, offset + 0x04),
                    Unused = ReadUInt16(layoutBytes, offset + 0x06)
                });
            }

            foreach (SphereGridLinkEntry link in links)
            {
                if (link.Node1 < nodes.Count)
                {
                    nodes[link.Node1].ConnectedNodeIndices.Add(link.Node2);
                    nodes[link.Node1].ConnectedLinkIndices.Add(link.Index);
                }

                if (link.Node2 < nodes.Count)
                {
                    nodes[link.Node2].ConnectedNodeIndices.Add(link.Node1);
                    nodes[link.Node2].ConnectedLinkIndices.Add(link.Index);
                }

                if (link.AnchorNode < nodes.Count)
                {
                    nodes[link.AnchorNode].AnchorLinkIndices.Add(link.Index);
                }
            }

            return new SphereGridLayoutFile
            {
                DisplayName = displayName,
                LayoutPath = layoutPath,
                ContentsPath = contentsPath,
                RawLayoutBytes = layoutBytes.ToArray(),
                RawContentsBytes = fullContentBytes.ToArray(),
                FileSize = layoutBytes.Length,
                ContentsFileSize = fullContentBytes.Length,
                Unknown1 = ReadUInt16(layoutBytes, 0x00),
                ClusterCount = clusterCount,
                NodeCount = nodeCount,
                LinkCount = linkCount,
                Unknown5 = ReadUInt16(layoutBytes, 0x08),
                Unknown6 = ReadUInt16(layoutBytes, 0x0A),
                Unknown7 = ReadUInt16(layoutBytes, 0x0C),
                Unknown8 = ReadUInt16(layoutBytes, 0x0E),
                Clusters = clusters,
                Nodes = nodes,
                Links = links
            };
        }

        // RT0 preserve-only "identity" writer for the sphere-type table (sphere.bin).
        //
        // NOTE: the production UI path is WriteSphereTypes -> BuildSphereTypesLocaleFile, which is a LOSSY
        // rebuild: it repacks the string pool from decoded-then-re-encoded text (dedup ordered), so a
        // no-edit save through that path is NOT guaranteed byte-identical to the original file. To prove
        // the byte-faithful RT0 gate we add this SEPARATE preserve-only writer (the existing rebuild stays
        // for the UI).
        //
        // Pattern (proven by KeyItem_File): clone the original file bytes and re-stamp ONLY the
        // clearly-fixed scalar fields in place inside the fixed-length data section. The string-offset/
        // key fields (entry bytes 0x00..0x07) and the entire trailing string pool are preserved verbatim
        // from the original, so a no-edit Read->WriteIdentity is byte-identical by construction.
        //
        // The sphere-type editor only authors Behavior (0x08), Activates (0x0A),
        // Range (0x0C), SpecialRole (0x0D) and Reserved0x0E (0x0E); those are the only re-stamped bytes.
        public static byte[] WriteIdentity(SphereGridSphereTypeTable table)
        {
            ArgumentNullException.ThrowIfNull(table);

            byte[]? original = table.JpOriginalBytes ?? table.UsOriginalBytes;
            if (original == null)
                throw new InvalidOperationException("sphere.bin writer requires the original bytes captured during Read.");

            byte[] output = original.ToArray();

            SphereGridLocalizedDataHeader header = table.Header;
            const int dataBase = 0x14;
            int entryLength = header.EntryLength;
            if (entryLength <= 0)
                throw new InvalidDataException($"sphere.bin uses an invalid entry length 0x{entryLength:X4}.");

            foreach (SphereGridSphereTypeEntry entry in table.Entries)
            {
                int relativeIndex = entry.Index - header.MinIndex;
                if (relativeIndex < 0 || relativeIndex >= header.EntryCount)
                    throw new InvalidOperationException($"sphere.bin entry index {entry.Index} is outside the table range.");

                int entryOffset = dataBase + relativeIndex * entryLength;
                if (entryOffset < dataBase || entryOffset + 0x10 > output.Length || entryOffset + entryLength > dataBase + header.DataLength)
                    throw new InvalidOperationException($"sphere.bin entry {entry.Index} extends past the data section.");

                // Only the clearly-fixed scalar fields are re-stamped; string offset/key fields (0x00..0x07)
                // are left as preserved in the cloned original. WriteUInt16 lives in SphereGrid_File.Write.cs.
                WriteUInt16(output, entryOffset + 0x08, entry.Behavior);
                WriteUInt16(output, entryOffset + 0x0A, entry.Activates);
                output[entryOffset + 0x0C] = entry.Range;
                output[entryOffset + 0x0D] = entry.SpecialRole;
                WriteUInt16(output, entryOffset + 0x0E, entry.Reserved0x0E);
            }

            return output;
        }

        static LocalizedDataPayload? TryReadLocalizedDataPayload(string? path, byte[]? bytes = null)
        {
            if (bytes == null && (string.IsNullOrWhiteSpace(path) || !File.Exists(path)))
                return null;

            bytes ??= File.ReadAllBytes(path!);
            if (bytes.Length < 0x14)
                throw new InvalidDataException($"Localized data file is too small: {path}");

            SphereGridLocalizedDataHeader header = new()
            {
                SourcePath = path,
                Signature = bytes[0x00],
                MinIndex = ReadUInt16(bytes, 0x08),
                MaxIndex = ReadUInt16(bytes, 0x0A),
                EntryLength = ReadUInt16(bytes, 0x0C),
                DataLength = ReadUInt16(bytes, 0x0E),
                EntryCount = Math.Max(0, ReadUInt16(bytes, 0x0A) - ReadUInt16(bytes, 0x08) + 1),
                FileSize = bytes.Length
            };

            int availableDataLength = Math.Max(0, bytes.Length - 0x14);
            int dataLength = Math.Min(header.DataLength, availableDataLength);

            byte[] dataBytes = new byte[dataLength];
            Array.Copy(bytes, 0x14, dataBytes, 0, dataLength);

            byte[] stringBytes = Array.Empty<byte>();
            int stringOffset = 0x14 + dataLength;
            if (stringOffset < bytes.Length)
            {
                stringBytes = new byte[bytes.Length - stringOffset];
                Array.Copy(bytes, stringOffset, stringBytes, 0, stringBytes.Length);
            }

            return new LocalizedDataPayload
            {
                Header = header,
                DataBytes = dataBytes,
                StringBytes = stringBytes,
                OriginalBytes = bytes.ToArray()
            };
        }

        static SphereGridLocalizedText ReadLocalizedText(
            byte[]? jpStrings, ushort jpOffset, ushort jpKey,
            byte[]? usStrings, ushort usOffset, ushort usKey,
            Dictionary<byte, char> jpDecoder,
            Dictionary<byte, char> usDecoder)
        {
            return new SphereGridLocalizedText
            {
                JpOffset = jpOffset,
                JpKey = jpKey,
                JpText = DecodeString(jpStrings, jpOffset, jpDecoder),
                UsOffset = usOffset,
                UsKey = usKey,
                UsText = DecodeString(usStrings, usOffset, usDecoder)
            };
        }

        static string DecodeString(byte[]? stringBytes, int offset, Dictionary<byte, char> decoder)
        {
            if (stringBytes == null || stringBytes.Length == 0 || offset < 0 || offset >= stringBytes.Length)
                return string.Empty;

            byte[] raw = TextBinary_Util.ReadNullTerminatedScript(stringBytes, offset);
            return TextBinary_Util.DecodeScriptToString(raw, decoder, true);
        }

        static ushort ReadUInt16(byte[] bytes, int offset)
        {
            return offset >= 0 && offset + 2 <= bytes.Length
                ? (ushort)(bytes[offset] | (bytes[offset + 1] << 8))
                : (ushort)0;
        }

        static short ReadInt16(byte[] bytes, int offset)
        {
            return unchecked((short)ReadUInt16(bytes, offset));
        }

        static byte ReadByte(byte[] bytes, int offset)
        {
            return offset >= 0 && offset < bytes.Length
                ? bytes[offset]
                : (byte)0;
        }

        sealed class LocalizedDataPayload
        {
            public required SphereGridLocalizedDataHeader Header { get; init; }
            public required byte[] DataBytes { get; init; }
            public required byte[] StringBytes { get; init; }
            public required byte[] OriginalBytes { get; init; }

            public byte[]? GetEntryBytes(int index)
            {
                int relativeIndex = index - Header.MinIndex;
                if (relativeIndex < 0 || relativeIndex >= Header.EntryCount || Header.EntryLength <= 0)
                    return null;

                int offset = relativeIndex * Header.EntryLength;
                if (offset < 0 || offset + Header.EntryLength > DataBytes.Length)
                    return null;

                byte[] result = new byte[Header.EntryLength];
                Array.Copy(DataBytes, offset, result, 0, Header.EntryLength);
                return result;
            }
        }
    }

    public sealed class SphereGridLocalizedDataHeader
    {
        public required string SourcePath { get; init; }
        public required byte Signature { get; init; }
        public required ushort MinIndex { get; init; }
        public required ushort MaxIndex { get; init; }
        public required ushort EntryLength { get; init; }
        public required ushort DataLength { get; init; }
        public required int EntryCount { get; init; }
        public required int FileSize { get; init; }
    }

    public sealed class SphereGridLocalizedText
    {
        public required ushort JpOffset { get; init; }
        public required ushort JpKey { get; init; }
        public required string JpText { get; init; }
        public required ushort UsOffset { get; init; }
        public required ushort UsKey { get; init; }
        public required string UsText { get; init; }

        public string PreferredText => !string.IsNullOrWhiteSpace(UsText) ? UsText : JpText;
        public string LocaleSummary => !string.IsNullOrWhiteSpace(UsText) ? "US first" : "JP fallback";
        public string HeaderSummary =>
            $"JP Ofst {JpOffset:X4}h · Key {JpKey:X4}h{Environment.NewLine}US Ofst {UsOffset:X4}h · Key {UsKey:X4}h";
    }

    /// <summary>
    /// Sphere behavior — Fahrenheit/Ghidra `Sphere.type` @ sphere.bin +0x08.
    /// Proved on vanilla jppc corpus (50 entries): only 0..2 are used.
    /// </summary>
    public enum SphereBehavior : ushort
    {
        None = 0,
        /// <summary>Activates a node; <see cref="SphereGridSphereTypeEntry.Activates"/> holds the target bitmask.</summary>
        Activator = 1,
        /// <summary>Modifies the grid (Return/Teleport/Clear/Master...) — <see cref="SphereGridSphereTypeEntry.SpecialRole"/> selects the behavior.</summary>
        Modifier = 2,
    }

    /// <summary>
    /// Node-category targets for activator spheres — Fahrenheit/Ghidra `Sphere.activates` @ sphere.bin +0x0A.
    /// BIT ORDER PROVEN 2026-08-01 (corpus + fahrenheit + game semantics): Strength=bit0, Defense=1, Magic=2,
    /// MagicDefense=3, Agility=4, Luck=5, Evasion=6, Accuracy=7, Hp=8, Mp=9, Ability=10 (All=0x07FF).
    /// Vanilla jppc sphere.bin (entries 0-4, ACTIVATOR, Normal): entry0 0x0103={STR,DEF,HP}=Power Sphere;
    /// entry1 0x020C={MAG,MDF,MP}=Mana; entry2 0x00D0={AGL,EVA,ACC}=Speed; entry3 0x0400={ABILITY}=Ability;
    /// entry4 0x0020={LUCK}=Luck; entry9 0x07FF=Friend. See docs/reverse/FFX_SPHERE_BIN_ACTIVATES_BIT_ORDER_PROVEN_2026-08-01.md.
    /// </summary>
    [Flags]
    public enum SphereTargets : ushort
    {
        Strength = 1 << 0,
        Defense = 1 << 1,
        Magic = 1 << 2,
        MagicDefense = 1 << 3,
        Agility = 1 << 4,
        Luck = 1 << 5,
        Evasion = 1 << 6,
        Accuracy = 1 << 7,
        Hp = 1 << 8,
        Mp = 1 << 9,
        Ability = 1 << 10,
        All = 0x07FF,
    }

    /// <summary>
    /// Sphere action range — Fahrenheit/Ghidra `Sphere.range` @ sphere.bin +0x0C.
    /// Proved on vanilla jppc corpus: 0x00 (empty), 0x01 (normal), 0x20 (unlimited — Key Spheres / Friend / teleports).
    /// </summary>
    public enum SphereRange : byte
    {
        None = 0,
        Normal = 1,
        Unlimited = 1 << 5, // 0x20
    }

    public sealed class SphereGridSphereTypeTable
    {
        public required string JpPath { get; init; }
        public required string? UsPath { get; init; }
        public required byte[]? JpOriginalBytes { get; init; }
        public required byte[]? UsOriginalBytes { get; init; }
        public required SphereGridLocalizedDataHeader Header { get; init; }
        public required IReadOnlyList<SphereGridSphereTypeEntry> Entries { get; init; }
    }

    public sealed class SphereGridSphereTypeEntry
    {
        public required int Index { get; init; }
        public required SphereGridLocalizedText Description { get; init; }
        public required SphereGridLocalizedText SimplifiedDescription { get; init; }

        /// <summary>+0x08 — <see cref="SphereBehavior"/> (None/Activator/Modifier).</summary>
        public required ushort Behavior { get; init; }

        /// <summary>+0x0A — target bitmask for activator spheres (<see cref="SphereTargets"/>).</summary>
        public required ushort Activates { get; init; }

        /// <summary>+0x0C — <see cref="SphereRange"/> (None/Normal/Unlimited).</summary>
        public required byte Range { get; init; }

        /// <summary>+0x0D — special role: 0 normal, 1 Return, 2 Teleport, 3 Clear, 4 Master, 8..11 Key L1..L4 (proved on vanilla corpus; other values observed up to 24).</summary>
        public required byte SpecialRole { get; init; }

        /// <summary>+0x0E — reserved (always 0 in vanilla).</summary>
        public required ushort Reserved0x0E { get; init; }

        public required byte[] RawBytes { get; init; }

        public SphereBehavior BehaviorKind => (SphereBehavior)Behavior;
        public SphereTargets ActivatesTargets => (SphereTargets)Activates;
        public SphereRange RangeKind => (SphereRange)Range;

        public string ActionLabel => BehaviorKind switch
        {
            SphereBehavior.Activator => "Activator",
            SphereBehavior.Modifier => "Modifier",
            _ => $"Unknown ({Behavior:X4}h)"
        };

        public string RangeLabel => RangeKind switch
        {
            SphereRange.Normal => "Short Range",
            SphereRange.Unlimited => "Long Range",
            _ => $"Unknown ({Range:X2}h)"
        };
    }

    public sealed class SphereGridNodeTypeTable
    {
        public required string JpPath { get; init; }
        public required string? UsPath { get; init; }
        public required byte[]? JpOriginalBytes { get; init; }
        public required byte[]? UsOriginalBytes { get; init; }
        public required SphereGridLocalizedDataHeader Header { get; init; }
        public required IReadOnlyList<SphereGridNodeTypeEntry> Entries { get; init; }
    }

    public sealed class SphereGridNodeTypeEntry
    {
        public required int Index { get; init; }
        public required SphereGridLocalizedText Name { get; init; }
        public required SphereGridLocalizedText SimplifiedName { get; init; }
        public required SphereGridLocalizedText Description { get; init; }
        public required SphereGridLocalizedText SimplifiedDescription { get; init; }
        public required ushort NodeEffectBitfield { get; init; }
        public required ushort LearnedMove { get; init; }
        public required ushort IncreaseAmount { get; init; }
        public required ushort AppearanceType { get; init; }
        public required byte[] RawBytes { get; init; }

        public string PreferredName => string.IsNullOrWhiteSpace(Name.PreferredText) ? $"NodeType {Index:X2}h" : Name.PreferredText;
        public string PreferredDescription => Description.PreferredText;
    }

    public sealed class SphereGridLayoutFile
    {
        public required string DisplayName { get; init; }
        public required string LayoutPath { get; init; }
        public required string ContentsPath { get; init; }
        public required byte[] RawLayoutBytes { get; init; }
        public required byte[] RawContentsBytes { get; init; }
        public required int FileSize { get; init; }
        public required int ContentsFileSize { get; init; }
        public required ushort Unknown1 { get; init; }
        public required ushort ClusterCount { get; init; }
        public required ushort NodeCount { get; init; }
        public required ushort LinkCount { get; init; }
        public required ushort Unknown5 { get; init; }
        public required ushort Unknown6 { get; init; }
        public required ushort Unknown7 { get; init; }
        public required ushort Unknown8 { get; init; }
        public required IReadOnlyList<SphereGridClusterEntry> Clusters { get; init; }
        public required IReadOnlyList<SphereGridNodeEntry> Nodes { get; init; }
        public required IReadOnlyList<SphereGridLinkEntry> Links { get; init; }
    }

    public sealed class SphereGridClusterEntry
    {
        public required int Index { get; init; }
        public required short PosX { get; init; }
        public required short PosY { get; init; }
        public required ushort Unused3 { get; init; }
        public required ushort RadiusType { get; init; }
        public required ushort Unused5 { get; init; }
        public required ushort Unused6 { get; init; }
        public required ushort Unused7 { get; init; }
        public required ushort Unused8 { get; init; }

        public int Radius => RadiusType & 0x03;
        public bool AltDesign => (RadiusType & 0x04) != 0;
    }

    public sealed class SphereGridNodeEntry
    {
        public required int Index { get; init; }
        public required short PosX { get; init; }
        public required short PosY { get; init; }
        public required ushort Unused3 { get; init; }
        public required ushort RedundantContent { get; init; }
        public required ushort Cluster { get; init; }
        public required ushort Unknown6 { get; init; }
        public required int ContentIndex { get; init; }
        public List<int> ConnectedNodeIndices { get; } = new();
        public List<int> ConnectedLinkIndices { get; } = new();
        public List<int> AnchorLinkIndices { get; } = new();
    }

    public sealed class SphereGridLinkEntry
    {
        public required int Index { get; init; }
        public required ushort Node1 { get; init; }
        public required ushort Node2 { get; init; }
        public required ushort AnchorNode { get; init; }
        public required ushort Unused { get; init; }
    }
}

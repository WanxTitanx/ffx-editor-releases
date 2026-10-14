using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FFXProjectEditor.FfxLib.Text
{
    public sealed class LegacyMenuMainResource_File
    {
        const int AuthorOffsetConst = 0xA8;
        const int ResourceNameOffsetConst = 0xB4;
        const int FileSizeFieldOffset = 0x10;
        const int PrimaryFooterOffsetField = 0x20;
        const int PrimaryFooterDuplicateOffsetField = 0x28;
        const int SecondaryFooterOffsetField = 0x2C;
        const int BodyOffsetField = 0x30;
        const int FooterMagicConst = 0x40;
        const int FooterExtensionSizeConst = 0x40;
        const int FooterTimestampMetadataDeltaConst = 0x08;
        const int FooterMetadataEchoDeltaConst = 0x04;
        const int PrimaryDescriptorHeader0Const = 0x00;
        const int PrimaryDescriptorHeader1Const = 0x01;
        const int PrimaryDescriptorHeader2Const = 0x04;
        const int PrimaryDescriptorCountConst = 9;
        const int PrimaryDescriptorTrailingZeroCountConst = 3;
        const int ResourceBodyOffsetConst = 0xE0;

        public required int FileSize { get; init; }
        public required int EntryOffset { get; init; }
        public required int BodyOffset { get; init; }
        public required int SecondaryFooterOffset { get; init; }
        public required int PrimaryFooterOffset { get; init; }
        public required int MetadataOffset { get; init; }
        public required int TimestampOffset { get; init; }
        public required string Author { get; init; }
        public required string ResourceName { get; init; }
        public required string Timestamp { get; init; }
        public required IReadOnlyList<int> PrimaryDescriptorOffsets { get; init; }

        public int BodyLength => EntryOffset - BodyOffset;
        public string PrimaryDescriptorSummary => string.Join(", ", PrimaryDescriptorOffsets.Select(offset => $"{offset:X4}h"));
        public string Summary =>
            $"LEGACY MENUMAIN RESOURCE · PROVEN NON-TEXT CONTAINER · body {BodyOffset:X4}h..{EntryOffset - 1:X4}h ({BodyLength:X}h) · footer {SecondaryFooterOffset:X4}h/{PrimaryFooterOffset:X4}h · author '{Author}' · resource '{ResourceName}' · timestamp {Timestamp}.";

        public static LegacyMenuMainResource_File Read(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);

            if (bytes.Length < ResourceNameOffsetConst + 0x20)
                throw new InvalidDataException("Legacy menumain resource is too small.");

            int entryOffset = TextBinary_Util.ReadInt32(bytes, 0x00);
            int fileSizeField = TextBinary_Util.ReadInt32(bytes, FileSizeFieldOffset);
            int primaryFooterOffset = TextBinary_Util.ReadInt32(bytes, PrimaryFooterOffsetField);
            int primaryFooterDuplicateOffset = TextBinary_Util.ReadInt32(bytes, PrimaryFooterDuplicateOffsetField);
            int secondaryFooterOffset = TextBinary_Util.ReadInt32(bytes, SecondaryFooterOffsetField);
            int bodyOffset = TextBinary_Util.ReadInt32(bytes, BodyOffsetField);

            if (fileSizeField != bytes.Length)
                throw new InvalidDataException($"Legacy menumain resource expected file-size echo {bytes.Length:X4}h, got {fileSizeField:X4}h.");

            if (bodyOffset != ResourceBodyOffsetConst)
                throw new InvalidDataException($"Legacy menumain resource expected body offset {ResourceBodyOffsetConst:X4}h, got {bodyOffset:X4}h.");

            if (entryOffset <= bodyOffset || entryOffset >= bytes.Length)
                throw new InvalidDataException($"Legacy menumain resource entry offset {entryOffset:X4}h is outside the proven body range.");

            if (primaryFooterOffset != primaryFooterDuplicateOffset)
                throw new InvalidDataException("Legacy menumain resource primary footer offset no longer matches its header duplicate.");

            if (secondaryFooterOffset <= entryOffset || secondaryFooterOffset >= bytes.Length)
                throw new InvalidDataException($"Legacy menumain resource secondary footer {secondaryFooterOffset:X4}h is outside the proven trailer range.");

            if (primaryFooterOffset != secondaryFooterOffset + FooterExtensionSizeConst)
            {
                throw new InvalidDataException(
                    $"Legacy menumain resource expected primary footer {secondaryFooterOffset + FooterExtensionSizeConst:X4}h after the secondary footer, got {primaryFooterOffset:X4}h.");
            }

            EnsureRange(bytes, secondaryFooterOffset, FooterExtensionSizeConst, "legacy menumain secondary footer");
            EnsureRange(bytes, primaryFooterOffset, (3 + PrimaryDescriptorCountConst + PrimaryDescriptorTrailingZeroCountConst) * 4, "legacy menumain primary footer");

            int footerMagic = TextBinary_Util.ReadInt32(bytes, secondaryFooterOffset + 0x00);
            int metadataOffset = TextBinary_Util.ReadInt32(bytes, secondaryFooterOffset + 0x0C);
            int timestampOffset = TextBinary_Util.ReadInt32(bytes, secondaryFooterOffset + 0x10);
            int metadataEchoOffset = TextBinary_Util.ReadInt32(bytes, secondaryFooterOffset + 0x28);

            if (footerMagic != FooterMagicConst)
                throw new InvalidDataException($"Legacy menumain resource expected secondary footer magic {FooterMagicConst:X2}h, got {footerMagic:X2}h.");

            if (metadataOffset < entryOffset || metadataOffset >= secondaryFooterOffset)
                throw new InvalidDataException($"Legacy menumain resource metadata block {metadataOffset:X4}h is outside the proven trailer-prelude range.");

            if (timestampOffset != metadataOffset + FooterTimestampMetadataDeltaConst)
            {
                throw new InvalidDataException(
                    $"Legacy menumain resource expected timestamp offset {metadataOffset + FooterTimestampMetadataDeltaConst:X4}h after metadata start, got {timestampOffset:X4}h.");
            }

            if (metadataEchoOffset != metadataOffset + FooterMetadataEchoDeltaConst)
            {
                throw new InvalidDataException(
                    $"Legacy menumain resource expected metadata echo offset {metadataOffset + FooterMetadataEchoDeltaConst:X4}h, got {metadataEchoOffset:X4}h.");
            }

            for (int i = 0; i < FooterTimestampMetadataDeltaConst; i++)
            {
                if (bytes[metadataOffset + i] != 0)
                    throw new InvalidDataException("Legacy menumain resource metadata prelude no longer matches the proven 8-byte zero block.");
            }

            string author = ReadAsciiString(bytes, AuthorOffsetConst, "legacy menumain author");
            string resourceName = ReadAsciiString(bytes, ResourceNameOffsetConst, "legacy menumain resource name");
            string timestamp = ReadAsciiString(bytes, timestampOffset, "legacy menumain timestamp");

            if (!string.Equals(author, "nakazawa", StringComparison.Ordinal))
                throw new InvalidDataException($"Legacy menumain resource expected author 'nakazawa', got '{author}'.");

            if (!string.Equals(resourceName, "menumain", StringComparison.Ordinal))
                throw new InvalidDataException($"Legacy menumain resource expected resource name 'menumain', got '{resourceName}'.");

            if (!MatchesTimestampShape(timestamp))
                throw new InvalidDataException($"Legacy menumain resource timestamp '{timestamp}' no longer matches the proven MM/DD HH:MM:SS shape.");

            int primaryDescriptorHeader0 = TextBinary_Util.ReadInt32(bytes, primaryFooterOffset + 0x00);
            int primaryDescriptorHeader1 = TextBinary_Util.ReadInt32(bytes, primaryFooterOffset + 0x04);
            int primaryDescriptorHeader2 = TextBinary_Util.ReadInt32(bytes, primaryFooterOffset + 0x08);

            if (primaryDescriptorHeader0 != PrimaryDescriptorHeader0Const
                || primaryDescriptorHeader1 != PrimaryDescriptorHeader1Const
                || primaryDescriptorHeader2 != PrimaryDescriptorHeader2Const)
            {
                throw new InvalidDataException(
                    $"Legacy menumain resource primary footer header expected 0/{PrimaryDescriptorHeader1Const}/{PrimaryDescriptorHeader2Const}, got {primaryDescriptorHeader0}/{primaryDescriptorHeader1}/{primaryDescriptorHeader2}.");
            }

            List<int> primaryDescriptorOffsets = new(PrimaryDescriptorCountConst);
            for (int i = 0; i < PrimaryDescriptorCountConst; i++)
            {
                int offset = TextBinary_Util.ReadInt32(bytes, primaryFooterOffset + 0x0C + i * 4);
                if (offset <= bodyOffset || offset >= bytes.Length)
                {
                    throw new InvalidDataException(
                        $"Legacy menumain resource primary descriptor {i:X2}h points outside the proven body/container range: {offset:X4}h.");
                }

                primaryDescriptorOffsets.Add(offset);
            }

            for (int i = 0; i < PrimaryDescriptorTrailingZeroCountConst; i++)
            {
                int trailing = TextBinary_Util.ReadInt32(bytes, primaryFooterOffset + 0x0C + (PrimaryDescriptorCountConst + i) * 4);
                if (trailing != 0)
                    throw new InvalidDataException($"Legacy menumain resource trailing primary footer slot {i:X2}h must stay zero in the proven container shape.");
            }

            int entryEchoOffset = primaryDescriptorOffsets[7];
            if (entryEchoOffset != entryOffset - 1)
                throw new InvalidDataException($"Legacy menumain resource expected primary descriptor 07h to echo entry-1 ({entryOffset - 1:X4}h), got {entryEchoOffset:X4}h.");

            return new LegacyMenuMainResource_File
            {
                FileSize = bytes.Length,
                EntryOffset = entryOffset,
                BodyOffset = bodyOffset,
                SecondaryFooterOffset = secondaryFooterOffset,
                PrimaryFooterOffset = primaryFooterOffset,
                MetadataOffset = metadataOffset,
                TimestampOffset = timestampOffset,
                Author = author,
                ResourceName = resourceName,
                Timestamp = timestamp,
                PrimaryDescriptorOffsets = primaryDescriptorOffsets
            };
        }

        static void EnsureRange(byte[] bytes, int offset, int length, string label)
        {
            if (offset < 0 || length < 0 || offset + length > bytes.Length)
                throw new InvalidDataException($"{label} runs past EOF.");
        }

        static string ReadAsciiString(byte[] bytes, int offset, string label)
        {
            if (offset < 0 || offset >= bytes.Length)
                throw new InvalidDataException($"{label} offset {offset:X4}h is outside the file.");

            int terminatorIndex = TextBinary_Util.FindNullTerminator(bytes, offset);
            if (terminatorIndex < 0)
                throw new InvalidDataException($"{label} is missing a terminating NULL byte.");

            byte[] rawBytes = TextBinary_Util.ReadNullTerminatedScript(bytes, offset);
            if (rawBytes.Any(b => b < 0x20 || b > 0x7E))
                throw new InvalidDataException($"{label} contains non-ASCII bytes.");

            return Encoding.ASCII.GetString(rawBytes);
        }

        static bool MatchesTimestampShape(string value)
        {
            if (value.Length != 14)
                return false;

            return char.IsDigit(value[0])
                && char.IsDigit(value[1])
                && value[2] == '/'
                && char.IsDigit(value[3])
                && char.IsDigit(value[4])
                && value[5] == ' '
                && char.IsDigit(value[6])
                && char.IsDigit(value[7])
                && value[8] == ':'
                && char.IsDigit(value[9])
                && char.IsDigit(value[10])
                && value[11] == ':'
                && char.IsDigit(value[12])
                && char.IsDigit(value[13]);
        }
    }
}

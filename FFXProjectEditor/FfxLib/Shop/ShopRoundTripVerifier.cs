using System;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Shop
{
    internal static class ShopRoundTripVerifier
    {
        public static ShopRoundTripReport Verify(byte[] bytes, ShopTableKind kind, ShopGearCatalog? gearCatalog = null)
        {
            ArgumentNullException.ThrowIfNull(bytes);

            ShopTable table = ShopTable_File.Read(bytes, kind, gearCatalog);
            byte[] roundTripped = ShopTable_File.Write(table);

            if (bytes.SequenceEqual(roundTripped))
            {
                return new ShopRoundTripReport
                {
                    IsByteIdentical = true,
                    Summary = "No-edit round-trip is byte-identical in the current build.",
                    FirstDifferentOffset = null,
                    ExpectedByte = null,
                    ActualByte = null
                };
            }

            int firstDifferentOffset = FindFirstDifference(bytes, roundTripped);
            byte? expectedByte = firstDifferentOffset < bytes.Length ? bytes[firstDifferentOffset] : null;
            byte? actualByte = firstDifferentOffset < roundTripped.Length ? roundTripped[firstDifferentOffset] : null;
            string detail = bytes.Length != roundTripped.Length
                ? $" File length changed from {bytes.Length} to {roundTripped.Length} bytes."
                : string.Empty;

            return new ShopRoundTripReport
            {
                IsByteIdentical = false,
                Summary = $"No-edit round-trip drifted at offset 0x{firstDifferentOffset:X4}. Writer stays closed.{detail}",
                FirstDifferentOffset = firstDifferentOffset,
                ExpectedByte = expectedByte,
                ActualByte = actualByte
            };
        }

        public static ShopRoundTripReport VerifyFile(string absolutePath, ShopTableKind kind, ShopGearCatalog? gearCatalog = null)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
                throw new InvalidDataException("Shop file path is missing.");

            if (!File.Exists(absolutePath))
                throw new FileNotFoundException("Shop file not found.", absolutePath);

            return Verify(File.ReadAllBytes(absolutePath), kind, gearCatalog);
        }

        static int FindFirstDifference(byte[] left, byte[] right)
        {
            int sharedLength = Math.Min(left.Length, right.Length);
            for (int i = 0; i < sharedLength; i++)
            {
                if (left[i] != right[i])
                    return i;
            }

            return sharedLength;
        }
    }

    internal sealed class ShopRoundTripReport
    {
        public required bool IsByteIdentical { get; init; }
        public required string Summary { get; init; }
        public required int? FirstDifferentOffset { get; init; }
        public required byte? ExpectedByte { get; init; }
        public required byte? ActualByte { get; init; }
    }
}

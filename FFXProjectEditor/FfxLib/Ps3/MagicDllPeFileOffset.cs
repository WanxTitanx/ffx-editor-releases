using System;
using System.IO;

namespace FFXProjectEditor.FfxLib.Ps3
{
    internal static class MagicDllPeFileOffset
    {
        public static int VaToFileOffset(byte[] peBytes, uint imageVa, uint defaultImageBase = 0x10000000)
        {
            if (peBytes.Length < 0x40)
                throw new InvalidDataException("PE too small");

            int peOff = BitConverter.ToInt32(peBytes, 0x3C);
            if (peOff < 0 || peOff + 0xF8 > peBytes.Length)
                throw new InvalidDataException("Invalid PE header");

            int numSec = BitConverter.ToInt16(peBytes, peOff + 6);
            int optSize = BitConverter.ToInt16(peBytes, peOff + 20);
            int secTable = peOff + 24 + optSize;
            uint rva = imageVa - defaultImageBase;

            for (int i = 0; i < numSec; i++)
            {
                int so = secTable + i * 40;
                if (so + 40 > peBytes.Length)
                    break;

                uint vSize = BitConverter.ToUInt32(peBytes, so + 8);
                uint vAddr = BitConverter.ToUInt32(peBytes, so + 12);
                uint rawSize = BitConverter.ToUInt32(peBytes, so + 16);
                uint rawPtr = BitConverter.ToUInt32(peBytes, so + 20);
                uint span = Math.Max(vSize, rawSize);
                if (rva >= vAddr && rva < vAddr + span)
                    return (int)(rawPtr + (rva - vAddr));
            }

            throw new InvalidDataException($"RVA 0x{rva:X} not mapped (VA 0x{imageVa:X})");
        }
    }
}

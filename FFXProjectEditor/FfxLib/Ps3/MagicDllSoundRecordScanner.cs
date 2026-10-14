using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FFXProjectEditor.FfxLib.Ps3
{
    /// <summary>
    /// Scans magic DLL PE bytes for embedded SeSep-shaped sound cue records
    /// (same dialect as event chunk2 — see FFX_EVENT_SESEP_PAYLOAD_CRACKED).
    /// </summary>
    public static class MagicDllSoundRecordScanner
    {
        public const uint SeSepMagicLo = 0x65536553u; // "SeSe"
        public const uint SeSepMagicHi = 0x20202070u; // "p   "

        public sealed record SeSepHit(
            int FileOffset,
            int Rva,
            uint SeId,
            uint RecLen,
            ushort WaveDataId,
            byte VoiceCount);

        public static IReadOnlyList<SeSepHit> Scan(byte[] peBytes, int imageBase = 0x10000000)
        {
            var hits = new List<SeSepHit>();
            if (peBytes.Length < 32)
                return hits;

            for (int i = 0; i <= peBytes.Length - 16; i++)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(peBytes.AsSpan(i)) != SeSepMagicLo)
                    continue;
                if (BinaryPrimitives.ReadUInt32LittleEndian(peBytes.AsSpan(i + 4)) != SeSepMagicHi)
                    continue;

                uint seId = BinaryPrimitives.ReadUInt32LittleEndian(peBytes.AsSpan(i + 8));
                uint recLen = BinaryPrimitives.ReadUInt32LittleEndian(peBytes.AsSpan(i + 12));
                if (recLen < 16 || recLen > 0x10000 || (recLen & 3) != 0 || i + recLen > peBytes.Length)
                    continue;

                ushort waveId = 0;
                byte voiceCount = 0;
                if (i + 0x12 <= peBytes.Length)
                {
                    voiceCount = peBytes[i + 0x10];
                    waveId = BinaryPrimitives.ReadUInt16LittleEndian(peBytes.AsSpan(i + 0x11));
                }

                int rva = imageBase + i; // approximate for overlay-flattened scan
                hits.Add(new SeSepHit(i, rva, seId, recLen, waveId, voiceCount));
                i += (int)recLen - 1;
            }

            return hits;
        }

        public static bool TryPatchSeId(byte[] peBytes, int fileOffset, uint newSeId)
        {
            if (fileOffset < 0 || fileOffset + 12 > peBytes.Length)
                return false;
            if (BinaryPrimitives.ReadUInt32LittleEndian(peBytes.AsSpan(fileOffset)) != SeSepMagicLo)
                return false;
            BinaryPrimitives.WriteUInt32LittleEndian(peBytes.AsSpan(fileOffset + 8), newSeId);
            return true;
        }

        public static bool TryPatchWaveDataId(byte[] peBytes, int fileOffset, ushort newWaveId)
        {
            if (fileOffset < 0 || fileOffset + 0x13 > peBytes.Length)
                return false;
            if (BinaryPrimitives.ReadUInt32LittleEndian(peBytes.AsSpan(fileOffset)) != SeSepMagicLo)
                return false;
            BinaryPrimitives.WriteUInt16LittleEndian(peBytes.AsSpan(fileOffset + 0x11), newWaveId);
            return true;
        }
    }
}

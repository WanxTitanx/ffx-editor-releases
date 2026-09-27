using System;
using System.Buffers.Binary;

namespace FFXProjectEditor.FfxLib.BattleMap
{
    /// <summary>
    /// Replaces chunk0 (battle camera ATEL script) in a target battle bin with chunk0 from a donor bin,
    /// shifting all later chunks and re-stamping the chunk-offset table. Used when the scenario template
    /// supplies backdrop/party anchors but a proven wide multi-boss camera lives in another vanilla bin
    /// (e.g. Bikanel desert <c>bika02_01</c> + aeon quad camera <c>nagi05_24</c>).
    /// </summary>
    public static class BattleChunk0GraftWriter
    {
        /// <summary>Swap chunk0 in <paramref name="targetBin"/> for chunk0 from <paramref name="donorBin"/>.</summary>
        public static byte[] GraftChunk0(byte[] targetBin, byte[] donorBin)
        {
            ArgumentNullException.ThrowIfNull(targetBin);
            ArgumentNullException.ThrowIfNull(donorBin);

            (int tgtStart, int tgtLen) = ExtractChunk0Span(targetBin);
            (int donStart, int donLen) = ExtractChunk0Span(donorBin);
            if (tgtStart < 0)
                throw new InvalidOperationException("target battle bin has no graftable chunk0.");
            if (donStart < 0)
                throw new InvalidOperationException("donor battle bin has no graftable chunk0.");

            int byteDelta = donLen - tgtLen;
            int splicePos = tgtStart + tgtLen;

            byte[] output;
            if (byteDelta == 0)
            {
                output = (byte[])targetBin.Clone();
                Array.Copy(donorBin, donStart, output, tgtStart, donLen);
            }
            else
            {
                output = new byte[targetBin.Length + byteDelta];
                Array.Copy(targetBin, 0, output, 0, tgtStart);
                Array.Copy(donorBin, donStart, output, tgtStart, donLen);
                Array.Copy(targetBin, splicePos, output, splicePos + byteDelta, targetBin.Length - splicePos);
            }

            int chunkCount = ReadInt32(output, 0) - 1;
            if (chunkCount < 1)
                throw new InvalidOperationException("invalid chunk count in target battle bin.");

            for (int i = 1; i <= chunkCount; i++)
            {
                int off = 0x04 + i * 4;
                int v = ReadInt32(output, off);
                if (v != 0 && v != unchecked((int)0xFFFFFFFF))
                    WriteInt32(output, off, v + byteDelta);
            }

            return output;
        }

        private static (int start, int length) ExtractChunk0Span(byte[] bytes)
        {
            if (bytes.Length < 0x40)
                return (-1, 0);

            int raw = ReadInt32(bytes, 0);
            int chunkCount = raw - 1;
            if (chunkCount <= 0)
                return (-1, 0);

            int[] offsets = new int[chunkCount + 1];
            for (int i = 0; i <= chunkCount; i++)
            {
                int off = ReadInt32(bytes, 0x04 + i * 4);
                if (off == unchecked((int)0xFFFFFFFF))
                {
                    chunkCount = i - 1;
                    break;
                }
                offsets[i] = off;
            }

            int start = offsets[0];
            if (start <= 0 || start > bytes.Length)
                return (-1, 0);

            int end = bytes.Length;
            for (int j = 1; j <= chunkCount; j++)
            {
                if (offsets[j] >= start)
                {
                    end = offsets[j];
                    break;
                }
            }

            int len = Math.Max(0, end - start);
            return len < 0x34 ? (-1, 0) : (start, len);
        }

        private static int ReadInt32(byte[] b, int o) =>
            (o < 0 || o + 4 > b.Length) ? 0 : BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(o, 4));

        private static void WriteInt32(byte[] b, int o, int v)
        {
            if (o < 0 || o + 4 > b.Length)
                throw new ArgumentOutOfRangeException(nameof(o));
            BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(o, 4), v);
        }
    }
}

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FFXProjectEditor.FfxLib.Audio
{
    /// <summary>Read legacy 8-byte rows from 9999_common.txt / 9999_loop.txt sidecars.</summary>
    public static class FevLegacySidecarReader
    {
        public sealed record CommonRow(int RowIndex, uint Key, uint FsbSampleIndex);

        public static IReadOnlyList<CommonRow> ReadCommonRows(string commonPath)
        {
            if (!File.Exists(commonPath))
                return [];

            byte[] bytes = File.ReadAllBytes(commonPath);
            if (bytes.Length % FevLegacySidecarWriter.RowBytes != 0)
                return [];

            int rows = bytes.Length / FevLegacySidecarWriter.RowBytes;
            var list = new List<CommonRow>(rows);
            for (int i = 0; i < rows; i++)
            {
                int o = i * FevLegacySidecarWriter.RowBytes;
                uint key = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(o));
                uint fsb = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(o + 4));
                list.Add(new CommonRow(i, key, fsb));
            }

            return list;
        }

        public static Dictionary<uint, int> BuildKeyToFsbIndexMap(IReadOnlyList<CommonRow> rows)
        {
            var map = new Dictionary<uint, int>();
            foreach (CommonRow row in rows)
                map[row.Key] = (int)row.FsbSampleIndex;
            return map;
        }
    }
}

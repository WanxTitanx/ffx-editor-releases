using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FFXProjectEditor.FfxLib.Audio
{
    /// <summary>Append one subsong entry to fsbext dump.dat (FSB5 rebuild oracle).</summary>
    internal static class FsbDumpDatAppender
    {
        const int HeaderSize = 0x3C;
        const uint EntryFlags = 8;

        public static bool TryAppend(string dumpPath, int newSampleIndex, int wavFileSize, out string? error)
        {
            error = null;
            if (!File.Exists(dumpPath))
            {
                error = "dump.dat missing";
                return false;
            }

            byte[] dump = File.ReadAllBytes(dumpPath);
            if (dump.Length < HeaderSize + 12)
            {
                error = "dump.dat too small";
                return false;
            }

            IReadOnlyList<int> markers = FindEntryMarkers(dump);
            if (markers.Count == 0)
            {
                error = "no dump.dat entries";
                return false;
            }

            int last = markers[^1];
            uint lastOffset = BinaryPrimitives.ReadUInt32LittleEndian(dump.AsSpan(last + 4));
            uint lastSize = BinaryPrimitives.ReadUInt32LittleEndian(dump.AsSpan(last + 8));
            uint newOffset = lastOffset + lastSize;

            byte[] entry = BuildEntry(newSampleIndex, newOffset, (uint)wavFileSize);
            var output = new byte[dump.Length + entry.Length];
            dump.AsSpan().CopyTo(output);
            entry.AsSpan().CopyTo(output.AsSpan(dump.Length));

            uint count = BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(12));
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(12), count + 1);

            File.WriteAllBytes(dumpPath, output);
            return true;
        }

        static IReadOnlyList<int> FindEntryMarkers(byte[] dump)
        {
            var markers = new List<int>();
            for (int i = HeaderSize; i <= dump.Length - 4; i++)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(dump.AsSpan(i)) == EntryFlags)
                    markers.Add(i);
            }
            return markers;
        }

        static byte[] BuildEntry(int index, uint fsbOffset, uint wavSize)
        {
            string idx = index.ToString();
            byte[] indexBytes = Encoding.ASCII.GetBytes(idx + "\0");
            byte[] nameBytes = Encoding.ASCII.GetBytes($"{idx}.wav\0");

            var entry = new byte[12 + indexBytes.Length + nameBytes.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(0), EntryFlags);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(4), fsbOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(8), wavSize);
            indexBytes.AsSpan().CopyTo(entry.AsSpan(12));
            nameBytes.AsSpan().CopyTo(entry.AsSpan(12 + indexBytes.Length));
            return entry;
        }
    }
}

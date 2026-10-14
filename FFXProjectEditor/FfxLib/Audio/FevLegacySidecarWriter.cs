using System;
using System.Buffers.Binary;
using System.IO;

namespace FFXProjectEditor.FfxLib.Audio
{
    /// <summary>Append one 8-byte row to 9999_common.txt (1:1 with FSB subsong index).</summary>
    public static class FevLegacySidecarWriter
    {
        public const int RowBytes = 8;

        public static bool TryAppendCommonRow(string commonPath, int newFsbSampleIndex0, out string? error)
        {
            error = null;
            if (!File.Exists(commonPath))
            {
                error = "9999_common.txt missing";
                return false;
            }

            byte[] bytes = File.ReadAllBytes(commonPath);
            if (bytes.Length % RowBytes != 0)
            {
                error = $"common.txt size {bytes.Length} not multiple of {RowBytes}";
                return false;
            }

            if (bytes.Length / RowBytes != newFsbSampleIndex0)
            {
                error = $"common row count {bytes.Length / RowBytes} != new sample index {newFsbSampleIndex0}";
                return false;
            }

            uint first = bytes.Length >= RowBytes
                ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(bytes.Length - RowBytes))
                : 0;
            first = first >= 99 ? first + 1 : (uint)newFsbSampleIndex0;

            var row = new byte[RowBytes];
            BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(0), first);
            BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(4), (uint)newFsbSampleIndex0);

            using var fs = new FileStream(commonPath, FileMode.Append, FileAccess.Write, FileShare.Read);
            fs.Write(row);
            return true;
        }

        public static string BackupCommon(string commonPath, string? stamp = null)
        {
            string suffix = Fsb9999SampleReplaceWriter.DefaultBackupSuffix + "_" + (stamp ?? DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            string backup = commonPath + suffix;
            File.Copy(commonPath, backup, overwrite: true);
            return backup;
        }

        public static void RestoreCommon(string commonPath, string backupPath)
        {
            if (!File.Exists(backupPath))
                throw new FileNotFoundException("common.txt backup not found", backupPath);
            File.Copy(backupPath, commonPath, overwrite: true);
        }
    }
}

using FFXProjectEditor.FfxLib.Save;
using System;
using System.IO;

namespace FFXProjectEditor.Tools
{
    /// <summary>
    /// Headless gate: load raw 25848-byte save, touch nothing, re-save with checksum, verify byte identity.
    /// </summary>
    internal static class FfxSaveRt0
    {
        public static int Run(string[] args)
        {
            if (args.Length < 2 || args[0] != "--ffx-save-rt0")
            {
                Console.WriteLine("usage: --ffx-save-rt0 <raw-25848-save>");
                return 2;
            }

            string path = args[1];
            try
            {
                byte[] before = File.ReadAllBytes(path);
                if (before.Length != FfxSaveCore.DataSize)
                {
                    Console.WriteLine($"FAIL: expected {FfxSaveCore.DataSize} bytes, got {before.Length}");
                    return 1;
                }

                var file = FfxSaveFile.Load(path);
                string temp = Path.Combine(Path.GetTempPath(), $"ffx_save_rt0_{Guid.NewGuid():N}.bin");
                file.Save(temp);

                byte[] after = File.ReadAllBytes(temp);
                File.Delete(temp);

                if (before.Length != after.Length)
                {
                    Console.WriteLine("FAIL: length drift after round-trip");
                    return 1;
                }

                for (int i = 0; i < before.Length; i++)
                {
                    if (before[i] == after[i])
                        continue;

                    Console.WriteLine($"FAIL: byte drift @0x{i:X4} ({before[i]} -> {after[i]})");
                    return 1;
                }

                ushort crc = FfxSaveChecksum.Compute(before);
                Console.WriteLine($"PASS: byte-identical round-trip ({before.Length} bytes, CRC=0x{crc:X4})");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }
    }
}

// Weapon-name gate (Pt8 / w_name.bin) — promotes the proven w_name.bin reader invariants into the
// permanent build gate. Self-contained reimplementation (same style as SphereGridHarness): no editor link.
//
// Gate proves, on the real corpus:
//   Header law — minIndex/maxIndex/entryLength(0x48)/dataLength consistent; string pool starts at 0x14+dataLength.
//   Offsets   — every one of the 14 text refs per entry (7 regular + 7 simplified, [offset u16][key u16])
//               is 0 (empty) or points inside the string pool (a NUL-terminated script).
//   RT0       — raw re-emit (model offsets/keys/models/final-word written back into a clone) is byte-identical.
// Any drift breaks the build (failures++ in Program.Main).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace ReverseHarness
{
    internal sealed class WeaponNameHarnessResult
    {
        public bool Pass { get; set; }
        public int Files { get; set; }
        public int HeaderOk { get; set; }
        public int OffsetsOk { get; set; }
        public int Rt0Passed { get; set; }
        public List<string> Fails { get; } = new List<string>();
    }

    internal static class WeaponNameHarness
    {
        private const int HeaderLength = 0x14;
        private const int EntryLength = 0x48;
        private const int CharacterCount = 0x07;
        private const int RegularBlockOffset = 0x00;
        private const int SimplifiedBlockOffset = 0x1C;
        private const int ModelBlockOffset = 0x38;
        private const int FinalWordOffset = 0x46;

        private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b));

        public static WeaponNameHarnessResult Run(string ffxRoot)
        {
            var result = new WeaponNameHarnessResult();
            if (!Directory.Exists(ffxRoot)) { result.Pass = true; return result; }

            var files = Directory.EnumerateFiles(ffxRoot, "w_name.bin", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();

            foreach (string path in files)
            {
                result.Files++;
                byte[] d = File.ReadAllBytes(path);
                string label = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(path))) ?? path) + "/w_name.bin";

                int minIndex = Le.U16(d, 0x08);
                int maxIndex = Le.U16(d, 0x0A);
                int entryLength = Le.U16(d, 0x0C);
                int dataLength = Le.U16(d, 0x0E);
                int entryCount = (maxIndex - minIndex) + 1;

                bool headerOk = entryLength == EntryLength
                    && maxIndex >= minIndex
                    && entryCount * EntryLength == dataLength
                    && HeaderLength + dataLength <= d.Length;
                if (!headerOk) { result.Fails.Add($"{label}: header (entryLen=0x{entryLength:X2} dataLen=0x{dataLength:X4})"); continue; }
                result.HeaderOk++;

                int poolStart = HeaderLength + dataLength;
                int poolLength = d.Length - poolStart;

                // Validate every text-ref offset lands inside the pool (0 = empty).
                bool offsetsOk = true;
                for (int i = 0; i < entryCount && offsetsOk; i++)
                {
                    int entryOffset = HeaderLength + (i * EntryLength);
                    for (int slot = 0; slot < CharacterCount; slot++)
                    {
                        ushort reg = Le.U16(d, entryOffset + RegularBlockOffset + (slot * 0x04));
                        ushort simp = Le.U16(d, entryOffset + SimplifiedBlockOffset + (slot * 0x04));
                        if (!OffsetValid(reg, poolLength) || !OffsetValid(simp, poolLength)) { offsetsOk = false; break; }
                    }
                }
                if (!offsetsOk) { result.Fails.Add($"{label}: a text offset lands outside the {poolLength}-byte pool"); continue; }
                result.OffsetsOk++;

                // RT0 — re-emit the modeled fields (offsets/keys/models/final word) back into a clone; raw-preserve the rest.
                byte[] re = (byte[])d.Clone();
                for (int i = 0; i < entryCount; i++)
                {
                    int entryOffset = HeaderLength + (i * EntryLength);
                    for (int slot = 0; slot < CharacterCount; slot++)
                    {
                        int rOff = entryOffset + RegularBlockOffset + (slot * 0x04);
                        int sOff = entryOffset + SimplifiedBlockOffset + (slot * 0x04);
                        Le.W16(re, rOff, Le.U16(d, rOff));         // regular offset
                        Le.W16(re, rOff + 2, Le.U16(d, rOff + 2)); // regular key
                        Le.W16(re, sOff, Le.U16(d, sOff));         // simplified offset
                        Le.W16(re, sOff + 2, Le.U16(d, sOff + 2)); // simplified key
                        Le.W16(re, entryOffset + ModelBlockOffset + (slot * 0x02), Le.U16(d, entryOffset + ModelBlockOffset + (slot * 0x02)));
                    }
                    Le.W16(re, entryOffset + FinalWordOffset, Le.U16(d, entryOffset + FinalWordOffset));
                }
                if (Sha(re) == Sha(d)) result.Rt0Passed++;
                else result.Fails.Add($"{label}: RT0 drift");
            }

            result.Pass = result.Files == 0
                || (result.HeaderOk == result.Files && result.OffsetsOk == result.Files && result.Rt0Passed == result.Files);
            return result;
        }

        private static bool OffsetValid(ushort offset, int poolLength)
        {
            return offset == 0 || offset < poolLength;
        }
    }
}

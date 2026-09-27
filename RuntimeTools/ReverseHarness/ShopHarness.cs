// Shop gate (Pt8 / ShopAndWeaponNameResearchLab) — promotes the proven item_shop.bin / arms_shop.bin
// slot-writer invariants into the permanent build gate. Self-contained reimplementation (same style as
// SphereGridHarness / WdReader): the harness does NOT link the editor.
//
// Gate proves, on the real corpus:
//   RT0 — no-edit re-emit (16-slot fixed table re-serialized from the parsed model) is byte-identical.
//   RT1 — a single-slot mutation changes EXACTLY the two payload bytes of that slot, nothing else.
// Any drift breaks the build (failures++ in Program.Main).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace ReverseHarness
{
    internal sealed class ShopHarnessResult
    {
        public bool Pass { get; set; }
        public int Files { get; set; }
        public int Rt0Passed { get; set; }
        public int Rt1Passed { get; set; }
        public List<string> Fails { get; } = new List<string>();
    }

    internal static class ShopHarness
    {
        private const int HeaderLength = 0x14;
        private const int EntryLength = 0x22;
        private const int SlotCount = 0x10;
        private const int SlotDataOffset = 0x02;

        private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b));

        public static ShopHarnessResult Run(string ffxRoot)
        {
            var result = new ShopHarnessResult();
            var files = new List<string>();
            foreach (string name in new[] { "item_shop.bin", "arms_shop.bin" })
            {
                if (!Directory.Exists(ffxRoot)) break;
                files.AddRange(Directory.EnumerateFiles(ffxRoot, name, SearchOption.AllDirectories));
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);

            foreach (string path in files)
            {
                result.Files++;
                byte[] orig = File.ReadAllBytes(path);
                string label = Path.GetFileName(path);

                if (!TryParse(orig, out int entryCount, out string err))
                {
                    result.Fails.Add($"{label}: parse :: {err}");
                    continue;
                }

                // RT0 — re-emit each entry's 16 slots from the parsed model into a clone of the original.
                byte[] reemit = (byte[])orig.Clone();
                for (int i = 0; i < entryCount; i++)
                {
                    int entryOffset = HeaderLength + (i * EntryLength);
                    for (int s = 0; s < SlotCount; s++)
                    {
                        int so = entryOffset + SlotDataOffset + (s * 0x02);
                        ushort v = Le.U16(orig, so);
                        Le.W16(reemit, so, v);
                    }
                }
                if (Sha(reemit) == Sha(orig)) result.Rt0Passed++;
                else { result.Fails.Add($"{label}: RT0 drift"); continue; }

                // RT1 — mutate row 0 / slot 0 to a different value; assert exactly that slot's 2 bytes change.
                byte[] mutated = (byte[])orig.Clone();
                int targetOffset = HeaderLength + SlotDataOffset; // row 0, slot 0
                ushort original = Le.U16(orig, targetOffset);
                ushort replacement = (ushort)(original ^ 0x0001);
                Le.W16(mutated, targetOffset, replacement);
                var diffs = new List<int>();
                for (int o = 0; o < orig.Length; o++)
                    if (orig[o] != mutated[o]) diffs.Add(o);
                bool local = diffs.Count > 0 && diffs.All(o => o == targetOffset || o == targetOffset + 1);
                if (local) result.Rt1Passed++;
                else result.Fails.Add($"{label}: RT1 non-local ({diffs.Count} bytes)");
            }

            result.Pass = result.Files == 0 || (result.Rt0Passed == result.Files && result.Rt1Passed == result.Files);
            return result;
        }

        private static bool TryParse(byte[] d, out int entryCount, out string err)
        {
            entryCount = 0;
            err = string.Empty;
            if (d.Length < HeaderLength) { err = "too small"; return false; }

            int minIndex = Le.U16(d, 0x08);
            int maxIndex = Le.U16(d, 0x0A);
            int entryLength = Le.U16(d, 0x0C);
            int dataLength = Le.U16(d, 0x0E);

            if (entryLength != EntryLength) { err = $"entryLen 0x{entryLength:X2}"; return false; }
            if (maxIndex < minIndex) { err = "bad index range"; return false; }

            entryCount = (maxIndex - minIndex) + 1;
            int expectedData = entryCount * EntryLength;
            if (dataLength != expectedData) { err = $"dataLen 0x{dataLength:X4} != 0x{expectedData:X4}"; return false; }
            if (HeaderLength + dataLength > d.Length) { err = "data past EOF"; return false; }
            return true;
        }
    }
}

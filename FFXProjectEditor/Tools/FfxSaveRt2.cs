using FFXProjectEditor.FfxLib.Save;
using System;
using System.IO;
using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Tools
{
    /// <summary>
    /// RT2: load real container (.psu / .ps2 / raw / .ffx), save back, verify 25848-byte payload identity.
    /// </summary>
    internal static class FfxSaveRt2
    {
        public static int Run(string[] args)
        {
            if (args.Length < 2 || args[0] != "--ffx-save-rt2")
            {
                Console.WriteLine("usage: --ffx-save-rt2 <save-path> [mc-slot-index]");
                Console.WriteLine("       --ffx-save-rt2 --self-test");
                return 2;
            }

            if (args[1] == "--self-test")
                return RunSelfTest();

            if (!int.TryParse(args.Length > 2 ? args[2] : "0", out int mcSlot))
                mcSlot = 0;

            return RunFile(args[1], mcSlot) ? 0 : 1;
        }

        static int RunSelfTest()
        {
            int passed = 0, failed = 0;
            string work = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "work", "save_editor_rt2");
            work = Path.GetFullPath(work);
            Directory.CreateDirectory(work);

            byte[] payloadA = FfxSaveMemoryCardFixture.CreateMinimalPayload("Tidus RT2 A");
            byte[] payloadB = FfxSaveMemoryCardFixture.CreateMinimalPayload("Yuna RT2 B");

            string rawPath = Path.Combine(work, "minimal_25848.bin");
            File.WriteAllBytes(rawPath, payloadA);
            if (RunFile(rawPath, 0)) { passed++; Console.WriteLine("PASS raw 25848"); }
            else { failed++; Console.WriteLine("FAIL raw 25848"); }

            string ps2Single = Path.Combine(work, "single_slot.ps2");
            FfxSaveMemoryCardFixture.WritePs2WithSlots(ps2Single, (payloadA, Strings.F2_slot_a_0912b3b7));
            if (RunFile(ps2Single, 0)) { passed++; Console.WriteLine("PASS .ps2 single slot"); }
            else { failed++; Console.WriteLine("FAIL .ps2 single slot"); }

            string ps2Dual = Path.Combine(work, "dual_slot.ps2");
            FfxSaveMemoryCardFixture.WritePs2WithSlots(ps2Dual, (payloadA, Strings.F2_slot_a_0912b3b7), (payloadB, "Slot B"));
            if (RunFile(ps2Dual, 0)) { passed++; Console.WriteLine("PASS .ps2 slot 0"); }
            else { failed++; Console.WriteLine("FAIL .ps2 slot 0"); }
            if (RunFile(ps2Dual, 1)) { passed++; Console.WriteLine("PASS .ps2 slot 1"); }
            else { failed++; Console.WriteLine("FAIL .ps2 slot 1"); }

            var listed = FfxSaveMemoryCard.ListSlots(ps2Dual);
            string labels = string.Join(", ", listed.Select(s => s.Label));
            Console.WriteLine($"INFO dual_slot.ps2 scan: {listed.Count} slot(s) — {labels}");

            string ffxPath = Path.Combine(work, "minimal.ffx");
            using (var fs = new FileStream(ffxPath, FileMode.Create))
            {
                fs.Write(new byte[FfxSaveFile.PcFfxHeaderSize]);
                fs.Write(payloadA);
                fs.Write(new byte[FfxSaveFile.PcFfxFooterSize]);
            }

            if (RunFile(ffxPath, 0)) { passed++; Console.WriteLine("PASS PC .ffx"); }
            else { failed++; Console.WriteLine("FAIL PC .ffx"); }

            Console.WriteLine($"RT2 self-test: {passed} passed, {failed} failed");
            return failed == 0 ? 0 : 1;
        }

        static bool RunFile(string path, int mcSlotIndex)
        {
            try
            {
                if (!File.Exists(path))
                {
                    Console.WriteLine($"FAIL: file not found: {path}");
                    return false;
                }

                FfxSaveFile session = LoadSession(path, mcSlotIndex);
                byte[] beforePayload = (byte[])session.Core.Data.Clone();
                byte[]? pcFooter = null;
                if (session.Format == FfxSaveFormat.PcFfx && new FileInfo(path).Length >= FfxSaveFile.PcFfxHeaderSize + FfxSaveCore.DataSize + FfxSaveFile.PcFfxFooterSize)
                {
                    pcFooter = File.ReadAllBytes(path).AsSpan(FfxSaveFile.PcFfxHeaderSize + FfxSaveCore.DataSize, FfxSaveFile.PcFfxFooterSize).ToArray();
                }

                string temp = Path.Combine(Path.GetTempPath(), $"ffx_save_rt2_{Guid.NewGuid():N}{Path.GetExtension(path)}");
                session.Save(temp);

                FfxSaveFile roundTrip = LoadSession(temp, mcSlotIndex);
                byte[] afterPayload = roundTrip.Core.Data;

                // PrepareForSave always rewrites CRC/tamper tag — compare normalized payloads.
                var expected = new FfxSaveCore(beforePayload);
                expected.PrepareForSave();

                if (!expected.Data.AsSpan().SequenceEqual(afterPayload))
                {
                    for (int i = 0; i < expected.Data.Length; i++)
                    {
                        if (expected.Data[i] == afterPayload[i])
                            continue;
                        Console.WriteLine($"FAIL payload drift @0x{i:X4}: {expected.Data[i]} -> {afterPayload[i]} ({path})");
                        return false;
                    }
                }

                if (session.Format == FfxSaveFormat.PcFfx && pcFooter != null)
                {
                    byte[] afterFile = File.ReadAllBytes(temp);
                    int footerStart = FfxSaveFile.PcFfxHeaderSize + FfxSaveCore.DataSize;
                    if (afterFile.Length < footerStart + FfxSaveFile.PcFfxFooterSize)
                    {
                        Console.WriteLine("FAIL PC .ffx footer missing after round-trip");
                        return false;
                    }

                    if (!afterFile.AsSpan(footerStart, FfxSaveFile.PcFfxFooterSize).SequenceEqual(pcFooter))
                    {
                        Console.WriteLine("FAIL PC .ffx 1032-byte footer drift");
                        return false;
                    }
                }

                if (session.Format == FfxSaveFormat.MemoryCard)
                {
                    byte[] beforeCard = File.ReadAllBytes(path);
                    byte[] afterCard = File.ReadAllBytes(temp);
                    if (beforeCard.Length != afterCard.Length)
                    {
                        Console.WriteLine($"FAIL MC size drift ({beforeCard.Length} -> {afterCard.Length})");
                        return false;
                    }

                    long offset = session.PsuLayout?.PayloadOffset ?? -1;
                    for (int i = 0; i < beforeCard.Length; i++)
                    {
                        if (offset >= 0 && i >= offset && i < offset + FfxSaveCore.DataSize)
                            continue;
                        if (beforeCard[i] != afterCard[i])
                        {
                            Console.WriteLine($"FAIL MC metadata drift @0x{i:X5}");
                            return false;
                        }
                    }
                }

                ushort crc = FfxSaveChecksum.Compute(afterPayload);
                Console.WriteLine($"PASS RT2 {Path.GetFileName(path)} ({session.Format}, slot {mcSlotIndex}, CRC=0x{crc:X4})");
                try { File.Delete(temp); } catch { /* ignore */ }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR RT2 {path}: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        static FfxSaveFile LoadSession(string path, int mcSlotIndex)
        {
            if (path.EndsWith(".ps2", StringComparison.OrdinalIgnoreCase)
                && new FileInfo(path).Length == FfxSaveMemoryCard.CardSize)
            {
                var slots = FfxSaveMemoryCard.ListSlots(path);
                if (slots.Count == 0)
                    throw new InvalidDataException(Strings.F2_no_ffx_slots_in_ps2_e8cafb42);
                int idx = Math.Clamp(mcSlotIndex, 0, slots.Count - 1);
                return FfxSaveMemoryCard.LoadSlot(path, slots[idx]);
            }

            return FfxSaveFile.Load(path);
        }
    }
}

using FFXProjectEditor.FfxLib.Ps3;
using System;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Tools
{
    internal static class Ps3MagicPhyreRepackRt0
    {
        const string DefaultMagicRoot = @"D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data\magic";
        const string PreferredFixture = @"D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data\magic\magic_0003\tex\d3d11\13312_19_0_0_256_128.dds.phyre";

        public static int Run(string[] args)
        {
            try
            {
                string sourcePath = args.Length > 1 ? args[1] : ResolveDefaultSource();
                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                {
                    Console.WriteLine("FAIL: source .dds.phyre not found.");
                    Console.WriteLine("Usage: --ps3magic-phyre-repack-rt0 [source.dds.phyre] [mip0_payload.bin] [output.dds.phyre]");
                    return 1;
                }

                if (!Ps3MagicTextureWriter.TryReadMip0Layout(sourcePath, out Ps3PhyreMip0Layout? layout, out string note) || layout == null)
                {
                    Console.WriteLine($"FAIL: source layout unreadable: {note}");
                    return 1;
                }

                if (args.Length > 2)
                {
                    string payloadPath = args[2];
                    string outputPath = args.Length > 3
                        ? args[3]
                        : Path.Combine("work", "ps3magic_phyrepkg_rt0", Path.GetFileName(sourcePath));

                    byte[] payload = Ps3MagicTextureWriter.ReadCompatibleMip0Payload(payloadPath, layout, out string payloadKind);
                    Ps3PhyreWriteResult authored = Ps3MagicTextureWriter.WriteSameShapeMip0(sourcePath, payload, outputPath);
                    Console.WriteLine(authored.SameLayout
                        ? "PASS: authored payload repacked with preserved Phyre layout."
                        : "FAIL: authored output layout drifted.");
                    Console.WriteLine($"source: {sourcePath}");
                    Console.WriteLine($"payload: {payloadPath} ({payloadKind})");
                    Console.WriteLine($"output: {outputPath}");
                    Console.WriteLine($"layout: {authored.OutputLayout.Summary}");
                    return authored.SameLayout ? 0 : 1;
                }

                return RunSelfTest(sourcePath, layout);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static int RunSelfTest(string sourcePath, Ps3PhyreMip0Layout layout)
        {
            string workDir = Path.Combine("work", "ps3magic_phyrepkg_rt0", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(workDir);

            string noEditPath = Path.Combine(workDir, "noedit_" + Path.GetFileName(sourcePath));
            string mutatedPath = Path.Combine(workDir, "mutated_mip0_" + Path.GetFileName(sourcePath));
            string extractedMip0Path = Path.Combine(workDir, Path.GetFileName(sourcePath) + ".mip0.bin");

            byte[] originalFile = File.ReadAllBytes(sourcePath);
            byte[] mip0 = Ps3MagicTextureWriter.ReadMip0Payload(sourcePath);
            File.WriteAllBytes(extractedMip0Path, mip0);

            Ps3PhyreWriteResult noEdit = Ps3MagicTextureWriter.WriteSameShapeMip0(sourcePath, mip0, noEditPath);
            bool noEditByteIdentical = originalFile.SequenceEqual(File.ReadAllBytes(noEditPath));

            byte[] mutatedMip0 = (byte[])mip0.Clone();
            MutatePayload(mutatedMip0);
            Ps3PhyreWriteResult mutated = Ps3MagicTextureWriter.WriteSameShapeMip0(sourcePath, mutatedMip0, mutatedPath);

            byte[] mutatedFile = File.ReadAllBytes(mutatedPath);
            DiffSummary diff = CountDiffs(originalFile, mutatedFile, layout.BufferStart, layout.Mip0Size);

            bool ok = noEdit.SameLayout
                && noEditByteIdentical
                && mutated.SameLayout
                && diff.Total > 0
                && diff.OutsideMip0 == 0;

            Console.WriteLine(ok
                ? "PASS: ps3 magic .dds.phyre same-shape repack gate passed."
                : "FAIL: ps3 magic .dds.phyre repack gate failed.");
            Console.WriteLine($"source: {sourcePath}");
            Console.WriteLine($"layout: {layout.Summary}");
            Console.WriteLine($"mip0 extracted: {extractedMip0Path}");
            Console.WriteLine($"no-edit output: {noEditPath}");
            Console.WriteLine($"no-edit byte-identical: {noEditByteIdentical}");
            Console.WriteLine($"mutated output: {mutatedPath}");
            Console.WriteLine($"mutated same layout: {mutated.SameLayout}");
            Console.WriteLine($"mutated diff total={diff.Total}, inMip0={diff.InMip0}, outsideMip0={diff.OutsideMip0}, first=0x{diff.First:X}, last=0x{diff.Last:X}");
            Console.WriteLine("Scope: this proves conservative mip0 payload replacement only; it is not a magic timeline/compiler proof.");
            return ok ? 0 : 1;
        }

        static string ResolveDefaultSource()
        {
            if (File.Exists(PreferredFixture)
                && Ps3MagicTextureWriter.TryReadMip0Layout(PreferredFixture, out _, out _))
            {
                return PreferredFixture;
            }

            if (!Directory.Exists(DefaultMagicRoot))
                return string.Empty;

            foreach (string file in Directory.EnumerateFiles(DefaultMagicRoot, "*.dds.phyre", SearchOption.AllDirectories))
            {
                if (Ps3MagicTextureWriter.TryReadMip0Layout(file, out _, out _))
                    return file;
            }

            return string.Empty;
        }

        static void MutatePayload(byte[] payload)
        {
            int count = Math.Min(payload.Length, 64);
            for (int i = 0; i < count; i++)
                payload[i] ^= (byte)(0x31 + (i * 17));
        }

        static DiffSummary CountDiffs(byte[] a, byte[] b, int mip0Start, int mip0Size)
        {
            int total = 0;
            int inMip0 = 0;
            int outside = 0;
            int first = -1;
            int last = -1;
            int max = Math.Max(a.Length, b.Length);
            int mip0End = mip0Start + mip0Size;
            for (int i = 0; i < max; i++)
            {
                byte av = i < a.Length ? a[i] : (byte)0;
                byte bv = i < b.Length ? b[i] : (byte)0;
                if (av == bv)
                    continue;

                total++;
                if (first < 0)
                    first = i;
                last = i;
                if (i >= mip0Start && i < mip0End)
                    inMip0++;
                else
                    outside++;
            }

            return new DiffSummary(total, inMip0, outside, first, last);
        }

        readonly record struct DiffSummary(int Total, int InMip0, int OutsideMip0, int First, int Last);
    }
}

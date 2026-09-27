using FFXProjectEditor.FfxLib.Ps3;
using System;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Tools
{
    internal static class PhyrePackageIoRt0
    {
        const string DefaultDds = @"D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data\magic\magic_0714\tex\d3d11\13312_19_0_0_128_64.dds.phyre";
        const string FallbackDds = @"D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data\magic\magic_0082\tex\d3d11\13312_19_0_0_128_64.dds.phyre";
        const string DefaultDae = @"D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data\chr\mon\m020\mdl\d3d11\m020.dae.phyre";
        const string Ps3DataRoot = @"D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data";

        public static int Run(string[] args)
        {
            try
            {
                string dds = args.Length > 1 ? args[1] : ResolveDds();
                string dae = args.Length > 2 ? args[2] : ResolveDae();
                string outputDir = args.Length > 3 ? args[3] : Path.Combine("work", "phyre_package_io_rt0");

                Console.WriteLine("=== Phyre Package I/O RT0 ===");
                Console.WriteLine($"dds source: {dds}");
                Console.WriteLine($"dae source: {dae}");
                Console.WriteLine($"output    : {outputDir}");

                if (!File.Exists(dds))
                {
                    Console.WriteLine("FAIL: .dds.phyre fixture not found.");
                    return 2;
                }
                if (!File.Exists(dae))
                {
                    Console.WriteLine("FAIL: .dae.phyre fixture not found.");
                    return 2;
                }

                Directory.CreateDirectory(outputDir);
                Ps3PhyrePackageInspection ddsInspection = Ps3PhyrePackageIo.Inspect(dds);
                Ps3PhyrePackageInspection daeInspection = Ps3PhyrePackageIo.Inspect(dae);
                Console.WriteLine($"dds inspect: {ddsInspection.Summary}");
                Console.WriteLine($"dae inspect: {daeInspection.Summary}");

                string extractDir = Path.Combine(outputDir, "extract");
                Ps3PhyrePackageExtractResult ddsExtract = Ps3PhyrePackageIo.ExtractPackage(dds, extractDir);
                Ps3PhyrePackageExtractResult daeExtract = Ps3PhyrePackageIo.ExtractPackage(dae, extractDir);

                string ddsRepack = Path.Combine(outputDir, "dds_repack.dds.phyre");
                Ps3PhyrePackageImportResult ddsImport = Ps3PhyrePackageIo.ImportDdsPayload(dds, ddsExtract.ExtractedDdsPath!, ddsRepack);
                bool ddsNoEdit = File.ReadAllBytes(dds).SequenceEqual(File.ReadAllBytes(ddsRepack));

                string daeRepack = Path.Combine(outputDir, "dae_repack.dae.phyre");
                Ps3PhyrePackageImportResult daeImport = Ps3PhyrePackageIo.ImportCompiledPackage(dae, dae, daeRepack);
                bool daeNoEdit = File.ReadAllBytes(dae).SequenceEqual(File.ReadAllBytes(daeRepack));

                Console.WriteLine($"dds extract dir: {ddsExtract.OutputDirectory}");
                Console.WriteLine($"dae extract dir: {daeExtract.OutputDirectory}");
                Console.WriteLine($"dds import    : {ddsImport.OutputPath} noEdit={ddsNoEdit}");
                Console.WriteLine($"dae import    : {daeImport.OutputPath} noEdit={daeNoEdit}");

                bool pass = ddsInspection.Kind == Ps3PhyrePackageKind.DdsTexture
                    && daeInspection.Kind == Ps3PhyrePackageKind.DaeModel
                    && File.Exists(ddsExtract.ManifestPath)
                    && File.Exists(daeExtract.ManifestPath)
                    && File.Exists(ddsExtract.ExtractedDdsPath)
                    && ddsNoEdit
                    && daeNoEdit;

                Console.WriteLine(pass
                    ? "VERDICT: PASS - native Phyre I/O extracted DDS/DAE manifests and repacked DDS payload + compiled DAE package byte-identically."
                    : "VERDICT: FAIL - Phyre I/O roundtrip did not meet required guards.");
                return pass ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static string ResolveDds()
        {
            if (File.Exists(DefaultDds))
                return DefaultDds;
            if (File.Exists(FallbackDds))
                return FallbackDds;
            return Directory.Exists(Ps3DataRoot)
                ? Directory.EnumerateFiles(Ps3DataRoot, "*.dds.phyre", SearchOption.AllDirectories).FirstOrDefault() ?? string.Empty
                : string.Empty;
        }

        static string ResolveDae()
        {
            if (File.Exists(DefaultDae))
                return DefaultDae;
            return Directory.Exists(Ps3DataRoot)
                ? Directory.EnumerateFiles(Ps3DataRoot, "*.dae.phyre", SearchOption.AllDirectories).FirstOrDefault() ?? string.Empty
                : string.Empty;
        }
    }
}

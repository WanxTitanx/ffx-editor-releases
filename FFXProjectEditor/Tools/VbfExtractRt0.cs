using FFXProjectEditor.Services.Extras;
using System;

namespace FFXProjectEditor.Tools
{
    internal static class VbfExtractRt0
    {
        public static int Run(string[] args)
        {
            if (args.Length > 4)
            {
                VbfExtractProbe customProbe = VbfExtract_Service.BuildProbe(
                    "custom",
                    args[1],
                    args[2],
                    args[3],
                    args[4]);

                return Report(customProbe);
            }

            string label = args.Length > 1 ? args[1] : "FFX_Data";
            VbfExtractProbe probe = label.ToLowerInvariant() switch
            {
                "ffx2" or "ffx2_data" => VbfExtract_Service.DetectFfx2(),
                "metamenu" => VbfExtract_Service.DetectMetaMenu(),
                _ => VbfExtract_Service.DetectFfx()
            };

            return Report(probe);
        }

        static int Report(VbfExtractProbe probe)
        {
            Console.WriteLine("[VbfExtractRt0] " + probe.Summary);
            Console.WriteLine("[VbfExtractRt0] command=" + VbfExtract_Service.BuildCommandPreview(probe));
            Console.WriteLine("[VbfExtractRt0] note=extract-only; no VBF repack path is tested or exposed.");

            bool pass = probe.Ready && probe.DictionaryLineCount > 0 && probe.VbfSize > 1024 * 1024;
            Console.WriteLine(pass ? "PASS" : "FAIL");
            return pass ? 0 : 1;
        }
    }
}

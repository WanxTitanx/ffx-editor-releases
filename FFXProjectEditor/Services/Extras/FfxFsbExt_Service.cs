using FFXProjectEditor.Services.Tools;
using System;
using System.Collections.Generic;
using System.IO;

namespace FFXProjectEditor.Services.Extras
{
    /// <summary>aluigi fsbext oracle — extract/rebuild FSB4 banks.</summary>
    public static class FfxFsbExt_Service
    {
        public const string WavSubdirName = "wav";
        public const string ScratchSubdirName = "scratch";
        public const string DumpFileName = "dump.dat";

        public static bool IsAvailable => FfxAudioToolsLocator.FsbExtAvailable;

        public static string WavDir(string workDir) => Path.Combine(workDir, WavSubdirName);

        public static string ScratchDir(string workDir) => Path.Combine(workDir, ScratchSubdirName);

        static ToolRunResult Run(string workingDir, IReadOnlyList<string> arguments, int timeoutMs = 300000)
        {
            string? cli = FfxAudioToolsLocator.LocateFsbExt();
            if (cli == null)
                return new ToolRunResult(ToolRunStatus.StartFailed, -1, "", "", "fsbext not found on this platform");

            return BoundedToolProcessRunner.Run(new ToolRunRequest(
                cli, arguments, WorkingDirectory: workingDir, TimeoutMs: timeoutMs));
        }

        public static (bool Ok, string Message, string WorkDir) Extract(string fsbPath, string workDir)
        {
            Directory.CreateDirectory(workDir);
            Directory.CreateDirectory(WavDir(workDir));

            string fsbCopy = Path.Combine(workDir, Path.GetFileName(fsbPath));
            File.Copy(fsbPath, fsbCopy, overwrite: true);
            string datPath = Path.Combine(workDir, DumpFileName);

            // -d wav: extracted subsongs land in work/wav/ (not the work root)
            // -A: IMA ADPCM tag for vgmstream/VLC compatibility
            ToolRunResult r = Run(
                workDir,
                ["-d", WavSubdirName, "-A", "-s", DumpFileName, Path.GetFileName(fsbCopy)]);
            if (!File.Exists(datPath))
                return (false, $"fsbext extract failed: {Trim(r.StdErrTail, r.StdOutTail)}", workDir);
            return (true, "Extracted", workDir);
        }

        public static (bool Ok, string Message) Rebuild(string workDir, string datFileName, string outFsbFileName)
        {
            string datPath = Path.Combine(workDir, datFileName);
            if (!File.Exists(datPath))
                return (false, $"Missing {datFileName}");

            ToolRunResult r = Run(workDir, ["-d", WavSubdirName, "-s", datFileName, "-r", outFsbFileName]);
            string outPath = Path.Combine(workDir, outFsbFileName);
            if (!File.Exists(outPath))
                return (false, $"fsbext rebuild failed: {Trim(r.StdErrTail, r.StdOutTail)}");
            return (true, outPath);
        }

        public static (bool Ok, string Message) IdentityRoundTrip(string fsbPath, string tempRoot)
        {
            string work = Path.Combine(tempRoot, $"fsb_rt0_{Guid.NewGuid():N}");
            try
            {
                (bool ok, string msg, _) = Extract(fsbPath, work);
                if (!ok) return (false, msg);

                (bool rb, string rbMsg) = Rebuild(work, DumpFileName, "roundtrip.fsb");
                if (!rb) return (false, rbMsg);

                byte[] a = File.ReadAllBytes(fsbPath);
                byte[] b = File.ReadAllBytes(Path.Combine(work, "roundtrip.fsb"));
                if (a.Length != b.Length)
                    return (false, $"Size drift: {a.Length} vs {b.Length}");
                return (true, "RT0 identity round-trip OK");
            }
            finally
            {
                try { if (Directory.Exists(work)) Directory.Delete(work, recursive: true); } catch { }
            }
        }

        static string Trim(string a, string b)
        {
            string s = string.IsNullOrWhiteSpace(a) ? b : a;
            if (string.IsNullOrWhiteSpace(s)) return "fsbext error";
            int nl = s.IndexOf('\n');
            return (nl > 0 ? s[..nl] : s).Trim();
        }
    }
}

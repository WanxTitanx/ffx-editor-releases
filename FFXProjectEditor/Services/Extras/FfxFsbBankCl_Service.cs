using FFXProjectEditor.FfxLib.Audio;
using FFXProjectEditor.Services.Tools;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace FFXProjectEditor.Services.Extras
{
    /// <summary>FMOD FSBankEx CLI (fsbankexcl / fsbankcl) — FSB rebuild fallback when fsbext append fails.</summary>
    public static class FfxFsbBankCl_Service
    {
        public static bool IsAvailable => Locate() != null;

        public static string? Locate()
        {
            return FfxAudioToolsLocator.LocateFsbankCl();
        }

        static ToolRunResult Run(IReadOnlyList<string> arguments, int timeoutMs = 300000)
        {
            string? cli = Locate();
            if (cli == null)
                return new ToolRunResult(ToolRunStatus.StartFailed, -1, "", "", "fsbankcl not found on this platform");

            return BoundedToolProcessRunner.Run(new ToolRunRequest(
                cli, arguments,
                WorkingDirectory: FfxAudioToolsLocator.ToolDirectory(cli),
                TimeoutMs: timeoutMs));
        }

        public static Fsb9999SampleAppendWriter.AppendResult TryAppendSample(
            string sourceFsb,
            string newWav,
            string workDir,
            bool dryRun)
        {
            string? cli = Locate();
            if (cli == null)
                return new Fsb9999SampleAppendWriter.AppendResult(false, "fsbankcl not found", null, -1, "fsbankcl");

            if (!FfxFsbExt_Service.IsAvailable)
                return new Fsb9999SampleAppendWriter.AppendResult(false, "fsbext required to extract bank before fsbankcl rebuild", null, -1, "fsbankcl");

            int? before = FfxFsbVgmStream_Service.TryCountSubsongs(sourceFsb);
            if (!before.HasValue)
                return new Fsb9999SampleAppendWriter.AppendResult(false, "subsong count unavailable", null, -1, "fsbankcl");

            Directory.CreateDirectory(workDir);
            string scratch = FfxFsbExt_Service.ScratchDir(workDir);
            Directory.CreateDirectory(scratch);
            string refWav = Path.Combine(scratch, "format_ref.wav");
            (bool decOk, string decMsg) = FfxFsbVgmStream_Service.ExportSubsong(sourceFsb, 0, refWav);
            if (!decOk)
                return new Fsb9999SampleAppendWriter.AppendResult(false, decMsg, null, -1, "fsbankcl");

            (bool valOk, string valMsg) = FfxFsbVgmStream_Service.ValidateReplacement(refWav, newWav);
            if (!valOk)
                return new Fsb9999SampleAppendWriter.AppendResult(false, valMsg, null, -1, "fsbankcl");

            if (dryRun)
                return new Fsb9999SampleAppendWriter.AppendResult(true, $"Dry-run OK: fsbankcl would append as index {before.Value}", null, before.Value, "fsbankcl");

            string wavDir = FfxFsbExt_Service.WavDir(workDir);
            if (Directory.Exists(wavDir))
            {
                try { Directory.Delete(wavDir, recursive: true); } catch { }
            }

            (bool exOk, string exMsg, _) = FfxFsbExt_Service.Extract(sourceFsb, workDir);
            if (!exOk)
                return new Fsb9999SampleAppendWriter.AppendResult(false, exMsg, null, -1, "fsbankcl");

            wavDir = FfxFsbExt_Service.WavDir(workDir);
            string destWav = Path.Combine(wavDir, $"{before.Value}.wav");
            File.Copy(newWav, destWav, overwrite: true);

            string[] wavFiles = Directory.Exists(wavDir)
                ? Directory.GetFiles(wavDir, "*.wav", SearchOption.TopDirectoryOnly)
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : [];

            if (wavFiles.Length <= before.Value)
                return new Fsb9999SampleAppendWriter.AppendResult(false, "WAV list missing new subsong", null, -1, "fsbankcl");

            string lstPath = Path.Combine(workDir, "fsbank_append.lst");
            WriteLstFile(lstPath, wavFiles);

            string outFsb = Path.Combine(workDir, "fsbank_appended.fsb");
            if (File.Exists(outFsb))
                File.Delete(outFsb);

            ToolRunResult run = Run(["-o", outFsb, "-format", "pcm", "-rebuild", "-verbosity", "2", lstPath]);
            if (!File.Exists(outFsb))
            {
                string err = TrimErr(run.StdErrTail, run.StdOutTail);
                return new Fsb9999SampleAppendWriter.AppendResult(false, $"fsbankcl rebuild failed: {err}", null, -1, "fsbankcl");
            }

            int? after = FfxFsbVgmStream_Service.TryCountSubsongs(outFsb);
            if (!after.HasValue || after.Value <= before.Value)
                return new Fsb9999SampleAppendWriter.AppendResult(
                    false,
                    $"fsbankcl rebuild did not increase subsong count ({before} → {after})",
                    outFsb,
                    before.Value,
                    "fsbankcl");

            return new Fsb9999SampleAppendWriter.AppendResult(
                true,
                $"Appended sample index {before.Value} via fsbankcl",
                outFsb,
                before.Value,
                "fsbankcl");
        }

        static void WriteLstFile(string lstPath, string[] wavFiles)
        {
            var sb = new StringBuilder();
            foreach (string wav in wavFiles)
                sb.AppendLine(wav);
            File.WriteAllText(lstPath, sb.ToString(), Encoding.ASCII);
        }

        static string TrimErr(string stderr, string stdout)
        {
            string s = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            if (string.IsNullOrWhiteSpace(s))
                return "unknown error";
            int nl = s.IndexOf('\n');
            return (nl > 0 ? s[..nl] : s).Trim();
        }
    }
}

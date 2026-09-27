using FFXProjectEditor.Services.Extras;
using System;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Audio
{
    /// <summary>
    /// Replaces one subsong/sample inside FMOD FSB4 bank (e.g. 9999_bank00.fsb) via fsbext oracle.
    /// FEV unchanged when sample index stays the same.
    /// </summary>
    public static class Fsb9999SampleReplaceWriter
    {
        public const string DefaultBackupSuffix = ".backup_custom_sfx";

        public sealed record ReplaceRequest(
            string SourceFsbPath,
            int SampleIndex0Based,
            string ReplacementWavPath,
            string? WorkDir = null);

        public sealed record ReplaceResult(
            bool Ok,
            string Message,
            string? OutputFsbPath,
            string? BackupPath,
            string? WorkDir);

        public static ReplaceResult ReplaceSample(ReplaceRequest request, bool dryRun = false)
        {
            if (!File.Exists(request.SourceFsbPath))
                return new ReplaceResult(false, "FSB not found", null, null, null);
            if (!File.Exists(request.ReplacementWavPath))
                return new ReplaceResult(false, "Replacement WAV not found", null, null, null);
            if (!FfxFsbExt_Service.IsAvailable)
                return new ReplaceResult(false, "fsbext not found — run scripts/bootstrap_fsb_audio_tools.ps1", null, null, null);
            if (!FfxFsbVgmStream_Service.IsAvailable)
                return new ReplaceResult(false, "vgmstream not found — run scripts/bootstrap_fsb_audio_tools.ps1", null, null, null);

            string work = request.WorkDir ?? Path.Combine(Path.GetTempPath(), $"fsb_replace_{Guid.NewGuid():N}");
            Directory.CreateDirectory(work);
            string scratch = FfxFsbExt_Service.ScratchDir(work);
            Directory.CreateDirectory(scratch);

            string originalWav = Path.Combine(scratch, $"orig_{request.SampleIndex0Based:D4}.wav");
            (bool decOk, string decMsg) = FfxFsbVgmStream_Service.ExportSubsong(
                request.SourceFsbPath, request.SampleIndex0Based, originalWav);
            if (!decOk)
                return new ReplaceResult(false, $"Could not decode original sample: {decMsg}", null, null, work);

            (bool valOk, string valMsg) = FfxFsbVgmStream_Service.ValidateReplacement(originalWav, request.ReplacementWavPath);
            if (!valOk)
                return new ReplaceResult(false, valMsg, null, null, work);

            if (dryRun)
                return new ReplaceResult(true, $"Dry-run OK: {valMsg}", null, null, work);

            (bool exOk, string exMsg, _) = FfxFsbExt_Service.Extract(request.SourceFsbPath, work);
            if (!exOk)
                return new ReplaceResult(false, exMsg, null, null, work);

            // fsbext dumps subsongs into work/wav/
            string wavDir = FfxFsbExt_Service.WavDir(work);
            string[] extractedWavs = Directory.Exists(wavDir)
                ? Directory.GetFiles(wavDir, "*.wav", SearchOption.TopDirectoryOnly)
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : [];

            string targetWav;
            if (request.SampleIndex0Based < extractedWavs.Length)
                targetWav = extractedWavs[request.SampleIndex0Based];
            else
            {
                // fallback: replace the decoded original filename pattern
                targetWav = Path.Combine(wavDir, Path.GetFileName(originalWav));
                if (!extractedWavs.Contains(targetWav) && extractedWavs.Length > 0)
                    targetWav = extractedWavs[Math.Min(request.SampleIndex0Based, extractedWavs.Length - 1)];
            }

            File.Copy(request.ReplacementWavPath, targetWav, overwrite: true);

            (bool rbOk, string rbMsg) = FfxFsbExt_Service.Rebuild(work, FfxFsbExt_Service.DumpFileName, "rebuilt.fsb");
            if (!rbOk)
                return new ReplaceResult(false, rbMsg, null, null, work);

            string outFsb = Path.Combine(work, "rebuilt.fsb");
            return new ReplaceResult(true, "Sample replaced in FSB", outFsb, null, work);
        }

        public static string BackupFsb(string fsbPath, string? stamp = null)
        {
            string suffix = DefaultBackupSuffix + "_" + (stamp ?? DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            string backup = fsbPath + suffix;
            File.Copy(fsbPath, backup, overwrite: true);
            return backup;
        }

        public static void RestoreFsb(string fsbPath, string backupPath)
        {
            if (!File.Exists(backupPath))
                throw new FileNotFoundException("FSB backup not found", backupPath);
            File.Copy(backupPath, fsbPath, overwrite: true);
        }

        public static ReplaceResult RoundTripIdentity(string fsbPath, string tempRoot)
        {
            if (!FfxFsbExt_Service.IsAvailable)
                return new ReplaceResult(false, "fsbext missing", null, null, null);

            (bool ok, string msg) = FfxFsbExt_Service.IdentityRoundTrip(fsbPath, tempRoot);
            return new ReplaceResult(ok, msg, null, null, null);
        }
    }
}

using FFXProjectEditor.Services.Extras;
using System;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Audio
{
    /// <summary>Append a new subsong to FSB4 bank without replacing existing samples.</summary>
    public static class Fsb9999SampleAppendWriter
    {
        public sealed record AppendRequest(string SourceFsbPath, string NewWavPath, string? WorkDir = null);

        public sealed record AppendResult(
            bool Ok,
            string Message,
            string? OutputFsbPath,
            int NewSampleIndex0,
            string? Method);

        public static AppendResult AppendSample(AppendRequest request, bool dryRun = false)
        {
            if (!File.Exists(request.SourceFsbPath))
                return new AppendResult(false, "FSB not found", null, -1, null);
            if (!File.Exists(request.NewWavPath))
                return new AppendResult(false, "WAV not found", null, -1, null);
            if (!FfxAudioToolsLocator.CustomSfxToolsReady)
                return new AppendResult(false, "Install audio tools (bootstrap_fsb_audio_tools.ps1)", null, -1, null);

            int? before = FfxFsbVgmStream_Service.TryCountSubsongs(request.SourceFsbPath);
            if (!before.HasValue)
                return new AppendResult(false, "Could not count subsongs before append", null, -1, null);

            string work = request.WorkDir ?? Path.Combine(Path.GetTempPath(), $"fsb_append_{Guid.NewGuid():N}");
            Directory.CreateDirectory(work);

            AppendResult fsbextTry = TryFsbExtAppend(request, work, before.Value, dryRun);
            if (fsbextTry.Ok || dryRun)
                return fsbextTry;

            if (FfxFsbBankCl_Service.IsAvailable)
            {
                AppendResult bankCl = FfxFsbBankCl_Service.TryAppendSample(request.SourceFsbPath, request.NewWavPath, work, dryRun);
                if (bankCl.Ok)
                    return bankCl with { Method = "fsbankcl" };
                return new AppendResult(false, $"fsbext: {fsbextTry.Message}; fsbankcl: {bankCl.Message}", null, -1, null);
            }

            return fsbextTry with
            {
                Message = fsbextTry.Message + " (install fsbankcl for fallback — see tools/README_FSB_TOOLS.md)",
            };
        }

        static AppendResult TryFsbExtAppend(AppendRequest request, string work, int subsongsBefore, bool dryRun)
        {
            string scratch = FfxFsbExt_Service.ScratchDir(work);
            Directory.CreateDirectory(scratch);
            string refWav = Path.Combine(scratch, "format_ref.wav");
            (bool decOk, string decMsg) = FfxFsbVgmStream_Service.ExportSubsong(request.SourceFsbPath, 0, refWav);
            if (!decOk)
                return new AppendResult(false, decMsg, null, -1, "fsbext");

            (bool valOk, string valMsg) = FfxFsbVgmStream_Service.ValidateReplacement(refWav, request.NewWavPath);
            if (!valOk)
                return new AppendResult(false, valMsg, null, -1, "fsbext");

            if (dryRun)
                return new AppendResult(true, $"Dry-run OK: would append as index {subsongsBefore}", null, subsongsBefore, "fsbext");

            string wavDir = FfxFsbExt_Service.WavDir(work);
            if (Directory.Exists(wavDir))
            {
                try { Directory.Delete(wavDir, recursive: true); } catch { }
            }

            (bool exOk, string exMsg, _) = FfxFsbExt_Service.Extract(request.SourceFsbPath, work);
            if (!exOk)
                return new AppendResult(false, exMsg, null, -1, "fsbext");

            wavDir = FfxFsbExt_Service.WavDir(work);
            string[] extracted = Directory.Exists(wavDir)
                ? Directory.GetFiles(wavDir, "*.wav", SearchOption.TopDirectoryOnly)
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : [];

            string newName = $"{subsongsBefore}.wav";
            string destWav = Path.Combine(wavDir, newName);
            File.Copy(request.NewWavPath, destWav, overwrite: true);

            string dumpPath = Path.Combine(work, FfxFsbExt_Service.DumpFileName);
            int wavSize = (int)new FileInfo(destWav).Length;
            if (!FsbDumpDatAppender.TryAppend(dumpPath, subsongsBefore, wavSize, out string? dumpErr))
                return new AppendResult(false, dumpErr ?? "dump.dat append failed", null, -1, "fsbext");

            (bool rbOk, string rbMsg) = FfxFsbExt_Service.Rebuild(work, FfxFsbExt_Service.DumpFileName, "appended.fsb");
            if (!rbOk)
                return new AppendResult(false, rbMsg, null, -1, "fsbext");

            string outFsb = Path.Combine(work, "appended.fsb");
            int? after = FfxFsbVgmStream_Service.TryCountSubsongs(outFsb);
            if (!after.HasValue || after.Value <= subsongsBefore)
                return new AppendResult(false, $"fsbext rebuild did not increase subsong count ({subsongsBefore} → {after})", outFsb, subsongsBefore, "fsbext");

            return new AppendResult(true, $"Appended sample index {subsongsBefore}", outFsb, subsongsBefore, "fsbext");
        }
    }
}

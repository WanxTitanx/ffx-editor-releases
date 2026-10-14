using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Audio;
using FFXProjectEditor.FfxLib.Ps3;
using FFXProjectEditor.Services.Extras;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FFXProjectEditor.FfxLib.Ps3
{
    public static partial class CommandSoundPackService
    {
        public const string NewSeIdAudioOutputDir = @"work\command_sound_new_seid";

        public sealed record NewSeIdAudioPackRequest(
            int TargetMagicId,
            uint NewSeId,
            uint DonorSeId,
            string SourceWavPath,
            string SfxLocale,
            bool MirrorJpLocale,
            int? DonorFsbSampleIndex0,
            int RecordIndex,
            string? Note);

        public sealed record NewSeIdAudioPackResult(
            bool Ok,
            string Message,
            string? StagedFevPath,
            string? StagedFsbPath,
            string? StagedDllPath,
            string? ManifestPath,
            string? FevBackupPath,
            string? FsbBackupPath,
            string? DllBackupPath);

        public static NewSeIdAudioPackResult StageNewSeIdAudioPack(
            NewSeIdAudioPackRequest request,
            string sfxRoot,
            string magicDllRoot,
            string outputDir)
        {
            if (!FfxAudioToolsLocator.CustomSfxToolsReady)
                return Fail("Install audio tools (bootstrap_fsb_audio_tools.ps1)");

            string fevSource = Path.Combine(sfxRoot, "9999.fev");
            string fsbSource = Path.Combine(sfxRoot, "9999_bank00.fsb");
            if (!File.Exists(fevSource) || !File.Exists(fsbSource))
                return Fail($"9999.fev or 9999_bank00.fsb missing under {sfxRoot}");

            if (FevLegacyReader.ContainsSeId(fevSource, request.NewSeId))
                return Fail($"seId {request.NewSeId} already exists in FEV");

            Directory.CreateDirectory(outputDir);
            string work = Path.Combine(outputDir, "build");
            if (Directory.Exists(work))
            {
                try { Directory.Delete(work, recursive: true); } catch { }
            }
            Directory.CreateDirectory(work);

            Fsb9999SampleAppendWriter.AppendResult fsbAppend = Fsb9999SampleAppendWriter.AppendSample(
                new Fsb9999SampleAppendWriter.AppendRequest(fsbSource, request.SourceWavPath, Path.Combine(work, "fsb")));
            if (!fsbAppend.Ok || fsbAppend.OutputFsbPath == null)
                return Fail(fsbAppend.Message);

            FevLegacySequenceWriter.CloneResult fevClone = FevLegacySequenceWriter.CloneSequenceToNewSeId(
                fevSource,
                new FevLegacySequenceWriter.ClonePlan(
                    request.DonorSeId,
                    request.NewSeId,
                    fsbAppend.NewSampleIndex0,
                    request.DonorFsbSampleIndex0),
                Path.Combine(work, "fev"));
            if (!fevClone.Ok || fevClone.OutputFevPath == null)
                return Fail(fevClone.Message);

            string stagedFsb = Path.Combine(outputDir, "9999_bank00.fsb");
            string stagedFev = Path.Combine(outputDir, "9999.fev");
            string stagedCommon = Path.Combine(outputDir, "9999_common.txt");
            string commonSource = Path.Combine(sfxRoot, "9999_common.txt");
            File.Copy(fsbAppend.OutputFsbPath, stagedFsb, overwrite: true);
            File.Copy(fevClone.OutputFevPath, stagedFev, overwrite: true);
            if (File.Exists(commonSource))
            {
                File.Copy(commonSource, stagedCommon, overwrite: true);
                if (!FevLegacySidecarWriter.TryAppendCommonRow(stagedCommon, fsbAppend.NewSampleIndex0, out string? sidecarErr))
                    return Fail(sidecarErr ?? "common.txt append failed");
            }

            string stagedDll = Path.Combine(outputDir, $"magic_{request.TargetMagicId:D4}.dll");
            string srcDll = Path.Combine(magicDllRoot, $"magic_{request.TargetMagicId:D4}.dll");
            if (!File.Exists(srcDll))
                return Fail($"DLL not found: {srcDll}");

            File.Copy(srcDll, stagedDll, overwrite: true);
            ushort? wave = CommandSoundCorpusLoader.TryGetForMagicId(request.TargetMagicId)?.WaveDataId;
            MagicDllSoundWriter.PatchResult patch = MagicDllSoundWriter.PatchFromCorpusEntry(
                outputDir,
                request.TargetMagicId,
                request.NewSeId,
                wave,
                request.RecordIndex,
                dryRun: false);
            if (!patch.Ok)
                return Fail(patch.Message);

            var manifest = new
            {
                generated_utc = DateTimeOffset.UtcNow.ToString("O"),
                tier = "new_seid_audio_triple",
                request.TargetMagicId,
                request.NewSeId,
                request.DonorSeId,
                fsb_append_index = fsbAppend.NewSampleIndex0,
                fsb_method = fsbAppend.Method,
                fev_clone_method = fevClone.Method,
                request.SourceWavPath,
                request.SfxLocale,
                request.MirrorJpLocale,
                staged_fev = stagedFev,
                staged_fsb = stagedFsb,
                staged_dll = stagedDll,
                note = request.Note ?? "",
            };
            string manifestPath = Path.Combine(outputDir, $"new_seid_magic_{request.TargetMagicId:D4}.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

            return new NewSeIdAudioPackResult(
                true,
                "New seId triple pack staged (FEV+FSB+DLL)",
                stagedFev,
                stagedFsb,
                stagedDll,
                manifestPath,
                null,
                null,
                null);
        }

        public static NewSeIdAudioPackResult DeployNewSeIdAudioPack(
            NewSeIdAudioPackResult staged,
            string sfxRoot,
            string magicDllRoot,
            bool mirrorJp = false)
        {
            if (!staged.Ok || staged.StagedFevPath == null || staged.StagedFsbPath == null)
                return staged with { Message = "Nothing staged" };

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fevTarget = Path.Combine(sfxRoot, "9999.fev");
            string fsbTarget = Path.Combine(sfxRoot, "9999_bank00.fsb");
            string commonTarget = Path.Combine(sfxRoot, "9999_common.txt");
            string stagedCommon = Path.Combine(Path.GetDirectoryName(staged.StagedFevPath)!, "9999_common.txt");

            string? fevBackup = File.Exists(fevTarget) ? FevLegacySequenceWriter.BackupFev(fevTarget, stamp) : null;
            string? fsbBackup = File.Exists(fsbTarget) ? Fsb9999SampleReplaceWriter.BackupFsb(fsbTarget, stamp) : null;
            string? commonBackup = File.Exists(commonTarget) ? FevLegacySidecarWriter.BackupCommon(commonTarget, stamp) : null;

            File.Copy(staged.StagedFevPath, fevTarget, overwrite: true);
            File.Copy(staged.StagedFsbPath, fsbTarget, overwrite: true);
            if (File.Exists(stagedCommon))
                File.Copy(stagedCommon, commonTarget, overwrite: true);

            PackResult dllDeploy = DeployStaged(staged.StagedDllPath!, magicDllRoot);

            if (mirrorJp)
            {
                string? jpRoot = TryResolveJpMirror(sfxRoot);
                if (jpRoot != null && Directory.Exists(jpRoot))
                {
                    string jpFev = Path.Combine(jpRoot, "9999.fev");
                    string jpFsb = Path.Combine(jpRoot, "9999_bank00.fsb");
                    if (File.Exists(jpFev)) FevLegacySequenceWriter.BackupFev(jpFev, stamp);
                    if (File.Exists(jpFsb)) Fsb9999SampleReplaceWriter.BackupFsb(jpFsb, stamp);
                    File.Copy(staged.StagedFevPath, jpFev, overwrite: true);
                    File.Copy(staged.StagedFsbPath, jpFsb, overwrite: true);
                }
            }

            return staged with
            {
                Message = "Deployed new seId triple pack",
                FevBackupPath = fevBackup,
                FsbBackupPath = fsbBackup,
                DllBackupPath = dllDeploy.BackupPath,
            };
        }

        public static NewSeIdAudioPackResult RestoreNewSeIdBackups(string sfxRoot, string magicDllRoot, int magicId)
        {
            string fevPath = Path.Combine(sfxRoot, "9999.fev");
            string fsbPath = Path.Combine(sfxRoot, "9999_bank00.fsb");
            string commonPath = Path.Combine(sfxRoot, "9999_common.txt");

            string? fevBackup = Directory.GetFiles(sfxRoot, $"9999.fev{CustomFsbBackupSuffix}_*")
                .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (fevBackup != null)
                FevLegacySequenceWriter.RestoreFev(fevPath, fevBackup);

            string? fsbBackup = Directory.GetFiles(sfxRoot, $"9999_bank00.fsb{CustomFsbBackupSuffix}_*")
                .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (fsbBackup != null)
                Fsb9999SampleReplaceWriter.RestoreFsb(fsbPath, fsbBackup);

            string? commonBackup = Directory.GetFiles(sfxRoot, $"9999_common.txt{CustomFsbBackupSuffix}_*")
                .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (commonBackup != null)
                FevLegacySidecarWriter.RestoreCommon(commonPath, commonBackup);

            PackResult dllRestore = RestoreLatestBackup(magicDllRoot, magicId);
            return new NewSeIdAudioPackResult(
                fevBackup != null || fsbBackup != null || commonBackup != null || dllRestore.Ok,
                $"FEV: {(fevBackup != null ? "restored" : "no backup")}; FSB: {(fsbBackup != null ? "restored" : "no backup")}; common: {(commonBackup != null ? "restored" : "no backup")}; DLL: {dllRestore.Message}",
                null, null, dllRestore.DeployedDllPath, null,
                fevBackup, fsbBackup, dllRestore.BackupPath);
        }

        static string? TryResolveJpMirror(string usSfxRoot)
        {
            if (usSfxRoot.EndsWith(@"\us", StringComparison.OrdinalIgnoreCase))
                return usSfxRoot[..^2] + "jp";
            return null;
        }

        static NewSeIdAudioPackResult Fail(string msg) => new(false, msg, null, null, null, null, null, null, null);
    }
}

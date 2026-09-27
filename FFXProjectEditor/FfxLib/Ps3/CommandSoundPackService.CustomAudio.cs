using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Audio;
using FFXProjectEditor.FfxLib.Ps3;
using FFXProjectEditor.Services.Extras;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FFXProjectEditor.FfxLib.Ps3
{
    public static partial class CommandSoundPackService
    {
        public const string CustomAudioOutputDir = @"work\command_sound_custom";
        public const string CustomFsbBackupSuffix = Fsb9999SampleReplaceWriter.DefaultBackupSuffix;

        public sealed record CustomAudioPackRequest(
            int TargetMagicId,
            uint TargetSeId,
            int FsbSampleIndex0,
            string SourceWavPath,
            string SfxLocale,
            bool PatchDllSeId,
            ushort? DonorWaveDataId,
            int RecordIndex,
            string? Note);

        public sealed record CustomAudioPackResult(
            bool Ok,
            string Message,
            string? StagedFsbPath,
            string? StagedDllPath,
            string? ManifestPath,
            string? DeployedFsbPath,
            string? DeployedDllPath,
            string? FsbBackupPath,
            string? DllBackupPath);

        public static CustomAudioPackResult StageCustomAudioPack(
            CustomAudioPackRequest request,
            string sfxRoot,
            string magicDllRoot,
            string outputDir)
        {
            if (!FfxAudioToolsLocator.CustomSfxToolsReady)
                return new CustomAudioPackResult(false, "Install audio tools (bootstrap_fsb_audio_tools.ps1)", null, null, null, null, null, null, null);

            string fsbSource = Path.Combine(sfxRoot, "9999_bank00.fsb");
            if (!File.Exists(fsbSource))
                return new CustomAudioPackResult(false, $"FSB not found: {fsbSource}", null, null, null, null, null, null, null);

            Directory.CreateDirectory(outputDir);
            string work = Path.Combine(outputDir, "fsb_work");
            if (Directory.Exists(work))
            {
                try { Directory.Delete(work, recursive: true); } catch { /* ignore */ }
            }

            Fsb9999SampleReplaceWriter.ReplaceResult fsbReplace = Fsb9999SampleReplaceWriter.ReplaceSample(
                new Fsb9999SampleReplaceWriter.ReplaceRequest(fsbSource, request.FsbSampleIndex0, request.SourceWavPath, work));

            if (!fsbReplace.Ok || fsbReplace.OutputFsbPath == null)
                return new CustomAudioPackResult(false, fsbReplace.Message, null, null, null, null, null, null, null);

            string stagedFsb = Path.Combine(outputDir, "9999_bank00.fsb");
            File.Copy(fsbReplace.OutputFsbPath, stagedFsb, overwrite: true);

            string? stagedDll = null;
            MagicDllSoundWriter.PatchResult? patch = null;
            if (request.PatchDllSeId)
            {
                string packDir = outputDir;
                string stagedDllTemp = Path.Combine(packDir, $"magic_{request.TargetMagicId:D4}.dll");
                string srcDll = Path.Combine(magicDllRoot, $"magic_{request.TargetMagicId:D4}.dll");
                if (!File.Exists(srcDll))
                    return new CustomAudioPackResult(false, $"DLL not found: {srcDll}", stagedFsb, null, null, null, null, null, null);

                File.Copy(srcDll, stagedDllTemp, overwrite: true);
                patch = MagicDllSoundWriter.PatchFromCorpusEntry(
                    packDir,
                    request.TargetMagicId,
                    request.TargetSeId,
                    request.DonorWaveDataId,
                    request.RecordIndex,
                    dryRun: false);
                if (!patch.Ok)
                    return new CustomAudioPackResult(false, patch.Message, stagedFsb, stagedDllTemp, null, null, null, null, null);
                stagedDll = stagedDllTemp;
            }

            var manifest = new
            {
                generated_utc = DateTimeOffset.UtcNow.ToString("O"),
                tier = "custom_audio_fsb_replace",
                request.TargetMagicId,
                request.TargetSeId,
                request.FsbSampleIndex0,
                request.SourceWavPath,
                request.SfxLocale,
                request.PatchDllSeId,
                staged_fsb = stagedFsb,
                staged_dll = stagedDll,
                note = request.Note ?? "",
            };
            string manifestPath = Path.Combine(outputDir, $"custom_audio_magic_{request.TargetMagicId:D4}.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

            return new CustomAudioPackResult(true, "Custom audio pack staged", stagedFsb, stagedDll, manifestPath, null, null, null, null);
        }

        public static CustomAudioPackResult DeployCustomAudioPack(
            CustomAudioPackResult staged,
            string sfxRoot,
            string magicDllRoot)
        {
            if (!staged.Ok || staged.StagedFsbPath == null)
                return staged with { Message = "Nothing staged to deploy" };

            string fsbTarget = Path.Combine(sfxRoot, "9999_bank00.fsb");
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string? fsbBackup = File.Exists(fsbTarget) ? Fsb9999SampleReplaceWriter.BackupFsb(fsbTarget, stamp) : null;
            File.Copy(staged.StagedFsbPath, fsbTarget, overwrite: true);

            string? dllBackup = null;
            string? deployedDll = null;
            if (staged.StagedDllPath != null && File.Exists(staged.StagedDllPath))
            {
                PackResult dllDeploy = DeployStaged(staged.StagedDllPath, magicDllRoot);
                dllBackup = dllDeploy.BackupPath;
                deployedDll = dllDeploy.DeployedDllPath;
            }

            return staged with
            {
                Message = "Deployed custom audio pack",
                DeployedFsbPath = fsbTarget,
                DeployedDllPath = deployedDll,
                FsbBackupPath = fsbBackup,
                DllBackupPath = dllBackup,
            };
        }

        public static CustomAudioPackResult RestoreCustomAudioBackups(string sfxRoot, string magicDllRoot, int magicId)
        {
            string fsbPath = Path.Combine(sfxRoot, "9999_bank00.fsb");
            string? fsbBackup = Directory.GetFiles(sfxRoot, $"9999_bank00.fsb{CustomFsbBackupSuffix}_*")
                .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (fsbBackup != null)
                Fsb9999SampleReplaceWriter.RestoreFsb(fsbPath, fsbBackup);

            PackResult dllRestore = RestoreLatestBackup(magicDllRoot, magicId);
            string fevPath = Path.Combine(sfxRoot, "9999.fev");
            string? fevBackup = Directory.GetFiles(sfxRoot, $"9999.fev{CustomFsbBackupSuffix}_*")
                .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (fevBackup != null && File.Exists(fevPath))
                FevLegacySequenceWriter.RestoreFev(fevPath, fevBackup);

            return new CustomAudioPackResult(
                fsbBackup != null || dllRestore.Ok,
                $"FSB: {(fsbBackup != null ? "restored" : "no backup")}; DLL: {dllRestore.Message}",
                null, null, null, fsbPath, dllRestore.DeployedDllPath, fsbBackup, dllRestore.BackupPath);
        }

        public static string ResolveSfxRoot(string gameInstallRoot, string locale)
        {
            string loc = locale.Equals("JP", StringComparison.OrdinalIgnoreCase) ? "jp" : "us";
            return Path.Combine(gameInstallRoot, "data", "mods", "FFX_Data", "GameData", "PS3Data", "sound_pc", "sfx", loc);
        }
    }
}

using FFXProjectEditor.FfxLib.Ability;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FFXProjectEditor.FfxLib.Ps3
{
    /// <summary>Stage and deploy magic DLL SeSep sound patches (Tier 1 remap).</summary>
    public static partial class CommandSoundPackService
    {
        public const string DefaultOutputDir = @"work\command_sound_pack";
        public const int Rt2FireMagicId = 84;
        public const int Rt2FiragaMagicId = 86;
        public const uint Rt2FireSeId = 9007;
        public const uint Rt2FiragaSeId = 9006;

        public sealed record PackRequest(
            int TargetMagicId,
            uint DonorSeId,
            ushort? DonorWaveDataId,
            int RecordIndex,
            string? Note);

        public sealed record PackResult(
            bool Ok,
            string Message,
            string? StagedDllPath,
            string? ManifestPath,
            string? DeployedDllPath,
            string? BackupPath,
            MagicDllSoundWriter.PatchResult? Patch);

        public static PackResult StagePack(
            PackRequest request,
            string magicDllRoot,
            string outputDir,
            string? sourceDllPath = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(magicDllRoot);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputDir);

            string source = sourceDllPath ?? Path.Combine(magicDllRoot, $"magic_{request.TargetMagicId:D4}.dll");
            if (!File.Exists(source))
                return new PackResult(false, $"Source DLL not found: {source}", null, null, null, null, null);

            Directory.CreateDirectory(outputDir);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string stagedDll = Path.Combine(outputDir, $"magic_{request.TargetMagicId:D4}.dll");
            File.Copy(source, stagedDll, overwrite: true);

            MagicDllSoundWriter.PatchResult patch = MagicDllSoundWriter.PatchFromCorpusEntry(
                outputDir,
                request.TargetMagicId,
                request.DonorSeId,
                request.DonorWaveDataId,
                request.RecordIndex,
                dryRun: false);

            if (!patch.Ok)
                return new PackResult(false, patch.Message, stagedDll, null, null, null, patch);

            var manifest = new
            {
                generated_utc = DateTimeOffset.UtcNow.ToString("O"),
                target_magic_id = request.TargetMagicId,
                donor_se_id = request.DonorSeId,
                donor_wave_data_id = request.DonorWaveDataId,
                record_index = request.RecordIndex,
                note = request.Note ?? "",
                source_dll = source,
                staged_dll = stagedDll,
                before = patch.RecordsBefore.Select(ToDto).ToList(),
                after = patch.RecordsAfter.Select(ToDto).ToList(),
            };

            string manifestPath = Path.Combine(outputDir, $"command_sound_pack_{request.TargetMagicId:D4}.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

            return new PackResult(true, "Staged", stagedDll, manifestPath, null, null, patch);
        }

        public static PackResult DeployStaged(string stagedDllPath, string magicDllRoot)
        {
            if (!File.Exists(stagedDllPath))
                return new PackResult(false, $"Staged DLL missing: {stagedDllPath}", stagedDllPath, null, null, null, null);

            string fileName = Path.GetFileName(stagedDllPath);
            string target = Path.Combine(magicDllRoot, fileName);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string? backup = File.Exists(target) ? MagicDllSoundWriter.BackupDll(target, stamp) : null;
            File.Copy(stagedDllPath, target, overwrite: true);
            return new PackResult(true, "Deployed", stagedDllPath, null, target, backup, null);
        }

        public static PackResult RestoreLatestBackup(string magicDllRoot, int magicId)
        {
            string dllPath = Path.Combine(magicDllRoot, $"magic_{magicId:D4}.dll");
            string? backup = Directory.GetFiles(magicDllRoot, $"magic_{magicId:D4}.dll{MagicDllSoundWriter.DefaultBackupSuffix}_*")
                .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (backup == null)
                return new PackResult(false, "No backup found", null, null, null, null, null);

            MagicDllSoundWriter.RestoreDll(dllPath, backup);
            return new PackResult(true, $"Restored from {Path.GetFileName(backup)}", null, null, dllPath, backup, null);
        }

        public static PackResult RunRt2FireToFiragaDemo(string magicDllRoot, string outputDir, bool deploy)
        {
            var request = new PackRequest(
                Rt2FireMagicId,
                Rt2FiragaSeId,
                CommandSoundCorpusLoader.TryGetForMagicId(Rt2FiragaMagicId)?.WaveDataId,
                0,
                "RT2 demo: Fire magic_0084 gets Firaga seId 9006");

            PackResult staged = StagePack(request, magicDllRoot, Path.Combine(outputDir, "rt2_fire_to_firaga"));
            if (!staged.Ok)
                return staged;

            if (!deploy)
                return staged with { Message = "RT2 demo staged (add deploy to copy to game)" };

            PackResult deployed = DeployStaged(staged.StagedDllPath!, magicDllRoot);
            return deployed with { Patch = staged.Patch, ManifestPath = staged.ManifestPath };
        }

        static object ToDto(MagicDllSoundRecordScanner.SeSepHit r) => new
        {
            r.FileOffset,
            r.SeId,
            r.WaveDataId,
            r.RecLen,
            r.VoiceCount,
        };
    }
}

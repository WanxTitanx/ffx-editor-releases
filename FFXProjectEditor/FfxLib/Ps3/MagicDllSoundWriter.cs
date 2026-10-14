using FFXProjectEditor.FfxLib.Ps3;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FFXProjectEditor.FfxLib.Ps3
{
    /// <summary>
    /// Patches SeSep-shaped sound records inside a magic DLL PE overlay.
    /// Targets <c>seId@+0x08</c> (FMOD sequence key) and <c>waveDataId@+0x11</c> (SPU preload).
    /// </summary>
    public static class MagicDllSoundWriter
    {
        public sealed record SoundPatchPlan(
            int MagicId,
            int RecordFileOffset,
            uint? NewSeId,
            ushort? NewWaveDataId,
            string Note);

        public sealed record PatchResult(
            bool Ok,
            string Message,
            IReadOnlyList<MagicDllSoundRecordScanner.SeSepHit> RecordsBefore,
            IReadOnlyList<MagicDllSoundRecordScanner.SeSepHit> RecordsAfter);

        public static IReadOnlyList<MagicDllSoundRecordScanner.SeSepHit> ListRecords(string dllPath)
        {
            byte[] bytes = File.ReadAllBytes(dllPath);
            return MagicDllSoundRecordScanner.Scan(bytes);
        }

        public static PatchResult ApplyPatch(string dllPath, SoundPatchPlan plan, bool dryRun = false)
        {
            if (!File.Exists(dllPath))
                return new PatchResult(false, $"DLL not found: {dllPath}", [], []);

            byte[] bytes = File.ReadAllBytes(dllPath);
            var before = MagicDllSoundRecordScanner.Scan(bytes).ToList();
            if (before.Count == 0)
                return new PatchResult(false, "No SeSep records found in PE", before, before);

            MagicDllSoundRecordScanner.SeSepHit target = before.FirstOrDefault(h => h.FileOffset == plan.RecordFileOffset)
                ?? before[0];

            bool changed = false;
            if (plan.NewSeId.HasValue)
                changed |= MagicDllSoundRecordScanner.TryPatchSeId(bytes, target.FileOffset, plan.NewSeId.Value);
            if (plan.NewWaveDataId.HasValue)
                changed |= MagicDllSoundRecordScanner.TryPatchWaveDataId(bytes, target.FileOffset, plan.NewWaveDataId.Value);

            if (!changed)
                return new PatchResult(false, "No patch applied (values unchanged or offset invalid)", before, before);

            var after = MagicDllSoundRecordScanner.Scan(bytes).ToList();
            if (!dryRun)
                File.WriteAllBytes(dllPath, bytes);

            return new PatchResult(true, dryRun ? "Dry-run OK" : "Patch written", before, after);
        }

        public static PatchResult RoundTripSelfTest(string dllPath)
        {
            IReadOnlyList<MagicDllSoundRecordScanner.SeSepHit> records = ListRecords(dllPath);
            if (records.Count == 0)
                return new PatchResult(false, "SKIP: no SeSep records", records, records);

            MagicDllSoundRecordScanner.SeSepHit r = records[0];
            byte[] original = File.ReadAllBytes(dllPath);
            string temp = Path.Combine(Path.GetTempPath(), $"magic_sound_rt0_{Guid.NewGuid():N}.dll");
            try
            {
                File.Copy(dllPath, temp, overwrite: true);
                PatchResult p1 = ApplyPatch(temp, new SoundPatchPlan(0, r.FileOffset, r.SeId, r.WaveDataId, "identity"));
                if (!p1.Ok)
                    return p1;

                byte[] round = File.ReadAllBytes(temp);
                bool identical = original.AsSpan().SequenceEqual(round);
                return identical
                    ? new PatchResult(true, "RT0 byte-identical", records, ListRecords(temp))
                    : new PatchResult(false, "RT0 FAILED: bytes drifted on identity patch", records, ListRecords(temp));
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }

        public static string SerializeRecords(IReadOnlyList<MagicDllSoundRecordScanner.SeSepHit> records) =>
            JsonSerializer.Serialize(records.Select(r => new
            {
                r.FileOffset,
                r.Rva,
                r.SeId,
                r.RecLen,
                r.WaveDataId,
                r.VoiceCount,
            }), new JsonSerializerOptions { WriteIndented = true });

        public const string DefaultBackupSuffix = ".backup_command_sound";

        public static string BackupDll(string dllPath, string? stamp = null)
        {
            if (!File.Exists(dllPath))
                throw new FileNotFoundException("DLL not found for backup", dllPath);

            string suffix = DefaultBackupSuffix + "_" + (stamp ?? DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            string backupPath = dllPath + suffix;
            File.Copy(dllPath, backupPath, overwrite: true);
            return backupPath;
        }

        public static void RestoreDll(string dllPath, string backupPath)
        {
            if (!File.Exists(backupPath))
                throw new FileNotFoundException("Backup not found", backupPath);
            File.Copy(backupPath, dllPath, overwrite: true);
        }

        public static PatchResult PatchFromDonorMagicDll(
            string targetDllPath,
            string donorDllPath,
            int recordIndex = 0,
            bool dryRun = false)
        {
            IReadOnlyList<MagicDllSoundRecordScanner.SeSepHit> donorRecords = ListRecords(donorDllPath);
            if (donorRecords.Count == 0)
                return new PatchResult(false, "Donor DLL has no SeSep records.", [], []);

            if (recordIndex < 0 || recordIndex >= donorRecords.Count)
                return new PatchResult(false, $"Donor record index {recordIndex} out of range (count={donorRecords.Count})", donorRecords, donorRecords);

            MagicDllSoundRecordScanner.SeSepHit donor = donorRecords[recordIndex];
            IReadOnlyList<MagicDllSoundRecordScanner.SeSepHit> targetBefore = ListRecords(targetDllPath);
            if (targetBefore.Count == 0)
                return new PatchResult(false, "Target DLL has no SeSep records.", targetBefore, targetBefore);

            int targetIndex = Math.Min(recordIndex, targetBefore.Count - 1);
            MagicDllSoundRecordScanner.SeSepHit target = targetBefore[targetIndex];
            return ApplyPatch(
                targetDllPath,
                new SoundPatchPlan(0, target.FileOffset, donor.SeId, donor.WaveDataId, $"donor:{Path.GetFileName(donorDllPath)}[{recordIndex}]"),
                dryRun);
        }

        public static PatchResult PatchFromCorpusEntry(
            string magicDllRoot,
            int targetMagicId,
            uint donorSeId,
            ushort? donorWaveDataId = null,
            int recordIndex = 0,
            bool dryRun = false)
        {
            string dllPath = Path.Combine(magicDllRoot, $"magic_{targetMagicId:D4}.dll");
            IReadOnlyList<MagicDllSoundRecordScanner.SeSepHit> records = ListRecords(dllPath);
            if (records.Count == 0)
                return new PatchResult(false, "No SeSep records in target DLL.", records, records);

            if (recordIndex < 0 || recordIndex >= records.Count)
                return new PatchResult(false, $"Record index {recordIndex} out of range", records, records);

            MagicDllSoundRecordScanner.SeSepHit target = records[recordIndex];
            return ApplyPatch(
                dllPath,
                new SoundPatchPlan(targetMagicId, target.FileOffset, donorSeId, donorWaveDataId, $"corpus seId={donorSeId}"),
                dryRun);
        }
    }
}

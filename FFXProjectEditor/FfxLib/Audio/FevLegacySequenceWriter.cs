using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FFXProjectEditor.FfxLib.Audio
{
    /// <summary>Clone donor FEV sequence row to a new seId (Phase 2 — no overwrite of donor).</summary>
    public static class FevLegacySequenceWriter
    {
        public sealed record ClonePlan(
            uint DonorSeId,
            uint NewSeId,
            int FsbSampleIndex0,
            int? DonorFsbSampleIndex0);

        public sealed record CloneResult(
            bool Ok,
            string Message,
            string? OutputFevPath,
            int? PatchedSampleRefOffset,
            string? Method);

        public static CloneResult CloneSequenceToNewSeId(string fevSourcePath, ClonePlan plan, string? workDir = null)
        {
            if (!File.Exists(fevSourcePath))
                return new CloneResult(false, "FEV not found", null, null, null);

            if (FevLegacyReader.ContainsSeId(fevSourcePath, plan.NewSeId))
                return new CloneResult(false, $"seId {plan.NewSeId} already present in FEV", null, null, null);

            FevLegacyReader.SequenceBlobHit? hit =
                FevLegacyReader.TryLocateSequenceBlob(fevSourcePath, plan.DonorSeId);

            string method = "seid_blob";
            if (hit == null)
            {
                int donorIdx = plan.DonorFsbSampleIndex0 ?? 0;
                hit = FevLegacyReader.TryLocateSequenceByFsbIndex(fevSourcePath, donorIdx);
                method = hit != null ? "fsb_index_blob" : "registration_trailer";
            }

            byte[] fev = File.ReadAllBytes(fevSourcePath);
            byte[] output;
            int? samplePatch = null;

            if (hit != null)
            {
                byte[] blob = fev.AsSpan(hit.AbsoluteOffset, hit.BlobLength).ToArray();
                int seIdRel = FindSeIdOffsetInBlob(blob, plan.DonorSeId);
                if (seIdRel >= 0)
                    BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(seIdRel), plan.NewSeId);

                samplePatch = TryPatchSampleIndex(blob, plan.DonorFsbSampleIndex0, plan.FsbSampleIndex0);
                output = AppendBlobToContainingChunk(fev, hit, blob);
                if (output.Length == fev.Length)
                    return new CloneResult(false, "Could not append clone to FEV chunk", null, samplePatch, method);
            }
            else
            {
                output = fev.ToArray();
            }

            output = AppendRegistrationTrailer(output, plan.NewSeId, plan.FsbSampleIndex0, plan.DonorSeId);

            string work = workDir ?? Path.Combine(Path.GetTempPath(), $"fev_clone_{Guid.NewGuid():N}");
            Directory.CreateDirectory(work);
            string outPath = Path.Combine(work, "9999_cloned.fev");
            File.WriteAllBytes(outPath, output);

            if (!FevLegacyReader.ContainsSeId(outPath, plan.NewSeId))
                return new CloneResult(false, "Clone written but new seId not found after append", outPath, samplePatch, method);

            return new CloneResult(
                true,
                $"Registered new seId {plan.NewSeId} ({method})",
                outPath,
                samplePatch,
                method);
        }

        static byte[] AppendRegistrationTrailer(byte[] fev, uint newSeId, int fsbSampleIndex0, uint donorSeId)
        {
            // Editor registration block — offline gate + RT2 correlation; not a full FMOD PROJECT event.
            var trailer = new byte[FevLegacyReader.RegistrationMagic.Length + 12];
            FevLegacyReader.RegistrationMagic.AsSpan().CopyTo(trailer);
            BinaryPrimitives.WriteUInt32LittleEndian(trailer.AsSpan(8), newSeId);
            BinaryPrimitives.WriteUInt32LittleEndian(trailer.AsSpan(12), (uint)fsbSampleIndex0);
            BinaryPrimitives.WriteUInt32LittleEndian(trailer.AsSpan(16), donorSeId);

            var merged = new byte[fev.Length + trailer.Length];
            fev.AsSpan().CopyTo(merged);
            trailer.AsSpan().CopyTo(merged.AsSpan(fev.Length));
            return merged;
        }

        static int FindSeIdOffsetInBlob(byte[] blob, uint seId)
        {
            byte[] needle = BitConverter.GetBytes(seId);
            for (int i = 0; i <= blob.Length - 4; i++)
            {
                if (blob.AsSpan(i, 4).SequenceEqual(needle))
                    return i;
            }
            return -1;
        }

        static int? TryPatchSampleIndex(byte[] blob, int? donorIndex, int newIndex)
        {
            if (!donorIndex.HasValue) return null;
            int donor = donorIndex.Value;
            for (int i = 0; i <= blob.Length - 4; i += 2)
            {
                uint v = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(i));
                if (v == (uint)donor)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(i), (uint)newIndex);
                    return i;
                }
                ushort s = BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(i));
                if (s == donor)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(i), (ushort)newIndex);
                    return i;
                }
            }
            return null;
        }

        static byte[] AppendBlobToContainingChunk(byte[] fev, FevLegacyReader.SequenceBlobHit hit, byte[] blob)
        {
            IReadOnlyList<FevLegacyReader.FevChunkInfo> chunks = FevLegacyReader.ParseChunks(fev);
            FevLegacyReader.FevChunkInfo? chunk = chunks.FirstOrDefault(c =>
                hit.AbsoluteOffset >= c.BodyOffset && hit.AbsoluteOffset < c.BodyOffset + c.BodySize);
            if (chunk == null)
            {
                var grown = fev.ToList();
                grown.AddRange(blob);
                return grown.ToArray();
            }

            int insertAt = chunk.BodyOffset + chunk.BodySize;
            int pad = (blob.Length & 1);
            var result = new List<byte>(fev.Length + blob.Length + pad + 8);
            result.AddRange(fev.AsSpan(0, insertAt));
            result.AddRange(blob);
            if (pad == 1) result.Add(0);

            int tailStart = insertAt;
            if (tailStart < fev.Length)
                result.AddRange(fev.AsSpan(tailStart));

            byte[] bytes = result.ToArray();
            int added = blob.Length + pad;
            PatchChunkSize(bytes, chunk.HeaderOffset, chunk.BodySize + added);
            if (bytes.Length >= 8)
            {
                int riffSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), riffSize + added);
            }
            return bytes;
        }

        static void PatchChunkSize(byte[] bytes, int headerOffset, int newBodySize)
        {
            if (headerOffset + 8 > bytes.Length) return;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(headerOffset + 4), newBodySize);
        }

        public static string BackupFev(string fevPath, string? stamp = null)
        {
            string suffix = Fsb9999SampleReplaceWriter.DefaultBackupSuffix + "_" + (stamp ?? DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            string backup = fevPath + suffix;
            File.Copy(fevPath, backup, overwrite: true);
            return backup;
        }

        public static void RestoreFev(string fevPath, string backupPath)
        {
            if (!File.Exists(backupPath))
                throw new FileNotFoundException("FEV backup not found", backupPath);
            File.Copy(backupPath, fevPath, overwrite: true);
        }

        public static string WritePlanManifest(ClonePlan plan, int magicId, string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            string path = Path.Combine(outputDir, $"fev_append_plan_{plan.NewSeId}.json");
            var payload = new Dictionary<string, object?>
            {
                ["generated_utc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["tier"] = "fev_legacy_clone",
                ["magic_id"] = magicId,
                ["new_se_id"] = plan.NewSeId,
                ["donor_se_id"] = plan.DonorSeId,
                ["fsb_sample_index"] = plan.FsbSampleIndex0,
            };
            File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            return path;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Ps3
{
    internal enum MagicDllFamilyDetectionMethod
    {
        Unknown = 0,
        SlotKindSignature,
        Slot0ByteScan,
        EgoStringFallback
    }

    internal sealed record MagicDllFamilyClassification(
        MagicDllEffectFamily Family,
        MagicDllFamilyDetectionMethod Method,
        string PreclassifiedBucket,
        IReadOnlyList<int> Slot0DiscriminantOffsets);

    /// <summary>
    /// Ports <c>scripts/scan_ab.py</c>: slot-kind bucket pre-classification plus
    /// PE byte-scan of overlay slot 0 for runtime host-offset immediates.
    ///
    /// CORRECTED 2026-07-05: discriminant offsets were swapped.
    ///   0xB2C (2860) Materializer  → Family C (NOT B)
    ///   0xB30 (2864) RecordInterp  → Family C (NOT B)
    ///   0xB1C (2844) ParsePPP      → ALL families (NOT discriminant)
    ///   0xB18 (2840) BindResource  → ALL families (NOT discriminant)
    ///   0xC8C (3212) AllocRoot     → Families B or C
    ///   Ego* strings               → Family D
    /// Reference: 110 confirmed D DLLs with Ego* strings, 59 unique Ego types.
    /// Size signal: C is small (100-500KB), B medium (500KB-1MB), A/D large (700KB+).
    /// </summary>
    internal static class MagicDllFamilyClassifier
    {
        public const string ScanNeededSlotKindSignature =
            "code|code|code|code|code|code|code|data|code|code|code|code|code|code|code|code";

        static readonly HashSet<int> DiscriminantOffsets = [0xB2C, 0xB30, 0xC8C]; // 2860, 2864, 3212

        public static MagicDllFamilyClassification Classify(MagicDllInspection inspection)
        {
            if (inspection.OverlayEvidence == null)
                return new(MagicDllEffectFamily.Unknown, MagicDllFamilyDetectionMethod.Unknown, string.Empty, []);

            string slotKind = inspection.OverlayEvidence.SlotKindSignature ?? string.Empty;
            string bucket = PreclassifyBucket(slotKind);
            if (!string.Equals(bucket, "SCAN_NEEDED", StringComparison.Ordinal))
            {
                MagicDllEffectFamily family = bucket switch
                {
                    "B_extended" => MagicDllEffectFamily.B_RootRecordInterpreter,
                    "C_heavy" or "C_light" => MagicDllEffectFamily.C_RootSelfGovernedParam,
                    "D" => MagicDllEffectFamily.D_EgoTasklist,
                    _ => MagicDllEffectFamily.Unknown
                };
                return new(family, MagicDllFamilyDetectionMethod.SlotKindSignature, bucket, []);
            }

            IReadOnlyList<int> slot0Offsets = ScanSlot0DiscriminantOffsets(inspection);
            bool hasMaterializer = slot0Offsets.Contains(0xB2C) || slot0Offsets.Contains(0xB30);
            bool hasAllocRoot = slot0Offsets.Contains(0xC8C);

            // 0xB2C (2860) Materializer/0xB30 (2864) RecordInterp: shared by B (subgroup) and C.
            // C is small (procedural, ~100-500KB). B is larger (EgoVM, 600KB+).
            if (hasMaterializer || hasAllocRoot)
            {
                long fileSize = GetFileSize(inspection.FilePath);
                if (fileSize > 0 && fileSize < 600 * 1024)
                    return new(MagicDllEffectFamily.C_RootSelfGovernedParam, MagicDllFamilyDetectionMethod.Slot0ByteScan, bucket, slot0Offsets);
                return new(MagicDllEffectFamily.B_RootRecordInterpreter, MagicDllFamilyDetectionMethod.Slot0ByteScan, bucket, slot0Offsets);
            }

            // 0xB1C (2844) ParsePPP and 0xB18 (2840) BindResource are NOT discriminants:
            // ALL four families call these. Only check Ego* strings for Family D.
            bool hasEgo = inspection.Strings.Any(st =>
                st.Value.Contains("EgoTask", StringComparison.Ordinal)
                || st.Value.Contains("EgoCtrl", StringComparison.Ordinal)
                || st.Value.Contains("EgoListItm", StringComparison.Ordinal));
            if (hasEgo)
                return new(MagicDllEffectFamily.D_EgoTasklist, MagicDllFamilyDetectionMethod.EgoStringFallback, bucket, slot0Offsets);

            return new(MagicDllEffectFamily.A_ParticleSelfContained, MagicDllFamilyDetectionMethod.Slot0ByteScan, bucket, slot0Offsets);
        }

        public static MagicDllEffectFamily DetectFamily(MagicDllInspection inspection) =>
            Classify(inspection).Family;

        public static string PreclassifyBucket(string slotKindSignature)
        {
            string sig = slotKindSignature.Trim();
            return sig switch
            {
                "code|code|code|code|code|code|code|null|code|code|code|code|code|code|code|code" => "B_extended",
                // C_heavy and C_light are unreliable — fall through to byte-scan
                "code|code|code|code|code|code|code|data|null|null|null|null|null|null|null|code" => "D",
                ScanNeededSlotKindSignature => "SCAN_NEEDED",
                // Catch-all: anything else (C_heavy, C_light, unknown) → scan
                _ => "SCAN_NEEDED"
            };
        }

        public static IReadOnlyList<int> ScanSlot0DiscriminantOffsets(MagicDllInspection inspection)
        {
            MagicDllOverlaySlot? slot0 = inspection.OverlayEvidence?.Slots.FirstOrDefault(s => s.Index == 0);
            if (slot0 == null || string.IsNullOrWhiteSpace(slot0.Rva))
                return [];

            if (!TryParseHex(slot0.Rva, out int rva))
                return [];

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(inspection.FilePath);
            }
            catch
            {
                return [];
            }

            int fileOffset;
            try
            {
                fileOffset = inspection.RvaToFileOffset(rva);
            }
            catch
            {
                return [];
            }

            return ScanDiscriminantOffsets(bytes, fileOffset);
        }

        public static IReadOnlyList<int> ScanDiscriminantOffsets(byte[] bytes, int fileOffset)
        {
            if (fileOffset < 0 || fileOffset >= bytes.Length)
                return [];

            HashSet<int> found = [];
            int end = Math.Min(fileOffset + 2048, bytes.Length - 3);
            for (int i = fileOffset; i < end; i++)
            {
                int imm = BitConverter.ToInt32(bytes, i);
                if (DiscriminantOffsets.Contains(imm))
                    found.Add(imm);
            }

            return found.OrderBy(v => v).ToList();
        }

        static bool TryParseHex(string text, out int value)
        {
            string trimmed = text.Trim();
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[2..];
            return int.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        static long GetFileSize(string path)
        {
            try { return new FileInfo(path).Length; }
            catch { return -1; }
        }
    }
}

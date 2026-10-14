using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FFXProjectEditor.FfxLib.Ps3
{
    /// <summary>Heuristic recolor for cloned Prism Flare DLLs (magic_0714 / magic_0715) — Family A only.</summary>
    internal static class PrismMagicDllRecolor
    {
        const float PrismR = 0.82f;
        const float PrismG = 0.28f;
        const float PrismB = 1.0f;
        const float PrismA = 1.0f;

        /// <summary>RT2 smoke-test: pure magenta vec4 — impossible to miss if DLL color path is live.</summary>
        public static readonly (float R, float G, float B, float A) DrasticMagenta = (1.0f, 0.0f, 1.0f, 1.0f);

        public sealed record RecolorResult(
            string SourceDll,
            string OutputDll,
            int PatchCount,
            IReadOnlyList<MagicDllBytePatch> Patches,
            string SourceSha256,
            string OutputSha256);

        public static RecolorResult Apply(string sourceDll, string outputDll, int maxPatches = 16) =>
            Apply(sourceDll, outputDll, PrismR, PrismG, PrismB, PrismA, maxPatches);

        public static RecolorResult ApplyDrastic(string sourceDll, string outputDll, int maxPatches = 32)
        {
            var (r, g, b, a) = DrasticMagenta;
            return Apply(sourceDll, outputDll, r, g, b, a, maxPatches);
        }

        /// <summary>
        /// RT2 FAILED (2026-06-14): Thundaga <c>0094/0716</c> is <b>Family D</b> (PPP/EgoTask), not Family A.
        /// Global vec4 scan hits matrix blocks (e.g. 0x31640) → bolts go 2D/disconnected. Do not use.
        /// </summary>
        [Obsolete("RT2 failed — Family D Thundaga uses PPP pppColMove, not host+3732 vec4.")]
        public static RecolorResult ApplyThundaFiraAnim1DrawTint(string sourceDll, string outputDll, int maxPatches = 2) =>
            Apply(sourceDll, outputDll, 1.0f, 0.48f, 0.10f, 1.0f, maxPatches, PatchCandidateFilter.WhiteOnly);

        [Obsolete("RT2 failed — see ApplyThundaFiraAnim1DrawTint.")]
        public static RecolorResult ApplyThundaFiraAnim1DrawDrastic(string sourceDll, string outputDll, int maxPatches = 2)
        {
            var (r, g, b, a) = DrasticMagenta;
            return Apply(sourceDll, outputDll, r, g, b, a, maxPatches, PatchCandidateFilter.WhiteOnly);
        }

        [Obsolete("RT2 failed — Thundaga bolts are Family D PPP, not Family A vec4.")]
        public static RecolorResult ApplyThundaFiraBoltOrangeTint(string sourceDll, string outputDll, int maxPatches = 2) =>
            ApplyThundaFiraAnim1DrawTint(sourceDll, outputDll, maxPatches);

        [Obsolete("RT2 failed — Thundaga bolts are Family D PPP, not Family A vec4.")]
        public static RecolorResult ApplyThundaFiraBoltTint(string sourceDll, string outputDll, int maxPatches = 4) =>
            Apply(sourceDll, outputDll, 0.92f, 0.38f, 0.15f, 1.0f, maxPatches, PatchCandidateFilter.WhiteOnly);

        public static RecolorResult Apply(
            string sourceDll,
            string outputDll,
            float r,
            float g,
            float b,
            float a,
            int maxPatches = 16) =>
            Apply(sourceDll, outputDll, r, g, b, a, maxPatches, PatchCandidateFilter.Any);

        public static RecolorResult Apply(
            string sourceDll,
            string outputDll,
            float r,
            float g,
            float b,
            float a,
            int maxPatches,
            PatchCandidateFilter filter)
        {
            if (!File.Exists(sourceDll))
                throw new FileNotFoundException("DLL not found.", sourceDll);

            MagicDllInspection inspection = MagicDllDecompiler.Inspect(sourceDll);
            byte[] bytes = File.ReadAllBytes(sourceDll);
            List<MagicDllBytePatch> patches = BuildPatches(inspection, bytes, r, g, b, a, maxPatches, filter);

            foreach (MagicDllBytePatch patch in patches)
            {
                byte[] payload = ParseHexBytes(patch.Hex);
                int offset = patch.FileOffset;
                if (offset < 0 || offset + payload.Length > bytes.Length)
                    throw new InvalidOperationException($"Patch at 0x{offset:X} out of range.");
                Array.Copy(payload, 0, bytes, offset, payload.Length);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputDll))!);
            File.WriteAllBytes(outputDll, bytes);

            return new RecolorResult(
                sourceDll,
                outputDll,
                patches.Count,
                patches,
                inspection.Sha256,
                MagicDllDecompiler.Inspect(outputDll).Sha256);
        }

        public static void WritePatchPlan(string sourceDll, string planPath, int maxPatches = 16) =>
            WritePatchPlan(sourceDll, planPath, PrismR, PrismG, PrismB, PrismA, maxPatches);

        public static void WritePatchPlan(
            string sourceDll,
            string planPath,
            float r,
            float g,
            float b,
            float a,
            int maxPatches = 16)
        {
            MagicDllInspection inspection = MagicDllDecompiler.Inspect(sourceDll);
            byte[] bytes = File.ReadAllBytes(sourceDll);
            var plan = new MagicDllPatchPlan
            {
                BytePatches = BuildPatches(inspection, bytes, r, g, b, a, maxPatches)
            };
            File.WriteAllText(planPath, JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true }));
        }

        internal enum PatchCandidateFilter
        {
            Any,
            /// <summary>Legacy name — prefer <see cref="PossibleTimingBlueDominant"/> for Family D Waterga cast DLL.</summary>
            BlueOnly,
            /// <summary>RT2 Flan Flood 0718: blue-dominant vec4 advanced damage timing; not visual tint.</summary>
            PossibleTimingBlueDominant,
            WhiteOnly,
        }

        static List<MagicDllBytePatch> BuildPatches(
            MagicDllInspection inspection,
            byte[] bytes,
            float r,
            float g,
            float b,
            float a,
            int maxPatches,
            PatchCandidateFilter filter = PatchCandidateFilter.Any)
        {
            byte[] prism = FloatBytes(r, g, b, a);
            string prismHex = BitConverter.ToString(prism).Replace("-", "", StringComparison.Ordinal);
            var hits = new List<(int offset, int score)>();

            foreach (MagicDllSection section in inspection.Sections)
            {
                if (section.Name.Equals(".text", StringComparison.OrdinalIgnoreCase))
                    continue;

                int start = Math.Max(0, section.RawPointer);
                int end = Math.Min(bytes.Length, section.RawPointer + section.RawSize);
                for (int offset = Align4(start); offset <= end - 16; offset += 4)
                {
                    int score = ScoreColorVector(bytes, offset, filter);
                    if (score <= 0)
                        continue;
                    hits.Add((offset, score));
                }
            }

            var used = new HashSet<int>();
            var patches = new List<MagicDllBytePatch>();
            foreach ((int offset, int score) in hits.OrderByDescending(h => h.score).ThenBy(h => h.offset))
            {
                if (!used.Add(offset))
                    continue;
                if (patches.Count >= maxPatches)
                    break;
                if (patches.Any(p => Math.Abs(p.FileOffset - offset) < 12))
                    continue;

                patches.Add(new MagicDllBytePatch
                {
                    FileOffset = offset,
                    Hex = prismHex,
                    Note = filter == PatchCandidateFilter.PossibleTimingBlueDominant
                        ? $"possible-timing vec4 score={score} (RT2: not color — cast→hit sync)"
                        : $"prism vec4 recolor score={score}"
                });
            }

            return patches;
        }

        static int ScoreColorVector(byte[] bytes, int offset, PatchCandidateFilter filter = PatchCandidateFilter.Any)
        {
            float r = BitConverter.ToSingle(bytes, offset);
            float g = BitConverter.ToSingle(bytes, offset + 4);
            float b = BitConverter.ToSingle(bytes, offset + 8);
            float a = BitConverter.ToSingle(bytes, offset + 12);
            if (!IsFiniteColor(r, g, b, a))
                return 0;

            if (filter is PatchCandidateFilter.BlueOnly or PatchCandidateFilter.PossibleTimingBlueDominant
                && !IsBlueTintCandidate(r, g, b))
                return 0;

            if (filter == PatchCandidateFilter.WhiteOnly && !IsWhiteDrawCandidate(r, g, b, a))
                return 0;

            int score = 0;
            if (r >= 0.92f && g >= 0.92f && b >= 0.92f)
            {
                score += filter == PatchCandidateFilter.WhiteOnly ? 140 : 120;
                if (offset >= 0x10000)
                    score += 30;
            }
            else if (r >= 0.75f && g <= 0.65f && b <= 0.35f)
                score += 100;
            else if (b >= 0.55f && r <= 0.55f)
                score += 95;
            else if (r >= 0.6f && g >= 0.2f && g <= 0.8f && b <= 0.4f)
                score += 80;
            else if (r >= 0.4f && g >= 0.4f && b >= 0.4f)
                score += 40;

            if (a is >= 0.95f and <= 1.05f)
                score += 10;
            return score;
        }

        static bool IsBlueTintCandidate(float r, float g, float b) =>
            b >= 0.55f && r <= 0.55f;

        static bool IsWhiteDrawCandidate(float r, float g, float b, float a) =>
            r is >= 0.92f and <= 1.08f
            && g is >= 0.92f and <= 1.08f
            && b is >= 0.92f and <= 1.08f
            && a is >= 0.95f and <= 1.05f;

        static bool IsFiniteColor(float r, float g, float b, float a) =>
            float.IsFinite(r) && float.IsFinite(g) && float.IsFinite(b) && float.IsFinite(a)
            && r >= 0f && r <= 1.5f && g >= 0f && g <= 1.5f && b >= 0f && b <= 1.5f
            && a >= 0f && a <= 1.5f;

        static byte[] FloatBytes(float r, float g, float b, float a)
        {
            byte[] buf = new byte[16];
            BitConverter.TryWriteBytes(buf.AsSpan(0, 4), r);
            BitConverter.TryWriteBytes(buf.AsSpan(4, 4), g);
            BitConverter.TryWriteBytes(buf.AsSpan(8, 4), b);
            BitConverter.TryWriteBytes(buf.AsSpan(12, 4), a);
            return buf;
        }

        static int Align4(int value) => (value + 3) & ~3;

        static byte[] ParseHexBytes(string hex)
        {
            hex = hex.Replace(" ", "", StringComparison.Ordinal).Replace("0x", "", StringComparison.OrdinalIgnoreCase);
            if (hex.Length % 2 != 0)
                throw new FormatException("Hex payload must have even length.");
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = byte.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return bytes;
        }
    }
}

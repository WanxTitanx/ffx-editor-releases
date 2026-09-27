using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ps3
{
    /// <summary>
    /// Scan Family D magic DLL <c>.data</c> for blue-dominant vec3/vec4.
    /// RT2 Flan Flood 0718: these often mark <b>possible cast→hit timing</b>, not <c>pppColMove</c> tint.
    /// Excludes dense identity-matrix blocks (RT2: patching those breaks bolt transforms).
    /// </summary>
    internal static class MagicDllPppColorCandidateScanner
    {
        public sealed record ColorCandidate(
            int FileOffset,
            string RvaHex,
            float R,
            float G,
            float B,
            float A,
            int Score,
            string Note);

        public static IReadOnlyList<ColorCandidate> Scan(MagicDllInspection inspection, int maxResults = 32)
        {
            byte[] bytes = File.ReadAllBytes(inspection.FilePath);
            MagicDllSection? data = inspection.Sections.FirstOrDefault(s =>
                s.Name.Equals(".data", StringComparison.OrdinalIgnoreCase));
            if (data == null || data.RawSize <= 0)
                return [];

            int start = Math.Max(0, data.RawPointer);
            int end = Math.Min(bytes.Length, data.RawPointer + data.RawSize);
            var hits = new List<ColorCandidate>();

            for (int offset = start; offset <= end - 12; offset += 4)
            {
                if (IsInsideIdentityMatrixRun(bytes, offset, start, end))
                    continue;

                float r = BitConverter.ToSingle(bytes, offset);
                float g = BitConverter.ToSingle(bytes, offset + 4);
                float b = BitConverter.ToSingle(bytes, offset + 8);
                float a = offset + 16 <= end ? BitConverter.ToSingle(bytes, offset + 12) : 1f;

                int score = ScoreBlueTint(r, g, b, a);
                if (score <= 0)
                    continue;

                hits.Add(new ColorCandidate(
                    offset,
                    $"0x{TryRva(inspection, offset):X}",
                    r, g, b, a,
                    score,
                    BuildNote(bytes, offset, start, end)));
            }

            return hits
                .GroupBy(h => h.FileOffset)
                .Select(g => g.First())
                .OrderByDescending(h => h.Score)
                .ThenBy(h => h.FileOffset)
                .Take(maxResults)
                .ToList();
        }

        static bool IsInsideIdentityMatrixRun(byte[] bytes, int offset, int start, int end)
        {
            int runStart = offset;
            while (runStart >= start && IsUnitFloat(bytes, runStart))
                runStart -= 4;

            runStart += 4;
            int runLen = 0;
            for (int p = runStart; p <= end - 4 && IsUnitFloat(bytes, p); p += 4)
                runLen++;

            return runLen >= 12 && offset >= runStart && offset < runStart + runLen;
        }

        static bool IsUnitFloat(byte[] bytes, int offset) =>
            offset + 4 <= bytes.Length
            && BitConverter.ToSingle(bytes, offset) is >= 0.92f and <= 1.08f;

        static int ScoreBlueTint(float r, float g, float b, float a)
        {
            if (!float.IsFinite(r) || !float.IsFinite(g) || !float.IsFinite(b) || !float.IsFinite(a))
                return 0;
            if (r is < 0.04f or > 1.05f || g is < 0.04f or > 1.05f || b is < 0.15f or > 1.05f)
                return 0;
            if (a is < 0f or > 1.1f)
                return 0;
            if (b <= r + 0.08f || b <= g + 0.05f)
                return 0;

            int score = 40;
            if (b >= 0.85f)
                score += 40;
            else if (b >= 0.55f)
                score += 25;
            if (r <= 0.35f && g <= 0.55f)
                score += 20;
            if (Math.Abs(a - 1f) < 0.05f)
                score += 10;
            return score;
        }

        static string BuildNote(byte[] bytes, int offset, int start, int end)
        {
            bool hasAlpha = offset + 16 <= end;
            if (!hasAlpha)
                return "vec3 blue tint";

            float a = BitConverter.ToSingle(bytes, offset + 12);
            return Math.Abs(a - 1f) < 0.05f ? "vec4 blue tint" : $"vec4 blue tint a={a:F3}";
        }

        static int TryRva(MagicDllInspection inspection, int fileOffset)
        {
            foreach (MagicDllSection section in inspection.Sections)
            {
                int s = section.RawPointer;
                int e = s + section.RawSize;
                if (fileOffset >= s && fileOffset < e)
                    return section.VirtualAddress + (fileOffset - s);
            }

            return fileOffset;
        }

        public static string FormatCandidateLine(ColorCandidate c) =>
            $"0x{c.FileOffset:X6} RVA={c.RvaHex} ({c.R.ToString("F3", CultureInfo.InvariantCulture)},"
            + $"{c.G.ToString("F3", CultureInfo.InvariantCulture)},"
            + $"{c.B.ToString("F3", CultureInfo.InvariantCulture)},"
            + $"{c.A.ToString("F3", CultureInfo.InvariantCulture)}) score={c.Score} {c.Note}";
    }
}

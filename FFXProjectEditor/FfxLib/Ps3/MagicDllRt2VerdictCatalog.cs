using System;
using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.Ps3
{
    /// <summary>
    /// Offline RT2 verdicts from ThundaFira / Flan Flood labs — keyed by donor magic id + PE file offset.
    /// Source: <c>docs/reverse/FFX_MAGIC_DLL_RT2_ATTEMPT_LEDGER_2026-06-15.md</c>
    /// </summary>
    internal static class MagicDllRt2VerdictCatalog
    {
        internal enum VerdictKind
        {
            None,
            DeadNoVisual,
            DeadTransform,
            DeadSoftlock,
            TimingProved,
        }

        internal readonly struct Overlay(
            VerdictKind kind,
            string hypothesis,
            string confidence,
            string symptom,
            string ledgerId,
            int scoreCap)
        {
            public VerdictKind Kind { get; } = kind;
            public string Hypothesis { get; } = hypothesis;
            public string Confidence { get; } = confidence;
            public string Symptom { get; } = symptom;
            public string LedgerId { get; } = ledgerId;
            public int ScoreCap { get; } = scoreCap;
            public bool PatchBlocked => Kind is VerdictKind.DeadNoVisual or VerdictKind.DeadTransform or VerdictKind.DeadSoftlock;
            public bool IsTimingProved => Kind == VerdictKind.TimingProved;
            public static Overlay None => default;
            public bool HasVerdict => Kind != VerdictKind.None;
        }

        sealed record OffsetEntry(
            int DonorMagicId,
            int FileOffset,
            VerdictKind Kind,
            string Hypothesis,
            string Symptom,
            string LedgerId);

        static readonly OffsetEntry[] OffsetTable =
        [
            // Thundaga cast DLL (0094) — ThundaFira F1 RT2−
            new(94, 0x366C0, VerdictKind.DeadNoVisual,
                "RT2−: PPP static vec4 — zero cor visual (Anim1 draw não lê)",
                "Cast ThundaFira: raios iguais Thundaga",
                "T03"),
            new(94, 0x37D64, VerdictKind.DeadNoVisual,
                "RT2−: PPP static vec4 — zero cor visual",
                "Cast ThundaFira: sem mudança",
                "T04"),
            new(94, 0x37D04, VerdictKind.DeadNoVisual,
                "RT2−: PPP static vec4 — zero cor visual",
                "Cast ThundaFira: sem mudança",
                "T05"),
            new(94, 0x31640, VerdictKind.DeadTransform,
                "RT2−: matriz PPP 3D — NÃO tint; patch destrói raio (2D desconexo)",
                "Raios 2D / transform quebrado",
                "T07"),
            new(94, 0x3164C, VerdictKind.DeadTransform,
                "RT2−: matriz PPP 3D — NÃO tint; patch destrói raio (2D desconexo)",
                "Raios 2D / transform quebrado",
                "T07"),
            new(94, 0x39DF0, VerdictKind.DeadNoVisual,
                "RT2−: vec4 G1 — score offline alto; zero cor in-game (chão)",
                "ThundaFira indistinguível de Thundaga",
                "T10"),
            new(94, 0x44180, VerdictKind.DeadNoVisual,
                "RT2−: vec4 G1 — score offline alto; zero cor in-game (chão)",
                "ThundaFira indistinguível de Thundaga",
                "T10"),
            new(94, 0x37654, VerdictKind.DeadNoVisual,
                "RT2−: vec4 G2 — família (0.3,0.5,0.8); zero cor in-game",
                "ThundaFira indistinguível de Thundaga",
                "T11"),
            new(94, 0x376B4, VerdictKind.DeadNoVisual,
                "RT2−: vec4 G2 — família (0.3,0.5,0.8); zero cor in-game",
                "ThundaFira indistinguível de Thundaga",
                "T11"),
            new(94, 0x37710, VerdictKind.DeadNoVisual,
                "RT2−: PE_tint_false_positive — 0 xrefs IDA; zero cor visual",
                "Raios iguais Thundaga após patch laranja",
                "T14"),
            new(94, 0x8C58, VerdictKind.DeadNoVisual,
                "RT2−: dataA cyan vec4 — não é cor runtime (2-site patch)",
                "Visual = Thundaga; sem softlock",
                "T13"),
            new(94, 0x8C7C, VerdictKind.DeadNoVisual,
                "RT2−: dataA cyan vec4 — não é cor runtime (2-site patch)",
                "Visual = Thundaga; sem softlock",
                "T13"),
        ];

        /// <summary>Clone slot → donor DLL for inherited PE layout (byte-identical clones).</summary>
        static readonly Dictionary<int, int> CloneToDonor = new()
        {
            [716] = 94,
            [717] = 95,
            [718] = 96,
            [719] = 97,
        };

        /// <summary>Magic ids where blue-dominant vec4 patch antecipou dano (Flan Flood RT2).</summary>
        static readonly HashSet<int> TimingProvedBlueVec4MagicIds = [96, 718];

        public static int ResolveDonorMagicId(int? magicId)
        {
            if (magicId is not int id)
                return -1;
            return CloneToDonor.TryGetValue(id, out int donor) ? donor : id;
        }

        public static Overlay Resolve(
            int? magicId,
            MagicDllEffectFamily family,
            int fileOffset,
            bool isVec4,
            bool blueDominantTimingHeuristic)
        {
            if (magicId is int id && TimingProvedBlueVec4MagicIds.Contains(id)
                && blueDominantTimingHeuristic && isVec4)
            {
                return new Overlay(
                    VerdictKind.TimingProved,
                    "RT2+: timing cast→hit — patch antecipou dano; NÃO é cor visual (Flan Flood 0718)",
                    "rt2-timing",
                    "Damage before visual; restore DLL = blue opener intact",
                    "T25",
                    scoreCap: 120);
            }

            int donor = ResolveDonorMagicId(magicId);
            if (donor < 0)
                return Overlay.None;

            foreach (OffsetEntry entry in OffsetTable)
            {
                if (entry.DonorMagicId != donor || entry.FileOffset != fileOffset)
                    continue;

                string confidence = entry.Kind switch
                {
                    VerdictKind.DeadTransform => "rt2-dead-transform",
                    VerdictKind.DeadSoftlock => "rt2-dead-softlock",
                    _ => "rt2-dead",
                };

                return new Overlay(
                    entry.Kind,
                    entry.Hypothesis,
                    confidence,
                    entry.Symptom,
                    entry.LedgerId,
                    scoreCap: 8);
            }

            return Overlay.None;
        }

        public static string ClassifyUnprovenVectorHypothesis(bool hasFourth) =>
            hasFourth
                ? "Hipótese cor/transform vec4 — precisa RT2"
                : "Hipótese cor/transform vec3 — precisa RT2";
    }
}

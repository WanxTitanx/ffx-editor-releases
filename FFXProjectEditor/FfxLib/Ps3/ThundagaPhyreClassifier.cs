using System;

namespace FFXProjectEditor.FfxLib.Ps3
{
    /// <summary>
    /// RT2 (2026-06-14): Thundaga <c>0094/0716</c> = Family D (PPP/EgoTask), not Family A particles.
    /// Phyere <c>_256_128</c>/<c>_512_256</c> = burst sheets (Anim2 / <c>0717</c>).
    /// RT2 Halyson (2026-06-14): <c>_128_128</c> on <c>0716</c> = ground impact flash, NOT bolt rays.
    /// IDA+preview (2026-06-15): phyre <c>13568_…_256_128</c> = converging bolt streaks (paired with mask <c>13440</c> in DLL).
    /// <c>_128_64</c> recolor does not change bolts. DLL vec4 tint sites RT2− (incl. <c>0x37710</c>).
    /// </summary>
    internal static class ThundagaPhyreClassifier
    {
        public enum SheetRole
        {
            Unknown,
            /// <summary>RT2 retired for bolt rays — no phyre sheet proven yet.</summary>
            Bolt,
            Explosion,
            /// <summary><c>_128_64</c> on <c>0094</c> only; RT2 negative for bolt color.</summary>
            Anim1Only,
            /// <summary><c>_128_128</c> — ground marker / impact flash on <c>0716</c> (RT2 proved).</summary>
            GroundImpactFlash,
        }

        public static SheetRole ClassifyFileName(string fileName)
        {
            // KeThRes bind pair in magic_0094 @ 0x10031B90 — bolt + mask (not explosion).
            if (fileName.Contains("13568_", StringComparison.Ordinal)
                || fileName.Contains("13440_", StringComparison.Ordinal))
                return SheetRole.Bolt;

            if (fileName.Contains("_128_64", StringComparison.Ordinal))
                return SheetRole.Anim1Only;

            if (fileName.Contains("_128_128", StringComparison.Ordinal))
                return SheetRole.GroundImpactFlash;

            if (fileName.Contains("_256_128", StringComparison.Ordinal)
                || fileName.Contains("_512_256", StringComparison.Ordinal))
                return SheetRole.Explosion;

            return SheetRole.Unknown;
        }

        public static bool IsBoltSheet(string fileName) => ClassifyFileName(fileName) == SheetRole.Bolt;
        public static bool IsExplosionSheet(string fileName) => ClassifyFileName(fileName) == SheetRole.Explosion;
        public static bool IsGroundImpactFlashSheet(string fileName) =>
            ClassifyFileName(fileName) == SheetRole.GroundImpactFlash;
    }
}

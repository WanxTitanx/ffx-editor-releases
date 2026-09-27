using System;
using System.IO;

namespace FFXProjectEditor.FfxLib.Ps3
{
    /// <summary>
    /// RT2 FAIL (2026-06-15): heurística <c>BlueOnly</c> em <c>magic_0718.dll</c> antecipou dano;
    /// azul do opener persistiu após restore — candidatos são <b>possível timing</b>, não tinte visual.
    /// Ver <c>docs/reverse/FFX_FLAN_FLOOD_DLL_TIMING_RT2_FAIL_2026-06-15.md</c>.
    /// </summary>
    internal static class FlanFloodDllPossibleTimingPatch
    {
        public const string CastDllPossibleTimingBackupSuffix = ".backup_flameflan_flood_dll";

        public const int CastCloneMagicId = 718;

        /// <summary>Restaura <c>magic_0718.dll</c> antes de qualquer patch de possível-timing.</summary>
        public static bool RestoreCastDllFromPossibleTimingBackup(string gameRoot)
        {
            string dll = ResolveCastDllPath(gameRoot);
            string backup = dll + CastDllPossibleTimingBackupSuffix;
            if (!File.Exists(backup))
                return false;
            File.Copy(backup, dll, overwrite: true);
            return true;
        }

        /// <summary>
        /// Aposentado — opt-in só para RE. Patcheia até 3 vec4 blue-dominant (possível cast→hit sync).
        /// </summary>
        public static int TryPatchCastDllPossibleTimingVec4(string gameRoot, int maxPatches = 3)
        {
            string dll = ResolveCastDllPath(gameRoot);
            if (!File.Exists(dll))
                return 0;

            string backup = dll + CastDllPossibleTimingBackupSuffix;
            if (!File.Exists(backup))
                File.Copy(dll, backup, overwrite: false);
            File.Copy(backup, dll, overwrite: true);

            PrismMagicDllRecolor.RecolorResult result = PrismMagicDllRecolor.Apply(
                dll,
                dll,
                1.0f,
                0.28f,
                0.06f,
                1.0f,
                maxPatches,
                PrismMagicDllRecolor.PatchCandidateFilter.PossibleTimingBlueDominant);

            return result.PatchCount;
        }

        static string ResolveCastDllPath(string gameRoot) =>
            Path.Combine(gameRoot, "magicFiles", "FFX", $"magic_{CastCloneMagicId:D4}.dll");
    }
}

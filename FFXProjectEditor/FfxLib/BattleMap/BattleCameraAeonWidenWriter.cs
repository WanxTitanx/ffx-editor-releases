using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.BattleMap
{
    /// <summary>
    /// Pulls battle camera distance (camSetPolar 3rd arg) toward a proven wide multi-boss donor while keeping the
    /// scenario's own chunk0 script (desert/outdoor shots stay coherent — unlike replacing all of chunk0).
    /// </summary>
    public static class BattleCameraAeonWidenWriter
    {
        public const float DefaultDistanceScale = 1.45f;
        public const float MinPolarDistance = 48f;

        /// <summary>Scale every camSetPolar distance float pool entry by <paramref name="distanceScale"/>.</summary>
        public static byte[] WidenPolarDistances(byte[] battleBin, string battleId, float distanceScale = DefaultDistanceScale)
        {
            ArgumentNullException.ThrowIfNull(battleBin);
            if (distanceScale <= 1f) return battleBin;

            var setup = BattleCameraSetup_File.ReadFromBattleBin(battleId, battleBin);
            if (!setup.HasCamera || setup.Script == null)
                return battleBin;

            var distanceIndices = CollectPolarDistancePoolIndices(setup);
            if (distanceIndices.Count == 0)
                return battleBin;

            byte[] output = battleBin;
            foreach (int poolIndex in distanceIndices.OrderBy(i => i))
            {
                var current = BattleCameraSetup_File.ReadFromBattleBin(battleId, output);
                var knob = current.FloatParams.FirstOrDefault(p => p.PoolIndex == poolIndex);
                if (knob == null) continue;
                float scaled = MathF.Max(MinPolarDistance, knob.Value * distanceScale);
                output = current.WithFloat(poolIndex, scaled);
            }
            return output;
        }

        /// <summary>
        /// Match establishing-shot distance to <paramref name="donorBin"/> (e.g. nagi05_24 quad), clamped to a safe range.
        /// </summary>
        public static byte[] WidenTowardDonor(byte[] battleBin, string battleId, byte[] donorBin, string donorId)
        {
            ArgumentNullException.ThrowIfNull(battleBin);
            ArgumentNullException.ThrowIfNull(donorBin);

            var target = BattleCameraSetup_File.ReadFromBattleBin(battleId, battleBin);
            var donor = BattleCameraSetup_File.ReadFromBattleBin(donorId, donorBin);
            float targetDist = target.Establishing?.PolarDistance ?? 0f;
            float donorDist = donor.Establishing?.PolarDistance ?? 0f;
            if (targetDist <= 1f || donorDist <= 1f)
                return WidenPolarDistances(battleBin, battleId);

            float scale = Math.Clamp(donorDist / targetDist, 1.12f, 1.85f);
            return WidenPolarDistances(battleBin, battleId, scale);
        }

        private static HashSet<int> CollectPolarDistancePoolIndices(BattleCameraSetup_File setup)
        {
            var indices = new HashSet<int>();
            foreach (CameraCall call in setup.Calls.Where(c => c.Role == "polar"))
            {
                var floats = call.Args.Where(a => a.Kind == CamArgKind.FloatConst).ToList();
                if (floats.Count >= 3)
                    indices.Add(floats[2].PoolIndex);
            }
            return indices;
        }
    }
}

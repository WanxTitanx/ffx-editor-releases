using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using FFXProjectEditor.Services;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    /// <summary>
    /// SIN curse runtime state — sidecar files read/write for the DLL hook and CLI baker.
    /// Sidecar lives under <c>modules/config/</c> (same as GridTeach, Lancet flags).
    /// </summary>
    public static class SinCurseSidecarIO
    {
        public const string FlagFileName = "sin_curse.flag";
        public const string IntensityFileName = "sin_f7_intensity.flag";
        public const string StateFileName = "sin_curse_state.json";
        private const string ConfigRootEnvVar = "FFX_MODULES_CONFIG_ROOT";

        /// <summary>F7 intensity levels.</summary>
        public enum SinIntensity
        {
            Off,
            Betinha,
            Default,
            Alfa,
        }

        /// <summary>Runtime state persisted between field transitions.</summary>
        public sealed class SinCurseState
        {
            public string RegionId { get; set; } = "";
            public string FieldPath { get; set; } = "";
            public int AreaThreatCap { get; set; }
            public SinIntensity Intensity { get; set; } = SinIntensity.Default;
            public int Seed { get; set; }
            public List<InfectedMonster> InfectedMonsters { get; set; } = new();
            public string BakeManifestPath { get; set; } = "";
        }

        public sealed class InfectedMonster
        {
            public string MonsterId { get; set; } = "";
            public string PresetId { get; set; } = "";
            public int Threat { get; set; }
            public float Scale { get; set; } = 1.8f;
        }

        /// <summary>Resolve the config directory (same as GridTeach: modules/config/ under game root).</summary>
        public static string DefaultConfigDir()
        {
            string? configured = Environment.GetEnvironmentVariable(ConfigRootEnvVar);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                string explicitPath = Path.GetFullPath(configured);
                Directory.CreateDirectory(explicitPath);
                return explicitPath;
            }

            string? gameRoot = PortablePathResolver.GameInstallRoot;
            if (!string.IsNullOrWhiteSpace(gameRoot))
            {
                string gameConfig = Path.Combine(gameRoot, "modules", "config");
                Directory.CreateDirectory(gameConfig);
                return gameConfig;
            }

            string? bundled = PortablePathResolver.BundledPath("modules", "config");
            if (bundled != null)
                return bundled;

            throw new DirectoryNotFoundException(
                $"FFX modules/config is unavailable; load or detect the game, or set {ConfigRootEnvVar}.");
        }

        /// <summary>Check if SIN curse flag exists (hook gate).</summary>
        public static bool IsSinCurseEnabled(string configDir)
        {
            return File.Exists(Path.Combine(configDir, FlagFileName));
        }

        /// <summary>Read F7 intensity from flag file. Returns Default if file missing.</summary>
        public static SinIntensity ReadIntensity(string configDir)
        {
            string path = Path.Combine(configDir, IntensityFileName);
            if (!File.Exists(path))
                return SinIntensity.Default;

            string content = File.ReadAllText(path).Trim().ToLowerInvariant();
            return content switch
            {
                "off" => SinIntensity.Off,
                "betinha" => SinIntensity.Betinha,
                "alfa" => SinIntensity.Alfa,
                _ => SinIntensity.Default,
            };
        }

        /// <summary>Write F7 intensity flag.</summary>
        public static void WriteIntensity(string configDir, SinIntensity intensity)
        {
            string content = intensity switch
            {
                SinIntensity.Off => "off",
                SinIntensity.Betinha => "betinha",
                SinIntensity.Default => "default",
                SinIntensity.Alfa => "alfa",
                _ => "default",
            };
            File.WriteAllText(Path.Combine(configDir, IntensityFileName), content);
        }

        /// <summary>Read runtime state from sidecar JSON.</summary>
        public static SinCurseState? ReadState(string configDir)
        {
            string path = Path.Combine(configDir, StateFileName);
            if (!File.Exists(path))
                return null;

            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                return JsonSerializer.Deserialize<SinCurseState>(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Write runtime state to sidecar JSON.</summary>
        public static void WriteState(string configDir, SinCurseState state)
        {
            string path = Path.Combine(configDir, StateFileName);
            string json = JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true,
            });
            File.WriteAllText(path, json, Encoding.UTF8);
        }

        /// <summary>Parse intensity from string (for CLI args).</summary>
        public static SinIntensity ParseIntensity(string? text)
        {
            return (text ?? "").Trim().ToLowerInvariant() switch
            {
                "off" => SinIntensity.Off,
                "betinha" => SinIntensity.Betinha,
                "default" => SinIntensity.Default,
                "alfa" => SinIntensity.Alfa,
                _ => SinIntensity.Default,
            };
        }

        /// <summary>Get infection probability for a given intensity (0.0 to 1.0).</summary>
        public static double GetInfectionProbability(SinIntensity intensity) => intensity switch
        {
            SinIntensity.Off => 0.0,
            SinIntensity.Betinha => 0.30,
            SinIntensity.Default => 0.60,
            SinIntensity.Alfa => 1.0,
            _ => 0.60,
        };
    }
}

using System;
using System.IO;
using System.Text.Json;
using FFXProjectEditor.Diagnostics;

namespace FFXProjectEditor.Core
{
    /// <summary>
    /// Feature flag for the AI Assistant module (Jarvis-UI, 2026-09-15).
    ///
    /// WHY this exists: the assistant is a BYOK experiment the owner may or may not
    /// maintain long-term — so it ships DISABLED BY DEFAULT and only enters the
    /// navigation surface (rail / dashboard grid / Ctrl+K palette / Dispatch) when
    /// the user opts in from the toggle next to the music control. The flag lives
    /// in its own file on purpose: the module must not gate itself, and deleting
    /// the whole feature later is one file + one registry line.
    ///
    /// Persisted at %LOCALAPPDATA%/FFXProjectEditor/feature-flags.json
    /// (same convention as ai-assistant.json / last-project.txt). Default: OFF.
    /// <see cref="Changed"/> fires on every committed toggle so the shell can
    /// rebuild the icon rail and bounce an open AI panel back to Home.
    /// </summary>
    public static class AiFeatureGate
    {
        const string Area = "AiFeatureGate";

        /// <summary>Module id this gate controls — single source for the registry filter.</summary>
        public const string ModuleId = "ai-assistant";

        // Test seam: tests point this at a temp file. Production uses LocalAppData.
        internal static string? SettingsPathOverride;
        static string SettingsFilePath => SettingsPathOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FFXProjectEditor", "feature-flags.json");

        sealed class PersistedFlags
        {
            public bool AiAssistant { get; set; }
        }

        static bool? _enabled;

        /// <summary>Raised after <see cref="Enabled"/> commits a new value (shell rebuild hook).</summary>
        public static event Action? Changed;

        /// <summary>True when the user opted in. Default false; missing/corrupt file → false.</summary>
        public static bool Enabled
        {
            get => _enabled ??= Load();
            set
            {
                if (Enabled == value) return;
                _enabled = value;
                Persist(value);
                DebugLog.Info(Area, $"ai-assistant gate → {(value ? "ENABLED" : "disabled")}");
                Changed?.Invoke();
            }
        }

        /// <summary>Reset the cached value (tests only — forces a reload on next read).</summary>
        internal static void ResetForTests() => _enabled = null;

        static bool Load()
        {
            try
            {
                if (!File.Exists(SettingsFilePath)) return false;
                var flags = JsonSerializer.Deserialize<PersistedFlags>(File.ReadAllText(SettingsFilePath));
                return flags?.AiAssistant ?? false;
            }
            catch (Exception ex)
            {
                // Corrupt/unreadable flags must never kill the shell — feature stays off.
                DebugLog.Warn(Area, $"flags load failed ({ex.GetType().Name}) — default OFF");
                return false;
            }
        }

        static void Persist(bool value)
        {
            try
            {
                string path = SettingsFilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(new PersistedFlags { AiAssistant = value }));
            }
            catch (Exception ex)
            {
                DebugLog.Warn(Area, $"flags save failed ({ex.GetType().Name})");
            }
        }
    }
}

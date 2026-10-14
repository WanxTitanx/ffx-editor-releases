using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace FFXProjectEditor.FfxLib.Ai
{
    // USER-DEFINED command labels (#7): persist friendly names for performCommand ids that the bundled command
    // dictionaries don't name (e.g. an unnamed "comando 0x60AB" / "Command 93"). The owner asked to let the user
    // label the nameless ids; the name then shows wherever that command id is decoded, across every monster.
    //
    // PURE in-memory by default. The editor calls Load() once at startup and Save() after each edit; the AiScriptLab
    // gate never calls Load(), so the override map is empty there and AiAutomation.DetectActions names exactly as
    // before (the --ai3 gate stays PASS). Keyed by the FULL u16 operand ((category<<12)|id) so a label is unambiguous
    // across command tables. System.* only (System.Text.Json), keeping FfxLib/Ai dependency-free.
    public static class AiCommandLabels
    {
        static readonly Dictionary<ushort, string> _labels = new();

        /// <summary>Raised whenever the override map changes (set/clear/load) so bound UIs can re-render.</summary>
        public static event Action? Changed;

        /// <summary>The live override map (operand -> user label). Read-only view.</summary>
        public static IReadOnlyDictionary<ushort, string> All => _labels;

        /// <summary>The user label for an operand, or null when none is set.</summary>
        public static string? Get(ushort operand) => _labels.TryGetValue(operand, out string? v) ? v : null;

        /// <summary>Set (or, when label is blank, clear) the user label for an operand. Returns true if the map
        /// actually changed (so the caller can decide whether to persist + refresh).</summary>
        public static bool Set(ushort operand, string? label)
        {
            string trimmed = (label ?? string.Empty).Trim();
            bool changed;
            if (trimmed.Length == 0)
            {
                changed = _labels.Remove(operand);
            }
            else
            {
                _labels.TryGetValue(operand, out string? cur);
                changed = !string.Equals(cur, trimmed, StringComparison.Ordinal);
                _labels[operand] = trimmed;
            }
            if (changed) Changed?.Invoke();
            return changed;
        }

        /// <summary>Remove the user label for an operand (back to the dictionary/hex default).</summary>
        public static bool Clear(ushort operand) => Set(operand, null);

        /// <summary>Replace ALL labels from a persisted JSON file (operand-hex string -> label). Best-effort: a
        /// missing or malformed file leaves the map empty. Never throws.</summary>
        public static void Load(string path)
        {
            try
            {
                _labels.Clear();
                if (File.Exists(path))
                {
                    Dictionary<string, string>? map =
                        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                    if (map != null)
                        foreach (KeyValuePair<string, string> kv in map)
                            if (ushort.TryParse(kv.Key.Replace("0x", "", StringComparison.OrdinalIgnoreCase),
                                    NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort op)
                                && !string.IsNullOrWhiteSpace(kv.Value))
                                _labels[op] = kv.Value.Trim();
                }
            }
            catch { /* best-effort — persistence is a convenience, never a hard requirement */ }
            Changed?.Invoke();
        }

        /// <summary>Persist the labels to a JSON file (operand-hex string -> label). Best-effort; never throws.</summary>
        public static void Save(string path)
        {
            try
            {
                string? dir = Path.GetDirectoryName(path);
                if (dir != null) Directory.CreateDirectory(dir);
                var map = new Dictionary<string, string>(_labels.Count);
                foreach (KeyValuePair<ushort, string> kv in _labels) map[kv.Key.ToString("X4")] = kv.Value;
                File.WriteAllText(path, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* best-effort */ }
        }
    }
}

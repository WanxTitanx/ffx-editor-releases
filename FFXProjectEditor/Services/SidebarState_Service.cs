using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FFXProjectEditor.Services
{
    // Tiny persistence for the sidebar section collapse state. Mirrors Project_Service's LocalAppData
    // convention (last-project.txt) so a user's collapsed sections survive across sessions. Convenience
    // only — any failure is swallowed, the sidebar just opens expanded.
    public static class SidebarState_Service
    {
        static readonly string StatePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FFXProjectEditor", "sidebar-state.json");

        static readonly Dictionary<string, bool> _collapsed = Load();

        static Dictionary<string, bool> Load()
        {
            try
            {
                if (File.Exists(StatePath))
                    return JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(StatePath)) ?? new();
            }
            catch { /* ignore — fall back to all-expanded */ }
            return new();
        }

        public static bool IsCollapsed(string key) => _collapsed.TryGetValue(key, out bool v) && v;

        public static void SetCollapsed(string key, bool collapsed)
        {
            _collapsed[key] = collapsed;
            try
            {
                string? dir = Path.GetDirectoryName(StatePath);
                if (dir != null) Directory.CreateDirectory(dir);
                File.WriteAllText(StatePath, JsonSerializer.Serialize(_collapsed));
            }
            catch { /* persistence is convenience, not a requirement */ }
        }
    }
}

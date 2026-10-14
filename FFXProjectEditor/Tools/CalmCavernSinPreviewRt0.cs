using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.MagicDllEditor;

namespace FFXProjectEditor.Tools
{
    internal static class CalmCavernSinPreviewRt0
    {
        sealed class ClonePlan
        {
            public int Clone { get; set; }
            public string Name { get; set; } = "";
        }

        public static int Run(string planPath, string stageRoot, string siteRoot, string resultPath)
        {
            Program.BuildAvaloniaApp().SetupWithoutStarting();
            List<ClonePlan> plan = JsonSerializer.Deserialize<List<ClonePlan>>(
                File.ReadAllText(planPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Missing VFX preview plan.");
            string dllRoot = Path.Combine(stageRoot, "magicFiles", "FFX");
            string texRoot = Path.Combine(stageRoot, "data", "mods", "FFX_Data", "GameData", "PS3Data", "magic");
            string viewerDataRoot = Path.Combine(siteRoot, "viewer-data");
            Directory.CreateDirectory(viewerDataRoot);
            var parser = new MagicDllParser();
            var results = new List<object>();
            int failed = 0;
            foreach (ClonePlan item in plan)
            {
                string dll = Path.Combine(dllRoot, $"magic_{item.Clone:D4}.dll");
                try
                {
                    MagicDllFile parsed = parser.Parse(dll);
                    var session = new MagicPreviewSession(viewerDataRoot);
                    session.Publish(parsed, File.ReadAllBytes(dll), item.Name, new[] { texRoot });
                    string manifest = Path.Combine(viewerDataRoot, "magic-preview", session.Id, "current.json");
                    using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(manifest));
                    JsonElement root = doc.RootElement;
                    int textures = root.GetProperty("textures").GetArrayLength();
                    int warnings = root.GetProperty("textureWarnings").GetArrayLength();
                    results.Add(new { id = item.Clone, name = item.Name, ok = true,
                        session = session.Id, textures, warnings, roots = root.GetProperty("rootCount").GetInt32(),
                        url = $"/noclip/index.html?magic={item.Clone}&magicPreview={session.Id}#ffx/magic-studio" });
                    Console.WriteLine($"{item.Clone:D4} {item.Name}: {textures} textures, {warnings} warnings, {session.Id}");
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
                {
                    failed++;
                    results.Add(new { id = item.Clone, name = item.Name, ok = false, error = ex.Message });
                    Console.Error.WriteLine($"{item.Clone:D4} {item.Name}: {ex.Message}");
                }
            }
            File.WriteAllText(resultPath, JsonSerializer.Serialize(results,
                new JsonSerializerOptions { WriteIndented = true }));
            return failed == 0 && results.Count == plan.Count ? 0 : 1;
        }
    }
}

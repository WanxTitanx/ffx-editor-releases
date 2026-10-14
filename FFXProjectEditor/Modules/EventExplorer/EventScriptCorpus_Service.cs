using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FFXProjectEditor.Modules.EventExplorer
{
    internal static class EventScriptCorpus_Service
    {
        sealed class CorpusSnapshot
        {
            public required bool IsAvailable { get; init; }
            public required string StatusSummary { get; init; }
            public required IReadOnlyDictionary<string, string> ScriptByEventId { get; init; }
        }

        static readonly Regex BlockHeaderRegex = new(@"^--- .*[/\\](?<id>[a-z0-9_]+)\.ebp ---$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Lazy<CorpusSnapshot> Snapshot = new(LoadSnapshot);

        public static bool IsAvailable => Snapshot.Value.IsAvailable;
        public static string StatusSummary => Snapshot.Value.StatusSummary;

        public static string? GetScriptPreview(string eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId))
                return null;

            return Snapshot.Value.ScriptByEventId.TryGetValue(eventId, out string? preview)
                ? preview
                : null;
        }

        static CorpusSnapshot LoadSnapshot()
        {
            string[] paths = ResolveCorpusPaths();
            if (paths.Length == 0)
            {
                return new CorpusSnapshot
                {
                    IsAvailable = false,
                    StatusSummary = "Event parser corpus not found. Put eventScriptOutput.txt or blitzballOutput.txt in Downloads to surface full decompiled scripts here.",
                    ScriptByEventId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                };
            }

            try
            {
                Dictionary<string, string> byId = new(StringComparer.OrdinalIgnoreCase);
                List<string> loadedFiles = new();

                foreach (string path in paths)
                {
                    LoadFile(path, byId);
                    loadedFiles.Add(Path.GetFileName(path));
                }

                return new CorpusSnapshot
                {
                    IsAvailable = byId.Count > 0,
                    StatusSummary = $"Loaded event script corpus from {string.Join(", ", loadedFiles)} ({byId.Count} event blocks).",
                    ScriptByEventId = byId
                };
            }
            catch (Exception ex)
            {
                return new CorpusSnapshot
                {
                    IsAvailable = false,
                    StatusSummary = $"Failed to parse event corpus: {ex.Message}",
                    ScriptByEventId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                };
            }
        }

        static void LoadFile(string path, IDictionary<string, string> target)
        {
            string? currentId = null;
            StringBuilder? currentBuilder = null;

            foreach (string line in File.ReadLines(path))
            {
                Match headerMatch = BlockHeaderRegex.Match(line);
                if (headerMatch.Success)
                {
                    FlushCurrent(target, currentId, currentBuilder);
                    currentId = headerMatch.Groups["id"].Value;
                    currentBuilder = new StringBuilder();
                }

                currentBuilder?.AppendLine(line);
            }

            FlushCurrent(target, currentId, currentBuilder);
        }

        static void FlushCurrent(IDictionary<string, string> target, string? currentId, StringBuilder? currentBuilder)
        {
            if (string.IsNullOrWhiteSpace(currentId) || currentBuilder == null || currentBuilder.Length == 0)
                return;

            if (!target.ContainsKey(currentId))
            {
                target.Add(currentId, currentBuilder.ToString().Trim());
            }
        }

        static string[] ResolveCorpusPaths()
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] candidatePaths =
            [
                Path.Combine(userProfile, "Downloads", "eventScriptOutput.txt"),
                Path.Combine(userProfile, "Downloads", "blitzballOutput.txt"),
                Path.Combine(AppContext.BaseDirectory, "eventScriptOutput.txt"),
                Path.Combine(AppContext.BaseDirectory, "blitzballOutput.txt"),
                Path.Combine(Directory.GetCurrentDirectory(), "eventScriptOutput.txt"),
                Path.Combine(Directory.GetCurrentDirectory(), "blitzballOutput.txt")
            ];

            return candidatePaths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }
}

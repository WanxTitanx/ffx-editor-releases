using FFXProjectEditor.Resources;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Modules.AuroraFieldExplorer
{
    /// <summary>One overworld field map (<c>map/&lt;area&gt;/&lt;field&gt;</c>) from the batch CSV.</summary>
    internal sealed class FieldMapRow
    {
        public required string MapEntity { get; init; }
        public required string Area { get; init; }
        public required string FieldToken { get; init; }

        public string DisplayName => FieldToken;
        public string Summary => MapEntity;
        public string DetailSummary => $"area {Area} · HD field map (overworld)";

        public static FieldMapRow? TryParseCsvLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('"') && line.Contains("entity"))
                return null;

            string[] parts = line.Split(',');
            if (parts.Length < 3)
                return null;

            string entity = parts[0].Trim().Trim('"');
            string area = parts[1].Trim().Trim('"');
            string token = parts[2].Trim().Trim('"');
            if (!entity.StartsWith("map/", StringComparison.OrdinalIgnoreCase) || token.Length == 0)
                return null;

            return new FieldMapRow
            {
                MapEntity = entity,
                Area = area,
                FieldToken = token,
            };
        }
    }

    /// <summary>Loads the ~299 HD field maps list (same corpus as MapViewer batch).</summary>
    internal static class AuroraFieldExplorer_FieldCatalog
    {
        const string CsvFileName = "map-entities.csv";
        static readonly string CsvRepoRelative =
            Path.Combine("RuntimeTools", "FFXMapViewerWeb", "prompts", "2026-06-03-map-batch-web-ready", CsvFileName);

        public static IReadOnlyList<FieldMapRow> LoadAll(out string? error)
        {
            error = null;
            string? path = ResolveCsvPath();
            if (path == null)
            {
                error = string.Format(Strings.U_Au_CsvNotFound, CsvRepoRelative);
                return Array.Empty<FieldMapRow>();
            }

            try
            {
                var rows = new List<FieldMapRow>();
                foreach (string line in File.ReadLines(path))
                {
                    FieldMapRow? row = FieldMapRow.TryParseCsvLine(line);
                    if (row != null)
                        rows.Add(row);
                }

                return rows
                    .OrderBy(r => r.Area, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(r => r.FieldToken, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return Array.Empty<FieldMapRow>();
            }
        }

        public static string? ResolveCsvPath()
        {
            string copied = Path.Combine(AppContext.BaseDirectory, "AuroraFieldExplorer", CsvFileName);
            if (File.Exists(copied))
                return copied;
            return FindUpwards(CsvRepoRelative);
        }

        static string? FindUpwards(string relativePath)
        {
            foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                if (string.IsNullOrEmpty(start))
                    continue;
                DirectoryInfo? dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, relativePath);
                    if (File.Exists(candidate))
                        return candidate;
                    dir = dir.Parent;
                }
            }
            return null;
        }
    }
}

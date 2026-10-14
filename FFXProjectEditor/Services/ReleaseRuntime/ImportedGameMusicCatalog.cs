// ============================================================================
// ImportedGameMusicCatalog — safe reader for locally-derived music manifests
// PURPOSE : load only fixed-catalog WAVs produced below the LocalAppData audio
//           root, merging repeat imports without trusting manifest labels/paths.
// WHY     : AudioStudio must not depend on redistributed bundled music and
//           must never let a tampered manifest escape its owned batch folder.
// MAINT   : loading re-hashes at most ten WAVs. Keep the fixed cap so integrity
//           verification cannot become an unbounded startup scan.
// ============================================================================

using FFXProjectEditor.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace FFXProjectEditor.Services.ReleaseRuntime;

public sealed record ImportedGameMusicTrack(
    string Id,
    int StreamIndex,
    string DisplayName,
    string FilePath,
    long Size,
    string Sha256,
    long LoopStartSamples,
    long LoopEndSamples);

public static class ImportedGameMusicCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyList<ImportedGameMusicTrack> Load(string destinationRoot)
    {
        if (string.IsNullOrWhiteSpace(destinationRoot) || !Path.IsPathRooted(destinationRoot))
        {
            throw new ArgumentException("Imported music root must be an absolute path.", nameof(destinationRoot));
        }

        string gameMusicRoot = Path.Combine(Path.GetFullPath(destinationRoot), "GameMusic");
        if (!Directory.Exists(gameMusicRoot))
        {
            return [];
        }

        List<ManifestCandidate> manifests = [];
        foreach (string batchDirectory in EnumerateBatchDirectories(gameMusicRoot))
        {
            string manifestPath = Path.Combine(batchDirectory, GameMusicImportCatalog.ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                ImportedManifest? manifest = JsonSerializer.Deserialize<ImportedManifest>(
                    File.ReadAllText(manifestPath),
                    JsonOptions);
                if (IsApprovedManifest(manifest))
                {
                    manifests.Add(new ManifestCandidate(batchDirectory, manifest!));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                DebugLog.Warn(
                    "ReleaseRuntime.GameMusicCatalog",
                    $"Ignored unreadable local music manifest ({ex.GetType().Name}).");
            }
        }

        Dictionary<string, ImportedGameMusicTrack> newestById = new(StringComparer.Ordinal);
        foreach (ManifestCandidate candidate in manifests
                     .OrderByDescending(item => item.Manifest.ImportedAtUtc))
        {
            foreach (ImportedManifestTrack entry in candidate.Manifest.Tracks ?? [])
            {
                if (newestById.ContainsKey(entry.Id ?? string.Empty))
                {
                    continue;
                }

                ImportedGameMusicTrack? track = TryResolveTrack(candidate.BatchDirectory, entry);
                if (track != null)
                {
                    newestById.Add(track.Id, track);
                }
            }
        }

        return GameMusicImportCatalog.Tracks
            .Where(track => newestById.ContainsKey(track.Id))
            .Select(track => newestById[track.Id])
            .ToArray();
    }

    private static IEnumerable<string> EnumerateBatchDirectories(string gameMusicRoot)
    {
        try
        {
            return Directory.GetDirectories(gameMusicRoot, "*", SearchOption.TopDirectoryOnly);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsApprovedManifest(ImportedManifest? manifest) => manifest is
    {
        SchemaVersion: 1,
        SourceKind: "user-owned-fsb",
        RedistributionAllowed: false,
        Tracks: not null,
    }
        && GameMusicImportCatalog.ValidatedSourceSha256.Contains(
            manifest.SourceSha256 ?? string.Empty,
            StringComparer.OrdinalIgnoreCase)
        && GameMusicImportCatalog.IsValidatedVgmstreamTool(manifest.ToolName, manifest.ToolSha256);

    private static ImportedGameMusicTrack? TryResolveTrack(
        string batchDirectory,
        ImportedManifestTrack entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Id)
            || string.IsNullOrWhiteSpace(entry.FileName)
            || !IsSha256(entry.Sha256)
            || entry.Size <= 0)
        {
            return null;
        }

        GameMusicTrackDefinition definition;
        try
        {
            definition = GameMusicImportCatalog.GetRequired(entry.Id);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }

        if (entry.StreamIndex != definition.StreamIndex
            || !string.Equals(entry.FileName, Path.GetFileName(entry.FileName), StringComparison.Ordinal)
            || !string.Equals(Path.GetExtension(entry.FileName), ".wav", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            string fullBatch = Path.GetFullPath(batchDirectory);
            string filePath = Path.GetFullPath(Path.Combine(fullBatch, entry.FileName));
            if (!IsSameOrUnder(filePath, fullBatch) || !File.Exists(filePath))
            {
                return null;
            }

            FileInfo info = new(filePath);
            if (info.Length != entry.Size
                || !string.Equals(HashFile(filePath), entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return new ImportedGameMusicTrack(
                definition.Id,
                definition.StreamIndex,
                definition.DisplayName,
                filePath,
                entry.Size,
                entry.Sha256!,
                definition.LoopStartSamples,
                definition.LoopEndSamples);
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or ArgumentException
                                       or NotSupportedException)
        {
            DebugLog.Warn(
                "ReleaseRuntime.GameMusicCatalog",
                $"Ignored invalid local music output ({ex.GetType().Name}).");
            return null;
        }
    }

    private static bool IsSha256(string? value) => value is { Length: 64 }
        && value.All(Uri.IsHexDigit);

    private static string HashFile(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool IsSameOrUnder(string path, string root)
    {
        string fullPath = Path.GetFullPath(path);
        string fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ManifestCandidate(string BatchDirectory, ImportedManifest Manifest);

    private sealed class ImportedManifest
    {
        public int SchemaVersion { get; set; }
        public string? SourceKind { get; set; }
        public string? SourceSha256 { get; set; }
        public string? ToolName { get; set; }
        public string? ToolSha256 { get; set; }
        public DateTimeOffset ImportedAtUtc { get; set; }
        public bool RedistributionAllowed { get; set; }
        public List<ImportedManifestTrack>? Tracks { get; set; }
    }

    private sealed class ImportedManifestTrack
    {
        public string? Id { get; set; }
        public int StreamIndex { get; set; }
        public string? FileName { get; set; }
        public long Size { get; set; }
        public string? Sha256 { get; set; }
    }
}

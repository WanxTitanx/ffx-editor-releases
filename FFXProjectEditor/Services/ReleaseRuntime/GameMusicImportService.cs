// ============================================================================
// GameMusicImportService — consented, local-only FFX music derivation
// PURPOSE : validate a user-owned FSB and explicit vgmstream tool, decode an
//           explicit subset of the ten approved tracks, and atomically publish
//           results plus hashes under LocalAppData or a user-chosen project.
// WHY     : the application must never redistribute game audio, scan silently,
//           write the source bank, or promote partial decoder output.
// MAINT   : source paths are intentionally absent from the persistent manifest.
//           Cleanup is limited to service-owned staging directories.
// ============================================================================

using FFXProjectEditor.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FFXProjectEditor.Services.ReleaseRuntime;

public sealed record GameMusicToolDescriptor(string ExecutablePath, string ExpectedSha256);

public sealed record GameMusicImportRequest(
    string SourceFsbPath,
    string? DestinationRoot,
    GameMusicToolDescriptor VgmstreamTool,
    IReadOnlyCollection<string> TrackIds,
    bool UserConfirmedSourceOwnership,
    bool UserConsentedToLocalImport);

public sealed record GameMusicImportedTrackResult(
    string Id,
    int StreamIndex,
    string DisplayName,
    string OutputPath,
    long Size,
    string Sha256,
    GameMusicStreamMetadata Metadata);

public sealed record GameMusicImportResult(
    string SourceSha256,
    long SourceSize,
    string ToolSha256,
    string OutputRoot,
    string ManifestPath,
    IReadOnlyList<GameMusicImportedTrackResult> Tracks,
    bool RedistributionAllowed);

public enum GameMusicImportPhase
{
    Validating,
    Probing,
    Decoding,
    Publishing,
    Complete,
}

public sealed record GameMusicImportProgress(
    GameMusicImportPhase Phase,
    int CompletedTracks,
    int TotalTracks,
    int Percent);

public sealed class GameMusicImportService
{
    private const string LocalOnlyNotice =
        "Locally derived from the user's own FFX installation. Game audio must not be redistributed.";

    private readonly IGameMusicToolRunner toolRunner;
    private readonly HashSet<string> validatedSourceSha256;
    private readonly string applicationBaseDirectory;
    private readonly TimeProvider timeProvider;

    public GameMusicImportService()
        : this(new VgmstreamGameMusicToolRunner())
    {
    }

    public GameMusicImportService(
        IGameMusicToolRunner toolRunner,
        IEnumerable<string>? validatedSourceSha256 = null,
        string? applicationBaseDirectory = null,
        TimeProvider? timeProvider = null)
    {
        this.toolRunner = toolRunner ?? throw new ArgumentNullException(nameof(toolRunner));
        this.validatedSourceSha256 = new HashSet<string>(
            validatedSourceSha256 ?? GameMusicImportCatalog.ValidatedSourceSha256,
            StringComparer.OrdinalIgnoreCase);
        if (this.validatedSourceSha256.Count == 0 || this.validatedSourceSha256.Any(hash => !IsSha256(hash)))
        {
            throw new ArgumentException("At least one valid source SHA-256 is required.", nameof(validatedSourceSha256));
        }

        string basePath = applicationBaseDirectory ?? AppContext.BaseDirectory;
        if (string.IsNullOrWhiteSpace(basePath) || !Path.IsPathRooted(basePath))
        {
            throw new ArgumentException("Application base directory must be absolute.", nameof(applicationBaseDirectory));
        }
        this.applicationBaseDirectory = Path.GetFullPath(basePath);
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public static string GetDefaultLocalDestinationRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FFXProjectEditor",
        "Audio");

    public async Task<GameMusicImportResult> ImportAsync(
        GameMusicImportRequest request,
        CancellationToken cancellationToken = default)
    {
        return await ImportAsync(request, progress: null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GameMusicImportResult> ImportAsync(
        GameMusicImportRequest request,
        IProgress<GameMusicImportProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            GameMusicImportResult result = await ImportCoreAsync(request, progress, cancellationToken).ConfigureAwait(false);
            DebugLog.Info(
                "ReleaseRuntime.GameMusicImport",
                $"Imported {result.Tracks.Count} local-only track(s); source={result.SourceSha256[..12]}.");
            return result;
        }
        catch (OperationCanceledException)
        {
            DebugLog.Warn("ReleaseRuntime.GameMusicImport", "Local music import was cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            DebugLog.Error("ReleaseRuntime.GameMusicImport", $"Import failed ({ex.GetType().Name}).");
            throw;
        }
    }

    private async Task<GameMusicImportResult> ImportCoreAsync(
        GameMusicImportRequest request,
        IProgress<GameMusicImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        IReadOnlyList<GameMusicTrackDefinition> selection = ResolveExplicitSelection(request.TrackIds);
        RequireConsent(request);
        progress?.Report(new GameMusicImportProgress(
            GameMusicImportPhase.Validating,
            0,
            selection.Count,
            2));

        string sourcePath = RequireExistingAbsoluteFile(request.SourceFsbPath, ".fsb", "source FSB");
        GameMusicToolDescriptor toolDescriptor = request.VgmstreamTool
            ?? throw new InvalidDataException("An explicit vgmstream tool descriptor is required.");
        string toolPath = RequireVgmstreamToolPath(toolDescriptor.ExecutablePath);
        if (!IsSha256(toolDescriptor.ExpectedSha256))
        {
            throw new InvalidDataException("The explicitly selected tool requires an expected SHA-256.");
        }

        string destinationRoot = ResolveDestinationRoot(request.DestinationRoot);
        string sourceHash = await HashFileAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        if (!validatedSourceSha256.Contains(sourceHash))
        {
            throw new InvalidDataException("The selected FSB fingerprint is not in the validated game-music catalog.");
        }

        string toolHash = await HashFileAsync(toolPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(toolHash, toolDescriptor.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The selected vgmstream executable does not match its expected SHA-256.");
        }

        List<(GameMusicTrackDefinition Track, GameMusicStreamMetadata Metadata)> probed = [];
        foreach (GameMusicTrackDefinition track in selection)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GameMusicStreamMetadata metadata = await toolRunner.ProbeAsync(
                toolPath,
                sourcePath,
                track.StreamIndex,
                cancellationToken).ConfigureAwait(false);
            ValidateMetadata(track, metadata);
            probed.Add((track, metadata));
            progress?.Report(new GameMusicImportProgress(
                GameMusicImportPhase.Probing,
                probed.Count,
                selection.Count,
                5 + (probed.Count * 30 / selection.Count)));
        }

        string selectionHash = HashText(string.Join("\n", selection.Select(track => track.Id)));
        string finalRoot = Path.Combine(
            destinationRoot,
            "GameMusic",
            $"{sourceHash[..16]}-{selectionHash[..16]}");
        if (Directory.Exists(finalRoot) || File.Exists(finalRoot))
        {
            throw new IOException("This exact local music import already exists; existing output was not overwritten.");
        }

        string stagingParent = Path.Combine(destinationRoot, ".ffx-game-music-staging");
        string stagingRoot = Path.Combine(stagingParent, $"batch-{Guid.NewGuid():N}");
        List<GameMusicImportedTrackResult> stagedResults = [];
        try
        {
            Directory.CreateDirectory(stagingRoot);
            foreach ((GameMusicTrackDefinition track, GameMusicStreamMetadata metadata) in probed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string outputPath = Path.Combine(stagingRoot, track.OutputFileName);
                await toolRunner.DecodeAsync(
                    toolPath,
                    sourcePath,
                    track,
                    outputPath,
                    cancellationToken).ConfigureAwait(false);
                ValidateWave(outputPath, track);
                FileInfo outputInfo = new(outputPath);
                stagedResults.Add(new GameMusicImportedTrackResult(
                    track.Id,
                    track.StreamIndex,
                    track.DisplayName,
                    outputPath,
                    outputInfo.Length,
                    await HashFileAsync(outputPath, cancellationToken).ConfigureAwait(false),
                    metadata));
                progress?.Report(new GameMusicImportProgress(
                    GameMusicImportPhase.Decoding,
                    stagedResults.Count,
                    selection.Count,
                    35 + (stagedResults.Count * 55 / selection.Count)));
            }

            string sourceHashAfter = await HashFileAsync(sourcePath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(sourceHash, sourceHashAfter, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("The source FSB changed during import; no output was promoted.");
            }

            GameMusicImportManifest manifest = new(
                SchemaVersion: 1,
                SourceKind: "user-owned-fsb",
                SourceSha256: sourceHash,
                SourceSize: new FileInfo(sourcePath).Length,
                ToolName: Path.GetFileName(toolPath),
                ToolSha256: toolHash,
                ImportedAtUtc: timeProvider.GetUtcNow(),
                RedistributionAllowed: false,
                Notice: LocalOnlyNotice,
                Tracks: stagedResults.Select(item => new GameMusicImportManifestTrack(
                    item.Id,
                    item.StreamIndex,
                    item.DisplayName,
                    Path.GetFileName(item.OutputPath),
                    item.Size,
                    item.Sha256,
                    item.Metadata)).ToArray());
            string stagingManifestPath = Path.Combine(stagingRoot, GameMusicImportCatalog.ManifestFileName);
            await File.WriteAllTextAsync(
                stagingManifestPath,
                JsonSerializer.Serialize(manifest, ManifestJsonOptions),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken).ConfigureAwait(false);

            progress?.Report(new GameMusicImportProgress(
                GameMusicImportPhase.Publishing,
                selection.Count,
                selection.Count,
                95));

            Directory.CreateDirectory(Path.GetDirectoryName(finalRoot)!);
            Directory.Move(stagingRoot, finalRoot);

            GameMusicImportedTrackResult[] finalResults = stagedResults.Select(item => item with
            {
                OutputPath = Path.Combine(finalRoot, Path.GetFileName(item.OutputPath)),
            }).ToArray();
            GameMusicImportResult result = new(
                sourceHash,
                new FileInfo(sourcePath).Length,
                toolHash,
                finalRoot,
                Path.Combine(finalRoot, GameMusicImportCatalog.ManifestFileName),
                finalResults,
                RedistributionAllowed: false);
            progress?.Report(new GameMusicImportProgress(
                GameMusicImportPhase.Complete,
                selection.Count,
                selection.Count,
                100));
            return result;
        }
        finally
        {
            DeleteOwnedStaging(stagingRoot, stagingParent);
        }
    }

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private string ResolveDestinationRoot(string? requestedRoot)
    {
        string candidate = string.IsNullOrWhiteSpace(requestedRoot)
            ? GetDefaultLocalDestinationRoot()
            : requestedRoot;
        if (!Path.IsPathRooted(candidate))
        {
            throw new ArgumentException("Music import destination must be an absolute LocalAppData or project path.");
        }

        string fullDestination = Path.GetFullPath(candidate);
        if (IsSameOrUnder(fullDestination, applicationBaseDirectory))
        {
            throw new InvalidOperationException(
                "Local game audio cannot be written inside the application directory or release payload.");
        }
        return fullDestination;
    }

    private static IReadOnlyList<GameMusicTrackDefinition> ResolveExplicitSelection(
        IReadOnlyCollection<string>? trackIds)
    {
        if (trackIds == null || trackIds.Count == 0)
        {
            throw new InvalidOperationException("An explicit non-empty music selection is required.");
        }
        if (trackIds.Count > GameMusicImportCatalog.MaximumTracks)
        {
            throw new ArgumentOutOfRangeException(
                nameof(trackIds),
                $"At most {GameMusicImportCatalog.MaximumTracks} game tracks may be imported.");
        }

        HashSet<string> selected = new(StringComparer.Ordinal);
        foreach (string id in trackIds)
        {
            if (string.IsNullOrWhiteSpace(id) || !selected.Add(id))
            {
                throw new InvalidDataException("Music selection contains a blank or duplicate track id.");
            }
            _ = GameMusicImportCatalog.GetRequired(id);
        }

        return GameMusicImportCatalog.Tracks.Where(track => selected.Contains(track.Id)).ToArray();
    }

    private static void RequireConsent(GameMusicImportRequest request)
    {
        if (!request.UserConfirmedSourceOwnership)
        {
            throw new InvalidOperationException("User confirmation of the game source is required.");
        }
        if (!request.UserConsentedToLocalImport)
        {
            throw new InvalidOperationException("Explicit consent to local-only music import is required.");
        }
    }

    private static void ValidateMetadata(
        GameMusicTrackDefinition expected,
        GameMusicStreamMetadata actual)
    {
        bool matches = actual.StreamCount == GameMusicImportCatalog.ExpectedStreamCount
            && actual.StreamIndex == expected.StreamIndex
            && string.Equals(actual.StreamName, expected.StreamName, StringComparison.Ordinal)
            && actual.SampleRate == expected.SampleRate
            && actual.Channels == expected.Channels
            && actual.LoopStartSamples == expected.LoopStartSamples
            && actual.LoopEndSamples == expected.LoopEndSamples
            && actual.TotalSamples == expected.TotalSamples;
        if (!matches)
        {
            throw new InvalidDataException($"vgmstream metadata does not match approved track {expected.Id}.");
        }
    }

    private static void ValidateWave(string path, GameMusicTrackDefinition track)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using BinaryReader reader = new(stream, Encoding.ASCII, leaveOpen: true);
        if (stream.Length < 44 || ReadFourCc(reader) != "RIFF")
        {
            throw new InvalidDataException($"Decoded WAV for {track.Id} has no RIFF header.");
        }
        _ = reader.ReadUInt32();
        if (ReadFourCc(reader) != "WAVE")
        {
            throw new InvalidDataException($"Decoded WAV for {track.Id} has no WAVE signature.");
        }

        bool validFormat = false;
        bool hasAudioData = false;
        while (stream.Position + 8 <= stream.Length)
        {
            string chunkId = ReadFourCc(reader);
            uint chunkSize = reader.ReadUInt32();
            long chunkStart = stream.Position;
            long chunkEnd = checked(chunkStart + chunkSize);
            if (chunkEnd > stream.Length)
            {
                throw new InvalidDataException($"Decoded WAV for {track.Id} has a truncated chunk.");
            }

            if (chunkId == "fmt " && chunkSize >= 16)
            {
                ushort audioFormat = reader.ReadUInt16();
                ushort channels = reader.ReadUInt16();
                uint sampleRate = reader.ReadUInt32();
                _ = reader.ReadUInt32();
                _ = reader.ReadUInt16();
                ushort bitsPerSample = reader.ReadUInt16();
                validFormat = (audioFormat == 1 || audioFormat == 0xFFFE)
                    && channels == track.Channels
                    && sampleRate == track.SampleRate
                    && bitsPerSample == 16;
            }
            else if (chunkId == "data")
            {
                hasAudioData = chunkSize > 0;
            }

            stream.Position = chunkEnd + (chunkSize % 2);
        }

        if (!validFormat || !hasAudioData)
        {
            throw new InvalidDataException($"Decoded WAV for {track.Id} is not validated PCM16 audio.");
        }
    }

    private static string ReadFourCc(BinaryReader reader) => Encoding.ASCII.GetString(reader.ReadBytes(4));

    // The pinned vgmstream binary is vgmstream-cli.exe on Windows and the extensionless
    // vgmstream-cli ELF on Linux; both ship side by side under tools/vgmstream.
    private static string RequireVgmstreamToolPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
        {
            throw new ArgumentException("The vgmstream executable path must be explicit and absolute.");
        }
        string fullPath = Path.GetFullPath(path);
        string fileName = Path.GetFileName(fullPath);
        if (!string.Equals(fileName, GameMusicImportCatalog.VgmstreamFileNameWindows, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fileName, GameMusicImportCatalog.VgmstreamFileNameLinux, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The explicitly selected tool must be the bundled vgmstream-cli.");
        }
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The vgmstream executable was not found.");
        }
        return fullPath;
    }

    private static string RequireExistingAbsoluteFile(string? path, string extension, string label)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
        {
            throw new ArgumentException($"The {label} path must be explicit and absolute.");
        }
        string fullPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(fullPath), extension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"The {label} must use the {extension} extension.");
        }
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"The {label} was not found.");
        }
        return fullPath;
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    private static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool IsSha256(string? value) => value is { Length: 64 }
        && value.All(Uri.IsHexDigit);

    private static bool IsSameOrUnder(string path, string root)
    {
        string fullPath = Path.GetFullPath(path);
        string fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteOwnedStaging(string stagingRoot, string stagingParent)
    {
        string fullStaging = Path.GetFullPath(stagingRoot);
        if (!Path.GetFileName(fullStaging).StartsWith("batch-", StringComparison.Ordinal)
            || !IsSameOrUnder(fullStaging, stagingParent)
            || !Directory.Exists(fullStaging))
        {
            return;
        }

        try
        {
            Directory.Delete(fullStaging, recursive: true);
        }
        catch (IOException)
        {
            DebugLog.Warn("ReleaseRuntime.GameMusicImport", "Owned staging cleanup was incomplete.");
        }
        catch (UnauthorizedAccessException)
        {
            DebugLog.Warn("ReleaseRuntime.GameMusicImport", "Owned staging cleanup was denied.");
        }
    }

    private sealed record GameMusicImportManifest(
        int SchemaVersion,
        string SourceKind,
        string SourceSha256,
        long SourceSize,
        string ToolName,
        string ToolSha256,
        DateTimeOffset ImportedAtUtc,
        bool RedistributionAllowed,
        string Notice,
        IReadOnlyList<GameMusicImportManifestTrack> Tracks);

    private sealed record GameMusicImportManifestTrack(
        string Id,
        int StreamIndex,
        string DisplayName,
        string FileName,
        long Size,
        string Sha256,
        GameMusicStreamMetadata Metadata);
}

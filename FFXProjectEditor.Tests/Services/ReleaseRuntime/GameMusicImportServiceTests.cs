using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.Services.ReleaseRuntime;
using Xunit;

namespace FFXProjectEditor.Tests.Services.ReleaseRuntime;

public sealed class GameMusicImportServiceTests
{
    [Fact]
    public void VgmstreamMetadataParser_ParsesTheValidatedMetadataShape()
    {
        const string metadata = """
            sample rate: 44100 Hz
            channels: 2
            loop start: 1119010 samples (0:25.374 seconds)
            loop end: 4174895 samples (1:34.669 seconds)
            stream total samples: 4453376 (1:40.984 seconds)
            stream count: 89
            stream index: 1
            stream name: 1_02_zanarkand
            """;

        GameMusicStreamMetadata parsed = VgmstreamMetadataParser.Parse(metadata);

        Assert.Equal(1, parsed.StreamIndex);
        Assert.Equal(89, parsed.StreamCount);
        Assert.Equal("1_02_zanarkand", parsed.StreamName);
        Assert.Equal(44_100, parsed.SampleRate);
        Assert.Equal(2, parsed.Channels);
        Assert.Equal(1_119_010, parsed.LoopStartSamples);
        Assert.Equal(4_174_895, parsed.LoopEndSamples);
        Assert.Equal(4_453_376, parsed.TotalSamples);
    }

    [Fact]
    public void VgmstreamMetadataParser_RejectsIncompleteMetadata()
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            VgmstreamMetadataParser.Parse("stream index: 1\nstream name: 1_02_zanarkand"));

        Assert.Contains("incomplete", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Catalog_ContainsExactlyTheTenApprovedUniqueTracks()
    {
        (string Id, int StreamIndex, string DisplayName)[] expected =
        [
            ("1_02_zanarkand", 1, "To Zanarkand"),
            ("1_05_otherworld", 4, "Otherworld"),
            ("1_17_the_blitzers", 15, "The Blitzers"),
            ("1_18_besaid", 16, "Besaid"),
            ("2_01_yuna_s_theme", 25, "Yuna's Theme"),
            ("2_06_luca", 29, "Luca"),
            ("2_11_blitz_off", 33, "Blitz Off"),
            ("4_07_wandering_flame", 70, "Wandering Flame"),
            ("4_08_someday_the_dream_will_end", 71, "Someday the Dream Will End"),
            ("m_71_wakka_s_theme", 89, "Wakka's Theme"),
        ];

        Assert.Equal(10, GameMusicImportCatalog.Tracks.Count);
        Assert.Equal(expected, GameMusicImportCatalog.Tracks
            .Select(track => (track.Id, track.StreamIndex, track.DisplayName)));
        Assert.Equal(10, GameMusicImportCatalog.Tracks.Select(track => track.Id).Distinct().Count());
        Assert.Equal(10, GameMusicImportCatalog.Tracks.Select(track => track.StreamIndex).Distinct().Count());
    }

    [Fact]
    public void Catalog_PinsTheValidatedFsbFingerprintAndStreamMetadata()
    {
        Assert.Contains(
            "C3D4547F9716F72FB346E6EB1BDC817FC246BAFA0E18202E189AFB24DBF0ECE4",
            GameMusicImportCatalog.ValidatedSourceSha256,
            StringComparer.OrdinalIgnoreCase);

        GameMusicTrackDefinition zanarkand = GameMusicImportCatalog.GetRequired("1_02_zanarkand");
        Assert.Equal("1_02_zanarkand", zanarkand.StreamName);
        Assert.Equal(44_100, zanarkand.SampleRate);
        Assert.Equal(2, zanarkand.Channels);
        Assert.Equal(1_119_010, zanarkand.LoopStartSamples);
        Assert.Equal(4_174_895, zanarkand.LoopEndSamples);
        Assert.Equal(4_453_376, zanarkand.TotalSamples);
    }

    [Fact]
    public async Task ImportAsync_RejectsAnImplicitEmptySelection()
    {
        using TestWorkspace workspace = new();
        ImportFixture fixture = workspace.CreateFixture();

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.ImportAsync(fixture.Request with { TrackIds = [] }));

        Assert.Contains("explicit", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fixture.Runner.ProbedStreams);
    }

    [Fact]
    public async Task ImportAsync_RejectsMoreThanTenSelectionsBeforeLaunchingTheTool()
    {
        using TestWorkspace workspace = new();
        ImportFixture fixture = workspace.CreateFixture();
        string[] tooMany = GameMusicImportCatalog.Tracks.Select(track => track.Id)
            .Append(GameMusicImportCatalog.Tracks[0].Id)
            .ToArray();

        ArgumentOutOfRangeException error = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            fixture.Service.ImportAsync(fixture.Request with { TrackIds = tooMany }));

        Assert.Contains("10", error.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Runner.ProbedStreams);
    }

    [Fact]
    public async Task ImportAsync_RequiresOwnershipAndLocalImportConsent()
    {
        using TestWorkspace workspace = new();
        ImportFixture fixture = workspace.CreateFixture();

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ImportAsync(
            fixture.Request with { UserConfirmedSourceOwnership = false }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ImportAsync(
            fixture.Request with { UserConsentedToLocalImport = false }));

        Assert.Empty(fixture.Runner.ProbedStreams);
    }

    [Fact]
    public async Task ImportAsync_RejectsUnknownSourceAndToolHashes()
    {
        using TestWorkspace workspace = new();
        ImportFixture fixture = workspace.CreateFixture();

        GameMusicImportService wrongSourcePolicy = new(
            fixture.Runner,
            [new string('0', 64)],
            fixture.ApplicationBaseDirectory);
        await Assert.ThrowsAsync<InvalidDataException>(() => wrongSourcePolicy.ImportAsync(fixture.Request));

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.ImportAsync(
            fixture.Request with
            {
                VgmstreamTool = fixture.Request.VgmstreamTool with { ExpectedSha256 = new string('F', 64) },
            }));

        Assert.Empty(fixture.Runner.ProbedStreams);
    }

    [Fact]
    public async Task ImportAsync_RejectsDestinationInsideTheApplicationDirectory()
    {
        using TestWorkspace workspace = new();
        ImportFixture fixture = workspace.CreateFixture();
        string unsafeDestination = Path.Combine(fixture.ApplicationBaseDirectory, "Assets", "Audio");

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.ImportAsync(fixture.Request with { DestinationRoot = unsafeDestination }));

        Assert.Contains("application", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fixture.Runner.ProbedStreams);
    }

    [Fact]
    public async Task ImportAsync_ValidatesMetadataBeforeDecodeAndLeavesNoPromotedBatchOnMismatch()
    {
        using TestWorkspace workspace = new();
        ImportFixture fixture = workspace.CreateFixture();
        fixture.Runner.MetadataTransform = metadata => metadata with { StreamName = "wrong_stream" };

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Service.ImportAsync(fixture.Request));

        Assert.Contains("metadata", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fixture.Runner.DecodedStreams);
        Assert.False(Directory.Exists(Path.Combine(fixture.Request.DestinationRoot!, "GameMusic")));
    }

    [Fact]
    public async Task ImportAsync_WritesOnlyTheExplicitSubsetAndReturnsVerifiableHashes()
    {
        using TestWorkspace workspace = new();
        ImportFixture fixture = workspace.CreateFixture();
        string sourceHashBefore = Sha256(fixture.Request.SourceFsbPath);
        string[] selection = ["1_18_besaid", "1_02_zanarkand"];

        GameMusicImportResult result = await fixture.Service.ImportAsync(
            fixture.Request with { TrackIds = selection });

        Assert.False(result.RedistributionAllowed);
        Assert.Equal(sourceHashBefore, result.SourceSha256);
        Assert.Equal(fixture.Request.VgmstreamTool.ExpectedSha256, result.ToolSha256);
        Assert.Equal(sourceHashBefore, Sha256(fixture.Request.SourceFsbPath));
        Assert.Equal([1, 16], fixture.Runner.ProbedStreams);
        Assert.Equal([1, 16], fixture.Runner.DecodedStreams);
        Assert.Equal(["1_02_zanarkand", "1_18_besaid"], result.Tracks.Select(track => track.Id));
        Assert.All(result.Tracks, track =>
        {
            Assert.True(File.Exists(track.OutputPath));
            Assert.Equal(64, track.Sha256.Length);
            Assert.True(track.Size > 44);
            Assert.True(IsUnder(track.OutputPath, fixture.Request.DestinationRoot!));
        });

        Assert.True(File.Exists(result.ManifestPath));
        string manifest = await File.ReadAllTextAsync(result.ManifestPath);
        Assert.Contains("\"redistributionAllowed\": false", manifest, StringComparison.Ordinal);
        Assert.Contains("1_02_zanarkand", manifest, StringComparison.Ordinal);
        Assert.Contains("1_18_besaid", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Request.SourceFsbPath, manifest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1_05_otherworld", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_ReportsValidationProbeDecodeAndPublishProgress()
    {
        using TestWorkspace workspace = new();
        ImportFixture fixture = workspace.CreateFixture();
        List<GameMusicImportProgress> updates = [];

        await fixture.Service.ImportAsync(
            fixture.Request,
            new CallbackProgress<GameMusicImportProgress>(updates.Add));

        Assert.Equal(GameMusicImportPhase.Validating, updates.First().Phase);
        Assert.Contains(updates, update => update.Phase == GameMusicImportPhase.Probing);
        Assert.Contains(updates, update => update.Phase == GameMusicImportPhase.Decoding);
        Assert.Contains(updates, update => update.Phase == GameMusicImportPhase.Publishing);
        Assert.Equal(GameMusicImportPhase.Complete, updates.Last().Phase);
        Assert.Equal(100, updates.Last().Percent);
    }

    private static bool IsUnder(string path, string root)
    {
        string fullPath = Path.GetFullPath(path);
        string fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "ffx-game-music-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public ImportFixture CreateFixture()
        {
            string source = Path.Combine(Root, "user-game", "ffx_music_bank00.fsb");
            string tool = Path.Combine(Root, "tools", "vgmstream-cli.exe");
            string destination = Path.Combine(Root, "chosen-project");
            string appBase = Path.Combine(Root, "application");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            Directory.CreateDirectory(Path.GetDirectoryName(tool)!);
            Directory.CreateDirectory(appBase);
            File.WriteAllBytes(source, Encoding.ASCII.GetBytes("user-owned-fsb-fixture"));
            File.WriteAllBytes(tool, Encoding.ASCII.GetBytes("explicit-tool-fixture"));

            string sourceHash = Sha256(source);
            string toolHash = Sha256(tool);
            FakeGameMusicToolRunner runner = new();
            GameMusicImportService service = new(runner, [sourceHash], appBase);
            GameMusicImportRequest request = new(
                source,
                destination,
                new GameMusicToolDescriptor(tool, toolHash),
                ["1_02_zanarkand"],
                UserConfirmedSourceOwnership: true,
                UserConsentedToLocalImport: true);
            return new ImportFixture(service, runner, request, appBase);
        }

        public void Dispose()
        {
            string expectedRoot = Path.Combine(Path.GetTempPath(), "ffx-game-music-tests")
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string fullRoot = Path.GetFullPath(Root);
            if (fullRoot.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullRoot))
            {
                Directory.Delete(fullRoot, recursive: true);
            }
        }
    }

    private sealed record ImportFixture(
        GameMusicImportService Service,
        FakeGameMusicToolRunner Runner,
        GameMusicImportRequest Request,
        string ApplicationBaseDirectory);

    private sealed class FakeGameMusicToolRunner : IGameMusicToolRunner
    {
        public List<int> ProbedStreams { get; } = [];
        public List<int> DecodedStreams { get; } = [];
        public Func<GameMusicStreamMetadata, GameMusicStreamMetadata>? MetadataTransform { get; set; }

        public Task<GameMusicStreamMetadata> ProbeAsync(
            string executablePath,
            string sourceFsbPath,
            int streamIndex,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProbedStreams.Add(streamIndex);
            GameMusicTrackDefinition track = GameMusicImportCatalog.Tracks.Single(item => item.StreamIndex == streamIndex);
            GameMusicStreamMetadata metadata = new(
                track.StreamIndex,
                89,
                track.StreamName,
                track.SampleRate,
                track.Channels,
                track.LoopStartSamples,
                track.LoopEndSamples,
                track.TotalSamples);
            return Task.FromResult(MetadataTransform?.Invoke(metadata) ?? metadata);
        }

        public Task DecodeAsync(
            string executablePath,
            string sourceFsbPath,
            GameMusicTrackDefinition track,
            string outputWavPath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DecodedStreams.Add(track.StreamIndex);
            Directory.CreateDirectory(Path.GetDirectoryName(outputWavPath)!);
            WriteTinyPcmWave(outputWavPath, track.SampleRate, track.Channels);
            return Task.CompletedTask;
        }

        private static void WriteTinyPcmWave(string path, int sampleRate, int channels)
        {
            const short bitsPerSample = 16;
            byte[] data = new byte[Math.Max(4, channels * 4)];
            using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: false);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + data.Length);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(sampleRate);
            int byteRate = sampleRate * channels * bitsPerSample / 8;
            writer.Write(byteRate);
            writer.Write((short)(channels * bitsPerSample / 8));
            writer.Write(bitsPerSample);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(data.Length);
            writer.Write(data);
        }
    }
}

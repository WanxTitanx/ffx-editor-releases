using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Tests.Infrastructure;
using FFXProjectEditor.Tests.ViewerHub;
using Xunit;
using Xunit.Abstractions;

namespace FFXProjectEditor.Tests.MagicDll;

// ── Owned-byte parser equivalence and pathname independence ──
// A captured PE must produce one graph from that capture, even if its logical path changes.
// MAINT: preserve the real-corpus comparison and negative controls; this is not mutation/RT2 proof.
[Collection(FileSystemReparseGuardHookCollection.Name)]
public sealed class MagicDllSnapshotParserTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ffx-magic-snapshot-").FullName;
    private readonly ITestOutputHelper _output;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public MagicDllSnapshotParserTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData("magic_0021.dll", 21)]
    [InlineData("magic_0098.dll", 98)]
    public void Snapshot_MatchesPathParserAndOwnsItsInput(string name, int id)
    {
        byte[] input = MagicDllTestFixture.ReadBytes(name);
        byte[] original = (byte[])input.Clone();
        string logical = Path.Combine(_root, "not-created", name);
        var parser = CreateParser();
        Assert.True(parser.TryParse(MagicDllTestFixture.GetPath(name), out var fromPath, out var pathError), pathError);
        Assert.True(parser.TryParseSnapshot(input, logical, out var snapshot, out var error), error);
        Assert.False(File.Exists(logical));
        Assert.Equal(logical, snapshot!.SourcePath);
        Assert.Equal(name, snapshot.DllName);
        Assert.Equal(id, snapshot.MagicId);
        AssertEquivalent(fromPath!, snapshot);
        Assert.Single(snapshot.Roots);
        Assert.NotEmpty(snapshot.Roots[0].Programs.SelectMany(program => program.Slots));
        if (id == 21) Assert.Equal(74, snapshot.Roots[0].TotalSlots);

        string graph = StructureHash(snapshot);
        input[0] ^= 0xff;
        input[snapshot.DataSectionRawPtr] ^= 0xff;
        Assert.Equal(original, snapshot.Serialize());
        Assert.Equal(graph, StructureHash(snapshot));
        Assert.NotSame(input, snapshot.FileBytes);
        byte[] serialized = snapshot.Serialize();
        serialized[0] ^= 0xff;
        Assert.Equal(original, snapshot.FileBytes);
    }

    [Fact]
    public void Snapshot_IgnoresAnExistingPoisonedLogicalPath()
    {
        string logical = Path.Combine(_root, "magic_0021.dll");
        byte[] poison = { 1, 2, 3, 4 };
        File.WriteAllBytes(logical, poison);
        byte[] input = MagicDllTestFixture.ReadBytes("magic_0021.dll");
        Assert.True(CreateParser().TryParseSnapshot(input, logical, out var snapshot, out var error), error);
        Assert.Equal(input, snapshot!.FileBytes);
        Assert.Equal(poison, File.ReadAllBytes(logical));
        Assert.False(CreateParser().TryParse(logical, out _, out _));
    }

    [Fact]
    public void Snapshot_HonorsExplicitMapAndHandlerTableBeforePathFallbacks()
    {
        var options = new MagicDllParserOptions
        {
            FieldMap = MagicFieldMap.LoadEmbedded(),
            FieldMapPath = Path.Combine(_root, "missing-map.json"),
            FpHandlerTable = new MagicFpHandlerTable(21, "configured", new Dictionary<int, string>
            {
                [0] = "pppScale"
            }),
            FpDirectoryPath = Path.Combine(_root, "missing-fp")
        };
        var parser = new MagicDllParser(options);
        byte[] bytes = MagicDllTestFixture.ReadBytes("magic_0021.dll");
        Assert.True(parser.TryParse(MagicDllTestFixture.GetPath("magic_0021.dll"), out var expected, out var oldError), oldError);
        Assert.True(parser.TryParseSnapshot(bytes, "magic_0021.dll", out var snapshot, out var error), error);
        AssertEquivalent(expected!, snapshot!);
        var slots = snapshot!.Roots.SelectMany(root => root.Programs).SelectMany(program => program.Slots).ToArray();
        Assert.NotEmpty(slots);
        Assert.Equal("pppScale", slots[0].OpcodeName);
        Assert.NotNull(slots[0].Field);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("short")]
    [InlineData("mz")]
    [InlineData("pe")]
    [InlineData("pe-offset")]
    [InlineData("section-table")]
    [InlineData("section-count")]
    [InlineData("data-offset")]
    public void Snapshot_InvalidPeFailsWithoutChangingInput(string corruption)
    {
        byte[] bytes = MagicDllTestFixture.ReadBytes("magic_0021.dll");
        switch (corruption)
        {
            case "empty": bytes = Array.Empty<byte>(); break;
            case "short": bytes = bytes[..63]; break;
            case "mz": bytes[0] = 0; break;
            case "pe": bytes[0x80] = 0; break;
            case "pe-offset": BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x3c), int.MaxValue); break;
            case "section-table": BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x80 + 20), ushort.MaxValue); break;
            case "section-count": BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x80 + 6), 97); break;
            case "data-offset": BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x80 + 24 + 0xe0 + 80 + 20), -1); break;
            default: throw new ArgumentOutOfRangeException(nameof(corruption));
        }
        byte[] before = (byte[])bytes.Clone();
        Assert.False(CreateParser().TryParseSnapshot(bytes, "magic_0021.dll", out var parsed, out var error));
        Assert.Null(parsed);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(before, bytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Snapshot_RejectsAnEmptyLogicalLabel(string label)
    {
        Assert.False(CreateParser().TryParseSnapshot(MagicDllTestFixture.ReadBytes("magic_0021.dll"), label,
            out var parsed, out var error));
        Assert.Null(parsed);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void LinuxWrapper_ConsumesCaptureAcrossSourceAbaWithoutCreatingStaging()
    {
        string source = Path.Combine(_root, "magic_0021.dll");
        string staging = Path.Combine(_root, "must-not-be-created");
        byte[] original = MagicDllTestFixture.ReadBytes("magic_0021.dll");
        byte[] poison = MagicDllTestFixture.ReadBytes("magic_0098.dll");
        File.WriteAllBytes(source, original);
        var previousHook = FileSystemReparseGuard.BeforeHandleOperationForTests;
        string? previousStaging = MagicDllDocument_Wrapper.StagingRootOverrideForTests;
        try
        {
            if (OperatingSystem.IsLinux())
            {
                int before = 0, after = 0, stagingCalls = 0;
                MagicDllDocument_Wrapper.StagingRootOverrideForTests = staging;
                FileSystemReparseGuard.BeforeHandleOperationForTests = (operation, path) =>
                {
                    if (operation.StartsWith("staging-", StringComparison.Ordinal)) stagingCalls++;
                    if (operation == "snapshot-before-parse")
                    {
                        before++;
                        Assert.Equal(source, path);
                        File.WriteAllBytes(source, poison);
                    }
                    if (operation == "snapshot-after-parse")
                    {
                        after++;
                        File.WriteAllBytes(source, original);
                    }
                };
                var wrapper = new MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(source, out string error), error);
                Assert.Equal(1, before);
                Assert.Equal(1, after);
                Assert.Equal(0, stagingCalls);
                Assert.Equal(original, wrapper.SourceBytes);
                Assert.Equal(original, wrapper.ParsedFile!.FileBytes);
                Assert.Equal(source, wrapper.ParsedFile.SourcePath);
                Assert.Equal(21, wrapper.MagicId);
                Assert.False(Directory.Exists(staging));
            }
            else
            {
                // Windows retains its separate mandatory-lock staging contract. This branch still
                // tests real snapshot consumption; it is not a vacuous platform return.
                File.WriteAllBytes(source, poison);
                Assert.True(CreateParser().TryParseSnapshot(original, source, out var parsed, out var error), error);
                Assert.Equal(original, parsed!.FileBytes);
                Assert.Equal(poison, File.ReadAllBytes(source));
                File.WriteAllBytes(source, original);
            }
            Assert.Equal(original, File.ReadAllBytes(source));
        }
        finally
        {
            FileSystemReparseGuard.BeforeHandleOperationForTests = previousHook;
            MagicDllDocument_Wrapper.StagingRootOverrideForTests = previousStaging;
        }
    }

    [Fact]
    public void Snapshot_RealCorpusMatchesEveryPathParseAndPreservesInputs()
    {
        string[] paths = Directory.GetFiles(TestDataPaths.MagicCorpus, "magic_*.dll")
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        Assert.True(paths.Length >= 500, $"Expected a real corpus of at least 500 DLLs, found {paths.Length}.");
        string auditBefore = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(TestDataPaths.MagicAudit)));
        var parser = CreateParser();
        int successes = 0, matchingFailures = 0;
        var manifest = new List<object>();
        foreach (string path in paths)
        {
            byte[] before = File.ReadAllBytes(path);
            string inputSha = Convert.ToHexString(SHA256.HashData(before));
            bool oldOk = parser.TryParse(path, out var oldFile, out var oldError);
            bool snapshotOk = parser.TryParseSnapshot(before, path, out var snapshot, out var error);
            Assert.True(oldOk == snapshotOk, $"{path}: path={oldError}; snapshot={error}");
            if (oldOk)
            {
                AssertEquivalent(oldFile!, snapshot!);
                Assert.Equal(before, snapshot!.Serialize());
                successes++;
            }
            else
            {
                Assert.Null(snapshot);
                Assert.False(string.IsNullOrWhiteSpace(oldError));
                Assert.False(string.IsNullOrWhiteSpace(error));
                matchingFailures++;
            }
            string afterSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            Assert.Equal(inputSha, afterSha);
            manifest.Add(new { name = Path.GetFileName(path), inputSha, afterSha, success = snapshotOk,
                structureSha = snapshotOk ? StructureHash(snapshot!) : null });
        }
        Assert.True(successes >= 500, $"Only {successes}/{paths.Length} DLLs parsed successfully.");
        Assert.Equal(auditBefore, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(TestDataPaths.MagicAudit))));
        string report = TestDataPaths.ReportPath("magic-snapshot-parity.json");
        File.WriteAllText(report, JsonSerializer.Serialize(new { total = paths.Length, successes, matchingFailures,
            auditSha256 = auditBefore, manifest }));
        _output.WriteLine($"Real corpus: {paths.Length}; exact successful pairs: {successes}; matched failures (not equality passes): {matchingFailures}. Report: {report}");
    }

    private static MagicDllParser CreateParser() => new(new MagicDllParserOptions { FieldMap = MagicFieldMap.LoadEmbedded() });

    private static void AssertEquivalent(MagicDllFile expected, MagicDllFile actual)
    {
        Assert.Equal(expected.MagicId, actual.MagicId);
        Assert.Equal(expected.FileBytes, actual.FileBytes);
        Assert.Equal(expected.Data, actual.Data);
        Assert.Equal(StructureHash(expected), StructureHash(actual));
    }

    private static string StructureHash(MagicDllFile file) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            file.DataSectionRawPtr, file.DataSectionSize, file.DataSectionRva, file.Sections, file.Roots
        }, JsonOptions)));

    public void Dispose() => Directory.Delete(_root, recursive: true);
}

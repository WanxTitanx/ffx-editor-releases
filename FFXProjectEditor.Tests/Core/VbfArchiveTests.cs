using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.FfxLib.Archives;
using FFXProjectEditor.Modules.Extras;
using Xunit;

namespace FFXProjectEditor.Tests.Core;

public sealed class VbfArchiveTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "vbf-tests-" + Guid.NewGuid().ToString("N"));
    public VbfArchiveTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    string At(string name) => Path.Combine(root, name);

    [Fact]
    public void ReadsIndependentFixtureIncludingBlockBoundariesAndEmptyEntries()
    {
        var data = Samples();
        string fixture = Fixture(data);
        var archive = VbfArchive.Open(fixture);
        Assert.Equal(data.Count, archive.Entries.Count);
        foreach (var entry in archive.Entries) Assert.Equal(data[entry.Path], Extract(archive, entry));
        VbfArchiveOperations.Verify(archive);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CreatesFromFolderAndReadbackMatchesEveryByte(bool compress)
    {
        var data = Samples();
        string folder = At("input");
        foreach (var item in data) Put(Path.Combine(folder, item.Key), item.Value);
        var workspace = new VbfWorkspace();
        workspace.Add(VbfWorkspace.Import(new[] { folder }, folderContents: true));
        string output = At("created.vbf");
        var result = VbfArchiveWriter.Save(workspace.Files, output, compress);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))), result.Sha256);
        var archive = VbfArchive.Open(output);
        foreach (var entry in archive.Entries) Assert.Equal(data[entry.Path], Extract(archive, entry));
        var large = archive.Entries.Single(x => x.Path == "nested/compressible.bin");
        Assert.Equal(compress, large.PackedSize < large.Size);
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }

    [Fact]
    public void ReplaceAddDeleteRenameUndoAndRepackPreserveOriginalAndPackedData()
    {
        string fixture = Fixture(Samples()); byte[] original = File.ReadAllBytes(fixture);
        var archive = VbfArchive.Open(fixture); var workspace = new VbfWorkspace(archive);
        string replacement = Put(At("replacement.bin"), new byte[] { 9, 8, 7 });
        var added = VbfWorkspace.Import(new[] { replacement });
        workspace.Add(new[] { added[0] with { Path = "one.bin" }, added[0] with { Path = "new/sub/file.bin" } });
        workspace.Remove(new[] { "empty.bin" });
        workspace.Rename("new/", "renamed");
        workspace.Undo();
        Assert.Contains(workspace.Files, x => x.Path == "new/sub/file.bin");
        workspace.Rename("new/", "renamed");
        string target = At("edited.vbf"); Put(target, Encoding.UTF8.GetBytes("old destination"));
        var result = VbfArchiveWriter.Save(workspace.Files, target, original: archive);
        Assert.Equal(original, File.ReadAllBytes(fixture));
        Assert.Equal("old destination", File.ReadAllText(result.BackupPath!));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(original)), result.SourceHashes[fixture]);
        var edited = VbfArchive.Open(target);
        Assert.DoesNotContain(edited.Entries, x => x.Path == "empty.bin");
        Assert.Contains(edited.Entries, x => x.Path == "renamed/sub/file.bin");
        Assert.Equal(new byte[] { 9, 8, 7 }, Extract(edited, edited.Entries.Single(x => x.Path == "one.bin")));
        var unchanged = archive.Entries.Single(x => x.Path == "nested/compressible.bin");
        var copy = edited.Entries.Single(x => x.Path == unchanged.Path);
        Assert.Equal(unchanged.OpaqueField, copy.OpaqueField);
        Assert.Equal(original.AsSpan((int)unchanged.Offset, (int)unchanged.PackedSize).ToArray(),
            File.ReadAllBytes(target).AsSpan((int)copy.Offset, (int)copy.PackedSize).ToArray());
        // The backup is a real restoration source, independent of the new output.
        File.Copy(result.BackupPath!, At("restored.vbf"));
        Assert.Equal("old destination", File.ReadAllText(At("restored.vbf")));
    }

    [Fact]
    public void CancelDuringRepackLeavesDestinationUntouchedAndRemovesTemporary()
    {
        string input = Put(At("large.bin"), new byte[200000]);
        string target = Put(At("existing.vbf"), new byte[] { 1, 2, 3 });
        using var cts = new CancellationTokenSource();
        var progress = new CallbackProgress(p => { if (p.Phase == "write") cts.Cancel(); });
        Assert.Throws<OperationCanceledException>(() => VbfArchiveWriter.Save(
            VbfWorkspace.Import(new[] { input }), target, progress: progress, token: cts.Token));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(target));
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        Assert.Empty(Directory.GetFiles(root, "*.bak.*"));
    }

    [Fact]
    public void RejectsSourceDestinationAndImportedFileChanges()
    {
        string fixture = Fixture(Samples()); var archive = VbfArchive.Open(fixture);
        Assert.Throws<IOException>(() => VbfArchiveWriter.Save(new VbfWorkspace(archive).Files, fixture));
        Assert.Throws<IOException>(() => VbfArchiveWriter.Save(Array.Empty<VbfContent>(), fixture, original: archive));
        string input = Put(At("input.bin"), new byte[] { 1 });
        var imported = VbfWorkspace.Import(new[] { input });
        Assert.Throws<IOException>(() => VbfArchiveWriter.Save(imported, input));
        File.WriteAllBytes(input, new byte[] { 2, 3 });
        Assert.Throws<IOException>(() => VbfArchiveWriter.Save(imported, At("bad.vbf")));
        Assert.False(File.Exists(At("bad.vbf")));
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }

    [Fact]
    public void DetectsImportedFileModifiedDuringWriteEvenWithSameLengthAndTimestamp()
    {
        string input = Put(At("input.bin"), new byte[200000]); var imported = VbfWorkspace.Import(new[] { input });
        DateTime timestamp = File.GetLastWriteTimeUtc(input);
        var progress = new CallbackProgress(p =>
        {
            if (p.Phase != "verify") return;
            File.WriteAllBytes(input, Enumerable.Repeat((byte)7, 200000).ToArray()); File.SetLastWriteTimeUtc(input, timestamp);
        });
        Assert.Throws<IOException>(() => VbfArchiveWriter.Save(imported, At("bad.vbf"), progress: progress));
        Assert.False(File.Exists(At("bad.vbf")));
    }

    [Fact]
    public void ExtractionPreflightsConflictsAndPreservesOverwrittenFilesInBackups()
    {
        var archive = VbfArchive.Open(Fixture(Samples())); var workspace = new VbfWorkspace(archive);
        string extraction = At("out"); Put(Path.Combine(extraction, "one.bin"), new byte[] { 99 });
        Assert.Throws<IOException>(() => VbfArchiveOperations.Extract(workspace.Files, extraction));
        Assert.False(File.Exists(Path.Combine(extraction, "empty.bin")));
        Assert.Equal(workspace.Files.Count, VbfArchiveOperations.Extract(workspace.Files, extraction, overwrite: true));
        Assert.Equal(new byte[] { 99 }, File.ReadAllBytes(Directory.GetFiles(extraction, "one.bin.bak.*").Single()));
        foreach (var entry in archive.Entries)
            Assert.Equal(Extract(archive, entry), File.ReadAllBytes(Path.Combine(extraction, entry.Path)));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("c:/drive")]
    [InlineData("dir//file")]
    [InlineData("dir/./file")]
    [InlineData("dir/CON.txt")]
    [InlineData("dir/trailing.")]
    public void UnsafePathsAreRejected(string name)
    {
        Assert.Throws<ArgumentException>(() => VbfArchive.NormalizePath(name));
        Assert.Throws<InvalidDataException>(() => VbfArchive.Open(Fixture(new() { [name] = new byte[] { 1 } })));
    }

    [Fact]
    public void ImportRejectsCollisionsAndLinksAndExtractionRejectsSymlinkEscape()
    {
        string a = Put(At("a/SAME.bin"), new byte[] { 1 });
        string b = Put(At("b/same.bin"), new byte[] { 2 });
        Assert.Throws<IOException>(() => VbfWorkspace.Import(new[] { a, b }));
        if (OperatingSystem.IsWindows()) return; // symlink creation may require a privilege on Windows
        Directory.CreateDirectory(At("outside")); Directory.CreateDirectory(At("out"));
        Directory.CreateSymbolicLink(At("out/nested"), At("outside"));
        Assert.Throws<IOException>(() => VbfWorkspace.Import(new[] { At("out") }));
        var archive = VbfArchive.Open(Fixture(Samples()));
        Assert.Throws<IOException>(() => VbfArchiveOperations.Extract(new VbfWorkspace(archive).Files, At("out")));
        Assert.Empty(Directory.GetFiles(At("outside")));
    }

    [Fact]
    public void CorruptChecksumAndValidlyHashedInvalidRangesAreRejected()
    {
        byte[] valid = File.ReadAllBytes(Fixture(new() { ["one.bin"] = new byte[] { 1, 2, 3 } }));
        var mutations = new Action<byte[]>[]
        {
            b => b[0] = 0,
            b => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), uint.MaxValue),
            b => BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(8), ulong.MaxValue),
            b => b[16] ^= 1,
            b => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(32), 5),
            b => BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(40), ulong.MaxValue),
            b => BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(48), 1),
            b => BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(56), 9999),
            b => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(64), uint.MaxValue)
        };
        foreach (var mutate in mutations)
        {
            byte[] changed = (byte[])valid.Clone(); mutate(changed);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(changed.AsSpan(4));
            if (length < changed.Length - 16) MD5.HashData(changed.AsSpan(0, (int)length)).CopyTo(changed.AsSpan(changed.Length - 16));
            string path = Put(At("malformed.vbf"), changed);
            Assert.Throws<InvalidDataException>(() => VbfArchive.Open(path));
        }
        valid[^1] ^= 1;
        Assert.Throws<InvalidDataException>(() => VbfArchive.Open(Put(At("bad-footer.vbf"), valid)));
        Assert.Throws<InvalidDataException>(() => VbfArchive.Open(Put(At("short.vbf"), new byte[10])));
    }

    [Fact]
    public void PayloadCorruptionFailsVerificationAndDoesNotPublishExtraction()
    {
        string file = Fixture(new() { ["large.bin"] = new byte[180000] });
        var archive = VbfArchive.Open(file); var entry = archive.Entries.Single();
        byte[] bytes = File.ReadAllBytes(file);
        int stored = archive.BlockLength(0);
        bytes[(int)entry.Offset + stored - 1] ^= 1; // Adler32, outside header MD5
        File.WriteAllBytes(file, bytes);
        archive = VbfArchive.Open(file);
        Assert.Throws<InvalidDataException>(() => VbfArchiveOperations.Verify(archive));
        Assert.Throws<InvalidDataException>(() => VbfArchiveOperations.Extract(new VbfWorkspace(archive).Files, At("out")));
        Assert.False(File.Exists(At("out/large.bin")));
        Assert.Empty(Directory.GetFiles(At("out"), "*.tmp"));
    }

    [Fact]
    public void EmptyArchiveAndZeroLengthEntryAreSupported()
    {
        string output = At("empty.vbf"); VbfArchiveWriter.Save(Array.Empty<VbfContent>(), output);
        Assert.Empty(VbfArchive.Open(output).Entries);
        string empty = Put(At("empty.bin"), Array.Empty<byte>());
        VbfArchiveWriter.Save(VbfWorkspace.Import(new[] { empty }), At("one-empty.vbf"));
        var archive = VbfArchive.Open(At("one-empty.vbf"));
        Assert.Empty(Extract(archive, archive.Entries.Single()));
    }

    [Fact]
    public void ExtractionDoesNotOverwriteAFileCreatedAfterPreflight()
    {
        var archive = VbfArchive.Open(Fixture(new() { ["a.bin"] = new byte[] { 1 }, ["b.bin"] = new byte[] { 2 } }));
        string output = At("out");
        var progress = new CallbackProgress(p =>
        {
            if (p.Completed == 1) Put(Path.Combine(output, "b.bin"), new byte[] { 99 });
        });
        Assert.Throws<IOException>(() => VbfArchiveOperations.Extract(new VbfWorkspace(archive).Files, output, progress: progress));
        Assert.Equal(new byte[] { 99 }, File.ReadAllBytes(Path.Combine(output, "b.bin")));
    }

    [Fact]
    public void ReaderHandlesPayloadOffsetsAboveFourGiBWithoutAllocatingTheArchive()
    {
        string fixture = Fixture(new() { ["large-offset.bin"] = new byte[] { 91 } });
        byte[] initial = File.ReadAllBytes(fixture);
        int headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(initial.AsSpan(4));
        byte[] header = initial.AsSpan(0, headerSize).ToArray();
        long offset = (1L << 32) + 128;
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(48), (ulong)offset);
        using (var stream = new FileStream(At("sparse.vbf"), FileMode.Create, FileAccess.Write))
        {
            stream.Write(header); stream.Position = offset; stream.WriteByte(91); stream.Write(MD5.HashData(header));
        }
        var archive = VbfArchive.Open(At("sparse.vbf"));
        Assert.Equal(new byte[] { 91 }, Extract(archive, archive.Entries.Single()));
        Assert.True(archive.FileSize > uint.MaxValue);
    }

    [Fact]
    public void FileFolderCollisionFailsBeforeChangingWorkspaceOrExtraction()
    {
        string local = Put(At("source.bin"), new byte[] { 1 });
        var content = VbfWorkspace.Import(new[] { local }).Single();
        var workspace = new VbfWorkspace(); workspace.Add(new[] { content with { Path = "folder" } });
        Assert.Throws<IOException>(() => workspace.Add(new[] { content with { Path = "folder/child" } }));
        Assert.Single(workspace.Files);
        var archive = VbfArchive.Open(Fixture(new() { ["folder"] = new byte[] { 1 }, ["folder/child"] = new byte[] { 2 } }));
        Assert.Throws<IOException>(() => VbfArchiveOperations.Extract(new VbfWorkspace(archive).Files, At("out")));
        Assert.False(Directory.Exists(At("out")));
    }

    [Fact]
    public async Task BrowserNavigatesSearchesStagesAndSavesWithoutExternalTools()
    {
        var model = new VbfExtract_DataModel();
        await model.OpenAsync(Fixture(Samples()));
        Assert.False(model.IsBusy); Assert.True(model.CanVerify);
        Assert.Contains(model.Rows, x => x.IsFolder && x.Path == "nested/");
        model.Navigate("nested/"); Assert.Single(model.Rows);
        model.Up(); model.FilterText = "compressible"; Assert.Single(model.Rows);
        model.FilterText = "";
        string file = Put(At("new.bin"), new byte[] { 10, 20 });
        await model.AddAsync(new[] { file }); Assert.True(model.HasUnsavedChanges);
        model.Delete(new[] { "new.bin" }); model.Undo();
        Assert.Contains(model.Workspace.Files, x => x.Path == "new.bin");
        await model.SaveAsync(At("browser.vbf"));
        Assert.False(model.HasUnsavedChanges);
        Assert.Contains(VbfArchive.Open(At("browser.vbf")).Entries, x => x.Path == "new.bin");
        int count = model.Workspace.Files.Count;
        await model.OpenAsync(At("missing.vbf")); Assert.Equal(count, model.Workspace.Files.Count);
    }

    [Fact]
    public async Task ImportConfirmationReportsAddsAndReplacementsAndCancelDoesNotMutate()
    {
        var model = new VbfExtract_DataModel();
        await model.OpenAsync(Fixture(Samples()));
        var original = model.Workspace.Files.ToArray();
        string folder = At("import");
        Put(Path.Combine(folder, "one.bin"), new byte[] { 8 });
        Put(Path.Combine(folder, "new.bin"), new byte[] { 9 });
        int called = 0;
        await model.AddAsync(new[] { folder }, folderContents: true, confirm: preview =>
        {
            called++;
            Assert.Equal(1, preview.Added); Assert.Equal(1, preview.Replaced); Assert.Equal("/", preview.Folder);
            Assert.Equal(original, model.Workspace.Files.ToArray());
            return Task.FromResult(false);
        });
        Assert.Equal(1, called); Assert.Equal(original, model.Workspace.Files.ToArray());
        Assert.False(model.HasUnsavedChanges); Assert.False(model.IsBusy);
        await model.AddAsync(new[] { folder }, folderContents: true, confirm: _ => Task.FromResult(true));
        Assert.Equal(original.Length + 1, model.Workspace.Files.Count);
        Assert.True(model.Workspace.Files.Single(x => x.Path == "one.bin").IsPending);
    }

    [Fact]
    public async Task DecliningReplaceOrCreateRetainsTheOpenedArchive()
    {
        var model = new VbfExtract_DataModel();
        string fixture = Fixture(Samples()); await model.OpenAsync(fixture);
        var original = model.Workspace;
        string file = Put(At("new.bin"), new byte[] { 8 });
        await model.ReplaceAsync("one.bin", file, preview =>
        {
            Assert.Equal(1, preview.Replaced); Assert.Equal(0, preview.Added);
            Assert.Equal("one.bin", preview.Paths.Single());
            return Task.FromResult(false);
        });
        Assert.False(model.Workspace.Files.Single(x => x.Path == "one.bin").IsPending);
        await model.AddAsync(new[] { file }, confirm: _ => Task.FromResult(false), startNewArchive: true);
        Assert.Same(original, model.Workspace); Assert.Equal(fixture, model.SourcePath);
        Assert.False(model.HasUnsavedChanges);
    }

    static Dictionary<string, byte[]> Samples()
    {
        var random = new byte[131199]; new Random(752).NextBytes(random);
        return new()
        {
            ["empty.bin"] = Array.Empty<byte>(), ["one.bin"] = new byte[] { 42 },
            ["below.bin"] = new byte[65535], ["exact.bin"] = new byte[65536],
            ["above.bin"] = new byte[65537], ["random.bin"] = random,
            ["nested/compressible.bin"] = Enumerable.Repeat((byte)65, 220000).ToArray()
        };
    }

    static byte[] Extract(VbfArchive archive, VbfEntry entry)
    {
        using var output = new MemoryStream(); archive.ExtractTo(entry, output); return output.ToArray();
    }

    string Put(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); return path;
    }

    // Independent fixture encoder: explicit SRYK layout, not the production writer.
    // It also mimics the shipped packer's garbage offset on empty entries.
    string Fixture(Dictionary<string, byte[]> files)
    {
        var names = files.Keys.ToArray(); var payload = new MemoryStream();
        var blockSizes = new List<ushort>(); var starts = new List<int>(); var offsets = new List<long>();
        foreach (string name in names)
        {
            byte[] data = files[name]; starts.Add(blockSizes.Count); offsets.Add(payload.Position);
            for (int i = 0; i < data.Length; i += 65536)
            {
                int count = Math.Min(65536, data.Length - i);
                using var packed = new MemoryStream();
                if (i + count < data.Length)
                    using (var z = new ZLibStream(packed, CompressionLevel.SmallestSize, true)) z.Write(data, i, count);
                byte[] block = packed.Length > 0 && packed.Length < count ? packed.ToArray() : data.AsSpan(i, count).ToArray();
                blockSizes.Add(block.Length == 65536 ? (ushort)0 : (ushort)block.Length); payload.Write(block);
            }
        }
        byte[] nameTable = Encoding.UTF8.GetBytes(string.Join('\0', names) + (names.Length == 0 ? "" : "\0"));
        int headerSize = 20 + names.Length * 48 + nameTable.Length + blockSizes.Count * 2;
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output, Encoding.UTF8, true);
        writer.Write(0x4b595253u); writer.Write((uint)headerSize); writer.Write((ulong)names.Length);
        foreach (string name in names) writer.Write(MD5.HashData(Encoding.UTF8.GetBytes(name.ToLowerInvariant())));
        ulong nameOffset = 0;
        for (int i = 0; i < names.Length; i++)
        {
            writer.Write((uint)starts[i]); writer.Write(0x003d13f6u); writer.Write((ulong)files[names[i]].Length);
            writer.Write(files[names[i]].Length == 0 ? 0x12345678ffffUL : (ulong)(headerSize + offsets[i]));
            writer.Write(nameOffset); nameOffset += (ulong)Encoding.UTF8.GetByteCount(names[i]) + 1;
        }
        writer.Write((uint)nameTable.Length + 4); writer.Write(nameTable);
        foreach (ushort size in blockSizes) writer.Write(size);
        byte[] header = output.ToArray(); writer.Write(payload.ToArray()); writer.Write(MD5.HashData(header));
        return Put(At("fixture.vbf"), output.ToArray());
    }

    sealed class CallbackProgress(Action<VbfProgress> action) : IProgress<VbfProgress>
    { public void Report(VbfProgress value) => action(value); }
}

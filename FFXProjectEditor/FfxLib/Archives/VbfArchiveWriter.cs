using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace FFXProjectEditor.FfxLib.Archives;

internal sealed record VbfProgress(string Phase, string Path, long Completed, long Total);
internal sealed record VbfSaveResult(string Path, string Sha256, string? BackupPath,
    IReadOnlyDictionary<string, string> SourceHashes);

internal static class VbfArchiveWriter
{
    public static VbfSaveResult Save(IReadOnlyCollection<VbfContent> contents, string destination,
        bool compress = true, IProgress<VbfProgress>? progress = null, CancellationToken token = default,
        VbfArchive? original = null)
    {
        destination = Path.GetFullPath(destination);
        VbfSafeFileSystem.RejectLinks(destination);
        var files = contents.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray();
        if (files.Length > VbfArchive.MaxEntries || files.Select(x => VbfArchive.NormalizePath(x.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length)
            throw new InvalidDataException("Too many files or duplicate VBF paths.");
        VbfWorkspace.ValidateHierarchy(files.Select(x => x.Path));
        var archives = files.Where(x => x.Archive != null).Select(x => x.Archive!).Append(original)
            .Where(x => x != null).Cast<VbfArchive>().Distinct().ToArray();
        var inputs = files.Where(x => x.LocalPath != null).Select(x => Path.GetFullPath(x.LocalPath!))
            .Concat(archives.Select(x => x.FilePath)).ToArray();
        if (inputs.Any(x => string.Equals(x, destination, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Choose a different output VBF. Source files are preserved.");
        string directory = Path.GetDirectoryName(destination)!;
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);

        long namesSize = files.Sum(x => (long)VbfArchive.Utf8.GetByteCount(x.Path) + 1);
        long blockCount = files.Sum(x => x.Size / VbfArchive.BlockSize + (x.Size % VbfArchive.BlockSize == 0 ? 0 : 1));
        long headerSize = checked(20L + 48L * files.Length + namesSize + 2 * blockCount);
        if (files.Any(x => x.Size < 0) || headerSize > VbfArchive.MaxHeaderSize)
            throw new InvalidDataException("VBF index exceeds supported limits.");
        long required = checked(headerSize + 16 + files.Sum(x => x.PackedSize) +
            (File.Exists(destination) ? new FileInfo(destination).Length : 0));
        VbfSafeFileSystem.EnsureSpace(destination, required);
        string? previousHash = File.Exists(destination) ? HashFile(destination, token) : null;
        string temp = Path.Combine(directory, "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        var sources = new Dictionary<VbfArchive, FileStream>();
        var sourceHashes = new Dictionary<string, string>(VbfSafeFileSystem.PathComparison == StringComparison.OrdinalIgnoreCase
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        try
        {
            foreach (var archive in archives)
            {
                progress?.Report(new("source", archive.FilePath, 0, archive.FileSize));
                var source = archive.OpenVerifiedSource();
                sources.Add(archive, source);
                sourceHashes.Add(archive.FilePath, HashStream(source, token));
            }
            var offsets = new long[files.Length];
            var starts = new int[files.Length];
            var blockSizes = new ushort[(int)blockCount];
            var hashes = new string[files.Length];
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                output.Position = headerSize;
                int blockIndex = 0;
                byte[] buffer = new byte[VbfArchive.BlockSize];
                long total = files.Sum(x => x.PackedSize), done = 0;
                for (int i = 0; i < files.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var item = files[i];
                    offsets[i] = output.Position; starts[i] = blockIndex;
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    if (item.Archive != null && item.Entry != null)
                    {
                        var input = sources[item.Archive];
                        input.Position = item.Entry.Offset;
                        for (int b = 0; b < item.Entry.BlockCount; b++)
                        {
                            token.ThrowIfCancellationRequested();
                            ushort size = item.Archive.BlockLength(item.Entry.BlockStart + b);
                            int length = VbfArchive.StoredLength(size);
                            input.ReadExactly(buffer.AsSpan(0, length));
                            output.Write(buffer, 0, length); hash.AppendData(buffer, 0, length);
                            blockSizes[blockIndex++] = size;
                            done += length;
                            progress?.Report(new("write", item.Path, done, total));
                        }
                    }
                    else
                    {
                        using var input = item.OpenLocal();
                        long remaining = item.Size;
                        while (remaining > 0)
                        {
                            token.ThrowIfCancellationRequested();
                            int length = (int)Math.Min(buffer.Length, remaining);
                            input.ReadExactly(buffer.AsSpan(0, length));
                            hash.AppendData(buffer, 0, length);
                            byte[]? encoded = null;
                            // Keep the final block raw, matching the shipped PC packer
                            // and avoiding the partial-block/raw ambiguity in consumers.
                            if (compress && remaining > buffer.Length)
                            {
                                using var encodedStream = new MemoryStream();
                                using (var zlib = new ZLibStream(encodedStream, CompressionLevel.Optimal, true))
                                    zlib.Write(buffer, 0, length);
                                if (encodedStream.Length < length) encoded = encodedStream.ToArray();
                            }
                            int stored = encoded?.Length ?? length;
                            blockSizes[blockIndex++] = stored == VbfArchive.BlockSize ? (ushort)0 : (ushort)stored;
                            if (encoded != null) output.Write(encoded); else output.Write(buffer, 0, length);
                            remaining -= length; done += length;
                            progress?.Report(new("write", item.Path, done, total));
                        }
                        if (input.ReadByte() != -1) throw new IOException("Imported file grew during repack: " + item.LocalPath);
                    }
                    hashes[i] = Convert.ToHexString(hash.GetHashAndReset());
                }
                long end = output.Position;
                output.Position = 0;
                using var writer = new BinaryWriter(output, VbfArchive.Utf8, true);
                writer.Write(VbfArchive.Magic); writer.Write((uint)headerSize); writer.Write((ulong)files.Length);
                foreach (var item in files) writer.Write(MD5.HashData(VbfArchive.Utf8.GetBytes(item.Path)));
                ulong nameOffset = 0;
                for (int i = 0; i < files.Length; i++)
                {
                    writer.Write((uint)starts[i]);
                    writer.Write(files[i].Entry?.OpaqueField ?? 0u);
                    writer.Write((ulong)files[i].Size); writer.Write((ulong)offsets[i]); writer.Write(nameOffset);
                    nameOffset += (ulong)VbfArchive.Utf8.GetByteCount(files[i].Path) + 1;
                }
                writer.Write((uint)namesSize + 4);
                foreach (var item in files) { writer.Write(VbfArchive.Utf8.GetBytes(item.Path)); writer.Write((byte)0); }
                foreach (ushort size in blockSizes) writer.Write(size);
                if (output.Position != headerSize) throw new InvalidDataException("VBF header accounting mismatch.");
                output.Position = 0;
                byte[] header = new byte[(int)headerSize]; output.ReadExactly(header);
                output.Position = end; output.Write(MD5.HashData(header)); output.Flush(true);
            }

            var generated = VbfArchive.Open(temp, token);
            using (var input = generated.OpenVerifiedSource())
            {
                for (int i = 0; i < files.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var expected = files[i]; var entry = generated.Entries[i];
                    if (entry.Path != expected.Path || entry.Size != expected.Size)
                        throw new InvalidDataException("VBF readback index mismatch.");
                    progress?.Report(new("verify", entry.Path, i, files.Length));
                    string actual;
                    if (expected.Entry != null)
                    {
                        // Prove byte preservation, including the original compressed
                        // representation. No recompression or reinterpretation of opaque data.
                        input.Position = entry.Offset;
                        actual = HashRange(input, entry.PackedSize, token);
                    }
                    else
                    {
                        using var sink = new VbfHashSink();
                        generated.ExtractTo(input, entry, sink, token);
                        actual = sink.Finish();
                        using var local = expected.OpenLocal();
                        if (HashStream(local, token) != hashes[i])
                            throw new IOException("Imported file changed during repack: " + expected.LocalPath);
                    }
                    if (actual != hashes[i]) throw new InvalidDataException("VBF readback payload mismatch: " + entry.Path);
                }
            }
            foreach (var source in sources)
            {
                progress?.Report(new("source", source.Key.FilePath, 0, source.Key.FileSize));
                if (HashStream(source.Value, token) != sourceHashes[source.Key.FilePath])
                    throw new IOException("Source VBF changed during repack.");
                using var unchanged = source.Key.OpenVerifiedSource();
            }
            progress?.Report(new("hash", destination, 0, new FileInfo(temp).Length));
            string outputHash = HashFile(temp, token);
            token.ThrowIfCancellationRequested();
            string? backup = Promote(temp, destination, previousHash, token);
            progress?.Report(new("complete", destination, files.Length, files.Length));
            return new VbfSaveResult(destination, outputHash, backup, sourceHashes);
        }
        finally
        {
            foreach (var stream in sources.Values) stream.Dispose();
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    internal static string? Promote(string temp, string destination, string? expectedHash, CancellationToken token)
    {
        VbfSafeFileSystem.RejectLinks(destination);
        string? actual = File.Exists(destination) ? HashFile(destination, token) : null;
        if (actual != expectedHash) throw new IOException("The destination changed during the operation.");
        string? backup = null;
        if (actual != null)
        {
            backup = destination + ".bak." + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "." + Guid.NewGuid().ToString("N");
            File.Copy(destination, backup, false);
            if (HashFile(backup, token) != actual) throw new IOException("Backup verification failed.");
        }
        token.ThrowIfCancellationRequested();
        // Temporary file and target share a filesystem. Existing destinations have
        // a verified, recoverable backup before the single atomic rename.
        File.Move(temp, destination, expectedHash != null);
        return backup;
    }

    internal static string HashFile(string path, CancellationToken token)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return HashStream(input, token);
    }

    internal static string HashStream(Stream input, CancellationToken token)
    {
        input.Position = 0;
        return HashRange(input, input.Length, token);
    }

    internal static string HashRange(Stream input, long length, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            while (length > 0)
            {
                token.ThrowIfCancellationRequested();
                int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, length));
                if (read == 0) throw new EndOfStreamException();
                hash.AppendData(buffer, 0, read); length -= read;
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
}

internal sealed class VbfHashSink : Stream
{
    readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    public string Finish() => Convert.ToHexString(hash.GetHashAndReset());
    public override void Write(byte[] buffer, int offset, int count) => hash.AppendData(buffer, offset, count);
    public override void Write(ReadOnlySpan<byte> buffer) => hash.AppendData(buffer);
    protected override void Dispose(bool disposing) { if (disposing) hash.Dispose(); base.Dispose(disposing); }
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace FFXProjectEditor.FfxLib.Archives;

internal static class VbfArchiveOperations
{
    public static int Extract(IReadOnlyCollection<VbfContent> files, string root, bool overwrite = false,
        IProgress<VbfProgress>? progress = null, CancellationToken token = default)
    {
        root = Path.GetFullPath(root);
        VbfSafeFileSystem.RejectLinks(root);
        VbfWorkspace.ValidateHierarchy(files.Select(x => x.Path));
        var planned = files.Select(x => (Content: x, Destination: VbfSafeFileSystem.Destination(root, x.Path))).ToArray();
        var inputs = files.Select(x => x.LocalPath ?? x.Archive?.FilePath).Where(x => x != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Complete conflict preflight before writing even the first output.
        foreach (var item in planned)
        {
            if (inputs.Contains(item.Destination)) throw new IOException("Extraction would overwrite a source file.");
            if (Directory.Exists(item.Destination) || (!overwrite && File.Exists(item.Destination)))
                throw new IOException("Extraction destination already exists: " + item.Destination);
        }
        VbfSafeFileSystem.EnsureSpace(root, checked(files.Sum(x => x.Size) + planned.Sum(x => File.Exists(x.Destination) ? new FileInfo(x.Destination).Length : 0)));
        var sources = new Dictionary<VbfArchive, FileStream>();
        int done = 0;
        try
        {
            foreach (var item in planned)
            {
                token.ThrowIfCancellationRequested();
                string target = item.Destination;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                VbfSafeFileSystem.RejectLinks(target);
                string? previous = File.Exists(target) ? VbfArchiveWriter.HashFile(target, token) : null;
                if (!overwrite && previous != null)
                    throw new IOException("Extraction destination appeared during the operation: " + target);
                string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        if (item.Content.Archive is { } archive && item.Content.Entry is { } entry)
                        {
                            if (!sources.TryGetValue(archive, out var input))
                            { input = archive.OpenVerifiedSource(); sources.Add(archive, input); }
                            archive.ExtractTo(input, entry, output, token);
                        }
                        else
                        {
                            using var input = item.Content.OpenLocal();
                            byte[] buffer = new byte[VbfArchive.BlockSize];
                            long remaining = item.Content.Size;
                            while (remaining > 0)
                            {
                                token.ThrowIfCancellationRequested();
                                int count = (int)Math.Min(buffer.Length, remaining);
                                input.ReadExactly(buffer.AsSpan(0, count)); output.Write(buffer, 0, count); remaining -= count;
                            }
                        }
                        output.Flush(true);
                    }
                    VbfArchiveWriter.Promote(temp, target, previous, token);
                    done++;
                    progress?.Report(new("extract", item.Content.Path, done, planned.Length));
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            return done;
        }
        finally { foreach (var input in sources.Values) input.Dispose(); }
    }

    public static void Verify(VbfArchive archive, IProgress<VbfProgress>? progress = null, CancellationToken token = default)
    {
        using var input = archive.OpenVerifiedSource();
        int done = 0;
        foreach (var entry in archive.Entries)
        {
            archive.ExtractTo(input, entry, Stream.Null, token);
            done++;
            progress?.Report(new("verify", entry.Path, done, archive.Entries.Count));
        }
    }
}

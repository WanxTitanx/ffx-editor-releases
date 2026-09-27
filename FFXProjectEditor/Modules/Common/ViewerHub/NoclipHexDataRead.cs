// WHY: NoClip emits lowercase hex IDs while established extractions may use uppercase filenames.
// MAINT: This is a bounded ASCII hex-bin rule, not a case-insensitive filesystem or path fallback.
// Each probe stays below a retained read-only parent; ambiguity and observed namespace changes refuse.
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32.SafeHandles;
using FFXProjectEditor.Diagnostics;
using G = FFXProjectEditor.Modules.Common.ViewerHub.FileSystemReparseGuard;
using L = FFXProjectEditor.Modules.Common.ViewerHub.LinuxReadFileSystem;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

internal static class NoclipHexDataRead
{
    internal static G.VerifiedOpenResult TryOpen(
        string root, string candidatePath, string requestPath, out G.VerifiedReadFile? verified)
    {
        verified = null;
        if (!L.IsSupported || !IsHexDataRequest(requestPath))
            return G.TryOpenVerifiedRead(root, candidatePath, out verified);

        G.VerifiedReadFile? found = null;
        try
        {
            string canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            string candidate = Path.GetFullPath(candidatePath);
            string relative = Path.GetRelativePath(canonicalRoot, candidate);
            if (Path.IsPathRooted(relative) || relative is "." or ".." ||
                relative.StartsWith("../", StringComparison.Ordinal))
                return G.VerifiedOpenResult.Rejected;

            string requestedName = requestPath[^8..];
            if (!string.Equals(Path.GetFileName(candidate), requestedName, StringComparison.OrdinalIgnoreCase))
                return G.VerifiedOpenResult.Rejected;
            string parentPath = Path.GetDirectoryName(candidate)!;
            if (!G.TryOpenVerifiedDirectory(canonicalRoot, out G.VerifiedDirectory? trusted) || trusted == null)
                return G.VerifiedOpenResult.Rejected;
            using (trusted)
            {
                string parentRelative = Path.GetRelativePath(canonicalRoot, parentPath);
                // Retain the original mapped root: reopening an arbitrary absolute parent would
                // bypass the original NO_XDEV rule for a nested mount.
                SafeFileHandle parentHandle;
                try
                {
                    parentHandle = parentRelative == "." ?
                        L.OpenRoot(parentPath) : L.OpenRead(trusted.Handle, parentRelative);
                }
                catch (Exception error) when (parentRelative != "." && L.IsMissing(error))
                {
                    return G.VerifiedOpenResult.NotFound;
                }
                using var parent = new G.VerifiedDirectory(
                    parentHandle, parentPath, G.FileIdentityKind.Linux);
                if (parentRelative == "." &&
                    L.Observe(parent.Handle, L.DirectoryType).Identity !=
                    L.Observe(trusted.Handle, L.DirectoryType).Identity)
                    return G.VerifiedOpenResult.Rejected;
                L.LinuxFileObservation directoryBefore = L.Observe(parent.Handle, L.DirectoryType);
                L.LinuxFileObservation foundBefore = default;
                foreach (string name in Variants(requestedName[..4]))
                {
                    G.VerifiedOpenResult result = G.TryOpenVerifiedRead(parent, name + ".bin", out var probe);
                    if (result == G.VerifiedOpenResult.NotFound)
                    {
                        probe?.Dispose();
                        continue;
                    }
                    if (result != G.VerifiedOpenResult.Success || probe == null)
                    {
                        probe?.Dispose();
                        return G.VerifiedOpenResult.Rejected;
                    }
                    if (found != null)
                    {
                        probe.Dispose();
                        return G.VerifiedOpenResult.Rejected; // Even two hardlinked spellings are ambiguous.
                    }
                    found = probe;
                    foundBefore = L.Observe(found.Stream.SafeFileHandle, L.RegularFileType);
                }

                // Directory observations detect changes during this finite probe; no permanent
                // membership, mandatory lock or immutable HTTP stream is claimed.
                if (L.Observe(parent.Handle, L.DirectoryType) != directoryBefore)
                    return G.VerifiedOpenResult.Rejected;
                if (found != null)
                {
                    G.VerifiedOpenResult final = G.TryOpenVerifiedRead(
                        parent, Path.GetFileName(found.FullPath), out var namedFile);
                    using (namedFile)
                    {
                        if (final != G.VerifiedOpenResult.Success || namedFile == null ||
                            L.Observe(namedFile.Stream.SafeFileHandle, L.RegularFileType) != foundBefore ||
                            L.Observe(found.Stream.SafeFileHandle, L.RegularFileType) != foundBefore ||
                            L.Observe(parent.Handle, L.DirectoryType) != directoryBefore)
                            return G.VerifiedOpenResult.Rejected;
                    }
                }
                using var namedParent = L.OpenRoot(parentPath);
                if (L.Observe(namedParent, L.DirectoryType) != directoryBefore)
                    return G.VerifiedOpenResult.Rejected;
                if (found == null) return G.VerifiedOpenResult.NotFound;
                DebugLog.Info("ViewerHub.HexData", "Resolved one unambiguous NoClip hex file without mutation.");
                verified = found;
                found = null;
                return G.VerifiedOpenResult.Success;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            DebugLog.Warn("ViewerHub.HexData", "NoClip hex-file observation was refused.");
            return G.VerifiedOpenResult.Rejected;
        }
        finally { found?.Dispose(); }
    }

    private static bool IsHexDataRequest(string requestPath)
    {
        const string prefix = "/data/FinalFantasyX/";
        if (requestPath is null || requestPath.Length != prefix.Length + 11 ||
            !requestPath.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        ReadOnlySpan<char> tail = requestPath.AsSpan(prefix.Length);
        if (!IsLowerHex(tail[0]) || !IsLowerHex(tail[1]) || tail[2] != '/' ||
            !tail[7..].SequenceEqual(".bin".AsSpan()))
            return false;
        for (int i = 3; i < 7; i++)
            if (!IsLowerHex(char.ToLowerInvariant(tail[i])) || tail[i] > 127)
                return false;
        return true;
    }

    private static bool IsLowerHex(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f';

    private static IReadOnlyList<string> Variants(string id)
    {
        string lower = id.ToLowerInvariant();
        var names = new List<string>(16) { lower };
        for (int position = 0; position < 4; position++)
        {
            if (lower[position] is not (>= 'a' and <= 'f')) continue;
            int previousCount = names.Count;
            for (int index = 0; index < previousCount; index++)
            {
                char[] letters = names[index].ToCharArray();
                letters[position] = char.ToUpperInvariant(letters[position]);
                names.Add(new string(letters));
            }
        }
        return names; // Four ASCII hex digits have at most sixteen distinct spellings.
    }
}

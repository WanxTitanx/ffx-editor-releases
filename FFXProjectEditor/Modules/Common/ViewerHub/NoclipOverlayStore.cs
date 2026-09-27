using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

// ── Per-user NoClip preview overlays ───────────────────────────────────────────────
// The selected game-data extraction is a read-only capability. Every native preview mutation
// is staged atomically under viewer-data/noclip-overlays and exposed by a narrow HTTP route.
internal static class NoclipOverlayStore
{
    internal static readonly ConcurrentDictionary<string, object> EditWriteLocks = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<
        string,
        ConcurrentDictionary<string, FileSystemReparseGuard.FileIdentity>> OwnedBattleFiles = new(
            StringComparer.Ordinal);

    internal static string ProcessSessionId { get; } = Guid.NewGuid().ToString("N");

    internal static string ResolveFinalFantasyXRoot(string viewerDataRoot) => Path.GetFullPath(
        Path.Combine(viewerDataRoot, "noclip-overlays", "FinalFantasyX"));

    internal static string ResolveBattleRoot(string viewerDataRoot) =>
        Path.Combine(ResolveFinalFantasyXRoot(viewerDataRoot), "0e");

    internal static string ResolveBattleSessionRoot(string viewerDataRoot, string sessionId)
    {
        if (!Guid.TryParseExact(sessionId, "N", out _))
            throw new ArgumentException("The overlay session id must be a canonical GUID.", nameof(sessionId));
        return Path.Combine(ResolveBattleRoot(viewerDataRoot), sessionId);
    }

    internal static string ResolveEditsRoot(string viewerDataRoot) =>
        Path.Combine(ResolveFinalFantasyXRoot(viewerDataRoot), "edits");

    internal static string StageBattle(string viewerDataRoot, int encounterId, byte[] content)
        => StageBattle(viewerDataRoot, encounterId, content, ProcessSessionId);

    internal static string StageBattle(
        string viewerDataRoot,
        int encounterId,
        byte[] content,
        string sessionId)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0)
            throw new ArgumentException("Battle preview content cannot be empty.", nameof(content));
        if (encounterId is < 0 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(encounterId));

        string directory = ResolveBattleSessionRoot(viewerDataRoot, sessionId);
        string destination = Path.Combine(
            directory,
            $"{encounterId:X4}.{Guid.NewGuid():N}.bin");
        string canonicalDestination = Path.GetFullPath(destination);
        FileSystemReparseGuard.FileIdentity identity =
            WriteBytesAtomically(canonicalDestination, content);
        OwnedBattleFiles.GetOrAdd(
            sessionId,
            static _ => new ConcurrentDictionary<string, FileSystemReparseGuard.FileIdentity>(
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))[
                    canonicalDestination] = identity;
        return canonicalDestination;
    }

    internal static IReadOnlyCollection<string> SnapshotOwnedBattleFiles(string sessionId) =>
        OwnedBattleFiles.TryGetValue(
            sessionId,
            out ConcurrentDictionary<string, FileSystemReparseGuard.FileIdentity>? files)
            ? files.Keys.ToArray()
            : Array.Empty<string>();

    internal static bool TryDeleteOwnedBattle(string viewerDataRoot, string sessionId, string stagedPath)
    {
        string sessionRoot;
        string candidate;
        try
        {
            sessionRoot = Path.GetFullPath(ResolveBattleSessionRoot(viewerDataRoot, sessionId));
            candidate = Path.GetFullPath(stagedPath);
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!string.Equals(Path.GetDirectoryName(candidate), sessionRoot, comparison))
                return false;
        }
        catch
        {
            return false;
        }

        if (!OwnedBattleFiles.TryGetValue(
                sessionId,
                out ConcurrentDictionary<string, FileSystemReparseGuard.FileIdentity>? files) ||
            !files.TryGetValue(candidate, out FileSystemReparseGuard.FileIdentity expectedIdentity))
            return false;
        if (!FileSystemReparseGuard.TryDeleteVerifiedFile(
                sessionRoot,
                candidate,
                expectedIdentity))
            return false;
        files.TryRemove(candidate, out _);
        return true;
    }

    internal static string WriteEdit(
        string viewerDataRoot,
        int encounterId,
        int slot,
        double? dx,
        double? dy,
        double? dz,
        double? heading,
        double? scale)
    {
        if (encounterId is < 0 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(encounterId));
        return WriteEditToRoot(
            ResolveEditsRoot(viewerDataRoot),
            encounterId.ToString(CultureInfo.InvariantCulture),
            slot,
            dx,
            dy,
            dz,
            heading,
            scale);
    }

    internal static string WriteEditToRoot(
        string editsRoot,
        string encounterId,
        int slot,
        double? dx,
        double? dy,
        double? dz,
        double? heading,
        double? scale)
    {
        if (!TryNormalizeEncounterId(encounterId, out string canonicalEncounterId))
            throw new ArgumentException("Encounter id must be an unsigned 16-bit decimal value.", nameof(encounterId));
        if (slot is < 0 or > 7)
            throw new ArgumentOutOfRangeException(nameof(slot));

        string canonicalRoot = Path.GetFullPath(editsRoot);
        string destination = Path.Combine(canonicalRoot, canonicalEncounterId + ".json");

        // Native Linux mutations go through the exact-descriptor owned-output layer; the Windows
        // entries below require Win32 handles. Same atomic read-merge-write contract on both.
        if (!OperatingSystem.IsWindows())
            return NoclipOverlayStoreLinux.WriteEditToRoot(
                canonicalRoot, canonicalEncounterId, slot, dx, dy, dz, heading, scale);

        object writeLock = EditWriteLocks.GetOrAdd(destination, static _ => new object());
        lock (writeLock)
        {
            using FileSystemReparseGuard.VerifiedDirectory directory =
                FileSystemReparseGuard.OpenOrCreateVerifiedDirectory(canonicalRoot);
            using FileStream processLock = AcquireCrossProcessWriteLock(
                directory, canonicalEncounterId + ".json.lock");
            string? existing = ReadExistingText(canonicalRoot, destination);
            string updated = SidecarEditsWriter.ApplySlot(
                existing,
                slot,
                dx,
                dy,
                dz,
                heading,
                scale);
            WriteTextAtomically(directory, canonicalEncounterId + ".json", updated);
        }
        return destination;
    }

    /// <summary>
    /// A sidecar read/merge/write transaction must be serialized across editor processes, not only
    /// tasks. FileShare.None is released automatically after a crash and does not require secrets or
    /// a machine-global service. The zero-byte lock file remains as harmless LocalAppData metadata.
    /// </summary>
    private static FileStream AcquireCrossProcessWriteLock(
        FileSystemReparseGuard.VerifiedDirectory directory,
        string lockFileName)
    {
        Stopwatch timeout = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return FileSystemReparseGuard.OpenOrCreateExclusiveFile(directory, lockFileName);
            }
            catch (IOException) when (timeout.Elapsed < TimeSpan.FromSeconds(10))
            {
                Thread.Sleep(25);
            }
        }
    }

    private static FileSystemReparseGuard.FileIdentity WriteBytesAtomically(
        string destination,
        byte[] content)
    {
        string directoryPath = Path.GetDirectoryName(destination)!;
        using FileSystemReparseGuard.VerifiedDirectory directory =
            FileSystemReparseGuard.OpenOrCreateVerifiedDirectory(directoryPath);
        return WriteAtomically(directory, Path.GetFileName(destination), stream =>
            stream.Write(content, 0, content.Length));
    }

    private static void WriteTextAtomically(
        FileSystemReparseGuard.VerifiedDirectory directory,
        string destinationFileName,
        string content)
    {
        _ = WriteAtomically(directory, destinationFileName, stream =>
        {
            using var writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                16 * 1024,
                leaveOpen: true);
            writer.Write(content);
            writer.Flush();
        });
    }

    private static FileSystemReparseGuard.FileIdentity WriteAtomically(
        FileSystemReparseGuard.VerifiedDirectory directory,
        string destinationFileName,
        Action<FileStream> write)
    {
        string temporaryFileName = destinationFileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using FileStream stream = FileSystemReparseGuard.CreateNewVerifiedFile(directory, temporaryFileName);
        bool promoted = false;
        try
        {
            write(stream);
            stream.Flush(flushToDisk: true);
            FileSystemReparseGuard.FileIdentity identity =
                FileSystemReparseGuard.PromoteOpenedFile(directory, stream, destinationFileName);
            promoted = true;
            return identity;
        }
        finally
        {
            if (!promoted)
            {
                try { FileSystemReparseGuard.DeleteOpenedFile(stream); } catch { }
            }
        }
    }

    private static string? ReadExistingText(string trustedRoot, string destination)
    {
        FileSystemReparseGuard.VerifiedOpenResult result =
            FileSystemReparseGuard.TryOpenVerifiedRead(trustedRoot, destination, out var file);
        if (result == FileSystemReparseGuard.VerifiedOpenResult.NotFound)
            return null;
        if (result != FileSystemReparseGuard.VerifiedOpenResult.Success || file == null)
            throw new IOException("The existing NoClip edit overlay could not be proven by handle identity.");
        using (file)
        using (var reader = new StreamReader(file.Stream, Encoding.UTF8, true, 16 * 1024, leaveOpen: true))
            return reader.ReadToEnd();
    }

    internal static bool TryNormalizeEncounterId(string? value, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrEmpty(value) || value.Length > 10)
            return false;
        foreach (char character in value)
            if (character is < '0' or > '9')
                return false;

        if (!ushort.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ushort parsed))
            return false;
        canonical = parsed.ToString(CultureInfo.InvariantCulture);
        return true;
    }
}

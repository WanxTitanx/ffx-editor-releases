using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

// ── Native Linux sidecar mutation path ──
// WHY: the Windows mutation entries on FileSystemReparseGuard require Win32 handles (reparse
// detection, exclusive open). On Linux the sidecar write must go through the exact-descriptor
// owned-output layer (B2a/B2b), keeping the same atomic read-merge-write contract: per-file
// in-process lock, cross-process write lock, snapshot, apply, verify, replace retaining displaced.
// MAINT: mirrors NoclipOverlayStore.WriteEditToRoot semantics 1:1; both must evolve together.
internal static class NoclipOverlayStoreLinux
{
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
        string canonicalRoot = Path.GetFullPath(editsRoot);
        string destination = Path.Combine(canonicalRoot, encounterId + ".json");

        object writeLock = NoclipOverlayStore.EditWriteLocks.GetOrAdd(destination, static _ => new object());
        lock (writeLock)
        {
            using LinuxOwnedOutputDirectory output = LinuxOwnedOutputDirectory.OpenOrCreate(canonicalRoot);
            using IDisposable processLock = output.AcquireWriteLock(TimeSpan.FromSeconds(10));

            string updated = SidecarEditsWriter.ApplySlot(
                ReadExisting(output, encounterId),
                slot,
                dx,
                dy,
                dz,
                heading,
                scale);
            byte[] updatedBytes = Encoding.UTF8.GetBytes(updated);
            WriteAtomically(output, encounterId + ".json", updatedBytes);
        }
        return destination;
    }

    internal static string? ReadExisting(LinuxOwnedOutputDirectory output, string encounterId)
    {
        LinuxOwnedOutputDirectory.Snapshot? existing = output.ReadSnapshot(encounterId + ".json");
        return existing == null ? null : Encoding.UTF8.GetString(existing.Bytes);
    }

    private static void WriteAtomically(LinuxOwnedOutputDirectory output, string leaf, byte[] content)
    {
        LinuxOwnedOutputDirectory.Snapshot? existing = output.ReadSnapshot(leaf);
        if (existing == null)
        {
            var receipt = output.PublishNew(leaf, content);
            Verify(output, leaf, receipt.Identity, content);
            return;
        }
        string retention = leaf + ".displaced-" + Guid.NewGuid().ToString("N");
        output.ReplaceRetainingDisplaced(leaf, existing, retention, content);
        Verify(output, leaf, output.ReadSnapshot(leaf)!.Identity, content);
    }

    private static void Verify(LinuxOwnedOutputDirectory output, string leaf,
        FileSystemReparseGuard.FileIdentity expected, byte[] expectedBytes)
    {
        LinuxOwnedOutputDirectory.Snapshot? actual = output.ReadSnapshot(leaf);
        if (actual == null || actual.Identity != expected ||
            !actual.Bytes.SequenceEqual(expectedBytes))
            throw new IOException("The sidecar write failed its exact-byte verification.");
    }
}

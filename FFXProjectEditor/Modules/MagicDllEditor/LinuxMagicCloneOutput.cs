using FFXProjectEditor.Diagnostics;
using FFXProjectEditor.Modules.Common.ViewerHub;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace FFXProjectEditor.Modules.MagicDllEditor;

// ── Native Linux create-only Magic clone ──
// The requested clone is durable user output, not a hidden backup or a recovery journal.
// MAINT: the directory lock is cooperative. Finite name/identity observations are not inode
// CAS or a same-UID sandbox; after publication any verification failure preserves the file.
internal static class LinuxMagicCloneOutput
{
    // Null in product; tests prove post-publication identity checks without racing a scheduler.
    internal static Action<string>? BeforeReadbackForTests { get; set; }
    internal static void Publish(string directoryPath, string destinationName, int magicId, ReadOnlySpan<byte> bytes)
    {
        if (magicId is < 0 or > 9999) throw new ArgumentOutOfRangeException(nameof(magicId));
        string canonical = "magic_" + magicId.ToString("D4", CultureInfo.InvariantCulture) + ".dll";
        string legacy = "magic_" + magicId.ToString(CultureInfo.InvariantCulture) + ".dll";
        if (!string.Equals(destinationName, canonical, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A clone requires its canonical four-digit Magic filename.", nameof(destinationName));
        if (bytes.Length > LinuxOwnedOutputDirectory.MaximumBytes)
            throw new IOException("Linux clone exceeds the 64 MiB output limit.");
        byte[] captured = bytes.ToArray();

        using var output = LinuxOwnedOutputDirectory.OpenOrCreate(directoryPath);
        using var lease = output.AcquireWriteLock(TimeSpan.FromSeconds(10));
        var names = output.ListChildNames();
        RejectCollision(names, canonical, legacy, permittedLeaf: null);
        // Reserve the requested visible leaf before linking, so a full inventory cannot
        // knowingly turn an otherwise successful publication into an uncertain result.
        if (names.Count >= LinuxDirectoryInventory.MaximumEntries)
            throw new IOException("Linux clone directory exceeds its bounded child limit.");

        var receipt = output.PublishNew(destinationName, captured);
        try
        {
            RejectCollision(output.ListChildNames(), canonical, legacy, destinationName);
            BeforeReadbackForTests?.Invoke(receipt.FullPath);
            var actual = output.ReadSnapshot(destinationName);
            if (actual == null || actual.Identity != receipt.Identity ||
                !actual.Bytes.SequenceEqual(captured))
                throw new IOException("The create-only clone failed its exact-byte verification.");
            DebugLog.Info("Magic.Writer", $"Native create-only clone verified ({captured.Length} bytes).");
        }
        catch (Exception error)
        {
            DebugLog.Error("Magic.Writer", "Clone publication is uncertain; output was preserved.", error);
            throw new LinuxPublicationUncertainException(receipt.FullPath, error);
        }
    }

    private static void RejectCollision(IReadOnlyList<string> names, string canonical,
        string legacy, string? permittedLeaf)
    {
        foreach (string name in names)
        {
            // Linux names retain exact casing. Only logical Magic collision compatibility
            // uses case-insensitive canonical and historical UNPADDED spelling, as Windows does.
            if (!string.Equals(name, permittedLeaf, StringComparison.Ordinal) &&
                (string.Equals(name, canonical, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(name, legacy, StringComparison.OrdinalIgnoreCase)))
                throw new IOException($"Magic id already exists at {name}.");
        }
    }
}

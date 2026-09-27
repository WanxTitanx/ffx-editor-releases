// WHY: Inventory fingerprints must bind the same finite metadata across ordering and UI cultures.
// MAINT: v1 uses length-prefixed strict UTF8 and fixed 64-bit little-endian scalars, streamed into SHA256.
// This digest is NOT the final ACK token; root/head/presence/domain-dependent snapshots are separate.
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using R = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryRecords;
using O = FFXProjectEditor.Modules.Common.ViewerHub.LinuxOutputFileSystem.OutputObservation;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

internal static partial class LinuxRecoveryInspection
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private const int MaximumInventoryRows = 2 * LinuxDirectoryInventory.MaximumEntries;

    internal static InventoryDigest Fingerprint(IReadOnlyList<Entry> entries)
    {
        if (entries is null || entries.Count > MaximumInventoryRows)
            throw new InvalidDataException("Recovery inventory exceeds its metadata entry bound.");
        long bytes = 0;
        var keys = new HashSet<(EntrySpace Space, string Leaf)>();
        for (int index = 0; index < entries.Count; index++)
        {
            Entry entry = entries[index] ?? throw new InvalidDataException("Missing inventory row.");
            ValidateEntry(entry);
            if (!keys.Add((entry.Space, entry.Leaf)))
                throw new InvalidDataException("Repeated recovery inventory name.");
            try { bytes = checked(bytes + entry.Observation.File.Length); }
            catch (OverflowException error)
            { throw new InvalidDataException("Recovery history accounting overflowed.", error); }
        }

        // Allocation/sorting starts only after every row and count has a finite validated bound.
        Entry[] ordered = entries.OrderBy(row => row.Space)
            .ThenBy(row => row.Leaf, StringComparer.Ordinal).ToArray();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendText(hash, "SPIRA-REFORGE/LinuxRecovery/Inventory/v1");
        AppendUnsigned(hash, (ulong)ordered.Length);
        foreach (Entry entry in ordered)
        {
            O value = entry.Observation;
            AppendUnsigned(hash, (ulong)entry.Space);
            AppendText(hash, entry.Leaf);
            AppendText(hash, "Linux");
            AppendUnsigned(hash, ((ulong)value.File.Identity.DeviceMajor << 32) |
                value.File.Identity.DeviceMinor);
            AppendUnsigned(hash, value.File.Identity.Inode);
            AppendUnsigned(hash, value.File.Identity.MountId);
            AppendSigned(hash, value.File.Length);
            AppendUnsigned(hash, value.Mode);
            AppendUnsigned(hash, value.OwnerId);
            AppendUnsigned(hash, value.LinkCount);
            AppendSigned(hash, value.File.ModifiedSeconds);
            AppendUnsigned(hash, value.File.ModifiedNanoseconds);
            AppendSigned(hash, value.File.ChangedSeconds);
            AppendUnsigned(hash, value.File.ChangedNanoseconds);
        }
        return new(Convert.ToHexString(hash.GetHashAndReset()), bytes, ordered.Length);
    }

    private static void ValidateEntry(Entry entry)
    {
        string leaf = entry.Leaf;
        if (entry.Space is not (EntrySpace.Root or EntrySpace.Journal) ||
            leaf is null || leaf.Length is < 1 or > 255)
            throw new InvalidDataException("Invalid recovery inventory namespace or leaf length.");
        try
        {
            if (StrictUtf8.GetByteCount(leaf) > 255)
                throw new InvalidDataException("Recovery leaf exceeds its UTF8 byte bound.");
            LinuxOutputFileSystem.ValidateLeaf(leaf);
        }
        catch (ArgumentException error)
        { throw new InvalidDataException("Invalid recovery inventory leaf.", error); }

        O value = entry.Observation;
        if (value.File.Length < 0 || value.File.Identity.Inode == 0 || value.File.Identity.MountId == 0 ||
            value.File.ModifiedNanoseconds >= 1_000_000_000 || value.File.ChangedNanoseconds >= 1_000_000_000 ||
            (value.Mode & 0xF000) != 0x8000 || (value.Mode & 0xE12) != 0 ||
            (value.Mode & 0x100) == 0 || value.LinkCount != 1)
            throw new InvalidDataException("Invalid regular-file inventory observation.");
        // Owner is captured, not authenticated by this pure function; native acquisition checked it.
    }

    private static void AppendText(IncrementalHash hash, string text)
    {
        Span<byte> encoded = stackalloc byte[256];
        int count = StrictUtf8.GetBytes(text.AsSpan(), encoded);
        AppendUnsigned(hash, (ulong)count);
        hash.AppendData(encoded[..count]);
    }

    private static void AppendUnsigned(IncrementalHash hash, ulong value)
    {
        Span<byte> encoded = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(encoded, value);
        hash.AppendData(encoded);
    }

    private static void AppendSigned(IncrementalHash hash, long value)
    {
        Span<byte> encoded = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(encoded, value);
        hash.AppendData(encoded);
    }

    private static R.Identity ToIdentity(O value) => new("Linux",
        ((ulong)value.File.Identity.DeviceMajor << 32) | value.File.Identity.DeviceMinor,
        value.File.Identity.Inode, value.File.Identity.MountId);

    private static R.Artifact ToArtifact(O value, string sha256) => new(
        ToIdentity(value), value.File.Length, sha256,
        new(value.Mode, value.OwnerId, value.LinkCount, value.File.ModifiedSeconds,
            value.File.ModifiedNanoseconds, value.File.ChangedSeconds, value.File.ChangedNanoseconds));
}

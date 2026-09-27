// WHY: User confirmation must bind exact currently displayed observations, not JSON formatting or culture.
// MAINT: ACK/v1 uses length-prefixed strict UTF8, raw SHA256 and fixed little-endian 64-bit scalars.
// Captured snapshots are immutable and bounded. No public-file bytes are described by this token.
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using I = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryInspection;
using O = FFXProjectEditor.Modules.Common.ViewerHub.LinuxOutputFileSystem.OutputObservation;
using R = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryRecords;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

internal static partial class LinuxRecoveryBaseline
{
    private static readonly UTF8Encoding TokenUtf8 = new(false, true);

    internal static string ComputeToken(I.Snapshot value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        TokenText(hash, "SPIRA-REFORGE/LinuxRecovery/ACK/v1");
        TokenText(hash, value.RootPath, 16384);
        TokenObservation(hash, value.Root);
        TokenNumber(hash, value.Journal is null ? 0UL : 1UL);
        if (value.Journal is { } journal) TokenObservation(hash, journal);
        TokenHash(hash, value.Digest.Sha256);
        TokenNumber(hash, unchecked((ulong)value.Digest.Bytes));
        TokenNumber(hash, (ulong)value.Digest.Entries);
        TokenNames(hash, value.RootNames);
        TokenNames(hash, value.JournalNames);
        TokenNumber(hash, (ulong)value.HeadSelection.Issue);
        TokenNumber(hash, value.HeadSelection.Head is null ? 0UL : 1UL);
        if (value.HeadSelection.Head is { } head)
        {
            TokenText(hash, head.Leaf);
            TokenNumber(hash, head.Epoch.Ordinal);
            TokenText(hash, head.Epoch.Scope.ToString("N"));
            TokenIdentity(hash, head.Source.Identity);
            TokenNumber(hash, unchecked((ulong)head.Source.Length));
            TokenHash(hash, head.Source.Sha256); // Exact persisted header bytes, not re-encoded JSON.
            TokenStamp(hash, head.Source.Stamp);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void TokenObservation(IncrementalHash hash, O value)
    {
        var id = value.File.Identity;
        TokenIdentity(hash, new("Linux", ((ulong)id.DeviceMajor << 32) | id.DeviceMinor, id.Inode, id.MountId));
        TokenNumber(hash, unchecked((ulong)value.File.Length));
        TokenStamp(hash, new(value.Mode, value.OwnerId, value.LinkCount, value.File.ModifiedSeconds,
            value.File.ModifiedNanoseconds, value.File.ChangedSeconds, value.File.ChangedNanoseconds));
    }

    private static void TokenIdentity(IncrementalHash hash, R.Identity value)
    {
        TokenText(hash, value.Backend);
        TokenNumber(hash, value.Device);
        TokenNumber(hash, value.Inode);
        TokenNumber(hash, value.Mount);
    }

    private static void TokenStamp(IncrementalHash hash, R.Stamp value)
    {
        TokenNumber(hash, value.Mode);
        TokenNumber(hash, value.Owner);
        TokenNumber(hash, value.Links);
        TokenNumber(hash, unchecked((ulong)value.ModifiedSeconds));
        TokenNumber(hash, value.ModifiedNanos);
        TokenNumber(hash, unchecked((ulong)value.ChangedSeconds));
        TokenNumber(hash, value.ChangedNanos);
    }

    private static void TokenNames(IncrementalHash hash, IReadOnlyList<string> names)
    {
        if (names.Count > LinuxRecoveryBudget.MaximumDirectoryEntries)
            throw new InvalidDataException("Recovery token name count exceeds the inspection bound.");
        TokenNumber(hash, (ulong)names.Count);
        foreach (string name in names.OrderBy(name => name, StringComparer.Ordinal)) TokenText(hash, name);
    }

    private static void TokenText(IncrementalHash hash, string text, int maximumBytes = 255)
    {
        int count = TokenUtf8.GetByteCount(text);
        if (count > maximumBytes) throw new InvalidDataException("Recovery token text exceeds its byte bound.");
        TokenNumber(hash, (ulong)count);
        hash.AppendData(TokenUtf8.GetBytes(text));
    }

    private static void TokenHash(IncrementalHash hash, string sha)
    {
        if (sha.Length != 64) throw new InvalidDataException("Recovery token needs a fixed-width SHA256.");
        hash.AppendData(Convert.FromHexString(sha));
    }

    private static void TokenNumber(IncrementalHash hash, ulong value)
    {
        Span<byte> encoded = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(encoded, value);
        hash.AppendData(encoded);
    }
}

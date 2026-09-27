using FFXProjectEditor.Diagnostics;
using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Security.Cryptography;
using static FFXProjectEditor.Modules.Common.ViewerHub.LinuxReadFileSystem;
using static FFXProjectEditor.Modules.Common.ViewerHub.LinuxOutputFileSystem;

namespace FFXProjectEditor.Modules.Common.ViewerHub;

// ── Retained Linux name exchange ──
// The displaced object remains named for explicit recovery; an uncertain exchange must never
// trigger pathname cleanup or automatic rollback. MAINT: renameat2 is not expected-inode CAS.
internal sealed partial class LinuxOwnedOutputDirectory
{
    internal Replacement ReplaceRetainingDisplaced(string targetLeaf, Snapshot expectedSnapshot,
        string retentionLeaf, ReadOnlySpan<byte> replacementBytes)
    {
        ValidateLeaf(targetLeaf);
        ValidateLeaf(retentionLeaf);
        ArgumentNullException.ThrowIfNull(expectedSnapshot);
        if (string.Equals(targetLeaf, retentionLeaf, StringComparison.Ordinal))
            throw new ArgumentException("Linux replacement requires two distinct direct leaves.", nameof(retentionLeaf));
        if (replacementBytes.Length > MaximumBytes)
            throw new ArgumentOutOfRangeException(nameof(replacementBytes), "Linux output exceeds the 64 MiB domain limit.");
        byte[] captured = replacementBytes.ToArray();
        string targetPath = Path.Combine(FullPath, targetLeaf);
        string retentionPath = Path.Combine(FullPath, retentionLeaf);

        lock (_gate)
        {
            EnsureUsable();
            VerifyRoot();
            using var original = OpenRead(_directory, targetLeaf);
            VerifyReplacementLeaf(original, targetLeaf, expectedSnapshot.Observation, expectedSnapshot.Bytes);
            using var prepared = CreateAnonymous(_directory);
            var initial = LinuxOutputFileSystem.Observe(prepared, RegularFileType);
            RequireOwnedFile(initial, expectedLinks: 0, requirePrivateMode: true);
            bool linked = false, exchanged = false;
            try
            {
                RandomAccess.Write(prepared, captured, 0);
                Sync(prepared);
                VerifyPreparedFile(prepared, captured, initial.File.Identity, expectedLinks: 0);
                var beforeLink = LinuxOutputFileSystem.Observe(prepared, RegularFileType);
                BeforeOperationForTests?.Invoke("replace-before-link", targetPath);
                VerifyRoot();
                VerifyReplacementLeaf(original, targetLeaf, expectedSnapshot.Observation, expectedSnapshot.Bytes);
                VerifyPreparedFile(prepared, captured, initial.File.Identity, expectedLinks: 0);
                LinkNew(prepared, _directory, retentionLeaf); // Exact descriptor, create-only, no collision cleanup.
                linked = true;

                BeforeOperationForTests?.Invoke("replace-linked", retentionPath);
                VerifyRoot();
                VerifyReplacementLeaf(original, targetLeaf, expectedSnapshot.Observation, expectedSnapshot.Bytes);
                // linkat changes link count and ctime, not the prepared inode's mtime/bytes/owner/mode.
                var linkedObservation = VerifyReplacementLeaf(prepared, retentionLeaf,
                    beforeLink with { LinkCount = 1 }, captured, allowChangedTime: true);
                VerifyRoot();
                // Deliberately the last observation boundary: tests demonstrate that this syscall
                // can exchange an unexpected leaf and that postconditions must reject that result.
                BeforeOperationForTests?.Invoke("replace-before-exchange", targetPath);
                Exchange(_directory, targetLeaf, retentionLeaf);
                exchanged = true;
                BeforeOperationForTests?.Invoke("replace-exchanged", targetPath);

                // Native ext-family probe confirmed rename changes ctime on both inodes. Only
                // those ctime fields may differ at this boundary. Pin them again before fsync.
                var newTarget = VerifyReplacementLeaf(prepared, targetLeaf, linkedObservation, captured, allowChangedTime: true);
                var retained = VerifyReplacementLeaf(original, retentionLeaf,
                    expectedSnapshot.Observation, expectedSnapshot.Bytes, allowChangedTime: true);
                VerifyRoot();
                Sync(_directory);
                VerifyReplacementLeaf(prepared, targetLeaf, newTarget, captured);
                VerifyReplacementLeaf(original, retentionLeaf, retained, expectedSnapshot.Bytes);
                VerifyRoot();
                DebugLog.Info(LogCategory, $"Replacement verified; prior file retained ({captured.Length} new bytes).");
                return new Replacement(ReplacementReceipt(targetPath, newTarget, captured),
                    ReplacementReceipt(retentionPath, retained, expectedSnapshot.Bytes));
            }
            catch (Exception error) when (linked)
            {
                _faulted = true;
                DebugLog.Error(LogCategory, "Replacement is uncertain; both possible output names were preserved.", error);
                throw new LinuxReplacementUncertainException(targetPath, retentionPath, exchanged, error);
            }
        }
    }

    // ── Retained plus named observations ──
    // Never obtain the replacement descriptor by reopening a stage pathname. Named descriptors
    // below are independent checks only. Stable exact reads still cannot exclude every ABA write.
    private OutputObservation VerifyReplacementLeaf(SafeFileHandle retained, string leaf,
        OutputObservation expected, ReadOnlySpan<byte> bytes, bool allowChangedTime = false)
    {
        var observed = LinuxOutputFileSystem.Observe(retained, RegularFileType);
        RequireOwnedFile(observed, expectedLinks: 1, requirePrivateMode: false);
        var comparable = allowChangedTime
            ? observed with { File = observed.File with
                { ChangedSeconds = expected.File.ChangedSeconds, ChangedNanoseconds = expected.File.ChangedNanoseconds } }
            : observed;
        if (comparable != expected || !ReadObservedBytes(retained, observed).AsSpan().SequenceEqual(bytes))
            throw new IOException("Linux replacement differs from its expected inode, metadata or bytes.");
        using var named = OpenRead(_directory, leaf);
        if (LinuxOutputFileSystem.Observe(named, RegularFileType) != observed ||
            !ReadObservedBytes(named, observed).AsSpan().SequenceEqual(bytes))
            throw new IOException("Linux replacement leaf no longer names its verified retained file.");
        return observed;
    }

    private static Publication ReplacementReceipt(string path, OutputObservation observed, ReadOnlySpan<byte> bytes) =>
        new(path, TagIdentity(observed.File.Identity), Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length);

    internal readonly record struct Replacement(Publication Target, Publication Retained);
}

internal sealed class LinuxReplacementUncertainException : IOException
{
    internal LinuxReplacementUncertainException(string targetPath, string retentionPath,
        bool exchangeCompleted, Exception cause)
        : base("Linux replacement could not be verified after linking; inspect both retained names before retrying.", cause)
    {
        TargetPath = targetPath;
        RetentionPath = retentionPath;
        ExchangeCompleted = exchangeCompleted;
    }
    internal string TargetPath { get; }
    internal string RetentionPath { get; }
    internal bool ExchangeCompleted { get; }
}

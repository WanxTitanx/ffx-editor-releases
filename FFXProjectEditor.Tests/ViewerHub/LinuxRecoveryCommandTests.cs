// WHY: Only bounded, privately captured LIVE commands may cross into the Linux recovery executor.
// MAINT: These are pure command tests. They perform no native publication and execute no parsed record.
using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.Modules.Common.ViewerHub;
using Xunit;
using E = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryExecutor;
using F = FFXProjectEditor.Modules.Common.ViewerHub.FileSystemReparseGuard;
using R = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryRecords;

namespace FFXProjectEditor.Tests.ViewerHub;

[Collection(FileSystemReparseGuardHookCollection.Name)]
public sealed class LinuxRecoveryCommandTests : IDisposable
{
    private const int MaximumRootBytes = 16384;
    private const int MaximumCommandPathBytes = MaximumRootBytes + 1 + 255;
    private readonly string _root = "/tmp/linux-recovery-command-" + Guid.NewGuid().ToString("N");

    public void Dispose() => E.BeforePhaseForTests = null;

    [Fact]
    public void Save_CapturesBothArraysOnlyAfterBothBoundsAndDoesNotAliasCaller()
    {
        if (!Native()) return;
        byte[] original = { 1, 2, 3 };
        byte[] working = { 4, 5, 6 };
        bool hookObservedCallerValues = false;
        E.BeforePhaseForTests = (phase, _) =>
        {
            if (phase != "capture-save") return;
            hookObservedCallerValues = original.SequenceEqual(new byte[] { 1, 2, 3 }) &&
                working.SequenceEqual(new byte[] { 4, 5, 6 });
            original[0] = 7;
            working[0] = 8;
        };

        E.Command command = E.Command.Save(At("copy.bin"), null, null, original, working);
        original[1] = 9;
        working[1] = 10;

        Assert.True(hookObservedCallerValues);
        Assert.Equal(new byte[] { 7, 2, 3 }, command.Original.ToArray());
        Assert.Equal(new byte[] { 8, 5, 6 }, command.Working.ToArray());
        Assert.Equal(new byte[] { 7, 9, 3 }, original);
        Assert.Equal(new byte[] { 8, 10, 6 }, working);
    }

    [Theory]
    [InlineData("null-original")]
    [InlineData("null-working")]
    [InlineData("large-original")]
    [InlineData("large-working")]
    public void Save_NullOrOversizedPayloadRefusesBeforeCaptureOrEarlierBufferCopy(string variant)
    {
        if (!Native()) return;
        byte[] small = new byte[1024 * 1024];
        byte[] oversized = GC.AllocateUninitializedArray<byte>(R.MaximumPayloadBytes + 1);
        byte[]? original = variant == "null-original" ? null :
            variant == "large-original" ? oversized : small;
        byte[]? working = variant == "null-working" ? null :
            variant == "large-working" ? oversized : small;
        bool captured = false;
        E.BeforePhaseForTests = (phase, _) => captured |= phase == "capture-save";
        long before = GC.GetAllocatedBytesForCurrentThread();

        Exception error = Record.Exception(() =>
            E.Command.Save(At("copy.bin"), null, null, original!, working!))!;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.NotNull(error);
        Assert.IsAssignableFrom<ArgumentException>(error);
        Assert.False(captured);
        // In the large-working row, a 1 MiB copy of `small` would prove the first buffer was
        // cloned before the second bound. Caller arrays were allocated before this measurement.
        if (variant == "large-working") Assert.InRange(allocated, 0, 128 * 1024);
    }

    [Theory]
    [InlineData("dot")]
    [InlineData("dotdot")]
    [InlineData("repeated")]
    [InlineData("trailing")]
    [InlineData("backslash")]
    [InlineData("relative")]
    public void RawMagicPathSyntax_IsRejectedBeforeNormalization(string variant)
    {
        if (!Native()) return;
        string path = variant switch
        {
            "dot" => _root + "/./copy.bin",
            "dotdot" => _root + "/child/../copy.bin",
            "repeated" => _root + "//copy.bin",
            "trailing" => _root + "/copy.bin/",
            "backslash" => _root + "/copy\\name.bin",
            "relative" => "tmp/copy.bin",
            _ => throw new InvalidOperationException(variant),
        };

        Assert.ThrowsAny<ArgumentException>(() =>
            E.Command.Save(path, null, null, new byte[] { 1 }, new byte[] { 2 }));
    }

    [Theory]
    [InlineData("source-command-bound")]
    [InlineData("last-save-command-bound")]
    [InlineData("game-root-bound")]
    [InlineData("root-bounds")]
    [InlineData("leaf-256")]
    [InlineData("backup-leaf-256")]
    [InlineData("journal-root")]
    [InlineData("journal-leaf")]
    [InlineData("retained-leaf")]
    [InlineData("malformed-surrogate")]
    public void CommandPathAndReservedNameBounds_AreClosedBeforeFilesystemUse(string variant)
    {
        if (!Native()) return;
        string hash = new('A', 64);
        var linux = new F.FileIdentity(F.FileIdentityKind.Linux, 1, 2, 3);
        string tooLongCommand = "/" + new string('a', MaximumCommandPathBytes);
        string tooLongGameRoot = "/" + new string('g', MaximumRootBytes + 1);
        string utf16Root = "/" + new string('r', MaximumRootBytes + 1);
        string utf8Root = "/" + string.Concat(Enumerable.Repeat("😀", 5000));

        Action action = variant switch
        {
            "source-command-bound" => () => E.Command.Save(At("copy.bin"), tooLongCommand,
                null, new byte[] { 1 }, new byte[] { 2 }),
            "last-save-command-bound" => () => E.Command.RestoreLastSave(At("copy.bin"),
                tooLongCommand, null, null, hash, hash, linux),
            "game-root-bound" => () => E.Command.Save(At("copy.bin"), null, tooLongGameRoot,
                new byte[] { 1 }, new byte[] { 2 }),
            "root-bounds" => () =>
            {
                Assert.ThrowsAny<ArgumentException>(() => E.Command.EditSidecar(utf16Root, 1, 0,
                    null, null, null, null, null));
                _ = E.Command.EditSidecar(utf8Root, 1, 0,
                    null, null, null, null, null); // Escapes to the outer assertion.
            },
            "leaf-256" => () => E.Command.Save(_root + "/" + new string('x', 256), null, null,
                new byte[] { 1 }, new byte[] { 2 }),
            "backup-leaf-256" => () => E.Command.Save(_root + "/" + new string('x', 252), null,
                null, new byte[] { 1 }, new byte[] { 2 }),
            "journal-root" => () => E.Command.EditSidecar(_root + "/" + R.JournalLeaf + "/nested",
                1, 0, null, null, null, null, null),
            "journal-leaf" => () => E.Command.Save(At(R.JournalLeaf + "-public"), null, null,
                new byte[] { 1 }, new byte[] { 2 }),
            "retained-leaf" => () => E.Command.Save(At(R.RetentionPrefix + "public"), null, null,
                new byte[] { 1 }, new byte[] { 2 }),
            "malformed-surrogate" => () => E.Command.Save(_root + "/bad\uD800.bin", null, null,
                new byte[] { 1 }, new byte[] { 2 }),
            _ => throw new InvalidOperationException(variant),
        };

        Assert.ThrowsAny<ArgumentException>(action);
    }

    [Theory]
    [InlineData("save", false)]
    [InlineData("save", true)]
    [InlineData("restore", false)]
    [InlineData("restore", true)]
    public void GameRootUtf8Bound_IsEnforcedBeforeCaptureForSaveAndRestore(string operation, bool overLimit)
    {
        if (!Native()) return;
        // The surrogate pairs fit the UTF16 pre-bound in both rows. Only UTF8 distinguishes
        // exactly 16 KiB from 16 KiB + 1; no configured-root directory is created or opened.
        string gameRoot = "/" + string.Concat(Enumerable.Repeat("\U0001F600", 4095)) +
            (overLimit ? "\U0001F600" : "abc");
        Assert.True(gameRoot.Length < MaximumRootBytes);
        Assert.Equal(MaximumRootBytes + (overLimit ? 1 : 0),
            System.Text.Encoding.UTF8.GetByteCount(gameRoot));
        int captures = 0;
        E.BeforePhaseForTests = (phase, _) => { if (phase == "capture-save") captures++; };
        string target = At("copy.bin");
        string hash = new('A', 64);
        var linux = new F.FileIdentity(F.FileIdentityKind.Linux, 1, 2, 3);
        E.Command? command = null;

        Exception? error = Record.Exception(() =>
        {
            command = operation == "save"
                ? E.Command.Save(target, null, gameRoot, new byte[] { 1 }, new byte[] { 2 })
                : E.Command.RestoreLastSave(target, target, null, gameRoot, hash, hash, linux);
        });

        if (overLimit)
        {
            Assert.IsAssignableFrom<ArgumentException>(error);
            Assert.Null(command);
            Assert.Equal(0, captures);
        }
        else
        {
            Assert.Null(error);
            Assert.NotNull(command);
            Assert.Equal(gameRoot, command.GameRoot);
            Assert.Equal(operation == "save" ? 1 : 0, captures);
        }
    }

    [Fact]
    public void MagicLeaf_At251CharactersIsAcceptedBecauseBackupReachesExactly255()
    {
        if (!Native()) return;
        string leaf = new('m', 251);

        E.Command command = E.Command.Save(At(leaf), null, null,
            new byte[] { 1 }, new byte[] { 2 });

        Assert.Equal(leaf, command.TargetLeaf);
        Assert.Equal(_root, command.RootPath);
    }

    [Theory]
    [InlineData("source-target")]
    [InlineData("source-backup")]
    [InlineData("configured-game")]
    [InlineData("steam-library")]
    [InlineData("steamapps-common")]
    public void MagicTargetAndBackup_RejectEveryProtectedSourceOrGameShape(string variant)
    {
        if (!Native()) return;
        string target = At("copy.bin");
        string? source = variant switch
        {
            "source-target" => target,
            "source-backup" => target + ".bak",
            _ => "/tmp/source-" + Guid.NewGuid().ToString("N"),
        };
        string? gameRoot = variant == "configured-game" ? _root : null;
        if (variant == "steam-library") target = "/tmp/SteamLibrary/copy.bin";
        if (variant == "steamapps-common") target = "/tmp/steamapps/common/game/copy.bin";

        Assert.ThrowsAny<ArgumentException>(() => E.Command.Save(target, source, gameRoot,
            new byte[] { 1 }, new byte[] { 2 }));
    }

    [Theory]
    [InlineData("different-last-path")]
    [InlineData("invalid-last-path")]
    [InlineData("windows-identity")]
    [InlineData("short-target-hash")]
    [InlineData("nonhex-backup-hash")]
    [InlineData("null-target-hash")]
    public void Restore_RejectsNonExactPathHashOrLinuxIdentity(string variant)
    {
        if (!Native()) return;
        string target = At("copy.bin");
        string last = variant == "different-last-path" ? At("other.bin") : target;
        if (variant == "invalid-last-path") last = _root + "/./copy.bin";
        var identity = new F.FileIdentity(variant == "windows-identity"
            ? F.FileIdentityKind.Windows : F.FileIdentityKind.Linux, 1, 2, 3);
        string? targetHash = variant == "short-target-hash" ? "AA" :
            variant == "null-target-hash" ? null : new string('A', 64);
        string backupHash = variant == "nonhex-backup-hash" ? new string('Z', 64) : new string('B', 64);

        Exception error = Record.Exception(() => E.Command.RestoreLastSave(target, last, null,
            null, targetHash!, backupHash, identity))!;
        if (variant == "nonhex-backup-hash") Assert.IsType<FormatException>(error);
        else Assert.IsAssignableFrom<ArgumentException>(error);
    }

    [Fact]
    public void Restore_ValidCommandCapturesCanonicalProofWithoutAcceptingPayloadBytes()
    {
        if (!Native()) return;
        string target = At("copy.bin");
        var identity = new F.FileIdentity(F.FileIdentityKind.Linux, 11, 22, 33);

        E.Command command = E.Command.RestoreLastSave(target, target, "/tmp/source", null,
            new string('a', 64), new string('b', 64), identity);

        Assert.Equal(R.OperationKind.MagicRestore, command.Kind);
        Assert.Equal(target, Path.Combine(command.RootPath, command.TargetLeaf));
        Assert.Equal(new string('A', 64), command.Restore!.TargetSha256);
        Assert.Equal(new string('B', 64), command.Restore.BackupSha256);
        Assert.Equal(identity, command.Restore.BackupIdentity);
        Assert.Empty(command.Original.ToArray());
        Assert.Empty(command.Working.ToArray());
    }

    [Fact]
    public void Sidecar_ValidCommandCapturesEncounterLeafSlotAndFiniteDeltas()
    {
        if (!Native()) return;
        E.Command command = E.Command.EditSidecar(_root, ushort.MaxValue, 7,
            1.25, -2.5, 3.75, 0.5, 2);

        Assert.Equal(R.OperationKind.SidecarEdit, command.Kind);
        Assert.Equal("65535.json", command.TargetLeaf);
        Assert.Equal(new E.SidecarValues(7, 1.25, -2.5, 3.75, 0.5, 2), command.Sidecar);
    }

    [Theory]
    [InlineData("encounter-negative")]
    [InlineData("encounter-large")]
    [InlineData("slot-negative")]
    [InlineData("slot-large")]
    [InlineData("nan")]
    [InlineData("positive-infinity")]
    [InlineData("negative-infinity")]
    public void Sidecar_RejectsOutOfDomainIdentifiersAndEveryNonfiniteValue(string variant)
    {
        if (!Native()) return;
        int encounter = variant == "encounter-negative" ? -1 :
            variant == "encounter-large" ? ushort.MaxValue + 1 : 1;
        int slot = variant == "slot-negative" ? -1 : variant == "slot-large" ? 8 : 0;
        double? x = variant == "nan" ? double.NaN : null;
        double? y = variant == "positive-infinity" ? double.PositiveInfinity : null;
        double? z = variant == "negative-infinity" ? double.NegativeInfinity : null;

        Assert.ThrowsAny<ArgumentException>(() =>
            E.Command.EditSidecar(_root, encounter, slot, x, y, z, null, null));
    }

    [Fact]
    public void OversizedCommandPath_IsRejectedBeforeLexicalSplitWithoutHeavyThreadAllocation()
    {
        if (!Native()) return;
        // The oversized caller string is intentionally allocated before the oracle. The test
        // measures this thread only; it is not a global RSS, GC-heap peak, or zero-allocation claim.
        string oversized = "/" + new string('p', MaximumCommandPathBytes);
        byte[] one = { 1 };
        _ = Record.Exception(() => E.Command.Save(oversized, null, null, one, one)); // JIT/warm path.
        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int index = 0; index < 3; index++)
            Assert.ThrowsAny<ArgumentException>(() =>
                E.Command.Save(oversized, null, null, one, one));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(allocated, 0, 128 * 1024);
    }

    [Fact]
    public void ParsedIntent_IsAnObservationAndNotAClosedLiveCommand()
    {
        if (!Native()) return;
        Assert.False(typeof(E.Command).IsAssignableFrom(typeof(R.Intent)));
        Assert.False(typeof(R.Intent).IsAssignableFrom(typeof(E.Command)));
        E.Outcome InvokeClosedCommand(E.Command command) => E.Execute(command);
        Assert.NotNull((Func<E.Command, E.Outcome>)InvokeClosedCommand);
    }

    private string At(string leaf) => _root + "/" + leaf;
    private static bool Native()
    {
        if (!OperatingSystem.IsLinux()) return false;
        Assert.True(LinuxReadFileSystem.IsSupported);
        return true;
    }
}

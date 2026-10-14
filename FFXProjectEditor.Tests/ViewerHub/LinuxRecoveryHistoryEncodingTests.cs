// WHY: Canonical carry-over and snapshot value equality must not depend on runtime capability identity.
// MAINT: Synthetic observations below test finite encodings/comparisons, not native ownership or ACK authority.
using System;
using System.IO;
using FFXProjectEditor.Modules.Common.ViewerHub;
using Xunit;
using F = FFXProjectEditor.Tests.ViewerHub.LinuxRecoveryHistoryFixture;
using H = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryHistory;
using I = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryInspection;

namespace FFXProjectEditor.Tests.ViewerHub;

public sealed class LinuxRecoveryHistoryEncodingTests
{
    [Fact]
    public void CarryOver_HasSeparateDomainAndPreservesExactAccounting()
    {
        I.Entry row = LinuxRecoveryInspectionEncodingTests.Row();
        foreach (I.Entry[] rows in new[] { Array.Empty<I.Entry>(), new[] { row },
            new[] { row, row with { Space = I.EntrySpace.Journal, Leaf = "old-record" } } })
        {
            var actual = H.CarryOverFingerprint(rows);
            Assert.Equal(F.Carry(rows), actual); // Independent BinaryWriter composition, no History call.
            var inventory = I.Fingerprint(rows);
            Assert.NotEqual(inventory.Sha256, actual.Sha256);
            Assert.Equal(inventory.Bytes, actual.Bytes);
            Assert.Equal(inventory.Entries, actual.Entries);
        }
    }

    [Fact]
    public void CarryOver_BindsMetadataEvenWhenTotalsDoNotChange()
    {
        I.Entry row = LinuxRecoveryInspectionEncodingTests.Row();
        var changed = row with { Leaf = "changed-leaf" };
        var first = H.CarryOverFingerprint(new[] { row });
        var second = H.CarryOverFingerprint(new[] { changed });
        Assert.Equal(first.Bytes, second.Bytes);
        Assert.Equal(first.Entries, second.Entries);
        Assert.NotEqual(first.Sha256, second.Sha256);
        Assert.Throws<InvalidDataException>(() => H.CarryOverFingerprint(new[]
            { row with { Observation = row.Observation with
                { File = row.Observation.File with { Length = -1 } } } }));
    }

    [Theory]
    [InlineData("root-path")] [InlineData("root-stamp")] [InlineData("journal-presence")]
    [InlineData("inventory")] [InlineData("head")] [InlineData("root-names")]
    [InlineData("journal-names")]
    public void SameObservation_ComparesValuesAndBindsEachSnapshotComponent(string field)
    {
        var root = LinuxRecoveryInspectionEncodingTests.Row().Observation with { Mode = 0x41C0 };
        var empty = I.Fingerprint(Array.Empty<I.Entry>());
        var none = new I.Selection(null, I.HeadIssue.None);
        I.Snapshot Snapshot() => new("/private/root", root, null, Array.Empty<string>(),
            Array.Empty<string>(), Array.Empty<I.Entry>(), empty, none);
        I.Snapshot first = Snapshot();
        Assert.NotSame(first, Snapshot());
        Assert.True(H.SameObservation(first, Snapshot()));
        I.Snapshot changed = new(
            field == "root-path" ? "/private/other" : first.RootPath,
            field == "root-stamp" ? root with
                { File = root.File with { ChangedNanoseconds = root.File.ChangedNanoseconds + 1 } } : root,
            field == "journal-presence" ? root : null,
            field == "root-names" ? new[] { "public.bin" } : Array.Empty<string>(),
            field == "journal-names" ? new[] { "historical" } : Array.Empty<string>(),
            Array.Empty<I.Entry>(),
            field == "inventory" ? empty with { Sha256 = new string('A', 64) } : empty,
            field == "head" ? new(null, I.HeadIssue.InvalidHeader) : none);
        Assert.False(H.SameObservation(first, changed));
    }
}

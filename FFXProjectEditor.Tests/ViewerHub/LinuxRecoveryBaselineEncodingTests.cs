// WHY: ACK tokens need independent binary composition, and baseline admission must include journal setup.
// MAINT: Synthetic snapshots below prove finite encodings/arithmetic, never native ownership or a UI ACK.
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FFXProjectEditor.Modules.Common.ViewerHub;
using Xunit;
using B = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryBaseline;
using F = FFXProjectEditor.Tests.ViewerHub.LinuxRecoveryHistoryFixture;
using I = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryInspection;
using O = FFXProjectEditor.Modules.Common.ViewerHub.LinuxOutputFileSystem.OutputObservation;
using R = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryRecords;

namespace FFXProjectEditor.Tests.ViewerHub;

public sealed class LinuxRecoveryBaselineEncodingTests
{
    private static readonly Guid Fresh = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static I.Snapshot Snapshot(string change = "")
    {
        var row = LinuxRecoveryInspectionEncodingTests.Row() with { Leaf = ".spira-retained-old" };
        O root = row.Observation with { Mode = 0x41C0, LinkCount = 2 };
        O journalValue = root with { File = root.File with
            { Identity = root.File.Identity with { Inode = root.File.Identity.Inode + 1 } } };
        O? journal = journalValue;
        var carry = F.Carry(Array.Empty<I.Entry>());
        R.Identity rootId = new("Linux", ((ulong)root.File.Identity.DeviceMajor << 32) |
            root.File.Identity.DeviceMinor, root.File.Identity.Inode, root.File.Identity.MountId);
        var epoch = new R.Epoch(1, rootId, 1, F.ScopeA, null, carry.Sha256, carry.Bytes, carry.Entries,
            new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero));
        var source = new R.Artifact(rootId with { Inode = rootId.Inode + 2 }, 200, new string('A', 64),
            new(0x8180, row.Observation.OwnerId, 1, 10, 11, 12, 13));
        I.Head? head = new(R.EpochLeaf(1, F.ScopeA), epoch, source);
        string[] roots = { "z.bin", "ä.bin", R.JournalLeaf, row.Leaf };
        string[] journals = { head.Leaf, "z.bin", "ä.bin" };
        I.InventoryDigest digest = I.Fingerprint(new[] { row });
        switch (change)
        {
            case "root-identity": root = root with { File = root.File with
                { Identity = root.File.Identity with { MountId = root.File.Identity.MountId + 1 } } }; break;
            case "root-stamp": root = root with { File = root.File with
                { ChangedNanoseconds = root.File.ChangedNanoseconds + 1 } }; break;
            case "journal-presence": journal = null; break;
            case "journal-identity": journal = journalValue with { File = journalValue.File with
                { Identity = journalValue.File.Identity with { Inode = journalValue.File.Identity.Inode + 1 } } }; break;
            case "journal-stamp": journal = journalValue with { OwnerId = journalValue.OwnerId + 1 }; break;
            case "inventory-hash": digest = digest with { Sha256 = new string('B', 64) }; break;
            case "inventory-bytes": digest = digest with { Bytes = digest.Bytes + 1 }; break;
            case "inventory-count": digest = digest with { Entries = digest.Entries + 1 }; break;
            case "root-names": roots = roots.Append("other").ToArray(); break;
            case "journal-names": journals = journals.Append("other").ToArray(); break;
            case "head-absence": head = null; break;
            case "head-ordinal": head = head with { Epoch = epoch with { Ordinal = 2 } }; break;
            case "head-scope": head = head with { Epoch = epoch with { Scope = F.ScopeB } }; break;
            case "head-leaf": head = head with { Leaf = R.EpochLeaf(2, F.ScopeA) }; break;
            case "head-identity": head = head with { Source = source with
                { Identity = source.Identity with { Mount = source.Identity.Mount + 1 } } }; break;
            case "head-sha": head = head with { Source = source with { Sha256 = new string('B', 64) } }; break;
            case "head-length": head = head with { Source = source with { Length = source.Length + 1 } }; break;
            case "head-stamp": head = head with { Source = source with
                { Stamp = source.Stamp with { ChangedNanos = source.Stamp.ChangedNanos + 1 } } }; break;
            case "reverse": Array.Reverse(roots); Array.Reverse(journals); break;
        }
        return new(change == "root-path" ? "/private/other" : "/private/output", root, journal, roots, journals,
            new[] { row }, digest, new(head, change == "head-issue" ? I.HeadIssue.InvalidHeader : I.HeadIssue.None));
    }

    [Fact]
    public void Token_UsesIndependentBinaryEncodingAndSeparateDomain()
    {
        foreach (I.Snapshot observation in new[] { Snapshot(), Snapshot("head-absence"),
            Snapshot("journal-presence"), Snapshot("reverse") })
        {
            string token = B.ComputeToken(observation);
            Assert.Equal(IndependentToken(observation), token);
            Assert.NotEqual(observation.Digest.Sha256, token);
            Assert.NotEqual(F.Carry(observation.Entries.ToArray()).Sha256, token);
            Assert.Equal(token, new B.Proposal(observation).Token);
        }
    }

    [Theory]
    [InlineData("root-path")] [InlineData("root-identity")] [InlineData("root-stamp")]
    [InlineData("journal-presence")] [InlineData("journal-identity")] [InlineData("journal-stamp")]
    [InlineData("inventory-hash")] [InlineData("inventory-bytes")] [InlineData("inventory-count")]
    [InlineData("root-names")] [InlineData("journal-names")] [InlineData("head-absence")]
    [InlineData("head-ordinal")] [InlineData("head-scope")] [InlineData("head-leaf")]
    [InlineData("head-identity")] [InlineData("head-sha")] [InlineData("head-length")]
    [InlineData("head-stamp")] [InlineData("head-issue")]
    public void Token_BindsEveryDisplayedSnapshotComponent(string field) =>
        Assert.NotEqual(B.ComputeToken(Snapshot()), B.ComputeToken(Snapshot(field)));

    [Theory]
    [InlineData("tr-TR")] [InlineData("de-DE")]
    public void Token_NameOrderingIsOrdinalAcrossCultureAndInputPermutations(string culture)
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new(culture);
            Assert.Equal(IndependentToken(Snapshot()), B.ComputeToken(Snapshot("reverse")));
            Assert.Equal(B.ComputeToken(Snapshot()), B.ComputeToken(Snapshot("reverse")));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = before; }
    }

    [Theory]
    [InlineData("root-text")] [InlineData("leaf-utf8")] [InlineData("name-count")]
    public void Token_RefusesUnboundedSyntheticTextOrNameCollections(string kind)
    {
        I.Snapshot original = Snapshot();
        I.Snapshot changed = new(kind == "root-text" ? "/" + new string('a', 16384) : original.RootPath,
            original.Root, original.Journal,
            kind == "leaf-utf8" ? new[] { new string('é', 128) } :
            kind == "name-count" ? Enumerable.Range(0, 8193).Select(n => "leaf-" + n).ToArray() : original.RootNames,
            original.JournalNames, original.Entries, original.Digest, original.HeadSelection);
        Assert.Throws<InvalidDataException>(() => B.ComputeToken(changed));
    }

    [Theory]
    [InlineData(false, 8191, true)] [InlineData(false, 8192, false)]
    [InlineData(true, 8192, true)]
    public void Admission_ReservesMissingJournalRootSlot(bool present, int rootEntries, bool allowed)
    {
        I.Snapshot original = Snapshot();
        I.Snapshot observation = new(original.RootPath, original.Root, present ? original.Journal : null,
            Enumerable.Range(0, rootEntries).Select(n => "public-" + n),
            Array.Empty<string>(), Array.Empty<I.Entry>(), I.Fingerprint(Array.Empty<I.Entry>()),
            new(null, I.HeadIssue.None));
        if (!allowed) { Assert.Throws<InvalidDataException>(() => B.Admission(observation)); return; }
        var reservation = B.Admission(observation);
        Assert.Equal(present ? rootEntries : rootEntries + 1, reservation.PeakRootEntries);
        Assert.Equal(1, reservation.PeakManagedEntries);
        Assert.Equal(1, reservation.PeakJournalEntries);
        Assert.Equal(R.MaximumRecordBytes, reservation.PeakBytes);
    }

    [Theory]
    [InlineData("empty")] [InlineData("current-head")] [InlineData("older-head")]
    [InlineData("intent-prefix")] [InlineData("uppercase-prefix")] [InlineData("retained-prefix")]
    public void NewScope_CannotReclassifyAnyObservedHistoricalNamespace(string collision)
    {
        I.Snapshot original = Snapshot();
        Guid candidate = collision == "empty" ? Guid.Empty : Fresh;
        string id = Fresh.ToString("N");
        string leaf = collision switch
        {
            "current-head" => R.EpochLeaf(1, Fresh),
            "older-head" => R.EpochLeaf(1, Fresh),
            "intent-prefix" => id + "-unknown",
            "uppercase-prefix" => id.ToUpperInvariant() + "-unknown",
            "retained-prefix" => R.RetentionPrefix + id + "-unknown.bin",
            _ => "unrelated"
        };
        var entry = LinuxRecoveryInspectionEncodingTests.Row() with
        { Space = collision == "retained-prefix" ? I.EntrySpace.Root : I.EntrySpace.Journal, Leaf = leaf };
        var selected = original.HeadSelection;
        if (collision == "current-head")
            selected = new(original.HeadSelection.Head! with
            { Leaf = leaf, Epoch = original.HeadSelection.Head!.Epoch with { Scope = Fresh } }, I.HeadIssue.None);
        if (collision == "older-head")
            selected = new(original.HeadSelection.Head! with
            {
                Leaf = R.EpochLeaf(2, F.ScopeA),
                Epoch = original.HeadSelection.Head!.Epoch with
                { Ordinal = 2, Previous = new(1, Fresh, original.HeadSelection.Head!.Source.Identity,
                    original.HeadSelection.Head!.Source.Sha256) }
            }, I.HeadIssue.None);
        I.Snapshot changed = new(original.RootPath, original.Root, original.Journal, original.RootNames,
            original.JournalNames, new[] { entry }, I.Fingerprint(new[] { entry }), selected);
        Assert.Throws<InvalidDataException>(() => B.ComposeEpoch(changed, candidate, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void NewEpoch_UsesCurrentRootAndHeaderSourceNotOldEmbeddedBinding()
    {
        I.Snapshot original = Snapshot();
        I.Head old = original.HeadSelection.Head!;
        var oldBinding = old.Epoch.Root with { Mount = old.Epoch.Root.Mount + 100 };
        I.Snapshot changed = new(original.RootPath, original.Root, original.Journal, original.RootNames,
            original.JournalNames, original.Entries, original.Digest,
            new(old with { Epoch = old.Epoch with { Root = oldBinding } }, I.HeadIssue.None));
        R.Epoch epoch = B.ComposeEpoch(changed, Fresh, DateTimeOffset.UtcNow);
        Assert.NotEqual(oldBinding, epoch.Root);
        Assert.Equal(old.Epoch.Root, epoch.Root);
        Assert.Equal(old.Source.Identity, epoch.Previous!.Identity);
        Assert.Equal(old.Source.Sha256, epoch.Previous.Sha256);
        Assert.Equal(F.Carry(changed.Entries.ToArray()).Sha256, epoch.CarryOverSha256);
        Assert.Equal(changed.Digest.Bytes, epoch.CarryOverBytes);
        Assert.Equal(changed.Digest.Entries, epoch.CarryOverEntries);
    }

    // BinaryWriter is deliberately independent of production incremental-hash/scalar helpers.
    // The signed fields use Write(Int64); their specified two's-complement wire bytes are identical.
    private static string IndependentToken(I.Snapshot value)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), leaveOpen: true);
        void Text(string text)
        {
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(text);
            writer.Write((ulong)bytes.Length); writer.Write(bytes);
        }
        void Identity(R.Identity identity)
        {
            Text(identity.Backend); writer.Write(identity.Device); writer.Write(identity.Inode); writer.Write(identity.Mount);
        }
        void Stamp(R.Stamp stamp)
        {
            writer.Write((ulong)stamp.Mode); writer.Write((ulong)stamp.Owner); writer.Write((ulong)stamp.Links);
            writer.Write(stamp.ModifiedSeconds); writer.Write((ulong)stamp.ModifiedNanos);
            writer.Write(stamp.ChangedSeconds); writer.Write((ulong)stamp.ChangedNanos);
        }
        void Observation(O observation)
        {
            var id = observation.File.Identity;
            Identity(new("Linux", ((ulong)id.DeviceMajor << 32) | id.DeviceMinor, id.Inode, id.MountId));
            writer.Write(observation.File.Length);
            Stamp(new(observation.Mode, observation.OwnerId, observation.LinkCount,
                observation.File.ModifiedSeconds, observation.File.ModifiedNanoseconds,
                observation.File.ChangedSeconds, observation.File.ChangedNanoseconds));
        }
        void Names(System.Collections.Generic.IReadOnlyList<string> names)
        {
            writer.Write((ulong)names.Count);
            foreach (string name in names.OrderBy(n => n, StringComparer.Ordinal)) Text(name);
        }
        Text("SPIRA-REFORGE/LinuxRecovery/ACK/v1"); Text(value.RootPath); Observation(value.Root);
        writer.Write(value.Journal is null ? 0UL : 1UL);
        if (value.Journal is { } journal) Observation(journal);
        writer.Write(Convert.FromHexString(value.Digest.Sha256)); writer.Write(value.Digest.Bytes);
        writer.Write((ulong)value.Digest.Entries); Names(value.RootNames); Names(value.JournalNames);
        writer.Write((ulong)value.HeadSelection.Issue);
        writer.Write(value.HeadSelection.Head is null ? 0UL : 1UL);
        if (value.HeadSelection.Head is { } head)
        {
            Text(head.Leaf); writer.Write(head.Epoch.Ordinal); Text(head.Epoch.Scope.ToString("N"));
            Identity(head.Source.Identity); writer.Write(head.Source.Length);
            writer.Write(Convert.FromHexString(head.Source.Sha256)); Stamp(head.Source.Stamp);
        }
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }
}

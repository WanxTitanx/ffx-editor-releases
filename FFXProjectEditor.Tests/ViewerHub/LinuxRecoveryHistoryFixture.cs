// WHY: Native history tests need real inode/record receipts, independent of the history reader.
// MAINT: This fixture constructs histories; it is NOT the product acknowledgement/operation API.
// Every writable path is under a newly created private test root, never the game/corpus/user exports.
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FFXProjectEditor.Modules.Common.ViewerHub;
using FFXProjectEditor.Tests.Infrastructure;
using D = FFXProjectEditor.Modules.Common.ViewerHub.LinuxOwnedOutputDirectory;
using I = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryInspection;
using R = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryRecords;

namespace FFXProjectEditor.Tests.ViewerHub;

internal sealed class LinuxRecoveryHistoryFixture : IDisposable
{
    internal static readonly Guid ScopeA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    internal static readonly Guid ScopeB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    internal string RootPath { get; } = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work",
        "linux-recovery-history-" + Guid.NewGuid().ToString("N"));
    internal LinuxRecoveryHistoryFixture() => TestDirectory.CreatePrivate(RootPath);
    internal string At(string leaf) => Path.Combine(RootPath, leaf);
    internal string RecordPath(string leaf) => Path.Combine(At(R.JournalLeaf), leaf);
    internal D Open() => D.OpenExisting(RootPath);
    internal static string Sha(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    // Independent composition of the documented carry-over domain from the existing inventory
    // primitive, so an unimplemented/incorrect History.CarryOverFingerprint cannot prepare its own RED fixture.
    internal static I.InventoryDigest Carry(I.Entry[] entries)
    {
        I.InventoryDigest inventory = I.Fingerprint(entries);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            byte[] domain = Encoding.ASCII.GetBytes("SPIRA-REFORGE/LinuxRecovery/CarryOver/v1");
            writer.Write((ulong)domain.Length);
            writer.Write(domain);
            writer.Write(Convert.FromHexString(inventory.Sha256));
            writer.Write(inventory.Bytes);
            writer.Write((ulong)inventory.Entries);
        }
        return inventory with { Sha256 = Sha(stream.ToArray()) };
    }

    internal static R.Artifact Artifact(D.Snapshot value)
    {
        var o = value.Observation;
        var id = o.File.Identity;
        return new(new("Linux", ((ulong)id.DeviceMajor << 32) | id.DeviceMinor, id.Inode, id.MountId),
            o.File.Length, Sha(value.Bytes), new(o.Mode, o.OwnerId, o.LinkCount,
                o.File.ModifiedSeconds, o.File.ModifiedNanoseconds,
                o.File.ChangedSeconds, o.File.ChangedNanoseconds));
    }

    internal R.Epoch Baseline(Guid scope, bool changedBinding = false)
    {
        using var root = Open();
        using var lease = root.AcquireWriteLock(TimeSpan.FromSeconds(5));
        using var journal = root.OpenOrCreateChild(R.JournalLeaf);
        I.Snapshot before = I.Capture(root);
        var id = before.Root.File.Identity;
        var binding = new R.Identity("Linux", ((ulong)id.DeviceMajor << 32) | id.DeviceMinor,
            id.Inode, changedBinding ? id.MountId + 1 : id.MountId);
        I.InventoryDigest carry = Carry(before.Entries.ToArray());
        I.Head? old = before.HeadSelection.Head;
        var previous = old is null ? null : new R.PreviousHead(old.Epoch.Ordinal, old.Epoch.Scope,
            old.Source.Identity, old.Source.Sha256);
        var epoch = new R.Epoch(1, binding, old is null ? 1 : checked(old.Epoch.Ordinal + 1),
            scope, previous, carry.Sha256, carry.Bytes, carry.Entries,
            new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero));
        journal.PublishNew(R.EpochLeaf(epoch.Ordinal, scope), R.Encode(epoch));
        return epoch;
    }

    internal sealed record Operation(R.Intent Intent, R.Completion? Completion)
    {
        internal string IntentLeaf => R.IntentLeaf(Intent.Scope, Intent.Sequence, Intent.Operation);
        internal string CompletionLeaf => R.CompletionLeaf(Intent.Scope, Intent.Sequence, Intent.Operation);
    }

    internal Operation AddOperation(ulong sequence = 1, Guid? operation = null,
        R.OperationKind kind = R.OperationKind.SidecarEdit, bool replace = false,
        int executeSteps = 2, bool complete = true)
    {
        using var root = Open();
        using var lease = root.AcquireWriteLock(TimeSpan.FromSeconds(5));
        using var journal = root.OpenExistingChild(R.JournalLeaf);
        R.Epoch epoch = I.Capture(root).HeadSelection.Head!.Epoch;
        Guid op = operation ?? Guid.NewGuid();
        string target = "output-" + sequence + ".bin";
        string[] names = kind == R.OperationKind.MagicSave
            ? new[] { target + ".bak", target } : new[] { target };
        var payloads = names.Select((_, index) => new byte[] { (byte)(0xA1 + index) }).ToArray();
        var old = new D.Snapshot?[names.Length];
        var steps = new R.Step[names.Length];
        for (int index = 0; index < names.Length; index++)
        {
            if (replace || kind == R.OperationKind.MagicRestore)
            {
                root.PublishNew(names[index], new byte[] { (byte)(0x31 + index) });
                if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
                File.SetUnixFileMode(At(names[index]), UnixFileMode.UserRead | UnixFileMode.UserWrite |
                    UnixFileMode.GroupRead | UnixFileMode.OtherRead); // Only deliberate0644 fixture.
                old[index] = root.ReadSnapshot(names[index], R.MaximumPayloadBytes);
            }
            steps[index] = new(names[index], old[index] is { } snapshot ? Artifact(snapshot) : null,
                payloads[index].Length, Sha(payloads[index]),
                old[index] is null ? null : R.RetainedLeaf(epoch.Scope, op, index));
        }
        var intent = new R.Intent(1, epoch.Scope, sequence, op, kind, steps[0],
            steps.Length == 2 ? steps[1] : null);
        byte[] exactIntent = R.Encode(intent);
        journal.PublishNew(R.IntentLeaf(epoch.Scope, sequence, op), exactIntent);
        var receipts = new R.StepReceipt[names.Length];
        int applied = Math.Min(executeSteps, steps.Length);
        for (int index = 0; index < applied; index++)
        {
            R.Step step = steps[index];
            if (old[index] is { } expected)
                root.ReplaceRetainingDisplaced(step.TargetLeaf, expected, step.RetentionLeaf!, payloads[index]);
            else root.PublishNew(step.TargetLeaf, payloads[index]);
            receipts[index] = new(Artifact(root.ReadSnapshot(step.TargetLeaf, R.MaximumPayloadBytes)!),
                step.RetentionLeaf is null ? null :
                    Artifact(root.ReadSnapshot(step.RetentionLeaf, R.MaximumPayloadBytes)!));
        }
        R.Completion? completion = null;
        if (complete && applied == steps.Length)
        {
            completion = new(1, epoch.Scope, sequence, op, Sha(exactIntent),
                receipts[0], receipts.Length == 2 ? receipts[1] : null);
            R.ValidateCompletion(R.IntentLeaf(epoch.Scope, sequence, op), exactIntent, completion);
            journal.PublishNew(R.CompletionLeaf(epoch.Scope, sequence, op), R.Encode(completion));
        }
        return new(intent, completion);
    }

    public void Dispose()
    {
        D.BeforeOperationForTests = null;
        if (Environment.GetEnvironmentVariable("FFX_KEEP_TEST_FIXTURES") == "1") return;
        Directory.Delete(RootPath, recursive: true);
    }
}

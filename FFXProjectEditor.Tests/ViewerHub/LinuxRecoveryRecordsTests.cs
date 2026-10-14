// WHY: Recovery control data must reject ambiguity and remain detached from filesystem permissions.
// MAINT: These are pure contract tests; passing them is not a persisted save, restart or RT2 proof.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using R = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryRecords;

namespace FFXProjectEditor.Tests.ViewerHub;

public sealed class LinuxRecoveryRecordsTests
{
    internal static readonly Guid Scope = Guid.Parse("11111111-1111-1111-1111-111111111111");
    internal static readonly Guid Operation = Guid.Parse("22222222-2222-2222-2222-222222222222");
    internal const string IntentName = "11111111111111111111111111111111-00000000000000000001-22222222222222222222222222222222.intent.json";
    private const string EpochName = "epoch-00000000000000000001-11111111111111111111111111111111.json";
    internal static string Digest(byte value) => Convert.ToHexString(SHA256.HashData(new[] { value }));
    internal static R.Artifact Artifact(ulong inode, byte value = 1) =>
        new(new("Linux", 8, inode, 2), 1, Digest(value),
            new(0x8180, 1000, 1, 10, 20, 30, 40));

    internal static R.Intent Intent(bool replace = false) =>
        new(1, Scope, 1, Operation, R.OperationKind.SidecarEdit,
            new("0001.json", replace ? Artifact(10) : null, 1, Digest(2),
                replace ? ".spira-retained-11111111111111111111111111111111-22222222222222222222222222222222-0.bin" : null),
            null);

    private static R.Epoch Epoch() => new(1, new("Linux", 8, 2, 2), 1, Scope, null,
        Digest(0), 0, 0, DateTimeOffset.UnixEpoch);

    internal static R.Completion Completion(R.Intent intent, byte[] bytes) =>
        new(1, intent.Scope, intent.Sequence, intent.Operation,
            Convert.ToHexString(SHA256.HashData(bytes)),
            new(Artifact(11, 2), intent.First.Expected is { } original
                ? original with { Stamp = original.Stamp with { ChangedSeconds = 31 } } : null),
            null);

    [Fact]
    public void AllThreeRecords_RoundTrip_AndBindExactIntent()
    {
        R.Epoch epoch = Epoch();
        Assert.Equal(epoch, R.ReadEpoch(EpochName, R.Encode(epoch)));
        R.Intent intent = Intent(replace: true);
        byte[] bytes = R.Encode(intent);
        Assert.Equal(intent, R.ReadIntent(IntentName, bytes));
        R.Completion completion = Completion(intent, bytes);
        string name = IntentName.Replace(".intent.json", ".complete.json", StringComparison.Ordinal);
        Assert.Equal(completion, R.ReadCompletion(name, R.Encode(completion)));
        R.ValidateCompletion(IntentName, bytes, completion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MagicSave_RequiresBackupFirst_AndExactlyTwoSteps(bool existingBackup)
    {
        R.Intent original = Intent();
        var backup = original.First with
        {
            TargetLeaf = "copy.dll.bak",
            Expected = existingBackup ? Artifact(15) : null,
            RetentionLeaf = existingBackup
                ? ".spira-retained-11111111111111111111111111111111-22222222222222222222222222222222-0.bin"
                : null,
        };
        R.Intent save = original with
        {
            Kind = R.OperationKind.MagicSave,
            First = backup,
            Second = original.First with { TargetLeaf = "copy.dll" },
        };
        Assert.Equal(save, R.ReadIntent(IntentName, R.Encode(save)));
        Assert.Throws<InvalidDataException>(() => R.Encode(save with { Second = null }));
        Assert.Throws<InvalidDataException>(() => R.Encode(save with
            { First = backup with { TargetLeaf = "another.dll.bak" } }));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("missing-nullable")]
    [InlineData("missing-first")]
    [InlineData("null-first")]
    [InlineData("numeric-kind")]
    [InlineData("unknown-kind")]
    [InlineData("wrong-case-key")]
    [InlineData("nested-unknown")]
    [InlineData("nested-missing")]
    [InlineData("nested-null")]
    [InlineData("version")]
    [InlineData("negative")]
    [InlineData("overflow-number")]
    public void Decode_RejectsMalformedOrUnsupportedFields(string mutation)
    {
        JsonObject root = JsonNode.Parse(R.Encode(Intent(true)))!.AsObject();
        switch (mutation)
        {
            case "unknown": root["surprise"] = 1; break;
            case "missing-nullable": root.Remove("second"); break;
            case "missing-first": root.Remove("first"); break;
            case "null-first": root["first"] = null; break;
            case "numeric-kind": root["kind"] = 2; break;
            case "unknown-kind": root["kind"] = "Replay"; break;
            case "wrong-case-key": root["Version"] = root["version"]!.DeepClone(); root.Remove("version"); break;
            case "nested-unknown": root["first"]!["expected"]!["identity"]!["drive"] = "C:"; break;
            case "nested-missing": root["first"]!["expected"]!["identity"]!.AsObject().Remove("device"); break;
            case "nested-null": root["first"]!["expected"]!["identity"] = null; break;
            case "version": root["version"] = 2; break;
            case "negative": root["sequence"] = -1; break;
            case "overflow-number": root["sequence"] = JsonNode.Parse("18446744073709551616"); break;
            default: throw new InvalidOperationException(mutation);
        }
        byte[] malformed = Encoding.UTF8.GetBytes(root.ToJsonString());
        Assert.Throws<InvalidDataException>(() => R.ReadIntent(IntentName, malformed));
    }

    [Theory]
    [InlineData("duplicate-root")]
    [InlineData("duplicate-nested")]
    [InlineData("comment")]
    [InlineData("trailing-comma")]
    [InlineData("extra-value")]
    [InlineData("deep")]
    [InlineData("invalid-utf8")]
    public void Decode_RejectsAmbiguousJsonBytes(string mutation)
    {
        string json = Encoding.UTF8.GetString(R.Encode(Intent(true)));
        byte[] bytes = mutation switch
        {
            "duplicate-root" => Encoding.UTF8.GetBytes(json.Insert(1, "\"version\":1,")),
            "duplicate-nested" => Encoding.UTF8.GetBytes(json.Replace("\"backend\":\"Linux\"",
                "\"backend\":\"Linux\",\"backend\":\"Linux\"", StringComparison.Ordinal)),
            "comment" => Encoding.UTF8.GetBytes("/*comment*/" + json),
            "trailing-comma" => Encoding.UTF8.GetBytes(json[..^1] + ",}"),
            "extra-value" => Encoding.UTF8.GetBytes(json + "{}"),
            "deep" => Encoding.UTF8.GetBytes(new string('[', 10) + "0" + new string(']', 10)),
            "invalid-utf8" => new byte[] { 0x7B, 0x22, 0xFF, 0x22, 0x3A, 0x30, 0x7D },
            _ => throw new InvalidOperationException(mutation),
        };
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => R.ReadIntent(IntentName, bytes));
        if (mutation == "deep") Assert.IsAssignableFrom<JsonException>(error.InnerException);
    }

    [Theory]
    [InlineData("epoch", "", "previous")]
    [InlineData("intent", "first", "expected")]
    [InlineData("intent", "first", "retentionLeaf")]
    [InlineData("intent", "", "second")]
    [InlineData("completion", "first", "retained")]
    [InlineData("completion", "", "second")]
    public void RequiredNullable_MustBePresentEvenWhenExplicitNullIsValid(
        string kind, string parent, string property)
    {
        R.Intent create = Intent();
        byte[] intentBytes = R.Encode(create);
        byte[] original = kind switch
        {
            "epoch" => R.Encode(Epoch()),
            "intent" => intentBytes,
            "completion" => R.Encode(Completion(create, intentBytes)),
            _ => throw new InvalidOperationException(kind),
        };
        string completionName = IntentName.Replace(".intent.json", ".complete.json", StringComparison.Ordinal);
        Action<byte[]> read = kind switch
        {
            "epoch" => bytes => { _ = R.ReadEpoch(EpochName, bytes); },
            "intent" => bytes => { _ = R.ReadIntent(IntentName, bytes); },
            "completion" => bytes => { _ = R.ReadCompletion(completionName, bytes); },
            _ => throw new InvalidOperationException(kind),
        };
        read(original); // Explicit null is a legal, present value in this create-shaped record.
        JsonObject root = JsonNode.Parse(original)!.AsObject();
        JsonObject container = parent.Length == 0 ? root : root[parent]!.AsObject();
        Assert.True(container.ContainsKey(property));
        Assert.Null(container[property]);
        Assert.True(container.Remove(property));
        byte[] absent = Encoding.UTF8.GetBytes(root.ToJsonString());
        Assert.Throws<InvalidDataException>(() => read(absent));
    }

    [Fact]
    public void RecordSize_ExactBoundaryPasses_OneMoreAndEmptyRefuse()
    {
        byte[] bytes = R.Encode(Intent());
        byte[] padded = new byte[R.MaximumRecordBytes];
        padded.AsSpan().Fill((byte)' ');
        bytes.CopyTo(padded, 0);
        Assert.Equal(Intent(), R.ReadIntent(IntentName, padded));
        byte[] oversized = new byte[R.MaximumRecordBytes + 1];
        padded.CopyTo(oversized, 0);
        oversized[^1] = (byte)' ';
        Assert.Throws<InvalidDataException>(() => R.ReadIntent(IntentName, oversized));
        Assert.Throws<InvalidDataException>(() => R.ReadIntent(IntentName, Array.Empty<byte>()));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("nested/leaf")]
    [InlineData(".spira-recovery")]
    [InlineData(".spira-retained-user.bin")]
    [InlineData(".SPIRA-RECOVERY")]
    [InlineData("")]
    public void Intent_RejectsUnsafeOrReservedTarget(string name) =>
        Assert.Throws<InvalidDataException>(() =>
            R.Encode(Intent() with { First = Intent().First with { TargetLeaf = name } }));

    [Fact]
    public void Names_AreOrdinalInvariantAndBoundToRecordFields()
    {
        CultureInfo incoming = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            Assert.Equal(IntentName, R.IntentLeaf(Scope, 1, Operation));
            Assert.Equal(EpochName, R.EpochLeaf(1, Scope));
            Assert.Equal(IntentName.Replace(".intent.json", ".complete.json", StringComparison.Ordinal),
                R.CompletionLeaf(Scope, 1, Operation));
            Assert.Equal(Intent(true).First.RetentionLeaf, R.RetainedLeaf(Scope, Operation, 0));
            Assert.Throws<InvalidDataException>(() => R.ReadIntent(IntentName.ToUpperInvariant(), R.Encode(Intent())));
            Assert.Throws<InvalidDataException>(() => R.ReadEpoch("other.json", R.Encode(Epoch())));
        }
        finally { CultureInfo.CurrentCulture = incoming; }
    }

    [Fact]
    public void Completion_BindsRawBytes_NotEquivalentReserialization()
    {
        R.Intent intent = Intent();
        byte[] canonical = R.Encode(intent);
        R.Completion completed = Completion(intent, canonical);
        byte[] equivalent = Encoding.UTF8.GetBytes(" " + Encoding.UTF8.GetString(canonical));
        Assert.Equal(intent, R.ReadIntent(IntentName, equivalent));
        Assert.Throws<InvalidDataException>(() => R.ValidateCompletion(IntentName, equivalent, completed));
        R.ValidateCompletion(IntentName, equivalent, completed with
            { IntentSha256 = Convert.ToHexString(SHA256.HashData(equivalent)) });
    }

    [Theory]
    [InlineData("operation")]
    [InlineData("target-length")]
    [InlineData("target-hash")]
    [InlineData("target-mode")]
    [InlineData("retained-identity")]
    [InlineData("retained-hash")]
    [InlineData("retained-mode")]
    [InlineData("retained-owner")]
    [InlineData("retained-mtime-seconds")]
    [InlineData("retained-mtime-nanos")]
    [InlineData("missing-retained")]
    [InlineData("extra-step")]
    public void Completion_RejectsMismatchedReceipt(string mutation)
    {
        R.Intent intent = Intent(true);
        byte[] bytes = R.Encode(intent);
        R.Completion good = Completion(intent, bytes);
        R.Completion bad = mutation switch
        {
            "operation" => good with { Operation = Scope },
            "target-length" => good with { First = good.First with { Target = good.First.Target with { Length = 2 } } },
            "target-hash" => good with { First = good.First with { Target = good.First.Target with { Sha256 = Digest(9) } } },
            "target-mode" => good with { First = good.First with { Target = good.First.Target with { Stamp = good.First.Target.Stamp with { Mode = 0x81A4 } } } },
            "retained-identity" => good with { First = good.First with { Retained = good.First.Retained! with { Identity = Artifact(99).Identity } } },
            "retained-hash" => good with { First = good.First with { Retained = good.First.Retained! with { Sha256 = Digest(9) } } },
            "retained-mode" => good with { First = good.First with { Retained = good.First.Retained! with { Stamp = good.First.Retained.Stamp with { Mode = 0x81A4 } } } },
            "retained-owner" => good with { First = good.First with { Retained = good.First.Retained! with { Stamp = good.First.Retained.Stamp with { Owner = 1001 } } } },
            "retained-mtime-seconds" => good with { First = good.First with { Retained = good.First.Retained! with { Stamp = good.First.Retained.Stamp with { ModifiedSeconds = 11 } } } },
            "retained-mtime-nanos" => good with { First = good.First with { Retained = good.First.Retained! with { Stamp = good.First.Retained.Stamp with { ModifiedNanos = 21 } } } },
            "missing-retained" => good with { First = good.First with { Retained = null } },
            "extra-step" => good with { Second = good.First },
            _ => throw new InvalidOperationException(mutation),
        };
        Assert.Throws<InvalidDataException>(() => R.ValidateCompletion(IntentName, bytes, bad));
    }

    [Theory]
    [InlineData("backend")]
    [InlineData("empty-guid")]
    [InlineData("zero-sequence")]
    [InlineData("null-hash")]
    [InlineData("lower-hash")]
    [InlineData("oversize-payload")]
    [InlineData("oversize-leaf")]
    [InlineData("null-stamp")]
    [InlineData("bad-nanos")]
    [InlineData("shared-write")]
    [InlineData("hardlink")]
    [InlineData("zero-inode")]
    [InlineData("missing-retention")]
    [InlineData("wrong-retention")]
    public void Intent_RejectsInvalidDomainBeforeEncoding(string mutation)
    {
        R.Intent good = Intent(true);
        R.Step step = good.First;
        R.Artifact old = step.Expected!;
        R.Intent bad = mutation switch
        {
            "backend" => good with { First = step with { Expected = old with { Identity = old.Identity with { Backend = "Windows" } } } },
            "empty-guid" => good with { Scope = Guid.Empty },
            "zero-sequence" => good with { Sequence = 0 },
            "null-hash" => good with { First = step with { PlannedSha256 = null! } },
            "lower-hash" => good with { First = step with { PlannedSha256 = Digest(2).ToLowerInvariant() } },
            "oversize-payload" => good with { First = step with { PlannedLength = R.MaximumPayloadBytes + 1 } },
            "oversize-leaf" => good with { First = step with { TargetLeaf = new string('x', 256) } },
            "null-stamp" => good with { First = step with { Expected = old with { Stamp = null! } } },
            "bad-nanos" => good with { First = step with { Expected = old with { Stamp = old.Stamp with { ChangedNanos = 1_000_000_000 } } } },
            "shared-write" => good with { First = step with { Expected = old with { Stamp = old.Stamp with { Mode = 0x81B4 } } } },
            "hardlink" => good with { First = step with { Expected = old with { Stamp = old.Stamp with { Links = 2 } } } },
            "zero-inode" => good with { First = step with { Expected = old with { Identity = old.Identity with { Inode = 0 } } } },
            "missing-retention" => good with { First = step with { RetentionLeaf = null } },
            "wrong-retention" => good with { First = step with { RetentionLeaf = "unowned.bin" } },
            _ => throw new InvalidOperationException(mutation),
        };
        Assert.Throws<InvalidDataException>(() => R.Encode(bad));
    }

    [Fact]
    public void Restore_RequiresExpectedTarget_AndNewReceiptCannotAliasOriginal()
    {
        R.Intent restore = Intent(true) with { Kind = R.OperationKind.MagicRestore };
        byte[] bytes = R.Encode(restore);
        R.ValidateCompletion(IntentName, bytes, Completion(restore, bytes));
        Assert.Throws<InvalidDataException>(() => R.Encode(restore with
            { First = restore.First with { Expected = null, RetentionLeaf = null } }));
        R.Completion alias = Completion(restore, bytes);
        alias = alias with { First = alias.First with
            { Target = alias.First.Target with { Identity = restore.First.Expected!.Identity } } };
        Assert.Throws<InvalidDataException>(() => R.ValidateCompletion(IntentName, bytes, alias));
    }

    [Fact]
    public void TwoStepCompletion_MatchesBothSteps_AndOrder()
    {
        R.Intent save = Intent() with
        {
            Kind = R.OperationKind.MagicSave,
            First = Intent().First with { TargetLeaf = "copy.dll.bak" },
            Second = Intent().First with { TargetLeaf = "copy.dll", PlannedSha256 = Digest(3) },
        };
        byte[] bytes = R.Encode(save);
        R.Completion complete = Completion(save, bytes) with
            { Second = new(Artifact(12, 3), null) };
        R.ValidateCompletion(IntentName, bytes, complete);
        Assert.Throws<InvalidDataException>(() => R.ValidateCompletion(IntentName, bytes,
            complete with { First = complete.Second!, Second = complete.First }));
    }


    [Theory]
    [InlineData("target-target")]
    [InlineData("target-retained")]
    [InlineData("retained-target")]
    [InlineData("retained-retained")]
    public void Completion_DistinctLeavesCannotReuseAnInode_EvenForEqualPayloads(string alias)
    {
        R.Completion original = Completion(Intent(true), R.Encode(Intent(true)));
        R.Completion good = original with { Second = new(Artifact(13, 2), Artifact(12)) };
        Assert.Equal(good, R.ReadCompletion(
            IntentName.Replace(".intent.json", ".complete.json", StringComparison.Ordinal), R.Encode(good)));
        R.StepReceipt second = good.Second!;
        R.Completion bad = alias switch
        {
            "target-target" => good with { Second = second with { Target = good.First.Target } },
            "target-retained" => good with { Second = second with { Retained = good.First.Target } },
            "retained-target" => good with { Second = second with { Target = good.First.Retained! } },
            "retained-retained" => good with { Second = second with { Retained = good.First.Retained } },
            _ => throw new InvalidOperationException(alias),
        };
        // Structural Encode is intentional: a different hash check in ValidateCompletion must
        // not mask the cross-step uniqueness guard this test targets.
        Assert.Throws<InvalidDataException>(() => R.Encode(bad));
    }

    [Fact]
    public void Intent_TwoObservedTargetsCannotShareOneSingleLinkedInode()
    {
        R.Intent one = Intent(true);
        R.Intent good = one with
        {
            Kind = R.OperationKind.MagicSave,
            First = one.First with { TargetLeaf = "copy.dll.bak" },
            Second = one.First with
            {
                TargetLeaf = "copy.dll",
                Expected = Artifact(12),
                RetentionLeaf = ".spira-retained-11111111111111111111111111111111-22222222222222222222222222222222-1.bin",
            },
        };
        Assert.Equal(good, R.ReadIntent(IntentName, R.Encode(good)));
        Assert.Throws<InvalidDataException>(() => R.Encode(good with
            { Second = good.Second! with { Expected = good.First.Expected } }));
    }

    [Fact]
    public void Leaf_Utf8BoundaryIsPreserved_AndHugeSegmentedInputRefusesBeforeSplit()
    {
        R.Intent one = Intent();
        R.Intent Valid(string leaf) => one with { First = one.First with { TargetLeaf = leaf } };
        foreach (string leaf in new[] { new string('x', 255), new string('é', 127) + "x" })
        {
            R.Intent accepted = Valid(leaf);
            Assert.Equal(accepted, R.ReadIntent(IntentName, R.Encode(accepted)));
        }
        Assert.Throws<InvalidDataException>(() => R.Encode(Valid(new string('é', 128))));

        string huge = string.Join("/", new string('a', 32768).ToCharArray()) + "/b";
        R.Intent oversized = Valid(huge); // Caller-owned allocation is outside the measurement.
        Action encode = () => { _ = R.Encode(oversized); };
        Assert.Throws<InvalidDataException>(encode); // Warm exception/assertion infrastructure.
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 3; i++) Assert.Throws<InvalidDataException>(encode);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(allocated, 1, 64 * 1024);
    }


    [Fact]
    public void Epoch_PredecessorIsContext_NotAFileOrLifetimeChain()
    {
        R.Epoch next = Epoch() with
        {
            Ordinal = 9, Scope = Operation,
            Previous = new(3, Scope, Artifact(15).Identity, Digest(1)),
            CarryOverBytes = 100, CarryOverEntries = 2,
        };
        Assert.Equal(next, R.ReadEpoch(R.EpochLeaf(9, Operation), R.Encode(next)));
        Assert.Throws<InvalidDataException>(() => R.Encode(next with { Previous = null }));
        Assert.Throws<InvalidDataException>(() => R.Encode(next with
            { Previous = next.Previous! with { Ordinal = 9 } }));
        // This test neither looks for predecessor files nor promotes a selected head.
    }
}

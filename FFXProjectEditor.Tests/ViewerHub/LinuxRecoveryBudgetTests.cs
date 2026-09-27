// WHY: Each admission dimension must reject independently, including ACK with unhealthy history.
// MAINT: No reducer or I/O is involved; supplied usage still needs a real retained-directory inventory.
using System;
using System.IO;
using Xunit;
using B = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryBudget;
using R = FFXProjectEditor.Modules.Common.ViewerHub.LinuxRecoveryRecords;

namespace FFXProjectEditor.Tests.ViewerHub;

public sealed class LinuxRecoveryBudgetTests
{
    [Theory]
    [InlineData("payload", 67108864L)]
    [InlineData("history", 536870912L)]
    [InlineData("managed", 2048L)]
    [InlineData("directory", 8192L)]
    [InlineData("record", 8192L)]
    public void ResourcePolicy_MatchesApprovedLiteralLimits(string dimension, long expected)
    {
        // Policy literals are intentionally independent of the production boundary formulas.
        long actual = dimension switch
        {
            "payload" => R.MaximumPayloadBytes,
            "history" => B.MaximumBytes,
            "managed" => B.MaximumEntries,
            "directory" => B.MaximumDirectoryEntries,
            "record" => R.MaximumRecordBytes,
            _ => throw new InvalidOperationException(dimension),
        };
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Operation_ReservesPayloadDisplacementAndAck_WithoutLifetimeCharge()
    {
        R.Intent intent = LinuxRecoveryRecordsTests.Intent(true);
        var observed = new B.Usage(100, 4, 9, 3);
        B.Reservation result = B.ForOperation(observed, intent);
        Assert.Equal(new B.Reservation(100 + 2 + 3 * 8192, 8, 10, 6), result);
        Assert.Equal(result, B.ForOperation(observed, intent));
        // A repeat against the SAME observed inventory does not mutate a hidden/lifetime counter.
        Assert.Equal(new B.Usage(100, 4, 9, 3), observed);
    }

    [Fact]
    public void Ack_HasSeparateFormula_StillChargesRemainingHistory()
    {
        var observed = new B.Usage(100, 4, 9, 3);
        Assert.Equal(new B.Reservation(8292, 5, 9, 4), B.ForAcknowledgement(observed));
        // At remaining header capacity, ordinary admission fails but ACK still succeeds.
        var headerOnly = new B.Usage(B.MaximumBytes - 8192, B.MaximumEntries - 1, 10, 10);
        Assert.Equal(B.MaximumBytes, B.ForAcknowledgement(headerOnly).PeakBytes);
        Assert.Equal(B.MaximumEntries, B.ForAcknowledgement(headerOnly).PeakManagedEntries);
        Assert.Throws<InvalidDataException>(() =>
            B.ForOperation(headerOnly, LinuxRecoveryRecordsTests.Intent()));
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("managed")]
    [InlineData("root")]
    [InlineData("journal")]
    public void Operation_EachExactLimitPasses_OneMoreIndependentlyRefuses(string dimension)
    {
        R.Intent intent = LinuxRecoveryRecordsTests.Intent();
        B.Usage boundary = dimension switch
        {
            "bytes" => new(B.MaximumBytes - 1 - 3 * 8192, 1, 1, 1),
            "managed" => new(0, B.MaximumEntries - 4, 1, 1),
            "root" => new(0, 1, B.MaximumDirectoryEntries - 1, 1),
            "journal" => new(0, 1, 1, B.MaximumDirectoryEntries - 3),
            _ => throw new InvalidOperationException(dimension),
        };
        B.Reservation accepted = B.ForOperation(boundary, intent);
        long exact = dimension switch
        {
            "bytes" => accepted.PeakBytes,
            "managed" => accepted.PeakManagedEntries,
            "root" => accepted.PeakRootEntries,
            _ => accepted.PeakJournalEntries,
        };
        Assert.Equal(dimension == "bytes" ? B.MaximumBytes :
            dimension == "managed" ? B.MaximumEntries : B.MaximumDirectoryEntries, exact);
        B.Usage excessive = dimension switch
        {
            "bytes" => boundary with { HistoryBytes = boundary.HistoryBytes + 1 },
            "managed" => boundary with { ManagedEntries = boundary.ManagedEntries + 1 },
            "root" => boundary with { RootEntries = boundary.RootEntries + 1 },
            _ => boundary with { JournalEntries = boundary.JournalEntries + 1 },
        };
        Assert.Throws<InvalidDataException>(() => B.ForOperation(excessive, intent));
    }

    [Theory]
    [InlineData(-1L, 0, 0, 0)]
    [InlineData(0L, -1, 0, 0)]
    [InlineData(0L, 0, -1, 0)]
    [InlineData(0L, 0, 0, -1)]
    [InlineData(long.MaxValue, 0, 0, 0)]
    [InlineData(0L, int.MaxValue, 0, 0)]
    [InlineData(0L, 0, int.MaxValue, 0)]
    [InlineData(0L, 0, 0, int.MaxValue)]
    public void InvalidAndOverflowingUsage_RefusesBeforeAnyPublication(long bytes, int entries, int root, int journal)
    {
        var observed = new B.Usage(bytes, entries, root, journal);
        Assert.Throws<InvalidDataException>(() => B.ForAcknowledgement(observed));
        Assert.Throws<InvalidDataException>(() =>
            B.ForOperation(observed, LinuxRecoveryRecordsTests.Intent()));
    }

    [Fact]
    public void TwoStepPlan_ChargesBothDisplacedPayloadsAndBothRootSlots()
    {
        R.Intent original = LinuxRecoveryRecordsTests.Intent(true);
        R.Intent save = original with
        {
            Kind = R.OperationKind.MagicSave,
            First = original.First with { TargetLeaf = "copy.dll.bak" },
            Second = original.First with
            {
                TargetLeaf = "copy.dll",
                PlannedLength = R.MaximumPayloadBytes,
                Expected = LinuxRecoveryRecordsTests.Artifact(12) with { Length = R.MaximumPayloadBytes },
                RetentionLeaf = ".spira-retained-11111111111111111111111111111111-22222222222222222222222222222222-1.bin",
            },
        };
        B.Reservation result = B.ForOperation(new(0, 0, 1, 0), save);
        Assert.Equal(2L + 2L * R.MaximumPayloadBytes + 3L * 8192, result.PeakBytes);
        Assert.Equal(5, result.PeakManagedEntries);
        Assert.Equal(3, result.PeakRootEntries);
        Assert.Equal(3, result.PeakJournalEntries);
    }
}

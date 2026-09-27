// ── Direct two-limit IBufferWriter contract ─────────────────────────────────────────────
// WHY: Serializer parity cannot prove request arithmetic, growth ordering, or failed export state.
// MAINT: Counters below describe this private sink only; no assertion treats them as GC/RSS evidence.
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using FFXProjectEditor.Modules.Common.ViewerHub;
using Xunit;
using B = FFXProjectEditor.Modules.Common.ViewerHub.LinuxSidecarJson.BoundedBufferWriter;

namespace FFXProjectEditor.Tests.ViewerHub;

public sealed class LinuxSidecarJsonBufferTests
{
    [Theory]
    [InlineData("negative-output")]
    [InlineData("negative-capacity")]
    [InlineData("output-above-capacity")]
    public void Constructor_RejectsInvalidLimitsWithoutAllocation(string kind)
    {
        Action action = kind switch
        {
            "negative-output" => () => _ = new B(-1, 0),
            "negative-capacity" => () => _ = new B(0, -1),
            _ => () => _ = new B(2, 1)
        };
        Assert.Throws<ArgumentOutOfRangeException>(action);
    }

    [Fact]
    public void GetMemory_ZeroHintReturnsNonEmptyMemory()
    {
        var writer = new B(4, 8);
        Memory<byte> memory = writer.GetMemory(0);
        Assert.True(memory.Length >= 1);
        writer.Advance(0);
        Assert.Equal(0, writer.WrittenCount);
        Assert.Empty(writer.ExportExact());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void GetMemory_NegativeHintLatchesFailureBeforeAllocation(int hint)
    {
        var writer = new B(4, 8);
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = writer.GetMemory(hint); });
        Assert.True(writer.IsFailed);
        Assert.Equal(0, writer.AllocationAttemptCount);
        Assert.Throws<InvalidOperationException>(() => writer.ExportExact());
    }

    [Fact]
    public void Advance_NegativeCountLatchesFailure()
    {
        var writer = new B(4, 8);
        _ = writer.GetMemory(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Advance(-1));
        Assert.True(writer.IsFailed);
        Assert.Throws<InvalidOperationException>(() => writer.ExportExact());
    }

    [Fact]
    public void Advance_CountOutsideGrantedMemoryIsRefused()
    {
        var writer = new B(16, 16);
        Memory<byte> memory = writer.GetMemory(1);
        Assert.Throws<InvalidOperationException>(() => writer.Advance(memory.Length + 1));
        Assert.Equal(0, writer.WrittenCount);
        Assert.True(writer.IsFailed);
    }

    [Fact]
    public void Advance_CheckedCumulativeCountOverflowIsRefused()
    {
        var writer = new B(16, 32);
        writer.GetSpan(1)[0] = 0x41;
        writer.Advance(1);
        Assert.Throws<InvalidDataException>(() => writer.Advance(int.MaxValue));
        Assert.Equal(1, writer.WrittenCount);
        Assert.True(writer.IsFailed);
        Assert.Throws<InvalidOperationException>(() => writer.ExportExact());
    }

    [Fact]
    public void GetMemory_CheckedWrittenPlusHintOverflowLatchesBeforeAnotherAllocation()
    {
        var writer = new B(16, 32);
        writer.GetSpan(1)[0] = 0x41;
        writer.Advance(1);
        int allocationsBefore = writer.AllocationAttemptCount;

        Assert.Throws<InvalidDataException>(() => { _ = writer.GetMemory(int.MaxValue); });

        Assert.Equal(allocationsBefore, writer.AllocationAttemptCount);
        Assert.Equal(1, writer.WrittenCount);
        Assert.True(writer.IsFailed);
        Assert.Throws<InvalidOperationException>(() => writer.ExportExact());
        Assert.Throws<InvalidOperationException>(() => { _ = writer.GetMemory(1); });
    }

    [Fact]
    public void Advance_CumulativeOutputLimitLatchesAndRefusesPartialExport()
    {
        var writer = new B(4, 16);
        Encoding.ASCII.GetBytes("abc").CopyTo(writer.GetSpan(3));
        writer.Advance(3);
        Encoding.ASCII.GetBytes("xy").CopyTo(writer.GetSpan(2));
        Assert.Throws<InvalidDataException>(() => writer.Advance(2));
        Assert.Equal(3, writer.WrittenCount);
        Assert.True(writer.IsFailed);
        Assert.Throws<InvalidOperationException>(() => writer.ExportExact());
    }

    [Fact]
    public void GetMemory_HintLargerThanLogicalOutputIsAllowedWhenActualAdvanceFits()
    {
        var writer = new B(4, 16);
        Memory<byte> memory = writer.GetMemory(8);
        Encoding.ASCII.GetBytes("four").CopyTo(memory.Span);
        writer.Advance(4);
        Assert.Equal(8, writer.LargestSizeHint);
        Assert.Equal(Encoding.ASCII.GetBytes("four"), writer.ExportExact());
    }

    [Theory]
    [InlineData(16, false)]
    [InlineData(17, true)]
    public void GetMemory_RequiredCapacityCAndCPlusOneHaveDistinctAllocationOutcomes(int hint, bool refused)
    {
        var writer = new B(16, 16);
        if (refused)
        {
            Assert.Throws<InvalidDataException>(() => { _ = writer.GetMemory(hint); });
            Assert.Equal(0, writer.AllocationAttemptCount);
            Assert.True(writer.IsFailed);
            return;
        }

        Assert.True(writer.GetMemory(hint).Length >= hint);
        Assert.Equal(1, writer.AllocationAttemptCount);
        Assert.Equal(1, writer.AllocationCount);
        Assert.Equal(16, writer.CurrentCapacity);
    }

    [Fact]
    public void GetMemory_ActualWorkingCapacityPlusOneIsRefusedBeforeAllocation()
    {
        var writer = new B(LinuxSidecarJson.LogicalByteLimit, LinuxSidecarJson.WorkingCapacityLimit);
        Assert.Throws<InvalidDataException>(() =>
        {
            _ = writer.GetMemory(LinuxSidecarJson.WorkingCapacityLimit + 1);
        });
        Assert.Equal(0, writer.AllocationAttemptCount);
        Assert.Equal(0, writer.CurrentCapacity);
        Assert.True(writer.IsFailed);
    }

    [Fact]
    public void Growth_ReallocationPreservesBytesAndExactExportsAreIndependent()
    {
        var writer = new B(600, 1024);
        Encoding.ASCII.GetBytes("abcd").CopyTo(writer.GetSpan(4));
        writer.Advance(4);
        Assert.Equal(256, writer.CurrentCapacity);

        Span<byte> second = writer.GetSpan(300);
        Assert.Equal(512, writer.CurrentCapacity);
        Encoding.ASCII.GetBytes("xyz").CopyTo(second);
        writer.Advance(3);

        byte[] first = writer.ExportExact();
        byte[] secondExport = writer.ExportExact();
        Assert.Equal(2, writer.AllocationCount);
        Assert.Equal(Encoding.ASCII.GetBytes("abcdxyz"), first);
        Assert.NotSame(first, secondExport);
        first[0] = 0;
        Assert.Equal((byte)'a', secondExport[0]);
    }

    [Theory]
    [InlineData(33, true)]
    [InlineData(32, false)]
    public void Utf8JsonWriter_FinalFlushEnforcesLogicalBoundary(int logicalLimit, bool succeeds)
    {
        var sink = new B(logicalLimit, 512);
        var json = new Utf8JsonWriter(sink);
        string value = new('a', 31); // Encoded JSON string length is exactly 33 bytes.
        json.WriteStringValue(value);
        Assert.Equal(0, sink.WrittenCount);

        if (succeeds)
        {
            json.Flush();
            json.Dispose();
            Assert.Equal(33, sink.WrittenCount);
            Assert.Equal(Encoding.UTF8.GetBytes("\"" + value + "\""), sink.ExportExact());
            return;
        }

        Assert.Throws<InvalidDataException>(() => json.Flush());
        sink.LatchFailure();
        try { json.Reset(); } catch { }
        try { json.Dispose(); } catch { }
        Assert.True(sink.IsFailed);
        Assert.Throws<InvalidOperationException>(() => sink.ExportExact());
    }

    [Fact]
    public void GetSpan_WritesThroughTheSameGrantAndAdvanceContract()
    {
        var writer = new B(3, 8);
        Span<byte> span = writer.GetSpan(3);
        span[0] = 1;
        span[1] = 2;
        span[2] = 3;
        writer.Advance(3);
        Assert.Equal(new byte[] { 1, 2, 3 }, writer.ExportExact());
        Assert.Equal(1, writer.RequestCount);
    }

    [Fact]
    public void ExplicitSerializationFailureLatchRefusesPreviouslyWrittenPrefix()
    {
        var writer = new B(8, 16);
        Encoding.ASCII.GetBytes("ok").CopyTo(writer.GetSpan(2));
        writer.Advance(2);
        writer.LatchFailure();
        Assert.Equal(2, writer.WrittenCount);
        Assert.True(writer.IsFailed);
        Assert.Throws<InvalidOperationException>(() => writer.ExportExact());
        Assert.Throws<InvalidOperationException>(() => { _ = writer.GetMemory(1); });
    }
}

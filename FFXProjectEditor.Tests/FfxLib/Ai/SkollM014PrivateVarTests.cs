using System;
using FFXProjectEditor.FfxLib.Ai;
using Xunit;

namespace FFXProjectEditor.Tests.FfxLib.Ai
{
    /// <summary>
    /// Regression tests for the private-var-slot exhaustion path observed in Skoll m014.
    /// The fixture encodes only the relevant AI-file structure: three workers, worker[1]
    /// with two private slots, and descriptors for both slots. It is deliberately
    /// versioned here rather than read from a developer's local game installation so
    /// the regression is reproducible in CI.
    /// </summary>
    public class SkollM014PrivateVarTests
    {
        const int Worker0Offset = 0x50;
        const int Worker1Offset = 0x80;
        const int Worker2Offset = 0xB0;
        const int VariableTableOffset = 0xE0;
        const int ScriptStartOffset = 0xF0;

        static byte[] CreateExhaustedPrivateSlotFixture()
        {
            byte[] ai = new byte[0x100];

            WriteU32(ai, AiScript_File.OffDeclaredLength, (uint)ai.Length);
            WriteU16(ai, AiScript_File.OffWorkersTotal, 3);
            WriteU32(ai, AiScript_File.OffScriptStart, ScriptStartOffset);
            WriteU16(ai, AiScript_File.OffWorkerCount, 3);
            WriteU32(ai, AiScript_File.OffWorkerOffsetTable, Worker0Offset);
            WriteU32(ai, AiScript_File.OffWorkerOffsetTable + 4, Worker1Offset);
            WriteU32(ai, AiScript_File.OffWorkerOffsetTable + 8, Worker2Offset);

            ConfigureWorker(ai, Worker0Offset, privateDataLength: 0);
            ConfigureWorker(ai, Worker1Offset, privateDataLength: 8);
            ConfigureWorker(ai, Worker2Offset, privateDataLength: 0);

            // Variable descriptor layout: [storage << 24 | slot][typeId].
            // Both 4-byte slots in worker[1]'s 8-byte private storage are occupied.
            WriteU32(ai, VariableTableOffset, 0x56000000);
            WriteU32(ai, VariableTableOffset + 4, 1);
            WriteU32(ai, VariableTableOffset + 8, 0x56000004);
            WriteU32(ai, VariableTableOffset + 12, 1);
            return ai;
        }

        static void ConfigureWorker(byte[] ai, int descriptorOffset, ushort privateDataLength)
        {
            WriteU16(ai, descriptorOffset + 0x10, privateDataLength);
            WriteU32(ai, descriptorOffset + 0x14, VariableTableOffset);
            WriteU32(ai, descriptorOffset + 0x18, VariableTableOffset + 16);
            WriteU32(ai, descriptorOffset + 0x1C, VariableTableOffset + 16);
        }

        static void WriteU16(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        static void WriteU32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        [Fact]
        public void Fixture_HasThreeWorkers_AndWorkerOneHasPrivateStorage()
        {
            AiScriptFile script = AiScript_File.Read(CreateExhaustedPrivateSlotFixture());

            Assert.Equal(3, script.Workers.Count);
            Assert.Equal(0, script.Workers[0].PrivateDataLength);
            Assert.Equal(8, script.Workers[1].PrivateDataLength);
            Assert.Equal(0, script.Workers[2].PrivateDataLength);
        }

        [Fact]
        public void TryFindFreePrivateSlot_Fails_BecauseBothSlotsAreOccupied()
        {
            AiScriptFile script = AiScript_File.Read(CreateExhaustedPrivateSlotFixture());

            bool ok = AiScript_File.TryFindFreePrivateVariableSlot(script, out int slot, out string reason);

            Assert.False(ok, $"expected failure but got slot {slot} ({reason})");
            Assert.DoesNotContain("Nenhum worker", reason, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("slots", reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GrowWorkerOne_ByEightBytes_FreesTheFirstNewSlot()
        {
            byte[] ai = CreateExhaustedPrivateSlotFixture();
            AiScriptFile script = AiScript_File.Read(ai);

            int desc = script.Workers[1].DescriptorOffset;
            int curLen = ai[desc + 0x10] | (ai[desc + 0x11] << 8);
            Assert.Equal(8, curLen);

            int newLen = curLen + 8;
            WriteU16(ai, desc + 0x10, (ushort)newLen);

            AiScriptFile regrown = AiScript_File.Read(ai);
            bool ok = AiScript_File.TryFindFreePrivateVariableSlot(regrown, out int slot, out string reason);

            Assert.True(ok, $"expected free slot after growth but failed: {reason}");
            Assert.Equal(curLen, slot);
            Assert.Equal(curLen + 8, regrown.Workers[1].PrivateDataLength);
        }

        [Fact]
        public void GrowWorkerOne_PreservesRoundTripByteIdentity()
        {
            byte[] ai = CreateExhaustedPrivateSlotFixture();
            AiScriptFile script = AiScript_File.Read(ai);

            Assert.True(AiScript_File.RoundTripsByteIdentical(ai),
                "fixture AiFile must round-trip byte-identical before any edit");

            int desc = script.Workers[1].DescriptorOffset;
            int newLen = (ai[desc + 0x10] | (ai[desc + 0x11] << 8)) + 8;
            WriteU16(ai, desc + 0x10, (ushort)newLen);

            AiScriptFile regrown = AiScript_File.Read(ai);
            byte[] re = AiScript_File.Write(regrown);
            Assert.Equal(ai.Length, re.Length);
            Assert.True(re.AsSpan().SequenceEqual(ai),
                "grown AiFile must round-trip byte-identical (only the privLen u16 changed)");
        }
    }
}

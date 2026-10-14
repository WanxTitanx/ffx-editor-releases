using System;
using System.Buffers.Binary;
using System.Linq;
using FFXProjectEditor.FfxLib.Save;
using Xunit;

namespace FFXProjectEditor.Tests.FfxLib.Save;

public class FfxSaveEquipmentTests
{
    [Fact]
    public void SlotCatalog_CoversExactlyTwoHundredNativeRecords()
    {
        var offsets = FfxSaveEquipment.BuildSlotList().Select(s => s.RelativeOffset).OrderBy(x => x);
        Assert.Equal(Enumerable.Range(0, 200).Select(i => i * 22), offsets);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(199)]
    public void ReadAndWrite_UsesFourAlignedWords_PreservesNeighboursAndUnknownBytes(int physicalSlot)
    {
        var core = new FfxSaveCore();
        Array.Fill(core.Data, (byte)0xA5);
        var slot = new FfxSaveEquipmentSlotInfo { RelativeOffset = physicalSlot * 22 };
        int b = slot.AbsoluteBase;
        core.Data[b + 2] = 1;
        core.Data[b + 11] = 4;
        ushort[] abilities = { 0x8062, 0x80FF, 0x0000, 0x00FF };
        for (int i = 0; i < 4; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(core.Data.AsSpan(b + 14 + i * 2), abilities[i]);
        var before = (byte[])core.Data.Clone();

        var snapshot = FfxSaveEquipmentSnapshot.Read(core, slot, physicalSlot);
        Assert.Equal(abilities.Select(x => (int)x), new[] { snapshot.Auto1, snapshot.Auto2, snapshot.Auto3, snapshot.Auto4 });
        snapshot.Write(core);
        Assert.Equal(before, core.Data);

        snapshot.Auto4 = 0x8087;
        snapshot.Write(core);
        before[b + 20] = 0x87;
        before[b + 21] = 0x80;
        Assert.Equal(before, core.Data);
        Assert.Equal(0xA5, core.Data[b + 22]);
    }

    [Fact]
    public void InvalidCapacityAndSlot_AreRejectedBeforeAnyWrite()
    {
        var core = new FfxSaveCore();
        var before = (byte[])core.Data.Clone();
        var snapshot = new FfxSaveEquipmentSnapshot { SlotBase = FfxSaveEquipment.InventoryBase, AutoCapacity = 5 };
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.Write(core));
        Assert.Equal(before, core.Data);
        var outside = new FfxSaveEquipmentSlotInfo { RelativeOffset = 200 * 22 };
        Assert.Throws<ArgumentOutOfRangeException>(() => FfxSaveEquipmentSnapshot.Read(core, outside, 200));
        snapshot = new FfxSaveEquipmentSnapshot { SlotBase = FfxSaveEquipment.InventoryBase + 1 };
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot.Write(core));
        Assert.Equal(before, core.Data);
    }
}

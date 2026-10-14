using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.BattleMap;
using Xunit;

namespace FFXProjectEditor.Tests.Core;

public class BattleArenaTwoAreaGrowTests
{
    private const int Start = 96, Payload = 1456;
    private static readonly int[][] Pointers = {
        new[] { 224, 272, 320, 512, 704, 752, 192, 1440 },
        new[] { 800, 848, 896, 1088, 1280, 1328, 208, 1440 }
    };
    private static void I32(byte[] b, int at, int n) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(at, 4), n);
    private static int I32(byte[] b, int at) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at, 4));
    private static void F(byte[] b, int at, float n) => BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(at, 4), n);

    private static byte[] Fixture()
    {
        var bytes = Enumerable.Range(0, Start + Payload + 24).Select(i => (byte)(i * 17 + 7)).ToArray();
        I32(bytes, 0, 8);
        int[] offsets = { 36, 52, 64, Start, Start + Payload, Start + Payload + 12, 0, bytes.Length };
        for (int i = 0; i < offsets.Length; i++) I32(bytes, 4 + i * 4, offsets[i]);
        for (int area = 0; area < 2; area++)
        {
            int rec = Start + area * 96;
            bytes[rec] = 0; bytes[rec + 1] = 2; bytes[rec + 4] = 3; bytes[rec + 5] = 12; bytes[rec + 6] = 3;
            for (int p = 0; p < 8; p++) I32(bytes, rec + 0x10 + p * 4, Pointers[area][p]);
        }
        for (int p = 192; p < Payload; p += 16)
        {
            F(bytes, Start + p, p); F(bytes, Start + p + 4, 0); F(bytes, Start + p + 8, -p); F(bytes, Start + p + 12, 0);
        }
        F(bytes, Start + 1440 + 12, 6.679f);
        return bytes;
    }
    private static IReadOnlyList<IReadOnlyList<(float X, float Y, float Z)>> Points(byte[] bytes, int count)
    {
        var decoded = BattleArenaAnchors_File.ReadFromBattleBin("two-area", bytes);
        return decoded.Areas.Select(a => (IReadOnlyList<(float, float, float)>)a[BattleArena_AnchorRole.MonsterLive]!.Anchors
            .Select(p => (p.X, p.Y, p.Z)).Concat(Enumerable.Range(3, count - 3).Select(i => ((float)(50 + i), 0f, (float)(70 + i)))).ToArray()).ToArray();
    }

    [Fact]
    public void NoEdit_IsByteIdenticalAndExistingSingleAreaGuardRemains()
    {
        var bytes = Fixture();
        Assert.Equal(bytes, BattleArenaGrowWriter.GrowMonstersInTwoAreas(bytes, Points(bytes, 3)));
        Assert.False(BattleArenaGrowWriter.Plan(bytes).CanGrow);
    }

    [Fact]
    public void Growth_RebasesBothAreasAndExtendsTightStagingWithoutTouchingOthers()
    {
        var bytes = Fixture(); var immutable = bytes.ToArray(); var points = Points(bytes, 5);
        var output = BattleArenaGrowWriter.GrowMonstersInTwoAreas(bytes, points);
        Assert.Equal(immutable, bytes);
        Assert.Equal(bytes.Length + 96, output.Length); // live0, tight stagingB0, live1
        var before = BattleArenaAnchors_File.ReadFromBattleBin("test", bytes);
        var after = BattleArenaAnchors_File.ReadFromBattleBin("test", output);
        Assert.Empty(after.Notes);
        for (int area = 0; area < 2; area++)
        {
            Assert.Equal(points[area], after.Areas[area][BattleArena_AnchorRole.MonsterLive]!.Anchors.Select(p => (p.X, p.Y, p.Z)).ToArray());
            foreach (var role in new[] { BattleArena_AnchorRole.PartyFront, BattleArena_AnchorRole.PartyBack, BattleArena_AnchorRole.Aeon, BattleArena_AnchorRole.Camera, BattleArena_AnchorRole.MonsterStagingA, BattleArena_AnchorRole.MonsterStagingB })
                Assert.Equal(before.Areas[area][role]!.Anchors.Select(p => (p.X, p.Y, p.Z, p.W)), after.Areas[area][role]!.Anchors.Take(before.Areas[area][role]!.Anchors.Count).Select(p => (p.X, p.Y, p.Z, p.W)));
        }
        Assert.Equal(bytes.AsSpan(Start + Payload).ToArray(), output.AsSpan(Start + Payload + 96).ToArray());
        var allowed = new HashSet<int>();
        for (int i = 4; i <= 7; i++) for (int k = 0; k < 4; k++) allowed.Add(4 + i * 4 + k);
        for (int area = 0; area < 2; area++)
        {
            allowed.Add(Start + area * 96 + 6);
            for (int p = 0x10; p < 0x30; p++) allowed.Add(Start + area * 96 + p);
        }
        int[] splices = { Start + 752, Start + 800, Start + 1328 };
        for (int i = 0; i < bytes.Length; i++)
            if (!allowed.Contains(i)) Assert.Equal(bytes[i], output[i + splices.Count(p => p <= i) * 32]);
    }

    [Theory]
    [InlineData("unaligned")]
    [InlineData("outside")]
    [InlineData("aliased")]
    [InlineData("count")]
    [InlineData("format")]
    [InlineData("terminal")]
    public void InvalidLayouts_AreRefusedWithoutMutatingTheSource(string mutation)
    {
        var bytes = Fixture(); var points = Points(bytes, 5);
        switch (mutation)
        {
            case "unaligned": I32(bytes, Start + 0x20, 705); break;
            case "outside": I32(bytes, Start + 0x20, int.MaxValue); break;
            case "aliased": I32(bytes, Start + 96 + 0x20, 704); break;
            case "count": bytes[Start + 6] = 13; break;
            case "format": bytes[Start] = 1; break;
            case "terminal": I32(bytes, 32, bytes.Length - 1); break;
        }
        var immutable = bytes.ToArray();
        Assert.ThrowsAny<Exception>(() => BattleArenaGrowWriter.GrowMonstersInTwoAreas(bytes, points));
        Assert.Equal(immutable, bytes);
    }

    [Fact]
    public void InvalidPointLists_AreRefusedWithoutMutatingSource()
    {
        var bytes = Fixture(); var immutable = bytes.ToArray();
        Assert.ThrowsAny<ArgumentException>(() => BattleArenaGrowWriter.GrowMonstersInTwoAreas(bytes, Points(bytes, 9)));
        var bad = Points(bytes, 5).Select(a => a.ToArray()).ToArray(); bad[1][4] = (float.NaN, 0, 0);
        Assert.ThrowsAny<ArgumentException>(() => BattleArenaGrowWriter.GrowMonstersInTwoAreas(bytes, bad));
        Assert.Throws<InvalidDataException>(() => BattleArenaGrowWriter.GrowMonstersInTwoAreas(bytes, Points(bytes, 5).Take(1).ToArray()));
        Assert.Equal(immutable, bytes);
    }
}

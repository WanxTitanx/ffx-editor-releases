using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Ps3;
using Xunit;

namespace FFXProjectEditor.Tests.Core;

public class SpiraSpellVisualRepairTests
{
    static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
        "Fixtures", "SpellVisualRepair", name));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OnlyAnimationPairsChangeAndRepeatedRepairIsStable(bool player)
    {
        byte[] input = Fixture(player ? "command.bin" : "monmagic2.bin"), saved = input.ToArray();
        byte[] output = player ? SpiraSpellVisualRepairWriter.RepairPlayer(input) : SpiraSpellVisualRepairWriter.RepairMonsters(input);
        var changed = player ? new[] { 63, 367, 368, 369 } : new[] { 341, 356 };
        int size = BinaryPrimitives.ReadUInt16LittleEndian(input.AsSpan(12));
        var allowed = changed.SelectMany(row => Enumerable.Range(20 + row * size + 16, 4)).ToHashSet();
        Assert.Equal(saved, input); Assert.Equal(input.Length, output.Length);
        for (int i = 0; i < input.Length; i++) if (!allowed.Contains(i)) Assert.Equal(input[i], output[i]);
        Assert.NotEqual(input, output);
        Assert.Equal(output, player ? SpiraSpellVisualRepairWriter.RepairPlayer(output) : SpiraSpellVisualRepairWriter.RepairMonsters(output));
        Assert.Equal(output, Ability_Command.WriteList(Ability_Command.ReadList(output, player), player));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(8)] [InlineData(10)] [InlineData(12)] [InlineData(14)] [InlineData(16)]
    public void MalformedHeaderIsRejectedBeforeWriting(int offset)
    {
        byte[] input = Fixture("command.bin"); input[offset] ^= 255;
        Assert.Throws<InvalidDataException>(() => SpiraSpellVisualRepairWriter.RepairPlayer(input));
    }

    [Fact]
    public void ForeignAnimationOrGameplayChangeIsRejected()
    {
        byte[] input = Fixture("command.bin"); input[20 + 367 * 96 + 16] ^= 1;
        Assert.Throws<InvalidDataException>(() => SpiraSpellVisualRepairWriter.RepairPlayer(input));
        var rows = Ability_Command.ReadList(Fixture("command.bin"), true); rows[367].AttackPower++;
        Assert.Throws<InvalidDataException>(() => SpiraSpellVisualRepairWriter.RepairPlayer(Ability_Command.WriteList(rows, true)));
        Assert.Throws<InvalidDataException>(() => SpiraSpellVisualRepairWriter.RepairMonsters(new byte[19]));
        Assert.Throws<ArgumentNullException>(() => SpiraSpellVisualRepairWriter.RepairPlayer(null!));
    }

    [Fact]
    public void WindRecipesUseOrdinaryCastImpactDonorsAndGroupCounter()
    {
        Assert.Equal(new[] { 80, 88, 96 }, SpiraWindSpellVisuals.PlayerTiers.Select(r => r.CastDonor));
        Assert.Equal(new[] { 81, 89, 97 }, SpiraWindSpellVisuals.PlayerTiers.Select(r => r.ImpactDonor));
        Assert.Equal(new short[] { 898, 899, 900 }, SpiraWindSpellVisuals.PlayerTiers.Select(r => r.ImpactId));
        Assert.Equal(96, SpiraWindSpellVisuals.Aeroja.CastDonor);
        Assert.Equal(97, SpiraWindSpellVisuals.Aeroja.ImpactDonor);
        Assert.DoesNotContain(SpiraWindSpellVisuals.PlayerTiers, r => r.CastDonor == 397 || r.ImpactDonor == 397);
    }

    [Fact]
    public void HolyPatchPreservesInputPayloadAndUsesOnlyWitnessedCodePadding()
    {
        byte[] source = Fixture("holy-native.bin"), saved = source.ToArray();
        byte[] output = HolyActorFallbackAdapter.Apply(source, 902);
        int pe = BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(60));
        int virtualSize = pe + 24 + 224 + 8;
        int patch = 0x400 + HolyActorFallbackAdapter.PatchRva - 0x1000;
        int cave = 0x400 + HolyActorFallbackAdapter.CaveRva - 0x1000;
        byte[] stub = HolyActorFallbackAdapter.BuildStub();
        var allowed = Enumerable.Range(virtualSize, 4).Concat(Enumerable.Range(patch, 6))
            .Concat(Enumerable.Range(cave, stub.Length)).ToHashSet();
        Assert.Equal(saved, source); Assert.Equal(source.Length, output.Length);
        for (int i = 0; i < source.Length; i++) if (!allowed.Contains(i)) Assert.Equal(source[i], output[i]);
        Assert.Equal(0xE9, output[patch]);
        Assert.Equal(HolyActorFallbackAdapter.CaveRva,
            HolyActorFallbackAdapter.PatchRva + 5 + BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(patch + 1)));
        Assert.Equal(stub, output.AsSpan(cave, stub.Length).ToArray());
        Assert.Throws<InvalidDataException>(() => HolyActorFallbackAdapter.Apply(output, 902));
    }

    [Theory]
    [InlineData(0)] [InlineData(60)] [InlineData(0x400)] [InlineData(0x8800 - 1)]
    public void HolyRejectsUnreviewedOrTruncatedProfiles(int offset)
    {
        byte[] source = Fixture("holy-native.bin"); source[offset] ^= 1;
        Assert.Throws<InvalidDataException>(() => HolyActorFallbackAdapter.Apply(source, 902));
        Assert.Throws<InvalidDataException>(() => HolyActorFallbackAdapter.Apply(source[..900], 902));
        Assert.Throws<ArgumentException>(() => HolyActorFallbackAdapter.Apply(source, 146));
    }

    [Fact]
    public void HolyRejectsChangedSectionExtentsAndPermissions()
    {
        byte[] original = Fixture("holy-native.bin");
        int pe = BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(60)), section = pe + 24 + 224;
        foreach (int offset in new[] { section + 36, section + 2 * 40 + 16, pe + 24 + 56, pe + 24 + 64 })
        {
            byte[] input = original.ToArray(); input[offset] ^= 1;
            Assert.Throws<InvalidDataException>(() => HolyActorFallbackAdapter.Apply(input, 902));
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.FfxLib.Monster;
using Xunit;

namespace FFXProjectEditor.Tests.FfxLib.Ai;

public sealed class SpiraSinAirshipTests
{
    static byte[] Fixture(int id) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "SinAirship", $"m{id}.bin"));
    static readonly Lazy<SinAirshipTable> Table = new(() =>
    {
        string root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "EvraeHoly");
        var evrae = MonsterMagicGrowWriter.AppendEvraeHoly(File.ReadAllBytes(Path.Combine(root, "monmagic2.bin")),
            File.ReadAllBytes(Path.Combine(root, "command.bin")), new (short, short)[] { (913, 914), (915, 916), (917, 918) },
            new short[] { 919, 920, 921, 922 }, new short[] { 923, 924, 925, 926 });
        return MonsterMagicGrowWriter.AppendSinAirshipSpells(evrae.Bytes);
    });
    static byte[] Build(int id) => SpiraSinAirshipWriter.Build(SpiraSinAirshipStats.Apply(Fixture(id), id), id, Table.Value.Commands);

    [Theory]
    [InlineData(136, 639998u, 6)] [InlineData(137, 639998u, 6)]
    [InlineData(138, 36000u, 0)] [InlineData(139, 714000u, 7)]
    public void TiersUseCurrentStatsAndAllPartsAbsorbHolyAndDark(int id, uint hp, int tier)
    {
        byte[] original = Fixture(id), result = SpiraSinAirshipStats.Apply(original, id);
        var a = Monster_File.Read(original).StatSheetFile; var b = Monster_File.Read(result).StatSheetFile;
        Assert.Equal(hp, b.Hp); Assert.Equal(a.Mp, b.Mp);
        Assert.Equal(tier == 0 ? a.Magic : SpiraNativeBossStats.ScaleAttribute(a.Magic, tier), b.Magic);
        var elements = Element_Flags.Holy | Element_Flags.Dark;
        Assert.Equal(elements, b.ElementalWeakness.Absorb & elements);
        Assert.Equal(0, (int)((b.ElementalWeakness.Weak | b.ElementalWeakness.Immune | b.ElementalWeakness.Resist) & elements));
        Assert.Equal(original.Length, result.Length);
        int start = BitConverter.ToInt32(original, 12), end = start + a.WriteSingle().Length;
        Assert.Equal(original.Take(start), result.Take(start)); Assert.Equal(original.Skip(end), result.Skip(end));
        Assert.Equal(result, Monster_File.Read(result).Write());
        Assert.Throws<InvalidOperationException>(() => SpiraSinAirshipStats.Apply(result, id));
        if (id == 138) Assert.Equal(1, original.Zip(result).Count(pair => pair.First != pair.Second));
    }

    [Fact]
    public void SpellCopiesPreserveOriginalPoolsDamageAndEffectsWithSafeBossMotion()
    {
        var rows = Ability_Command.ReadList(Table.Value.Bytes, false);
        Assert.Equal(435, rows.Count);
        for (int i = 0; i < SpiraSinAirshipWriter.Pool.Count; i++)
        {
            var recipe = SpiraSinAirshipWriter.Pool[i]; var source = rows[recipe.Original & 0xFFF]; var clone = rows[408 + i];
            Assert.Equal(source.AttackPower, clone.AttackPower); Assert.Equal(source.DamageFormula, clone.DamageFormula);
            Assert.Equal(source.Anim1Id, clone.Anim1Id); Assert.Equal(source.Anim2Id, clone.Anim2Id);
            Assert.Equal(3, clone.CasterAnimId); Assert.False(clone.FlagDamagePhysical);
            Assert.Equal(0, clone.StatusChance.Death); Assert.Equal(0, clone.StatusChance.Petrify);
        }
        Assert.Equal(22, Table.Value.Commands.Arms.Count);
        Assert.Equal(2, Table.Value.Commands.GenaisT5.Count); Assert.Equal(3, Table.Value.Commands.GenaisT6.Count);
        Assert.All(SpiraSinAirshipWriter.Pool.Where(s => s.Tier == 5), s => Assert.Equal((ushort)0xFFF2, s.Target));
        Assert.DoesNotContain(SpiraSinAirshipWriter.Pool, s => s.Name is "Sepulchral Avalanche" or "Condemnation Bell" or "Mountain Rend");
        Assert.Equal(Table.Value.Bytes, Ability_Command.WriteList(rows, false));
    }

    [Theory]
    [InlineData(136)] [InlineData(137)]
    public void ArmsKeepNativeAttackAndChooseExactlyTwoSpellsWithCorrectTargets(int id)
    {
        var s = AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(Build(id))!);
        foreach (int first in Enumerable.Range(0, 22))
            foreach (int second in Enumerable.Range(0, 22))
            {
                var trace = RunSite(s, 1, 0x6093, 0, first, second);
                Assert.Equal(3, trace.Count); Assert.Equal((ushort)0x6093, trace[0].Command);
                Assert.Equal(Table.Value.Commands.Arms[first].Command, trace[1].Command);
                Assert.Equal(Table.Value.Commands.Arms[second].Command, trace[2].Command);
                for (int i = 1; i <= 2; i++)
                {
                    var expected = Table.Value.Commands.Arms[i == 1 ? first : second].Target;
                    Assert.Equal(expected == 0xFFEF ? 1 : expected, trace[i].Target);
                }
            }
    }

    [Fact]
    public void TranslatedDonorsKeepTheirTextAndMismatchingPayloadIsRejected()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "EvraeHoly");
        byte[] english = File.ReadAllBytes(Path.Combine(root, "monmagic2.bin"));
        byte[] japanese = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "SinAirship", "monmagic2-jp.bin"));
        byte[] players = File.ReadAllBytes(Path.Combine(root, "command.bin"));
        var grown = MonsterMagicGrowWriter.AppendEvraeHoly(japanese, players,
            new (short, short)[] { (913, 914), (915, 916), (917, 918) },
            new short[] { 919, 920, 921, 922 }, new short[] { 923, 924, 925, 926 });
        var final = MonsterMagicGrowWriter.AppendSinAirshipSpells(grown.Bytes, english);
        var rows = Ability_Command.ReadList(final.Bytes, false);
        Assert.Equal(rows[267].NameScriptBytes, rows[408].NameScriptBytes);
        Assert.Equal(rows[267].DescriptionScriptBytes, rows[408].DescriptionScriptBytes);
        Assert.Equal(33, rows[267].CasterAnimId); Assert.Equal(3, rows[408].CasterAnimId);
        byte[] changed = grown.Bytes.ToArray(); changed[20 + 267 * 92 + 42] ^= 1;
        Assert.Throws<InvalidDataException>(() => MonsterMagicGrowWriter.AppendSinAirshipSpells(changed, english));
    }

    [Fact]
    public void GenaisAddsT5ThenT6OnlyOutsideShellAndPreservesPhaseStrengthScaling()
    {
        var s = AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(Build(139))!);
        foreach (ushort attack in new ushort[] { 0x6097, 0x6098 })
            for (int a = 0; a < 2; a++)
                for (int b = 0; b < 3; b++)
                {
                    var trace = RunSite(s, 0, attack, 1, a, b);
                    Assert.Equal(new[] { attack, Table.Value.Commands.GenaisT5[a].Command, Table.Value.Commands.GenaisT6[b].Command },
                        trace.Select(c => c.Command));
                    Assert.Single(RunSite(s, 0, attack, 0, a, b));
                }
        Assert.Single(RunSite(s, 0, 0x6096, 0, 0, 0));
        var aops = s.Instructions;
        var strengths = Enumerable.Range(1, aops.Count - 1).Where(i => aops[i].Opcode == 0xD8 && aops[i].Operand == 0x70A8)
            .Select(i => aops[i - 1].Operand).ToArray();
        Assert.Equal(new ushort[] { 34, 43 }, strengths);
        Assert.Throws<InvalidOperationException>(() => SpiraSinAirshipWriter.Build(Build(139), 139, Table.Value.Commands));
    }

    [Theory]
    [InlineData(136)] [InlineData(137)] [InlineData(139)]
    public void NewAiPreservesNativeRoutingAndNonAiTail(int id)
    {
        byte[] scaled = SpiraSinAirshipStats.Apply(Fixture(id), id), result = Build(id);
        int a = BitConverter.ToInt32(scaled, 8), b = BitConverter.ToInt32(result, 8);
        Assert.Equal(scaled.AsSpan(a).ToArray(), result.AsSpan(b).ToArray());
        var old = AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(scaled)!);
        var current = AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(result)!);
        Assert.Equal(old.Workers.Select(w => w.Entrypoints.Count), current.Workers.Select(w => w.Entrypoints.Count));
        Assert.True(AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(
            AiScript_File.SliceAiFileFromMonster(result)!, old, out _, out string error), error);
    }

    static List<(ushort Command, int Target)> RunSite(AiScriptFile s, int worker, ushort command, int shell, int first, int second)
    {
        var ops = s.Instructions;
        int call = Enumerable.Range(1, ops.Count - 1).First(i => ops[i].Opcode == 0xD8 && ops[i].Operand == 0x700B
            && ops[i - 1].Opcode == 0xAE && ops[i - 1].Operand == command);
        var stack = new Stack<int>(); stack.Push(0xFFF2); stack.Push(command);
        var vars = new Dictionary<int, int> { [1] = shell }; var rng = new Queue<int>(new[] { first, second });
        var commands = new List<(ushort, int)>(); var nodes = ops.ToDictionary(o => o.Offset - s.ScriptStart);
        int pc = ops[call].Offset - s.ScriptStart;
        int Pop() => stack.Pop();
        for (int budget = 0; budget < 10000; budget++)
        {
            var op = nodes[pc]; pc += op.Length;
            switch (op.Opcode)
            {
                case 0: break;
                case 0xAE: stack.Push(op.Operand); break;
                case 0x9F: stack.Push(vars.GetValueOrDefault(op.Operand)); break;
                case 0xA0: vars[op.Operand] = Pop(); break;
                case 0x06: { int b = Pop(), a = Pop(); stack.Push(a == b ? 1 : 0); break; }
                case 0x02: { int b = Pop(), a = Pop(); stack.Push(a != 0 && b != 0 ? 1 : 0); break; }
                case 0x14: { int b = Pop(), a = Pop(); stack.Push(a + b); break; }
                case 0x18: { int b = Pop(), a = Pop(); stack.Push(a % b); break; }
                case 0xD7: if (Pop() == 0) pc = s.Workers[worker].JumpTargets[op.Operand]; break;
                case 0xB0: case 0x3C: Assert.Empty(stack); return commands;
                case 0xB5 when op.Operand == 0x00A9: stack.Push(rng.Dequeue()); break;
                case 0xB5 when op.Operand == 0x700F: Assert.Equal(4, Pop()); Assert.Equal(0xFFF3, Pop()); stack.Push(1); break;
                case 0xB5 when op.Operand == 0x7010:
                    Assert.Equal(0, Pop()); Assert.Equal(0, Pop()); Assert.Equal(4, Pop()); Assert.Equal(0xFFF2, Pop()); stack.Push(1); break;
                case 0xD8 when op.Operand == 0x700B:
                    ushort skill = (ushort)Pop(); int target = Pop(); commands.Add((skill, target)); Assert.InRange(commands.Count, 1, 4); break;
                default: throw new InvalidOperationException($"Unhandled {op.Opcode:X2}/{op.Operand:X4} at {pc - op.Length:X}.");
            }
        }
        throw new InvalidOperationException("Sin sequence did not terminate.");
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Monster;
using Xunit;

namespace FFXProjectEditor.Tests.FfxLib.Ai;

public sealed class SpiraEvraeTests
{
    static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "EvraeHoly", name));
    static EvraeHolyTable Table() => MonsterMagicGrowWriter.AppendEvraeHoly(Fixture("monmagic2.bin"), Fixture("command.bin"),
        new (short, short)[] { (913, 914), (915, 916), (917, 918) },
        new short[] { 919, 920, 921, 922 }, new short[] { 923, 924, 925, 926 });
    static readonly Lazy<EvraeHolyTable> Spells = new(Table);
    static readonly Lazy<byte[]> Monster = new(() => SpiraEvraeWriter.Build(Fixture("original-m119.bin"), Fixture("m119.bin"), Spells.Value.Commands));
    static readonly Lazy<AiScriptFile> Script = new(() => AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(Monster.Value)!));

    [Fact]
    public void AppendPreservesAll385RowsAndTextAndUsesNativeFuryHitCounts()
    {
        var old = EntryListFile.Unpack(Fixture("monmagic2.bin")); var current = EntryListFile.Unpack(Spells.Value.Bytes);
        Assert.Equal(408, current.Header.RealEntryCount);
        Assert.Equal(old.FirstFile, current.FirstFile.Take(old.FirstFile.Length));
        Assert.Equal(old.SecondFile, current.SecondFile.Take(old.SecondFile.Length));
        var rows = Ability_Command.ReadList(Spells.Value.Bytes, false);
        foreach (ushort command in Spells.Value.Commands.Singles)
        {
            var c = rows[command & 0xFFF];
            Assert.Equal(24, c.AttackPower); Assert.Equal(1, c.HitCount);
            Assert.Equal(Ability_Command.ElementFlags.Holy, c.ElementFlgs);
        }
        for (int i = 0; i < 16; i++)
        {
            var c = rows[Spells.Value.Commands.RaFuries[i] & 0xFFF];
            Assert.Equal(3 + i % 4, c.HitCount); Assert.Equal(24, c.AttackPower);
            Assert.True(c.FlagMisc2RandomTargets); Assert.Equal((Ability_Command.TargetFlags)0x87, c.TargetFlgs);
            Assert.Equal(Ability_Command.ElementFlags.Holy, c.ElementFlgs);
            Assert.Equal(0, c.CostOverdrive); Assert.Equal(0, c.Anim2Id);
        }
        foreach (ushort command in Spells.Value.Commands.BasicFuries)
        {
            var c = rows[command & 0xFFF]; Assert.Equal(6, c.HitCount); Assert.Equal(12, c.AttackPower);
            Assert.True(c.FlagMisc2RandomTargets); Assert.Equal(Ability_Command.ElementFlags.Holy, c.ElementFlgs);
        }
        Assert.Equal(Spells.Value.Bytes, Ability_Command.WriteList(rows, false));
        Assert.Throws<InvalidDataException>(() => MonsterMagicGrowWriter.AppendEvraeHoly(Spells.Value.Bytes, Fixture("command.bin"),
            new (short, short)[] { (913, 914), (915, 916), (917, 918) },
            new short[] { 919, 920, 921, 922 }, new short[] { 923, 924, 925, 926 }));
    }

    [Fact]
    public void HasteChoosesOneToFourUniqueSpellsAcrossEveryPermutationWithoutOverflow()
    {
        for (int count = 1; count <= 4; count++)
            for (int permutation = 0; permutation < 24; permutation++)
            {
                var t = new Trace(); Site(t, 0x3036, count - 1, permutation);
                Assert.Single(t.Commands); Assert.Equal((ushort)0x3036, t.Commands[0].Skill);
                Continue(t);
                var spells = t.Commands.Skip(1).Select(c => c.Skill).ToArray();
                Assert.Equal(count, spells.Length); Assert.Equal(count, spells.Distinct().Count());
                Assert.All(spells, c => Assert.Contains(c, Spells.Value.Commands.Singles));
                Assert.Equal(0, t.Dropped); Assert.All(t.Commands.Skip(1), c => Assert.InRange(c.Target, 0, 2));
                Continue(t); Assert.Equal(count + 1, t.Commands.Count);
            }
    }

    [Fact]
    public void ReflectAlwaysReplacesHasteWithExactlyOneThreeToSixHitFury()
    {
        for (int choice = 0; choice < 16; choice++)
        {
            var t = new Trace { Reflect = 254 }; Site(t, 0x3036, choice); Continue(t);
            Assert.Single(t.Commands); Assert.Equal(Spells.Value.Commands.RaFuries[choice], t.Commands[0].Skill);
            Assert.Equal(0xFFF2, t.Commands[0].Target); Assert.Equal(0, t.Dropped);
        }
    }

    [Fact]
    public void ScytheAlwaysAddsOneBasicFuryAndPhysicalAttackUsesDifferentLivingTarget()
    {
        for (int choice = 0; choice < 4; choice++)
        {
            var t = new Trace(); Site(t, 0x605B, choice);
            Assert.Equal(new ushort[] { 0x605B, Spells.Value.Commands.BasicFuries[choice] }, t.Commands.Select(c => c.Skill));
            Assert.Equal(0, t.Dropped);
        }
        var two = new Trace(); Site(two, 0x407F, 0);
        Assert.Equal(2, two.Commands.Count); Assert.NotEqual(two.Commands[0].Target, two.Commands[1].Target);
        var one = new Trace { Alive = new[] { 0 } }; Site(one, 0x407F, 0); Assert.Single(one.Commands);
        var dead = new Trace(); Site(dead, 0x3036, 3, 0); dead.Alive = Array.Empty<int>(); Continue(dead);
        Assert.Single(dead.Commands);
    }

    [Fact]
    public void RoutingGrowthPreservesNativeTargetsT4StatsAndAllNonAiBytesExceptPurposeSix()
    {
        byte[] before = Fixture("m119.bin"), after = Monster.Value;
        int a = BitConverter.ToInt32(before, 8), b = BitConverter.ToInt32(after, 8);
        var changes = before.AsSpan(a).ToArray().Zip(after.AsSpan(b).ToArray()).Count(p => p.First != p.Second);
        Assert.Equal(2, changes);
        Assert.Equal(7, Script.Value.Workers[1].Entrypoints.Count);
        Assert.Equal(Monster_File.Read(before).StatSheetFile.WriteSingle(), Monster_File.Read(after).StatSheetFile.WriteSingle());
        var old = AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(Fixture("original-m119.bin"))!);
        Assert.True(AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(
            AiScript_File.SliceAiFileFromMonster(after)!, old, out _, out string error), error);
        Assert.Throws<InvalidOperationException>(() => SpiraEvraeWriter.Build(Fixture("original-m119.bin"), after, Spells.Value.Commands));
    }

    sealed class Trace
    {
        public int Reflect, Pending, Dropped, Pick;
        public int[] Alive = { 0, 1, 2 };
        public Dictionary<int, int> Vars = new();
        public Queue<int> Rng = new();
        public Stack<int> Stack = new();
        public List<(int Target, ushort Skill)> Commands = new();
    }
    static void Site(Trace t, ushort command, params int[] random)
    {
        var s = Script.Value; var a = s.Instructions;
        int i = Enumerable.Range(1, a.Count - 1).First(i => a[i].Opcode == 0xA0 && a[i].Operand >= 13
            && a[i - 1].Opcode == 0xAE && a[i - 1].Operand == command);
        t.Stack.Push(command == 0x3036 ? 20 : 0); t.Stack.Push(command); t.Pending = 0;
        foreach (int value in random) t.Rng.Enqueue(value);
        Run(t, a[i].Offset - s.ScriptStart);
    }
    static void Continue(Trace t) { t.Pending = 0; Run(t, Script.Value.Workers[1].Entrypoints[6]); }
    static void Run(Trace t, int pc)
    {
        var s = Script.Value; var nodes = s.Instructions.ToDictionary(o => o.Offset - s.ScriptStart);
        int Pop() => t.Stack.Pop();
        int Actor(int a) => a is >= 0xFFF8 and <= 0xFFFA ? 0xFFFA - a : a;
        for (int budget = 0; budget < 50000; budget++)
        {
            var op = nodes[pc]; pc += op.Length;
            switch (op.Opcode)
            {
                case 0: break;
                case 0xAE: t.Stack.Push(op.Operand); break;
                case 0x9F: t.Stack.Push(t.Vars.GetValueOrDefault(op.Operand)); break;
                case 0xA0: t.Vars[op.Operand] = Pop(); break;
                case 0x01: { int b = Pop(), a = Pop(); t.Stack.Push(a != 0 || b != 0 ? 1 : 0); break; }
                case 0x02: { int b = Pop(), a = Pop(); t.Stack.Push(a != 0 && b != 0 ? 1 : 0); break; }
                case 0x06: { int b = Pop(), a = Pop(); t.Stack.Push(a == b ? 1 : 0); break; }
                case 0x0B: { int b = Pop(), a = Pop(); t.Stack.Push(a < b ? 1 : 0); break; }
                case 0x14: { int b = Pop(), a = Pop(); t.Stack.Push(a + b); break; }
                case 0x15: { int b = Pop(), a = Pop(); t.Stack.Push(a - b); break; }
                case 0x18: { int b = Pop(), a = Pop(); t.Stack.Push(a % b); break; }
                case 0xB0: pc = s.Workers[1].JumpTargets[op.Operand]; break;
                case 0xD7: if (Pop() == 0) pc = s.Workers[1].JumpTargets[op.Operand]; break;
                case 0x3C: Assert.Empty(t.Stack); return;
                case 0xB5 when op.Operand == 0x00A9: t.Stack.Push(t.Rng.Count == 0 ? 0 : t.Rng.Dequeue()); break;
                case 0xB5 when op.Operand == 0x700F:
                {
                    int field = Pop(), actor = Actor(Pop());
                    t.Stack.Push(field switch { 0x32 => t.Reflect, 4 => actor is 20 or 0xFFF3 || t.Alive.Contains(actor) ? 1 : 0, _ => 0 }); break;
                }
                case 0xB5 when op.Operand == 0x701E: { int b = Actor(Pop()), a = Actor(Pop()); t.Stack.Push(a == b ? 1 : 0); break; }
                case 0xB5 when op.Operand == 0x7010:
                    Assert.Equal(0, Pop()); Assert.Equal(0, Pop()); Assert.Equal(4, Pop()); Assert.Equal(0xFFF2, Pop());
                    t.Stack.Push(t.Alive[t.Pick++ % t.Alive.Length]); break;
                case 0xD8 when op.Operand == 0x700B:
                {
                    ushort skill = (ushort)Pop(); int target = Actor(Pop());
                    if (t.Pending == 4) t.Dropped++; else { t.Pending++; t.Commands.Add((target, skill)); } break;
                }
                case 0xD8 when op.Operand == 0x7018: Pop(); Pop(); Pop(); break;
                case 0xD8 when op.Operand is 0x7037 or 0x7038: Pop(); Pop(); break;
                default: throw new InvalidOperationException($"Unhandled {op.Opcode:X2}/{op.Operand:X4} at {pc - op.Length:X}.");
            }
        }
        throw new InvalidOperationException("Evrae path did not terminate.");
    }
}

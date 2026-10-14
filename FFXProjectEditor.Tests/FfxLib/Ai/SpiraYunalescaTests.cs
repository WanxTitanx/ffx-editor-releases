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

public sealed class SpiraYunalescaTests
{
    static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Yunalesca", name));
    static readonly YunalescaCommands Skills = new(
        Enumerable.Range(0x6165, 8).Select(i => (ushort)i).ToArray(),
        Enumerable.Range(0, 4).SelectMany(i => new[] { 0x616D + i, 0x6175 + i * 3, 0x6176 + i * 3, 0x6177 + i * 3 })
            .Select(i => (ushort)i).ToArray(), 0x6157, 0x6174, 0x6171, 0x6172, 0x6173);
    static byte[] Deployed(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "YunalescaFuryFix", name));
    static readonly Lazy<IReadOnlyList<Ability_Command>> RepairedRows = new(() =>
        Ability_Command.ReadList(MonsterMagicGrowWriter.RepairYunalescaFury(Deployed("monmagic2.bin"), Fixture("command.bin"),
            new short[] { 909, 910, 911, 912 }).Bytes, false));
    static byte[] Build() => SpiraYunalescaWriter.Build(Fixture("m130.bin"), Skills);

    [Fact]
    public void T7UsesCurrentStatsAndNativeRewardWordsWithoutTouchingOtherSections()
    {
        byte[] original = Fixture("m130.bin"), saved = original.ToArray();
        var old = Monster_File.Read(original); byte[] result = SpiraYunalescaStats.Apply(original);
        var current = Monster_File.Read(result);
        Assert.Equal(saved, original); Assert.Equal(original.Length, result.Length);
        Assert.Equal(850000u, current.StatSheetFile.Hp); Assert.Equal(old.StatSheetFile.Mp, current.StatSheetFile.Mp);
        Assert.Equal(61, current.StatSheetFile.Magic); Assert.Equal(108, current.StatSheetFile.Defense);
        Assert.Equal(101, current.StatSheetFile.Agility);
        Assert.Equal(Element_Flags.Dark, current.StatSheetFile.ElementalWeakness.Immune);
        Assert.Equal(Element_Flags.Holy, current.StatSheetFile.ElementalWeakness.Weak);
        Assert.Equal((ushort)60000, current.LootFile.Ap); Assert.Equal(ushort.MaxValue, current.LootFile.ApOverkill);
        Assert.Equal((ushort)60000, current.LootFile.Gil);
        int stats = BitConverter.ToInt32(original, 12), loot = BitConverter.ToInt32(original, 20);
        var allowed = Enumerable.Range(stats, loot - stats).Concat(Enumerable.Range(loot, 6)).ToHashSet();
        for (int i = 0; i < original.Length; i++) if (!allowed.Contains(i)) Assert.Equal(original[i], result[i]);
        Assert.Equal(result, current.Write());
    }

    [Fact]
    public void SixteenSkillsPreserveOldRowsAndTextsAndContainOnlyRequestedStatuses()
    {
        byte[] original = Fixture("monmagic2.bin"), grown = original, characters = Fixture("command.bin");
        foreach (var recipe in MonsterMagicGrowWriter.YunalescaSpells)
            grown = MonsterMagicGrowWriter.AppendYunalescaSpell(grown, characters, recipe, 903, 904).Bytes;
        var before = EntryListFile.Unpack(original); var after = EntryListFile.Unpack(grown);
        Assert.Equal(before.FirstFile, after.FirstFile.Take(before.FirstFile.Length));
        Assert.Equal(before.SecondFile, after.SecondFile.Take(before.SecondFile.Length));
        var rows = Ability_Command.ReadList(grown, false);
        Assert.Equal(373, rows.Count); Assert.Equal(grown, Ability_Command.WriteList(rows, false));
        foreach (var c in rows.Skip(357).Take(8))
        {
            Assert.Equal(0, c.AttackPower); Assert.Equal(0, c.StatusChance.Death); Assert.Equal(0, c.StatusChance.Petrify);
            Assert.False(c.FlagStatusEject); Assert.False(c.FlagStatusDoom);
        }
        Assert.True(rows[363].FlagMisc2DelayS); Assert.True(rows[364].FlagStatusCurse);
        Assert.All(rows.Skip(365).Take(4), c => { Assert.Equal(18, c.AttackPower); Assert.Equal(Ability_Command.ElementFlags.Dark, c.ElementFlgs); });
        Assert.Equal(60, rows[372].AttackPower); Assert.Equal(Ability_Command.ElementFlags.Dark, rows[372].ElementFlgs);
        Assert.Equal(DamageFormula_Enum.WielderMaxHp, rows[369].DamageFormula); Assert.Equal(1, rows[369].AttackPower);
        Assert.Equal(255, rows[369].StatusChance.Regen); Assert.True(rows[369].FlagDamageHeals);
        Assert.Equal(2, rows[371].AttackPower); Assert.Equal(255, rows[371].StatusChance.Haste);
        Assert.Equal(254, rows[369].StatusDuration.Regen);
        Assert.Equal(254, rows[371].StatusDuration.Regen); Assert.Equal(254, rows[371].StatusDuration.Haste);
        Assert.Equal(0, rows[370].StatusChance.Haste);
        foreach (var c in new[] { rows[370], rows[371] })
        {
            Assert.Equal(255, c.StatusChance.Protect); Assert.Equal(255, c.StatusChance.Shell);
            Assert.Equal(255, c.StatusChance.NulBlaze); Assert.Equal(255, c.StatusChance.NulFrost);
            Assert.Equal(255, c.StatusChance.NulTide); Assert.Equal(255, c.StatusChance.NulShock);
            Assert.Equal(254, c.StatusDuration.Protect); Assert.Equal(254, c.StatusDuration.Shell);
            Assert.Equal(254, c.StatusDuration.NulBlaze); Assert.Equal(254, c.StatusDuration.NulFrost);
            Assert.Equal(254, c.StatusDuration.NulTide); Assert.Equal(254, c.StatusDuration.NulShock);
        }
    }

    [Fact]
    public void NativeRoutingAndMorphHpRatiosSurviveAndPhaseStrengthActuallyScales()
    {
        var old = AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(Fixture("m130.bin"))!);
        byte[] result = Build(); var current = AiScript_File.Read(result);
        Assert.Equal(old.Workers.Count, current.Workers.Count);
        Assert.Equal(old.Workers.Select(w => w.Entrypoints.Count), current.Workers.Select(w => w.Entrypoints.Count));
        Assert.True(AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(result, old, out _, out string error), error);
        var instructions = current.Instructions;
        var strengths = Enumerable.Range(3, instructions.Count - 3).Where(i => instructions[i].Opcode == 0xD8
            && instructions[i].Operand == 0x70A8 && instructions[i - 2].Operand == 9)
            .Select(i => instructions[i - 1].Operand);
        Assert.Equal(new ushort[] { 13, 61, 61 }, strengths);
        Assert.Equal(old.Instructions.Count(i => i.Opcode == 0xD8 && i.Operand == 0x7097),
            instructions.Count(i => i.Opcode == 0xD8 && i.Operand == 0x7097));
        Assert.Throws<InvalidOperationException>(() => SpiraYunalescaWriter.Build(
            AiScript_File.SpliceAiFileIntoMonsterGrow(Fixture("m130.bin"), result), Skills));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void FormOneNormalTurnAddsOneNonlethalDebuffOnly(int choice)
    {
        var t = Turn(0, choice);
        Assert.Equal(2, t.Commands.Count);
        Assert.Equal(Skills.Debuffs[choice], t.Commands[1].Skill);
        Assert.InRange(t.Commands[1].Target, 0, 2);
        var counter = Hit(0, 1, 100000, 99);
        Assert.DoesNotContain(counter.Commands, c => Skills.Debuffs.Contains(c.Skill));
    }

    [Theory]
    [InlineData(0, 0, 3)] [InlineData(1, 1, 4)] [InlineData(2, 2, 5)] [InlineData(3, 3, 6)]
    public void FormTwoOneFuryChoiceHasThreeToSixIndependentTargets(int spell, int roll, int expected)
    {
        var t = Turn(1, spell * 4 + roll);
        var fury = Assert.Single(t.Commands.Where(c => Skills.Furies.Contains(c.Skill)));
        Assert.Equal(Skills.Furies[spell * 4 + roll], fury.Skill);
        Assert.Equal(0xFFF2, fury.Target);
        var row = RepairedRows.Value[fury.Skill & 0xFFF];
        Assert.Equal(expected, row.HitCount);
        Assert.True(row.FlagMisc2RandomTargets);
        Assert.Equal(0x87, (byte)row.TargetFlgs);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void FourthFuryAddsOneSupportAndResetsCycle(int support)
    {
        var t = new Trace { Phase = 1 };
        Turn(t, 0); Turn(t, 0); Turn(t, 0);
        Assert.DoesNotContain(t.Commands, c => c.Skill == Skills.DarkWind || c.Skill == Skills.DarkMightGuard);
        Turn(t, 0, support);
        Assert.Equal(1, t.Commands.Count(c => c.Skill == Skills.DarkWind || c.Skill == Skills.DarkMightGuard));
        Assert.Equal(support == 0 ? Skills.DarkWind : Skills.DarkMightGuard, t.Commands.Last().Skill);
        Turn(t, 0);
        Assert.Equal(1, t.Commands.Count(c => c.Skill == Skills.DarkWind || c.Skill == Skills.DarkMightGuard));
    }

    [Fact]
    public void DeployedRecastPlanReproducesTwoAcceptedFuriesAndLostSupportAtNativeCapacity()
    {
        var old = AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(Deployed("m130.bin"))!);
        var t = new Trace { Phase = 1, PreludeCommands = 1 };
        t.Vars[14] = 2; // Old cycle was about to reach its third sequence.
        TurnScript(old, t, 0, 0, 0);
        Assert.Equal(2, t.Commands.Count(c => Skills.Furies.Contains(c.Skill)));
        Assert.DoesNotContain(t.Commands, c => c.Skill == Skills.DarkWind || c.Skill == Skills.DarkMightGuard);
        Assert.Equal(2, t.Dropped);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void FixedFuryAndFourthSupportFitIncludingNativeMorphPrelude(int hitVariant)
    {
        var t = new Trace { Phase = 1, PreludeCommands = 1 };
        for (int i = 0; i < 3; i++) Turn(t, hitVariant);
        Turn(t, hitVariant, 0);
        Assert.Equal(0, t.Dropped);
        Assert.Equal(4, t.Commands.Count(c => Skills.Furies.Contains(c.Skill)));
        Assert.Equal(Skills.DarkWind, t.Commands.Last().Skill);
        Assert.All(t.Commands.Where(c => Skills.Furies.Contains(c.Skill)),
            c => Assert.Equal(3 + hitVariant, RepairedRows.Value[c.Skill & 0xFFF].HitCount));
    }

    [Fact]
    public void RepairOnlyChangesSevenOwnedRowsAndAppendsTwelveNativeHitVariants()
    {
        byte[] input = Deployed("monmagic2.bin"), saved = input.ToArray();
        var result = MonsterMagicGrowWriter.RepairYunalescaFury(input, Fixture("command.bin"), new short[] { 909, 910, 911, 912 });
        var old = EntryListFile.Unpack(input); var current = EntryListFile.Unpack(result.Bytes);
        Assert.Equal(saved, input); Assert.Equal(385, current.Header.RealEntryCount);
        Assert.Equal(old.SecondFile, current.SecondFile.Take(old.SecondFile.Length));
        Assert.Equal(Skills.Furies, result.FuryOperands);
        var changed = new HashSet<int> { 365, 366, 367, 368, 369, 370, 371 };
        for (int i = 0; i < 373; i++) if (!changed.Contains(i))
            Assert.Equal(old.FirstFile.AsSpan(i * 92, 92).ToArray(), current.FirstFile.AsSpan(i * 92, 92).ToArray());
        foreach (ushort op in Skills.Furies)
        {
            var c = RepairedRows.Value[op & 0xFFF];
            Assert.InRange(c.HitCount, (byte)3, (byte)6); Assert.Equal(18, c.AttackPower);
            Assert.Equal(Ability_Command.ElementFlags.Dark, c.ElementFlgs); Assert.True(c.FlagMisc2RandomTargets);
            Assert.Equal(0, c.CostOverdrive); Assert.Equal(0, c.CostMp); Assert.Equal(0, c.Anim2Id);
        }
        Assert.Throws<InvalidDataException>(() => MonsterMagicGrowWriter.RepairYunalescaFury(result.Bytes,
            Fixture("command.bin"), new short[] { 909, 910, 911, 912 }));
    }

    [Fact]
    public void RepairedSupportHealsTenAndTwentyPercentAndKeepsBuffsDispellable()
    {
        var rows = RepairedRows.Value;
        foreach (int id in new[] { 369, 371 })
        {
            Assert.Equal(DamageFormula_Enum.WielderMaxHp, rows[id].DamageFormula);
            Assert.Equal(id == 369 ? 1 : 2, rows[id].AttackPower);
            Assert.True(rows[id].FlagDamageHeals);
            Assert.Equal(0, (int)rows[id].ElementFlgs);
            Assert.Equal(254, rows[id].StatusDuration.Regen);
        }
        Assert.Equal(254, rows[371].StatusDuration.Haste);
        foreach (int id in new[] { 370, 371 })
        {
            var d = rows[id].StatusDuration;
            Assert.All(new[] { d.Protect, d.Shell, d.NulBlaze, d.NulFrost, d.NulShock, d.NulTide },
                duration => Assert.Equal(254, duration));
        }
        var second = new Trace { Phase = 1, Kind = 2, Hp = 25000 };
        Hit(second, 99); Assert.Equal(35000, second.Hp);
        var third = new Trace { Phase = 2, Kind = 1, Hp = 50000 };
        Hit(third, 99); Assert.Equal(60000, third.Hp);
        third.Hp = 25000; Hit(third, 99); Assert.Equal(45000, third.Hp);
    }

    [Fact]
    public void MagicCounterProbabilitiesAreExactlyFiftyAndFifteenOverHundredRolls()
    {
        int ga = 0, ja = 0, normal = 0;
        for (int r = 0; r < 100; r++)
        {
            var second = Hit(1, 2, 100000, r); ga += second.Commands.Count(c => c.Skill == Skills.Darkga);
            Assert.DoesNotContain(second.Commands, c => c.Skill == 0x607C);
            var third = Hit(2, 1, 100000, r); ja += third.Commands.Count(c => c.Skill == Skills.Darkja);
            normal += third.Commands.Count(c => c.Skill == 0x607C);
        }
        Assert.Equal(50, ga); Assert.Equal(15, ja); Assert.Equal(85, normal);
    }

    [Fact]
    public void HpGatesAreOncePerFormExactAndKeepAttackerAcrossNestedSelfCallbacks()
    {
        var second = new Trace { Phase = 1, Kind = 2, Hp = 25000 };
        Hit(second, 0); Hit(second, 0);
        Assert.Equal(1, second.Commands.Count(c => c.Skill == Skills.DarkWind));
        Assert.All(second.Commands.Where(c => c.Skill == Skills.Darkga), c => Assert.Equal(0, c.Target));
        var third = new Trace { Phase = 2, Kind = 1, Hp = 20000 };
        Hit(third, 99); Hit(third, 99);
        Assert.Equal(1, third.Commands.Count(c => c.Skill == Skills.DarkMightGuard));
        Assert.Equal(1, third.Commands.Count(c => c.Skill == Skills.DarkWind));
        Assert.Equal(1, third.Commands.Count(c => c.Skill == Skills.HyperDarkGuard));
        Assert.All(third.Commands.Where(c => c.Skill == 0x607C), c => Assert.Equal(0, c.Target));
        Assert.DoesNotContain(Hit(2, 1, 75001, 99).Commands, c => c.Skill == Skills.DarkMightGuard);
        Assert.Contains(Hit(2, 1, 75000, 99).Commands, c => c.Skill == Skills.DarkMightGuard);
    }

    [Fact]
    public void CounterGateAndZeroDamageDoNotProduceDarkja()
    {
        var t = new Trace { Phase = 2, CounterAllowed = 0 }; Hit(t, 0);
        Assert.Empty(t.Commands);
        t = new Trace { Phase = 2, Damage = 0 }; Hit(t, 0);
        Assert.DoesNotContain(t.Commands, c => c.Skill == Skills.Darkja);
    }

    static Trace Turn(int phase, params int[] rng) { var t = new Trace { Phase = phase }; Turn(t, rng); return t; }
    static Trace Hit(int phase, int kind, int hp, params int[] rng)
    { var t = new Trace { Phase = phase, Kind = kind, Hp = hp }; Hit(t, rng); return t; }
    sealed class Trace
    {
        public int Phase, Kind = 1, Hp = 100000, MaxHp = 100000, Damage = 100, CounterAllowed = 1, LastAttacker;
        public Dictionary<int, int> Vars = new();
        public List<(int Target, ushort Skill)> Commands = new();
        public Stack<int> Stack = new();
        public Queue<int> Rng = new();
        public int TargetPick, Switch, PreludeCommands, PendingCommands, Dropped;
    }
    static void Turn(Trace t, params int[] rng)
    {
        TurnScript(AiScript_File.Read(Build()), t, rng);
    }
    static void TurnScript(AiScriptFile s, Trace t, params int[] rng)
    {
        var a = s.Instructions;
        int start = Enumerable.Range(0, a.Count - 7).Last(i => a[i].Opcode == 0xA0 && a[i + 1].Opcode == 0xA0
            && a[i + 2].Opcode == 0x9F && a[i + 2].Operand == 1 && a[i + 3].Opcode == 0xA0
            && a[i + 6].Opcode == 0xD8 && a[i + 6].Operand == 0x700B);
        t.Stack.Push(0); t.Stack.Push(0x606D);
        t.PendingCommands = t.PreludeCommands;
        Run(s, a[start].Offset - s.ScriptStart, t, rng);
    }
    static void Hit(Trace t, params int[] rng)
    {
        var s = AiScript_File.Read(Build()); var a = s.Instructions;
        int start = Enumerable.Range(0, a.Count - 4).Single(i => a[i].Opcode == 0x9F && a[i].Operand == 10
            && a[i + 1].Opcode == 0xA0 && a[i + 1].Operand >= 14
            && a[i + 2].Opcode == 0x9F && a[i + 2].Operand == 5 && a[i + 3].Opcode == 0xA0);
        t.LastAttacker = 0; t.Vars[5] = 0; t.Vars[10] = t.Kind;
        t.PendingCommands = 0;
        Run(s, a[start].Offset - s.ScriptStart, t, rng);
    }
    // Real rebuilt ATEL paths with bounded mocked native BIFs. This does not
    // establish gameplay, CTB timings or native rendering acceptance.
    static void Run(AiScriptFile s, int pc, Trace t, int[] rng)
    {
        t.Vars[1] = t.Phase; foreach (int r in rng) t.Rng.Enqueue(r);
        var nodes = s.Instructions.ToDictionary(i => i.Offset - s.ScriptStart); var w = s.Workers[1];
        int Pop() => t.Stack.Pop();
        for (int budget = 0; budget < 20000; budget++)
        {
            var op = nodes[pc]; pc += op.Length;
            switch (op.Opcode)
            {
                case 0x00: break;
                case 0xAE: t.Stack.Push(op.Operand); break;
                case 0x9F: t.Stack.Push(t.Vars.GetValueOrDefault(op.Operand)); break;
                case 0xA0: t.Vars[op.Operand] = Pop(); break;
                case 0x01: { int b = Pop(), a = Pop(); t.Stack.Push(a != 0 || b != 0 ? 1 : 0); break; }
                case 0x02: { int b = Pop(), a = Pop(); t.Stack.Push(a != 0 && b != 0 ? 1 : 0); break; }
                case 0x06: { int b = Pop(), a = Pop(); t.Stack.Push(a == b ? 1 : 0); break; }
                case 0x0B: { int b = Pop(), a = Pop(); t.Stack.Push(a < b ? 1 : 0); break; }
                case 0x0F: { int b = Pop(), a = Pop(); t.Stack.Push(a <= b ? 1 : 0); break; }
                case 0x14: { int b = Pop(), a = Pop(); t.Stack.Push(a + b); break; }
                case 0x16: { int b = Pop(), a = Pop(); t.Stack.Push(a * b); break; }
                case 0x18: { int b = Pop(), a = Pop(); t.Stack.Push(a % b); break; }
                case 0x2C: t.Switch = Pop(); break;
                case 0x29: t.Stack.Push(t.Switch); break;
                case 0xB0: pc = w.JumpTargets[op.Operand]; break;
                case 0xD6: if (Pop() != 0) pc = w.JumpTargets[op.Operand]; break;
                case 0xD7: if (Pop() == 0) pc = w.JumpTargets[op.Operand]; break;
                case 0x3C: Assert.Empty(t.Stack); return;
                case 0xB5 when op.Operand == 0x00A9: t.Stack.Push(t.Rng.Count == 0 ? 99 : t.Rng.Dequeue()); break;
                case 0xB5 when op.Operand == 0x70E0: t.Stack.Push(t.CounterAllowed); break;
                case 0xB5 when op.Operand == 0x700F:
                {
                    int field = Pop(), actor = Pop(); t.Stack.Push(field switch
                    { 0 when actor is 0xFFF3 or 20 => t.Hp, 2 => t.MaxHp, 4 => 1, 0xA6 => t.Damage, _ => 0 }); break;
                }
                case 0xB5 when op.Operand == 0x7010:
                    Assert.Equal(0, Pop()); Assert.Equal(0, Pop()); Assert.Equal(4, Pop()); Assert.Equal(0xFFF2, Pop());
                    t.Stack.Push(t.TargetPick++ % 3); break;
                case 0xD8 when op.Operand == 0x700B:
                {
                    ushort skill = (ushort)Pop(); int target = Pop(); if (target == 0xFFEF) target = t.LastAttacker;
                    // Steam 25501027 VA 0x7AC9EA rejects packet entries at count 4.
                    if (t.PendingCommands >= 4) { t.Dropped++; break; }
                    t.PendingCommands++;
                    t.Commands.Add((target, skill));
                    if (skill == Skills.DarkWind || skill == Skills.HyperDarkGuard)
                    {
                        var ability = RepairedRows.Value[skill & 0xFFF];
                        Assert.Equal(DamageFormula_Enum.WielderMaxHp, ability.DamageFormula);
                        t.Hp = Math.Min(t.MaxHp, t.Hp + t.MaxHp * ability.AttackPower / 10);
                    }
                    if (skill == Skills.DarkWind || skill == Skills.HyperDarkGuard || skill == Skills.DarkMightGuard)
                    { t.Vars[10] = 2; t.Vars[5] = 20; t.LastAttacker = 20; }
                    break;
                }
                default: throw new InvalidOperationException($"Unsupported primitive {op.Opcode:X2}/{op.Operand:X4} at {pc - op.Length}.");
            }
        }
        throw new InvalidOperationException("Yunalesca event failed to terminate.");
    }
}

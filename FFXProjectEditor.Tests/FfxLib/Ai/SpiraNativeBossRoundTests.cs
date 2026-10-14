using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.FfxLib.Monster;
using FFXProjectEditor.FfxLib.Ps3;
using Xunit;

namespace FFXProjectEditor.Tests.FfxLib.Ai;

public sealed class SpiraNativeBossRoundTests
{
    static readonly NativeBossCommands Commands = new(0x6156, 0x6157, 0x6158, 0x6159, 0x615A,
        0x615B, 0x615C, 0x615D, 0x615E, 0x615F);
    static byte[] Corpus(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NativeBosses", name));

    [Theory]
    [InlineData(124, 16800u)]
    [InlineData(212, 41150u)]
    [InlineData(119, 67200u)]
    [InlineData(126, 108000u)]
    [InlineData(142, 336000u)]
    public void RealBosses_ScaleCurrentStatsAndPreserveEveryOtherSection(int id, uint hp)
    {
        byte[] source = Corpus($"m{id:D3}.bin"), copy = (byte[])source.Clone();
        var old = Monster_File.Read(source);
        byte[] scaled = SpiraNativeBossStats.Apply(source, id);
        var current = Monster_File.Read(scaled);
        int tier = SpiraNativeBossStats.TierFor(id);
        Assert.Equal(hp, current.StatSheetFile.Hp);
        Assert.Equal(old.StatSheetFile.Mp, current.StatSheetFile.Mp);
        Assert.Equal(SpiraNativeBossStats.ScaleHp(old.StatSheetFile.HpOverkill, tier), current.StatSheetFile.HpOverkill);
        byte[] before = Attributes(old.StatSheetFile), after = Attributes(current.StatSheetFile);
        Assert.Equal(before.Select(value => SpiraNativeBossStats.ScaleAttribute(value, tier)), after);
        Assert.Equal(copy, source);
        Assert.Equal(source.Length, scaled.Length);
        int stats = BitConverter.ToInt32(source, 12), tail = BitConverter.ToInt32(source, 20);
        Assert.Equal(source[..stats], scaled[..stats]);
        Assert.Equal(source[tail..], scaled[tail..]);
        Assert.Equal(scaled, current.Write());
    }

    [Theory]
    [InlineData(124, Element_Flags.Dark, (Element_Flags)0)]
    [InlineData(126, Element_Flags.Dark, Element_Flags.Holy)]
    [InlineData(142, Element_Flags.Dark, Element_Flags.Holy)]
    [InlineData(119, Element_Flags.Holy, Element_Flags.Dark)]
    public void Affinities_AreExclusiveAndLeaveUnrelatedElementsAlone(int id, Element_Flags absorb, Element_Flags weak)
    {
        var old = Monster_File.Read(Corpus($"m{id:D3}.bin")).StatSheetFile.ElementalWeakness;
        var result = Monster_File.Read(SpiraNativeBossStats.Apply(Corpus($"m{id:D3}.bin"), id)).StatSheetFile.ElementalWeakness;
        Assert.True((result.Absorb & absorb) != 0);
        Assert.Equal(0, (int)((result.Immune | result.Resist | result.Weak) & absorb));
        if (weak != 0)
        {
            Assert.True((result.Weak & weak) != 0);
            Assert.Equal(0, (int)((result.Absorb | result.Immune | result.Resist) & weak));
        }
        Element_Flags untouched = ~(absorb | weak);
        Assert.Equal(old.Absorb & untouched, result.Absorb & untouched);
        Assert.Equal(old.Immune & untouched, result.Immune & untouched);
        Assert.Equal(old.Resist & untouched, result.Resist & untouched);
        Assert.Equal(old.Weak & untouched, result.Weak & untouched);
    }

    [Fact]
    public void Stats_RejectWrongIdentityAndBadBounds_AndClampActorDomains()
    {
        byte[] source = Corpus("m124.bin");
        Assert.Throws<InvalidDataException>(() => SpiraNativeBossStats.Apply(source, 119));
        byte[] bad = (byte[])source.Clone();
        BitConverter.GetBytes(int.MaxValue).CopyTo(bad, 12);
        Assert.Throws<InvalidDataException>(() => SpiraNativeBossStats.Apply(bad, 124));
        Assert.Equal((uint)int.MaxValue, SpiraNativeBossStats.ScaleHp(int.MaxValue, 10));
        Assert.Equal(255, SpiraNativeBossStats.ScaleAttribute(250, 6));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpiraNativeBossStats.ScaleAttribute(10, 11));
    }

    [Theory]
    [InlineData(124)] [InlineData(212)] [InlineData(119)] [InlineData(126)] [InlineData(142)]
    public void RealAi_GrowsSafelyAndPreservesWorkerRoutingAndEveryTailByte(int id)
    {
        byte[] source = Corpus($"m{id:D3}.bin"), copy = (byte[])source.Clone();
        var baseline = AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(source)!);
        var edit = SpiraNativeBossWriter.Build(source, id, Commands);
        Assert.True(edit.NativeCommandSites > 0);
        Assert.True(AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(edit.AiFile, baseline, out _, out string error), error);
        byte[] grown = AiScript_File.SpliceAiFileIntoMonsterGrow(source, edit.AiFile);
        Assert.Equal(copy, source);
        int oldTail = BitConverter.ToInt32(source, 8), newTail = BitConverter.ToInt32(grown, 8);
        Assert.Equal(source[oldTail..], grown[newTail..]);
        Assert.Equal(grown, Monster_File.Read(grown).Write());
        Assert.Throws<InvalidOperationException>(() => SpiraNativeBossWriter.Build(grown, id, Commands));
    }

    [Fact]
    public void SpellAppend_UsesExactDonorsAndPreservesAllRowsAndTexts()
    {
        byte[] original = Corpus("monmagic2.bin"), characters = Corpus("command.bin");
        var old = EntryListFile.Unpack(original);
        byte[] bytes = original;
        foreach (var recipe in MonsterMagicGrowWriter.NativeBossSpells)
        {
            var appended = MonsterMagicGrowWriter.AppendNativeBossSpell(bytes, characters, recipe, 900, 901);
            var row = Ability_Command.ReadList(appended.Bytes, false)[appended.Row];
            var donor = Ability_Command.ReadList(recipe.CharacterDonor ? characters : original, recipe.CharacterDonor)[recipe.DonorId];
            Assert.Equal(donor.AttackPower, row.AttackPower);
            Assert.Equal(donor.DamageFormula, row.DamageFormula);
            Assert.Equal(donor.HitCount, row.HitCount);
            Assert.Equal(donor.DamageFlgs, row.DamageFlgs);
            Assert.Equal(recipe.Element, row.ElementFlgs);
            Assert.Equal(0, row.CostMp); Assert.Equal(0, row.CostOverdrive);
            Assert.Equal(0x6000 | appended.Row, appended.Operand);
            bytes = appended.Bytes;
        }
        var grown = EntryListFile.Unpack(bytes);
        Assert.Equal(old.FirstFile, grown.FirstFile[..old.FirstFile.Length]);
        Assert.Equal(old.SecondFile, grown.SecondFile[..old.SecondFile.Length]);
        Assert.Equal(352, Ability_Command.ReadList(bytes, false).Count);
        Assert.Throws<InvalidDataException>(() => MonsterMagicGrowWriter.AppendNativeBossSpell(original, original,
            MonsterMagicGrowWriter.NativeBossSpells[0], 900, 901));
    }

    [Fact]
    public void CommandReadWrite_PreservesOpaqueBytesBetweenReferencedScripts()
    {
        byte[] source = Corpus("monmagic2.bin");
        Assert.Equal(source, Ability_Command.WriteList(Ability_Command.ReadList(source, false), false));
    }

    [Fact]
    public void SpellAppend_RejectsRebakingEvenIfTheNextRowOrAnimationIdsChange()
    {
        byte[] characters = Corpus("command.bin"), original = Corpus("monmagic2.bin");
        var recipe = MonsterMagicGrowWriter.NativeBossSpells[0];
        byte[] once = MonsterMagicGrowWriter.AppendNativeBossSpell(original, characters, recipe, 900, 901).Bytes;
        byte[] copy = (byte[])once.Clone();
        Assert.Throws<InvalidOperationException>(() => MonsterMagicGrowWriter.AppendNativeBossSpell(once, characters, recipe, 902, 903));
        Assert.Equal(copy, once);
    }

    [Fact]
    public void DispelHaste_HandlesComputedCommandAndDirectConsumerBranch_ThenContinuesNativeState()
    {
        byte[] source = Fixture(124, new ushort[] { 0x303D }, computed: true, branchToCall: true);
        Assert.Equal(new ushort[] { 0x303D }, Run(Ai(source), 1).Commands.Select(c => c.Command));
        var trace = Run(Build(source, 124), 1);
        Assert.Equal(new ushort[] { 0x303D, 0x3036 }, trace.Commands.Select(c => c.Command));
        Assert.Equal((ushort)0xFFF3, trace.Commands[1].Target);
        Assert.Equal(7, trace.Vars[0]); Assert.Empty(trace.Stack);
    }

    [Theory]
    [InlineData(0x4000)] [InlineData(0x407F)] [InlineData(0x4080)] [InlineData(0x4062)]
    public void Wendigo_AttackThenCheer_WithoutAddingCheerToSupportActions(int attack)
    {
        var trace = Run(Build(Fixture(212, new ushort[] { (ushort)attack, 0x403B }), 212), 1);
        Assert.Equal(new ushort[] { (ushort)attack, 0x301A, 0x403B }, trace.Commands.Select(c => c.Command));
        Assert.Equal(7, trace.Vars[0]); Assert.Empty(trace.Stack);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void Evrae_SecondAttackChoosesDifferentLivingActor_AndHasteAddsHolyFira(int random)
    {
        var trace = Run(Build(Fixture(119, new ushort[] { 0x407F, 0x3036 }, target: 0), 119), 1, random: random);
        Assert.Equal(new ushort[] { 0x407F, 0x407F, 0x3036, Commands.HolyFira }, trace.Commands.Select(c => c.Command));
        Assert.NotEqual(Resolve(trace.Commands[0].Target), Resolve(trace.Commands[1].Target));
        Assert.Empty(trace.Stack); Assert.Equal(7, trace.Vars[0]);
    }

    [Fact]
    public void Evrae_OneLivingActorDoesNotRepeatOnTheSameVictim()
    {
        var trace = Run(Build(Fixture(119, new ushort[] { 0x407F }, target: 0), 119), 1,
            alive: new Dictionary<int, int> { [0] = 1, [1] = 0, [2] = 0 });
        Assert.Single(trace.Commands); Assert.Empty(trace.Stack);
    }

    [Theory]
    [InlineData(0, 0x6159)] [InlineData(1, 0x615A)] [InlineData(2, 0x615B)] [InlineData(3, 0x615C)]
    public void Natus_ProtectHaste_BreakOneRandomAntiRaShell_AndReplacesFlare(int random, int anti)
    {
        var trace = Run(Build(Fixture(126, new ushort[] { 0x303B, 0x604F, 0x3052 }), 126), 1, random: random);
        Assert.Equal(new ushort[] { 0x303B, 0x3036, 0x604F, (ushort)anti, 0x303A, Commands.AntiFlareNatus },
            trace.Commands.Select(c => c.Command));
        Assert.Equal((ushort)0xFFF3, trace.Commands[1].Target);
        Assert.Equal((ushort)0xFFF3, trace.Commands[4].Target);
        Assert.Empty(trace.Stack); Assert.Equal(7, trace.Vars[0]);
    }

    [Fact]
    public void Flux_ReplacesOnlyHisOwnFlare()
    {
        var trace = Run(Build(Fixture(142, new ushort[] { 0x6079, 0x6078, 0x303D }), 142), 1);
        Assert.Equal(new ushort[] { Commands.AntiFlareFlux, 0x6078, 0x303D }, trace.Commands.Select(c => c.Command));
        Assert.Empty(trace.Stack);
    }

    [Theory]
    [InlineData(119, 0x3036, 0x6158)]
    [InlineData(126, 0x303B, 0x3036)]
    public void NativeOnHitBuffs_AlsoRunTheirRequiredFollowUp(int id, int buff, int followUp)
    {
        var trace = Run(Build(Fixture(id, new ushort[] { 0x4000 }, hitCommands: new ushort[] { (ushort)buff }), id), 2);
        Assert.Equal(new ushort[] { (ushort)buff, (ushort)followUp }, trace.Commands.Select(c => c.Command));
        Assert.Empty(trace.Stack);
    }

    [Theory]
    [InlineData(124, 10, 1, 1, 1)] [InlineData(126, 10, 1, 1, 1)]
    [InlineData(124, 0, 1, 1, 0)] [InlineData(126, -10, 1, 1, 0)]
    [InlineData(124, 10, 0, 1, 0)] [InlineData(126, 10, 1, 0, 0)]
    public void Counters_RequirePositiveDamageLivingCasterAndNativeCounterGate(int id, int damage, int living, int allowed, int count)
    {
        byte[] source = Fixture(id, new ushort[] { 0x4000 });
        Assert.Empty(Run(Ai(source), 2).Commands);
        var trace = Run(Build(source, id), 2, damage: damage, selfAlive: living, counter: allowed);
        Assert.Equal(count, trace.Commands.Count);
        if (count > 0) Assert.Equal(id == 124 ? Commands.Darkra : Commands.Darkga, trace.Commands[0].Command);
        Assert.Empty(trace.Stack);
    }

    [Fact]
    public void Requiem_InitializesChargesClampsAndReplacesNextTurn_ThenResets()
    {
        byte[] ai = Build(Fixture(124, new ushort[] { 0x3045 }), 124);
        var init = Run(ai, 0);
        Assert.Equal(100, init.Fields[0x14]); Assert.Equal(0, init.Fields[0x13]); Assert.Equal(1, init.Fields[0x89]);
        var hit = Run(ai, 2, damage: 20, gauge: 90);
        Assert.Equal(100, hit.Fields[0x13]); Assert.Single(hit.Commands); Assert.Equal(Commands.Darkra, hit.Commands[0].Command);
        var turn = Run(ai, 1, gauge: 75);
        Assert.Equal(Commands.Requiem, Assert.Single(turn.Commands).Command);
        Assert.Equal(0, turn.Fields[0x13]); Assert.Empty(turn.Stack);
        var next = Run(ai, 1, gauge: 0);
        Assert.Equal((ushort)0x3045, Assert.Single(next.Commands).Command);
        Assert.Equal(25, next.Fields[0x13]); Assert.Equal(7, next.Vars[0]);
    }

    [Fact]
    public void VisualTint_PreservesAlphaSelectorsHeaderAndSource_InEveryMip()
    {
        byte[] source = Enumerable.Range(0, 32 + 48).Select(i => (byte)i).ToArray(), copy = (byte[])source.Clone();
        for (int p = 32; p < source.Length; p += 16) { source[p + 8] = 255; source[p + 9] = 255; }
        copy = (byte[])source.Clone();
        byte[] result = SpiraNativeBossVisuals.TintDxt5MipChain(source, 32, 4, 4, "DXT5", .58f, .16f, .88f, out int levels, out int blocks);
        Assert.Equal(3, levels); Assert.Equal(3, blocks); Assert.Equal(copy, source);
        Assert.Equal(source[..32], result[..32]);
        for (int p = 32; p < source.Length; p += 16)
        {
            Assert.Equal(source[p..(p + 8)], result[p..(p + 8)]);
            Assert.Equal(source[(p + 12)..(p + 16)], result[(p + 12)..(p + 16)]);
            Assert.NotEqual(source[(p + 8)..(p + 12)], result[(p + 8)..(p + 12)]);
        }
        Assert.Throws<InvalidDataException>(() => SpiraNativeBossVisuals.TintDxt5MipChain(source[..^1], 32, 4, 4, "DXT5", 1, 1, 1, out _, out _));
        Assert.Throws<InvalidDataException>(() => SpiraNativeBossVisuals.TintDxt5MipChain(source, 32, 4, 4, "DXT1", 1, 1, 1, out _, out _));
    }

    static byte[] Attributes(Monster_StatSheet s) => new[] { s.Strength, s.Defense, s.Magic, s.MagicDefense, s.Agility, s.Luck, s.Evasion, s.Accuracy };
    static byte[] Ai(byte[] monster) => AiScript_File.SliceAiFileFromMonster(monster)!;
    static byte[] Build(byte[] source, int id) => SpiraNativeBossWriter.Build(source, id, Commands).AiFile;

    // Minimal event code in a real monster container. Independent init/onTurn/onHit,
    // plus a native tail-state write: new sequences must finish before that write.
    static byte[] Fixture(int id, ushort[] commands, ushort target = 0xFFFA, bool computed = false, bool branchToCall = false, ushort[]? hitCommands = null)
    {
        var code = new List<byte>();
        void Emit(byte opcode, ushort? operand = null) { code.Add(opcode); if (operand.HasValue) code.AddRange(BitConverter.GetBytes(operand.Value)); }
        Emit(0x3C); int onTurn = code.Count, consumer = 0;
        foreach (ushort command in commands)
        {
            Emit(0xAE, target);
            if (computed) { Emit(0xAE, (ushort)(command - 1)); Emit(0xAE, 1); Emit(0x14); }
            else Emit(0xAE, command);
            if (branchToCall) { Emit(0xB0, 0); consumer = code.Count; }
            Emit(0xD8, 0x700B);
        }
        Emit(0xAE, 7); Emit(0xA0, 0); Emit(0x3C); int onHit = code.Count;
        foreach (ushort command in hitCommands ?? Array.Empty<ushort>())
        { Emit(0xAE, 0xFFF3); Emit(0xAE, command); Emit(0xD8, 0x700B); }
        Emit(0x3C);
        int table = 0x80 + code.Count;
        var ai = new byte[(table + 12 + (branchToCall ? 4 : 0) + 15) / 16 * 16];
        void U32(int offset, int value) => BitConverter.GetBytes(value).CopyTo(ai, offset);
        void U16(int offset, ushort value) => BitConverter.GetBytes(value).CopyTo(ai, offset);
        U32(0, code.Count); U32(0x10, ai.Length); U16(0x14, 1); U32(0x30, 0x80); U16(0x36, 1); U32(0x38, 0x40);
        U16(0x48, 3); U16(0x4A, branchToCall ? (ushort)1 : (ushort)0); U16(0x50, 4);
        U32(0x54, 0x70); U32(0x58, 0x78); U32(0x5C, 0x78); U32(0x60, table); U32(0x64, table + 12);
        U32(0x70, 0x56000000); U32(0x74, 1); U32(table, 0); U32(table + 4, onTurn); U32(table + 8, onHit);
        if (branchToCall) U32(table + 12, consumer); code.CopyTo(ai, 0x80);
        var file = Monster_File.Read(Corpus($"m{id:D3}.bin"));
        file.AiFile = ai;
        ai.AsSpan(0, 4).CopyTo(file.OriginalHeader!.Padding.AsSpan(12, 4));
        byte[] routing = new byte[32]; routing[0] = 1; routing[1] = 2; routing[5] = 2; routing[6] = 8;
        routing[8] = 4; routing[10] = 1; routing[12] = 0; routing[14] = 0xFF; routing[15] = 0xFF; routing[16] = 2;
        file.WorkerFile = routing;
        return file.Write();
    }

    sealed class Trace
    {
        public List<(ushort Target, ushort Command)> Commands = new();
        public Dictionary<int, int> Vars = new();
        public Dictionary<int, int> Fields = new() { [0x13] = 0, [0x14] = 100 };
        public Stack<int> Stack = new();
    }
    static int Resolve(ushort target) => target switch { 0xFFFA => 0, 0xFFF9 => 1, 0xFFF8 => 2, _ => target };

    // Bounded interpreter for the emitted primitives. It validates order, stack,
    // targets and event continuation; it is explicitly not gameplay/RT2 proof.
    static Trace Run(byte[] ai, int entry, int random = 0, int damage = 0, int selfAlive = 1, int counter = 1,
        int gauge = 0, Dictionary<int, int>? alive = null)
    {
        var script = AiScript_File.Read(ai); var worker = script.Workers[0]; var trace = new Trace();
        trace.Fields[0x13] = gauge; alive ??= new() { [0] = 1, [1] = 1, [2] = 1 };
        var nodes = script.Instructions.ToDictionary(op => op.Offset - script.ScriptStart);
        int pc = worker.Entrypoints[entry];
        int Pop() => trace.Stack.Pop();
        for (int budget = 0; budget < 20000; budget++)
        {
            if (pc == script.CodeLength) return trace;
            var op = nodes[pc]; pc += op.Length;
            switch (op.Opcode)
            {
                case 0x00: break;
                case 0xAE: trace.Stack.Push(op.Operand); break;
                case 0x9F: trace.Stack.Push(trace.Vars.GetValueOrDefault(op.Operand)); break;
                case 0xA0: trace.Vars[op.Operand] = Pop(); break;
                case 0x06: { int b = Pop(), a = Pop(); trace.Stack.Push(a == b ? 1 : 0); break; }
                case 0x01: { int b = Pop(), a = Pop(); trace.Stack.Push(a != 0 || b != 0 ? 1 : 0); break; }
                case 0x02: { int b = Pop(), a = Pop(); trace.Stack.Push(a != 0 && b != 0 ? 1 : 0); break; }
                case 0x0B: { int b = Pop(), a = Pop(); trace.Stack.Push(a < b ? 1 : 0); break; }
                case 0x0F: { int b = Pop(), a = Pop(); trace.Stack.Push(a <= b ? 1 : 0); break; }
                case 0x14: trace.Stack.Push(Pop() + Pop()); break;
                case 0x15: { int b = Pop(), a = Pop(); trace.Stack.Push(a - b); break; }
                case 0x18: { int b = Pop(), a = Pop(); trace.Stack.Push(a % b); break; }
                case 0xB0: pc = worker.JumpTargets[op.Operand]; break;
                case 0xD6: if (Pop() != 0) pc = worker.JumpTargets[op.Operand]; break;
                case 0xD7: if (Pop() == 0) pc = worker.JumpTargets[op.Operand]; break;
                case 0x3C: return trace;
                case 0xB5 when op.Operand == 0x00A9: trace.Stack.Push(random); break;
                case 0xB5 when op.Operand == 0x70E0: trace.Stack.Push(counter); break;
                case 0xB5 when op.Operand == 0x700F:
                {
                    int field = Pop(); ushort actor = (ushort)Pop();
                    trace.Stack.Push(field == 0xA6 ? damage : field == 4 ? actor == 0xFFF3 ? selfAlive
                        : alive.GetValueOrDefault(Resolve(actor)) : trace.Fields.GetValueOrDefault(field)); break;
                }
                case 0xB5 when op.Operand == 0x7010:
                    Assert.Equal(0, Pop()); Assert.Equal(0, Pop()); Assert.Equal(4, Pop()); Assert.Equal(0xFFF2, Pop());
                    trace.Stack.Push(alive.First(p => p.Value > 0).Key); break;
                case 0xB5 when op.Operand == 0x701E:
                {
                    int b = Resolve((ushort)Pop()), a = Resolve((ushort)Pop()); trace.Stack.Push(a == b ? 1 : 0); break;
                }
                case 0xD8 when op.Operand == 0x7018:
                {
                    int value = Pop(), field = Pop(); Assert.Equal(0xFFF3, Pop()); trace.Fields[field] = value; break;
                }
                case 0xD8 when op.Operand is 0x700B or 0x705A:
                {
                    ushort command = (ushort)Pop(), target = (ushort)Pop(); trace.Commands.Add((target, command)); break;
                }
                default: throw new InvalidOperationException($"Unsupported primitive {op.Opcode:X2}/{op.Operand:X4}.");
            }
        }
        throw new InvalidOperationException("Emitted handler failed to terminate.");
    }
}

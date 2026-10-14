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

public sealed class SpiraFluxMagicCounterTests
{
    static readonly ushort[] Commands = { 0x6160, 0x6161, 0x6162, 0x6163, 0x6164 };
    static byte[] FileBytes(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "FluxMagicCounters", name));

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void PositiveMagicDamage_ChoosesOneSpellForEntirePartyAndContinuesNativeHook(int roll)
    {
        byte[] source = Fixture();
        var baseline = Run(AiScript_File.SliceAiFileFromMonster(source)!, roll, 100, 2, 1, 1);
        Assert.Empty(baseline.Commands);
        var trace = Run(SpiraFluxMagicCounterWriter.Build(source, Commands), roll, 100, 2, 1, 1);
        Assert.Equal((Target: (ushort)0xFFF2, Command: Commands[roll]), Assert.Single(trace.Commands));
        Assert.Equal(7, trace.Vars[0]); Assert.Empty(trace.Stack);
    }

    [Theory]
    [InlineData(100, 1, 1, 1)] // physical
    [InlineData(100, 0, 1, 1)] // non-magical
    [InlineData(0, 2, 1, 1)]   // miss / zero damage
    [InlineData(-100, 2, 1, 1)] // healing/absorb
    [InlineData(100, 2, 0, 1)] // dead caster
    [InlineData(100, 2, 1, 0)] // counter-to-counter gate
    public void IneligibleHits_DoNotCounterOrConsumeRng(int damage, int type, int alive, int allowed)
    {
        var trace = Run(SpiraFluxMagicCounterWriter.Build(Fixture(), Commands), 2, damage, type, alive, allowed);
        Assert.Empty(trace.Commands); Assert.Equal(0, trace.RngCalls);
        Assert.Equal(7, trace.Vars[0]); Assert.Empty(trace.Stack);
    }

    [Fact]
    public void ChoiceStateChangedWhileCommandYields_StillProducesOnlyOneCounter()
    {
        var trace = Run(SpiraFluxMagicCounterWriter.Build(Fixture(), Commands), 0, 10, 2, 1, 1, overwriteChoice: true);
        Assert.Equal(Commands[0], Assert.Single(trace.Commands).Command);
        Assert.Equal(7, trace.Vars[0]); Assert.Empty(trace.Stack);
    }

    [Fact]
    public void CurrentFlux_PreservesStatsTailRoutingAndExistingAntiFlare()
    {
        byte[] source = FileBytes("m142.bin"), copy = (byte[])source.Clone();
        var baseline = AiScript_File.Read(AiScript_File.SliceAiFileFromMonster(source)!);
        byte[] ai = SpiraFluxMagicCounterWriter.Build(source, Commands);
        Assert.True(AiValidator.TryValidateRebuiltAllowingBaselineUnknowns(ai, baseline, out _, out string reason), reason);
        byte[] grown = AiScript_File.SpliceAiFileIntoMonsterGrow(source, ai);
        int oldTail = BitConverter.ToInt32(source, 8), newTail = BitConverter.ToInt32(grown, 8);
        Assert.Equal(source[oldTail..], grown[newTail..]); Assert.Equal(copy, source);
        var file = Monster_File.Read(grown);
        Assert.Equal(336000u, file.StatSheetFile.Hp);
        Assert.Equal(grown, file.Write());
        Assert.Contains(AiScript_File.Read(ai).Instructions, op => op.Opcode == 0xAE && op.Operand == 0x615E);
        Assert.Throws<InvalidOperationException>(() => SpiraFluxMagicCounterWriter.Build(grown, Commands));
        Assert.Throws<InvalidDataException>(() => SpiraFluxMagicCounterWriter.Build(FileBytes("m127.bin"), Commands));
        Assert.Throws<ArgumentException>(() => SpiraFluxMagicCounterWriter.Build(source, Commands[..4]));
    }

    [Fact]
    public void CounterCommands_PreserveExactMortibodyDamageTargetMaskAndAnimationDispatchFlags()
    {
        byte[] original = FileBytes("monmagic2.bin"), character = FileBytes("command.bin"), grown = original;
        var originalRows = Ability_Command.ReadList(original, false);
        var characterRows = Ability_Command.ReadList(character, true);
        foreach (var recipe in MonsterMagicGrowWriter.FluxCounterSpells)
        {
            var appended = MonsterMagicGrowWriter.AppendFluxCounterSpell(grown, character, recipe, 930, recipe.CharacterDonor ? (short)0 : (short)930);
            var result = Ability_Command.ReadList(appended.Bytes, false)[appended.Row];
            var donor = (recipe.CharacterDonor ? characterRows : originalRows)[recipe.DonorRow];
            Assert.Equal(donor.AttackPower, result.AttackPower); Assert.Equal(donor.DamageFormula, result.DamageFormula);
            Assert.Equal(donor.DamageFlgs, result.DamageFlgs); Assert.Equal(donor.DamageTypeFlgs, result.DamageTypeFlgs);
            Assert.Equal(donor.HitCount, result.HitCount); Assert.Equal(donor.AttackAccuracy, result.AttackAccuracy);
            Assert.Equal(originalRows[57].TargetFlgs, result.TargetFlgs);
            Assert.Equal(Ability_Command.ElementFlags.Dark, result.ElementFlgs);
            Assert.Equal(donor.Misc1Flgs, result.Misc1Flgs); Assert.Equal(donor.Misc2Flgs, result.Misc2Flgs);
            Assert.Equal(donor.Misc3Flgs, result.Misc3Flgs); Assert.Equal(donor.Misc4Flgs, result.Misc4Flgs);
            Assert.Equal(recipe.CharacterDonor ? 12 : 16, result.AttackPower);
            Assert.Throws<InvalidOperationException>(() => MonsterMagicGrowWriter.AppendFluxCounterSpell(appended.Bytes, character, recipe, 932, 932));
            grown = appended.Bytes;
        }
        var old = EntryListFile.Unpack(original); var packed = EntryListFile.Unpack(grown);
        Assert.Equal(old.FirstFile, packed.FirstFile[..old.FirstFile.Length]);
        Assert.Equal(old.SecondFile, packed.SecondFile[..old.SecondFile.Length]);
        Assert.Equal(357, Ability_Command.ReadList(grown, false).Count);
    }

    static byte[] Fixture()
    {
        // Three events; onTurn contains the existing Anti-Flare, onHit writes native state.
        byte[] code = { 0x3C, 0xAE, 0xF2, 0xFF, 0xAE, 0x5E, 0x61, 0xD8, 0x0B, 0x70, 0x3C,
            0xAE, 7, 0, 0xA0, 0, 0, 0x3C };
        int table = 0x80 + code.Length;
        var ai = new byte[(table + 12 + 15) / 16 * 16];
        void U32(int off, int value) => BitConverter.GetBytes(value).CopyTo(ai, off);
        void U16(int off, ushort value) => BitConverter.GetBytes(value).CopyTo(ai, off);
        U32(0, code.Length); U32(0x10, ai.Length); U16(0x14, 1); U32(0x30, 0x80); U16(0x36, 1); U32(0x38, 0x40);
        U16(0x48, 3); U16(0x50, 4); U32(0x54, 0x70); U32(0x58, 0x78); U32(0x5C, 0x78);
        U32(0x60, table); U32(0x64, table + 12); U32(0x70, 0x56000000); U32(0x74, 1);
        U32(table, 0); U32(table + 4, 1); U32(table + 8, 11); code.CopyTo(ai, 0x80);
        var file = Monster_File.Read(FileBytes("m142.bin")); file.AiFile = ai;
        ai.AsSpan(0, 4).CopyTo(file.OriginalHeader!.Padding.AsSpan(12, 4));
        var route = new byte[32]; route[0] = 1; route[1] = 2; route[5] = 2; route[6] = 8;
        route[8] = 4; route[10] = 1; route[14] = 255; route[15] = 255; route[16] = 2;
        file.WorkerFile = route; return file.Write();
    }

    sealed class Trace
    {
        public Stack<int> Stack = new(); public Dictionary<int, int> Vars = new();
        public List<(ushort Target, ushort Command)> Commands = new(); public int RngCalls;
    }

    // Limited primitive interpreter; regression evidence, not RT2/gameplay emulation.
    static Trace Run(byte[] ai, int roll, int damage, int type, int alive, int allowed, bool overwriteChoice = false)
    {
        var script = AiScript_File.Read(ai); var worker = script.Workers[0]; var trace = new Trace();
        var nodes = script.Instructions.ToDictionary(op => op.Offset - script.ScriptStart);
        int pc = worker.Entrypoints[2]; int Pop() => trace.Stack.Pop();
        for (int budget = 0; budget < 2000; budget++)
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
                case 0x02: { int b = Pop(), a = Pop(); trace.Stack.Push(a != 0 && b != 0 ? 1 : 0); break; }
                case 0x0B: { int b = Pop(), a = Pop(); trace.Stack.Push(a < b ? 1 : 0); break; }
                case 0x18: { int b = Pop(), a = Pop(); trace.Stack.Push(a % b); break; }
                case 0xB0: pc = worker.JumpTargets[op.Operand]; break;
                case 0xD7: if (Pop() == 0) pc = worker.JumpTargets[op.Operand]; break;
                case 0x3C: return trace;
                case 0xB5 when op.Operand == 0x700F:
                { int field = Pop(); Assert.Equal(0xFFF3, Pop()); trace.Stack.Push(field == 0xA6 ? damage : alive); break; }
                case 0xB5 when op.Operand == 0x7019: trace.Stack.Push(0x3049); break;
                case 0xB5 when op.Operand == 0x701A:
                    Assert.Equal(1, Pop()); Assert.Equal(0x3049, Pop()); trace.Stack.Push(type); break;
                case 0xB5 when op.Operand == 0x70E0: trace.Stack.Push(allowed); break;
                case 0xB5 when op.Operand == 0x00A9: trace.Stack.Push(roll); trace.RngCalls++; break;
                case 0xD8 when op.Operand == 0x700B:
                { ushort cmd = (ushort)Pop(), target = (ushort)Pop(); trace.Commands.Add((target, cmd));
                    if (overwriteChoice) trace.Vars[1] = 4; break; }
                default: throw new InvalidOperationException($"Unsupported primitive {op.Opcode:X2}/{op.Operand:X4}.");
            }
        }
        throw new InvalidOperationException("Counter did not terminate.");
    }
}

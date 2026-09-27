using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Ai.Sin;
using FFXProjectEditor.Utils.Encoding;
using Xunit;

namespace FFXProjectEditor.Tests.FfxLib.Ai.Sin;

public sealed class SinPostActionPresetWriterTests
{
    public static IEnumerable<object[]> Recipes => SinPostActionPresetCatalog.All.Select(r => new object[] { r.Id });

    [Theory]
    [MemberData(nameof(Recipes))]
    public void EveryRecipe_PreservesNativeActionAndTail_WithoutModifyingSource(string id)
    {
        var recipe = SinPostActionPresetCatalog.Find(id)!;
        byte[] source = Fixture();
        byte[] copy = (byte[])source.Clone();
        var edited = Build(source, recipe.RequiredMonster ?? (recipe.DamageOnly ? "m198" : "m001"), id);
        var trace = Execute(edited.AiFile);
        Assert.Equal(recipe.ReplayNative ? new ushort[] { 0x4000, 0x4000 }
            : new ushort[] { 0x4000 }.Concat(recipe.Skills.Select(s => s.Operand)), trace.Commands.Select(c => c.Command));
        Assert.Equal(7, trace.Vars[0]); // native state update after the command still executes
        Assert.Empty(trace.Stack);
        Assert.Equal(copy, source);
        Assert.Equal(1, edited.NativeActionCount);
        Assert.All(trace.Commands, c => Assert.Equal(0x700B, c.Perform));
        byte[] spliced = AiScript_File.SpliceAiFileIntoMonsterGrow(source, edited.AiFile);
        Assert.True(AiScript_File.SliceAiFileFromMonster(spliced)!.AsSpan(0, edited.AiFile.Length).SequenceEqual(edited.AiFile));
    }

    [Fact]
    public void EveryOriginalCommand_GetsExactlyOneCompleteSequence_AndOnHitIsUnchanged()
    {
        var edit = Build(Fixture(twoNativeActions: true), "m001", "UNI-015");
        var trace = Execute(edit.AiFile);
        Assert.Equal(new ushort[] { 0x4000, 0x6130, 0x6131, 0x4001, 0x6130, 0x6131 }, trace.Commands.Select(c => c.Command));
        Assert.Equal(2, edit.NativeActionCount);
        Assert.Equal(new ushort[] { 0x4002 }, Execute(edit.AiFile, entrypoint: 1).Commands.Select(c => c.Command));
    }

    [Fact]
    public void Mirage_ReplaysEachResolvedNativeCommandOnce_ThenPreservesNativeStateUpdate()
    {
        var edit = Build(Fixture(twoNativeActions: true), "m001", "UNI-010");
        var trace = Execute(edit.AiFile);
        Assert.Equal(new ushort[] { 0x4000, 0x4000, 0x4001, 0x4001 }, trace.Commands.Select(c => c.Command));
        Assert.Equal(trace.Commands[0].Target, trace.Commands[1].Target);
        Assert.Equal(trace.Commands[2].Target, trace.Commands[3].Target);
        Assert.Equal(7, trace.Vars[0]);
        Assert.Empty(trace.Stack);
    }

    [Fact]
    public void Mirage_CapturesComputedArguments_WithoutRepeatingTheirExpressions()
    {
        var trace = Execute(Build(Fixture(computedArgs: true), "m001", "UNI-010").AiFile);
        Assert.Equal(new ushort[] { 0x4000, 0x4000 }, trace.Commands.Select(c => c.Command));
        Assert.All(trace.Commands, c => Assert.Equal(0xFFF9, c.Target));
        Assert.Empty(trace.Stack);
    }

    [Fact]
    public void Uni040_BranchDirectlyToNativeCall_StillTakesFreshHpSnapshots()
    {
        var trace = Execute(Build(Fixture(branchToCall: true), "m198", "UNI-040").AiFile,
            afterNativeHp: new[] { 90, 100, 100 });
        Assert.Equal(0xFFFA, trace.Statuses.Single().Target);
        Assert.Empty(trace.Stack);
    }

    [Fact]
    public void MalformedAi_IsRejectedWithoutThrowingOrReturningPartialEdits()
    {
        byte[] monster = Fixture();
        BitConverter.GetBytes(int.MaxValue).CopyTo(monster, 0x30 + AiScript_File.OffScriptStart);
        Assert.False(SinPostActionPresetWriter.TryBuild(monster, "m001", "UNI-015", out var edit, out string error));
        Assert.Null(edit);
        Assert.NotEmpty(error);
    }

    [Theory]
    [InlineData("UNI-016")]
    [InlineData("UNI-020")]
    [InlineData("UNI-037")]
    public void SameTargetPair_SelectsOnceAndKeepsTheResolvedTarget(string id)
    {
        var trace = Execute(Build(Fixture(), "m001", id).AiFile);
        Assert.Equal(1, trace.FindCount);
        Assert.Equal(trace.Commands[1].Target, trace.Commands[2].Target);
    }

    [Fact]
    public void GuardFormation_ChoosesAllyThenEnemy_AndChorusBuffsBeforePartyDamage()
    {
        var guard = Execute(Build(Fixture(), "m001", "UNI-017").AiFile);
        Assert.Equal(new ushort[] { 0xFFF1, 0xFFF2 }, guard.FindGroups);
        var chorus = Execute(Build(Fixture(), "m001", "UNI-038").AiFile);
        Assert.Equal(new ushort[] { 0xFFFA, 0xFFF1, 0xFFF2 }, chorus.Commands.Select(c => c.Target));
    }

    [Fact]
    public void Uni039_UsesForbiddenRiteAfterWillbreaker_WithoutAddingStatusToSkillPayload()
    {
        var trace = Execute(Build(Fixture(), "m001", "UNI-039").AiFile);
        Assert.Equal(new ushort[] { 0x4000, 0x6153, 0x6154 }, trace.Commands.Select(c => c.Command));
        Assert.Equal("command:6154", trace.Events[^2]);
        Assert.Equal("status:002A", trace.Events[^1]);
        Assert.Equal((Field: (ushort)0x002A, Value: 1), (trace.Statuses.Single().Field, trace.Statuses.Single().Value));
        Assert.Equal(3, trace.FindCount); // each skill plus the separate, random Forbidden Rite target
    }

    [Theory]
    [InlineData(100, 100, 100, 0)] // miss/no damage
    [InlineData(120, 100, 130, 0)] // healing
    [InlineData(90, 100, 100, 1)]
    [InlineData(90, 80, 100, 2)]
    [InlineData(90, 80, 70, 3)]
    public void Uni040_OnlyZombiesCharactersWhoseHpDecreased(int a, int b, int c, int expected)
    {
        var trace = Execute(Build(Fixture(), "m200", "UNI-040").AiFile, afterNativeHp: new[] { a, b, c });
        Assert.Single(trace.Commands); // no fabricated skill
        Assert.Equal(expected, trace.Statuses.Count);
        var targets = new ushort[] { 0xFFFA, 0xFFF9, 0xFFF8 };
        Assert.Equal(targets.Where((_, i) => new[] { a, b, c }[i] < 100), trace.Statuses.Select(s => s.Target));
        Assert.All(trace.Statuses, s => { Assert.Equal(0x0007, s.Field); Assert.Equal(1, s.Value); });
        Assert.Equal(7, trace.Vars[0]);
        Assert.Empty(trace.Stack);
    }

    [Fact]
    public void RepeatedBake_IsRejectedWithoutTouchingInput()
    {
        byte[] source = Fixture();
        foreach (string id in new[] { "UNI-015", "UNI-040", "UNI-010" })
        {
            string monsterId = id == "UNI-040" ? "m198" : "m001";
            byte[] baked = AiScript_File.SpliceAiFileIntoMonsterGrow(source, Build(source, monsterId, id).AiFile);
            byte[] copy = (byte[])baked.Clone();
            Assert.False(SinPostActionPresetWriter.TryBuild(baked, monsterId, id, out var edit, out string error));
            Assert.Null(edit);
            Assert.Contains("already exists", error);
            Assert.Equal(copy, baked);
        }
    }

    [Theory]
    [InlineData("UNI-021")]
    [InlineData("UNI-022")]
    [InlineData("UNI-023")]
    [InlineData("UNI-040")]
    [InlineData("UNI-999")]
    [InlineData("BOSS-SEYMOUR-001")]
    public void WrongMonsterOrUnspecifiedRecipe_IsRejected(string id)
    {
        Assert.False(SinPostActionPresetWriter.TryBuild(Fixture(), "m001", id, out var edit, out string error));
        Assert.Null(edit);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void SharedEventConsumer_IsRejectedRatherThanChangingOnHit()
    {
        Assert.False(SinPostActionPresetWriter.TryBuild(Fixture(sharedEntry: true), "m001", "UNI-015", out _, out string error));
        Assert.Contains("shares", error);
    }

    [Fact]
    public void LiveSkillTable_MatchesEveryCommandIdAndName()
    {
        string path = Path.Combine(RepoRoot(), "mods", "Spira Reforge", "data", "mods", "ffx_ps2", "ffx", "master", "new_uspc", "battle", "kernel", "monmagic2.bin");
        var rows = Ability_Command.ReadList(File.ReadAllBytes(path), hasExtraInfo: false);
        foreach (var skill in SinPostActionPresetCatalog.All.SelectMany(r => r.Skills))
        {
            int index = skill.Operand & 0xFFF;
            Assert.True(index < rows.Count, $"Missing row {index}");
            string name = FfxEncoding.DecodeScript(rows[index].NameScriptBytes).GetString(FfxEncoding.UsDecoder).Trim();
            Assert.Equal(skill.Name, name);
        }
    }

    [Theory]
    [InlineData("m198", "UNI-040")]
    [InlineData("m200", "UNI-040")]
    [InlineData("m186", "UNI-021")]
    [InlineData("m066", "UNI-022")]
    [InlineData("m064", "UNI-023")]
    [InlineData("m004", "UNI-039")]
    public void RealMonster_BakeAndContainerRoundTrip(string monsterId, string id)
    {
        byte[] monster = File.ReadAllBytes(Path.Combine(RepoRoot(), "mods", "Spira Reforge", "sin-clean-bins", "_" + monsterId, monsterId + ".bin"));
        var edit = Build(monster, monsterId, id);
        Assert.True(edit.NativeActionCount > 0);
        byte[] rebuilt = AiScript_File.SpliceAiFileIntoMonsterGrow(monster, edit.AiFile);
        Assert.True(AiScript_File.SliceAiFileFromMonster(rebuilt)!.AsSpan(0, edit.AiFile.Length).SequenceEqual(edit.AiFile));
        int oldWorker = BitConverter.ToInt32(monster, 8), newWorker = BitConverter.ToInt32(rebuilt, 8);
        Assert.Equal(monster[oldWorker..], rebuilt[newWorker..]);
    }

    static SinPostActionEdit Build(byte[] monster, string id, string preset)
    {
        Assert.True(SinPostActionPresetWriter.TryBuild(monster, id, preset, out var edit, out string error), error);
        return Assert.IsType<SinPostActionEdit>(edit);
    }

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "FFXProjectEditor", "FFXProjectEditor.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository corpus required.");
    }

    // Minimal, valid one-worker monster container: independent onTurn/onHit, a native private state
    // variable and trailing WorkerFile bytes. The old state write is an observable continuation contract.
    static byte[] Fixture(bool twoNativeActions = false, bool sharedEntry = false, bool computedArgs = false, bool branchToCall = false)
    {
        var code = new List<byte>();
        void Emit(byte op, ushort? value = null) { code.Add(op); if (value.HasValue) code.AddRange(BitConverter.GetBytes(value.Value)); }
        int callTarget = 0;
        void Command(ushort id)
        {
            if (computedArgs && id == 0x4000)
            { Emit(0xAE, 0xFFF8); Emit(0xAE, 1); Emit(0x14); Emit(0xAE, 0x3FFF); Emit(0xAE, 1); Emit(0x14); }
            else { Emit(0xAE, 0xFFFA); Emit(0xAE, id); }
            if (branchToCall && id == 0x4000) { Emit(0xB0, 0); callTarget = code.Count; }
            Emit(0xD8, 0x700B);
        }
        Command(0x4000);
        if (twoNativeActions) Command(0x4001);
        Emit(0xAE, 7); Emit(0xA0, 0); Emit(0x3C);
        int onHit = code.Count;
        Command(0x4002); Emit(0x3C);
        int table = 0x80 + code.Count;
        var ai = new byte[(table + 8 + (branchToCall ? 4 : 0) + 15) / 16 * 16];
        void U32(int off, int value) => BitConverter.GetBytes(value).CopyTo(ai, off);
        void U16(int off, ushort value) => BitConverter.GetBytes(value).CopyTo(ai, off);
        U32(0, code.Count); U32(0x10, ai.Length); U16(0x14, 1); U32(0x30, 0x80); U16(0x36, 1); U32(0x38, 0x40);
        U16(0x48, 2); U16(0x4A, branchToCall ? (ushort)1 : (ushort)0); U16(0x50, 4);
        U32(0x54, 0x70); U32(0x58, 0x78); U32(0x5C, 0x78); U32(0x60, table); U32(0x64, table + 8);
        U32(0x70, 0x56000000); U32(0x74, 1); U32(table, 0); U32(table + 4, sharedEntry ? 0 : onHit);
        if (branchToCall) U32(table + 8, callTarget);
        code.CopyTo(ai, 0x80);
        int worker = 0x30 + ai.Length, stat = worker + 32;
        var monster = new byte[stat + 16];
        BitConverter.GetBytes(monster.Length).CopyTo(monster, 0);
        BitConverter.GetBytes(0x30).CopyTo(monster, 4); BitConverter.GetBytes(worker).CopyTo(monster, 8); BitConverter.GetBytes(stat).CopyTo(monster, 12);
        ai.CopyTo(monster, 0x30);
        monster[worker] = 1; monster[worker + 1] = 2; monster[worker + 5] = 2; monster[worker + 6] = 8;
        monster[worker + 8] = 4; monster[worker + 12] = 0xFF; monster[worker + 13] = 0xFF;
        monster[worker + 14] = 0xFF; monster[worker + 15] = 0xFF; monster[worker + 16] = 1;
        return monster;
    }

    sealed class Trace
    {
        public List<(ushort Target, ushort Command, ushort Perform)> Commands = new();
        public List<(ushort Target, ushort Field, int Value)> Statuses = new();
        public List<string> Events = new();
        public Dictionary<int, int> Vars = new();
        public Stack<int> Stack = new();
        public List<ushort> FindGroups = new();
        public int FindCount;
    }

    // Bounded semantic regression of the emitted instructions; this is not a game/RT2 emulator.
    static Trace Execute(byte[] ai, int entrypoint = 0, int[]? afterNativeHp = null)
    {
        var script = AiScript_File.Read(ai); var trace = new Trace();
        var worker = script.Workers[0]; var byOffset = script.Instructions.ToDictionary(i => i.Offset - script.ScriptStart);
        int pc = worker.Entrypoints[entrypoint];
        var hp = new Dictionary<ushort, int> { [0xFFFA] = 100, [0xFFF9] = 100, [0xFFF8] = 100 };
        for (int budget = 0; budget < 500; budget++)
        {
            var op = byOffset[pc]; pc += op.Length;
            switch (op.Opcode)
            {
                case 0x00: break;
                case 0xAE: trace.Stack.Push(op.Operand); break;
                case 0x9F: trace.Stack.Push(trace.Vars.GetValueOrDefault(op.Operand)); break;
                case 0xA0: trace.Vars[op.Operand] = trace.Stack.Pop(); break;
                case 0x0B: int rhs = trace.Stack.Pop(), lhs = trace.Stack.Pop(); trace.Stack.Push(lhs < rhs ? 1 : 0); break;
                case 0x14: trace.Stack.Push(trace.Stack.Pop() + trace.Stack.Pop()); break;
                case 0xB0: pc = worker.JumpTargets[op.Operand]; break;
                case 0xD7: if (trace.Stack.Pop() == 0) pc = worker.JumpTargets[op.Operand]; break;
                case 0x3C: return trace;
                case 0xB5 when op.Operand == 0x700F:
                    Assert.Equal(0, trace.Stack.Pop()); trace.Stack.Push(hp[(ushort)trace.Stack.Pop()]); break;
                case 0xB5 when op.Operand == 0x7010:
                    Assert.Equal(0, trace.Stack.Pop()); Assert.Equal(0, trace.Stack.Pop()); Assert.Equal(4, trace.Stack.Pop());
                    trace.FindGroups.Add((ushort)trace.Stack.Pop()); trace.Stack.Push(10 + trace.FindCount++); break;
                case 0xD8 when op.Operand is 0x700B or 0x705A:
                    ushort cmd = (ushort)trace.Stack.Pop(), target = (ushort)trace.Stack.Pop();
                    trace.Commands.Add((target, cmd, op.Operand)); trace.Events.Add($"command:{cmd:X4}");
                    if (cmd == 0x4000 && afterNativeHp != null)
                    { hp[0xFFFA] = afterNativeHp[0]; hp[0xFFF9] = afterNativeHp[1]; hp[0xFFF8] = afterNativeHp[2]; }
                    break;
                case 0xD8 when op.Operand == 0x7018:
                    int value = trace.Stack.Pop(); ushort field = (ushort)trace.Stack.Pop(), actor = (ushort)trace.Stack.Pop();
                    trace.Statuses.Add((actor, field, value)); trace.Events.Add($"status:{field:X4}"); break;
                default: throw new InvalidOperationException($"Unexpected test opcode {op.Opcode:X2}/{op.Operand:X4}");
            }
        }
        throw new InvalidOperationException("Sequence did not terminate.");
    }
}

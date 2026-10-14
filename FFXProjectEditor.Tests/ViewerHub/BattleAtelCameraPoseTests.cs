using System;
using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.BattleMap;
using Xunit;

namespace FFXProjectEditor.Tests.ViewerHub;

public sealed class BattleAtelCameraPoseTests
{
    [Fact]
    public void DirectPositionIsNotReplacedByAPolarCallFromAnotherEntry()
    {
        var b = new ScriptBuilder(); b.Entry();
        b.Call(0x6020, 14, -6, 13); b.Call(0x6002, -61, -24, -54);
        b.Call(0x603A, 0); b.Call(0x603B, 700); b.Return();
        b.Entry(); b.Call(0x6020, 99, 99, 99); b.Call(0x6004, -194, 5, 88); b.Return();
        var pose = BattleAtelCameraPoseReader.ReadScript(b.Build(), new[] { 0 });
        Assert.NotNull(pose); Assert.Equal("camSetPos", pose.PositionCommand);
        Assert.Equal(new float[] { -61, -24, -54 }, pose.Eye);
        Assert.Equal(new float[] { 14, -6, 13 }, pose.Target); Assert.Equal(700f, pose.ScreenDepth);
    }

    [Fact]
    public void PolarUsesReferenceInItsOwnEntryAndDegreeConvention()
    {
        var b = new ScriptBuilder(); b.Entry();
        b.Call(0x6020, 10, 20, 30); b.Call(0x6004, 90, -30, 100); b.Return();
        var pose = BattleAtelCameraPoseReader.ReadScript(b.Build(), new[] { 0 });
        Assert.NotNull(pose); Assert.Equal("camSetPolar", pose.PositionCommand);
        Assert.InRange(pose.Eye[0], 9.999f, 10.001f);
        Assert.InRange(pose.Eye[1], -30.001f, -29.999f);
        Assert.InRange(pose.Eye[2], 116.601f, 116.604f);
    }

    [Fact]
    public void ComputedInitialCameraDoesNotBorrowALaterAttackCamera()
    {
        var b = new ScriptBuilder(); b.Entry(); b.Call(0x6020, 0, 0, 0);
        b.ComputedPosition(); b.Return();
        b.Entry(); b.Call(0x6020, 1, 2, 3); b.Call(0x6002, 50, 50, 50); b.Return();
        Assert.Null(BattleAtelCameraPoseReader.ReadScript(b.Build(), new[] { 0 }));
    }

    [Fact]
    public void LaterLiteralSetupRestoresTheFinalPoseAfterIntroMovement()
    {
        var b = new ScriptBuilder(); b.Entry();
        b.Call(0x6020, 0, 0, 0); b.Call(0x6002, 100, 100, 100); b.ComputedPosition();
        b.Call(0x6014, 0, 0, 30, 15);
        b.Call(0x6020, 14, -6, 13); b.Call(0x6002, -61, -24, -54); b.Return();
        var pose = BattleAtelCameraPoseReader.ReadScript(b.Build(), new[] { 0 });
        Assert.NotNull(pose); Assert.Equal(new float[] { -61, -24, -54 }, pose.Eye);
    }

    [Fact]
    public void BranchesAndActorDependentCallsAreNotGuessed()
    {
        foreach (bool branch in new[] { true, false })
        {
            var b = new ScriptBuilder(); b.Entry();
            b.Call(0x6020, 0, 0, 0); b.Call(0x6002, 10, 20, 30);
            if (branch) b.Branch(); else b.Call(0x6040, 1, 2, 3);
            b.Return();
            Assert.Null(BattleAtelCameraPoseReader.ReadScript(b.Build(), new[] { 0 }));
        }
    }

    [Fact]
    public void MissingWorkerAndNonFiniteValuesDoNotCreateCameras()
    {
        var b = new ScriptBuilder(); b.Entry(); b.Call(0x6020, 0, 0, 0); b.Call(0x6002, float.NaN, 1, 2); b.Return();
        Assert.Null(BattleAtelCameraPoseReader.ReadScript(b.Build(), new[] { 0 }));
        Assert.Null(BattleAtelCameraPoseReader.ReadScript(b.Build(), new[] { 12 }));
        Assert.Null(BattleAtelCameraPoseReader.Read("broken", new byte[] { 255, 255, 255, 127 }));
    }

    [Fact]
    public void UnsupportedCameraWritesInvalidateEarlierConstantValues()
    {
        var b = new ScriptBuilder(); b.Entry();
        b.Call(0x6020, 0, 0, 0); b.Call(0x6002, 10, -20, 30);
        b.Call(0x603C, 1, 2, 3, 4); b.Return();
        Assert.Null(BattleAtelCameraPoseReader.ReadScript(b.Build(), new[] { 0 }));
    }

    [Fact]
    public void CompletedOpeningMovementCanYieldBeforeTheNextCallback()
    {
        var b = new ScriptBuilder(); b.Entry();
        b.Call(0x6020, 0, -22, 31); b.Call(0x6002, 10, -12, 1);
        b.Call(0x601A); b.Call(0x6038); b.Call(0x0000, 20);
        b.Entry(); b.Branch(); b.Return();
        var pose = BattleAtelCameraPoseReader.ReadScript(b.Build(), new[] { 0 });
        Assert.NotNull(pose); Assert.Equal(new float[] { 10, -12, 1 }, pose.Eye);
    }

    sealed class ScriptBuilder
    {
        readonly List<AiInstruction> instructions = new();
        readonly List<int> entries = new();
        readonly byte[] data = new byte[16384];
        int code, poolIndex;
        public void Entry() => entries.Add(code);
        public void Call(ushort function, params float[] args)
        {
            foreach (float value in args)
            {
                BitConverter.GetBytes(value).CopyTo(data, 8192 + 4 * poolIndex);
                Emit(0xAF, (ushort)poolIndex++, AiOperandKind.FloatConst);
            }
            Emit(0xD8, function, AiOperandKind.FuncId);
        }
        public void ComputedPosition()
        { Emit(0x6B); Emit(0x6C); Emit(0x6D); Emit(0xD8, 0x6002, AiOperandKind.FuncId); }
        public void Return() => Emit(0x3C);
        public void Branch() => Emit(0xB0, 0, AiOperandKind.JumpIndex);
        void Emit(byte opcode, ushort operand = 0, AiOperandKind kind = AiOperandKind.None)
        {
            var item = new AiInstruction { Offset = 128 + code, Opcode = opcode, HasOperand = opcode >= 0x80, Operand = operand, OperandKind = kind };
            instructions.Add(item); code += item.Length;
        }
        public AiScriptFile Build() => new()
        {
            OriginalAiFileBytes = data, CodeLength = code, ScriptStart = 128,
            HeaderBytes = Array.Empty<byte>(), DataBytes = Array.Empty<byte>(), Instructions = instructions,
            UnknownOpcodes = Array.Empty<byte>(), CodeWalkClosedExactly = true, FloatPoolOffset = 8192,
            Workers = new[] { new AiWorker { Index = 0, Entrypoints = entries.ToArray(), JumpTargets = Array.Empty<int>() } }
        };
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Magic
{
    // ──────────────────────────────────────────────────────────────
    //  EgoVM (OPU) — 256-opcode dispatch table
    //
    //  Source: docs/magic/EGOVM_OPCODE_CATALOG.md (2026-07-17)
    //  Derived from FFX.exe IDA RE:
    //    - dispatch table @ g_FFX_MagicOpcodeTable_core (0xC48EC8)
    //    - 1024 bytes = 256 entries x 4 bytes (function pointers)
    //
    //  Each opcode maps to:
    //    - 16-bit opcode word (low byte = opcode, bit 0x100 = extended dispatch)
    //    - 1+ bytes of operand data (variable per opcode)
    // ──────────────────────────────────────────────────────────────

    public enum EgoVmOpcode : byte
    {
        RecordEndCleanup        = 0x00,
        ParamLoad               = 0x01,
        RelJump                 = 0x02,
        ParamLoadB              = 0x03,
        RecordArrayGet          = 0x04,
        RootFlag772Branch       = 0x05,
        Unhandled_06            = 0x06,
        Unhandled_07            = 0x07,
        Unhandled_08            = 0x08,
        Stub                    = 0x09,
        SoundParamSetup         = 0x0A,
        SoundParamVariant1      = 0x0B,
        SoundParamVariant2      = 0x0C,
        SoundParamVariant3      = 0x0D,
        SoundParamVariant4      = 0x0E,
        SoundParamVariant5      = 0x0F,
        Unhandled_10            = 0x10,
        Unhandled_11            = 0x11,
        FlowGroup1              = 0x12,
        FlowGroup2              = 0x13,
        FlowGroup3              = 0x14,
        FlowGroup4              = 0x15,
        FlowGroup5              = 0x16,
        FlowGroup6              = 0x17,
        Unhandled_18            = 0x18,
        Unhandled_19            = 0x19,
        BranchMulti             = 0x1A,
        Unhandled_1B            = 0x1B,
        Unhandled_1C            = 0x1C,
        GuardedRecordEnd        = 0x1D,
        SetRecordParamByte      = 0x1E,
        ParamLoadC              = 0x1F,
        Unhandled_20            = 0x20,
        SlotOpVariant           = 0x21,
        SlotPoolAction          = 0x22,
        DrawSetupVariant        = 0x23,
        DrawSetupA              = 0x24,
        DrawSetupB              = 0x25,
        RecordSlotAlloc         = 0x26,
        DrawShapeParticle       = 0x27,
        DrawTexture             = 0x28,
        DrawParticleField       = 0x29,
        DrawParam               = 0x2A,
        DrawMatrixSprite        = 0x2B,
        Unhandled_2C            = 0x2C,
        Unhandled_2D            = 0x2D,
        RecordFlag176Branch     = 0x2E,
        RecordFlag176BranchVar  = 0x2F,
        Unhandled_30            = 0x30,
        Unhandled_31            = 0x31,
        OPURecordSpawn          = 0x32,
        SetRecordField26        = 0x33,
        Unhandled_34            = 0x34,
        BranchSmall             = 0x35,
        KeKernelOp              = 0x36,
        KernelParamSetup        = 0x37,
        ShapeActiveInstanceRT   = 0x38,
        TransformPosRotScale    = 0x39,
        ParamLoadD              = 0x3A,
        Unhandled_3B            = 0x3B,
        Unhandled_3C            = 0x3C,
        ParamLoadE              = 0x3D,
        SlotPoolFlag            = 0x3E,
        BindResidentMotion      = 0x3F,
        DrawSelfContained       = 0x41,
        Material                = 0x42,
        Default                 = 0x44,
        Shape                   = 0x46,
        Matrix                  = 0x48,
        DrawMatrixSubmit        = 0x5D,
        DrawCommit              = 0x7F,
        FieldRender             = 0x80,
        DrawFieldShape          = 0x83,
        DrawFieldTexture        = 0x84,
        DrawSpecial             = 0x88,
        DrawParticleFieldB      = 0x89,
        TextureDispatch         = 0x9C,
        PostProcess             = 0x9F,
        DrawMatrixB             = 0xAB,
        PostProcessII           = 0xAF,
        TransformSpherical      = 0xBC,
        SwitchTransform         = 0xC9,
        TransformX              = 0xCD,
        PostProcessIII          = 0xCF,
        DrawFinal               = 0xDC,
        OPUMode                 = 0xDE,
        PhaseDrain              = 0xEC,
        TransformFinal          = 0xF9,
        MatrixCompose           = 0xFB,
    }

    public sealed class EgoVmOpcodeInfo
    {
        public EgoVmOpcode Opcode { get; init; }
        public byte OpcodeByte => (byte)Opcode;
        public string Name { get; init; } = "";
        public uint HandlerAddress { get; init; }
        public int OperandSize { get; init; }
        public string Category { get; init; } = "";
        public string Description { get; init; } = "";
        public bool IsHandled => HandlerAddress != 0;
    }

    public static class EgoVmOpcodeTable
    {
        private static readonly EgoVmOpcodeInfo[] _entries = BuildTable();
        public static EgoVmOpcodeInfo Get(byte b) => _entries[b];
        public static EgoVmOpcodeInfo Get(EgoVmOpcode o) => _entries[(byte)o];
        public static IReadOnlyList<EgoVmOpcodeInfo> All => _entries;
        public static int HandledCount => _entries.Count(e => e.IsHandled);

        public static int DefaultOperandSize(EgoVmOpcode op) => op switch
        {
            EgoVmOpcode.DrawShapeParticle => 8,
            EgoVmOpcode.DrawTexture       => 8,
            EgoVmOpcode.DrawParticleField => 12,
            EgoVmOpcode.DrawMatrixSprite  => 10,
            EgoVmOpcode.RelJump           => 4,
            EgoVmOpcode.BranchMulti       => 6,
            EgoVmOpcode.BranchSmall       => 2,
            EgoVmOpcode.RecordArrayGet    => 4,
            EgoVmOpcode.ParamLoad         => 4,
            EgoVmOpcode.ParamLoadB        => 4,
            EgoVmOpcode.ParamLoadC        => 4,
            EgoVmOpcode.ParamLoadD        => 4,
            EgoVmOpcode.ParamLoadE        => 4,
            EgoVmOpcode.SoundParamSetup   => 6,
            _                             => 2,
        };

        private static EgoVmOpcodeInfo[] BuildTable()
        {
            var table = new EgoVmOpcodeInfo[256];
            for (int i = 0; i < 256; i++)
            {
                var op = (EgoVmOpcode)i;
                var (cat, desc, handler, opSize) = GetMetadata(op);
                table[i] = new EgoVmOpcodeInfo
                {
                    Opcode = op,
                    Name = op.ToString(),
                    HandlerAddress = handler,
                    OperandSize = opSize == 0 ? DefaultOperandSize(op) : opSize,
                    Category = cat,
                    Description = desc,
                };
            }
            return table;
        }

        private static (string, string, uint, int) GetMetadata(EgoVmOpcode op) => op switch
        {
            EgoVmOpcode.RecordEndCleanup      => ("Record","End of record",0x0080C540,0),
            EgoVmOpcode.ParamLoad             => ("Param","Parameter load into record",0x007FD640,0),
            EgoVmOpcode.RelJump               => ("Flow","Relative jump",0x008169F0,0),
            EgoVmOpcode.ParamLoadB            => ("Param","Parameter load B",0x008158B0,0),
            EgoVmOpcode.RecordArrayGet        => ("Array","Get record from array",0x008173C0,0),
            EgoVmOpcode.RootFlag772Branch     => ("Branch","Branch on flag 772",0x00817830,0),
            EgoVmOpcode.Stub                  => ("Stub","No-op stub",0x0080C650,0),
            EgoVmOpcode.SoundParamSetup       => ("Sound","Sound setup",0x00818550,0),
            EgoVmOpcode.FlowGroup1            => ("Flow","Flow control 1",0x0081A5E0,0),
            EgoVmOpcode.FlowGroup2            => ("Flow","Flow control 2",0x0081A5E0,0),
            EgoVmOpcode.FlowGroup3            => ("Flow","Flow control 3",0x0081A5E0,0),
            EgoVmOpcode.FlowGroup4            => ("Flow","Flow control 4",0x0081A5E0,0),
            EgoVmOpcode.FlowGroup5            => ("Flow","Flow control 5",0x0081A5E0,0),
            EgoVmOpcode.FlowGroup6            => ("Flow","Flow control 6",0x0081A5E0,0),
            EgoVmOpcode.BranchMulti           => ("Branch","Complex dispatch",0x00804760,0),
            EgoVmOpcode.GuardedRecordEnd      => ("Record","Guarded record end",0x0080C7A0,0),
            EgoVmOpcode.SetRecordParamByte    => ("Param","Set param byte",0x00817440,0),
            EgoVmOpcode.ParamLoadC            => ("Param","Parameter load C",0x008169B0,0),
            EgoVmOpcode.SlotOpVariant         => ("Slot","Slot op variant",0x00817200,0),
            EgoVmOpcode.SlotPoolAction        => ("Slot","Slot pool alloc/free",0x00815EE0,0),
            EgoVmOpcode.DrawSetupVariant      => ("Draw","Draw setup variant",0x0081C250,0),
            EgoVmOpcode.DrawSetupA            => ("Draw","Draw setup A",0x0081B9E0,0),
            EgoVmOpcode.RecordSlotAlloc       => ("Record","Slot allocation",0x0080AE80,0),
            EgoVmOpcode.DrawShapeParticle     => ("Draw","Draw shape/particle",0x00808710,0),
            EgoVmOpcode.DrawTexture           => ("Draw","Draw texture-based",0x00808170,0),
            EgoVmOpcode.DrawParticleField     => ("Draw","Draw particle field",0x00808A30,0),
            EgoVmOpcode.DrawParam             => ("Draw","Draw param load",0x0080AF20,0),
            EgoVmOpcode.DrawMatrixSprite      => ("Draw","Draw matrix sprite",0x008142B0,0),
            EgoVmOpcode.RecordFlag176Branch   => ("Branch","Branch on flag+176",0x00817AF0,0),
            EgoVmOpcode.OPURecordSpawn        => ("Spawn","Spawn OPU record",0x0080AF90,0),
            EgoVmOpcode.SetRecordField26      => ("Record","Set field+26",0x00817390,0),
            EgoVmOpcode.BranchSmall           => ("Branch","Small branch",0x00816060,0),
            EgoVmOpcode.KeKernelOp            => ("Kernel","Ke kernel op",0x00819DD0,0),
            EgoVmOpcode.KernelParamSetup      => ("Kernel","Kernel param setup",0x0081B0B0,0),
            EgoVmOpcode.ShapeActiveInstanceRT => ("Shape","Shape active RT",0x00809780,0),
            EgoVmOpcode.TransformPosRotScale  => ("Xform","Pos/Rot/Scale xform",0x008158F0,0),
            EgoVmOpcode.ParamLoadD            => ("Param","Param load D",0x00815860,0),
            EgoVmOpcode.ParamLoadE            => ("Param","Param load E",0x00815FD0,0),
            EgoVmOpcode.SlotPoolFlag          => ("Slot","Slot pool flags",0x00817A30,0),
            EgoVmOpcode.BindResidentMotion    => ("Motion","Bind motion",0x00804400,0),
            _                                 => ("?","Unhandled",0,0),
        };
    }
}

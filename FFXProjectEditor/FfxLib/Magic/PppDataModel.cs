using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Magic
{
    // ──────────────────────────────────────────────────────────────
    //  PPP (Particle Program Protocol) — EgoVM Bytecode DataModel
    //
    //  Source: FFX.exe IDA RE (0x7170F0, 0x712080, 0x711540, 0x715250,
    //          0x71C190), magic_0086.dll (Thundara impact),
    //          magic_0087.dll (Thundara bolt)
    //  Confirmed: 279 opcodes, 40-byte dispatch entries, 16-byte slots
    //  Handler slots: +4 (mode_specific_func_0), +8 (mode_specific_func_1),
    //                 +C (mode_specific_func_2)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Slot types within a PPP program entry (16 bytes each).
    /// The interpreter checks slot+4, then slot+8, then slot+C.
    /// </summary>
    public enum PppSlotType : byte
    {
        /// <summary>Slot +4: mode_specific_func_0 — 4-arg handler (some Ke ops).</summary>
        ModeFunc0 = 0x04,
        /// <summary>Slot +8: mode_specific_func_1 — 3-arg U1 handler (move, color, rand).</summary>
        ModeFunc1 = 0x08,
        /// <summary>Slot +C: mode_specific_func_2 — 3-arg draw/matrix handler.</summary>
        ModeFunc2 = 0x0C,
    }

    /// <summary>
    /// Classification of a PPP opcode by its handler behavior and editing safety.
    /// </summary>
    public enum PppHandlerClass
    {
        /// <summary>Direct operand edit is safe and proven.</summary>
        Editable,
        /// <summary>Indirect — changes affect transforms, resources, or draw state.</summary>
        Indirect,
        /// <summary>Constructor-only — allocates particle/thread data at init.</summary>
        ConstructorOnly,
        /// <summary>No viable handler — all function pointers are null.</summary>
        NoGo,
        /// <summary>Host-resolved at runtime by FFX.exe via host_context table.</summary>
        HostResolved,
        /// <summary>Classification not yet determined.</summary>
        Unknown,
    }

    /// <summary>
    /// All 279 confirmed PPP opcodes, categorized by function.
    /// Source: FFX.exe string scan 0xB4FEB0–0xB513D4 + IDA handler analysis.
    /// </summary>
    public enum PppOpcode : ushort
    {
        // ════════════════════════════════════════════════════════════
        //  Draw (46)
        // ════════════════════════════════════════════════════════════
        PppDrawFilter = 0x0000,
        PppDrawHook = 0x0001,
        PppDrawMatrix = 0x0002,
        PppDrawMatrixFront = 0x0003,
        PppDrawMatrixFrontLoop = 0x0004,
        PppDrawMatrixLoop = 0x0005,
        PppDrawMatrixNoRot = 0x0006,
        PppDrawMatrixWood = 0x0007,
        PppDrawMatrixWoodLoop = 0x0008,
        PppDrawMdl = 0x0009,
        PppDrawMdl2 = 0x000A,
        PppDrawMdl3 = 0x000B,
        PppDrawMdlBS = 0x000C,
        PppDrawMdlCamera = 0x000D,
        PppDrawMdlCameraLoop = 0x000E,
        PppDrawMdlInf = 0x000F,
        PppDrawMdlLoop = 0x0010,
        PppDrawMdlLoopDisPos = 0x0011,
        PppDrawMdlLoopZ = 0x0012,
        PppDrawMdlPSim = 0x0013,
        PppDrawMdlRev = 0x0014,
        PppDrawMdlSea = 0x0015,
        PppDrawMdlSemi = 0x0016,
        PppDrawMdlSemi2 = 0x0017,
        PppDrawMdlSemi3 = 0x0018,
        PppDrawMdlTs = 0x0019,
        PppDrawMdlTs2 = 0x001A,
        PppDrawMdlTs3 = 0x001B,
        PppDrawRain = 0x001C,
        PppDrawShape = 0x001D,
        PppDrawShapeCamera = 0x001E,
        PppDrawShapeCameraDisPos = 0x001F,
        PppDrawShapeField = 0x0020,
        PppDrawShapeFieldGlobal = 0x0021,
        PppDrawShapeFieldRev = 0x0022,
        PppDrawShapeFieldSpd = 0x0023,
        PppDrawShapeRev = 0x0024,
        PppDrawShapeX = 0x0025,
        PppDrawShapeXImm = 0x0026,
        PppDrawSprite = 0x0027,
        // NOTE: These 6 Draw-range entries (0x0028-0x002D) share Yonishi string
        // names with entries in the Light (0x0506-0x0509) and op (0x0406-0x0407)
        // categories.  The Draw-range prefix disambiguates them here.
        PppDrawNeiMdlPointLight = 0x0028,
        PppDrawNeiMdlSemiPointLight = 0x0029,
        PppDrawNeiMdlTsPointLight = 0x002A,
        PppDrawNeiShapePointLight = 0x002B,
        PppDrawMatrix2d = 0x002C,
        PppDrawMdlXZon = 0x002D,

        // ════════════════════════════════════════════════════════════
        //  Ke — Kernel/Effect (106)
        // ════════════════════════════════════════════════════════════
        PppKeAccSpdSv = 0x0100,
        PppKeAcmCic = 0x0101,
        PppKeAcmSolid = 0x0102,
        PppKeBornPtCmpl = 0x0103,
        PppKeBornRnd = 0x0104,
        PppKeBornRnd2 = 0x0105,
        PppKeBornRnd3 = 0x0106,
        PppKeBornRnd4 = 0x0107,
        PppKeBornRnd5 = 0x0108,
        PppKeBornRnd5World = 0x0109,
        PppKeBornRnd6 = 0x010A,
        PppKeDMat = 0x010B,
        PppKeDMatFr = 0x010C,
        PppKeDMatPht = 0x010D,
        PppKeDMatPhtFr = 0x010E,
        PppKeDrct = 0x010F,
        PppKeGrvEff = 0x0110,
        PppKeGrvTgt = 0x0111,
        PppKeHitBall = 0x0112,
        PppKeHitBorn = 0x0113,
        PppKeHitChk = 0x0114,
        PppKeHitChkPxB = 0x0115,
        PppKeHmgEff = 0x0116,
        PppKeLnsArnd = 0x0117,
        PppKeLnsArndT = 0x0118,
        PppKeLnsClm = 0x0119,
        PppKeLnsClmT = 0x011A,
        PppKeLnsCrn = 0x011B,
        PppKeLnsCrnT = 0x011C,
        PppKeLnsFls = 0x011D,
        PppKeLnsFlsT = 0x011E,
        PppKeLnsLp = 0x011F,
        PppKeLnsLpSft = 0x0120,
        PppKeLnsLpT = 0x0121,
        PppKeMatPht = 0x0122,
        PppKeMatSN = 0x0123,
        PppKeMdlBmp = 0x0124,
        PppKeMdlDtt = 0x0125,
        PppKeMdlTfd = 0x0126,
        PppKeMdlTfd2 = 0x0127,
        PppKeMdlTfd3 = 0x0128,
        PppKeMdlTfdUv = 0x0129,
        PppKeMdlTfdUv2 = 0x012A,
        PppKeMdlTfdUv3 = 0x012B,
        PppKeMvYpEff = 0x012C,
        PppKeOfsMatXYZ = 0x012D,
        PppKeOfsPt = 0x012E,
        PppKeParMatR = 0x012F,
        PppKeShpDtt = 0x0130,
        PppKeShpTail = 0x0131,
        PppKeShpTail2 = 0x0132,
        PppKeShpTail2X = 0x0133,
        PppKeShpTail3 = 0x0134,
        PppKeShpTail3X = 0x0135,
        PppKeShpTail3XImm = 0x0136,
        PppKeShpTailLc = 0x0137,
        PppKeShpTailPht = 0x0138,
        PppKeShpTailX = 0x0139,
        PppKeTh = 0x013A,
        PppKeThCp = 0x013B,
        PppKeThCpSft = 0x013C,
        PppKeThHitBorn = 0x013D,
        PppKeThLz = 0x013E,
        PppKeThRes = 0x013F,
        PppKeThRes128 = 0x0140,
        PppKeThRes128x4 = 0x0141,
        PppKeThRes128x8 = 0x0142,
        PppKeThRes16 = 0x0143,
        PppKeThRes16x16 = 0x0144,
        PppKeThRes16x24 = 0x0145,
        PppKeThRes16x4 = 0x0146,
        PppKeThRes16x64 = 0x0147,
        PppKeThRes16x8 = 0x0148,
        PppKeThRes24 = 0x0149,
        PppKeThRes24x16 = 0x014A,
        PppKeThRes24x4 = 0x014B,
        PppKeThRes24x8 = 0x014C,
        PppKeThRes255 = 0x014D,
        PppKeThRes255x4 = 0x014E,
        PppKeThRes32 = 0x014F,
        PppKeThRes32x16 = 0x0150,
        PppKeThRes32x24 = 0x0151,
        PppKeThRes32x32 = 0x0152,
        PppKeThRes32x4 = 0x0153,
        PppKeThRes32x8 = 0x0154,
        PppKeThRes40 = 0x0155,
        PppKeThRes40x16 = 0x0156,
        PppKeThRes40x4 = 0x0157,
        PppKeThRes40x8 = 0x0158,
        PppKeThRes48 = 0x0159,
        PppKeThRes48x16 = 0x015A,
        PppKeThRes48x4 = 0x015B,
        PppKeThRes48x8 = 0x015C,
        PppKeThRes64 = 0x015D,
        PppKeThRes64x16 = 0x015E,
        PppKeThRes64x4 = 0x015F,
        PppKeThRes64x8 = 0x0160,
        PppKeThRes8 = 0x0161,
        PppKeThRes8x128 = 0x0162,
        PppKeThRes8x4 = 0x0163,
        PppKeThSft = 0x0164,
        PppKeThTp = 0x0165,
        PppKeThTp2 = 0x0166,
        PppKeTkFade = 0x0167,
        PppKeZCrct = 0x0168,
        PppKeZCrctShp = 0x0169,

        // ════════════════════════════════════════════════════════════
        //  Rand (33)
        // ════════════════════════════════════════════════════════════
        PppRandCV = 0x0200,
        PppRandChar = 0x0201,
        PppRandDownCV = 0x0202,
        PppRandDownChar = 0x0203,
        PppRandDownFV = 0x0204,
        PppRandDownFloat = 0x0205,
        PppRandDownHCV = 0x0206,
        PppRandDownIV = 0x0207,
        PppRandDownInt = 0x0208,
        PppRandDownShort = 0x0209,
        PppRandFV = 0x020A,
        PppRandFloat = 0x020B,
        PppRandHCV = 0x020C,
        PppRandIV = 0x020D,
        PppRandInt = 0x020E,
        PppRandShort = 0x020F,
        PppRandUpCV = 0x0210,
        PppRandUpChar = 0x0211,
        PppRandUpFV = 0x0212,
        PppRandUpFloat = 0x0213,
        PppRandUpHCV = 0x0214,
        PppRandUpIV = 0x0215,
        PppRandUpInt = 0x0216,
        PppRandUpShort = 0x0217,
        PppSRandCV = 0x0218,
        PppSRandDownCV = 0x0219,
        PppSRandDownFV = 0x021A,
        PppSRandDownHCV = 0x021B,
        PppSRandFV = 0x021C,
        PppSRandHCV = 0x021D,
        PppSRandUpCV = 0x021E,
        PppSRandUpFV = 0x021F,
        PppSRandUpHCV = 0x0220,

        // ════════════════════════════════════════════════════════════
        //  Matrix (29)
        // ════════════════════════════════════════════════════════════
        PppChrSclXYZMatrix = 0x0300,
        PppChrSclXZMatrix = 0x0301,
        PppChrSclYMatrix = 0x0302,
        PppChrXSclXYZMatrix = 0x0303,
        PppChrYSclXYZMatrix = 0x0304,
        PppMatrix = 0x0305,
        PppMatrixFront = 0x0306,
        PppMatrixLoc = 0x0307,
        PppMatrixLoop = 0x0308,
        PppMatrixScl = 0x0309,
        PppMatrixXYZ = 0x030A,
        PppMatrixXZY = 0x030B,
        PppMatrixYXZ = 0x030C,
        PppMatrixYZX = 0x030D,
        PppMatrixZXY = 0x030E,
        PppMatrixZYX = 0x030F,
        PppParMatrix = 0x0310,
        PppSCMatrix = 0x0311,
        PppSDMatrix = 0x0312,
        PppSMatrix = 0x0313,
        PppWMatrix = 0x0314,
        PppWMatrixXYZ = 0x0315,
        PppWMatrixXZY = 0x0316,
        PppWMatrixYXZ = 0x0317,
        PppWMatrixYZX = 0x0318,
        PppWMatrixZXY = 0x0319,
        PppWMatrixZYX = 0x031A,
        PppttMatrixLookAtForward = 0x031B,
        PppttMatrixLookAtForwardShape = 0x031C,

        // ════════════════════════════════════════════════════════════
        //  op — Special Operations (14)
        // ════════════════════════════════════════════════════════════
        PppopBlink = 0x0400,
        PppopBlinkZero = 0x0401,
        PppopBoneLine = 0x0402,
        PppopBoneSpline = 0x0403,
        PppopBoundG = 0x0404,
        PppopCreateRnd1 = 0x0405,
        PppopDrawmatrix2d = 0x0406,
        PppopDrawmdlXZon = 0x0407,
        PppopFlash = 0x0408,
        PppopMonotone = 0x0409,
        PppopPointAngle = 0x040A,
        PppopRandfloatOnce = 0x040B,
        PppopTheWorld = 0x040C,
        PppopTheWorldNobirth = 0x040D,

        // ════════════════════════════════════════════════════════════
        //  Light (12)
        // ════════════════════════════════════════════════════════════
        PppFpPointLight = 0x0500,
        PppFpPointLightModel = 0x0501,
        PppFpPointLightModelScl = 0x0502,
        PppFpPointLightVsf = 0x0503,
        PppFpPointLightVsfScl = 0x0504,
        PppNeiChrPointLight = 0x0505,
        PppNeiDrawMdlPointLight = 0x0506,
        PppNeiDrawMdlSemiPointLight = 0x0507,
        PppNeiDrawMdlTsPointLight = 0x0508,
        PppNeiDrawShapePointLight = 0x0509,
        PppNeiLightEikyo = 0x050A,
        PppNeiPointLight = 0x050B,

        // ════════════════════════════════════════════════════════════
        //  Move (7)
        // ════════════════════════════════════════════════════════════
        PppAngMove = 0x0600,
        PppAngMoveLoop = 0x0601,
        PppColMove = 0x0602,
        PppMove = 0x0603,
        PppMoveLoop = 0x0604,
        PppSclMove = 0x0605,
        PppSclMoveLoop = 0x0606,

        // ════════════════════════════════════════════════════════════
        //  Accele (4)
        // ════════════════════════════════════════════════════════════
        PppAccele = 0x0700,
        PppAngAccele = 0x0701,
        PppColAccele = 0x0702,
        PppSclAccele = 0x0703,

        // ════════════════════════════════════════════════════════════
        //  Point (5)
        // ════════════════════════════════════════════════════════════
        PppPObjPoint = 0x0800,
        PppPoint = 0x0801,
        PppPointAp = 0x0802,
        PppPointLoop = 0x0803,
        PppPointRAp = 0x0804,

        // ════════════════════════════════════════════════════════════
        //  Color (2)
        // ════════════════════════════════════════════════════════════
        PppColor = 0x0900,
        // NOTE: pppColMove also appears at 0x0602 (Move category). The Yonishi
        // string table shares the same name at dispatch table indices 0x0602 and
        // 0x0901. This entry is the Color-category dispatch alias.
        PppColMoveColor = 0x0901,

        // ════════════════════════════════════════════════════════════
        //  Other (20)
        // ════════════════════════════════════════════════════════════
        PppAngle = 0x0A00,
        PppDummyFunc = 0x0A01,
        PppEiWfacc = 0x0A02,
        PppEiWindFun = 0x0A03,
        PppEiZCrctDisPos = 0x0A04,
        PppFaceAp = 0x0A05,
        PppMemAlloc = 0x0A06,
        PppRyjMegaBirth = 0x0A07,
        PppRyjMegaBirthFilter = 0x0A08,
        PppRyjMegaBirthModel = 0x0A09,
        PppRyjMegaBirthModelFilter = 0x0A0A,
        PppScale = 0x0A0B,
        PppSegmentAp = 0x0A0C,
        PppVertexAp = 0x0A0D,
        PppVertexApAt = 0x0A0E,
        PppVertexApDisPos = 0x0A0F,
        PppVertexApLc = 0x0A10,
        PppVertexApWorld = 0x0A11,
        PppVertexAttend = 0x0A12,
        PppVtMime = 0x0A13,

        // ════════════════════════════════════════════════════════════
        //  tt — Transform (1)
        // ════════════════════════════════════════════════════════════
        PppttReflection = 0x0B00,
    }

    /// <summary>
    /// Metadata associated with each known PPP opcode.
    /// </summary>
    /// <param name="Opcode">The opcode identifier.</param>
    /// <param name="Name">Display name (e.g. "pppMove").</param>
    /// <param name="Category">Human-readable category name.</param>
    /// <param name="HandlerSlot">Which slot the handler lives in (+4/+8/+C).</param>
    /// <param name="HandlerClass">Editing safety classification.</param>
    /// <param name="OperandSize">Total operand size in bytes (0 if unknown).</param>
    /// <param name="Description">Brief description of what the opcode does.</param>
    public sealed record PppOpcodeInfo(
        PppOpcode Opcode,
        string Name,
        string Category,
        PppSlotType HandlerSlot,
        PppHandlerClass HandlerClass,
        int OperandSize,
        string Description);

    /// <summary>
    /// A single PPP instruction: opcode + its operand payload.
    /// </summary>
    public readonly struct PppInstruction
    {
        /// <summary>Opcode identifying the operation.</summary>
        public PppOpcode Opcode { get; }

        /// <summary>Raw operand bytes following the opcode in the program entry.</summary>
        public byte[] Operands { get; }

        /// <summary>Total size of this instruction in bytes (opcode + operands).</summary>
        public int TotalSize { get; }

        /// <summary>The handler slot this instruction targets.</summary>
        public PppSlotType SlotType { get; }

        /// <summary>The dispatch entry offset within the program (0, 40, 80, ...).</summary>
        public int EntryOffset { get; }

        public PppInstruction(PppOpcode opcode, byte[] operands, int totalSize,
            PppSlotType slotType, int entryOffset)
        {
            Opcode = opcode;
            Operands = operands ?? [];
            TotalSize = totalSize;
            SlotType = slotType;
            EntryOffset = entryOffset;
        }

        public void Deconstruct(out PppOpcode opcode, out byte[] operands)
        {
            opcode = Opcode;
            operands = Operands;
        }

        public override string ToString()
            => $"{Opcode.GetName()} @+{EntryOffset:X4} slot+{(byte)SlotType:X2} ({Operands.Length}B)";
    }

    /// <summary>
    /// Represents a parsed PPP draw record — the runtime-constructed structure
    /// that the interpreter sends to the renderer.
    /// </summary>
    public sealed class PppDrawRecord
    {
        /// <summary>List of instructions embedded in this record.</summary>
        public List<PppInstruction> Instructions { get; } = [];

        /// <summary>Program entry index this record belongs to.</summary>
        public int ProgramIndex { get; set; }

        /// <summary>Number of particles/slots allocated for this record.</summary>
        public int SlotCount { get; set; }

        /// <summary>Lifetime TTL in frames (from program_struct+38).</summary>
        public short LifetimeTtl { get; set; }

        /// <summary>Frame counter value at slot+12 (0 = removed).</summary>
        public ushort FrameCounter { get; set; }

        /// <summary>Alpha function selector byte (0x41, 0x42, 0x44, 0x46, 0x48, 0x88).</summary>
        public byte AlphaSelector { get; set; }

        /// <summary>Sprite count in this record (from count field).</summary>
        public ushort SpriteCount { get; set; }

        public void AddInstruction(PppInstruction instr)
            => Instructions.Add(instr);

        public override string ToString()
            => $"PppDrawRecord[prog={ProgramIndex}, slots={SlotCount}, "
               + $"spriteCount={SpriteCount}, alpha=0x{AlphaSelector:X2}, "
               + $"instructions={Instructions.Count}]";
    }

    /// <summary>
    /// Alpha selector byte meanings for the PPP draw path.
    /// See FFX_Magic_PPP_ClassifyOpcodeByte (0x712D10).
    /// </summary>
    public static class PppAlphaSelector
    {
        /// <summary>Alpha = 1.0 (full opacity). Multiplier = 256.</summary>
        public const byte Full = 0x41;
        /// <summary>Alpha = 0.0625. Multiplier = 16.</summary>
        public const byte Low = 0x42;
        /// <summary>INVALID — triggers a Virtuos warning.</summary>
        public const byte Invalid = 0x44;
        /// <summary>Alpha = 0.125. Multiplier = 32.</summary>
        public const byte Medium = 0x46;
        /// <summary>Alpha = 0.0078125. Multiplier = 2.</summary>
        public const byte Minimal = 0x48;
        /// <summary>Alpha = 2.0 (boost). Multiplier = 512.</summary>
        public const byte Boost = 0x88;

        /// <summary>Get the alpha multiplier for a selector byte.</summary>
        public static int GetMultiplier(byte selector) => selector switch
        {
            Full => 256,
            Low => 16,
            Invalid => 0,
            Medium => 32,
            Minimal => 2,
            Boost => 512,
            _ => 0,
        };
    }

    // ──────────────────────────────────────────────────────────────
    //  WD3 Container Types (Layer A/B validated)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// WD3 container header (48 bytes) found at the start of the
    /// PPP resource blob in each magic DLL's .data section.
    /// </summary>
    public readonly struct Wd3Header
    {
        /// <summary>Magic bytes: "WD3\x01" (0x01334457 LE).</summary>
        public static ReadOnlySpan<byte> Magic => [0x57, 0x44, 0x33, 0x01];

        /// <summary>Total container size in bytes.</summary>
        public int TotalSize { get; }
        /// <summary>Number of streams (typically 5).</summary>
        public int StreamCount { get; }
        /// <summary>Header size (always 48 = 0x30).</summary>
        public int HeaderSize { get; }
        /// <summary>Offset to the stream pointer table (always 0x20).</summary>
        public int StreamTableOffset => 0x20;
        /// <summary>Offset to stream headers (0x30 + streamCount * 4 + gap).</summary>
        public int StreamHeadersOffset => 0x34;
        /// <summary>Offset to stream data after all headers.</summary>
        public int StreamDataOffset => StreamHeadersOffset + StreamCount * 32;

        public Wd3Header(int totalSize, int streamCount)
        {
            TotalSize = totalSize;
            StreamCount = streamCount;
            HeaderSize = 48;
        }
    }

    /// <summary>
    /// A single WD3 stream header (32 bytes).
    /// Each stream is a logical view over the same body data.
    /// </summary>
    public readonly struct Wd3StreamHeader
    {
        /// <summary>End offset (exclusive, relative to blob base; 0 = end of blob).</summary>
        public int EndOffset { get; }
        /// <summary>Start offset (inclusive, relative to blob base).</summary>
        public int StartOffset { get; }
        /// <summary>Packed metadata (0x5fc600ff typical).</summary>
        public uint Packed1 { get; }
        /// <summary>Packed metadata (0xfe886e2b or 0xfbdd3da7).</summary>
        public uint Packed2 { get; }
        /// <summary>Float scale factor (~3.9921, consistent across streams).</summary>
        public float Scale { get; }

        public Wd3StreamHeader(int endOffset, int startOffset, uint packed1, uint packed2, float scale)
        {
            EndOffset = endOffset;
            StartOffset = startOffset;
            Packed1 = packed1;
            Packed2 = packed2;
            Scale = scale;
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  Lookup helpers
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Provides lookup tables for PPP opcode metadata.
    /// </summary>
    public static class PppOpcodeTable
    {
        private static readonly Dictionary<PppOpcode, PppOpcodeInfo> _infos = [];

        static PppOpcodeTable()
        {
            // Register all known opcodes with their metadata.
            var all = new List<PppOpcodeInfo>
            {
                // Draw
                Info(PppOpcode.PppDrawFilter, "pppDrawFilter", "Draw", PppSlotType.ModeFunc2, PppHandlerClass.Indirect, 8, "Screen-space filter effect"),
                Info(PppOpcode.PppDrawHook, "pppDrawHook", "Draw", PppSlotType.ModeFunc2, PppHandlerClass.Indirect, 8, "Custom draw hook callback"),
                Info(PppOpcode.PppDrawMatrix, "pppDrawMatrix", "Draw", PppSlotType.ModeFunc2, PppHandlerClass.Indirect, 4, "Draw with matrix transform"),
                Info(PppOpcode.PppDrawMdl, "pppDrawMdl", "Draw", PppSlotType.ModeFunc2, PppHandlerClass.Indirect, 8, "Draw model (base handler)"),
                Info(PppOpcode.PppDrawMdlTs, "pppDrawMdlTs", "Draw", PppSlotType.ModeFunc2, PppHandlerClass.Indirect, 8, "Draw textured model"),
                Info(PppOpcode.PppDrawShape, "pppDrawShape", "Draw", PppSlotType.ModeFunc2, PppHandlerClass.Indirect, 8, "Draw shape/primitive"),
                Info(PppOpcode.PppDrawShapeCamera, "pppDrawShapeCamera", "Draw", PppSlotType.ModeFunc2, PppHandlerClass.Indirect, 8, "Draw shape facing camera"),
                Info(PppOpcode.PppDrawSprite, "pppDrawSprite", "Draw", PppSlotType.ModeFunc2, PppHandlerClass.Indirect, 8, "Draw 2D sprite"),
                Info(PppOpcode.PppDrawRain, "pppDrawRain", "Draw", PppSlotType.ModeFunc2, PppHandlerClass.Indirect, 16, "Rain/particle-stream draw"),
                Info(PppOpcode.PppDrawMdlRev, "pppDrawMdlRev", "Draw", PppSlotType.ModeFunc2, PppHandlerClass.Indirect, 8, "Reverse-order model draw"),
                Info(PppOpcode.PppDrawMdlCamera, "pppDrawMdlCamera", "Draw", PppSlotType.ModeFunc2, PppHandlerClass.Indirect, 8, "Model draw aligned to camera"),
                Info(PppOpcode.PppDrawMdlLoop, "pppDrawMdlLoop", "Draw", PppSlotType.ModeFunc2, PppHandlerClass.Indirect, 8, "Looped model draw"),
                Info(PppOpcode.PppDrawMdlInf, "pppDrawMdlInf", "Draw", PppSlotType.ModeFunc2, PppHandlerClass.Indirect, 8, "Infinite-lifetime model draw"),
                Info(PppOpcode.PppDrawMdlBS, "pppDrawMdlBS", "Draw", PppSlotType.ModeFunc2, PppHandlerClass.Indirect, 8, "Billboard/spherical model draw"),
                Info(PppOpcode.PppDrawMdlTs2, "pppDrawMdlTs2", "Draw", PppSlotType.ModeFunc2, PppHandlerClass.Indirect, 56, "Textured model with secondary UV"),
                Info(PppOpcode.PppDrawShapeX, "pppDrawShapeX", "Draw", PppSlotType.ModeFunc2, PppHandlerClass.Indirect, 8, "Extended shape draw"),

                // Ke
                Info(PppOpcode.PppKeThRes, "pppKeThRes", "Ke", PppSlotType.ModeFunc0, PppHandlerClass.ConstructorOnly, 16, "Thread resolution (generic)"),
                Info(PppOpcode.PppKeThRes8, "pppKeThRes8", "Ke", PppSlotType.ModeFunc0, PppHandlerClass.ConstructorOnly, 16, "Thread resolution 8"),
                Info(PppOpcode.PppKeThRes16, "pppKeThRes16", "Ke", PppSlotType.ModeFunc0, PppHandlerClass.ConstructorOnly, 16, "Thread resolution 16"),
                Info(PppOpcode.PppKeThRes32, "pppKeThRes32", "Ke", PppSlotType.ModeFunc0, PppHandlerClass.ConstructorOnly, 16, "Thread resolution 32"),
                Info(PppOpcode.PppKeThRes64, "pppKeThRes64", "Ke", PppSlotType.ModeFunc0, PppHandlerClass.ConstructorOnly, 16, "Thread resolution 64"),
                Info(PppOpcode.PppKeThRes128, "pppKeThRes128", "Ke", PppSlotType.ModeFunc0, PppHandlerClass.ConstructorOnly, 16, "Thread resolution 128"),
                Info(PppOpcode.PppKeThRes255, "pppKeThRes255", "Ke", PppSlotType.ModeFunc0, PppHandlerClass.ConstructorOnly, 16, "Thread resolution 255"),
                Info(PppOpcode.PppKeThRes32x4, "pppKeThRes32x4", "Ke", PppSlotType.ModeFunc0, PppHandlerClass.ConstructorOnly, 16, "Thread resolution 32x4"),
                Info(PppOpcode.PppKeThRes64x4, "pppKeThRes64x4", "Ke", PppSlotType.ModeFunc0, PppHandlerClass.ConstructorOnly, 16, "Thread resolution 64x4"),
                Info(PppOpcode.PppKeTh, "pppKeTh", "Ke", PppSlotType.ModeFunc0, PppHandlerClass.Editable, 56, "Thread spawn (26 fields: 16 WORD + 4 float + 1 byte)"),
                Info(PppOpcode.PppKeThSft, "pppKeThSft", "Ke", PppSlotType.ModeFunc0, PppHandlerClass.Editable, 16, "Thread shift"),
                Info(PppOpcode.PppKeThTp, "pppKeThTp", "Ke", PppSlotType.ModeFunc0, PppHandlerClass.ConstructorOnly, 16, "Thread type"),
                Info(PppOpcode.PppKeBornRnd, "pppKeBornRnd", "Ke", PppSlotType.ModeFunc0, PppHandlerClass.ConstructorOnly, 16, "Born random"),
                Info(PppOpcode.PppKeGrvEff, "pppKeGrvEff", "Ke", PppSlotType.ModeFunc0, PppHandlerClass.Indirect, 16, "Gravity effect"),
                Info(PppOpcode.PppKeGrvTgt, "pppKeGrvTgt", "Ke", PppSlotType.ModeFunc0, PppHandlerClass.Indirect, 16, "Gravity toward target"),
                Info(PppOpcode.PppKeDMat, "pppKeDMat", "Ke", PppSlotType.ModeFunc0, PppHandlerClass.Indirect, 16, "Death matrix transform"),
                Info(PppOpcode.PppKeHitChk, "pppKeHitChk", "Ke", PppSlotType.ModeFunc0, PppHandlerClass.Indirect, 16, "Hit check"),
                Info(PppOpcode.PppKeMdlTfd, "pppKeMdlTfd", "Ke", PppSlotType.ModeFunc0, PppHandlerClass.Indirect, 16, "Model transform"),
                Info(PppOpcode.PppKeZCrctShp, "pppKeZCrctShp", "Ke", PppSlotType.ModeFunc0, PppHandlerClass.NoGo, 16, "Z-correct shape (read-only)"),
                Info(PppOpcode.PppKeShpTail, "pppKeShpTail", "Ke", PppSlotType.ModeFunc0, PppHandlerClass.Indirect, 16, "Shape tail"),
                Info(PppOpcode.PppKeShpTail2, "pppKeShpTail2", "Ke", PppSlotType.ModeFunc0, PppHandlerClass.Indirect, 16, "Shape tail 2"),
                Info(PppOpcode.PppKeLnsArnd, "pppKeLnsArnd", "Ke", PppSlotType.ModeFunc0, PppHandlerClass.Indirect, 16, "Lens around"),
                Info(PppOpcode.PppKeLnsFls, "pppKeLnsFls", "Ke", PppSlotType.ModeFunc0, PppHandlerClass.Indirect, 16, "Lens flash"),
                Info(PppOpcode.PppKeLnsClm, "pppKeLnsClm", "Ke", PppSlotType.ModeFunc0, PppHandlerClass.Indirect, 16, "Lens column"),

                // Move
                Info(PppOpcode.PppMove, "pppMove", "Move", PppSlotType.ModeFunc1, PppHandlerClass.Editable, 16, "Move delta (4 floats, double-layer)"),
                Info(PppOpcode.PppAngMove, "pppAngMove", "Move", PppSlotType.ModeFunc1, PppHandlerClass.Editable, 16, "Angular move delta (4 floats)"),
                Info(PppOpcode.PppSclMove, "pppSclMove", "Move", PppSlotType.ModeFunc1, PppHandlerClass.Editable, 24, "Scale move"),
                Info(PppOpcode.PppColMove, "pppColMove", "Move", PppSlotType.ModeFunc1, PppHandlerClass.Editable, 8, "Color move (4 WORDs)"),
                Info(PppOpcode.PppMoveLoop, "pppMoveLoop", "Move", PppSlotType.ModeFunc1, PppHandlerClass.Editable, 16, "Move delta with loop"),

                // Accele
                Info(PppOpcode.PppAccele, "pppAccele", "Accele", PppSlotType.ModeFunc1, PppHandlerClass.Editable, 16, "Acceleration (4 floats)"),
                Info(PppOpcode.PppAngAccele, "pppAngAccele", "Accele", PppSlotType.ModeFunc1, PppHandlerClass.Editable, 16, "Angular acceleration (4 i32)"),
                Info(PppOpcode.PppSclAccele, "pppSclAccele", "Accele", PppSlotType.ModeFunc1, PppHandlerClass.Editable, 24, "Scale acceleration"),
                Info(PppOpcode.PppColAccele, "pppColAccele", "Accele", PppSlotType.ModeFunc1, PppHandlerClass.Editable, 8, "Color acceleration (4 WORDs)"),

                // Point
                Info(PppOpcode.PppPoint, "pppPoint", "Point", PppSlotType.ModeFunc1, PppHandlerClass.Editable, 16, "Set point/position (4 floats, single-layer)"),
                Info(PppOpcode.PppAngle, "pppAngle", "Other", PppSlotType.ModeFunc1, PppHandlerClass.Editable, 16, "Set angle (4 DWORDs)"),
                Info(PppOpcode.PppScale, "pppScale", "Other", PppSlotType.ModeFunc1, PppHandlerClass.Editable, 16, "Set scale (4 floats)"),
                Info(PppOpcode.PppPointAp, "pppPointAp", "Point", PppSlotType.ModeFunc1, PppHandlerClass.Editable, 16, "Point appearance"),
                Info(PppOpcode.PppPointLoop, "pppPointLoop", "Point", PppSlotType.ModeFunc1, PppHandlerClass.Editable, 16, "Looping point"),

                // Color
                Info(PppOpcode.PppColor, "pppColor", "Color", PppSlotType.ModeFunc1, PppHandlerClass.Editable, 16, "Set color (lowest priority — see ADR-001)"),

                // Matrix
                Info(PppOpcode.PppMatrix, "pppMatrix", "Matrix", PppSlotType.ModeFunc2, PppHandlerClass.Indirect, 4, "Matrix transform (3 selectors)"),
                Info(PppOpcode.PppMatrixXYZ, "pppMatrixXYZ", "Matrix", PppSlotType.ModeFunc2, PppHandlerClass.Indirect, 4, "Matrix Euler ZYX"),
                Info(PppOpcode.PppMatrixFront, "pppMatrixFront", "Matrix", PppSlotType.ModeFunc2, PppHandlerClass.Indirect, 4, "Matrix aligned to camera front"),
                Info(PppOpcode.PppWMatrix, "pppWMatrix", "Matrix", PppSlotType.ModeFunc1, PppHandlerClass.Indirect, 16, "World matrix with PRNG"),

                // Rand
                Info(PppOpcode.PppRandFloat, "pppRandFloat", "Rand", PppSlotType.ModeFunc1, PppHandlerClass.Editable, 16, "Random float"),
                Info(PppOpcode.PppRandInt, "pppRandInt", "Rand", PppSlotType.ModeFunc1, PppHandlerClass.Editable, 16, "Random int"),
                Info(PppOpcode.PppRandShort, "pppRandShort", "Rand", PppSlotType.ModeFunc1, PppHandlerClass.Editable, 16, "Random short"),
                Info(PppOpcode.PppRandChar, "pppRandChar", "Rand", PppSlotType.ModeFunc1, PppHandlerClass.Editable, 16, "Random char/byte"),
                Info(PppOpcode.PppRandHCV, "pppRandHCV", "Rand", PppSlotType.ModeFunc1, PppHandlerClass.Editable, 16, "Random HCV (4 WORD range + byte flag)"),
            };

            foreach (var info in all)
                _infos[info.Opcode] = info;
        }

        private static PppOpcodeInfo Info(PppOpcode opcode, string name, string cat,
            PppSlotType slot, PppHandlerClass cls, int opSize, string desc)
            => new(opcode, name, cat, slot, cls, opSize, desc);

        /// <summary>Look up metadata for an opcode. Returns null if unknown.</summary>
        public static PppOpcodeInfo? GetInfo(PppOpcode opcode)
            => _infos.TryGetValue(opcode, out var info) ? info : null;

        /// <summary>Get the display name for an opcode.</summary>
        public static string GetName(this PppOpcode opcode)
            => GetInfo(opcode)?.Name ?? $"pppUnknown_0x{(ushort)opcode:X4}";

        /// <summary>Get the category for an opcode.</summary>
        public static string GetCategory(this PppOpcode opcode)
            => GetInfo(opcode)?.Category ?? "Unknown";

        /// <summary>Get the handler class for an opcode.</summary>
        public static PppHandlerClass GetHandlerClass(this PppOpcode opcode)
            => GetInfo(opcode)?.HandlerClass ?? PppHandlerClass.Unknown;

        /// <summary>Get the handler slot for an opcode.</summary>
        public static PppSlotType GetHandlerSlot(this PppOpcode opcode)
            => GetInfo(opcode)?.HandlerSlot ?? PppSlotType.ModeFunc1;

        /// <summary>Get the operand size for an opcode.</summary>
        public static int GetOperandSize(this PppOpcode opcode)
            => GetInfo(opcode)?.OperandSize ?? 16;
    }
}

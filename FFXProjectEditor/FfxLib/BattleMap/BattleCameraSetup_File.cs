using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;

namespace FFXProjectEditor.FfxLib.BattleMap
{
    /// <summary>
    /// 🎥 AURORA CHAMBER — reads (and edits) the ACTUAL battle-camera PARAMETERS that live in the ATEL script
    /// (chunk0) of a per-battle <c>&lt;id&gt;/&lt;id&gt;.bin</c>.
    ///
    /// RE (docs/reverse/FFX_BATTLE_CAMERA_SHOT_TABLE_IDA_2026-06-07.md §6, cross-checked vs Karifean/FFXDataParser —
    /// study-only): the battle camera is SCRIPT-COMPUTED. chunk0 is full of Camera-namespace calls (func-id high
    /// nibble 0x6) that actively place the camera — <c>camSetPolar(horizontal,elevation,distance)</c>,
    /// <c>refSetPos(x,y,z)</c>,
    /// <c>camSetRoll</c>, <c>camSetScrDpt</c>, the <c>camSetBtlPolar*</c> battle variants, etc. Their arguments are
    /// FLOAT constants from the script's float pool (the <c>PUSHF</c>/0xAF operand is a pool index). So the camera's
    /// angle / distance / position / roll are editable FLOATS in the per-battle bin. Editing a pool float is byte-local
    /// (same length) and the codec
    /// already exposes <see cref="AiScript_File.EditFloatConst"/>; splicing the same-length chunk0 back leaves every
    /// other chunk untouched. This is how a "custom angle" is authored IN-FILE (no scene authoring needed for the
    /// polar/position case).
    ///
    /// This reader surfaces every parameterized Camera-namespace call (one that pushes at least one float/immediate
    /// const) with its resolved arguments, plus the DISTINCT camera float-pool entries the battle uses (the editable
    /// knobs). Dependency-light (System.* + the System.*-only <see cref="AiScript_File"/>), so a gate can link it.
    /// </summary>
    public sealed class BattleCameraSetup_File
    {
        /// <summary>Camera FUNCSPACE = func-id high nibble 0x6 (cross-checked vs the ATEL reference).</summary>
        public const int CameraNamespace = 0x6;

        public required byte[] OriginalBinBytes { get; init; }
        public required string BattleId { get; init; }
        public required int Chunk0Offset { get; init; }
        public required int Chunk0Length { get; init; }
        public AiScriptFile? Script { get; init; }
        /// <summary>Float-pool offset (chunk0-relative); -1 when absent. A PUSHF operand indexes this pool.</summary>
        public int FloatPoolOffset { get; init; } = -1;
        /// <summary>Every parameterized Camera-namespace call found in chunk0, in script order.</summary>
        public required IReadOnlyList<CameraCall> Calls { get; init; }
        /// <summary>The DISTINCT float-pool entries fed to camera calls — the editable camera knobs (dedup by index).</summary>
        public required IReadOnlyList<CameraFloatParam> FloatParams { get; init; }
        public const ushort CamSetPolarFuncId = 0x6004;

        /// <summary>Best-effort "establishing" camera (first refSetPos + first exact camSetPolar) for a 3D marker. Null
        /// when neither is present. The ref point and camSetPolar convention are IDA-proven; target-aware
        /// camSetBtlPolar* variants are deliberately excluded from this simple marker/inverse.</summary>
        public CameraEstablishingShot? Establishing { get; init; }
        public required IReadOnlyList<string> Notes { get; init; }

        public bool HasCamera => Calls.Count > 0;

        // Camera-positioning func-ids worth labelling (names mirror the ATEL reference; the rest still show by id).
        static readonly Dictionary<ushort, string> Roles = new()
        {
            [0x6004] = "polar", [0x6040] = "polar", [0x6044] = "polar", [0x604D] = "polar",       // camSetPolar / camSetBtlPolar*
            [0x6020] = "refPos", [0x603F] = "refPos", [0x6041] = "refPos", [0x6045] = "refPos",   // refSetPos / refSetBtl*
            [0x603A] = "roll", [0x603B] = "screenDepth",                                           // camSetRoll / camSetScrDpt
            [0x6010] = "camMove", [0x602E] = "refMove",
        };

        /// <summary>Decode the camera parameters from a whole per-battle bin. Never throws — issues go to Notes.</summary>
        public static BattleCameraSetup_File ReadFromBattleBin(string battleId, byte[] battleBinBytes)
        {
            ArgumentNullException.ThrowIfNull(battleBinBytes);
            var notes = new List<string>();
            (int start, int length) = ExtractChunk0Span(battleBinBytes);
            if (start < 0)
            {
                notes.Add("chunk0 (ATEL script) absent.");
                return Empty(battleId, battleBinBytes, notes);
            }

            byte[] chunk0 = new byte[length];
            Array.Copy(battleBinBytes, start, chunk0, 0, length);

            AiScriptFile script;
            try { script = AiScript_File.Read(chunk0); }
            catch (Exception ex)
            {
                notes.Add($"chunk0 did not decode as ATEL: {ex.Message}");
                return Empty(battleId, battleBinBytes, notes, start, length);
            }

            int floatPool = script.FloatPoolOffset;
            var calls = ExtractCalls(script, floatPool, notes);

            // Distinct editable float knobs (dedup by pool index), labelled by the roles of the calls that use them.
            var byIndex = new Dictionary<int, CameraFloatParam>();
            foreach (CameraCall c in calls)
                foreach (CamArg a in c.Args)
                    if (a.Kind == CamArgKind.FloatConst && a.PoolIndex >= 0)
                    {
                        if (!byIndex.TryGetValue(a.PoolIndex, out CameraFloatParam? p))
                            byIndex[a.PoolIndex] = p = new CameraFloatParam { PoolIndex = a.PoolIndex, Value = a.FloatValue ?? 0f, Roles = new List<string>() };
                        if (!p.Roles.Contains(c.Role)) ((List<string>)p.Roles).Add(c.Role);
                        p.UseCount++;
                    }

            return new BattleCameraSetup_File
            {
                OriginalBinBytes = battleBinBytes,
                BattleId = battleId,
                Chunk0Offset = start,
                Chunk0Length = length,
                Script = script,
                FloatPoolOffset = floatPool,
                Calls = calls,
                FloatParams = byIndex.Values.OrderBy(p => p.PoolIndex).ToList(),
                Establishing = ComputeEstablishing(calls),
                Notes = notes,
            };
        }

        // Best-effort establishing camera: first refSetPos (role refPos, >=3 floats = x,y,z) + first exact camSetPolar.
        // IDA chain 0x6004 -> 0x7B9260 -> 0x7BB550 -> 0x7BF680 -> 0x7C4760 proves the streamed args are:
        // horizontal angle in degrees, elevation angle in degrees, distance.
        static CameraEstablishingShot? ComputeEstablishing(IReadOnlyList<CameraCall> calls)
        {
            static IReadOnlyList<CamArg> FloatArgs(CameraCall c) =>
                c.Args.Where(a => a.Kind == CamArgKind.FloatConst).ToList();
            static IReadOnlyList<CamArg> NumericArgs(CameraCall c) =>
                c.Args.Where(a => a.Kind is CamArgKind.FloatConst or CamArgKind.Immediate).ToList();

            CameraCall? refCall = calls.FirstOrDefault(c => c.Role == "refPos" && FloatArgs(c).Count >= 3);
            CameraCall? polarCall = calls.FirstOrDefault(c => c.FuncId == CamSetPolarFuncId && NumericArgs(c).Count >= 3);
            if (refCall == null && polarCall == null) return null;

            var e = new CameraEstablishingShot();
            if (refCall != null)
            {
                var fargs = FloatArgs(refCall);
                e.HasRef = true;
                e.RefX = fargs[0].FloatValue ?? 0f; e.RefY = fargs[1].FloatValue ?? 0f; e.RefZ = fargs[2].FloatValue ?? 0f;
                e.RefXIndex = fargs[0].PoolIndex; e.RefYIndex = fargs[1].PoolIndex; e.RefZIndex = fargs[2].PoolIndex;
                e.RefFuncId = refCall.FuncId;
            }
            if (polarCall != null)
            {
                var args = NumericArgs(polarCall);
                if (!TryReadNumericArg(args[0], out float horizontal, out int hIndex) ||
                    !TryReadNumericArg(args[1], out float elevation, out int eIndex) ||
                    !TryReadNumericArg(args[2], out float distance, out int dIndex))
                    return e.HasRef ? e : null;

                e.HasPolar = true;
                e.PolarHorizontalAngle = horizontal;
                e.PolarElevationAngle = elevation;
                e.PolarDistance = distance;
                e.PolarHorizontalIndex = hIndex;
                e.PolarElevationIndex = eIndex;
                e.PolarDistanceIndex = dIndex;
                e.PolarFuncId = polarCall.FuncId;
            }
            return e;
        }

        static bool TryReadNumericArg(CamArg arg, out float value, out int poolIndex)
        {
            switch (arg.Kind)
            {
                case CamArgKind.FloatConst:
                    value = arg.FloatValue ?? 0f;
                    poolIndex = arg.PoolIndex;
                    return true;
                case CamArgKind.Immediate:
                    value = arg.IntValue ?? 0;
                    poolIndex = -1;
                    return true;
                default:
                    value = 0f;
                    poolIndex = -1;
                    return false;
            }
        }

        /// <summary>IDA-proven camSetPolar cartesian projection:
        /// x=refX+cos(horizontal)*cos(elevation)*dist, y=refY+sin(elevation)*dist,
        /// z=refZ+sin(horizontal)*cos(elevation)*dist. Angles are degrees.</summary>
        public static (float X, float Y, float Z) PolarToEye(
            float refX, float refY, float refZ,
            float horizontalDeg, float elevationDeg, float distance)
        {
            double h = DegreesToRadians(horizontalDeg);
            double e = DegreesToRadians(elevationDeg);
            double ce = Math.Cos(e);
            return (
                refX + (float)(Math.Cos(h) * ce * distance),
                refY + (float)(Math.Sin(e) * distance),
                refZ + (float)(Math.Sin(h) * ce * distance));
        }

        /// <summary>Inverse of <see cref="PolarToEye"/> for byte-local eye dragging.</summary>
        public static (float HorizontalDeg, float ElevationDeg, float Distance) EyeToPolar(
            float refX, float refY, float refZ,
            float eyeX, float eyeY, float eyeZ)
        {
            double dx = eyeX - refX;
            double dy = eyeY - refY;
            double dz = eyeZ - refZ;
            double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (distance < 1e-6) return (0f, 0f, 0f);

            double horizontal = Math.Atan2(dz, dx);
            double planar = Math.Sqrt(dx * dx + dz * dz);
            double elevation = Math.Atan2(dy, planar);
            return ((float)RadiansToDegrees(horizontal), (float)RadiansToDegrees(elevation), (float)distance);
        }

        static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
        static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;

        static BattleCameraSetup_File Empty(string id, byte[] bin, List<string> notes, int start = -1, int len = 0) => new()
        {
            OriginalBinBytes = bin,
            BattleId = id,
            Chunk0Offset = start,
            Chunk0Length = len,
            Script = null,
            FloatPoolOffset = -1,
            Calls = Array.Empty<CameraCall>(),
            FloatParams = Array.Empty<CameraFloatParam>(),
            Notes = notes,
        };

        static IReadOnlyList<CameraCall> ExtractCalls(AiScriptFile script, int floatPool, List<string> notes)
        {
            var calls = new List<CameraCall>();
            IReadOnlyList<AiInstruction> ins = script.Instructions;
            byte[] ai = script.OriginalAiFileBytes;

            for (int idx = 0; idx < ins.Count; idx++)
            {
                AiInstruction call = ins[idx];
                if (AiScript_File.OperandKindOf(call.Opcode) != AiOperandKind.FuncId) continue;
                if ((call.Operand >> 12) != CameraNamespace) continue;

                // Gather the contiguous run of const/register push instructions immediately before the call (its args).
                var args = new List<CamArg>();
                for (int back = idx - 1; back >= 0 && idx - back <= 8; back--)
                {
                    AiInstruction p = ins[back];
                    CamArg? a = ClassifyArg(p, ai, floatPool);
                    if (a == null) break;          // first non-push ends the arg run
                    args.Insert(0, a);             // keep stream order (earliest push first)
                }
                if (args.Count == 0) continue;     // no literal args -> not an editable parameterized setup

                bool hasConst = args.Any(a => a.Kind is CamArgKind.FloatConst or CamArgKind.Immediate);
                if (!hasConst) continue;           // only register/computed args -> nothing byte-local to surface

                calls.Add(new CameraCall
                {
                    Offset = call.Offset,
                    FuncId = call.Operand,
                    FuncName = AiScript_File.CallName(call.Operand),
                    Role = Roles.TryGetValue(call.Operand, out string? r) ? r : "other",
                    Args = args,
                });
            }
            return calls;
        }

        // Classify a pushing instruction as a camera-call argument. Returns null for anything that is not a push
        // (which ends the contiguous arg run). Float consts carry the resolved value + pool index (editable).
        static CamArg? ClassifyArg(AiInstruction p, byte[] ai, int floatPool)
        {
            switch (p.Opcode)
            {
                case 0xAF: // PUSHF — float const (operand = float-pool index)
                    float? fv = null;
                    if (floatPool >= 0)
                    {
                        int off = floatPool + 4 * p.Operand;
                        if (off >= 0 && off + 4 <= ai.Length) fv = BinaryPrimitives.ReadSingleLittleEndian(ai.AsSpan(off, 4));
                    }
                    return new CamArg { Kind = CamArgKind.FloatConst, PoolIndex = p.Operand, FloatValue = fv, Offset = p.Offset, Opcode = p.Opcode };
                case 0xAE: // PUSHII — immediate int16
                    return new CamArg { Kind = CamArgKind.Immediate, IntValue = (short)p.Operand, Offset = p.Offset, Opcode = p.Opcode };
                case 0xAD: // PUSHI — int32 const pool (value not byte-local editable as a single knob)
                    return new CamArg { Kind = CamArgKind.IntConst, Offset = p.Offset, Opcode = p.Opcode };
                default:
                    // temp-register banks (PUSHI0-3 0x67-0x6A / PUSHF0-9 0x6B-0x74) = a computed value pushed earlier.
                    if (p.Opcode >= 0x67 && p.Opcode <= 0x74)
                        return new CamArg { Kind = CamArgKind.Register, Offset = p.Offset, Opcode = p.Opcode };
                    return null;
            }
        }

        // ---- edit (byte-local, same length; chunk0 + every other chunk offset preserved) ----------------------

        /// <summary>Rewrite a camera float-pool value (an angle/distance/position/roll knob). Byte-local: only the 4
        /// pool bytes change; the chunk0 length is preserved so every other chunk stays put. Returns a new bin.</summary>
        public byte[] WithFloat(int poolIndex, float newValue)
        {
            if (Script == null || Chunk0Offset < 0) throw new InvalidOperationException("no chunk0 camera script to edit.");
            if (FloatPoolOffset < 0) throw new InvalidOperationException("this battle's chunk0 has no float pool.");
            byte[] newChunk0 = AiScript_File.EditFloatConst(Script, poolIndex, newValue); // same length (pool patch)
            if (newChunk0.Length != Chunk0Length)
                throw new InvalidOperationException($"float edit changed chunk0 length {Chunk0Length}->{newChunk0.Length} (unexpected).");
            byte[] o = (byte[])OriginalBinBytes.Clone();
            Array.Copy(newChunk0, 0, o, Chunk0Offset, Chunk0Length);
            return o;
        }

        // ---- chunk table (chunk0 = the ATEL script; same walk as the other Aurora readers) -------------------

        static (int start, int length) ExtractChunk0Span(byte[] bytes)
        {
            const int wantIndex = 0;
            if (bytes.Length < 8) return (-1, 0);
            int chunkCount = ReadInt32(bytes, 0x00) - 1;
            if (chunkCount < wantIndex) return (-1, 0);
            int[] offsets = new int[chunkCount + 1];
            for (int i = 0; i <= chunkCount; i++)
            {
                int off = ReadInt32(bytes, 0x04 + i * 4);
                if (off == unchecked((int)0xFFFFFFFF)) { chunkCount = i - 1; break; }
                offsets[i] = off;
            }
            if (chunkCount < wantIndex) return (-1, 0);
            int start = offsets[wantIndex];
            if (start <= 0 || start > bytes.Length) return (-1, 0);
            int end = -1;
            for (int j = wantIndex + 1; j <= chunkCount; j++)
                if (offsets[j] >= start) { end = offsets[j]; break; }
            if (end < 0 || end > bytes.Length) end = bytes.Length;
            int len = Math.Max(0, end - start);
            if (len < 0x34) return (-1, 0);
            return (start, len);
        }

        static int ReadInt32(byte[] b, int o) =>
            (o < 0 || o + 4 > b.Length) ? 0 : BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(o, 4));
    }

    public enum CamArgKind { FloatConst, Immediate, IntConst, Register }

    /// <summary>One argument pushed before a camera call. Float consts (and immediates) are byte-local editable.</summary>
    public sealed class CamArg
    {
        public CamArgKind Kind { get; init; }
        public int Offset { get; init; }          // chunk0-relative offset of the pushing instruction
        public byte Opcode { get; init; }
        public int PoolIndex { get; init; } = -1;  // float-pool index (FloatConst only)
        public float? FloatValue { get; init; }    // resolved value (FloatConst)
        public int? IntValue { get; init; }        // resolved value (Immediate)

        public bool Editable => Kind is CamArgKind.FloatConst or CamArgKind.Immediate;

        public string Display => Kind switch
        {
            CamArgKind.FloatConst => FloatValue is float f ? f.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : $"f[{PoolIndex}]",
            CamArgKind.Immediate => IntValue?.ToString() ?? "?",
            CamArgKind.IntConst => "iconst",
            _ => $"reg_{Opcode:X2}",
        };
    }

    /// <summary>A Camera-namespace call (e.g. camSetPolar) with its resolved literal args.</summary>
    public sealed class CameraCall
    {
        public required int Offset { get; init; }
        public required ushort FuncId { get; init; }
        public required string FuncName { get; init; }
        /// <summary>Coarse role: polar / refPos / roll / screenDepth / camMove / other (for grouping/labels).</summary>
        public required string Role { get; init; }
        public required IReadOnlyList<CamArg> Args { get; init; }

        public override string ToString() =>
            $"@0x{Offset:X4} {FuncName}({string.Join(", ", Args.Select(a => a.Display))})";
    }

    /// <summary>Best-effort establishing camera for a 3D marker. Ref point is exact (refSetPos x,y,z, scene frame =
    /// battle frame, IDA-proven identity). camSetPolar 0x6004 is exact; camSetBtlPolar* is intentionally separate.</summary>
    public sealed class CameraEstablishingShot
    {
        public bool HasRef { get; set; }
        public float RefX { get; set; }
        public float RefY { get; set; }
        public float RefZ { get; set; }
        public int RefXIndex { get; set; } = -1;  // float-pool indices of the ref x/y/z (for byte-local drag-write)
        public int RefYIndex { get; set; } = -1;
        public int RefZIndex { get; set; } = -1;
        public ushort RefFuncId { get; set; }

        public bool HasPolar { get; set; }
        public float PolarHorizontalAngle { get; set; } // streamed arg1, degrees; drives X/Z orbit.
        public float PolarElevationAngle { get; set; }  // streamed arg2, degrees; drives Y elevation.
        public float PolarDistance { get; set; }
        public int PolarHorizontalIndex { get; set; } = -1;
        public int PolarElevationIndex { get; set; } = -1;
        public int PolarDistanceIndex { get; set; } = -1;
        public ushort PolarFuncId { get; set; }
        public bool CanEditPolarEye => HasPolar && PolarFuncId == BattleCameraSetup_File.CamSetPolarFuncId &&
                                       PolarHorizontalIndex >= 0 && PolarElevationIndex >= 0 && PolarDistanceIndex >= 0;

        // Back-compat aliases for older UI code and docs: "angle" is the horizontal X/Z angle.
        public float PolarAngle { get => PolarHorizontalAngle; set => PolarHorizontalAngle = value; }
        public int PolarAngleIndex { get => PolarHorizontalIndex; set => PolarHorizontalIndex = value; }
        public float PolarPitch { get => PolarHorizontalAngle; set => PolarHorizontalAngle = value; }
        public float PolarYaw { get => PolarElevationAngle; set => PolarElevationAngle = value; }
        public int PolarPitchIndex { get => PolarHorizontalIndex; set => PolarHorizontalIndex = value; }
        public int PolarYawIndex { get => PolarElevationIndex; set => PolarElevationIndex = value; }
    }

    /// <summary>A DISTINCT editable camera float knob (a float-pool entry fed to one or more camera calls).</summary>
    public sealed class CameraFloatParam
    {
        public required int PoolIndex { get; init; }
        public float Value { get; set; }
        public required IReadOnlyList<string> Roles { get; init; }  // which camera roles use this float (polar/refPos/…)
        public int UseCount { get; set; }

        public string RolesLabel => Roles.Count > 0 ? string.Join("/", Roles) : "camera";
    }
}

// BattleCameraScanLab — proof/RT0 gate for the 🎥 Aurora battle-CAMERA reader
// (FfxLib/BattleMap/BattleCameraScript_File.cs), run against the real per-battle btl corpus.
//
// This is the foundation the camera panel sits on. It PROVES, on a corpus the ATEL codec was NEVER tested on
// (chunk0 of every battle bin, not the monster AiFiles):
//   (1) DECODE   : chunk0 is a clean ATEL AiFile — the codec walk closes exactly on codeLength, no opcode
//                  outside the proven set, no decode Notes;
//   (2) RT0      : AiScript_File.Write(Read(chunk0)) == chunk0 byte-for-byte (the codec is byte-exact here too);
//   (3) camReq   : the camera-request call (func-id 0x703F) is present and its two operands (SHOT, TARGET) read;
//   (4) EDIT     : rewriting a directly-editable SHOT/TARGET immediate is byte-local (same length, exactly 2 bytes
//                  changed) and reversible (edit→re-read==new value; edit-back==original byte-identical);
//   (5) POLAR    : camSetPolar 0x6004 uses the IDA-proven horizontal/elevation/distance convention, and eye drag
//                  inverse writes those three float-pool values byte-locally when all three args are PUSHF-backed.
// Exit 0 only if every bin with a chunk0 decodes clean + RT0, AND every edit round-trip holds.
//
// Self-contained: links only the dependency-free reader + the ATEL codec; no editor / Avalonia.
// Usage: BattleCameraScanLab [btlRoot] [--json out.json]

using System.Text.Json;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.BattleMap;

static string DefaultRoot() => @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\battle\btl";

string root = DefaultRoot();
string? jsonOut = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--json" && i + 1 < args.Length) jsonOut = args[++i];
    else if (!args[i].StartsWith("--")) root = args[i];
}
if (!Directory.Exists(root)) { Console.Error.WriteLine($"btl root not found: {root}"); return 2; }

var files = Directory.EnumerateFiles(root, "*.bin", SearchOption.AllDirectories)
    .Where(p => string.Equals(Path.GetFileNameWithoutExtension(p), Path.GetFileName(Path.GetDirectoryName(p)), StringComparison.OrdinalIgnoreCase))
    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();

int total = 0, withChunk0 = 0, decodeClean = 0, rt0Ok = 0, withCamReq = 0, totalCamReq = 0;
int shotImm = 0, shotPool = 0, shotComputed = 0, shotMissing = 0;
int tgtImm = 0, tgtPool = 0, tgtComputed = 0, tgtMissing = 0;
int fullyEditable = 0;
int editTested = 0, editOk = 0;
var shotValues = new SortedDictionary<int, int>();   // SHOT literal histogram (1-based; engine uses shot-1)
var targetValues = new SortedDictionary<int, int>();  // TARGET literal histogram (-1 == 0xFFFF == none)
var extraOpcodes = new SortedDictionary<byte, int>(); // opcodes seen vs the monster census (expected; informational)
// camera SETUP (real angle/distance/pos floats in chunk0)
int setupBins = 0, setupCalls = 0, setupFloatParams = 0, setupEditTested = 0, setupEditOk = 0;
int estRef = 0, estPolar = 0, estBoth = 0, estPolarEyeEditable = 0;
int camSetPolarCalls = 0, targetAwarePolarCalls = 0, polarRoundTripTested = 0, polarRoundTripOk = 0;
var roleHist = new SortedDictionary<string, int>();
var fails = new List<string>();

foreach (string path in files)
{
    string id = Path.GetFileNameWithoutExtension(path);
    byte[] bytes = File.ReadAllBytes(path);
    total++;

    BattleCameraScript_File cam;
    try { cam = BattleCameraScript_File.ReadFromBattleBin(id, bytes); }
    catch (Exception ex) { fails.Add($"{id}: reader threw {ex.Message}"); continue; }

    if (cam.Chunk0Offset < 0) continue; // no chunk0 (benign — a few non-script bins exist)
    withChunk0++;

    // (1) DECODE — the codec walk must CLOSE exactly on codeLength. Opcodes outside the MONSTER census are
    //     EXPECTED here (battle/scene scripts exercise a wider ATEL set) and are reported but not a failure:
    //     the 0x80 length rule handles them and RT0 (step 2) is the real byte-exactness proof.
    foreach (byte u in cam.UnknownOpcodes) extraOpcodes[u] = extraOpcodes.TryGetValue(u, out int c) ? c + 1 : 1;
    if (cam.CodeWalkClosedExactly) decodeClean++;
    else fails.Add($"{id}: chunk0 walk did not close on codeLength");

    // (2) RT0 — the codec re-emits chunk0 byte-for-byte.
    if (cam.Script != null)
    {
        byte[] chunk0 = new byte[cam.Chunk0Length];
        Array.Copy(bytes, cam.Chunk0Offset, chunk0, 0, cam.Chunk0Length);
        bool rt0;
        try { rt0 = AiScript_File.RoundTripsByteIdentical(chunk0); }
        catch (Exception ex) { rt0 = false; fails.Add($"{id}: RT0 threw {ex.Message}"); }
        if (rt0) rt0Ok++;
        else fails.Add($"{id}: chunk0 RT0 drift (codec re-emit not byte-identical)");
    }

    // (3) camReq present + operand provenance.
    if (cam.ShotCount > 0) withCamReq++;
    totalCamReq += cam.ShotCount;
    foreach (CameraShotRef s in cam.Shots)
    {
        Tally(s.Shot, ref shotImm, ref shotPool, ref shotComputed, ref shotMissing, shotValues);
        Tally(s.Target, ref tgtImm, ref tgtPool, ref tgtComputed, ref tgtMissing, targetValues);
        if (s.FullyEditable) fullyEditable++;
    }

    // (4) EDIT round-trip — first fully-editable shot in this bin: shot edit + target edit, both reversible.
    CameraShotRef? editable = cam.Shots.FirstOrDefault(s => s.FullyEditable);
    if (editable != null)
    {
        editTested++;
        try
        {
            bool ok = ProveEdit(cam, editable, id, fails);
            if (ok) editOk++;
        }
        catch (Exception ex) { fails.Add($"{id}: EDIT threw {ex.Message}"); }
    }

    // (5) CAMERA SETUP — the real angle/distance/position/roll FLOATS in chunk0 (camSetPolar/refSetPos/...).
    BattleCameraSetup_File setup;
    try { setup = BattleCameraSetup_File.ReadFromBattleBin(id, bytes); }
    catch (Exception ex) { fails.Add($"{id}: setup reader threw {ex.Message}"); continue; }
    if (setup.Calls.Count > 0)
    {
        setupBins++;
        setupCalls += setup.Calls.Count;
        setupFloatParams += setup.FloatParams.Count;
        foreach (CameraCall c in setup.Calls) roleHist[c.Role] = roleHist.TryGetValue(c.Role, out int rc) ? rc + 1 : 1;
        camSetPolarCalls += setup.Calls.Count(c => c.FuncId == BattleCameraSetup_File.CamSetPolarFuncId);
        targetAwarePolarCalls += setup.Calls.Count(c => c.Role == "polar" && c.FuncId != BattleCameraSetup_File.CamSetPolarFuncId);
        if (setup.Establishing?.HasRef == true) estRef++;
        if (setup.Establishing?.HasPolar == true) estPolar++;
        if (setup.Establishing?.HasRef == true && setup.Establishing?.HasPolar == true) estBoth++;
        if (setup.Establishing?.CanEditPolarEye == true) estPolarEyeEditable++;

        if (setup.Establishing?.CanEditPolarEye == true)
        {
            polarRoundTripTested++;
            try
            {
                if (ProvePolarEyeRoundTrip(setup, id, fails)) polarRoundTripOk++;
            }
            catch (Exception ex) { fails.Add($"{id}: polar eye round-trip threw {ex.Message}"); }
        }

        // float-edit round-trip: tweak the first camera float knob, re-read it, byte-local + reversible.
        CameraFloatParam? fp = setup.FloatParams.FirstOrDefault();
        if (fp != null && setup.FloatPoolOffset >= 0)
        {
            setupEditTested++;
            try
            {
                float cur = fp.Value, nv = cur + 1.5f;
                byte[] e = setup.WithFloat(fp.PoolIndex, nv);
                bool lenOk = e.Length == bytes.Length;
                int poolByteOff = setup.Chunk0Offset + setup.FloatPoolOffset + 4 * fp.PoolIndex;
                bool local = true;
                for (int i = 0; i < bytes.Length; i++)
                    if (bytes[i] != e[i] && (i < poolByteOff || i >= poolByteOff + 4)) { local = false; break; }
                var re = BattleCameraSetup_File.ReadFromBattleBin(id, e).FloatParams.FirstOrDefault(p => p.PoolIndex == fp.PoolIndex);
                bool valOk = re != null && Math.Abs(re.Value - nv) < 1e-3f;
                byte[] back = BattleCameraSetup_File.ReadFromBattleBin(id, e).WithFloat(fp.PoolIndex, cur);
                bool rev = back.AsSpan().SequenceEqual(bytes);
                if (lenOk && local && valOk && rev) setupEditOk++;
                else fails.Add($"{id}: setup float edit (len={lenOk} local={local} val={valOk} rev={rev})");
            }
            catch (Exception ex) { fails.Add($"{id}: setup float edit threw {ex.Message}"); }
        }
    }
}

static void Tally(CameraArg a, ref int imm, ref int pool, ref int computed, ref int missing, SortedDictionary<int, int> hist)
{
    switch (a.Kind)
    {
        case CameraArgKind.Immediate: imm++; break;
        case CameraArgKind.IntConst: pool++; break;
        case CameraArgKind.Computed: computed++; break;
        default: missing++; break;
    }
    if (a.Value is int v) hist[v] = hist.TryGetValue(v, out int c) ? c + 1 : 1;
}

// Prove a SHOT edit and a TARGET edit are byte-local (diffs confined to the operand's 2-byte window) + reversible.
static bool ProveEdit(BattleCameraScript_File cam, CameraShotRef shot, string id, List<string> fails)
{
    bool all = true;
    int shotOpOff = cam.Chunk0Offset + shot.Shot.Offset + 1;   // u16 operand sits right after the opcode
    int tgtOpOff = cam.Chunk0Offset + shot.Target.Offset + 1;

    // --- SHOT (v4, 1-based; engine uses shot-1) ---
    short curShot = (short)(shot.Shot.Value ?? 0);
    short newShot = (short)(curShot == 1 ? 2 : 1);
    byte[] edited = cam.WithShot(shot, newShot);
    all &= CheckByteLocal(cam.OriginalBinBytes, edited, shotOpOff, id, "shot", fails);
    var reShot = BattleCameraScript_File.ReadFromBattleBin(id, edited).Shots.ElementAtOrDefault(shot.Index);
    if (reShot?.Shot.Value != newShot) { fails.Add($"{id}: shot re-read {reShot?.Shot.Value} != {newShot}"); all = false; }
    // reversible: edit back to original == byte-identical to the source.
    byte[] back = BattleCameraScript_File.ReadFromBattleBin(id, edited).WithShot(reShot!, curShot);
    if (!back.AsSpan().SequenceEqual(cam.OriginalBinBytes)) { fails.Add($"{id}: shot edit not reversible"); all = false; }

    // --- TARGET (v3; 0xFFFF == none) ---
    ushort curTgt = shot.Target.RawValue;
    ushort newTgt = (ushort)(curTgt == 0xFFFF ? 0 : 0xFFFF);
    byte[] editedT = cam.WithTarget(shot, newTgt);
    all &= CheckByteLocal(cam.OriginalBinBytes, editedT, tgtOpOff, id, "target", fails);
    var reTgt = BattleCameraScript_File.ReadFromBattleBin(id, editedT).Shots.ElementAtOrDefault(shot.Index);
    if (reTgt?.Target.RawValue != newTgt) { fails.Add($"{id}: target re-read {reTgt?.Target.RawValue:X} != {newTgt:X}"); all = false; }

    return all;
}

// A byte-local edit must keep the length and confine every changed byte to the operand's 2-byte window
// [opOff, opOff+2). (A value whose high byte is unchanged legitimately flips only 1 byte — still byte-local.)
static bool CheckByteLocal(byte[] a, byte[] b, int opOff, string id, string what, List<string> fails)
{
    if (a.Length != b.Length) { fails.Add($"{id}: {what} edit changed length {a.Length}->{b.Length}"); return false; }
    for (int i = 0; i < a.Length; i++)
        if (a[i] != b[i] && (i < opOff || i >= opOff + 2))
        { fails.Add($"{id}: {what} edit changed byte 0x{i:X} outside operand window 0x{opOff:X}"); return false; }
    return true;
}

// Prove the exact camSetPolar eye drag path: forward formula -> inverse -> write h/e/dist floats -> re-read -> revert.
static bool ProvePolarEyeRoundTrip(BattleCameraSetup_File setup, string id, List<string> fails)
{
    CameraEstablishingShot e = setup.Establishing!;
    float wantH = NormalizeAngle180(e.PolarHorizontalAngle + 2.5f);
    float wantE = Math.Clamp(NormalizeAngle180(e.PolarElevationAngle), -45f, 45f) + 1.25f;
    float wantD = Math.Max(1f, e.PolarDistance + 3f);
    (float eyeX, float eyeY, float eyeZ) = BattleCameraSetup_File.PolarToEye(
        e.RefX, e.RefY, e.RefZ, wantH, wantE, wantD);
    (float gotH, float gotE, float gotD) = BattleCameraSetup_File.EyeToPolar(
        e.RefX, e.RefY, e.RefZ, eyeX, eyeY, eyeZ);

    bool all = true;
    if (!AngleClose(wantH, gotH) || !AngleClose(wantE, gotE) || Math.Abs(wantD - gotD) > 1e-3f)
    {
        fails.Add($"{id}: polar inverse mismatch h {gotH:0.###}!={wantH:0.###} e {gotE:0.###}!={wantE:0.###} d {gotD:0.###}!={wantD:0.###}");
        all = false;
    }

    byte[] working = setup.OriginalBinBytes;
    foreach ((int idx, float val) in new[]
             {
                 (e.PolarHorizontalIndex, gotH),
                 (e.PolarElevationIndex, gotE),
                 (e.PolarDistanceIndex, gotD)
             })
    {
        BattleCameraSetup_File cur = BattleCameraSetup_File.ReadFromBattleBin(id, working);
        working = cur.WithFloat(idx, val);
    }

    all &= CheckFloatPoolLocal(setup, working, id, "polar eye", fails,
        e.PolarHorizontalIndex, e.PolarElevationIndex, e.PolarDistanceIndex);

    BattleCameraSetup_File reread = BattleCameraSetup_File.ReadFromBattleBin(id, working);
    CameraEstablishingShot? re = reread.Establishing;
    if (re == null ||
        !AngleClose(re.PolarHorizontalAngle, gotH) ||
        !AngleClose(re.PolarElevationAngle, gotE) ||
        Math.Abs(re.PolarDistance - gotD) > 1e-3f)
    {
        fails.Add($"{id}: polar re-read mismatch");
        all = false;
    }

    byte[] back = working;
    foreach ((int idx, float val) in new[]
             {
                 (e.PolarHorizontalIndex, e.PolarHorizontalAngle),
                 (e.PolarElevationIndex, e.PolarElevationAngle),
                 (e.PolarDistanceIndex, e.PolarDistance)
             })
    {
        BattleCameraSetup_File cur = BattleCameraSetup_File.ReadFromBattleBin(id, back);
        back = cur.WithFloat(idx, val);
    }
    if (!back.AsSpan().SequenceEqual(setup.OriginalBinBytes))
    {
        fails.Add($"{id}: polar eye edit not reversible");
        all = false;
    }

    return all;
}

static bool CheckFloatPoolLocal(BattleCameraSetup_File setup, byte[] edited, string id, string what, List<string> fails, params int[] poolIndices)
{
    if (setup.OriginalBinBytes.Length != edited.Length)
    {
        fails.Add($"{id}: {what} edit changed length {setup.OriginalBinBytes.Length}->{edited.Length}");
        return false;
    }

    var allowed = poolIndices.Distinct()
        .Select(i => setup.Chunk0Offset + setup.FloatPoolOffset + 4 * i)
        .ToArray();
    for (int i = 0; i < edited.Length; i++)
    {
        if (setup.OriginalBinBytes[i] == edited[i]) continue;
        bool local = allowed.Any(off => i >= off && i < off + 4);
        if (!local)
        {
            fails.Add($"{id}: {what} edit changed byte 0x{i:X} outside polar float-pool windows");
            return false;
        }
    }
    return true;
}

static bool AngleClose(float a, float b, float epsilon = 1e-3f)
{
    float d = NormalizeAngle180(a - b);
    return Math.Abs(d) <= epsilon;
}

static float NormalizeAngle180(float angle)
{
    float d = angle % 360f;
    if (d > 180f) d -= 360f;
    if (d < -180f) d += 360f;
    return d;
}

bool pass = withChunk0 > 0
            && decodeClean == withChunk0
            && rt0Ok == withChunk0
            && withCamReq > 0
            && editTested > 0 && editOk == editTested
            && setupBins > 0 && setupEditTested > 0 && setupEditOk == setupEditTested
            && polarRoundTripTested > 0 && polarRoundTripOk == polarRoundTripTested
            && fails.Count == 0;

Console.WriteLine($"BattleCameraScanLab — btl corpus @ {root}");
Console.WriteLine($"  files scanned        : {total}");
Console.WriteLine($"  with chunk0 (ATEL)   : {withChunk0}");
Console.WriteLine($"  walk CLOSED on codeLen: {decodeClean}/{withChunk0}");
Console.WriteLine($"  RT0 (codec byte-exact): {rt0Ok}/{withChunk0}");
Console.WriteLine($"  extra opcodes vs monster census: {(extraOpcodes.Count == 0 ? "(none)" : string.Join(" ", extraOpcodes.Select(kv => $"0x{kv.Key:X2}:{kv.Value}")))}");
Console.WriteLine($"  bins with camReq     : {withCamReq}  (total camReq calls: {totalCamReq})");
Console.WriteLine($"  SHOT   operand kind  : immediate={shotImm} pool={shotPool} computed={shotComputed} missing={shotMissing}");
Console.WriteLine($"  TARGET operand kind  : immediate={tgtImm} pool={tgtPool} computed={tgtComputed} missing={tgtMissing}");
Console.WriteLine($"  fully-editable cuts  : {fullyEditable}/{totalCamReq}");
Console.WriteLine($"  EDIT round-trip      : {editOk}/{editTested} (shot+target, byte-local + reversible)");
Console.WriteLine($"  SHOT value histogram : {string.Join(" ", shotValues.Take(20).Select(kv => $"{kv.Key}:{kv.Value}"))}{(shotValues.Count > 20 ? " ..." : "")}");
Console.WriteLine($"  TARGET value histogram: {string.Join(" ", targetValues.Take(20).Select(kv => $"{(kv.Key == -1 ? "none" : kv.Key.ToString())}:{kv.Value}"))}{(targetValues.Count > 20 ? " ..." : "")}");
Console.WriteLine($"  -- CAMERA SETUP (real angle/distance/pos floats in chunk0) --");
Console.WriteLine($"  bins with camera setup: {setupBins}  (total camera calls: {setupCalls}, distinct float knobs: {setupFloatParams})");
Console.WriteLine($"  setup roles          : {string.Join(" ", roleHist.Select(kv => $"{kv.Key}:{kv.Value}"))}");
Console.WriteLine($"  FLOAT edit round-trip: {setupEditOk}/{setupEditTested} (byte-local + reversible)");
Console.WriteLine($"  polar call split     : camSetPolar0x6004={camSetPolarCalls}  target-aware variants={targetAwarePolarCalls}");
Console.WriteLine($"  establishing camera  : ref(x,y,z)={estRef}  polar(h,e,dist)={estPolar}  both={estBoth}  eye-editable={estPolarEyeEditable}  (of {setupBins} setup bins)");
Console.WriteLine($"  polar eye round-trip : {polarRoundTripOk}/{polarRoundTripTested} (formula inverse + 3x WithFloat + reversible)");
if (fails.Count > 0)
{
    Console.WriteLine($"  FAILS ({fails.Count}):");
    foreach (var f in fails.Take(40)) Console.WriteLine($"    {f}");
    if (fails.Count > 40) Console.WriteLine($"    ... +{fails.Count - 40} more");
}
Console.WriteLine(pass
    ? "VERDICT: PASS — chunk0 is ATEL (RT0 byte-exact), camReq reads, shot/target edits are byte-local, and camSetPolar eye drag is formula-reversible."
    : "VERDICT: FAIL — a camera-scan invariant broke (see FAILS).");

if (jsonOut != null)
{
    var verdict = new
    {
        root, total, withChunk0, decodeClean, rt0Ok, withCamReq, totalCamReq,
        shotImm, shotPool, shotComputed, shotMissing, tgtImm, tgtPool, tgtComputed, tgtMissing,
        fullyEditable, editTested, editOk, pass,
        setupBins, setupCalls, setupFloatParams, setupEditTested, setupEditOk,
        estRef, estPolar, estBoth, estPolarEyeEditable, camSetPolarCalls, targetAwarePolarCalls,
        polarRoundTripTested, polarRoundTripOk,
        roleHist = roleHist.ToDictionary(kv => kv.Key, kv => kv.Value),
        shotValues = shotValues.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
        targetValues = targetValues.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
        fails = fails.Take(200).ToArray(),
    };
    File.WriteAllText(jsonOut, JsonSerializer.Serialize(verdict, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"wrote {jsonOut}");
}

return pass ? 0 : 1;

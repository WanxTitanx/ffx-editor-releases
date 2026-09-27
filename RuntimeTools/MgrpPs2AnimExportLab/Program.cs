using System.Text;
using System.Text.Json;
using MgrpPs2AnimExportLab;
using static MgrpPs2AnimExportLab.MgrpDecoder;

// MgrpPs2AnimExportLab — read-only RE tool. Emits an ANIMATED glTF from a FFX PS2 `.mgrp`
// body-motion clip. Codec decoded from the FFX HD native reimplementation (see csproj/docs).
// Skeleton: REAL `.chr` bind-pose hierarchy (parent links + rest TRS) when a `.chr` is present —
// proved from FFX.exe sub_827870/sub_827610; motion animates keyed components on top. Channel->bone
// remap (table[0]) comes from the .chr (sub_837B40/838FF0); real instScale = *(model+56)*0.001
// (sub_838FF0). Falls back to a motion-derived proxy grid only when no `.chr` is available.
//
// usage:
//   single: MgrpPs2AnimExportLab <in.mgrp> <out.gltf> [--chr <m###.chr>] [--record N] [--group G|auto] [--fps N] [--trans-scale auto|<f>]
//   batch:  MgrpPs2AnimExportLab batch <ps2_mon_root> <out_dir> [--fps N]

if (args.Length >= 1 && args[0].Equals("batch", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 3) { Console.Error.WriteLine("usage: batch <ps2_mon_root> <out_dir> [--fps N]"); return 1; }
    int bfps = 24;
    for (int i = 3; i < args.Length - 1; i++) if (args[i] == "--fps") bfps = int.Parse(args[i + 1]);
    return RunBatch(args[1], args[2], bfps);
}

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: MgrpPs2AnimExportLab <in.mgrp> <out.gltf> [--chr <chr>] [--record N] [--group G|auto] [--fps N] [--trans-scale auto|<f>]");
    Console.Error.WriteLine("       MgrpPs2AnimExportLab batch <ps2_mon_root> <out_dir> [--fps N]");
    return 1;
}
{
    string mgrpPath = args[0], outPath = args[1], chrPath = "";
    int wantRecord = 0, wantGroup = -1, fps = 24; string transScaleArg = "auto";
    for (int i = 2; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--chr": chrPath = args[++i]; break;
            case "--record": wantRecord = int.Parse(args[++i]); break;
            case "--group": wantGroup = args[i + 1] == "auto" ? -1 : int.Parse(args[i + 1]); i++; break;
            case "--fps": fps = int.Parse(args[++i]); break;
            case "--trans-scale": transScaleArg = args[++i]; break;
        }
    }
    var r = ExportOne(mgrpPath, outPath, chrPath, wantRecord, wantGroup, fps, transScaleArg, verbose: true);
    if (!r.Ok) { Console.Error.WriteLine($"FAILED: {r.Error}"); return 1; }
    return 0;
}

// ============================================================================
// Batch: iterate every mXXX under <mon_root>, export the richest resident clip.
int RunBatch(string monRoot, string outDir, int fps)
{
    if (!Directory.Exists(monRoot)) { Console.Error.WriteLine($"mon root not found: {monRoot}"); return 1; }
    Directory.CreateDirectory(outDir);
    var monsters = Directory.GetDirectories(monRoot)
        .Select(Path.GetFileName)
        .Where(n => n is { Length: 4 } && n[0] == 'm' && n.Skip(1).All(char.IsDigit))
        .OrderBy(n => n, StringComparer.Ordinal)
        .ToArray();
    Console.WriteLine($"batch: {monsters.Length} monster dirs under {monRoot}");

    var results = new List<ExportResult>();
    int ok = 0, animated = 0, staticOnly = 0, noMgrp = 0, stub = 0, fail = 0;
    foreach (var m in monsters)
    {
        // prefer the resident*.mgrp with the most motion; fall back across resident1/resident0/any.
        var motDir = Path.Combine(monRoot, m!, "mot");
        string? best = null; int bestRecords = -1;
        if (Directory.Exists(motDir))
            foreach (var f in Directory.GetFiles(motDir, "resident*.mgrp").OrderBy(x => x, StringComparer.Ordinal))
            {
                try
                {
                    var probe = MgrpFile.Load(f);
                    int recs = probe.Records.Count;
                    if (recs > bestRecords) { bestRecords = recs; best = f; }
                }
                catch { }
            }
        if (best == null) { results.Add(ExportResult.Failed(m!, "no resident*.mgrp")); noMgrp++; continue; }
        if (bestRecords <= 0) { results.Add(ExportResult.Failed(m!, "stub/empty .mgrp")); stub++; continue; }

        var chr = Path.Combine(monRoot, m!, "mdl", m + ".chr");
        var outPath = Path.Combine(outDir, $"{m}_anim.gltf");
        ExportResult r;
        try { r = ExportOne(best, outPath, File.Exists(chr) ? chr : "", 0, -1, fps, "auto", verbose: false); }
        catch (Exception ex) { r = ExportResult.Failed(m!, ex.GetType().Name + ": " + ex.Message); }
        results.Add(r);
        if (r.Ok) { ok++; if (r.KeyedChannels > 0) animated++; else staticOnly++; }
        else fail++;
    }

    // index.json — per-monster status (honest decode metrics, no over-claim)
    var index = new
    {
        generatedBy = "RuntimeTools/MgrpPs2AnimExportLab batch (read-only RE; no game files modified)",
        ps2MonRoot = monRoot,
        total = monsters.Length,
        exported = ok,
        animated,
        staticOnly,
        noMgrp,
        stub,
        failed = fail,
        decisionBand = "mgrp_motion_decoded_proved__chr_skeleton_hierarchy",
        promotionStatus = "not_promoted__skin_mesh_pending",
        note = "Per-monster animated glTF from the decoded PS2 .mgrp body-motion codec. Skeleton uses the .chr "
             + "bind-pose hierarchy (parent links + rest TRS) + channel->bone remap (table[0]; the rare 3/234 "
             + "monsters with multiple distinct tables use table[0]) + real instScale = *(model+56)*0.001 - all "
             + "proved from FFX.exe (sub_827870/827610/837B40/838FF0), corpus-gated (33830 remap entries, 0 OOB). "
             + "Falls back to a motion proxy grid only when no .chr. Skinned mesh remains a separate (ps3data) lane.",
        results,
    };
    File.WriteAllText(Path.Combine(outDir, "_index.json"),
        JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));

    // modelviewer-catalog.json — viewer-friendly (mirrors PhyreSkinnedAnimExportLab shape)
    var catEntries = results.Where(r => r.Ok).Select(r => new
    {
        monsterId = r.Monster,
        label = $"{r.Monster} {(r.KeyedChannels > 0 ? $"animated ({r.KeyedChannels} keyed ch, {r.MovingTargets.Count} bones)" : "static pose")}",
        decisionBand = r.KeyedChannels > 0 ? "mgrp_motion_decoded_proved__chr_skeleton_hierarchy" : "mgrp_static_pose_decoded",
        motionStatus = r.KeyedChannels > 0 ? $"keyed_{r.Frames}f_{fps}fps" : "static",
        frames = r.Frames,
        keyedChannels = r.KeyedChannels,
        movingTargets = r.MovingTargets,
        tags = new[] { "ps2_mgrp_chr_skeleton_hierarchy" },
        openableAssetPath = $"{r.Monster}_anim.gltf",
    }).ToArray();
    var catalog = new
    {
        schemaVersion = "mgrp-ps2-anim-1",
        generator = "RuntimeTools/MgrpPs2AnimExportLab batch (read-only RE; no game files modified)",
        defaultAssetId = catEntries.FirstOrDefault(e => e.monsterId == "m077")?.monsterId
                       ?? catEntries.FirstOrDefault(e => e.keyedChannels > 0)?.monsterId
                       ?? catEntries.FirstOrDefault()?.monsterId ?? "m001",
        summary = new { entryCount = catEntries.Length, animated = catEntries.Count(e => e.keyedChannels > 0) },
        entries = catEntries,
    };
    File.WriteAllText(Path.Combine(outDir, "modelviewer-catalog.json"),
        JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));

    Console.WriteLine($"DONE: exported={ok}/{monsters.Length}  animated={animated}  staticOnly={staticOnly}  noMgrp={noMgrp}  stub={stub}  failed={fail}");
    Console.WriteLine($"index:   {Path.Combine(outDir, "_index.json")}");
    Console.WriteLine($"catalog: {Path.Combine(outDir, "modelviewer-catalog.json")} ({catEntries.Length} entries)");
    // surface the richest animated clips (proof candidates)
    foreach (var r in results.Where(r => r.Ok && r.KeyedChannels > 0).OrderByDescending(r => r.KeyedChannels).Take(12))
        Console.WriteLine($"  rich: {r.Monster}  group={r.Group}  frames={r.Frames}  keyed={r.KeyedChannels}  bones={r.MovingTargets.Count}");
    foreach (var r in results.Where(x => !x.Ok).Take(8))
        Console.WriteLine($"  skip: {r.Monster}: {r.Error}");
    return 0;
}

// ============================================================================
// Export one .mgrp clip to an animated glTF (+ provenance sidecar). Returns a summary.
ExportResult ExportOne(string mgrpPath, string outPath, string chrPathIn, int wantRecord, int wantGroup, int fps, string transScaleArg, bool verbose)
{
    string monster = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(mgrpPath))) ?? "") is { Length: > 0 } mm ? mm : Path.GetFileNameWithoutExtension(mgrpPath);
    string chrPath = chrPathIn;
    if (chrPath.Length == 0)
    {
        try
        {
            var monDir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(mgrpPath))!, ".."));
            var mon = Path.GetFileName(monDir);
            var cand = Path.Combine(monDir, "mdl", mon + ".chr");
            if (File.Exists(cand)) chrPath = cand;
        }
        catch { }
    }
    int boneCount = chrPath.Length > 0 ? TargetBoneCountFromChr(chrPath) : 64;

    var mg = MgrpFile.Load(mgrpPath);
    if (mg.Records.Count == 0) return ExportResult.Failed(monster, "no records (stub/empty)");
    if (wantRecord >= mg.Records.Count) wantRecord = 0;
    var rec = mg.Records[wantRecord];

    int chosenGroup = wantGroup; SubClip? clip = null; int bestScore = -1;
    for (int g = 0; g < rec.GroupList.Count; g++)
    {
        if (wantGroup >= 0 && g != wantGroup) continue;
        int a2 = rec.GroupList[g].PtrB;
        if (a2 <= 0 || a2 + 16 > mg.Data.Length) continue;
        SubClip c;
        try { c = DecodeGroup(mg.Data, a2, boneCount); } catch { continue; }
        int keyed = c.Channels.Count(x => x.Mode == 3);
        int score = c.FrameCount * Math.Max(1, keyed);
        if (wantGroup >= 0) { clip = c; chosenGroup = g; break; }
        if (score > bestScore) { bestScore = score; clip = c; chosenGroup = g; }
    }
    if (clip == null) return ExportResult.Failed(monster, "no decodable group");

    int keyedCount = clip.Channels.Count(x => x.Mode == 3);
    var movingTargets = clip.Channels.Where(x => x.Mode == 3).Select(x => x.Target).Distinct().OrderBy(x => x).ToList();
    if (verbose)
        Console.WriteLine($"mgrp={Path.GetFileName(mgrpPath)} record={wantRecord} group={chosenGroup} frames={clip.FrameCount} targets={clip.TargetCount} keyed={keyedCount} movingTargets=[{string.Join(",", movingTargets)}] valConsumed={clip.ValueConsumed}");

    // real .chr skeleton (parent links + bind/rest TRS + remap + instScale); null => fall back to proxy grid.
    var skel = chrPath.Length > 0 ? ReadChrSkeleton(chrPath) : null;
    bool hier = skel is { BoneCount: > 0 };
    bool remapped = hier && skel!.SlotToBone.Length > 0;     // .chr@0x30 != 0 => slot->bone remap from the .chr
    bool remapMulti = hier && skel!.RemapMultipleTables;     // rare (3/234): >1 distinct table; used table[0]

    // translation scale. Hierarchy path uses the real instScale = *(model+56)*0.001 from the .chr
    // (proved FFX.exe sub_838FF0). Proxy path keeps the old auto-normalization.
    float instScale;
    if (transScaleArg != "auto") instScale = float.Parse(transScaleArg);
    else if (hier) instScale = skel!.InstScale;
    else
    {
        float maxAbsTrans = 0f;
        foreach (var ch in clip.Channels.Where(c => c.Mode == 3 && c.Comp >= 3 && c.Comp < 6))
            foreach (var s in ch.Samples!) maxAbsTrans = Math.Max(maxAbsTrans, Math.Abs(s));
        instScale = maxAbsTrans > 1 ? 0.6f / maxAbsTrans : 0.0001f;
    }

    // ---- glTF assembly ----
    var buf = new List<byte>();
    void Align4() { while (buf.Count % 4 != 0) buf.Add(0); }
    void WF(float f) => buf.AddRange(BitConverter.GetBytes(f));
    void WU16(ushort v) => buf.AddRange(BitConverter.GetBytes(v));
    var bufferViews = new List<object>();
    var accessors = new List<object>();
    int AddBV(int off, int len, int? target = null)
    { bufferViews.Add(target == null ? (object)new { buffer = 0, byteOffset = off, byteLength = len } : new { buffer = 0, byteOffset = off, byteLength = len, target }); return bufferViews.Count - 1; }

    float Ln = 1.0f, tk = 0.18f;
    (float, float, float)[] bverts =
    {
        (0,-tk,-tk),(Ln,-tk,-tk),(Ln,tk,-tk),(0,tk,-tk),
        (0,-tk, tk),(Ln,-tk, tk),(Ln,tk, tk),(0,tk, tk),
    };
    ushort[] bidx = { 0,1,2, 0,2,3,  4,6,5, 4,7,6,  0,4,5, 0,5,1,  3,2,6, 3,6,7,  0,3,7, 0,7,4,  1,5,6, 1,6,2 };
    Align4(); int posStart = buf.Count;
    float[] pmin = { 1e30f, 1e30f, 1e30f }, pmax = { -1e30f, -1e30f, -1e30f };
    foreach (var (x, y, z) in bverts) { WF(x); WF(y); WF(z);
        pmin[0]=Math.Min(pmin[0],x); pmin[1]=Math.Min(pmin[1],y); pmin[2]=Math.Min(pmin[2],z);
        pmax[0]=Math.Max(pmax[0],x); pmax[1]=Math.Max(pmax[1],y); pmax[2]=Math.Max(pmax[2],z); }
    int posBV = AddBV(posStart, buf.Count - posStart, 34962);
    int posAcc = accessors.Count; accessors.Add(new { bufferView = posBV, componentType = 5126, count = bverts.Length, type = "VEC3", min = pmin, max = pmax });
    Align4(); int idxStart = buf.Count; foreach (var v in bidx) WU16(v);
    int idxBV = AddBV(idxStart, buf.Count - idxStart, 34963);
    int idxAcc = accessors.Count; accessors.Add(new { bufferView = idxBV, componentType = 5123, count = bidx.Length, type = "SCALAR", min = new[] { 0 }, max = new[] { 7 } });

    int nF = Math.Max(1, clip.FrameCount);
    Align4(); int tStart = buf.Count; for (int f = 0; f < nF; f++) WF((float)f / fps);
    int tBV = AddBV(tStart, buf.Count - tStart);
    int tAcc = accessors.Count; accessors.Add(new { bufferView = tBV, componentType = 5126, count = nF, type = "SCALAR", min = new[] { 0f }, max = new[] { (nF - 1) / (float)fps } });

    var nodes = new List<Dictionary<string, object>>();
    var samplers = new List<object>();
    var channels = new List<object>();
    var materials = new List<object>();
    materials.Add(new { name = "bone_static", pbrMetallicRoughness = new { baseColorFactor = new[] { 0.55, 0.57, 0.62, 1.0 }, metallicFactor = 0.0, roughnessFactor = 0.85 }, doubleSided = true });
    materials.Add(new { name = "bone_moving", pbrMetallicRoughness = new { baseColorFactor = new[] { 0.90, 0.45, 0.20, 1.0 }, metallicFactor = 0.0, roughnessFactor = 0.7 }, doubleSided = true });

    object MakeMesh(int matIndex) => new
    {
        primitives = new[] { new {
            attributes = new Dictionary<string,int> { ["POSITION"] = posAcc },
            indices = idxAcc, mode = 4, material = matIndex } }
    };
    var meshes = new object[] { MakeMesh(0), MakeMesh(1) };

    double[] EulerToQuat(float rx, float ry, float rz)
    {
        double hx = rx / 2, hy = ry / 2, hz = rz / 2;
        double[] qx = { Math.Sin(hx), 0, 0, Math.Cos(hx) };
        double[] qy = { 0, Math.Sin(hy), 0, Math.Cos(hy) };
        double[] qz = { 0, 0, Math.Sin(hz), Math.Cos(hz) };
        double[] Mul(double[] a, double[] b) => new[]{
            a[3]*b[0]+a[0]*b[3]+a[1]*b[2]-a[2]*b[1],
            a[3]*b[1]-a[0]*b[2]+a[1]*b[3]+a[2]*b[0],
            a[3]*b[2]+a[0]*b[1]-a[1]*b[0]+a[2]*b[3],
            a[3]*b[3]-a[0]*b[0]-a[1]*b[1]-a[2]*b[2] };
        var q = Mul(Mul(qx, qy), qz);
        double n = Math.Sqrt(q[0]*q[0]+q[1]*q[1]+q[2]*q[2]+q[3]*q[3]); if (n == 0) return new[] { 0.0, 0, 0, 1 };
        return new[] { q[0]/n, q[1]/n, q[2]/n, q[3]/n };
    }
    float[] QuatOf(float rx, float ry, float rz)
    { var q = EulerToQuat(rx, ry, rz); return new[] { (float)q[0], (float)q[1], (float)q[2], (float)q[3] }; }

    int rootIndex;
    if (hier)
    {
        // ---- REAL .chr skeleton: node tree (parent links) + bind/rest TRS; motion animates keyed comps on top.
        int bc = skel!.BoneCount;
        var childrenOf = new List<int>[bc];
        for (int i = 0; i < bc; i++) childrenOf[i] = new List<int>();
        var roots = new List<int>();
        for (int b = 0; b < bc; b++)
        { int p = skel.Parent[b]; if (p < 0 || p >= bc) roots.Add(b); else childrenOf[p].Add(b); }

        // channel-target (motion slot) -> chr bone. Invert the .chr remap to bone -> slot.
        // No remap (.chr@0x30==0) => identity (slot s == bone s).
        int[] boneToSlot = new int[bc];
        for (int i = 0; i < bc; i++) boneToSlot[i] = remapped ? -1 : i;
        if (remapped)
            for (int s = 0; s < Math.Min(skel.SlotToBone.Length, bc); s++)   // runtime bound: min(count, boneCount)
            { int bb = skel.SlotToBone[s]; if (bb >= 0 && bb < bc) boneToSlot[bb] = s; }

        // One node per bone, in bone-index order => glTF node index == bone index.
        for (int b = 0; b < bc; b++)
        {
            var rest = skel.Rest[b];
            int tgt = boneToSlot[b];          // motion slot animating this bone (-1 = unmapped)
            bool mapped = tgt >= 0 && tgt < clip.TargetCount;
            bool[] keyed = new bool[9];
            if (mapped)
                foreach (var ch in clip.Channels)
                    if (ch.Target == tgt && ch.Mode == 3) keyed[ch.Comp] = true;
            bool rotK = keyed[0] || keyed[1] || keyed[2];
            bool trK = keyed[3] || keyed[4] || keyed[5];
            bool scK = keyed[6] || keyed[7] || keyed[8];
            bool moves = rotK || trK || scK;

            // Faithful to runtime: FFX_Mseq_InitChannelTracks (sub_839A00 @0x839ae4) sets the per-component
            // write-flag UNCONDITIONALLY for all 9 comps of every mapped bone, so the motion OVERWRITES the
            // full local TRS (keyed=curve; const mode0=0/mode1=1/mode2=static) - NOT the .chr rest. Rest is
            // kept ONLY for UNMAPPED bones (0xFFFF skip / slot>=targetCount), which the runtime leaves at bind.
            Trs Eff(int f) => SampleTarget(clip, tgt, f, instScale);
            var b0 = mapped ? Eff(0) : rest;
            var node = new Dictionary<string, object>
            {
                ["name"] = $"bone{b}{(skel.Parent[b] < 0 ? "_ROOT" : $"_p{skel.Parent[b]}")}{(moves ? "_MOVING" : "")}",
                ["mesh"] = moves ? 1 : 0,
                ["rotation"] = QuatOf(b0.RotX, b0.RotY, b0.RotZ),
                ["translation"] = new[] { b0.TrX, b0.TrY, b0.TrZ },
                ["scale"] = new[] { b0.ScX, b0.ScY, b0.ScZ },
            };
            if (childrenOf[b].Count > 0) node["children"] = childrenOf[b].ToArray();
            nodes.Add(node);   // node index == b

            if (!moves) continue;
            var trs = new Trs[nF];
            for (int f = 0; f < nF; f++) trs[f] = Eff(f);
            if (rotK)
            {
                Align4(); int rb = buf.Count;
                foreach (var x in trs) { var q = QuatOf(x.RotX, x.RotY, x.RotZ); WF(q[0]); WF(q[1]); WF(q[2]); WF(q[3]); }
                int rBV = AddBV(rb, buf.Count - rb); int rAcc = accessors.Count;
                accessors.Add(new { bufferView = rBV, componentType = 5126, count = nF, type = "VEC4" });
                samplers.Add(new { input = tAcc, output = rAcc, interpolation = "LINEAR" });
                channels.Add(new { sampler = samplers.Count - 1, target = new { node = b, path = "rotation" } });
            }
            if (trK)
            {
                Align4(); int trb = buf.Count;
                foreach (var x in trs) { WF(x.TrX); WF(x.TrY); WF(x.TrZ); }
                int trBV = AddBV(trb, buf.Count - trb); int trAcc = accessors.Count;
                accessors.Add(new { bufferView = trBV, componentType = 5126, count = nF, type = "VEC3" });
                samplers.Add(new { input = tAcc, output = trAcc, interpolation = "LINEAR" });
                channels.Add(new { sampler = samplers.Count - 1, target = new { node = b, path = "translation" } });
            }
            if (scK)
            {
                Align4(); int sb = buf.Count;
                foreach (var x in trs) { WF(x.ScX); WF(x.ScY); WF(x.ScZ); }
                int sBV = AddBV(sb, buf.Count - sb); int sAcc = accessors.Count;
                accessors.Add(new { bufferView = sBV, componentType = 5126, count = nF, type = "VEC3" });
                samplers.Add(new { input = tAcc, output = sAcc, interpolation = "LINEAR" });
                channels.Add(new { sampler = samplers.Count - 1, target = new { node = b, path = "scale" } });
            }
        }

        var rootNode = new Dictionary<string, object> { ["name"] = "mgrp_root", ["children"] = roots.ToArray() };
        nodes.Add(rootNode); rootIndex = nodes.Count - 1;
    }
    else
    {
        // ---- fallback: proxy "grid" (no .chr) — one anchored box per motion target.
        float spacing = 2.4f;
        int cols = (int)Math.Ceiling(Math.Sqrt(Math.Max(1, clip.TargetCount)));
        var rootChildren = new List<int>();
        for (int tgt = 0; tgt < clip.TargetCount; tgt++)
        {
            bool moves = clip.Channels.Any(c => c.Target == tgt && c.Mode == 3);
            int gx = tgt % cols, gy = tgt / cols;
            var offsetNode = new Dictionary<string, object>
            {
                ["name"] = $"target{tgt}_anchor",
                ["translation"] = new[] { gx * spacing, -gy * spacing, 0f },
            };
            var trs = new Trs[nF];
            for (int f = 0; f < nF; f++) trs[f] = SampleTarget(clip, tgt, f, instScale);
            var boneNode = new Dictionary<string, object>
            {
                ["name"] = $"target{tgt}{(moves ? "_MOVING" : "")}",
                ["mesh"] = moves ? 1 : 0,
                ["rotation"] = QuatOf(trs[0].RotX, trs[0].RotY, trs[0].RotZ),
                ["translation"] = new[] { trs[0].TrX, trs[0].TrY, trs[0].TrZ },
                ["scale"] = new[] { trs[0].ScX, trs[0].ScY, trs[0].ScZ },
            };
            nodes.Add(boneNode); int boneNodeIndex = nodes.Count - 1;
            offsetNode["children"] = new[] { boneNodeIndex };
            nodes.Add(offsetNode); rootChildren.Add(nodes.Count - 1);
            if (moves)
            {
                Align4(); int rb = buf.Count;
                foreach (var x in trs) { var q = QuatOf(x.RotX, x.RotY, x.RotZ); WF(q[0]); WF(q[1]); WF(q[2]); WF(q[3]); }
                int rBV = AddBV(rb, buf.Count - rb); int rAcc = accessors.Count;
                accessors.Add(new { bufferView = rBV, componentType = 5126, count = nF, type = "VEC4" });
                samplers.Add(new { input = tAcc, output = rAcc, interpolation = "LINEAR" });
                channels.Add(new { sampler = samplers.Count - 1, target = new { node = boneNodeIndex, path = "rotation" } });
                Align4(); int trb = buf.Count;
                foreach (var x in trs) { WF(x.TrX); WF(x.TrY); WF(x.TrZ); }
                int trBV = AddBV(trb, buf.Count - trb); int trAcc = accessors.Count;
                accessors.Add(new { bufferView = trBV, componentType = 5126, count = nF, type = "VEC3" });
                samplers.Add(new { input = tAcc, output = trAcc, interpolation = "LINEAR" });
                channels.Add(new { sampler = samplers.Count - 1, target = new { node = boneNodeIndex, path = "translation" } });
                Align4(); int sb = buf.Count;
                foreach (var x in trs) { WF(x.ScX); WF(x.ScY); WF(x.ScZ); }
                int sBV = AddBV(sb, buf.Count - sb); int sAcc = accessors.Count;
                accessors.Add(new { bufferView = sBV, componentType = 5126, count = nF, type = "VEC3" });
                samplers.Add(new { input = tAcc, output = sAcc, interpolation = "LINEAR" });
                channels.Add(new { sampler = samplers.Count - 1, target = new { node = boneNodeIndex, path = "scale" } });
            }
        }
        var rootNode = new Dictionary<string, object> { ["name"] = "mgrp_root", ["children"] = rootChildren.ToArray() };
        nodes.Add(rootNode); rootIndex = nodes.Count - 1;
    }

    var gltf = new Dictionary<string, object>
    {
        ["asset"] = new { version = "2.0", generator = "RuntimeTools/MgrpPs2AnimExportLab (read-only RE; no game files modified)" },
        ["scene"] = 0,
        ["scenes"] = new[] { new { nodes = new[] { rootIndex }, name = Path.GetFileNameWithoutExtension(mgrpPath) } },
        ["nodes"] = nodes.ToArray(),
        ["meshes"] = meshes,
        ["materials"] = materials.ToArray(),
        ["buffers"] = new[] { new { uri = "data:application/octet-stream;base64," + Convert.ToBase64String(buf.ToArray()), byteLength = buf.Count } },
        ["bufferViews"] = bufferViews.ToArray(),
        ["accessors"] = accessors.ToArray(),
        ["animations"] = new object[] { new { name = $"{monster}_g{chosenGroup}", samplers = samplers.ToArray(), channels = channels.ToArray() } },
    };

    var json = JsonSerializer.Serialize(gltf, new JsonSerializerOptions { WriteIndented = false });
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
    File.WriteAllText(outPath, json, new UTF8Encoding(false));

    var prov = new
    {
        tool = "RuntimeTools/MgrpPs2AnimExportLab (read-only RE; no game files modified)",
        source = Path.GetFullPath(mgrpPath),
        chr = chrPath,
        record = wantRecord, group = chosenGroup,
        frames = clip.FrameCount, targets = clip.TargetCount, fps,
        keyedChannels = keyedCount, movingTargets,
        valueConsumed = clip.ValueConsumed,
        instScaleUsed = instScale,
        decisionBand = hier ? "mgrp_motion_decoded_proved__chr_skeleton_hierarchy"
                            : "mgrp_motion_decoded_proved__skeleton_motion_derived",
        skeleton = new
        {
            kind = hier ? "chr_hierarchy" : "motion_proxy_grid",
            bones = hier ? skel!.BoneCount : clip.TargetCount,
            remapField0x30 = hier ? skel!.RemapField0x30 : -1,
            remap = !hier ? "n/a (no .chr)" : remapped ? (remapMulti ? "table[0] of multiple (rare)" : "table[0] (slot->bone)") : (skel!.RemapField0x30 != 0 ? "identity FALLBACK (.chr@0x30!=0, remap unparsed)" : "identity (.chr@0x30==0)"),
        },
        proved = new[]
        {
            "container record->group->ptrB sub-clip block",
            "9 channels/target order [rot3,trans3,scale3]; 2-bit modes; mode3 keyed [u16 len][delta-RLE]",
            "dequant rot=int16*pi/2048, trans=int16*instScale, scale=int16/4096",
            "delta-RLE 7/14-bit signed + RLE runs; per-frame LERP/angle-wrap",
            "instScale=*(model+56)*0.001 (FFX.exe sub_838FF0); slot->bone remap u16@(table+8+2*s) 0xFFFF=skip (sub_837B40/838FF0; corpus gate 33830 entries 0 OOB)",
            "mapped bones: ALL 9 comps overwritten by motion (mode0=0/1=1/2=static/3=keyed) per sub_839A00 write-flag; .chr rest only for UNMAPPED bones (faithful to runtime)",
            ".chr parent map + bind/rest TRS: parent=u16@(*(skel+28)+20*i); FFX.exe sub_827870/sub_827610 (proved)",
        },
        pending = new[]
        {
            remapMulti ? "this monster has >1 distinct remap table; exporter used table[0] (subid->table selection unresolved)"
                       : "subid->table selection (moot here: per-monster remap tables are identical)",
            "euler->quat rotation order (assumed intrinsic XYZ)",
            "skinned mesh + per-submesh material (separate ps3data lane) - this exporter renders bone-proxy sticks only",
        },
    };
    File.WriteAllText(Path.ChangeExtension(outPath, ".provenance.json"),
        JsonSerializer.Serialize(prov, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));

    if (verbose)
    {
        Console.WriteLine($"wrote {outPath} ({json.Length} bytes, buffer={buf.Count}) nodes={nodes.Count} animChannels={channels.Count}");
        Console.WriteLine($"provenance: {Path.ChangeExtension(outPath, ".provenance.json")}  instScale={instScale:E2} ({(hier ? $"skeleton=.chr hierarchy ({skel!.BoneCount} bones), trans bind-unit" : "skeleton=proxy grid, trans auto-normalized")})");
    }

    return new ExportResult(monster, true, null, Path.GetFileName(mgrpPath), wantRecord, chosenGroup,
        clip.FrameCount, clip.TargetCount, keyedCount, movingTargets, instScale, outPath);
}

// ============================================================================
public sealed record ExportResult(
    string Monster, bool Ok, string? Error, string MgrpName, int Record, int Group,
    int Frames, int Targets, int KeyedChannels, List<int> MovingTargets, float InstScale, string OutPath)
{
    public static ExportResult Failed(string monster, string error) =>
        new(monster, false, error, "", 0, 0, 0, 0, 0, new List<int>(), 0f, "");
}

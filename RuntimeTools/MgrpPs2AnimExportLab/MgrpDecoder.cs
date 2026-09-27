using System.Buffers.Binary;

namespace MgrpPs2AnimExportLab;

// Faithful decoder of the FFX PS2 .mgrp body-motion codec, transcribed from the FFX HD
// native reimplementation (FFX.exe, read-only IDA analysis). See csproj header for VAs.
public static class MgrpDecoder
{
    public sealed record Group(int Index, int PtrA, int PtrB);
    public sealed record Record(int Index, uint F0, uint Subid, int Channels, int Groups, int OffA, int OffB, List<Group> GroupList);

    public sealed class Channel
    {
        public int Target;        // bone index within the clip
        public int Comp;          // 0..8 -> rotX,rotY,rotZ, trX,trY,trZ, scX,scY,scZ
        public bool IsAngle;      // comp < 3
        public int Mode;          // 0=const0 1=const1 2=static 3=keyed
        public float Const;       // for modes 0/1/2 (unit float)
        public short[]? Samples;  // for mode 3, one int16 sample per frame
        public int BlockLen;      // mode 3 byte length
        public string Name => CompNames[Comp];
    }

    public sealed class SubClip
    {
        public int FrameCount;
        public int TargetCount;       // clamped to bone count
        public List<Channel> Channels = new();
        public int ValueConsumed;
        public int ModeBase, ValueBase;
    }

    public static readonly string[] CompNames =
        { "rotX","rotY","rotZ","trX","trY","trZ","scX","scY","scZ" };

    static ushort U16(byte[] d, int o) => BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(o));
    static uint   U32(byte[] d, int o) => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(o));
    static int Sx(int v, int bits) { int m = 1 << (bits - 1); return (v ^ m) - m; }
    static float S16Unit(short s) => Sx(s & 0xFFFF, 16) / 4096.0f;   // FFX_Mseq_S16ToUnitFloat

    public sealed class MgrpFile
    {
        public byte[] Data = Array.Empty<byte>();
        public List<Record> Records = new();
        public static MgrpFile Load(string path)
        {
            var d = File.ReadAllBytes(path);
            var f = new MgrpFile { Data = d };
            if (d.Length < 16) return f;
            int payloadEnd = (int)U32(d, 0x0C);
            if (d.Length <= payloadEnd) return f;
            int n = (d.Length - payloadEnd) / 20;
            for (int r = 0; r < n; r++)
            {
                int ro = payloadEnd + r * 20;
                uint f0 = U32(d, ro); uint subid = U32(d, ro + 4);
                int a = U16(d, ro + 8); int b = U16(d, ro + 10);
                int offA = (int)U32(d, ro + 12); int offB = (int)U32(d, ro + 16);
                var groups = new List<Group>();
                for (int g = 0; g < b; g++)
                {
                    int eo = offB + 16 * g;
                    if (eo + 16 > d.Length) break;
                    groups.Add(new Group(g, (int)U32(d, eo + 8), (int)U32(d, eo + 12)));
                }
                f.Records.Add(new Record(r, f0, subid, a, b, offA, offB, groups));
            }
            return f;
        }
    }

    // FFX_Mseq_AdvanceKeyedChannelCursors: delta-RLE accumulator, one int16 sample per frame.
    static short[] DecodeStream(byte[] buf, int pos, int nframes)
    {
        int delta = 0, sample = 0, run = 0;
        var outp = new short[nframes];
        for (int i = 0; i < nframes; i++)
        {
            if (run != 0) run--;
            else
            {
                if (pos >= buf.Length) { /* hold */ }
                else
                {
                    int c = buf[pos++];
                    if (c < 0x80) delta = Sx(c & 0x7F, 7);            // 7-bit signed delta
                    else if ((c & 0x40) != 0)                          // 14-bit signed delta
                    {
                        if (pos >= buf.Length) { }
                        else { int lo = c & 0x3F, hi = buf[pos++]; delta = Sx(lo | (hi << 6), 14); }
                    }
                    else run = c & 0x3F;                               // RUN: hold delta
                }
            }
            sample = Sx((sample + delta) & 0xFFFF, 16);
            outp[i] = (short)sample;
        }
        return outp;
    }

    // FFX_Mseq_InitChannelTracks: walk the 2-bit mode stream + value region into per-target channels.
    // a2 = a group's ptrB (self-describing sub-clip block).
    public static SubClip DecodeGroup(byte[] d, int a2, int boneCount)
    {
        var clip = new SubClip();
        clip.FrameCount = U16(d, a2 + 0);
        int targetCount = U16(d, a2 + 2);
        int modeOff = (int)U32(d, a2 + 8);
        int valueOff = (int)U32(d, a2 + 12);
        clip.ModeBase = a2 + modeOff;
        clip.ValueBase = a2 + valueOff;
        int nTarget = Math.Min(targetCount, boneCount);
        clip.TargetCount = nTarget;
        int nchan = 9 * nTarget;

        int modeBit = 0;                 // continuous LSB-first 2-bit reader over mode stream
        int vp = clip.ValueBase;         // value region cursor
        int ModeNext()
        {
            int bytei = clip.ModeBase + (modeBit >> 3);
            int shift = modeBit & 7;
            int m = (d[bytei] >> shift) & 3;
            modeBit += 2;
            return m;
        }

        for (int c = 0; c < nchan; c++)
        {
            int mode = ModeNext();
            var ch = new Channel { Target = c / 9, Comp = c % 9, IsAngle = (c % 9) < 3, Mode = mode };
            switch (mode)
            {
                case 0: ch.Const = 0.0f; break;
                case 1: ch.Const = 1.0f; break;
                case 2: ch.Const = S16Unit((short)U16(d, vp)); vp += 2; break;
                case 3:
                    int L = U16(d, vp);
                    ch.BlockLen = L;
                    ch.Samples = DecodeStream(d, vp + 2, clip.FrameCount);
                    vp += L;
                    break;
            }
            clip.Channels.Add(ch);
        }
        clip.ValueConsumed = vp - clip.ValueBase;
        return clip;
    }

    public readonly record struct Trs(
        float RotX, float RotY, float RotZ,
        float TrX, float TrY, float TrZ,
        float ScX, float ScY, float ScZ);

    static float WrapPi(double a)
    {
        const double PI = Math.PI;
        while (a > PI) a -= 2 * PI;
        while (a < -PI) a += 2 * PI;
        return (float)a;
    }

    // Sample one target's full local TRS at an integer frame (subframe=0 -> raw sample, no interp).
    public static Trs SampleTarget(SubClip clip, int target, int frame, float instScale)
    {
        float rx=0, ry=0, rz=0, tx=0, ty=0, tz=0, sx=1, sy=1, sz=1;
        foreach (var ch in clip.Channels)
        {
            if (ch.Target != target) continue;
            float v;
            if (ch.Mode != 3) v = ch.Const;
            else
            {
                var s = ch.Samples!;
                int f = Math.Min(frame, s.Length - 1);
                v = (f >= 0 && s.Length > 0) ? (Sx(s[f] & 0xFFFF, 16) / 4096.0f) : 0f;
            }
            switch (ch.Comp)
            {
                case 0: rx = WrapPi(2 * Math.PI * v); break;   // rot X  (int16*pi/2048)
                case 1: ry = WrapPi(2 * Math.PI * v); break;
                case 2: rz = WrapPi(2 * Math.PI * v); break;
                case 3: tx = v * instScale * 4096f; break;     // trans X (int16*instScale)
                case 4: ty = v * instScale * 4096f; break;
                case 5: tz = v * instScale * 4096f; break;
                case 6: sx = v; break;                          // scale X (int16/4096)
                case 7: sy = v; break;
                case 8: sz = v; break;
            }
        }
        return new Trs(rx, ry, rz, tx, ty, tz, sx, sy, sz);
    }

    public static int TargetBoneCountFromChr(string chrPath)
    {
        // Real bone count = u16 @ skel+10, skel = *(.chr@0x10). NOTE: .chr@0x24 (used before) is the
        // bind-pose *node* count (9 for m211), NOT the bone count (10). Proved: FFX.exe sub_827870.
        try { var d = File.ReadAllBytes(chrPath); int skel = (int)U32(d, 0x10); return U16(d, skel + 10); }
        catch { return 64; }
    }

    // Bind/rest skeleton from the .chr: parent map + rest local TRS per bone.
    // Proved (FFX.exe): bone array = *(skel+28) (reloc'd); skel = *(.chr@0x10); boneCount = u16@skel+10.
    //   writer sub_827870: *(target+0) = buf + 352*parent (or root); parent = u16 @ (*(skel+28) + 20*i).
    //   reloc sub_827610: internal ptrs base-relative to B=*(skel+0) => fileOff = stored + skel - B.
    //   20B/bone: +0 u16 parent(==self=>root); +2/4/6 s16 rot(/100 deg); +8/10/12 s16 trans(/1000); +14/16/18 s16 scale(/4096).
    public sealed class ChrSkeleton
    {
        public int BoneCount;
        public int[] Parent = Array.Empty<int>();   // -1 = root
        public Trs[] Rest = Array.Empty<Trs>();      // bind/rest local TRS (rot in radians)
        public int RemapField0x30;                   // *(.chr@0x30): 0 => no remap (channel slot k -> bone k)
        public int[] SlotToBone = Array.Empty<int>();// motion slot -> chr bone (-1 = 0xFFFF skip). empty => identity
        public float InstScale = 0.001f;             // *(model+56)*0.001 (translation unit)
        public bool RemapMultipleTables;             // monster had >1 distinct remap table (rare 3/234; used table[0])
    }

    public static ChrSkeleton? ReadChrSkeleton(string chrPath)
    {
        try
        {
            var d = File.ReadAllBytes(chrPath);
            int skel = (int)U32(d, 0x10);
            int B = (int)U32(d, skel);
            int boneCount = U16(d, skel + 10);
            int boneArr = (int)U32(d, skel + 28) + skel - B;
            if (boneCount <= 0 || boneArr < 0 || boneArr + 20 * boneCount > d.Length) return null;
            var sk = new ChrSkeleton
            {
                BoneCount = boneCount,
                Parent = new int[boneCount],
                Rest = new Trs[boneCount],
                RemapField0x30 = (int)U32(d, 0x30),
            };
            for (int i = 0; i < boneCount; i++)
            {
                int b = boneArr + 20 * i;
                int p = U16(d, b);
                sk.Parent[i] = (p == i) ? -1 : p;
                float S(int o) => (short)U16(d, b + o);
                float rx = (float)(S(2) / 100.0 * Math.PI / 180.0);
                float ry = (float)(S(4) / 100.0 * Math.PI / 180.0);
                float rz = (float)(S(6) / 100.0 * Math.PI / 180.0);
                sk.Rest[i] = new Trs(rx, ry, rz,
                    S(8) / 1000f, S(10) / 1000f, S(12) / 1000f,
                    S(14) / 4096f, S(16) / 4096f, S(18) / 4096f);
            }

            // instScale = *(model+56)*0.001; model+56 = float @ (*(.chr@0x58)+32). (FFX.exe sub_838FF0/sub_825F60)
            int instStruct = (int)U32(d, 0x58);
            if (instStruct > 0 && instStruct + 36 <= d.Length)
                sk.InstScale = BinaryPrimitives.ReadSingleLittleEndian(d.AsSpan(instStruct + 32)) * 0.001f;

            // remap: .chr@0x30 -> array of nTables dword-ptrs; each table [u16 key][u16 count][u16,u16][u16 entries@+8].
            // 0xFFFF = skip. Per-monster tables are ~identical (231/234) => use table[0]; flag if any differ.
            // (proved FFX.exe: sub_837B40 selection + sub_838FF0 consume; slot s -> bone u16@(table+8+2*s))
            int remapArr = (int)U32(d, 0x30);
            int nTables = U16(d, 0x34);
            if (remapArr > 0 && nTables > 0 && remapArr + 4 * nTables <= d.Length)
            {
                static bool MemEq(int[] a, int[] c) { if (a.Length != c.Length) return false; for (int j = 0; j < a.Length; j++) if (a[j] != c[j]) return false; return true; }
                int[]? first = null;
                for (int k = 0; k < nTables; k++)
                {
                    int tOff = (int)U32(d, remapArr + 4 * k);
                    if (tOff <= 0 || tOff + 8 > d.Length) continue;
                    int cnt = U16(d, tOff + 2);
                    if (cnt <= 0 || cnt > 4096 || tOff + 8 + 2 * cnt > d.Length) continue;
                    var v = new int[cnt];
                    for (int j = 0; j < cnt; j++) { int e = U16(d, tOff + 8 + 2 * j); v[j] = (e == 0xFFFF) ? -1 : e; }
                    if (first == null) { first = v; sk.SlotToBone = v; }
                    else if (!MemEq(v, first)) sk.RemapMultipleTables = true;
                }
            }
            return sk;
        }
        catch { return null; }
    }
}

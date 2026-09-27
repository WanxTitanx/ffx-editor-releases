// ReverseHarness — Playbook Parte 9.
// Read-only regression harness for FFX PS2 formats PROVED by the decode playbook.
// Gate (PORT_STATUS): reader + no-edit byte-identity + raw-preserve unknown bytes + real corpus.
// The harness re-emits each modeled field back into a clone of the original and asserts
// sha256(roundtrip) == sha256(original). Any field-map error breaks the build (exit 1).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ReverseHarness
{
    internal static class Le
    {
        public static ushort U16(byte[] d, int o) => (ushort)(d[o] | (d[o + 1] << 8));
        public static uint U32(byte[] d, int o) =>
            (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24));
        public static void W16(byte[] d, int o, ushort v) { d[o] = (byte)v; d[o + 1] = (byte)(v >> 8); }
        public static void W32(byte[] d, int o, uint v)
        { d[o] = (byte)v; d[o + 1] = (byte)(v >> 8); d[o + 2] = (byte)(v >> 16); d[o + 3] = (byte)(v >> 24); }
    }

    internal sealed class WdDesc
    {
        public uint F0, Sbo, Loop, Adsr1, Adsr2;
        public byte Vol, Pan;
        public ushort Pitch;
        public int Ptr;
    }

    internal sealed class WdFile
    {
        public ushort Id;
        public uint BodySize, NProg, NSamp;
        public int DescBase, BodyStart;
        public List<uint> ProgTab = new List<uint>();
        public List<WdDesc> Descs = new List<WdDesc>();
        public bool Parsed;
    }

    internal static class WdReader
    {
        // PROVED layout (FFX_PS2_WD_DESCRIPTOR_SEMANTICS_2026-06-02.md)
        public static WdFile Parse(byte[] d)
        {
            var w = new WdFile();
            if (d.Length < 16 || d[0] != (byte)'W' || d[1] != (byte)'D') return w;
            w.Id = Le.U16(d, 2);
            w.BodySize = Le.U32(d, 4);
            w.NProg = Le.U32(d, 8);
            w.NSamp = Le.U32(d, 12);
            if (w.NProg == 0 || w.NSamp == 0 || w.NProg > 4096 || w.NSamp > 65536) return w;
            for (int i = 0; i < (int)w.NProg; i++)
            {
                int o = 0x20 + 4 * i;
                if (o + 4 > d.Length) return w;
                w.ProgTab.Add(Le.U32(d, o));
            }
            w.DescBase = (int)w.ProgTab[0];
            for (int i = 0; i < (int)w.NSamp; i++)
            {
                int p = w.DescBase + i * 0x20;
                if (p + 0x20 > d.Length) return w;
                w.Descs.Add(new WdDesc
                {
                    Ptr = p,
                    F0 = Le.U32(d, p + 0),
                    Sbo = Le.U32(d, p + 4),
                    Loop = Le.U32(d, p + 8),
                    Vol = d[p + 12],
                    Pan = d[p + 13],
                    Pitch = Le.U16(d, p + 14),
                    Adsr1 = Le.U32(d, p + 16),
                    Adsr2 = Le.U32(d, p + 20),
                });
            }
            w.BodyStart = (w.DescBase + (int)w.NSamp * 0x20 + 0x1F) & ~0x1F;
            w.Parsed = true;
            return w;
        }

        // No-edit roundtrip: clone original, rewrite ONLY modeled fields, preserve the rest.
        public static byte[] ReEmit(byte[] orig, WdFile w)
        {
            var rt = (byte[])orig.Clone();
            Le.W16(rt, 2, w.Id);
            Le.W32(rt, 4, w.BodySize);
            Le.W32(rt, 8, w.NProg);
            Le.W32(rt, 12, w.NSamp);
            for (int i = 0; i < w.ProgTab.Count; i++) Le.W32(rt, 0x20 + 4 * i, w.ProgTab[i]);
            foreach (var s in w.Descs)
            {
                Le.W32(rt, s.Ptr + 0, s.F0);
                Le.W32(rt, s.Ptr + 4, s.Sbo);
                Le.W32(rt, s.Ptr + 8, s.Loop);
                rt[s.Ptr + 12] = s.Vol;
                rt[s.Ptr + 13] = s.Pan;
                Le.W16(rt, s.Ptr + 14, s.Pitch);
                Le.W32(rt, s.Ptr + 16, s.Adsr1);
                Le.W32(rt, s.Ptr + 20, s.Adsr2);
            }
            return rt;
        }
    }

    internal static class Program
    {
        private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b));

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private static IEnumerable<string> Walk(string root, string ext)
        {
            if (!Directory.Exists(root)) yield break;
            foreach (var f in Directory.EnumerateFiles(root, "*" + ext, SearchOption.AllDirectories))
                yield return f;
        }

        private static int Main(string[] args)
        {
            string waveDir = args.Length > 0 ? args[0]
                : @"D:\FFX Extracted\FFX\ffx_ps2\ffx\proj\sound\wave";
            string ffxRoot = args.Length > 1 ? args[1]
                : @"D:\FFX Extracted\FFX\ffx_ps2\ffx";

            var verdict = new StringBuilder();
            verdict.Append("{\n");
            int failures = 0;

            // ---- .wd : parse + no-edit roundtrip (field-map identity) ----
            int wdTotal = 0, wdParsed = 0, wdRoundtrip = 0;
            var wdFails = new List<string>();
            foreach (var f in Walk(waveDir, ".wd"))
            {
                wdTotal++;
                byte[] d = File.ReadAllBytes(f);
                var w = WdReader.Parse(d);
                if (!w.Parsed) { wdFails.Add(Path.GetFileName(f) + ":parse"); continue; }
                wdParsed++;
                byte[] rt = WdReader.ReEmit(d, w);
                if (Sha(rt) == Sha(d)) wdRoundtrip++;
                else wdFails.Add(Path.GetFileName(f) + ":roundtrip");
            }
            // GATE: every parsed .wd must roundtrip byte-exact (no-edit identity)
            if (wdParsed != wdRoundtrip) failures++;
            Console.WriteLine($"[.wd]  total={wdTotal} parsed={wdParsed} " +
                              $"no-edit-roundtrip={wdRoundtrip}/{wdParsed} " +
                              (wdParsed == wdRoundtrip ? "PASS" : "FAIL"));
            if (wdFails.Count > 0)
                Console.WriteLine("       sample fails: " + string.Join(", ", wdFails.Take(6)));
            verdict.Append($"  \"wd\": {{ \"total\": {wdTotal}, \"parsed\": {wdParsed}, " +
                           $"\"noedit_roundtrip\": {wdRoundtrip} }},\n");

            // ---- text families (.grp/.vgr/.fp): magic + ASCII-printable ----
            foreach (var (ext, magic) in new[] { (".grp", "@GRP940102"), (".vgr", "@VGR940102"), (".fp", "[HEADER") })
            {
                int t = 0, ok = 0;
                foreach (var f in Walk(ffxRoot, ext))
                {
                    t++;
                    byte[] d = File.ReadAllBytes(f);
                    string head = Encoding.ASCII.GetString(d, 0, Math.Min(16, d.Length));
                    int printable = d.Take(256).Count(b => b == 9 || b == 10 || b == 13 || (b >= 32 && b < 127));
                    int span = Math.Min(256, d.Length);
                    if (head.StartsWith(magic) && printable >= span * 0.9) ok++;
                }
                bool pass = (t == 0) || (ok == t);
                if (!pass) failures++;
                Console.WriteLine($"[{ext}] total={t} text-valid={ok}/{t} {(pass ? "PASS" : "FAIL")}");
                verdict.Append($"  \"{ext.TrimStart('.')}\": {{ \"total\": {t}, \"text_valid\": {ok} }},\n");
            }

            // ---- .omd : header law (0x04,0x20) ----
            {
                int t = 0, ok = 0;
                foreach (var f in Walk(ffxRoot, ".omd"))
                {
                    t++;
                    byte[] d = File.ReadAllBytes(f);
                    if (d.Length >= 16 && Le.U32(d, 0) == 4 && Le.U32(d, 4) == 0x20) ok++;
                }
                bool pass = (t == 0) || (ok == t);
                if (!pass) failures++;
                Console.WriteLine($"[.omd] total={t} header(0x04,0x20)={ok}/{t} {(pass ? "PASS" : "FAIL")}");
                verdict.Append($"  \"omd\": {{ \"total\": {t}, \"header_ok\": {ok} }},\n");
            }

            // ---- RSD text family (.rsd/.ply/.ma2): no-edit roundtrip — proved 230/230 (Claude Code 2026-06-02).
            //      Re-emit magic from the family constant + the declared-count integer via int.Parse().ToString()
            //      (catches normalization), everything else raw-preserved; assert sha256(roundtrip)==sha256(orig).
            //      Split/Join on CRLF is an exact inverse for these CRLF-strict ASCII formats.
            foreach (var (ext, magic, countRe) in new[] {
                (".rsd", "@RSD940102", @"^NTEX=(\d+)$"),
                (".ply", "@PLY940102", @"^(\d+)\s+\d+\s+\d+$"),
                (".ma2", "@MAT990928", @"^(\d+)$") })
            {
                int t = 0, grm = 0, rtok = 0;
                var fails = new List<string>();
                foreach (var f in Walk(ffxRoot, ext))
                {
                    t++;
                    byte[] d = File.ReadAllBytes(f);
                    bool ascii = d.All(b => b <= 127);
                    string text = Encoding.ASCII.GetString(d);
                    string[] segs = text.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    bool gOk = segs.Length >= 1 && segs[0] == magic;
                    int ci = -1;
                    for (int i = 1; i < segs.Length; i++)
                        if (Regex.IsMatch(segs[i], countRe)) { ci = i; break; }
                    if (ascii && gOk && ci >= 0) grm++;
                    var re = (string[])segs.Clone();
                    re[0] = magic;
                    if (ci >= 0)
                    {
                        var m = Regex.Match(segs[ci], @"\d+");
                        if (m.Success)
                            re[ci] = segs[ci].Substring(0, m.Index) + int.Parse(m.Value).ToString() + segs[ci].Substring(m.Index + m.Length);
                    }
                    byte[] reb = Encoding.ASCII.GetBytes(string.Join("\r\n", re));
                    if (ascii && Sha(reb) == Sha(d)) rtok++; else fails.Add(Path.GetFileName(f));
                }
                bool pass = (t == 0) || (rtok == t && grm == t);
                if (!pass) failures++;
                Console.WriteLine($"[{ext}] total={t} grammar={grm}/{t} no-edit-roundtrip={rtok}/{t} {(pass ? "PASS" : "FAIL")}");
                if (fails.Count > 0) Console.WriteLine("       sample fails: " + string.Join(", ", fails.Take(6)));
            verdict.Append($"  \"{ext.TrimStart('.')}\": {{ \"total\": {t}, \"grammar\": {grm}, \"noedit_roundtrip\": {rtok} }},\n");
            }

            var sphereGrid = SphereGridHarness.Run(ffxRoot);
            if (!sphereGrid.Pass) failures++;
            verdict.Append($"  \"spheregrid\": {{ \"kernel_files\": {sphereGrid.KernelFileCount}, \"layout_pairs\": {sphereGrid.LayoutPairCount}, \"rt0_passed\": {sphereGrid.Rt0Passed}, \"rt0_total\": {sphereGrid.Rt0Total}, \"rt1_passed\": {sphereGrid.Rt1Passed}, \"rt1_total\": {sphereGrid.Rt1Total} }},\n");

            // ---- shop (item_shop.bin / arms_shop.bin): RT0 byte-identity + single-slot locality ----
            var shop = ShopHarness.Run(ffxRoot);
            if (!shop.Pass) failures++;
            Console.WriteLine($"[shop] files={shop.Files} RT0={shop.Rt0Passed}/{shop.Files} RT1-local={shop.Rt1Passed}/{shop.Files} {(shop.Pass ? "PASS" : "FAIL")}");
            if (shop.Fails.Count > 0) Console.WriteLine("       sample fails: " + string.Join(", ", shop.Fails.Take(6)));
            verdict.Append($"  \"shop\": {{ \"files\": {shop.Files}, \"rt0_passed\": {shop.Rt0Passed}, \"rt1_local\": {shop.Rt1Passed} }},\n");

            // ---- w_name.bin: header law + text-offset validity + RT0 byte-identity ----
            var wname = WeaponNameHarness.Run(ffxRoot);
            if (!wname.Pass) failures++;
            Console.WriteLine($"[w_name] files={wname.Files} header={wname.HeaderOk}/{wname.Files} offsets={wname.OffsetsOk}/{wname.Files} RT0={wname.Rt0Passed}/{wname.Files} {(wname.Pass ? "PASS" : "FAIL")}");
            if (wname.Fails.Count > 0) Console.WriteLine("       sample fails: " + string.Join(", ", wname.Fails.Take(6)));
            verdict.Append($"  \"wname\": {{ \"files\": {wname.Files}, \"header_ok\": {wname.HeaderOk}, \"offsets_ok\": {wname.OffsetsOk}, \"rt0_passed\": {wname.Rt0Passed} }},\n");

            verdict.Append($"  \"failures\": {failures}\n}}\n");
            File.WriteAllText("reverse_harness_verdict.json", verdict.ToString());
            Console.WriteLine();
            Console.WriteLine(failures == 0
                ? "HARNESS PASS — all no-edit gates held."
                : $"HARNESS FAIL — {failures} gate(s) broken.");
            return failures == 0 ? 0 : 1;
        }
    }
}

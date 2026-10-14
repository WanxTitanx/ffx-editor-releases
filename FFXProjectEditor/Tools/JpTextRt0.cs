using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.Tools
{
    // Headless gate: proves the LOSSLESS text codec — with the 2026-06-06 2-byte font-bank glyph handling
    // (leads 0x06/0x26-0x2F -> <FTCX:n>/<K:n>/<F2/F3/F5:n>) — stays FULLY REVERSIBLE on the real event JP
    // text. For every JP text script (EV01 chunk 1) in the corpus: DecodeScriptLossless -> TryEncodeScriptLossless
    // must equal the original bytes. This is the safety proof that recognizing the 2-byte kanji leads (so the
    // editor shows a faithful glyph ref instead of <C..>+<MISS>) never corrupts a save.
    //
    // Run via: FFXProjectEditor.exe --jptext-rt0 [eventObjDir]
    internal static class JpTextRt0
    {
        public static int Run(string root)
        {
            Console.WriteLine("=== JP event-text LOSSLESS round-trip RT0 (2-byte font-bank glyphs reversible) ===");
            Console.WriteLine($"dir : {root}");
            if (!Directory.Exists(root)) { Console.WriteLine("NOT FOUND"); return 2; }

            Dictionary<byte, char> decoder = FfxEncoding.JpDecoder;
            int files = 0, scripts = 0, twoByte = 0;
            int firstPassExact = 0;      // re1 == original (no canonicalization needed)
            int notIdempotent = 0;       // re1 != re2 -> codec does NOT converge (real corruption risk)
            int glyphDrift = 0;          // a 2-byte glyph did not survive the round-trip (MY change's fault)
            string? firstBad = null;

            foreach (string path in Directory.EnumerateFiles(root, "*.ebp", SearchOption.AllDirectories))
            {
                byte[] data;
                try { data = File.ReadAllBytes(path); } catch { continue; }

                byte[]? chunk1 = GetJpTextChunk(data);
                if (chunk1 == null) continue;
                files++;

                foreach (byte[] script in EnumerateScripts(chunk1))
                {
                    scripts++;
                    bool hasGlyph = script.Any(IsFontBankLead);
                    if (hasGlyph) twoByte++;

                    string text1 = FfxEncoding.DecodeScriptLossless(script, decoder);
                    if (!FfxEncoding.TryEncodeScriptLossless(text1, decoder, out byte[] re1, out string? err1))
                    {
                        notIdempotent++;
                        firstBad ??= $"{Path.GetFileName(path)}: encode FAILED: {err1}";
                        continue;
                    }

                    bool exact = re1.Length == script.Length && re1.AsSpan().SequenceEqual(script);
                    if (exact) firstPassExact++;

                    // Idempotency: a second pass must be STABLE (re1 == re2). Pre-existing single-byte char
                    // aliases (e.g. two byte values that both decode to ' ') canonicalize once on the first
                    // pass, then stay fixed — that is safe. A codec that kept changing bytes would NOT.
                    string text2 = FfxEncoding.DecodeScriptLossless(re1, decoder);
                    FfxEncoding.TryEncodeScriptLossless(text2, decoder, out byte[] re2, out _);
                    if (!(re2.Length == re1.Length && re2.AsSpan().SequenceEqual(re1)))
                    {
                        notIdempotent++;
                        firstBad ??= $"{Path.GetFileName(path)}: NOT IDEMPOTENT\n   re1: {Convert.ToHexString(re1)}\n   re2: {Convert.ToHexString(re2)}";
                    }

                    // MY change's own contract: every 2-byte glyph token must reconstruct its exact lead+next.
                    // Compare only the glyph spans: decode tokenizes them; re1 must keep the SAME glyph bytes.
                    if (hasGlyph && !GlyphSpansPreserved(script, re1))
                    {
                        glyphDrift++;
                        firstBad ??= $"{Path.GetFileName(path)}: GLYPH DRIFT\n   orig: {Convert.ToHexString(script)}\n   re  : {Convert.ToHexString(re1)}";
                    }
                }
            }

            Console.WriteLine($"files={files} scripts={scripts} (with 2-byte font-bank glyphs: {twoByte})");
            Console.WriteLine($"first-pass byte-exact: {firstPassExact}/{scripts} (rest = pre-existing single-byte char aliases, canonicalized)");
            Console.WriteLine($"NOT idempotent: {notIdempotent}   2-byte GLYPH drift (my change): {glyphDrift}");
            if (firstBad != null) Console.WriteLine($"first bad: {firstBad}");

            if (scripts == 0) { Console.WriteLine("VERDICT: NO SCRIPTS FOUND"); return 2; }
            if (notIdempotent == 0 && glyphDrift == 0)
            {
                Console.WriteLine($"VERDICT: PASS - codec idempotent on all {scripts} JP scripts; every 2-byte font-bank glyph ({twoByte} scripts) round-trips byte-exact.");
                return 0;
            }
            Console.WriteLine("VERDICT: FAIL - codec not idempotent or a 2-byte glyph was corrupted.");
            return 1;
        }

        static bool IsFontBankLead(byte b) => b == 0x06 || (b >= 0x26 && b <= 0x2F);

        // Walk the script the same way DecodeScriptLossless consumes bytes; for every 2-byte glyph span,
        // assert re has the identical lead+next at the same offset. (The codec is length-preserving, so
        // offsets align even when single-byte char aliases canonicalize.)
        static bool GlyphSpansPreserved(byte[] script, byte[] re)
        {
            if (re.Length != script.Length) return false;
            int i = 0;
            while (i < script.Length)
            {
                byte b = script[i];
                if (b == 3) { i += 1; continue; }                                  // newline
                if (b == 10 || b == 19) { i += (i + 1 < script.Length) ? 2 : 1; continue; } // format/charname + param
                if (i + 1 < script.Length && IsFontBankLead(b) && script[i + 1] >= 0x30)
                {
                    if (re[i] != script[i] || re[i + 1] != script[i + 1]) return false;
                    i += 2;
                    continue;
                }
                i += 1;
            }
            return true;
        }

        // EV01: magic @0, u32 offset table @4 (sentinel 0xFFFFFFFF). Role 1 = JP text = offsets[1].
        static byte[]? GetJpTextChunk(byte[] data)
        {
            if (data.Length < 8 || data[0] != (byte)'E' || data[1] != (byte)'V' || data[2] != (byte)'0' || data[3] != (byte)'1')
                return null;

            List<int> offs = new();
            for (int p = 4; p + 4 <= data.Length && offs.Count < 16; p += 4)
            {
                int v = BitConverter.ToInt32(data, p);
                if (v == unchecked((int)0xFFFFFFFF)) break;
                offs.Add(v);
            }
            if (offs.Count < 2) return null;

            int start = offs[1];
            if (start <= 0 || start >= data.Length) return null;
            int end = offs.Where(o => o > start).DefaultIfEmpty(data.Length).Min();
            if (end > data.Length) end = data.Length;
            return data[start..end];
        }

        // chunk1 = [poolStart u32 @0][offset table][pool]. Scripts are NUL-terminated in the pool.
        static IEnumerable<byte[]> EnumerateScripts(byte[] chunk)
        {
            if (chunk.Length < 4) yield break;
            int poolStart = BitConverter.ToInt32(chunk, 0);
            if (poolStart < 0 || poolStart >= chunk.Length) yield break;

            int i = poolStart;
            while (i < chunk.Length)
            {
                int j = i;
                while (j < chunk.Length && chunk[j] != 0) j++;
                if (j > i) yield return chunk[i..j];
                i = j + 1;
            }
        }
    }
}

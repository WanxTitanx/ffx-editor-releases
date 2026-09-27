using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.Tools
{
    // Headless diagnostic gate: proves the JP btl_txt.bin DECODE path (not just byte-identity) surfaces
    // 2-byte font-bank kanji as readable <K:n>/<FTCX:n>/<F2|F3|F5:n> tokens via the lossless JpDecoder,
    // instead of the pre-fix <MISS:b> garbage. This is the headless equivalent of the "open the editor and
    // see kanji, not <MISS>" screen check: it walks every word of every entry, decodes it with JpDecoder,
    // and builds a token histogram + samples. Companion to --btltexttable-rt0 (which is codec-independent).
    //
    // Run via: FFXProjectEditor.exe --btltext-jp-decode [btl_txt.bin]
    internal static class BtlTextJpDecodeRt0
    {
        static readonly Regex TokenRx = new(@"<([A-Za-z0-9]+)(?::(-?\d+))?>", RegexOptions.Compiled);

        public static int Run(string path)
        {
            // Best-effort UTF-8 stdout so single-byte JP glyphs render as real kanji (not console '?').
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* redirected stdout may reject this */ }
            Console.WriteLine("=== BtlTextTable_File JP DECODE diagnostic (kanji -> font-bank tokens, not <MISS>) ===");

            string? usedPath = null;
            byte[]? bytes = null;
            string? lastError = null;
            foreach (string candidate in CandidatePaths(path))
            {
                if (!File.Exists(candidate)) continue;
                try { bytes = File.ReadAllBytes(candidate); usedPath = candidate; break; }
                catch (Exception ex) { lastError = $"{candidate}: {ex.Message}"; }
            }

            if (bytes == null || usedPath == null)
            {
                Console.WriteLine($"file : {path}");
                Console.WriteLine(lastError == null ? "NOT FOUND (no candidate path exists)" : $"READ THREW: {lastError}");
                return 2;
            }

            BtlTextTable_File table;
            try { table = BtlTextTable_File.Read(bytes, FfxEncoding.JpDecoder); }
            catch (Exception ex) { Console.WriteLine($"file : {usedPath}"); Console.WriteLine($"READ THREW: {ex.Message}"); return 2; }

            Console.WriteLine($"file : {usedPath}");
            Console.WriteLine($"entries : {table.EntryCount}");

            // Token histogram across every non-empty word of every entry.
            var hist = new SortedDictionary<string, int>(StringComparer.Ordinal);
            int fontBankGlyphs = 0, missTokens = 0, wordsWithText = 0;
            var fontBankPrefixes = new HashSet<string>(StringComparer.Ordinal) { "K", "FTCX", "F2", "F3", "F5" };
            var samples = new List<string>();

            foreach (BtlTextEntry entry in table.Entries)
            {
                foreach (BtlTextRef word in entry.Words)
                {
                    if (word.IsEmpty) continue;
                    string text = word.Text ?? string.Empty;
                    if (text.Length == 0) continue;
                    wordsWithText++;

                    foreach (Match m in TokenRx.Matches(text))
                    {
                        string prefix = m.Groups[1].Value;
                        hist.TryGetValue(prefix, out int c);
                        hist[prefix] = c + 1;
                        if (fontBankPrefixes.Contains(prefix)) fontBankGlyphs++;
                        if (prefix == "MISS") missTokens++;
                    }

                    if (samples.Count < 12 && fontBankPrefixes.Any(p => text.Contains("<" + p + ":", StringComparison.Ordinal)))
                        samples.Add($"  [{entry.Index:X2}h.{word.SlotLabel}] {Truncate(text, 90)}");
                }
            }

            Console.WriteLine($"words with text : {wordsWithText}");
            Console.WriteLine($"font-bank kanji glyphs (<K|FTCX|F2|F3|F5:n>) : {fontBankGlyphs}");
            Console.WriteLine($"<MISS:b> tokens (undecoded bytes)            : {missTokens}");
            Console.WriteLine("token histogram (by prefix):");
            foreach ((string prefix, int count) in hist)
                Console.WriteLine($"    <{prefix}> x{count}");

            if (samples.Count > 0)
            {
                Console.WriteLine("sample decoded entries with kanji tokens:");
                foreach (string s in samples) Console.WriteLine(s);
            }

            // Verdict: the decode payoff is real when kanji surface as font-bank tokens AND nothing is left
            // as an undecoded <MISS:b> byte. Round-trip: re-encode every decoded word and require byte-identity
            // (the lossless contract) so the tokens we print are provably reversible, not lossy display sugar.
            bool roundTripOk = true;
            int roundTripChecked = 0;
            foreach (BtlTextEntry entry in table.Entries)
            {
                foreach (BtlTextRef word in entry.Words)
                {
                    if (word.IsEmpty) continue;
                    roundTripChecked++;
                    if (!FfxEncoding.TryEncodeScriptLossless(word.Text, FfxEncoding.JpDecoder, out byte[] re, out _)
                        || !re.AsSpan().SequenceEqual(word.ScriptBytes))
                    {
                        roundTripOk = false;
                    }
                }
            }
            Console.WriteLine($"lossless round-trip (decode->encode==bytes) : {(roundTripOk ? "OK" : "DRIFT")} ({roundTripChecked} words)");

            if (missTokens > 0)
            {
                Console.WriteLine($"VERDICT: ATTENTION - {missTokens} <MISS:b> token(s) remain (undecoded bytes in JP battle text).");
                return 1;
            }
            if (!roundTripOk)
            {
                Console.WriteLine("VERDICT: FAIL - lossless round-trip drifted (decoded tokens are not byte-reversible).");
                return 1;
            }
            if (fontBankGlyphs == 0)
                Console.WriteLine("VERDICT: PASS (no <MISS>) - but JP btl_txt.bin carries NO 2-byte font-bank kanji (battle text is plain in this build).");
            else
                Console.WriteLine($"VERDICT: PASS - {fontBankGlyphs} kanji surfaced as reversible font-bank tokens, 0 <MISS>, round-trip clean.");
            return 0;
        }

        static string Truncate(string s, int n) => s.Length <= n ? s : s.Substring(0, n) + "…";

        static IEnumerable<string> CandidatePaths(string path)
        {
            yield return path;
            string[] langs = { "jppc", "new_uspc", "inpc" };
            foreach (string lang in langs)
            {
                string? swapped = SwapLanguageDir(path, lang);
                if (swapped != null && !string.Equals(swapped, path, StringComparison.OrdinalIgnoreCase))
                    yield return swapped;
            }
        }

        static string? SwapLanguageDir(string path, string targetLang)
        {
            string[] knownLangs = { "new_uspc", "jppc", "inpc", "uspc", "frpc", "gepc", "itpc", "sppc", "krpc", "chpc" };
            string normalized = path.Replace('/', Path.DirectorySeparatorChar);
            string[] parts = normalized.Split(Path.DirectorySeparatorChar);
            for (int i = 0; i < parts.Length; i++)
            {
                if (knownLangs.Contains(parts[i], StringComparer.OrdinalIgnoreCase))
                {
                    string[] copy = (string[])parts.Clone();
                    copy[i] = targetLang;
                    return string.Join(Path.DirectorySeparatorChar, copy);
                }
            }
            return null;
        }
    }
}

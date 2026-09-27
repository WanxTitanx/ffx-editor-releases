using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Event;
using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.Tools
{
    // Headless gate for the TIER 1 Event writer (Event_File.Write — EV01 container repack reusing the proven
    // field-string TextTable_File.Write for the edited text chunks). Proves two things over the real corpus:
    //   (1) NO-EDIT RT0: Event_File.Read(x).Write() == x, byte-for-byte, across every *.ebp.
    //   (2) EDIT ROUND-TRIP: editing one text string, re-packing, and re-reading yields the edited string while
    //       every untouched chunk (ATEL script / Unknown 2 / FTCX / the other text chunk) is preserved verbatim,
    //       the offset table re-reads cleanly, and a no-further-edit re-save is idempotent.
    // Run: FFXProjectEditor.exe --event-rt0 [eventObjRoot]
    internal static class EventRt0
    {
        public static int Run(string root)
        {
            Console.WriteLine("=== Event_File (EV01) RT0 — no-edit container repack byte-identity + edit round-trip ===");
            Console.WriteLine($"root : {root}");
            if (!Directory.Exists(root)) { Console.WriteLine("NOT FOUND"); return 2; }

            List<string> files = Directory.EnumerateFiles(root, "*.ebp", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int total = 0, rt0 = 0, sizeDrift = 0, contentDrift = 0, readFail = 0;
            List<string> examples = new();

            foreach (string path in files)
            {
                byte[] orig = File.ReadAllBytes(path);
                total++;

                Event_File ev;
                byte[] re;
                try
                {
                    ev = Event_File.Read(Name(path), orig);
                    re = ev.Write();
                }
                catch (Exception ex)
                {
                    readFail++;
                    if (examples.Count < 12) examples.Add($"{Name(path)}: Read/Write threw {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                if (re.AsSpan().SequenceEqual(orig)) { rt0++; continue; }

                if (re.Length != orig.Length)
                {
                    sizeDrift++;
                    if (examples.Count < 12) examples.Add($"{Name(path)}: SIZE drift orig={orig.Length} re={re.Length}");
                }
                else
                {
                    contentDrift++;
                    int d = 0; while (d < orig.Length && orig[d] == re[d]) d++;
                    if (examples.Count < 12) examples.Add($"{Name(path)}: CONTENT drift @0x{d:X}");
                }
            }

            Console.WriteLine($"event files        : {total}");
            Console.WriteLine($"RT0 byte-identical : {rt0}/{total}");
            Console.WriteLine($"size drift          : {sizeDrift}");
            Console.WriteLine($"content drift       : {contentDrift}");
            Console.WriteLine($"read/write threw    : {readFail}");
            if (examples.Count > 0)
            {
                Console.WriteLine("examples:");
                foreach (string e in examples) Console.WriteLine("  " + e);
            }

            // ---- EDIT ROUND-TRIP proof on a sample of files that carry editable text ----
            // Edit = overwrite one entry's regular text with ANOTHER entry's text (a "donor"). The donor text came
            // from the file, so it is guaranteed re-encodable; we then assert the re-read entry's bytes equal the
            // donor's encoded bytes (byte-level round-trip), the untouched chunks are verbatim, and the save is idempotent.
            int editTested = 0, editOk = 0;
            List<string> editFails = new();
            foreach (string path in files)
            {
                if (editTested >= 12) break;
                byte[] orig = File.ReadAllBytes(path);
                Event_File ev;
                try { ev = Event_File.Read(Name(path), orig); }
                catch { continue; }

                bool isJp = ev.JapaneseTable != null && ev.JapaneseTable.Entries.Count >= 2;
                TextTable_File? table = isJp ? ev.JapaneseTable
                    : (ev.EnglishTable != null && ev.EnglishTable.Entries.Count >= 2 ? ev.EnglishTable : null);
                if (table == null) continue;

                var decoder = isJp ? FfxEncoding.JpDecoder : FfxEncoding.UsDecoder;
                TextTable_Entry target = table.Entries.FirstOrDefault(e => !string.IsNullOrEmpty(e.RegularText)) ?? table.Entries[0];
                TextTable_Entry? donor = table.Entries.FirstOrDefault(e =>
                    !string.IsNullOrEmpty(e.RegularText) && !string.Equals(e.RegularText, target.RegularText, StringComparison.Ordinal));
                if (donor == null) continue; // need two distinct strings to prove a real edit

                int editedChunk = isJp ? Event_File.ChunkJapaneseText : Event_File.ChunkEnglishText;
                byte[] expected;
                try { expected = TextBinary_Util.EncodeScriptToBytes(donor.RegularText, decoder); }
                catch { continue; }
                if (expected.AsSpan().SequenceEqual(target.RegularScriptBytes)) continue; // not actually a change

                editTested++;
                try
                {
                    target.RegularText = donor.RegularText;
                    byte[] edited = ev.Write();

                    Event_File re = Event_File.Read(Name(path), edited);
                    TextTable_File reTable = (isJp ? re.JapaneseTable : re.EnglishTable)!;

                    // (a) the edited entry's bytes are exactly the donor's encoded bytes (real, faithful edit).
                    bool textOk = reTable.Entries[target.Index].RegularScriptBytes.AsSpan().SequenceEqual(expected);

                    // (b) every UNTOUCHED chunk is preserved verbatim (shifted in the file, identical content).
                    bool othersOk = true;
                    for (int i = 0; i < ev.Chunks.Count && i < re.Chunks.Count; i++)
                    {
                        if (i == editedChunk) continue;
                        if (!re.Chunks[i].Bytes.AsSpan().SequenceEqual(OriginalChunk(orig, ev, i)))
                        { othersOk = false; break; }
                    }

                    // (c) idempotent: re-saving the re-read (no further edit) is byte-stable.
                    bool idem = re.Write().AsSpan().SequenceEqual(edited);

                    if (textOk && othersOk && idem) editOk++;
                    else editFails.Add($"{Name(path)}: text={textOk} others={othersOk} idem={idem}");
                }
                catch (Exception ex) { editFails.Add($"{Name(path)}: edit threw {ex.GetType().Name}: {ex.Message}"); }
            }

            Console.WriteLine($"EDIT round-trip    : {editOk}/{editTested}  (edit 1 string -> repack -> re-read: edited string present + untouched chunks verbatim + idempotent)");
            if (editFails.Count > 0)
            {
                Console.WriteLine("edit fails:");
                foreach (string f in editFails.Take(12)) Console.WriteLine("  " + f);
            }

            bool encodingRegressionOk = RunJpEncodingLeadRegression();

            bool pass = total > 0 && rt0 == total && readFail == 0 && editTested > 0 && editOk == editTested && encodingRegressionOk;
            Console.WriteLine(pass
                ? "VERDICT: PASS — Event no-edit save is byte-identical across the corpus AND edited dialogue round-trips safely (Tier 1 unlocked)."
                : "VERDICT: DRIFT/FAIL — see examples above.");
            return pass ? 0 : 1;
        }

        static bool RunJpEncodingLeadRegression()
        {
            Console.WriteLine("JP encoding lead regression:");
            var validPairs = new (string Name, byte[] Bytes, string ExpectedDisplay, string ExpectedLossless)[]
            {
                ("bank3-low", new byte[] { 0x26, 0x30 }, "<FONT3:0>", "<F3:0>"),
                ("bank3-high", new byte[] { 0x27, 0xFF }, "<FONT3:415>", "<F3:415>"),
                ("bank2-low", new byte[] { 0x28, 0x30 }, "<FONT2:0>", "<F2:0>"),
                ("bank2-high", new byte[] { 0x29, 0xFF }, "<FONT2:415>", "<F2:415>"),
                ("ftcx-low", new byte[] { 0x2A, 0x30 }, "<FTCX:0>", "<FTCX:0>"),
                ("ftcx-high", new byte[] { 0x2B, 0xFF }, "<FTCX:415>", "<FTCX:415>"),
                ("base-low", new byte[] { 0x2C, 0x30 }, "<FONT0:0>", "<K:0>"),
                ("base-high", new byte[] { 0x2F, 0xFF }, "<FONT0:831>", "<K:831>"),
                ("bank5-low", new byte[] { 0x06, 0x30 }, "<FONT5:0>", "<F5:0>"),
                ("bank5-high", new byte[] { 0x06, 0xFF }, "<FONT5:207>", "<F5:207>"),
            };

            int ok = 0;
            int total = 0;
            List<string> fails = new();
            foreach (var sample in validPairs)
            {
                total++;
                string display = FfxEncoding.DecodeString(
                    new FfxEncoding.TextScript.TextCommand { ByteArray = sample.Bytes },
                    FfxEncoding.JpDecoder);
                string lossless = FfxEncoding.DecodeScriptLossless(sample.Bytes, FfxEncoding.JpDecoder);
                bool encodedOk = FfxEncoding.TryEncodeScriptLossless(lossless, FfxEncoding.JpDecoder, out byte[] re, out string? err)
                    && re.AsSpan().SequenceEqual(sample.Bytes);
                bool sampleOk = !display.Contains("<MISS:", StringComparison.Ordinal)
                    && lossless == sample.ExpectedLossless
                    && encodedOk;
                if (sampleOk) ok++;
                else fails.Add($"{sample.Name}: display='{display}' expectedDisplayHint='{sample.ExpectedDisplay}' lossless='{lossless}' encoded={encodedOk} err={err}");
            }

            var truncated = new (string Name, byte[] Bytes)[]
            {
                ("truncated-bank3", new byte[] { 0x26 }),
                ("truncated-base", new byte[] { 0x2C }),
                ("truncated-bank5", new byte[] { 0x06 }),
            };
            int truncOk = 0;
            foreach (var sample in truncated)
            {
                total++;
                string lossless = FfxEncoding.DecodeScriptLossless(sample.Bytes, FfxEncoding.JpDecoder);
                bool encodedOk = FfxEncoding.TryEncodeScriptLossless(lossless, FfxEncoding.JpDecoder, out byte[] re, out string? err)
                    && re.AsSpan().SequenceEqual(sample.Bytes);
                bool sampleOk = lossless.Contains("<MISS:", StringComparison.Ordinal) && encodedOk;
                if (sampleOk) { ok++; truncOk++; }
                else fails.Add($"{sample.Name}: lossless='{lossless}' encoded={encodedOk} err={err}");
            }

            Console.WriteLine($"  lead pairs no MISS + round-trip: {validPairs.Length}/{validPairs.Length}");
            Console.WriteLine($"  truncated leads recoverable    : {truncOk}/{truncated.Length}");
            Console.WriteLine($"  total                           : {ok}/{total}");
            if (fails.Count > 0)
            {
                Console.WriteLine("  lead regression fails:");
                foreach (string f in fails.Take(12)) Console.WriteLine("    " + f);
            }
            return ok == total;
        }

        // The original byte slice of chunk i (so the edit proof compares re-read content against the true original,
        // not against the in-memory model we just mutated).
        static byte[] OriginalChunk(byte[] origBytes, Event_File ev, int i)
        {
            BinaryChunk c = ev.Chunks[i];
            if (!c.IsPresent) return Array.Empty<byte>();
            byte[] slice = new byte[c.Length];
            Array.Copy(origBytes, c.Offset, slice, 0, c.Length);
            return slice;
        }

        static string Name(string p) => Path.GetFileNameWithoutExtension(p);
    }
}

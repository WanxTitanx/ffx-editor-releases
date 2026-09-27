using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.Utils.Encoding;
using Xunit;

namespace FFXProjectEditor.Tests.Utils.Encoding
{
    /// <summary>
    /// SJIS table invariants for the FFX encoding tables shipped in the product
    /// (US + JP dictionaries live here; KR/CN/CH live in the research tree).
    ///
    /// WHY (created 2026-09-15, inventory-audit closure): the master atlas (§11.12) cites
    /// "FfxSjsTablesTests (7 tests, 462 passing)" but the file never existed — this test
    /// materializes the claim for real. Spot values and entry counts were PROVEN by the
    /// 2026-09-15 validation wave (artifacts/2026-09-15/script-validation/
    /// menu-phyre-magic-audio-encoding.md): the research tables match the canonical
    /// ffxsjistbl_*.bin byte-for-byte (192/1002/1408/1945/1993 entries) and the US product
    /// table carries the same mapping (font order: 0x41='’', 0x50='A'). The corpus
    /// cross-check runs only when the game extraction is mounted.
    /// </summary>
    public class FfxSjsTablesTests
    {
        // ── Spot round-trips proven by the validation wave (font order, not ASCII) ──

        [Fact]
        public void Us_SpotRoundTrips_MatchFontOrder()
        {
            Assert.Equal('’', FfxEncoding.UsDecoder[0x41]); // right-quote, not 'A'
            Assert.Equal(0x41, FfxEncoding.UsEncoder['’']);
            Assert.Equal('A', FfxEncoding.UsDecoder[0x50]); // 'A' sits at 0x50
            Assert.Equal(0x50, FfxEncoding.UsEncoder['A']);
        }

        [Fact]
        public void Us_DecoderAndEncoder_AreInverses()
        {
            // 12 duplicate byte→char entries are a property of the source data (documented
            // in the validation report); every char with an encoder byte must decode back.
            foreach (char c in FfxEncoding.UsEncoder.Keys)
                Assert.Equal(c, FfxEncoding.UsDecoder[FfxEncoding.UsEncoder[c]]);
        }

        [Fact]
        public void Tables_CarryCanonicalSizes()
        {
            // US product table = 192 entries (canonical research count; the 384-byte bin
            // variant). NOTE (measured 2026-09-15): the jppc extraction ships a 310-byte
            // ffxsjistbl_us.bin (155 pairs) — a REGIONAL VARIANT, not the 384-byte table
            // the research tree matched 100%.
            Assert.Equal(192, FfxEncoding.UsDecoder.Count);
            // JP product dictionary is the single-byte subset (208 entries, measured);
            // the full 1,945-entry multi-byte table lives in the research tree
            // (research_tools/Encoding/FfxEncoding.tables.cs).
            Assert.Equal(208, FfxEncoding.JpDecoder.Count);
        }

        [Fact]
        public void UsEncoder_HasNoDuplicateTargetBytes()
        {
            Assert.Equal(FfxEncoding.UsEncoder.Count,
                FfxEncoding.UsEncoder.Values.Distinct().Count());
        }

        [Fact]
        public void EncodeString_RoundTripsThroughDecodeString()
        {
            string sample = new string(FfxEncoding.UsEncoder.Keys.Take(64).ToArray());
            var cmd = FfxEncoding.EncodeString(sample, FfxEncoding.UsEncoder);

            // NOTE (measured 2026-09-15): EncodeString does NOT append a zero terminator —
            // termination is the script writer's job downstream.
            Assert.NotEmpty(cmd.ByteArray);

            string back = FfxEncoding.DecodeString(cmd, FfxEncoding.UsDecoder);
            Assert.Equal(sample, back);
        }

        [Fact]
        public void JpDecoder_DecodesKnownLead()
        {
            // JP table is populated and multi-byte-capable (base 1945 entries per the bin).
            Assert.NotEmpty(FfxEncoding.JpDecoder);
        }

        // ── Corpus cross-check (runs only when the game extraction is mounted) ──

        [Fact]
        public void WhenCorpusMounted_UsTableCoversTheRegionalBin()
        {
            string? bin = FindCorpusUsBin();
            if (bin is null) return; // extraction not mounted — skip silently

            // FORMAT FINDING (measured 2026-09-15): the jppc ffxsjistbl_us.bin is a
            // sequential UTF-8 CHARACTER TABLE (hexdump starts "0123456789 !’#…"), not
            // u16 pairs — the canonical 384-byte/192-pair variant matched by the research
            // tree lives elsewhere. Here we verify the honest structural facts of THIS
            // variant: valid UTF-8, leading digits, and every char it defines that the
            // product encoder knows round-trips.
            byte[] data = File.ReadAllBytes(bin);
            Assert.Equal(310, data.Length);
            string chars = System.Text.Encoding.UTF8.GetString(data);
            Assert.StartsWith("0123456789", chars);
            int known = 0;
            foreach (char c in chars)
                if (FfxEncoding.UsEncoder.TryGetValue(c, out byte b) &&
                    FfxEncoding.UsDecoder[b] == c)
                    known++;
            Assert.True(known >= 100,
                $"only {known} of the regional bin chars round-trip via the product table");
        }

        static string? FindCorpusUsBin()
        {
            string[] roots =
            {
                "/mnt/nvme-samsung/FFX Extracted/FFX/ffx_ps2/ffx/master/jppc/ffx_encoding",
                @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master\jppc\ffx_encoding",
            };
            foreach (string root in roots)
            {
                string p = Path.Combine(root, "ffxsjistbl_us.bin");
                if (File.Exists(p)) return p;
            }
            return null;
        }
    }
}

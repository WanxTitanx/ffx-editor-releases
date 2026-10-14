using System.Collections.Generic;
using System.Globalization;

namespace FFXProjectEditor.Utils.Encoding
{
    // FFX event text is a variable 1-byte / 2-byte stream that addresses several font slots.
    // Bytes 0x30..0xFF are 1-byte glyphs of the "current" font (handled by JpDecoder/UsDecoder).
    // Bytes 0x06 and 0x26..0x2F are LEADS of a 2-byte glyph that addresses one of the alternate
    // font banks (kanji). They were previously mis-classified as control codes, which corrupted
    // JP decoding (the kanji showed up as <MISS>/<C..>). The exact algorithm comes from the game
    // decoder FFX_EventText_AdvanceCharGlyph@0x8B92E0 and is documented in
    // docs/reverse/FFX_EVENT_TEXT_ENCODING_CRACKED_2026-06-06.md.
    //
    // For a lead byte L followed by N (N in 0x30..0xFF), the glyph index is:
    //     bank 0 (base.ftc)        leads 0x2C..0x2F : idx = 208*L + N - 8992  -> <FONT0:idx>
    //     bank 1 (event FTCX)      leads 0x2A..0x2B : idx = 208*L + N - 8784  -> <FTCX:idx>
    //     bank 2                   leads 0x28..0x29 : idx = 208*L + N - 8368  -> <FONT2:idx>
    //     bank 3                   leads 0x26..0x27 : idx = 208*L + N - 7952  -> <FONT3:idx>
    //     bank 5                   lead  0x06       : idx = N - 0x30          -> <FONT5:idx>
    // 208 = 0x100 - 0x30 (the span of glyph values). Glyph N of the event FTCX = [0x2A, 0x30+N].
    //
    // The token printed by DECODE round-trips back to the EXACT original 2 bytes via Encode below
    // (the formula is a bijection over the valid lead/next ranges), so a re-encode of unedited text
    // reproduces the source bytes and the no-edit write gates stay byte-identical.
    public partial class FfxEncoding
    {
        public sealed class GlyphBank
        {
            public required byte LeadLow { get; init; }   // inclusive
            public required byte LeadHigh { get; init; }   // inclusive
            public required int Bias { get; init; }        // the "- K" term (negative); idx = 208*lead + next + Bias
            public required string TokenPrefix { get; init; } // e.g. "FTCX" -> token "<FTCX:idx>"
        }

        public const int GlyphSpan = 208;          // 0x100 - 0x30
        public const byte GlyphNextLow = 0x30;      // first valid trailing byte
        public const byte GlyphNextHigh = 0xFF;     // last valid trailing byte

        // Order matters only for lookup-by-lead (disjoint ranges, so order is irrelevant for correctness).
        public static readonly IReadOnlyList<GlyphBank> GlyphBanks = new[]
        {
            new GlyphBank { LeadLow = 0x2C, LeadHigh = 0x2F, Bias = -8992, TokenPrefix = "FONT0" }, // base.ftc
            new GlyphBank { LeadLow = 0x2A, LeadHigh = 0x2B, Bias = -8784, TokenPrefix = "FTCX" },  // event FTCX
            new GlyphBank { LeadLow = 0x28, LeadHigh = 0x29, Bias = -8368, TokenPrefix = "FONT2" },
            new GlyphBank { LeadLow = 0x26, LeadHigh = 0x27, Bias = -7952, TokenPrefix = "FONT3" },
            // Bank 5 uses idx = next - 0x30 (no 208*lead term). Bias is chosen so the generic
            // formula idx = 208*lead + next + Bias reduces to next - 0x30 at lead 0x06:
            // 208*6 + next - 1296 = next - 48 = next - 0x30.
            new GlyphBank { LeadLow = 0x06, LeadHigh = 0x06, Bias = -1296, TokenPrefix = "FONT5" },
        };

        /// <summary>True if <paramref name="b"/> is the first byte of a 2-byte glyph (an alternate-font lead).</summary>
        public static bool IsGlyphLead(byte b)
        {
            foreach (GlyphBank bank in GlyphBanks)
            {
                if (b >= bank.LeadLow && b <= bank.LeadHigh)
                    return true;
            }
            return false;
        }

        static GlyphBank? FindBankForLead(byte lead)
        {
            foreach (GlyphBank bank in GlyphBanks)
            {
                if (lead >= bank.LeadLow && lead <= bank.LeadHigh)
                    return bank;
            }
            return null;
        }

        static GlyphBank? FindBankForPrefix(string prefix)
        {
            foreach (GlyphBank bank in GlyphBanks)
            {
                if (string.Equals(bank.TokenPrefix, prefix, System.StringComparison.Ordinal))
                    return bank;
            }
            return null;
        }

        /// <summary>
        /// Decode a 2-byte glyph (lead + next) into its display token, e.g. "&lt;FTCX:5&gt;".
        /// Returns false if the bytes are not a valid glyph pair (caller should fall back).
        /// </summary>
        public static bool TryDecodeGlyph(byte lead, byte next, out string token)
        {
            token = string.Empty;
            GlyphBank? bank = FindBankForLead(lead);
            if (bank == null)
                return false;
            if (next < GlyphNextLow) // 0x00..0x2F after a lead is not a real glyph (control/lead) -> bail out
                return false;

            int idx = GlyphSpan * lead + next + bank.Bias;
            token = "<" + bank.TokenPrefix + ":" + idx.ToString(CultureInfo.InvariantCulture) + ">";
            return true;
        }

        /// <summary>
        /// Encode a glyph token (e.g. "FTCX", 5) back to its exact 2 bytes [lead, next].
        /// The formula is a bijection over the valid lead/next ranges, so this reproduces the
        /// original bytes that <see cref="TryDecodeGlyph"/> came from.
        /// </summary>
        public static bool TryEncodeGlyph(string tokenPrefix, int idx, out byte lead, out byte next)
        {
            lead = 0;
            next = 0;
            GlyphBank? bank = FindBankForPrefix(tokenPrefix);
            if (bank == null)
                return false;

            // raw = 208*lead + next, with next in [0x30, 0xFF]. Try each allowed lead.
            int raw = idx - bank.Bias;
            for (int l = bank.LeadLow; l <= bank.LeadHigh; l++)
            {
                int n = raw - GlyphSpan * l;
                if (n >= GlyphNextLow && n <= GlyphNextHigh)
                {
                    lead = (byte)l;
                    next = (byte)n;
                    return true;
                }
            }
            return false;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FFXProjectEditor.Utils.Encoding
{
    // Lossless, reversible FFX text codec.
    //
    // GetString (display decode) DROPS control bytes other than newline/format/charname — so editing a
    // control-bearing string and re-encoding the display text loses those controls. This pair guarantees
    // EncodeLossless(DecodeLossless(bytes)) == bytes for every script in the corpus, by surfacing every
    // control byte as a reversible token:
    //   newline (3)                  <-> "\n"
    //   format  (10 + p) known       <-> named token (</> <W> <B> <FMT52>)
    //   format  (10 + p) unknown     <-> "<C10:p>"
    //   charname(19 + p) known       <-> named token (<TIDUS> .. <MINDY>)
    //   charname(19 + p) unknown     <-> "<C19:p>"
    //   raw control c (1,2,4..9,11..18,20..47) <-> "<Cc>"
    //   plain byte b in decoder      <-> its char
    //   plain byte b not in decoder  <-> "<MISS:b>"
    // It is additive: GetString and the existing encoder are untouched.
    public partial class FfxEncoding
    {
        static readonly Dictionary<string, byte> FormatTokenToByte = BuildReverse(FormatCodes);
        static readonly Dictionary<string, byte> ReadOnlyFormatTokenToByte = BuildReverse(ReadOnlyFormatCodes);
        static readonly Dictionary<string, byte> CharNameTokenToByte = BuildReverse(CharacterNameCodes);

        public static string DecodeScriptLossless(byte[] script, Dictionary<byte, char> decoder)
        {
            ArgumentNullException.ThrowIfNull(script);
            ArgumentNullException.ThrowIfNull(decoder);

            StringBuilder sb = new();
            for (int i = 0; i < script.Length; i++)
            {
                byte b = script[i];

                if (b == C_NEW_LINE)
                {
                    sb.Append('\n');
                    continue;
                }

                if (b == C_FORMAT || b == C_CHAR_NAME)
                {
                    bool hasParam = i + 1 < script.Length;
                    byte p = hasParam ? script[i + 1] : (byte)0;

                    if (b == C_FORMAT && FormatCodes.TryGetValue(p, out string? fmt))
                        sb.Append(fmt);
                    else if (b == C_FORMAT && ReadOnlyFormatCodes.TryGetValue(p, out string? roFmt))
                        sb.Append(roFmt);
                    else if (b == C_CHAR_NAME && CharacterNameCodes.TryGetValue(p, out string? name))
                        sb.Append(name);
                    else
                        sb.Append(hasParam ? $"<C{b}:{p}>" : $"<C{b}>");

                    if (hasParam)
                        i++;
                    continue;
                }

                // 2-byte font-bank glyph (cracked 2026-06-06, FFX_EventText_AdvanceCharGlyph@0x8B92E0):
                // a lead byte (0x06, 0x26-0x2F) + the next byte select one glyph in a font slot. Surface it
                // as ONE reversible token (<FTCX:n>=event kanji, <K:n>=base.ftc kanji, <F2/F3/F5:n>=other
                // banks) instead of <C..>+<MISS>. Requires next>=0x30 (the char-value range) so idx>=0 and
                // the encode reconstructs the exact pair; otherwise fall through to the raw-control path.
                if (i + 1 < script.Length && TryFontBankLead(b, out int fbBank, out byte fbLeadBase) && script[i + 1] >= 0x30)
                {
                    int idx = 208 * (b - fbLeadBase) + (script[i + 1] - 0x30);
                    sb.Append($"<{FontBankPrefix(fbBank)}:{idx}>");
                    i++;
                    continue;
                }

                if (ControlDecoder.ContainsKey(b))
                {
                    // raw control byte (1,2,4..9,11..18,20..47; 0 only if a script were unterminated)
                    sb.Append($"<C{b}>");
                    continue;
                }

                if (decoder.TryGetValue(b, out char ch))
                {
                    // Escape literal angle brackets so they never collide with token delimiters.
                    if (ch == '<') sb.Append("<LT>");
                    else if (ch == '>') sb.Append("<GT>");
                    else sb.Append(ch);
                }
                else
                {
                    sb.Append($"<MISS:{b}>");
                }
            }

            return sb.ToString();
        }

        public static bool TryEncodeScriptLossless(string? text, Dictionary<byte, char> decoder, out byte[] bytes, out string? error)
        {
            ArgumentNullException.ThrowIfNull(decoder);

            error = null;
            List<byte> output = new();
            Dictionary<char, byte> charEncoder = new();
            foreach ((byte key, char value) in decoder)
                charEncoder.TryAdd(value, key);

            string normalized = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');

            for (int i = 0; i < normalized.Length; i++)
            {
                char c = normalized[i];

                if (c == '\n')
                {
                    output.Add(C_NEW_LINE);
                    continue;
                }

                if (c == '<')
                {
                    int end = normalized.IndexOf('>', i + 1);
                    if (end < 0)
                    {
                        error = $"Unterminated token at index {i}.";
                        bytes = Array.Empty<byte>();
                        return false;
                    }

                    string token = normalized.Substring(i, end - i + 1);

                    // Escaped literal angle brackets resolve through the charset like any other char.
                    if (token is "<LT>" or "<GT>")
                    {
                        char literal = token == "<LT>" ? '<' : '>';
                        if (!charEncoder.TryGetValue(literal, out byte literalByte))
                        {
                            error = $"Charset has no byte for literal '{literal}'.";
                            bytes = Array.Empty<byte>();
                            return false;
                        }

                        output.Add(literalByte);
                        i = end;
                        continue;
                    }

                    if (!TryEncodeToken(token, out byte[] tokenBytes, out error))
                    {
                        bytes = Array.Empty<byte>();
                        return false;
                    }

                    output.AddRange(tokenBytes);
                    i = end;
                    continue;
                }

                if (c == '>')
                {
                    error = "Stray '>' without an opening '<'.";
                    bytes = Array.Empty<byte>();
                    return false;
                }

                if (charEncoder.TryGetValue(c, out byte encoded))
                {
                    output.Add(encoded);
                }
                else
                {
                    error = $"Unsupported character '{c}' (U+{(int)c:X4}).";
                    bytes = Array.Empty<byte>();
                    return false;
                }
            }

            bytes = output.ToArray();
            return true;
        }

        static bool TryEncodeToken(string token, out byte[] bytes, out string? error)
        {
            error = null;
            bytes = Array.Empty<byte>();

            // Named tokens first (they never start with the generic "C<digit>" / "MISS:" forms).
            if (FormatTokenToByte.TryGetValue(token, out byte fmtCode)) { bytes = [C_FORMAT, fmtCode]; return true; }
            if (ReadOnlyFormatTokenToByte.TryGetValue(token, out byte roCode)) { bytes = [C_FORMAT, roCode]; return true; }
            if (CharNameTokenToByte.TryGetValue(token, out byte nameCode)) { bytes = [C_CHAR_NAME, nameCode]; return true; }

            string inner = token.Substring(1, token.Length - 2); // strip < >

            // 2-byte font-bank glyph token (reverse of the DecodeScriptLossless lead handling).
            if (TryEncodeFontBankToken(inner, out byte[] fbBytes)) { bytes = fbBytes; return true; }

            if (inner.StartsWith("MISS:", StringComparison.Ordinal))
            {
                if (TryParseByte(inner.AsSpan(5), out byte missByte)) { bytes = [missByte]; return true; }
                error = $"Malformed token {token}.";
                return false;
            }

            if (inner.Length >= 2 && (inner[0] == 'C' || inner[0] == 'c'))
            {
                int colon = inner.IndexOf(':');
                if (colon > 1)
                {
                    if (TryParseByte(inner.AsSpan(1, colon - 1), out byte ctrl) && TryParseByte(inner.AsSpan(colon + 1), out byte param))
                    {
                        bytes = [ctrl, param];
                        return true;
                    }
                }
                else if (TryParseByte(inner.AsSpan(1), out byte ctrlOnly))
                {
                    bytes = [ctrlOnly];
                    return true;
                }
            }

            error = $"Unknown token {token}.";
            return false;
        }

        static bool TryParseByte(ReadOnlySpan<char> span, out byte value)
        {
            value = 0;
            return int.TryParse(span, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                && parsed >= 0
                && parsed <= 255
                && (value = (byte)parsed) == parsed;
        }

        static Dictionary<string, byte> BuildReverse(Dictionary<byte, string> map)
        {
            Dictionary<string, byte> reverse = new(StringComparer.Ordinal);
            foreach ((byte key, string value) in map)
                reverse.TryAdd(value, key);
            return reverse;
        }

        // 2-byte font-bank glyph encoding (cracked 2026-06-06). A lead byte selects a font slot; the
        // glyph index within the slot is 208*(lead-leadBase) + (next-0x30). 208 = the 0x30..0xFF char span.
        //   bank 0 (leads 0x2C-0x2F) = base.ftc (999 common kanji)   -> token <K:idx>
        //   bank 1 (leads 0x2A-0x2B) = event FTCX (this event's kanji) -> token <FTCX:idx>
        //   bank 2 (leads 0x28-0x29) / bank 3 (0x26-0x27) / bank 5 (0x06) = other font banks -> <F2/F3/F5:idx>
        static bool TryFontBankLead(byte lead, out int bank, out byte leadBase)
        {
            switch (lead)
            {
                case 0x2C: case 0x2D: case 0x2E: case 0x2F: bank = 0; leadBase = 0x2C; return true;
                case 0x2A: case 0x2B: bank = 1; leadBase = 0x2A; return true;
                case 0x28: case 0x29: bank = 2; leadBase = 0x28; return true;
                case 0x26: case 0x27: bank = 3; leadBase = 0x26; return true;
                case 0x06: bank = 5; leadBase = 0x06; return true;
                default: bank = -1; leadBase = 0; return false;
            }
        }

        static string FontBankPrefix(int bank) => bank switch
        {
            0 => "K", 1 => "FTCX", 2 => "F2", 3 => "F3", 5 => "F5", _ => "F" + bank,
        };

        // prefix -> (bank, leadBase, number of lead bytes in the bank). leadCount bounds idx for reversibility.
        static readonly Dictionary<string, (byte leadBase, int leadCount)> FontBankPrefixes = new(StringComparer.Ordinal)
        {
            ["K"] = (0x2C, 4), ["FTCX"] = (0x2A, 2), ["F2"] = (0x28, 2), ["F3"] = (0x26, 2), ["F5"] = (0x06, 1),
        };

        static bool TryEncodeFontBankToken(string inner, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            int colon = inner.IndexOf(':');
            if (colon <= 0)
                return false;
            string prefix = inner.Substring(0, colon);
            if (!FontBankPrefixes.TryGetValue(prefix, out (byte leadBase, int leadCount) info))
                return false;
            if (!int.TryParse(inner.AsSpan(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx))
                return false;
            if (idx < 0 || idx >= 208 * info.leadCount)
                return false;
            byte lead = (byte)(info.leadBase + idx / 208);
            byte next = (byte)(0x30 + idx % 208);
            bytes = new[] { lead, next };
            return true;
        }
    }
}

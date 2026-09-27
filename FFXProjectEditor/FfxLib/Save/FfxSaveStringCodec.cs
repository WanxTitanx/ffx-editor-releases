// ============================================================================
// FfxSaveStringCodec — FFX save-game string codec (FFXED char table)
// PURPOSE : maps save bytes <-> in-game text via a 256-entry decode table (digits, punctuation, A-Z, then a
//           breadth of Latin/accent/music/symbol code points) with '?' fallback.
// WHY     : save strings are NOT ASCII/UTF8 — they use the game's proprietary single-byte char table; the
//           editor must decode for display and re-encode on write (evenly symmetric via DecodeByte/EncodeChar).
// EVIDENCE: FFXED v0.749 char table.
// MAINT   : EncodeChar is linear scan (O(256)) and returns '?' (79) for unmapped chars — keep DecodeTable
//           and EncodeChar symmetric; adding a glyph touches both directions.
// ============================================================================
namespace FFXProjectEditor.FfxLib.Save
{
    /// <summary>
    /// FFX save-game string codec (FFXED char table).
    /// </summary>
    internal static class FfxSaveStringCodec
    {
        static readonly char[] DecodeTable = BuildDecodeTable();

        static char[] BuildDecodeTable()
        {
            var table = new char[256];
            for (int i = 0; i < table.Length; i++)
                table[i] = '?';

            char digit = '0';
            for (int i = 48; i <= 57; i++)
                table[i] = digit++;

            table[58] = ' ';
            table[59] = '!';
            table[60] = '"';
            table[61] = '#';
            table[62] = '$';
            table[63] = '%';
            table[64] = '&';
            table[65] = '\'';
            table[66] = '(';
            table[67] = ')';
            table[68] = '*';
            table[69] = '+';
            table[70] = ',';
            table[71] = '-';
            table[72] = '.';
            table[73] = '/';
            table[74] = ':';
            table[75] = ';';
            table[76] = '<';
            table[77] = '=';
            table[78] = '>';
            table[79] = '?';

            char upper = 'A';
            for (int i = 80; i <= 141; i++)
                table[i] = upper++;

            table[142] = (char)183;
            table[143] = (char)12304;
            table[144] = (char)12305;
            table[145] = (char)9834;
            table[146] = (char)9829;
            table[148] = (char)8220;
            table[149] = (char)8221;
            table[150] = '-';
            table[152] = (char)161;
            table[153] = (char)8593;
            table[154] = (char)8595;
            table[155] = (char)8592;
            table[156] = (char)8594;
            table[157] = (char)168;
            table[158] = (char)171;
            table[159] = (char)176;
            table[161] = (char)187;
            table[162] = (char)191;
            table[163] = (char)192;
            table[164] = (char)193;
            table[165] = (char)194;
            table[166] = (char)196;
            table[167] = (char)199;
            table[168] = (char)200;
            table[169] = (char)201;
            table[170] = (char)202;
            table[171] = (char)203;
            table[172] = (char)204;
            table[173] = (char)205;
            table[174] = (char)206;
            table[175] = (char)207;
            table[176] = (char)209;
            table[177] = (char)210;
            table[178] = (char)211;
            table[179] = (char)212;
            table[180] = (char)214;
            table[181] = (char)217;
            table[182] = (char)218;
            table[183] = (char)219;
            table[184] = (char)220;
            table[185] = (char)223;
            table[186] = (char)224;
            table[187] = (char)225;
            table[188] = (char)226;
            table[189] = (char)228;
            table[190] = (char)231;
            table[191] = (char)232;
            table[192] = (char)233;
            table[193] = (char)234;
            table[194] = (char)235;
            table[195] = (char)236;
            table[196] = (char)237;
            table[197] = (char)238;
            table[198] = (char)239;
            table[199] = (char)241;
            table[200] = (char)242;
            table[201] = (char)243;
            table[202] = (char)244;
            table[203] = (char)246;
            table[204] = (char)249;
            table[205] = (char)250;
            table[206] = (char)251;
            table[207] = (char)252;
            table[208] = ',';
            table[209] = (char)402;
            table[210] = (char)8222;
            table[211] = (char)8230;
            table[212] = (char)8216;
            table[213] = (char)8217;
            table[214] = (char)8226;
            table[215] = (char)8208;
            table[216] = (char)732;
            table[217] = (char)8482;
            table[219] = (char)8250;
            table[220] = (char)167;
            table[221] = (char)169;
            table[222] = (char)170;
            table[223] = (char)174;
            table[224] = (char)177;
            table[225] = (char)178;
            table[226] = (char)179;
            table[227] = (char)188;
            table[228] = (char)189;
            table[229] = (char)190;
            table[230] = (char)215;
            table[231] = (char)247;
            table[232] = (char)8249;
            table[233] = (char)8943;
            return table;
        }

        public static char DecodeByte(byte b) => DecodeTable[b];

        public static byte EncodeChar(char c)
        {
            for (int i = 48; i < DecodeTable.Length; i++)
            {
                if (DecodeTable[i] == c)
                    return (byte)i;
            }

            return 79; // '?'
        }
    }
}

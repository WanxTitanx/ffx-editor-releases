using System.Collections.Generic;

namespace FFXProjectEditor.Utils.Encoding
{
    public partial class FfxEncoding
    {
        public const byte C_NULL = 0;
        public const byte C_NEW_LINE = 3;
        public const byte C_FORMAT = 10;
        public const byte C_CHAR_NAME = 19;

        public static readonly Dictionary<byte, string> ControlDecoder = new Dictionary<byte, string>
        {
            // Control codes
            { 0, "<NULL>" }, // NULL
            { 1, "<C1>" },
            { 2, "<C2>" },
            { 3, "<C3>" }, // Environment.NewLine
            { 4, "<C4>" },
            { 5, "<C5>" },
            // 0x06 is NOT a control code: it is a 2-byte glyph lead (font slot 5). Decoded via GlyphLeads.
            { 7, "<C7>" },
            { 8, "<C8>" },
            { 9, "<C9>" },
            { 10, "<C10>" }, // Formatting control code
            { 11, "<C11>" },
            { 12, "<C12>" },
            { 13, "<C13>" },
            { 14, "<C14>" },
            { 15, "<C15>" },
            { 16, "<C16>" },
            { 17, "<C17>" },
            { 18, "<C18>" }, // Input?
            { 19, "<C19>" }, // Character name control code
            { 20, "<C20>" },
            { 21, "<C21>" },
            { 22, "<C22>" }, // Item name 1 ?
            { 23, "<C23>" }, // Item name 2 ?
            { 24, "<C24>" },
            { 25, "<C25>" },
            { 26, "<C26>" },
            { 27, "<C27>" },
            { 28, "<C28>" },
            { 29, "<C29>" },
            { 30, "<C30>" },
            { 31, "<C31>" },
            { 32, "<C32>" },
            { 33, "<C33>" },
            { 34, "<C34>" },
            { 35, "<C35>" }, // Key Item ?
            { 36, "<C36>" },
            { 37, "<C37>" },
            // 0x26..0x2F are NOT control codes: they are 2-byte glyph leads (font banks 3/2/1/0).
            // Decoded via GlyphLeads. See docs/reverse/FFX_EVENT_TEXT_ENCODING_CRACKED_2026-06-06.md.
        };

        // Codes for 10 (0x0A)
        public static readonly Dictionary<byte, string> FormatCodes = new Dictionary<byte, string>
        {
            { 65, "</>" }, // Back to normal format
            { 67, "<W>" }, // Warning yellow text (start and end)
            { 177, "<B>" }, // Bold (white)
        };

        // Decode-only format codes proven in the corpus but not yet accepted for writing.
        public static readonly Dictionary<byte, string> ReadOnlyFormatCodes = new Dictionary<byte, string>
        {
            { 82, "<FMT52>" }, // Proven in field-string help text. Context suggests gray/inactive formatting, but write semantics stay locked.
        };

        // Codes for 19 (0x13)
        public static readonly Dictionary<byte, string> CharacterNameCodes = new Dictionary<byte, string>
        {
            { 48, "<TIDUS>" },
            { 49, "<YUNA>" },
            { 50, "<AURON>" },
            { 51, "<KIMAHRI>" },
            { 52, "<WAKKA>" },
            { 53, "<LULU>" },
            { 54, "<RIKKU>" },
            { 55, "<SEYMOUR>" },
            { 56, "<VALEFOR>" },
            { 57, "<IFRIT>" },
            { 58, "<IXION>" },
            { 59, "<SHIVA>" },
            { 60, "<BAHAMUT>" },
            { 61, "<ANIMA>" },
            { 62, "<YOJIMBO>" },
            { 63, "<CINDY>" },
            { 64, "<SANDY>" },
            { 65, "<MINDY>" },
        };
    }
}

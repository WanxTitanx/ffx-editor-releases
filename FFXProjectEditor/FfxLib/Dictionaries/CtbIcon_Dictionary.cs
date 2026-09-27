using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.Dictionaries
{
    /// <summary>CTB icon type ids from the FFXmon enemy editor (Icon type column, A4 block).</summary>
    public static class CtbIcon_Dictionary
    {
        public static IReadOnlyDictionary<byte, string> Instance { get; } = new Dictionary<byte, string>
        {
            { 0, "Tidus" },
            { 1, "Yuna" },
            { 2, "Auron" },
            { 3, "Kimahri" },
            { 4, "Wakka" },
            { 5, "Lulu" },
            { 6, "Rikku" },
            { 7, "Seymour (playable)" },
            { 8, "Valefor" },
            { 9, "Ifrit" },
            { 10, "Ixion" },
            { 11, "Shiva" },
            { 12, "Bahamut" },
            { 13, "Anima" },
            { 14, "Yojimbo" },
            { 15, "Cindy" },
            { 16, "Sandy" },
            { 17, "Mindy" },
            { 20, "Monster" },
            { 21, "Boss" },
            { 22, "Sub-boss" },
            { 23, "Cid" },
        };

        public static string ResolveName(byte iconId)
        {
            return Instance.TryGetValue(iconId, out string? name) ? name : $"Unknown ({iconId})";
        }
    }
}

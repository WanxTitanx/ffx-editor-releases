using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FFXProjectEditor.FfxLib.Ps3
{
    /// <summary>Parses moveAnim fields from AiCommandMetadata / kernel raw property text.</summary>
    internal static class MagicDllMoveAnimParser
    {
        static readonly Regex MagicIdRegex = new(
            @"magic_(\d{4})",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static bool TryParseMoveAnim(string raw, out int anim1, out int anim2)
        {
            anim1 = anim2 = 0;
            if (!TryExtractMoveAnimField(raw, out string field))
                return false;

            List<int> ids = ExtractMagicIds(field);
            if (ids.Count == 0)
                return false;

            anim1 = ids[0];
            anim2 = ids.Count > 1 ? ids[1] : ids[0];
            return true;
        }

        public static IReadOnlyList<int> EnumerateMagicIds(string raw)
        {
            if (!TryExtractMoveAnimField(raw, out string field))
                return [];
            return ExtractMagicIds(field);
        }

        static bool TryExtractMoveAnimField(string raw, out string field)
        {
            field = string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            int idx = raw.IndexOf("moveAnim=", StringComparison.Ordinal);
            if (idx < 0)
                return false;

            field = raw[(idx + "moveAnim=".Length)..];
            int tab = field.IndexOf('\t');
            if (tab >= 0)
                field = field[..tab];
            return true;
        }

        static List<int> ExtractMagicIds(string field)
        {
            List<int> ids = [];
            foreach (Match match in MagicIdRegex.Matches(field))
            {
                int id = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                if (!ids.Contains(id))
                    ids.Add(id);
            }

            return ids;
        }
    }
}

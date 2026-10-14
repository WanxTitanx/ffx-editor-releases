using System.Linq;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.FfxLib.Mods;

internal static class Mod006Readiness
{
    // A codec lookup alone cannot prove renderer coverage or the Hook locale/save lifecycle.
    public static bool CanExportPack => false;
    public static string MissingUsCharacters(string text) => new(text.Where(c => !char.IsControl(c)
        && !FfxEncoding.UsDecoder.ContainsValue(c)).Distinct().OrderBy(c => c).ToArray());
}

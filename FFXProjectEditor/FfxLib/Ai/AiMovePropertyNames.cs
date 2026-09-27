using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.Ai
{
    // Names for ability/move fields used by readMoveProperty (0x701A) and related accessors.
    // Cross-referenced from Karifean/FFXDataParser ScriptConstants.putMoveProperty.
    public static class AiMovePropertyNames
    {
        public static readonly IReadOnlyDictionary<ushort, string> Names = new Dictionary<ushort, string>
        {
            [0x0000] = "damageFormula",
            [0x0001] = "damageType",
            [0x0002] = "affectHP",
            [0x0003] = "affectMP",
            [0x0004] = "affectCTB",
            [0x0005] = "elementHoly",
            [0x0006] = "elementWater",
            [0x0007] = "elementThunder",
            [0x0008] = "elementIce",
            [0x0009] = "elementFire",
            [0x000A] = "targetType",
        };

        public static string? Get(ushort fieldId) => Names.TryGetValue(fieldId, out string? n) ? n : null;
    }
}

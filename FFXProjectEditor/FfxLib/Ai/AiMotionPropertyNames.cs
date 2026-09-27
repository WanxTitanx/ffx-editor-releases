using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.Ai
{
    // Names for getMotionField (0x70AC) and setMotionField (0x70B2).
    // Cross-referenced from Karifean/FFXDataParser ScriptConstants.putMotionProperty.
    public static class AiMotionPropertyNames
    {
        public static readonly IReadOnlyDictionary<ushort, string> Names = new Dictionary<ushort, string>
        {
            [0x0000] = "motion_attack_start_dist",
            [0x0001] = "motion_attack_offset",
            [0x0002] = "motion_move_backjump_dist",
            [0x0003] = "motion_run_speed",
            [0x0004] = "motion_run_speed_return",
            [0x0005] = "motion_run_speed_v0",
            [0x0006] = "motion_run_speed_acc",
            [0x0007] = "motion_weight",
            [0x0008] = "motion_attack_height",
            [0x0009] = "motion_width",
        };

        public static string? Get(ushort fieldId) => Names.TryGetValue(fieldId, out string? n) ? n : null;
    }
}

using System.Collections.Generic;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MagicDllEditor
{
    /// <summary>
    /// Catalog of HUMAN descriptions for PPP opcodes (what each handler does, in player
    /// language). Based on IDA decompilation (FFX_recon.i64) + RE docs (PPP_C2_*).
    /// Used in the editor panel when a slot is selected.
    /// English only (monolingual — outside i18n, same as DebugLog).
    /// </summary>
    internal static class MagicPppDescriptionCatalog
    {
        public static string Get(string opcode)
        {
            if (opcode != null && _descriptions.TryGetValue(opcode, out string? d))
                return d;
            return "Effect handler (PPP). The exact behavior depends on the family. Open the fields to see the editable parameters.";
        }

        private static readonly Dictionary<string, string> _descriptions = new()
        {
            // --- Movement / scale / transform (Family U1 / BoneAnim) ---
            ["pppMove"] = "Moves the target along X/Y/Z/W each frame. The values are deltas added to the bone's position.",
            ["pppSclMove"] = "Moves the target's scale along X/Y/Z/W each frame. Deltas added to the bone's scale.",
            ["pppSclAccele"] = Strings.F2_accelerates_the_target_s_scale_on_x_y_z_dca8b201,
            ["pppAccele"] = "Accelerates the target on X/Y/Z/W each frame (double-layer motion).",
            ["pppAngMove"] = "Rotates the target on X/Y/Z (angles) each frame. Deltas added to the bone's rotation.",
            ["pppAngAccele"] = "Accelerates the target's rotation on X/Y/Z (angles) each frame.",
            ["pppAngle"] = "Sets the target's rotation angle on X/Y/Z/W each frame.",
            ["pppScale"] = "Scales the target on X/Y/Z/W each frame. Sets the scale magnitude.",
            ["pppPoint"] = "Sets the target's position (point) on X/Y/Z/W each frame.",
            ["pppColMove"] = Strings.F2_shifts_the_target_s_color_on_r_g_b_a_eac_859d8a28,
            ["pppColAccele"] = "Accelerates the target's color change on R/G/B/A each frame.",
            ["pppColor"] = "Sets the target's color on R/G/B/A each frame.",

            // --- Matrices / transform host ---
            ["pppMatrixXYZ"] = "Builds the Euler rotation matrix (X->Y->Z order) from the bone transform state and applies it to the model.",
            ["pppMatrixXZY"] = "Builds the Euler rotation matrix (X->Z->Y order) from the bone transform state.",
            ["pppMatrixYXZ"] = "Builds the Euler rotation matrix (Y->X->Z order).",
            ["pppMatrixYZX"] = "Builds the Euler rotation matrix (Y->Z->X order).",
            ["pppMatrixZXY"] = "Builds the Euler rotation matrix (Z->X->Y order).",
            ["pppMatrixZYX"] = "Builds the Euler rotation matrix (Z->Y->X order).",
            ["pppMatrixScl"] = "Builds a scale matrix applied to the target.",
            ["pppMatrixLoc"] = "Builds a local-position matrix for the target (host transform).",
            ["pppDrawMatrix"] = "Projects the model's world coordinates to screen space (2D projection).",
            ["pppDrawMatrixFront"] = "Renders the model with a front transform matrix (billboard / facing camera).",
            ["pppDrawMatrixWood"] = "Applies a wood-like matrix transform to the model (custom orientation).",
            ["pppDrawShape"] = "Draws a shape (mesh/geometry) at the target's position.",
            ["pppDrawShapeRev"] = "Draws a shape with reversed winding (back face).",
            ["pppDrawMdl"] = "Draws the model at the target's transform.",
            ["pppDrawMdlLoop"] = "Draws the model in a loop (repeated frames).",
            ["pppDrawSprite"] = "Draws a sprite (billboard) at the target's position.",
            // --- Vertex / apply ---
            ["pppVertexAp"] = "Applies vertex data (mesh vertices) to the target.",
            ["pppVertexApAt"] = "Applies vertex data to the target with timing (model index, vertex count, delay, order mode).",
            ["pppVertexApLc"] = "Applies vertex data to the target (local variant).",
            ["pppVertexApDisPos"] = "Applies vertex data with capture batch (displacement/position).",
            ["pppVertexApWorld"] = "Applies vertex data in world space.",

            // --- Random ---
            ["pppRandIV"] = "Generates a random integer value in a range and applies it to the target.",
            ["pppRandFV"] = "Generates a random float value in a range and applies it to the target.",
            ["pppRandCV"] = "Generates a random color value (RGBA) in a range.",
            ["pppSRandFV"] = "Generates a random float with a small/noise range.",
            ["pppRandUpIV"] = "Generates a random integer toward the upper bound.",
            ["pppRandDownIV"] = "Generates a random integer toward the lower bound.",
            ["pppRandChar"] = "Generates a random small value (byte) and applies it.",
            ["pppRandInt"] = "Generates a random integer and applies it.",
            ["pppRandShort"] = "Generates a random short value and applies it.",

            // --- KeTh (resource chain / texture) ---
            ["pppKeTh"] = "Keys a texture handler (resource chain) - associates a texture with the target.",
            ["pppKeThSft"] = "Shifts/offsets the texture handler (UV offset) over time.",
            ["pppKeThRes32"] = "Allocates a 32-byte resource chain for the target (texture resource).",
            ["pppKeThRes48"] = "Allocates a 48-byte resource chain for the target (texture resource).",
            ["pppKeShpTail"] = "Shapes the tail effect (particle trail) of the target.",
            ["pppKeShpTail2"] = "Shapes the tail effect (variant 2) - pushes the transform node.",
            ["pppKeShpDtt"] = "Shapes the tail with a dot/sparkle detail.",
            ["pppKeDMat"] = "Keys a death-material (fade-out) for the target.",
            ["pppKeDMatFr"] = "Keys a death-material with a front-face variant.",
            ["pppKeHmgEff"] = "Keys a homing effect (portrait/HUD) - targets and follows.",
            ["pppKeOfsPt"] = "Offsets a point (position) of the target.",
            ["pppKeTkFade"] = "Fades out the target (transparency over time).",
            ["pppKeLnsLpSft"] = "Lens effect with a soft shift (glow).",
            ["pppKeBornRnd4"] = "Randomly spawns children (birth) of the target.",
            ["pppKeHitBorn"] = "Spawns children on hit (impact).",
            ["pppKeMdlTfd"] = "Accumulates a model transform offset (Euler table) over time.",
            ["pppKeMdlTfdUv"] = "Accumulates a model transform with UV offset over time.",
            ["pppKeMdlBmp"] = "Keys a bitmap on the model (texture swap).",

            // --- Others ---
            ["pppDummyFunc"] = "No-op handler (does nothing) - placeholder.",
            ["pppDrawFilter"] = "Applies a filter (blur/glow) to the model's projection.",
            ["pppDrawHook"] = "Hooks a custom draw callback on the target.",
            ["pppKeAcmSolid"] = "Keys an accumulate solid (opacity) for the target.",
            ["pppKeMvYpEff"] = "Keys a Y-axis movement effect (vertical bob/float).",
            ["pppKeLnsLpSft"] = "Lens effect with a soft shift (glow). The two values shift the lens/bloom offset on X and Y.",
            ["pppKeAcmCic"] = "Keys an accumulate cycle: mode 2 enables the cycle, other modes route to a different routine.",
            ["pppKeShpTail"] = "Shapes the tail effect (particle trail) of the target - pushes the transform node.",
            ["pppKeShpTail2"] = "Shapes the tail effect (variant 2) - pushes the transform node.",
            ["pppKeShpTail2X"] = "Shapes the tail effect (variant 2X).",
            ["pppKeShpTailX"] = "Shapes the tail effect (variant X).",
            ["pppKeShpDtt"] = "Shapes the tail with a dot/sparkle detail.",
            ["pppKeDMat"] = "Keys a death-material (fade-out) for the target.",
            ["pppKeDMatFr"] = "Keys a death-material with a front-face variant.",
            ["pppKeDMatPhtFr"] = "Keys a death-material with a photo (snapshot) front-face variant.",
            ["pppKeMatPht"] = "Keys a material with a photo (snapshot) effect.",
            ["pppKeOfsMatXYZ"] = "Offsets the material's XYZ transform.",
            ["pppKeParMatR"] = "Keys a parent material with rotation.",
            ["pppDrawMatrixWood"] = "Applies a wood-like matrix transform (custom orientation).",
            ["pppDrawMatrixNoRot"] = "Draws a matrix transform without rotation (no rotation applied).",
            ["pppNeiChrPointLight"] = "Sets a point light on a character (index, offset, and a 4-vector light position).",
            ["pppNeiDrawMdlPointLight"] = "Sets a point light that follows a drawn model (complex draw record).",
            ["pppNeiDrawMdlTsPointLight"] = "Sets a point light that follows a drawn model with texture/shift (complex).",
            ["pppSRandUpHCV"] = "Random color/vector value toward the upper bound (4 components).",
        };
    }
}


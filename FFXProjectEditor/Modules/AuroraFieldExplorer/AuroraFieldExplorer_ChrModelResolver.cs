namespace FFXProjectEditor.Modules.AuroraFieldExplorer
{
    /// <summary>
    /// Maps Field Scout CHR names to baked HD glTF paths (phyre_chr_gate / modelviewer catalog).
    /// Generated models are served only from the per-user LocalAppData viewer-data root.
    /// </summary>
    internal static class AuroraFieldExplorer_ChrModelResolver
    {
        public static string? TryResolveModelUrl(string? chrName)
        {
            if (string.IsNullOrWhiteSpace(chrName) || chrName.Length < 4 || !char.IsLetter(chrName[0]))
                return null;

            string? folder = char.ToLowerInvariant(chrName[0]) switch
            {
                'n' => "npc_anim",
                'c' => "pc_anim",
                'f' => "obj_anim",
                'm' => "phyre_chr_anim",
                's' => "summon_anim",
                'w' => "wep_anim",
                _ => null,
            };

            if (folder == null)
                return null;

            string id = chrName.ToLowerInvariant();
            return $"/viewer-data/model/{folder}/models/{id}/{id}_static.gltf";
        }
    }
}

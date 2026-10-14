using FFXProjectEditor.Modules.Extras;
using System.Collections.Generic;
using System.IO;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Services.Extras
{
    internal static class ExtrasSourceResolver_Service
    {
        public static IReadOnlyList<ExtrasSourceRootDescriptor> BuildSourceRoots()
        {
            Project_Service project = Project_Service.Instance;

            string? masterRoot = project.ProjectPath;
            string? ffxPs2Root = project.Path_FfxPs2Root;
            string? ps3DataRoot = project.Path_Ps3DataRoot;

            return
            [
                BuildDescriptor(
                    "Master Workspace",
                    "Primary editable workspace used by the current shell.",
                    masterRoot),
                BuildDescriptor(
                    "FFX PS2 Tree",
                    "Read-only campaign root for Pt52/Pt54/Pt56/Pt58/Pt67 research surfaces.",
                    ffxPs2Root),
                BuildDescriptor(
                    "PS3Data Tree",
                    "Companion read-only tree for modern carrier, texture, and Phyre-side comparisons.",
                    ps3DataRoot)
            ];
        }

        static ExtrasSourceRootDescriptor BuildDescriptor(string title, string role, string? path)
        {
            bool exists = !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
            string statusSummary = exists
                ? "Detected and ready for read-only product surfaces."
                : Strings.F2_not_detected_from_the_current_master_roo_5b8dc448;

            return new ExtrasSourceRootDescriptor(title, role, path, exists, statusSummary);
        }
    }
}

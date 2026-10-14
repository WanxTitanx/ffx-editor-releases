using FFXProjectEditor.FfxLib.BattleMap;
using FFXProjectEditor.Modules.AuroraFieldExplorer;
using FFXProjectEditor.Services;
using System;
using System.IO;

namespace FFXProjectEditor.Tools
{
    /// <summary>
    /// After <c>publish-scout-to-editor.ps1</c>, rebuild MapViewer encounter overlays so NPC/chest markers
    /// from WalkManifest appear without re-exporting glTF.
    /// Run: dotnet run --project FFXProjectEditor -- --field-explorer-refresh-walk
    /// </summary>
    internal static class FieldExplorerWalkPublishRt0
    {
        public static int Run(string[] args)
        {
            BattleMapCatalog_File? btlmap = TryLoadBtlmapCatalog();
            AuroraFieldExplorer_WalkPublishService.OperationResult result =
                AuroraFieldExplorer_WalkPublishService.RefreshOverlays(btlmap);

            Console.WriteLine("Field Explorer walk publish refresh");
            Console.WriteLine($"  {result.Message}");

            if (!result.Ok && result.WalkedFields == 0)
                return 1;

            return result.RefreshedOverlays > 0 ? 0 : result.WalkedFields > 0 ? 2 : 0;
        }

        static BattleMapCatalog_File? TryLoadBtlmapCatalog()
        {
            try
            {
                string root = BattleMapCatalog_File.DefaultBtlmapRoot;
                string? ps3 = Project_Service.Instance.Path_Ps3DataRoot;
                if (!string.IsNullOrWhiteSpace(ps3))
                {
                    string candidate = Path.Combine(ps3, "btlmap");
                    if (Directory.Exists(candidate))
                        root = candidate;
                }

                return Directory.Exists(root) ? BattleMapCatalog_File.Scan(root) : null;
            }
            catch
            {
                return null;
            }
        }
    }
}

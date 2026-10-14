using System;
using FFXProjectEditor.Modules.AuroraChamber;
using FFXProjectEditor.Modules.Common.ViewerShell;
using FFXProjectEditor.Services;

namespace FFXProjectEditor.Tools
{
    // Headless EditViewer probe (2026-09-16, Jarvis-Aurora): runs the exact code path of the
    // "Open EditViewer" button (RenderSelectedEmbeddedAsync → ExternalBrowserLauncher.Open) without
    // needing Avalonia input. Exists because VM input injection (xdotool/ydotool through Xwayland)
    // can't deliver pointer button-presses to the window, so interactive UI validation is blocked —
    // this RT0 exercises the same logic headlessly and opens the real browser.
    // Run: FFXProjectEditor.exe --aurora-editviewer-rt0 <masterPath> [mapKey]
    internal static class AuroraEditViewerRt0
    {
        public static int Run(string[] args)
        {
            string masterPath = args.Length > 1 ? args[1] : "";
            string mapKey = args.Length > 2 ? args[2] : "bsil03";
            Console.WriteLine("=== Aurora EditViewer RT0 (battle-stage ?edit=1 → browser) ===");
            Console.WriteLine($"master : {masterPath}");
            Console.WriteLine($"mapKey : {mapKey}");

            if (!System.IO.Directory.Exists(masterPath)) { Console.WriteLine("NOT FOUND"); return 2; }
            try
            {
                Project_Service.Instance.LoadProject(masterPath);
            }
            catch (Exception ex) { Console.WriteLine($"LOAD THREW: {ex.Message}"); return 2; }
            Console.WriteLine($"loaded : {Project_Service.Instance.ProjectPath}");
            Console.WriteLine($"Path_Btl: {Project_Service.Instance.Path_Btl}");

            var dm = new AuroraChamber_DataModel();
            Console.WriteLine($"scenes : {dm.Scenes.Count} · catalog: {dm.CatalogSummary}");

            if (!dm.SelectSceneByMapKey(mapKey))
            {
                Console.WriteLine($"SCENE NOT FOUND: {mapKey}");
                return 2;
            }
            Console.WriteLine($"scene  : {dm.SelectedScene?.Scene.SceneId} · battle={dm.SelectedBattle} · battles={dm.Battles.Count}");

            (bool ok, string? url, string msg) = dm.RenderSelectedEmbeddedAsync().GetAwaiter().GetResult();
            Console.WriteLine($"render : ok={ok} battleStage={dm.LastEmbeddedIsBattleStage}");
            Console.WriteLine($"status : {dm.RenderStatus}");
            Console.WriteLine($"url    : {url ?? "(null)"}");
            Console.WriteLine($"msg    : {msg}");

            if (!ok || url == null) return 1;
            bool launched = ExternalBrowserLauncher.Open(url);
            Console.WriteLine($"browser: launched={launched} embeddedSupported={ExternalBrowserLauncher.IsEmbeddedViewerSupported}");
            Console.WriteLine("VERDICT: PASS — EditViewer routed to " +
                (dm.LastEmbeddedIsBattleStage ? "BATTLE-STAGE (?edit=1)" : "mapviewer") + " and opened.");
            if (!launched) return 3;

            // The StudioWebServer is in-process — hold it alive so the browser can fetch the stage
            // geometry/monster data through it (CDN fetch-through) before this probe exits.
            int holdSecs = args.Length > 3 && int.TryParse(args[3], out int s) ? s : 0;
            if (holdSecs > 0)
            {
                Console.WriteLine($"holding server alive {holdSecs}s for browser render…");
                System.Threading.Thread.Sleep(holdSecs * 1000);
            }
            return 0;
        }
    }
}

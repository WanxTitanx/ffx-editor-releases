using System;
using System.IO;
using System.Collections.Generic;

namespace FFXProjectEditor.Tools
{
    // Runs all the Wave-1 writer-completeness RT0 gates in one shot and prints a summary table.
    // Run: FFXProjectEditor.exe --wave1-rt0
    internal static class Wave1Rt0
    {
        public static int Run()
        {
            string K = Path.Combine(FFXProjectEditor.OwnerEnvironmentPaths.ExtractedRoot, @"ffx_ps2\ffx\master\");
            var fams = new (string name, string path, Func<string, int> run)[]
            {
                ("WeaponNameTable",                K + @"new_uspc\battle\kernel\w_name.bin",   WeaponNameTableRt0.Run),
                ("MacroDictionary",                K + @"new_uspc\menu\macrodic.dcp",          MacroDictionaryRt0.Run),
                ("NameDescriptionTextTable",       K + @"new_uspc\battle\kernel\item_txt.bin",  NameDescriptionTextTableRt0.Run),
                ("NameDescriptionTextPrefixTable", K + @"inpc\battle\kernel\a_ability.bin",      NameDescriptionTextPrefixTableRt0.Run),
                ("BtlTextTable",                   K + @"new_uspc\battle\kernel\btl_txt.bin",    BtlTextTableRt0.Run),
                ("SphereGrid",                     K + @"jppc\battle\kernel\sphere.bin",         SphereGridRt0.Run),
                ("ShopGearCatalog",                K + @"jppc\battle\kernel\shop_arms.bin",      ShopGearCatalogRt0.Run),
                ("BukiGetTreasureCatalog",         K + @"jppc\battle\kernel\buki_get.bin",       BukiGetTreasureCatalogRt0.Run),
                ("AlBhedDictionary",               K + @"new_uspc\menu\albheddic.bin",          AlBhedDictionaryRt0.Run),
                ("PointerScriptTable",             K + @"jppc\menu\battle_script.bin",           PointerScriptTableRt0.Run),
                ("BattleTextTable",                K + @"new_uspc\battle\kernel\btl_txt.bin",    BattleTextTableRt0.Run),
                ("ShopTable",                      K + @"jppc\battle\kernel\item_shop.bin",      ShopTableRt0.Run),
            };

            var results = new List<(string name, string verdict)>();
            int pass = 0;
            foreach (var f in fams)
            {
                Console.WriteLine($"\n----- {f.name} -----");
                int rc;
                try { rc = f.run(f.path); }
                catch (Exception ex) { Console.WriteLine($"THREW: {ex.Message}"); rc = 2; }
                string v = rc == 0 ? "PASS" : rc == 1 ? "DRIFT" : "ERROR";
                results.Add((f.name, v));
                if (rc == 0) pass++;
            }

            Console.WriteLine("\n========== WAVE 1 WRITER-COMPLETENESS SUMMARY ==========");
            foreach (var (name, verdict) in results)
                Console.WriteLine($"  {verdict,-6} {name}");
            Console.WriteLine($"  ----------------------------------------------------");
            Console.WriteLine($"  PASS {pass}/{fams.Length}");
            return pass == fams.Length ? 0 : 1;
        }
    }
}

using FFXProjectEditor.FfxLib.Monster;
using System;
using System.IO;

namespace FFXProjectEditor.Tools
{
    internal static class MonsterOdAssignRt0
    {
        // Macalania OD assignments: monster_id -> ForcedAction (operand)
        static readonly (string file, ushort forcedAction, string skillName)[] Assignments = new[]
        {
            ("_m019/m019.bin", (ushort)0x6106, "Permafrost"),       // Ice Flan
            ("_m087/m087.bin", (ushort)0x6107, "Mana Storm Fire"),  // Chimera
            ("_m003/m003.bin", (ushort)0x610A, "Frost Claw"),       // Murussu
        };

        public static int Run(string root)
        {
            if (!Directory.Exists(root))
            {
                Console.Error.WriteLine($"Monster root not found: {root}");
                return 2;
            }

            int ok = 0, fail = 0;
            foreach (var (relPath, forcedAction, skillName) in Assignments)
            {
                string path = Path.Combine(root, relPath);
                if (!File.Exists(path))
                {
                    Console.WriteLine($"  SKIP  {relPath} — file not found");
                    fail++;
                    continue;
                }

                byte[] orig = File.ReadAllBytes(path);
                var monster = Monster_File.Read(orig);
                ushort old = monster.StatSheetFile.ForcedAction;
                monster.StatSheetFile.ForcedAction = forcedAction;
                byte[] patched = monster.Write();

                File.WriteAllBytes(path, patched);
                Console.WriteLine($"  OK    {relPath}  ForcedAction: 0x{old:X4} → 0x{forcedAction:X4} ({skillName})");
                ok++;
            }

            Console.WriteLine($"\nDone: {ok} assigned, {fail} failed.");
            return fail > 0 ? 1 : 0;
        }
    }
}

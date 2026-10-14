using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.FfxLib.WeaponNames;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.Tools
{
    // Headless extractor: dumps FFX name tables (item / weapon / ability / command /
    // monster-magic / monster names) from every language folder to JSON.
    // Run: FFXProjectEditor.exe --name-catalog-extract <masterRoot> <out.json>
    internal static class NameCatalogExtractorRt0
    {
        static readonly (string Dir, string Iso, Dictionary<byte, char> Decoder)[] Languages =
        {
            ("new_uspc", "en", FfxEncoding.UsDecoder),
            ("inpc",     "en", FfxEncoding.UsDecoder),
            ("new_sppc", "es", FfxEncoding.UsDecoder),
            ("new_frpc", "fr", FfxEncoding.UsDecoder),
            ("new_depc", "de", FfxEncoding.UsDecoder),
            ("new_itpc", "it", FfxEncoding.UsDecoder),
            ("jppc",     "ja", FfxEncoding.JpDecoder),
            ("new_jppc", "ja", FfxEncoding.JpDecoder),
            ("new_krpc", "ko", FfxEncoding.JpDecoder),
            ("new_chpc", "zh", FfxEncoding.JpDecoder),
        };

        static readonly (string File, string Category)[] NameDescTables =
        {
            ("item_txt.bin",   "item_labels"),
            ("name_txt.bin",   "menu_labels"),
            ("summon_txt.bin", "summon_labels"),
        };

        static readonly (string File, string Category, bool HasExtra)[] KernelTables =
        {
            ("item.bin",      "items",      true),
            ("command.bin",   "commands",   true),
            ("a_ability.bin", "abilities",  true),
            ("monmagic1.bin", "monster_magic", false),
            ("monmagic2.bin", "monster_magic", false),
        };

        static readonly (string File, string Category)[] MonsterTables =
        {
            ("monster1.bin", "monsters"),
            ("monster2.bin", "monsters"),
            ("monster3.bin", "monsters"),
        };

        public static int Run(string[] args)
        {
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }

            string root = args.Length > 1 ? args[1] : null;
            string outPath = args.Length > 2 ? args[2] : null;
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(outPath))
            {
                Console.WriteLine("USO: --name-catalog-extract <masterRoot> <out.json>");
                return 2;
            }
            if (!Directory.Exists(root))
            {
                Console.WriteLine($"BLOCKED: pasta nao existe: {root}");
                return 2;
            }

            var catalog = new Dictionary<string, Dictionary<string, Dictionary<int, string>>>();
            var counts = new Dictionary<string, int>();

            foreach (var (dir, iso, decoder) in Languages)
            {
                string langRoot = Path.Combine(root, dir, "battle", "kernel");
                if (!Directory.Exists(langRoot)) continue;

                // Menu label tables (NameDescriptionTextTable format).
                foreach (var (file, category) in NameDescTables)
                {
                    string path = Path.Combine(langRoot, file);
                    if (!File.Exists(path)) continue;
                    try
                    {
                        var table = NameDescriptionTextTable_File.Read(File.ReadAllBytes(path), decoder);
                        if (!catalog.TryGetValue(category, out var byIso)) { byIso = new(); catalog[category] = byIso; }
                        if (!byIso.TryGetValue(iso, out var names)) { names = new(); byIso[iso] = names; }
                        foreach (var e in table.Entries)
                        {
                            string name = e.NameText?.Trim();
                            if (string.IsNullOrWhiteSpace(name)) continue;
                            names[e.Index] = name;
                        }
                        Console.WriteLine($"{dir}/{file}: {table.EntryCount} entries");
                    }
                    catch (Exception ex) { Console.WriteLine($"{dir}/{file}: SKIP ({ex.Message})"); }
                }

                // Kernel tables (item/command/ability/monmagic).
                foreach (var (file, category, hasExtra) in KernelTables)
                {
                    string kPath = Path.Combine(langRoot, file);
                    if (!File.Exists(kPath)) continue;
                    try
                    {
                        var list = Ability_Command.ReadList(File.ReadAllBytes(kPath), hasExtra);
                        if (!catalog.TryGetValue(category, out var byIso)) { byIso = new(); catalog[category] = byIso; }
                        if (!byIso.TryGetValue(iso, out var names)) { names = new(); byIso[iso] = names; }
                        for (int i = 0; i < list.Count; i++)
                        {
                            string name = FfxEncoding.DecodeScriptLossless(
                                list[i].NameScriptBytes ?? Array.Empty<byte>(), decoder);
                            if (string.IsNullOrWhiteSpace(name)) continue;
                            names[i] = name;
                        }
                        Console.WriteLine($"{dir}/{file}: {list.Count} entries");
                    }
                    catch (Exception ex) { Console.WriteLine($"{dir}/{file}: SKIP ({ex.Message})"); }
                }
// Monster name tables (monster1/2/3.bin).
                foreach (var (file, category) in MonsterTables)
                {
                    string mPath = Path.Combine(langRoot, file);
                    if (!File.Exists(mPath)) continue;
                    try
                    {
                        byte[] raw = File.ReadAllBytes(mPath);
                        if (raw.Length < 0x14) continue;
                        int minIdx = EntryU16(raw, 0x08);
                        int maxIdx = EntryU16(raw, 0x0A);
                        int entryLen = EntryU16(raw, 0x0C);
                        int poolStart = 0x14 + EntryU16(raw, 0x0E);
                        int count = (maxIdx - minIdx) + 1;
                        if (entryLen <= 0 || count <= 0 || raw.Length < poolStart) continue;
                        if (!catalog.TryGetValue(category, out var byIso)) { byIso = new(); catalog[category] = byIso; }
                        if (!byIso.TryGetValue(iso, out var names)) { names = new(); byIso[iso] = names; }
                        for (int mi = 0; mi < count; mi++)
                        {
                            int baseOff = 0x14 + mi * entryLen;
                            if (baseOff + 2 > raw.Length) break;
                            string name = DecodePoolString(raw, poolStart, EntryU16(raw, baseOff), decoder);
                            if (string.IsNullOrWhiteSpace(name)) continue;
                            names[minIdx + mi] = name;
                        }
                        Console.WriteLine($"{dir}/{file}: {count} entries");
                    }
                    catch (Exception ex) { Console.WriteLine($"{dir}/{file}: SKIP ({ex.Message})"); }
                }

                // Weapon names (w_name.bin).
                string wName = Path.Combine(langRoot, "w_name.bin");
                if (File.Exists(wName))
                {
                    try
                    {
                        var table = WeaponNameTable_File.Read(File.ReadAllBytes(wName), decoder);
                        if (!catalog.TryGetValue("weapons", out var byIso)) { byIso = new(); catalog["weapons"] = byIso; }
                        if (!byIso.TryGetValue(iso, out var names)) { names = new(); byIso[iso] = names; }
                        foreach (var e in table.Entries)
                            foreach (var r in e.RegularNames)
                            {
                                string? t = r.Text?.Trim();
                                if (!string.IsNullOrWhiteSpace(t)) { names[e.Index] = t; break; }
                            }
                        Console.WriteLine($"{dir}/w_name.bin: {table.EntryCount} entries");
                    }
                    catch (Exception ex) { Console.WriteLine($"{dir}/w_name.bin: SKIP ({ex.Message})"); }
                }
            }

            var payload = new
            {
                summary = new
                {
                    title = "FFX in-game name tables (ID -> name per language)",
                    source = "ffx_ps2/ffx/master/<lang>/battle/kernel",
                    generatedAt = DateTime.UtcNow.ToString("o"),
                    counts = counts,
                },
                catalog = catalog,
            };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
            File.WriteAllText(outPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"\nEscrito em: {outPath}");
            Console.WriteLine("Categorias: " + string.Join(", ", catalog.Keys));
            return 0;
        }

        static int EntryU16(byte[] b, int off)
        {
            if (off + 1 >= b.Length) return 0;
            return b[off] | (b[off + 1] << 8);
        }

        static string DecodePoolString(byte[] raw, int poolStart, int offset, Dictionary<byte, char> decoder)
        {
            if (offset <= 0 || offset >= raw.Length - poolStart) return "";
            int start = poolStart + offset;
            if (start >= raw.Length) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = start; i < raw.Length; i++)
            {
                byte x = raw[i];
                if (x == 0) break;
                if (decoder.TryGetValue(x, out char ch)) sb.Append(ch);
                else sb.Append('<').Append(x).Append('>');
            }
            return sb.ToString();
        }
    }
}

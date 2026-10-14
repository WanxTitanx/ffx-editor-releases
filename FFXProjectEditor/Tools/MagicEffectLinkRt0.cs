using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Ps3;
using FFXProjectEditor.Utils.Encoding;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace FFXProjectEditor.Tools
{
    // Headless bridge from a concrete command/monmagic row to its visual-effect assets.
    // It proves the data-level selector (Anim1Id/Anim2Id) and inventories the same-shape
    // .dds.phyre payloads that the existing PS3 magic texture writer can replace.
    internal static class MagicEffectLinkRt0
    {
        const string DefaultAbilityFile = @"work\monster_magic_grow_pilot\monmagic2.bin";
        const string DefaultMagicRoot = @"D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data\magic";
        const string DefaultMagicDllRoot = @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\magicFiles\FFX";
        const string DefaultOutputDir = @"work\monster_magic_grow_pilot";

        public static int Run(string[] args)
        {
            try
            {
                string abilityFile = args.Length > 1 ? args[1] : DefaultAbilityFile;
                string idArg = args.Length > 2 ? args[2] : "last";
                string magicRoot = args.Length > 3 ? args[3] : DefaultMagicRoot;
                string outputDir = args.Length > 4 ? args[4] : DefaultOutputDir;
                string magicDllRoot = args.Length > 5 ? args[5] : DefaultMagicDllRoot;
                bool hasExtraInfo = InferHasExtraInfo(abilityFile);

                Console.WriteLine("=== Magic Effect LINK RT0 (command row -> magic_#### assets) ===");
                Console.WriteLine($"ability : {abilityFile}");
                Console.WriteLine($"id      : {idArg}");
                Console.WriteLine($"magic   : {magicRoot}");
                Console.WriteLine($"dllroot : {magicDllRoot}");
                Console.WriteLine($"output  : {outputDir}");

                if (!File.Exists(abilityFile))
                {
                    Console.WriteLine("FAIL: ability/monmagic file not found.");
                    return 2;
                }
                if (!Directory.Exists(magicRoot))
                {
                    Console.WriteLine("FAIL: ps3data magic root not found.");
                    return 2;
                }

                byte[] bytes = File.ReadAllBytes(abilityFile);
                List<Ability_Command> entries = Ability_Command.ReadList(bytes, hasExtraInfo);
                int commandId = ResolveCommandId(idArg, entries.Count);
                if (commandId < 0 || commandId >= entries.Count)
                {
                    Console.WriteLine($"FAIL: command id {commandId} is outside 0..{entries.Count - 1}.");
                    return 2;
                }

                Ability_Command command = entries[commandId];
                List<EffectAssetReport> effects = BuildEffectReports(command, magicRoot);
                bool hasAnyTexture = effects.Any(effect => effect.TextureCount > 0);
                bool hasWritableTexture = effects.Any(effect => effect.WritableTextureCount > 0);
                List<RuntimeDllReport> runtimeDlls = BuildRuntimeDllReports(command, magicDllRoot);
                bool hasAllRuntimeDlls = Directory.Exists(magicDllRoot) && runtimeDlls.All(dll => dll.Exists);

                Directory.CreateDirectory(outputDir);
                string jsonFile = Path.Combine(outputDir, "magic_effect_link_rt0.json");
                string mdFile = Path.Combine(outputDir, "MAGIC_EFFECT_LINK_RT0.md");

                var payload = new
                {
                    abilityFile,
                    abilitySha256 = Sha256Hex(bytes),
                    hasExtraInfo,
                    commandId,
                    name = DecodeUs(command.NameScriptBytes),
                    description = DecodeUs(command.DescriptionScriptBytes),
                    anim1Id = command.Anim1Id,
                    anim2Id = command.Anim2Id,
                    casterAnimId = command.CasterAnimId,
                    magicRoot,
                    magicDllRoot,
                    effects,
                    runtimeDlls,
                    pass = hasAnyTexture && hasWritableTexture && hasAllRuntimeDlls
                };

                File.WriteAllText(jsonFile, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
                File.WriteAllText(mdFile, BuildMarkdown(payload.name, payload.description, abilityFile, commandId, command, magicRoot, magicDllRoot, effects, runtimeDlls, hasAnyTexture, hasWritableTexture, hasAllRuntimeDlls));

                Console.WriteLine($"command : {commandId} `{payload.name}`");
                Console.WriteLine($"anim ids: Anim1={command.Anim1Id} Anim2={command.Anim2Id} Caster={command.CasterAnimId}");
                foreach (EffectAssetReport effect in effects)
                {
                    Console.WriteLine($"effect  : magic_{effect.EffectId:D4} folder={effect.FolderExists} textures={effect.TextureCount} writable={effect.WritableTextureCount}");
                    if (!string.IsNullOrWhiteSpace(effect.FirstWritableTexture))
                        Console.WriteLine($"first   : {effect.FirstWritableTexture}");
                }
                foreach (RuntimeDllReport dll in runtimeDlls)
                    Console.WriteLine($"dll     : magic_{dll.EffectId:D4} exists={dll.Exists} path={dll.Path}");
                Console.WriteLine($"json    : {jsonFile}");
                Console.WriteLine($"runbook : {mdFile}");

                if (!hasAnyTexture)
                {
                    Console.WriteLine("VERDICT: FAIL - Anim IDs were read, but no ps3data magic texture folder was found for them.");
                    return 1;
                }
                if (!hasWritableTexture)
                {
                    Console.WriteLine("VERDICT: FAIL - texture folders exist, but no same-shape writable .dds.phyre layout was detected.");
                    return 1;
                }
                if (!hasAllRuntimeDlls)
                {
                    Console.WriteLine("VERDICT: FAIL - runtime magicFiles DLL is missing for at least one Anim ID. The game will show missing/damaged magic_####.dll.");
                    return 1;
                }

                Console.WriteLine("VERDICT: PASS - ability row resolves to magic_#### textures and runtime magicFiles DLLs.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static bool InferHasExtraInfo(string abilityFile)
        {
            string name = Path.GetFileName(abilityFile);
            return name.Equals("command.bin", StringComparison.OrdinalIgnoreCase)
                || name.Equals("command2.bin", StringComparison.OrdinalIgnoreCase)
                || name.Equals("item.bin", StringComparison.OrdinalIgnoreCase)
                || name.Equals("item2.bin", StringComparison.OrdinalIgnoreCase);
        }

        static int ResolveCommandId(string value, int count)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Equals("last", StringComparison.OrdinalIgnoreCase))
                return count - 1;

            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(value[2..], System.Globalization.NumberStyles.HexNumber, null, out int hex))
                return hex;

            return int.Parse(value);
        }

        static List<EffectAssetReport> BuildEffectReports(Ability_Command command, string magicRoot)
        {
            int[] ids = new[] { command.Anim1Id, command.Anim2Id }
                .Where(id => id >= 0)
                .Select(id => (int)id)
                .Distinct()
                .ToArray();

            List<EffectAssetReport> reports = [];
            foreach (int id in ids)
            {
                string folder = Path.Combine(magicRoot, $"magic_{id:D4}");
                List<TextureAssetReport> textures = [];
                List<string> sidecars = [];

                if (Directory.Exists(folder))
                {
                    foreach (string file in Directory.EnumerateFiles(folder, "*.dds.phyre", SearchOption.AllDirectories)
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                    {
                        bool ok = Ps3MagicTextureWriter.TryReadMip0Layout(file, out Ps3PhyreMip0Layout? layout, out string note);
                        textures.Add(new TextureAssetReport(
                            Path.GetRelativePath(folder, file),
                            file,
                            new FileInfo(file).Length,
                            ok,
                            ok && layout != null ? layout.Format : "?",
                            ok && layout != null ? layout.Width : 0,
                            ok && layout != null ? layout.Height : 0,
                            ok && layout != null ? layout.BufferStart : 0,
                            ok && layout != null ? layout.Mip0Size : 0,
                            ok && layout != null ? layout.Summary : note,
                            ok && layout != null ? Mip0Formula(layout.Format, layout.Width, layout.Height) : string.Empty));
                    }

                    sidecars = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                        .Where(path => !path.EndsWith(".dds.phyre", StringComparison.OrdinalIgnoreCase))
                        .Select(path => Path.GetRelativePath(folder, path))
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

                TextureAssetReport? firstWritable = textures.FirstOrDefault(texture => texture.WritableMip0);
                string repackCommand = firstWritable == null
                    ? string.Empty
                    : $"dotnet FFXProjectEditor.dll --ps3magic-phyre-repack-rt0 \"{firstWritable.FullPath}\" \"edited_same_format_same_dims.dds\" \"work\\magic_effect_texture_pilot\\magic_{id:D4}\\{Path.GetFileName(firstWritable.FullPath)}\"";

                reports.Add(new EffectAssetReport(
                    id,
                    $"magic_{id:D4}",
                    folder,
                    Directory.Exists(folder),
                    textures.Count,
                    textures.Count(texture => texture.WritableMip0),
                    sidecars,
                    textures.Take(24).ToList(),
                    firstWritable?.FullPath ?? string.Empty,
                    repackCommand));
            }

            return reports;
        }

        static List<RuntimeDllReport> BuildRuntimeDllReports(Ability_Command command, string magicDllRoot)
        {
            int[] ids = new[] { command.Anim1Id, command.Anim2Id }
                .Where(id => id >= 0)
                .Select(id => (int)id)
                .Distinct()
                .ToArray();

            List<RuntimeDllReport> reports = [];
            foreach (int id in ids)
            {
                string path = Path.Combine(magicDllRoot, $"magic_{id:D4}.dll");
                bool exists = File.Exists(path);
                reports.Add(new RuntimeDllReport(
                    id,
                    path,
                    exists,
                    exists ? new FileInfo(path).Length : 0,
                    exists ? Sha256Hex(File.ReadAllBytes(path)) : string.Empty));
            }
            return reports;
        }

        static string BuildMarkdown(
            string name,
            string description,
            string abilityFile,
            int commandId,
            Ability_Command command,
            string magicRoot,
            string magicDllRoot,
            IReadOnlyList<EffectAssetReport> effects,
            IReadOnlyList<RuntimeDllReport> runtimeDlls,
            bool hasAnyTexture,
            bool hasWritableTexture,
            bool hasAllRuntimeDlls)
        {
            List<string> lines = [];
            lines.Add("# Magic Effect LINK RT0");
            lines.Add("");
            lines.Add($"Status: {(hasAnyTexture && hasWritableTexture && hasAllRuntimeDlls ? "PASS" : "FAIL")}");
            lines.Add("");
            lines.Add("## Command row");
            lines.Add("");
            lines.Add($"- File: `{abilityFile}`");
            lines.Add($"- Command id: `{commandId}`");
            lines.Add($"- Name: `{name}`");
            lines.Add($"- Description: `{description}`");
            lines.Add($"- Anim1Id: `{command.Anim1Id}` -> `magic_{command.Anim1Id:D4}`");
            lines.Add($"- Anim2Id: `{command.Anim2Id}` -> `magic_{command.Anim2Id:D4}`");
            lines.Add($"- CasterAnimId: `{command.CasterAnimId}`");
            lines.Add($"- PS3 magic root: `{magicRoot}`");
            lines.Add($"- Runtime magicFiles root: `{magicDllRoot}`");
            lines.Add("");
            lines.Add("## Effect assets");
            lines.Add("");

            foreach (EffectAssetReport effect in effects)
            {
                lines.Add($"### magic_{effect.EffectId:D4}");
                lines.Add("");
                lines.Add($"- Folder exists: `{effect.FolderExists}`");
                lines.Add($"- Folder: `{effect.Folder}`");
                lines.Add($"- Texture count: `{effect.TextureCount}`");
                lines.Add($"- Same-shape writable textures: `{effect.WritableTextureCount}`");
                lines.Add($"- Sidecars: `{effect.Sidecars.Count}`");
                if (!string.IsNullOrWhiteSpace(effect.FirstWritableTexture))
                    lines.Add($"- First writable texture: `{effect.FirstWritableTexture}`");
                if (!string.IsNullOrWhiteSpace(effect.RepackCommand))
                    lines.Add($"- Repack command: `{effect.RepackCommand}`");
                lines.Add("");
                foreach (TextureAssetReport texture in effect.Textures)
                {
                    lines.Add($"- `{texture.RelativePath}`: writable=`{texture.WritableMip0}`, `{texture.LayoutSummary}`, formula=`{texture.Mip0Formula}`");
                }
                lines.Add("");
            }

            lines.Add("## Runtime DLLs");
            lines.Add("");
            foreach (RuntimeDllReport dll in runtimeDlls)
            {
                lines.Add($"- `magic_{dll.EffectId:D4}.dll`: exists=`{dll.Exists}`, size=`{dll.FileSize}`, sha256=`{dll.Sha256}`");
                lines.Add($"  - Path: `{dll.Path}`");
            }
            lines.Add("");
            lines.Add("## Meaning");
            lines.Add("");
            lines.Add("- This proves the command row selects visual effect ids through `Anim1Id`/`Anim2Id`.");
            lines.Add("- The folders under `ps3data\\magic\\magic_####` are editable base texture payloads when a known `PTexture2D` mip0 layout is detected.");
            lines.Add("- The DLLs under `magicFiles\\FFX\\magic_####.dll` are required at runtime; missing DLLs reproduce the in-game `missing or damaged` crash dialog.");
            lines.Add("- Repacking is same-format/same-dimensions/same-mip0-size only; it changes texture payload, not the DLL callback timeline.");
            lines.Add("- Full new timing/mesh behavior still belongs to the `magic_####.dll` + root/interpreter/opcode lane.");
            lines.Add("");
            return string.Join(Environment.NewLine, lines);
        }

        static string Mip0Formula(string format, int width, int height) => format switch
        {
            "ARGB8" => $"{width} * {height} * 4 = {width * height * 4}",
            "DXT1" => $"ceil({width}/4) * ceil({height}/4) * 8 = {((width + 3) / 4) * ((height + 3) / 4) * 8}",
            "DXT3" or "DXT5" => $"ceil({width}/4) * ceil({height}/4) * 16 = {((width + 3) / 4) * ((height + 3) / 4) * 16}",
            "L8" => $"{width} * {height} = {width * height}",
            _ => string.Empty
        };

        static string DecodeUs(byte[] bytes) =>
            FfxEncoding.DecodeScript(bytes).GetString(FfxEncoding.UsDecoder, withControlCodes: true);

        static string Sha256Hex(byte[] bytes)
        {
            byte[] hash = SHA256.HashData(bytes);
            return string.Concat(hash.Select(b => b.ToString("x2")));
        }

        internal sealed record EffectAssetReport(
            int EffectId,
            string FolderName,
            string Folder,
            bool FolderExists,
            int TextureCount,
            int WritableTextureCount,
            IReadOnlyList<string> Sidecars,
            IReadOnlyList<TextureAssetReport> Textures,
            string FirstWritableTexture,
            string RepackCommand);

        internal sealed record RuntimeDllReport(
            int EffectId,
            string Path,
            bool Exists,
            long FileSize,
            string Sha256);

        internal sealed record TextureAssetReport(
            string RelativePath,
            string FullPath,
            long FileSize,
            bool WritableMip0,
            string Format,
            int Width,
            int Height,
            int BufferStart,
            int Mip0Size,
            string LayoutSummary,
            string Mip0Formula);
    }
}

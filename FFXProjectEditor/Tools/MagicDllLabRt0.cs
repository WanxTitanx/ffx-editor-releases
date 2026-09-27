using FFXProjectEditor.FfxLib.Ps3;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Tools
{
    internal static class MagicDllLabRt0
    {
        const string DefaultMagicFilesRoot = @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\magicFiles\FFX";
        const string DefaultOutputDir = @"work\magic_dll_lab_rt0";

        public static int Run(string[] args)
        {
            try
            {
                string dllPath = args.Length > 1 ? args[1] : ResolveDefaultDll();
                string outputDir = args.Length > 2 ? args[2] : Path.Combine(DefaultOutputDir, Path.GetFileNameWithoutExtension(dllPath));
                string repoRoot = Directory.GetCurrentDirectory();

                Console.WriteLine("=== Magic DLL LAB RT0 (PE decompile + byte-preserving compile) ===");
                Console.WriteLine($"dll    : {dllPath}");
                Console.WriteLine($"output : {outputDir}");

                if (!File.Exists(dllPath))
                {
                    Console.WriteLine("FAIL: DLL not found.");
                    return 2;
                }

                MagicDllDecompileResult decompile = MagicDllDecompiler.DecompileToFolder(dllPath, outputDir, repoRoot);
                string repacked = Path.Combine(outputDir, "repacked_byte_identical.dll");
                MagicDllCompileResult compile = MagicDllDecompiler.CompileBytePreserving(dllPath, repacked);
                MagicDllNativeBuildResult native = MagicDllDecompiler.BuildNativeProject(decompile.NativeProjectDir, Path.Combine(outputDir, "native_rebuild.dll"));

                var payload = new
                {
                    source = dllPath,
                    outputDir,
                    decompile.Inspection,
                    decompile.ManifestPath,
                    decompile.MarkdownPath,
                    decompile.SectionsDir,
                    decompile.StringsPath,
                    decompile.ExportsPath,
                    decompile.ImportsPath,
                    decompile.PatchTemplatePath,
                    decompile.NativeProjectDir,
                    bytePreservingCompile = compile,
                    nativeBuild = native,
                    pass = compile.Pass
                };

                string json = Path.Combine(outputDir, "magic_dll_lab_rt0.json");
                File.WriteAllText(json, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));

                Console.WriteLine($"machine : {decompile.Inspection.MachineName} PE{(decompile.Inspection.IsPe32Plus ? "32+" : "32")}");
                Console.WriteLine($"sections: {decompile.Inspection.Sections.Count}");
                Console.WriteLine($"exports : {decompile.Inspection.Exports.Count}");
                Console.WriteLine($"imports : {decompile.Inspection.Imports.Sum(lib => lib.Imports.Count)} in {decompile.Inspection.Imports.Count} libs");
                Console.WriteLine($"strings : {decompile.Inspection.Strings.Count}");
                Console.WriteLine($"overlay : {(decompile.Inspection.OverlayEvidence == null ? "missing" : "attached")}");
                Console.WriteLine($"manifest: {decompile.ManifestPath}");
                Console.WriteLine($"runbook : {decompile.MarkdownPath}");
                Console.WriteLine($"sections: {decompile.SectionsDir}");
                Console.WriteLine($"native : {decompile.NativeProjectDir}");
                Console.WriteLine($"repack : {compile.Summary} source={compile.SourceSha256} output={compile.OutputSha256}");
                Console.WriteLine($"c/asm  : {native.Summary}");
                Console.WriteLine($"json   : {json}");

                if (!compile.Pass)
                {
                    Console.WriteLine("VERDICT: FAIL - byte-preserving compiler drifted.");
                    return 1;
                }

                Console.WriteLine("VERDICT: PASS - DLL decoded and re-emitted byte-identical; C/ASM rebuild project generated.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static string ResolveDefaultDll()
        {
            string prism = Path.Combine(DefaultMagicFilesRoot, "magic_0714.dll");
            if (File.Exists(prism))
                return prism;
            string fira = Path.Combine(DefaultMagicFilesRoot, "magic_0082.dll");
            if (File.Exists(fira))
                return fira;
            return prism;
        }
    }
}

using Keystone;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Ps3
{
    internal static class MagicDllDecompiler
    {
        const int ImageDirectoryEntryExport = 0;
        const int ImageDirectoryEntryImport = 1;

        public static MagicDllInspection Inspect(string dllPath, string? repoRoot = null)
        {
            byte[] bytes = File.ReadAllBytes(dllPath);
            PeReader reader = new(bytes);
            MagicDllOverlayEvidence? overlay = TryLoadOverlayEvidence(dllPath, repoRoot);

            List<MagicDllSection> sections = reader.ReadSections();
            List<MagicDllExport> exports = reader.ReadExports(sections);
            List<MagicDllImportLibrary> imports = reader.ReadImports(sections);
            List<MagicDllAsciiString> strings = ExtractAsciiStrings(bytes, sections, 4, 4000);

            return new MagicDllInspection(
                dllPath,
                Path.GetFileName(dllPath),
                TryParseMagicId(Path.GetFileNameWithoutExtension(dllPath)),
                bytes.LongLength,
                Sha256Hex(bytes),
                reader.Machine,
                reader.MachineName,
                reader.Characteristics,
                reader.IsPe32Plus,
                reader.ImageBase,
                reader.AddressOfEntryPoint,
                reader.SizeOfImage,
                reader.SectionAlignment,
                reader.FileAlignment,
                reader.TimeDateStampUtc,
                sections,
                exports,
                imports,
                strings,
                overlay,
                BuildWarnings(reader, sections, exports, imports, overlay));
        }

        public static MagicDllDecompileResult DecompileToFolder(string dllPath, string outputDir, string? repoRoot = null)
        {
            MagicDllInspection inspection = Inspect(dllPath, repoRoot);
            Directory.CreateDirectory(outputDir);

            string sectionsDir = Path.Combine(outputDir, "sections");
            Directory.CreateDirectory(sectionsDir);

            byte[] bytes = File.ReadAllBytes(dllPath);
            foreach (MagicDllSection section in inspection.Sections)
            {
                if (section.RawSize <= 0 || section.RawPointer < 0 || section.RawPointer >= bytes.Length)
                    continue;

                int count = Math.Min(section.RawSize, bytes.Length - section.RawPointer);
                File.WriteAllBytes(Path.Combine(sectionsDir, $"{SanitizeFilePart(section.Name)}_{section.RawPointer:X8}_{section.VirtualAddress:X8}.bin"),
                    bytes.Skip(section.RawPointer).Take(count).ToArray());
            }

            string manifestPath = Path.Combine(outputDir, "magic_dll_manifest.json");
            string markdownPath = Path.Combine(outputDir, "MAGIC_DLL_DECOMPILE.md");
            string stringsPath = Path.Combine(outputDir, "strings.txt");
            string exportsPath = Path.Combine(outputDir, "exports.def");
            string importsPath = Path.Combine(outputDir, "imports.txt");
            string patchTemplatePath = Path.Combine(outputDir, "patch_plan.template.json");
            string projectDir = Path.Combine(outputDir, "c_asm_project");

            File.WriteAllText(manifestPath, JsonSerializer.Serialize(inspection, JsonOptions()));
            File.WriteAllText(markdownPath, BuildMarkdown(inspection));
            File.WriteAllLines(stringsPath, inspection.Strings.Select(s => $"0x{s.FileOffset:X8}\tRVA=0x{s.Rva:X8}\t{s.Section}\t{s.Value}"));
            File.WriteAllText(exportsPath, BuildDefFile(inspection));
            File.WriteAllLines(importsPath, inspection.Imports.SelectMany(lib => lib.Imports.Select(i => $"{lib.Library}\t{i.DisplayName}\tThunkRVA=0x{i.ThunkRva:X8}")));
            File.WriteAllText(patchTemplatePath, JsonSerializer.Serialize(MagicDllPatchPlan.Template(), JsonOptions()));
            CreateNativeProject(inspection, projectDir);

            return new MagicDllDecompileResult(inspection, manifestPath, markdownPath, sectionsDir, stringsPath, exportsPath, importsPath, patchTemplatePath, projectDir);
        }

        public static MagicDllCompileResult CompileBytePreserving(string sourceDll, string outputDll)
        {
            ThrowIfOutputExists(outputDll);
            MagicDllInspection source = Inspect(sourceDll);
            string stagedDll = CreateStagingPath(outputDll);
            try
            {
                File.Copy(sourceDll, stagedDll, overwrite: false);
                MagicDllInspection staged = Inspect(stagedDll);
                bool pass = string.Equals(source.Sha256, staged.Sha256, StringComparison.OrdinalIgnoreCase);
                if (!pass)
                    throw new IOException("Staged Magic DLL hash does not match the source.");

                ThrowIfOutputExists(outputDll);
                File.Move(stagedDll, outputDll);
                return new MagicDllCompileResult(
                    sourceDll,
                    outputDll,
                    source.Sha256,
                    staged.Sha256,
                    true,
                    "byte-identical DLL re-emitted");
            }
            finally
            {
                if (File.Exists(stagedDll))
                    File.Delete(stagedDll);
            }
        }

        public static MagicDllCompileResult ApplyPatchPlan(string sourceDll, string patchPlanPath, string outputDll)
        {
            MagicDllPatchPlan? plan = JsonSerializer.Deserialize<MagicDllPatchPlan>(File.ReadAllText(patchPlanPath), JsonOptions());
            if (plan == null)
                throw new InvalidDataException("Patch plan JSON could not be decoded.");

            ThrowIfOutputExists(outputDll);

            MagicDllInspection sourceInfo = Inspect(sourceDll);
            byte[] bytes = File.ReadAllBytes(sourceDll);

            foreach (MagicDllBytePatch patch in plan.BytePatches ?? [])
            {
                byte[] payload = ParseHexBytes(patch.Hex);
                ApplyBytes(bytes, ResolvePatchOffset(sourceInfo, patch.FileOffset, patch.Rva), payload);
            }

            foreach (MagicDllAsciiPatch patch in plan.AsciiPatches ?? [])
            {
                int offset = ResolvePatchOffset(sourceInfo, patch.FileOffset, patch.Rva);
                int max = patch.MaxLength <= 0 ? patch.Text.Length + (patch.NullTerminate ? 1 : 0) : patch.MaxLength;
                byte[] payload = new byte[max];
                byte[] text = Encoding.ASCII.GetBytes(patch.Text ?? string.Empty);
                if (text.Length > max || (patch.NullTerminate && text.Length >= max))
                    throw new InvalidOperationException($"ASCII patch at 0x{offset:X} does not fit in {max} bytes.");
                Array.Copy(text, payload, text.Length);
                ApplyBytes(bytes, offset, payload);
            }

            foreach (MagicDllAsmPatch patch in plan.AsmPatches ?? [])
            {
                int offset = ResolvePatchOffset(sourceInfo, patch.FileOffset, patch.Rva);
                ulong address = patch.VirtualAddress != 0
                    ? patch.VirtualAddress
                    : (patch.Rva != 0 ? sourceInfo.ImageBase + (uint)patch.Rva : sourceInfo.ImageBase + (uint)sourceInfo.FileOffsetToRva(offset));
                byte[] payload = AssembleX86(patch.Assembly ?? string.Empty, address, sourceInfo.IsPe32Plus);
                if (patch.MaxLength > 0 && payload.Length > patch.MaxLength)
                    throw new InvalidOperationException($"ASM patch at 0x{offset:X} assembled to {payload.Length} bytes, over max {patch.MaxLength}.");
                if (patch.MaxLength > 0 && payload.Length < patch.MaxLength)
                    payload = payload.Concat(Enumerable.Repeat((byte)0x90, patch.MaxLength - payload.Length)).ToArray();
                ApplyBytes(bytes, offset, payload);
            }

            string stagedDll = CreateStagingPath(outputDll);
            try
            {
                WriteAllBytesCreateNew(stagedDll, bytes);
                MagicDllInspection staged = Inspect(stagedDll);

                ThrowIfOutputExists(outputDll);
                File.Move(stagedDll, outputDll);
                return new MagicDllCompileResult(
                    sourceDll,
                    outputDll,
                    sourceInfo.Sha256,
                    staged.Sha256,
                    true,
                    "patched DLL re-emitted and PE headers decoded");
            }
            finally
            {
                if (File.Exists(stagedDll))
                    File.Delete(stagedDll);
            }
        }

        public static MagicDllCloneResult CloneToMagicId(string sourceDll, string magicFilesRoot, int newMagicId)
        {
            if (newMagicId < 0 || newMagicId > 9999)
                throw new ArgumentOutOfRangeException(nameof(newMagicId), "magic id must fit magic_####.");

            Directory.CreateDirectory(magicFilesRoot);
            string outputDll = Path.Combine(magicFilesRoot, $"magic_{newMagicId:D4}.dll");
            if (File.Exists(outputDll) || Directory.Exists(outputDll))
                throw new IOException($"Cannot clone to {Path.GetFileName(outputDll)} because that Magic DLL already exists.");

            MagicDllInspection source = Inspect(sourceDll);
            string stagedDll = outputDll + $".ffxms-staging-{Guid.NewGuid():N}";
            try
            {
                File.Copy(sourceDll, stagedDll, overwrite: false);
                MagicDllInspection staged = Inspect(stagedDll);
                if (!source.Sha256.Equals(staged.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Staged Magic DLL hash does not match the source.");

                // File.Move has CREATE_NEW semantics here: if another process claims the ID after
                // the preflight, the commit fails without replacing that process's file.
                if (File.Exists(outputDll) || Directory.Exists(outputDll))
                    throw new IOException($"Cannot clone to {Path.GetFileName(outputDll)} because that Magic DLL already exists.");
                File.Move(stagedDll, outputDll);

                return new MagicDllCloneResult(
                    sourceDll,
                    outputDll,
                    newMagicId,
                    source.Sha256,
                    staged.Sha256,
                    true);
            }
            finally
            {
                if (File.Exists(stagedDll))
                    File.Delete(stagedDll);
            }
        }

        private static string CreateStagingPath(string outputDll)
        {
            string fullOutput = Path.GetFullPath(outputDll);
            Directory.CreateDirectory(Path.GetDirectoryName(fullOutput)!);
            return fullOutput + $".ffxms-staging-{Guid.NewGuid():N}";
        }

        private static string CreateNativeBuildStagingDirectory(string outputDll)
        {
            string outputDirectory = Path.GetDirectoryName(outputDll)!;
            Directory.CreateDirectory(outputDirectory);
            string stagingDirectory = Path.Combine(
                outputDirectory,
                Path.GetFileName(outputDll) + $".ffxms-staging-{Guid.NewGuid():N}");
            Directory.CreateDirectory(stagingDirectory);
            return stagingDirectory;
        }

        private static void ThrowIfOutputExists(string outputDll)
        {
            if (File.Exists(outputDll) || Directory.Exists(outputDll))
                throw new IOException($"Cannot write {Path.GetFileName(outputDll)} because that Magic DLL already exists.");
        }

        private static void WriteAllBytesCreateNew(string path, byte[] bytes)
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);
        }

        public static MagicDllNativeBuildResult BuildNativeProject(string projectDir, string outputDll, string toolchain = "auto")
            => BuildNativeProjectCore(projectDir, outputDll, toolchain, FindOnPath, RunProcess);

        internal static MagicDllNativeBuildResult BuildNativeProjectCore(
            string projectDir,
            string outputDll,
            string toolchain,
            Func<string, string?> compilerResolver,
            Func<string, string, string, (int exitCode, string stdout, string stderr)> processRunner)
        {
            ArgumentNullException.ThrowIfNull(compilerResolver);
            ArgumentNullException.ThrowIfNull(processRunner);

            // A native rebuild may point directly at a user's magicFiles folder. Treat that path
            // as create-only before inspecting the project or resolving a compiler, so no error
            // path can reinterpret an existing DLL as disposable build output.
            string fullOutputDll = Path.GetFullPath(outputDll);
            ThrowIfOutputExists(fullOutputDll);

            string cFile = Path.Combine(projectDir, "magic_stub.c");
            string defFile = Path.Combine(projectDir, "exports.def");
            if (!File.Exists(cFile) || !File.Exists(defFile))
                return new MagicDllNativeBuildResult(projectDir, outputDll, false, "native project is missing magic_stub.c or exports.def", string.Empty, string.Empty);

            string? compiler = toolchain.Equals("clang", StringComparison.OrdinalIgnoreCase) ? compilerResolver("clang-cl.exe") : null;
            compiler ??= toolchain.Equals("msvc", StringComparison.OrdinalIgnoreCase) ? compilerResolver("cl.exe") : null;
            compiler ??= compilerResolver("cl.exe") ?? compilerResolver("clang-cl.exe");
            if (compiler == null)
                return new MagicDllNativeBuildResult(projectDir, outputDll, false, "no cl.exe or clang-cl.exe found on PATH; run from a Developer Command Prompt or install LLVM/MSVC Build Tools", string.Empty, string.Empty);

            string stagingDirectory = CreateNativeBuildStagingDirectory(fullOutputDll);
            string stagedDll = Path.Combine(stagingDirectory, Path.GetFileName(fullOutputDll));
            try
            {
                string stagedObject = Path.Combine(stagingDirectory, "magic_stub.obj");
                string stagedImportLibrary = Path.Combine(stagingDirectory, Path.GetFileNameWithoutExtension(fullOutputDll) + ".lib");
                string stagedPdb = Path.Combine(stagingDirectory, Path.GetFileNameWithoutExtension(fullOutputDll) + ".pdb");
                string args = $"/nologo /LD /O2 /EHsc \"{cFile}\" /Fo:\"{stagedObject}\" /Fe:\"{stagedDll}\" /link /DEF:\"{defFile}\" /MACHINE:X86 /INCREMENTAL:NO /OUT:\"{stagedDll}\" /IMPLIB:\"{stagedImportLibrary}\" /PDB:\"{stagedPdb}\"";
                (int exitCode, string stdout, string stderr) = processRunner(compiler, args, projectDir);
                bool compilerProducedDll = File.Exists(stagedDll);
                if (exitCode != 0 || !compilerProducedDll)
                {
                    string failure = exitCode != 0
                        ? $"compiler failed with exit code {exitCode}"
                        : "compiler exited successfully but did not produce the staged DLL";
                    return new MagicDllNativeBuildResult(projectDir, outputDll, false, failure, stdout, stderr);
                }

                if (!TryValidateNativeBuildOutput(stagedDll, defFile, out string validationFailure))
                {
                    return new MagicDllNativeBuildResult(
                        projectDir,
                        outputDll,
                        false,
                        "compiler output is invalid: " + validationFailure,
                        stdout,
                        stderr);
                }

                // Recheck immediately before the create-only move. File.Move remains the final
                // authority if another process claims the destination between these operations.
                ThrowIfOutputExists(fullOutputDll);
                File.Move(stagedDll, fullOutputDll);
                return new MagicDllNativeBuildResult(projectDir, outputDll, true, "native C/ASM project built a DLL", stdout, stderr);
            }
            finally
            {
                // The compiler may emit .lib/.exp sidecars. They live inside the isolated staging
                // directory, so cleanup never enumerates or deletes anything beside user output.
                if (Directory.Exists(stagingDirectory))
                    Directory.Delete(stagingDirectory, recursive: true);
            }
        }

        private static bool TryValidateNativeBuildOutput(
            string stagedDll,
            string definitionFile,
            out string failure)
        {
            MagicDllInspection inspection = Inspect(stagedDll);
            if (inspection.Machine != 0x014C ||
                !string.Equals(inspection.MachineName, "x86", StringComparison.OrdinalIgnoreCase))
            {
                failure = $"expected an x86 PE image, got machine 0x{inspection.Machine:X4} ({inspection.MachineName})";
                return false;
            }

            if (inspection.IsPe32Plus)
            {
                failure = "x86 Magic DLL output must be PE32, not PE32+";
                return false;
            }

            const ushort ImageFileDll = 0x2000;
            if ((inspection.Characteristics & ImageFileDll) == 0)
            {
                failure = "PE image is not marked as a DLL (IMAGE_FILE_DLL missing)";
                return false;
            }

            if (inspection.ImageBase == 0 || inspection.SizeOfImage <= 0 ||
                inspection.SectionAlignment <= 0 || inspection.FileAlignment <= 0)
            {
                failure = "PE optional-header layout is incomplete";
                return false;
            }

            bool hasMappedSection = inspection.Sections.Any(section =>
                section.VirtualAddress > 0 &&
                section.RawPointer >= 0 &&
                section.RawSize > 0 &&
                (long)section.RawPointer + section.RawSize <= inspection.FileSize);
            if (!hasMappedSection)
            {
                failure = "PE image has no file-backed mapped section";
                return false;
            }

            if (!inspection.Sections.Any(section => (section.Characteristics & 0x20000000u) != 0))
            {
                failure = "PE image has no executable section";
                return false;
            }

            if (inspection.Exports.Count == 0)
            {
                failure = "PE image exposes no exports from the generated definition file";
                return false;
            }

            if (!TryReadDefinitionExports(definitionFile, out var requiredExports, out failure))
                return false;

            foreach ((string requiredName, int? requiredOrdinal) in requiredExports)
            {
                bool present = inspection.Exports.Any(export =>
                    string.Equals(export.Name, requiredName, StringComparison.Ordinal) &&
                    (!requiredOrdinal.HasValue || export.Ordinal == requiredOrdinal.Value));
                if (!present)
                {
                    failure = requiredOrdinal.HasValue
                        ? $"required export {requiredName} @{requiredOrdinal.Value} is missing"
                        : $"required export {requiredName} is missing";
                    return false;
                }
            }

            failure = string.Empty;
            return true;
        }

        private static bool TryReadDefinitionExports(
            string definitionFile,
            out List<(string Name, int? Ordinal)> exports,
            out string failure)
        {
            exports = [];
            try
            {
                foreach (string rawLine in File.ReadLines(definitionFile))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith(';') ||
                        line.Equals("EXPORTS", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("LIBRARY ", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string[] tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length == 0)
                        continue;
                    string name = tokens[0].Split('=')[0];
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    int? ordinal = null;
                    foreach (string token in tokens.Skip(1))
                    {
                        if (token.Length > 1 && token[0] == '@' &&
                            int.TryParse(token.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
                        {
                            ordinal = parsed;
                            break;
                        }
                    }
                    exports.Add((name, ordinal));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failure = "could not read exports.def: " + ex.Message;
                return false;
            }

            if (exports.Count == 0)
            {
                failure = "exports.def declares no required exports";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        static void CreateNativeProject(MagicDllInspection inspection, string projectDir)
        {
            Directory.CreateDirectory(projectDir);
            File.WriteAllText(Path.Combine(projectDir, "exports.def"), BuildDefFile(inspection));
            File.WriteAllText(Path.Combine(projectDir, "magic_stub.c"), BuildStubC(inspection));
            File.WriteAllText(Path.Combine(projectDir, "patches.asm"), BuildPatchAsmTemplate(inspection));
            File.WriteAllText(Path.Combine(projectDir, "build_msvc_x86.bat"), BuildMsvcBatch(inspection));
            File.WriteAllText(Path.Combine(projectDir, "README.md"), BuildNativeProjectReadme(inspection));
        }

        static string BuildMarkdown(MagicDllInspection info)
        {
            List<string> lines = [];
            lines.Add("# Magic DLL Decompile LAB");
            lines.Add("");
            lines.Add($"- DLL: `{info.FileName}`");
            lines.Add($"- Magic id: `{(info.MagicId.HasValue ? info.MagicId.Value.ToString("D4", CultureInfo.InvariantCulture) : "-")}`");
            lines.Add($"- Size: `{info.FileSize}`");
            lines.Add($"- SHA256: `{info.Sha256}`");
            lines.Add($"- Machine: `{info.MachineName}` (`0x{info.Machine:X4}`)");
            lines.Add($"- PE: `{(info.IsPe32Plus ? "PE32+" : "PE32")}`");
            lines.Add($"- ImageBase: `0x{info.ImageBase:X}`");
            lines.Add($"- EntryRVA: `0x{info.EntryPointRva:X}`");
            lines.Add($"- SizeOfImage: `0x{info.SizeOfImage:X}`");
            lines.Add("");
            lines.Add("## Sections");
            lines.Add("");
            foreach (MagicDllSection s in info.Sections)
                lines.Add($"- `{s.Name}` RVA=`0x{s.VirtualAddress:X8}` VS=`0x{s.VirtualSize:X}` RAW=`0x{s.RawPointer:X8}`/`0x{s.RawSize:X}` flags=`0x{s.Characteristics:X8}`");
            lines.Add("");
            lines.Add("## Exports");
            lines.Add("");
            foreach (MagicDllExport e in info.Exports.Take(96))
                lines.Add($"- ordinal `{e.Ordinal}` name `{e.Name}` RVA=`0x{e.Rva:X8}` file=`0x{e.FileOffset:X8}`");
            if (info.Exports.Count > 96)
                lines.Add($"- ... {info.Exports.Count - 96} more");
            lines.Add("");
            lines.Add("## Imports");
            lines.Add("");
            foreach (MagicDllImportLibrary lib in info.Imports)
                lines.Add($"- `{lib.Library}`: {string.Join(", ", lib.Imports.Take(24).Select(i => i.DisplayName))}{(lib.Imports.Count > 24 ? " ..." : string.Empty)}");
            lines.Add("");
            lines.Add("## Overlay Evidence");
            lines.Add("");
            if (info.OverlayEvidence == null)
            {
                lines.Add("- No overlay CSV row attached.");
            }
            else
            {
                MagicDllOverlayEvidence o = info.OverlayEvidence;
                lines.Add($"- GetEffectOverlayTable: `{o.GetEffectOverlayEa}`");
                lines.Add($"- InitMagicPRX: `{o.InitMagicPrxEa}`");
                lines.Add($"- Table RVA: `{o.TableRva}`");
                lines.Add($"- Slots read: `{o.SlotCountRead}`, nonzero: `{o.NonzeroSlotCount}`");
                lines.Add($"- Slot kinds: `{o.SlotKindSignature}`");
                foreach (MagicDllOverlaySlot slot in o.Slots.Where(s => !string.Equals(s.Kind, "null", StringComparison.OrdinalIgnoreCase)))
                    lines.Add($"- slot {slot.Index:D2}: VA=`{slot.VirtualAddress}` RVA=`{slot.Rva}` kind=`{slot.Kind}` section=`{slot.Section}`");
            }
            lines.Add("");
            lines.Add("## Authoring Boundary");
            lines.Add("");
            lines.Add("- Byte-preserving compile can re-emit the original DLL and apply exact byte/string/ASM patches.");
            lines.Add("- The generated C/ASM project can build a replacement DLL when a native x86 toolchain is installed.");
            lines.Add("- This is not a claim that machine code was recovered into the original C source; full semantic decompilation still needs IDA/Ghidra/manual work.");
            lines.Add("");
            if (info.Warnings.Count > 0)
            {
                lines.Add("## Warnings");
                lines.Add("");
                foreach (string warning in info.Warnings)
                    lines.Add($"- {warning}");
                lines.Add("");
            }
            return string.Join(Environment.NewLine, lines);
        }

        static string BuildDefFile(MagicDllInspection info)
        {
            List<string> lines = [];
            lines.Add("LIBRARY " + Path.GetFileNameWithoutExtension(info.FileName));
            lines.Add("EXPORTS");
            foreach (MagicDllExport export in info.Exports.OrderBy(e => e.Ordinal))
            {
                string name = string.IsNullOrWhiteSpace(export.Name) ? $"Ordinal_{export.Ordinal}" : SanitizeExportName(export.Name);
                lines.Add($"    {name} @{export.Ordinal}");
            }
            if (info.Exports.Count == 0)
                lines.Add("    InitMagicPRX @1");
            return string.Join(Environment.NewLine, lines) + Environment.NewLine;
        }

        static string BuildStubC(MagicDllInspection info)
        {
            List<string> exports = info.Exports
                .Select(e => string.IsNullOrWhiteSpace(e.Name) ? $"Ordinal_{e.Ordinal}" : SanitizeExportName(e.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (exports.Count == 0)
                exports.Add("InitMagicPRX");

            List<string> lines = [];
            lines.Add("#include <windows.h>");
            lines.Add("");
            lines.Add("BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)");
            lines.Add("{");
            lines.Add("    (void)instance; (void)reason; (void)reserved;");
            lines.Add("    return TRUE;");
            lines.Add("}");
            lines.Add("");
            lines.Add("/*");
            lines.Add("   Replacement project generated by FFX Project Editor.");
            lines.Add("   Fill these exports with real behavior only after the overlay slots and calling convention are understood.");
            lines.Add("*/");
            foreach (string export in exports)
            {
                lines.Add($"__declspec(dllexport) void __cdecl {export}(void)");
                lines.Add("{");
                lines.Add("    return;");
                lines.Add("}");
                lines.Add("");
            }
            return string.Join(Environment.NewLine, lines);
        }

        static string BuildPatchAsmTemplate(MagicDllInspection info) =>
            "; x86 patch snippets for Magic DLL LAB" + Environment.NewLine +
            "; Use patch_plan.template.json asmPatches to assemble snippets into exact file offsets." + Environment.NewLine +
            "; Example:" + Environment.NewLine +
            ";   nop" + Environment.NewLine +
            ";   ret" + Environment.NewLine +
            $"; Source: {info.FileName} SHA256 {info.Sha256}" + Environment.NewLine;

        static string BuildMsvcBatch(MagicDllInspection info) =>
            "@echo off" + Environment.NewLine +
            "setlocal" + Environment.NewLine +
            "set OUT=%~dp0rebuild.dll" + Environment.NewLine +
            "cl /nologo /LD /O2 /EHsc \"%~dp0magic_stub.c\" /Fe:\"%OUT%\" /link /DEF:\"%~dp0exports.def\" /MACHINE:X86 /OUT:\"%OUT%\"" + Environment.NewLine +
            "echo Built %OUT%" + Environment.NewLine;

        static string BuildNativeProjectReadme(MagicDllInspection info)
        {
            return string.Join(Environment.NewLine, new[]
            {
                "# Native C/ASM Rebuild Project",
                "",
                $"Source DLL: `{info.FileName}`",
                $"Source SHA256: `{info.Sha256}`",
                "",
                "This folder is a rebuild harness, not magic recovered source.",
                "",
                "- `exports.def` preserves the visible export surface.",
                "- `magic_stub.c` is the C replacement entrypoint/export scaffold.",
                "- `patches.asm` is for hand-written x86 snippets that can be assembled into patch plans.",
                "- `build_msvc_x86.bat` builds a replacement DLL when run from an x86/x64 Developer Command Prompt with MSVC tools.",
                "",
                "For exact preservation, use the byte-preserving compiler/repacker instead of the C stub."
            }) + Environment.NewLine;
        }

        static MagicDllOverlayEvidence? TryLoadOverlayEvidence(string dllPath, string? repoRoot)
        {
            int? magicId = TryParseMagicId(Path.GetFileNameWithoutExtension(dllPath));
            if (magicId == null)
                return null;

            string? root = repoRoot;
            if (string.IsNullOrWhiteSpace(root))
                root = FindRepoRoot(AppContext.BaseDirectory) ?? FindRepoRoot(Environment.CurrentDirectory);
            if (string.IsNullOrWhiteSpace(root))
                return null;

            string csvPath = Path.Combine(root, "docs", "reverse", "magicfiles_overlay_2026-06-03", "ffx_magic_overlay_tables.csv");
            if (!File.Exists(csvPath))
                return null;

            using StreamReader sr = new(csvPath);
            string? headerLine = sr.ReadLine();
            if (headerLine == null)
                return null;
            List<string> headers = SplitCsv(headerLine);

            string key = magicId.Value.ToString("D4", CultureInfo.InvariantCulture);
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                List<string> values = SplitCsv(line);
                Dictionary<string, string> row = headers
                    .Select((h, i) => new { h, v = i < values.Count ? values[i] : string.Empty })
                    .ToDictionary(x => x.h, x => x.v, StringComparer.OrdinalIgnoreCase);

                if (!row.TryGetValue("magic_id", out string? id) || !id.Equals(key, StringComparison.OrdinalIgnoreCase))
                    continue;

                List<MagicDllOverlaySlot> slots = [];
                for (int i = 0; i < 16; i++)
                {
                    string prefix = $"slot_{i:D2}_";
                    slots.Add(new MagicDllOverlaySlot(
                        i,
                        row.GetValueOrDefault(prefix + "va", string.Empty),
                        row.GetValueOrDefault(prefix + "rva", string.Empty),
                        row.GetValueOrDefault(prefix + "kind", string.Empty),
                        row.GetValueOrDefault(prefix + "section", string.Empty)));
                }

                return new MagicDllOverlayEvidence(
                    row.GetValueOrDefault("get_effect_overlay_ea", string.Empty),
                    row.GetValueOrDefault("init_magic_prx_ea", string.Empty),
                    row.GetValueOrDefault("table_ea", string.Empty),
                    row.GetValueOrDefault("table_rva", string.Empty),
                    row.GetValueOrDefault("slot_count_read", string.Empty),
                    row.GetValueOrDefault("nonzero_slot_count", string.Empty),
                    row.GetValueOrDefault("unique_target_count", string.Empty),
                    row.GetValueOrDefault("slot_kind_signature", string.Empty),
                    row.GetValueOrDefault("slot_section_signature", string.Empty),
                    row.GetValueOrDefault("host_offset_signature", string.Empty),
                    slots);
            }

            return null;
        }

        static List<string> SplitCsv(string line)
        {
            List<string> values = [];
            StringBuilder current = new();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = !quoted;
                    }
                }
                else if (c == ',' && !quoted)
                {
                    values.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            values.Add(current.ToString());
            return values;
        }

        static List<MagicDllAsciiString> ExtractAsciiStrings(byte[] bytes, IReadOnlyList<MagicDllSection> sections, int minLen, int maxCount)
        {
            List<MagicDllAsciiString> strings = [];
            int start = -1;
            for (int i = 0; i <= bytes.Length; i++)
            {
                bool printable = i < bytes.Length && bytes[i] >= 0x20 && bytes[i] <= 0x7E;
                if (printable)
                {
                    if (start < 0)
                        start = i;
                    continue;
                }

                if (start >= 0 && i - start >= minLen)
                {
                    string value = Encoding.ASCII.GetString(bytes, start, i - start);
                    MagicDllSection? section = sections.FirstOrDefault(s => start >= s.RawPointer && start < s.RawPointer + s.RawSize);
                    int rva = section == null ? 0 : section.VirtualAddress + (start - section.RawPointer);
                    strings.Add(new MagicDllAsciiString(start, rva, section?.Name ?? "headers/overlay", value));
                    if (strings.Count >= maxCount)
                        break;
                }
                start = -1;
            }
            return strings;
        }

        static List<string> BuildWarnings(PeReader reader, IReadOnlyList<MagicDllSection> sections, IReadOnlyList<MagicDllExport> exports, IReadOnlyList<MagicDllImportLibrary> imports, MagicDllOverlayEvidence? overlay)
        {
            List<string> warnings = [];
            if (!reader.IsValid)
                warnings.Add(reader.InvalidReason);
            if (!string.Equals(reader.MachineName, "x86", StringComparison.OrdinalIgnoreCase))
                warnings.Add("Expected x86 magic DLL, but machine is " + reader.MachineName + ".");
            if (exports.Count == 0)
                warnings.Add("No export table was decoded; runtime may use ordinal-only or external loader metadata.");
            if (imports.Count == 0)
                warnings.Add("No import table was decoded.");
            if (overlay == null)
                warnings.Add("No overlay table CSV row attached for this magic id.");
            if (sections.Count == 0)
                warnings.Add("No sections were decoded.");
            return warnings;
        }

        static int ResolvePatchOffset(MagicDllInspection info, int fileOffset, int rva)
        {
            if (fileOffset >= 0)
                return fileOffset;
            if (rva > 0)
                return info.RvaToFileOffset(rva);
            throw new InvalidOperationException("Patch must provide fileOffset or rva.");
        }

        static void ApplyBytes(byte[] bytes, int offset, byte[] payload)
        {
            if (offset < 0 || offset + payload.Length > bytes.Length)
                throw new InvalidOperationException($"Patch 0x{offset:X}+0x{payload.Length:X} is outside file length 0x{bytes.Length:X}.");
            Array.Copy(payload, 0, bytes, offset, payload.Length);
        }

        static byte[] AssembleX86(string assembly, ulong address, bool x64)
        {
            using Engine engine = new(Architecture.X86, x64 ? Mode.X64 : Mode.X32);
            engine.ThrowOnError = true;
            EncodedData data = engine.Assemble(assembly, address);
            return data.Buffer;
        }

        static byte[] ParseHexBytes(string? hex)
        {
            string clean = new((hex ?? string.Empty).Where(Uri.IsHexDigit).ToArray());
            if (clean.Length % 2 != 0)
                throw new FormatException("hex byte string has odd length.");
            byte[] bytes = new byte[clean.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = byte.Parse(clean.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return bytes;
        }

        static (int exitCode, string stdout, string stderr) RunProcess(string exe, string args, string workingDir)
        {
            ProcessStartInfo psi = new(exe, args)
            {
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using Process p = Process.Start(psi)!;
            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, stdout, stderr);
        }

        static string? FindOnPath(string exeName)
        {
            string? path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrWhiteSpace(path))
                return null;
            foreach (string dir in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir))
                    continue;
                string candidate = Path.Combine(dir.Trim(), exeName);
                if (File.Exists(candidate))
                    return candidate;
            }
            return null;
        }

        static string? FindRepoRoot(string start)
        {
            DirectoryInfo? dir = new DirectoryInfo(start);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "FFXProjectEditor.sln")) || File.Exists(Path.Combine(dir.FullName, "PORT_STATUS.md")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }

        static int? TryParseMagicId(string name)
        {
            if (name.StartsWith("magic_", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(name.AsSpan(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                return id;
            return null;
        }

        static string Sha256Hex(byte[] bytes) => string.Concat(SHA256.HashData(bytes).Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

        static string SanitizeFilePart(string value)
        {
            string sanitized = new(value.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? "section" : sanitized;
        }

        static string SanitizeExportName(string value)
        {
            StringBuilder sb = new();
            foreach (char ch in value)
            {
                if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_')
                    sb.Append(ch);
                else
                    sb.Append('_');
            }
            if (sb.Length == 0 || char.IsDigit(sb[0]))
                sb.Insert(0, "Export_");
            return sb.ToString();
        }

        static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true };

        sealed class PeReader
        {
            readonly byte[] bytes;
            int peOffset;
            int optionalOffset;
            int sectionOffset;
            int optionalSize;
            int sectionCount;
            int dataDirectoryOffset;
            int numberOfRvaAndSizes;

            public bool IsValid { get; }
            public string InvalidReason { get; } = string.Empty;
            public ushort Machine { get; }
            public ushort Characteristics { get; }
            public string MachineName => Machine switch
            {
                0x014C => "x86",
                0x8664 => "x64",
                _ => "unknown"
            };
            public bool IsPe32Plus { get; }
            public ulong ImageBase { get; }
            public int AddressOfEntryPoint { get; }
            public int SizeOfImage { get; }
            public int SectionAlignment { get; }
            public int FileAlignment { get; }
            public DateTimeOffset TimeDateStampUtc { get; }

            public PeReader(byte[] bytes)
            {
                this.bytes = bytes;
                try
                {
                    if (bytes.Length < 0x40 || ReadU16(0) != 0x5A4D)
                        throw new InvalidDataException("missing MZ header");
                    peOffset = ReadI32(0x3C);
                    if (peOffset <= 0 || peOffset + 0x18 >= bytes.Length || ReadU32(peOffset) != 0x00004550)
                        throw new InvalidDataException("missing PE signature");

                    Machine = ReadU16(peOffset + 4);
                    Characteristics = ReadU16(peOffset + 22);
                    sectionCount = ReadU16(peOffset + 6);
                    uint unixTime = ReadU32(peOffset + 8);
                    TimeDateStampUtc = DateTimeOffset.FromUnixTimeSeconds(unixTime);
                    optionalSize = ReadU16(peOffset + 20);
                    optionalOffset = peOffset + 24;
                    sectionOffset = optionalOffset + optionalSize;

                    ushort magic = ReadU16(optionalOffset);
                    IsPe32Plus = magic == 0x20B;
                    if (magic != 0x10B && magic != 0x20B)
                        throw new InvalidDataException($"unknown optional header magic 0x{magic:X4}");

                    AddressOfEntryPoint = ReadI32(optionalOffset + 16);
                    ImageBase = IsPe32Plus ? ReadU64(optionalOffset + 24) : ReadU32(optionalOffset + 28);
                    SectionAlignment = ReadI32(optionalOffset + 32);
                    FileAlignment = ReadI32(optionalOffset + 36);
                    SizeOfImage = ReadI32(optionalOffset + 56);
                    dataDirectoryOffset = optionalOffset + (IsPe32Plus ? 112 : 96);
                    numberOfRvaAndSizes = ReadI32(optionalOffset + (IsPe32Plus ? 108 : 92));

                    IsValid = true;
                }
                catch (Exception ex)
                {
                    IsValid = false;
                    InvalidReason = ex.Message;
                }
            }

            public List<MagicDllSection> ReadSections()
            {
                List<MagicDllSection> sections = [];
                if (!IsValid)
                    return sections;

                for (int i = 0; i < sectionCount; i++)
                {
                    int p = sectionOffset + i * 40;
                    if (p + 40 > bytes.Length)
                        break;
                    string name = ReadAsciiZ(p, 8);
                    int virtualSize = ReadI32(p + 8);
                    int virtualAddress = ReadI32(p + 12);
                    int rawSize = ReadI32(p + 16);
                    int rawPointer = ReadI32(p + 20);
                    uint characteristics = ReadU32(p + 36);
                    sections.Add(new MagicDllSection(name, virtualAddress, virtualSize, rawPointer, rawSize, characteristics));
                }
                return sections;
            }

            public List<MagicDllExport> ReadExports(IReadOnlyList<MagicDllSection> sections)
            {
                List<MagicDllExport> exports = [];
                if (!TryGetDirectory(ImageDirectoryEntryExport, out int exportRva, out int exportSize) || exportRva == 0 || exportSize == 0)
                    return exports;

                if (!TryRvaToOffset(sections, exportRva, out int exportOffset) || exportOffset + 40 > bytes.Length)
                    return exports;

                int baseOrdinal = ReadI32(exportOffset + 16);
                int functionCount = ReadI32(exportOffset + 20);
                int nameCount = ReadI32(exportOffset + 24);
                int functionsRva = ReadI32(exportOffset + 28);
                int namesRva = ReadI32(exportOffset + 32);
                int ordinalsRva = ReadI32(exportOffset + 36);

                if (!TryRvaToOffset(sections, functionsRva, out int functionsOffset))
                    return exports;
                TryRvaToOffset(sections, namesRva, out int namesOffset);
                TryRvaToOffset(sections, ordinalsRva, out int ordinalsOffset);

                Dictionary<int, string> namesByIndex = new();
                int safeNameCount = Math.Min(nameCount, 4096);
                for (int i = 0; i < safeNameCount; i++)
                {
                    int nameRva = ReadI32(namesOffset + i * 4);
                    ushort ordinalIndex = ReadU16(ordinalsOffset + i * 2);
                    if (TryRvaToOffset(sections, nameRva, out int nameOffset))
                        namesByIndex[ordinalIndex] = ReadAsciiZ(nameOffset, 512);
                }

                int safeFunctionCount = Math.Min(functionCount, 8192);
                for (int i = 0; i < safeFunctionCount; i++)
                {
                    int rva = ReadI32(functionsOffset + i * 4);
                    if (rva == 0)
                        continue;
                    int fileOffset = TryRvaToOffset(sections, rva, out int off) ? off : -1;
                    namesByIndex.TryGetValue(i, out string? name);
                    exports.Add(new MagicDllExport(baseOrdinal + i, name ?? string.Empty, rva, fileOffset));
                }
                return exports;
            }

            public List<MagicDllImportLibrary> ReadImports(IReadOnlyList<MagicDllSection> sections)
            {
                List<MagicDllImportLibrary> libraries = [];
                if (!TryGetDirectory(ImageDirectoryEntryImport, out int importRva, out int importSize) || importRva == 0 || importSize == 0)
                    return libraries;
                if (!TryRvaToOffset(sections, importRva, out int importOffset))
                    return libraries;

                for (int d = 0; d < 512; d++)
                {
                    int p = importOffset + d * 20;
                    if (p + 20 > bytes.Length)
                        break;
                    int originalFirstThunk = ReadI32(p);
                    int nameRva = ReadI32(p + 12);
                    int firstThunk = ReadI32(p + 16);
                    if (originalFirstThunk == 0 && nameRva == 0 && firstThunk == 0)
                        break;
                    string libName = TryRvaToOffset(sections, nameRva, out int nameOffset) ? ReadAsciiZ(nameOffset, 260) : $"rva_0x{nameRva:X}";
                    int thunkRva = originalFirstThunk != 0 ? originalFirstThunk : firstThunk;
                    List<MagicDllImport> imports = [];
                    if (TryRvaToOffset(sections, thunkRva, out int thunkOffset))
                    {
                        int step = IsPe32Plus ? 8 : 4;
                        ulong ordinalMask = IsPe32Plus ? 0x8000000000000000UL : 0x80000000UL;
                        for (int i = 0; i < 4096; i++)
                        {
                            int t = thunkOffset + i * step;
                            if (t + step > bytes.Length)
                                break;
                            ulong value = IsPe32Plus ? ReadU64(t) : ReadU32(t);
                            if (value == 0)
                                break;
                            int callThunkRva = firstThunk + i * step;
                            if ((value & ordinalMask) != 0)
                            {
                                imports.Add(new MagicDllImport(string.Empty, null, (ushort)(value & 0xFFFF), callThunkRva));
                            }
                            else if (TryRvaToOffset(sections, (int)value, out int importByNameOffset) && importByNameOffset + 2 < bytes.Length)
                            {
                                ushort hint = ReadU16(importByNameOffset);
                                string importName = ReadAsciiZ(importByNameOffset + 2, 512);
                                imports.Add(new MagicDllImport(importName, hint, null, callThunkRva));
                            }
                        }
                    }
                    libraries.Add(new MagicDllImportLibrary(libName, imports));
                }
                return libraries;
            }

            bool TryGetDirectory(int index, out int rva, out int size)
            {
                rva = 0;
                size = 0;
                if (!IsValid || index < 0 || index >= numberOfRvaAndSizes || dataDirectoryOffset + index * 8 + 8 > bytes.Length)
                    return false;
                rva = ReadI32(dataDirectoryOffset + index * 8);
                size = ReadI32(dataDirectoryOffset + index * 8 + 4);
                return true;
            }

            bool TryRvaToOffset(IReadOnlyList<MagicDllSection> sections, int rva, out int offset)
            {
                offset = 0;
                if (rva < 0)
                    return false;
                foreach (MagicDllSection s in sections)
                {
                    int span = Math.Max(s.VirtualSize, s.RawSize);
                    if (rva >= s.VirtualAddress && rva < s.VirtualAddress + span)
                    {
                        offset = s.RawPointer + (rva - s.VirtualAddress);
                        return offset >= 0 && offset <= bytes.Length;
                    }
                }
                if (rva < sectionOffset)
                {
                    offset = rva;
                    return true;
                }
                return false;
            }

            ushort ReadU16(int offset) => BitConverter.ToUInt16(bytes, offset);
            uint ReadU32(int offset) => BitConverter.ToUInt32(bytes, offset);
            ulong ReadU64(int offset) => BitConverter.ToUInt64(bytes, offset);
            int ReadI32(int offset) => BitConverter.ToInt32(bytes, offset);
            string ReadAsciiZ(int offset, int max)
            {
                int end = offset;
                int limit = Math.Min(bytes.Length, offset + max);
                while (end < limit && bytes[end] != 0)
                    end++;
                return offset >= 0 && offset < bytes.Length ? Encoding.ASCII.GetString(bytes, offset, Math.Max(0, end - offset)) : string.Empty;
            }
        }
    }

    internal sealed record MagicDllInspection(
        string FilePath,
        string FileName,
        int? MagicId,
        long FileSize,
        string Sha256,
        ushort Machine,
        string MachineName,
        ushort Characteristics,
        bool IsPe32Plus,
        ulong ImageBase,
        int EntryPointRva,
        int SizeOfImage,
        int SectionAlignment,
        int FileAlignment,
        DateTimeOffset TimeDateStampUtc,
        IReadOnlyList<MagicDllSection> Sections,
        IReadOnlyList<MagicDllExport> Exports,
        IReadOnlyList<MagicDllImportLibrary> Imports,
        IReadOnlyList<MagicDllAsciiString> Strings,
        MagicDllOverlayEvidence? OverlayEvidence,
        IReadOnlyList<string> Warnings)
    {
        public int RvaToFileOffset(int rva)
        {
            foreach (MagicDllSection s in Sections)
            {
                int span = Math.Max(s.VirtualSize, s.RawSize);
                if (rva >= s.VirtualAddress && rva < s.VirtualAddress + span)
                    return s.RawPointer + (rva - s.VirtualAddress);
            }
            throw new InvalidOperationException($"RVA 0x{rva:X} is outside decoded sections.");
        }

        public int FileOffsetToRva(int fileOffset)
        {
            foreach (MagicDllSection s in Sections)
            {
                if (fileOffset >= s.RawPointer && fileOffset < s.RawPointer + s.RawSize)
                    return s.VirtualAddress + (fileOffset - s.RawPointer);
            }
            throw new InvalidOperationException($"file offset 0x{fileOffset:X} is outside decoded sections.");
        }
    }

    internal sealed record MagicDllSection(string Name, int VirtualAddress, int VirtualSize, int RawPointer, int RawSize, uint Characteristics)
    {
        public string VirtualAddressDisplay => $"RVA 0x{VirtualAddress:X8}";
        public string VirtualSizeDisplay => $"VS 0x{VirtualSize:X}";
        public string RawPointerDisplay => $"RAW 0x{RawPointer:X8}";
        public string RawSizeDisplay => $"SIZE 0x{RawSize:X}";
    }
    internal sealed record MagicDllExport(int Ordinal, string Name, int Rva, int FileOffset)
    {
        public string OrdinalDisplay => $"@{Ordinal}";
        public string RvaDisplay => $"RVA 0x{Rva:X8}";
        public string FileOffsetDisplay => $"FILE 0x{FileOffset:X8}";
    }
    internal sealed record MagicDllImportLibrary(string Library, IReadOnlyList<MagicDllImport> Imports);
    internal sealed record MagicDllImport(string Name, ushort? Hint, ushort? Ordinal, int ThunkRva)
    {
        public string DisplayName => Ordinal.HasValue ? $"ordinal#{Ordinal.Value}" : Name;
    }
    internal sealed record MagicDllAsciiString(int FileOffset, int Rva, string Section, string Value)
    {
        public string FileOffsetDisplay => $"file 0x{FileOffset:X8}";
    }
    internal sealed record MagicDllOverlayEvidence(
        string GetEffectOverlayEa,
        string InitMagicPrxEa,
        string TableEa,
        string TableRva,
        string SlotCountRead,
        string NonzeroSlotCount,
        string UniqueTargetCount,
        string SlotKindSignature,
        string SlotSectionSignature,
        string HostOffsetSignature,
        IReadOnlyList<MagicDllOverlaySlot> Slots);
    internal sealed record MagicDllOverlaySlot(int Index, string VirtualAddress, string Rva, string Kind, string Section)
    {
        public string IndexDisplay => $"slot {Index:D2}";
    }
    internal sealed record MagicDllDecompileResult(
        MagicDllInspection Inspection,
        string ManifestPath,
        string MarkdownPath,
        string SectionsDir,
        string StringsPath,
        string ExportsPath,
        string ImportsPath,
        string PatchTemplatePath,
        string NativeProjectDir);
    internal sealed record MagicDllCompileResult(string SourceDll, string OutputDll, string SourceSha256, string OutputSha256, bool Pass, string Summary);
    internal sealed record MagicDllCloneResult(string SourceDll, string OutputDll, int NewMagicId, string SourceSha256, string OutputSha256, bool ByteIdentical);
    internal sealed record MagicDllNativeBuildResult(string ProjectDir, string OutputDll, bool Pass, string Summary, string Stdout, string Stderr);

    internal sealed class MagicDllPatchPlan
    {
        public List<MagicDllBytePatch> BytePatches { get; set; } = [];
        public List<MagicDllAsciiPatch> AsciiPatches { get; set; } = [];
        public List<MagicDllAsmPatch> AsmPatches { get; set; } = [];

        public static MagicDllPatchPlan Template() => new()
        {
            BytePatches =
            [
                new MagicDllBytePatch
                {
                    FileOffset = -1,
                    Rva = 0,
                    Hex = "90",
                    Note = "Exact bytes. Set fileOffset or rva before use."
                }
            ],
            AsciiPatches =
            [
                new MagicDllAsciiPatch
                {
                    FileOffset = -1,
                    Rva = 0,
                    Text = "NEW_TEXT",
                    MaxLength = 16,
                    NullTerminate = true,
                    Note = "Same-or-shorter ASCII replacement, padded with zeroes."
                }
            ],
            AsmPatches =
            [
                new MagicDllAsmPatch
                {
                    FileOffset = -1,
                    Rva = 0,
                    VirtualAddress = 0,
                    Assembly = "nop",
                    MaxLength = 1,
                    Note = "Keystone x86/x64 assembly patch. Pads with NOPs when MaxLength is larger."
                }
            ]
        };
    }

    internal sealed class MagicDllBytePatch
    {
        public int FileOffset { get; set; } = -1;
        public int Rva { get; set; }
        public string Hex { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
    }

    internal sealed class MagicDllAsciiPatch
    {
        public int FileOffset { get; set; } = -1;
        public int Rva { get; set; }
        public string Text { get; set; } = string.Empty;
        public int MaxLength { get; set; }
        public bool NullTerminate { get; set; } = true;
        public string Note { get; set; } = string.Empty;
    }

    internal sealed class MagicDllAsmPatch
    {
        public int FileOffset { get; set; } = -1;
        public int Rva { get; set; }
        public ulong VirtualAddress { get; set; }
        public string Assembly { get; set; } = string.Empty;
        public int MaxLength { get; set; }
        public string Note { get; set; } = string.Empty;
    }
}

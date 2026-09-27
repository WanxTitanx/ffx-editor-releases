using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MagicCorpusIndexer;

public static class Program
{
    private const string DefaultFfxPs2Root = @"D:\FFX Extracted\FFX\ffx_ps2\ffx";
    private const string DefaultPs3DataRoot = @"D:\FFX Extracted\FFX\ffx_data\GameData\PS3Data";
    private const string DefaultPs3PngMagicRoot = @"D:\FFX Mods\ps3data_textures_png\magic";
    private const string DefaultMagicFilesRoot = @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\magicFiles\FFX";
    private const string DefaultOutputRoot = @"RuntimeTools\FFXMagicViewerWeb\public\magic";
    private const string DefaultOverlaySummaryPath = @"docs\reverse\magicfiles_overlay_2026-06-03\ffx_magic_overlay_summary.json";
    private const string DefaultOverlayTableCsvPath = @"docs\reverse\magicfiles_overlay_2026-06-03\ffx_magic_overlay_tables.csv";
    private const string DefaultExeBridgeCsvPath = @"docs\reverse\magicfiles_exe_bridge_2026-06-03\ffx_magicfiles_exe_functions_9da_9db.csv";
    private const string DefaultMagicCaptureLabRoot = @"work\magic_capture_lab";
    private const int MaxPreviewCopiesPerEntry = 6;
    private const int MaxTm2PreviewCopiesPerEntry = 4;

    private static readonly JsonSerializerOptions OutputJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static int Main(string[] args)
    {
        try
        {
            var options = ParseOptions(args);
            return RunIndex(options);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int RunIndex(IReadOnlyDictionary<string, string> options)
    {
        var ffxPs2Root = Path.GetFullPath(GetOption(options, "ffx-ps2-root", DefaultFfxPs2Root));
        var ps3DataRoot = Path.GetFullPath(GetOption(options, "ps3data-root", DefaultPs3DataRoot));
        var ps3PngMagicRoot = Path.GetFullPath(GetOption(options, "ps3-png-magic-root", DefaultPs3PngMagicRoot));
        var magicFilesRoot = Path.GetFullPath(GetOption(options, "magicfiles-root", DefaultMagicFilesRoot));
        var overlaySummaryPath = Path.GetFullPath(GetOption(options, "overlay-summary", DefaultOverlaySummaryPath));
        var overlayTableCsvPath = Path.GetFullPath(GetOption(options, "overlay-table-csv", DefaultOverlayTableCsvPath));
        var exeBridgeCsvPath = Path.GetFullPath(GetOption(options, "exe-bridge-csv", DefaultExeBridgeCsvPath));
        var magicCaptureLabRoot = Path.GetFullPath(GetOption(options, "magic-capture-lab-root", DefaultMagicCaptureLabRoot));
        var outputRoot = Path.GetFullPath(GetOption(options, "output", DefaultOutputRoot));

        Directory.CreateDirectory(outputRoot);
        Directory.CreateDirectory(Path.Combine(outputRoot, "thumbs"));

        var kernelIndex = BuildKernelIndex(ffxPs2Root);
        var ps2Packages = BuildPs2PackageIndex(ffxPs2Root, outputRoot);
        var batEff = BuildBatEffSummary(ffxPs2Root);
        var ps3Magic = BuildPs3MagicIndex(ps3DataRoot, ps3PngMagicRoot, outputRoot);
        var overlayIndex = BuildOverlayIndex(overlaySummaryPath, overlayTableCsvPath);
        var runtimeDlls = BuildRuntimeDllIndex(magicFilesRoot, overlayIndex);
        var runtimeEvidence = BuildRuntimeEvidence(overlayIndex.Summary, exeBridgeCsvPath);
        var captureEvidence = BuildCaptureEvidenceIndex(magicCaptureLabRoot);
        var entries = BuildCatalogEntries(ps2Packages, ps3Magic, overlayIndex, runtimeDlls, batEff, captureEvidence, outputRoot);

        var catalog = new MagicViewerCatalog(
            DateTimeOffset.UtcNow,
            "magic-viewer-catalog-v1",
            "RuntimeTools/MagicCorpusIndexer",
            new CatalogSummary(
                entries.Length,
                entries.Count(entry => entry.DecisionBand == "multi_lane_candidate"),
                entries.Count(entry => entry.Ps2Package != null),
                entries.Count(entry => entry.Ps3Magic != null),
                entries.Count(entry => entry.Overlay != null),
                entries.Count(entry => entry.RuntimeDll != null),
                entries.Count(entry => entry.PreviewUrls.Length > 0 || (entry.Ps2Package?.Tm2Previews.Any(preview => !string.IsNullOrWhiteSpace(preview.PreviewUrl)) ?? false))),
            kernelIndex,
            batEff,
            runtimeEvidence,
            entries);

        WriteJson(Path.Combine(outputRoot, "kernel-index.json"), kernelIndex);
        WriteJson(Path.Combine(outputRoot, "mag-package-index.json"), ps2Packages.Values.OrderBy(value => value.MagicId, StringComparer.OrdinalIgnoreCase).ToArray());
        WriteJson(Path.Combine(outputRoot, "bat-eff-index.json"), batEff);
        WriteJson(Path.Combine(outputRoot, "ps3-magic-index.json"), ps3Magic.Values.OrderBy(value => value.MagicId, StringComparer.OrdinalIgnoreCase).ToArray());
        WriteJson(Path.Combine(outputRoot, "magicfiles-overlay-index.json"), overlayIndex);
        WriteJson(Path.Combine(outputRoot, "runtime-dll-index.json"), runtimeDlls.Values.OrderBy(value => value.MagicId, StringComparer.OrdinalIgnoreCase).ToArray());
        WriteJson(Path.Combine(outputRoot, "runtime-evidence-index.json"), runtimeEvidence);
        WriteJson(Path.Combine(outputRoot, "capture-evidence-index.json"), captureEvidence.Values.OrderBy(value => value.MagicId, StringComparer.OrdinalIgnoreCase).ToArray());
        WriteJson(Path.Combine(outputRoot, "magic-viewer-catalog.json"), catalog);
        WriteCsv(Path.Combine(outputRoot, "magic-viewer-catalog.csv"), entries);

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            outputRoot,
            entryCount = entries.Length,
            decisionBands = entries.GroupBy(entry => entry.DecisionBand).ToDictionary(group => group.Key, group => group.Count()),
            hdPreviewsCopied = entries.Sum(entry => entry.PreviewUrls.Length),
            ps2Tm2Previews = entries.Sum(entry => entry.Ps2Package?.Tm2Previews.Count(preview => !string.IsNullOrWhiteSpace(preview.PreviewUrl)) ?? 0),
            overlayRows = overlayIndex.Rows.Length,
            ps2PackageCount = ps2Packages.Count,
            ps3MagicCount = ps3Magic.Count,
            runtimeDllCount = runtimeDlls.Count,
        }, OutputJsonOptions));

        return 0;
    }

    private static KernelIndex BuildKernelIndex(string ffxPs2Root)
    {
        var kernelRoot = Path.Combine(ffxPs2Root, "master", "jppc", "battle", "kernel");
        var files = new List<KernelFileRecord>();
        foreach (var fileName in new[]
                 {
                     "magic.bin",
                     "monmagic1.bin",
                     "monmagic2.bin",
                     "command.bin",
                     "a_ability.bin",
                     "c_ability.bin",
                 })
        {
            var path = Path.Combine(kernelRoot, fileName);
            if (!File.Exists(path))
            {
                continue;
            }

            var info = new FileInfo(path);
            files.Add(new KernelFileRecord(
                fileName,
                path,
                Path.GetRelativePath(ffxPs2Root, path).Replace('\\', '/'),
                info.Length,
                ReadHeadHex(path),
                fileName switch
                {
                    "magic.bin" => "Magic identity table lane.",
                    "monmagic1.bin" => "Monster-side magic selector lane #1.",
                    "monmagic2.bin" => "Monster-side magic selector lane #2.",
                    "command.bin" => "Battle command-side context lane.",
                    _ => "Ability-side context lane.",
                }));
        }

        return new KernelIndex(kernelRoot, 0, 1023, files.ToArray());
    }

    private static Dictionary<string, Ps2PackageRecord> BuildPs2PackageIndex(string ffxPs2Root, string outputRoot)
    {
        var packagesRoot = Path.Combine(ffxPs2Root, "yonishi_data", "dat_ov");
        var result = new Dictionary<string, Ps2PackageRecord>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(packagesRoot))
        {
            return result;
        }

        foreach (var directory in Directory.EnumerateDirectories(packagesRoot, "mag_*", SearchOption.TopDirectoryOnly))
        {
            var folderName = Path.GetFileName(directory);
            var magicId = folderName["mag_".Length..];
            var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var extCounts = files
                .GroupBy(path => Path.GetExtension(path).ToLowerInvariant())
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

            var hasBin = extCounts.ContainsKey(".bin");
            var hasTm2 = extCounts.ContainsKey(".tm2");
            var hasAnm = extCounts.ContainsKey(".anm") || extCounts.ContainsKey(".an2");
            var hasModel = extCounts.ContainsKey(".rsd") || extCounts.ContainsKey(".ply") || extCounts.ContainsKey(".ma2") || extCounts.ContainsKey(".omd") || extCounts.ContainsKey(".grp");
            var hasParticle = extCounts.ContainsKey(".ha") || extCounts.ContainsKey(".h") || extCounts.ContainsKey(".otp");
            var richnessScore = CountTrue(hasBin, hasTm2, hasAnm, hasModel, hasParticle);

            var richness = !hasBin
                ? "anomaly_poor"
                : richnessScore >= 4 ? "rich"
                : richnessScore >= 2 ? "medium"
                : "poor";

            var sampleFiles = files
                .Take(20)
                .Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/'))
                .ToArray();
            var tm2Previews = BuildPs2Tm2Previews(directory, files, outputRoot, folderName);
            var modelCandidates = BuildPs2ModelCandidates(directory, files, ffxPs2Root, outputRoot, folderName);

            result[magicId] = new Ps2PackageRecord(
                magicId,
                folderName,
                directory,
                Path.GetRelativePath(ffxPs2Root, directory).Replace('\\', '/'),
                files.Length,
                files.Sum(path => new FileInfo(path).Length),
                richness,
                extCounts,
                sampleFiles,
                hasTm2,
                hasAnm,
                hasModel,
                hasParticle,
                tm2Previews,
                modelCandidates);
        }

        return result;
    }

    private static BatEffSummary BuildBatEffSummary(string ffxPs2Root)
    {
        var path = Path.Combine(ffxPs2Root, "yonishi_data", "dat_et", "bat_eff");
        if (!Directory.Exists(path))
        {
            return new BatEffSummary(path, Array.Empty<string>(), new Dictionary<string, int>(), Array.Empty<string>(), Array.Empty<string>());
        }

        var files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var extCounts = files
            .GroupBy(file => Path.GetExtension(file).ToLowerInvariant())
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var stems = files
            .Select(file => Path.GetFileNameWithoutExtension(file))
            .Where(stem => !string.IsNullOrWhiteSpace(stem))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(stem => stem, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var duplicateNames = files
            .GroupBy(file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new BatEffSummary(
            path,
            stems,
            extCounts,
            files.Take(40).Select(file => Path.GetRelativePath(path, file).Replace('\\', '/')).ToArray(),
            duplicateNames);
    }

    private static Dictionary<string, Ps3MagicRecord> BuildPs3MagicIndex(string ps3DataRoot, string ps3PngMagicRoot, string outputRoot)
    {
        var magicRoot = Path.Combine(ps3DataRoot, "magic");
        var result = new Dictionary<string, Ps3MagicRecord>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(magicRoot))
        {
            return result;
        }

        foreach (var directory in Directory.EnumerateDirectories(magicRoot, "magic_*", SearchOption.TopDirectoryOnly))
        {
            var folderName = Path.GetFileName(directory);
            var magicId = folderName["magic_".Length..];
            var ddsFiles = Directory.EnumerateFiles(directory, "*.dds.phyre", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var sourcePreviewDir = Path.Combine(ps3PngMagicRoot, folderName);
            var previewUrls = CopyPreviewImages(sourcePreviewDir, outputRoot, folderName);

            result[magicId] = new Ps3MagicRecord(
                magicId,
                folderName,
                directory,
                Path.GetRelativePath(ps3DataRoot, directory).Replace('\\', '/'),
                ddsFiles.Length,
                ddsFiles.Take(12).Select(file => Path.GetRelativePath(directory, file).Replace('\\', '/')).ToArray(),
                previewUrls);
        }

        return result;
    }

    private static Dictionary<string, RuntimeMagicDllRecord> BuildRuntimeDllIndex(string magicFilesRoot, OverlayIndex overlayIndex)
    {
        var result = new Dictionary<string, RuntimeMagicDllRecord>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(magicFilesRoot))
        {
            return result;
        }

        var overlayById = overlayIndex.Rows.ToDictionary(row => row.MagicId, StringComparer.OrdinalIgnoreCase);
        foreach (var dllPath in Directory.EnumerateFiles(magicFilesRoot, "magic_*.dll", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var magicId = TryParseMagicDllId(Path.GetFileNameWithoutExtension(dllPath));
            if (string.IsNullOrWhiteSpace(magicId))
            {
                continue;
            }

            overlayById.TryGetValue(magicId, out var overlay);
            try
            {
                result[magicId] = InspectRuntimeMagicDll(dllPath, magicFilesRoot, overlay);
            }
            catch (Exception ex)
            {
                var bytes = SafeReadAllBytes(dllPath);
                result[magicId] = new RuntimeMagicDllRecord(
                    magicId,
                    Path.GetFileName(dllPath),
                    dllPath,
                    Path.GetRelativePath(magicFilesRoot, dllPath).Replace('\\', '/'),
                    bytes.LongLength,
                    bytes.Length == 0 ? string.Empty : Sha256Hex(bytes),
                    "unreadable",
                    "unknown",
                    string.Empty,
                    "0x0",
                    "0x0",
                    0,
                    0,
                    Array.Empty<RuntimeDllSectionRecord>(),
                    Array.Empty<RuntimeDllExportRecord>(),
                    Array.Empty<RuntimeDllImportLibraryRecord>(),
                    0,
                    0,
                    0,
                    Array.Empty<string>(),
                    Array.Empty<RuntimeDllStringFamilyRecord>(),
                    overlay != null,
                    overlay?.Slots ?? Array.Empty<OverlaySlotRecord>(),
                    [$"PE inspect failed: {ex.GetType().Name}: {ex.Message}"]);
            }
        }

        return result;
    }

    private static RuntimeMagicDllRecord InspectRuntimeMagicDll(string dllPath, string magicFilesRoot, OverlayRow? overlay)
    {
        var bytes = File.ReadAllBytes(dllPath);
        var warnings = new List<string>();
        if (bytes.Length < 0x100 || bytes[0] != (byte)'M' || bytes[1] != (byte)'Z')
        {
            throw new InvalidDataException("DLL does not start with an MZ header.");
        }

        var peOffset = PeI32(bytes, 0x3C);
        if (!HasRange(bytes, peOffset, 0x18) || bytes[peOffset] != (byte)'P' || bytes[peOffset + 1] != (byte)'E')
        {
            throw new InvalidDataException("PE signature was not found.");
        }

        var coffOffset = peOffset + 4;
        var machine = PeU16(bytes, coffOffset);
        var sectionCount = PeU16(bytes, coffOffset + 2);
        var timestamp = PeU32(bytes, coffOffset + 4);
        var optionalHeaderSize = PeU16(bytes, coffOffset + 16);
        var optionalOffset = coffOffset + 20;
        if (!HasRange(bytes, optionalOffset, optionalHeaderSize))
        {
            throw new InvalidDataException("Optional header is truncated.");
        }

        var optionalMagic = PeU16(bytes, optionalOffset);
        var isPe32Plus = optionalMagic == 0x20B;
        var isPe32 = optionalMagic == 0x10B;
        if (!isPe32 && !isPe32Plus)
        {
            warnings.Add($"Unexpected optional header magic 0x{optionalMagic:X4}.");
        }

        var entryPointRva = PeI32(bytes, optionalOffset + 16);
        var imageBase = isPe32Plus ? PeU64(bytes, optionalOffset + 24) : PeU32(bytes, optionalOffset + 28);
        var sizeOfImage = PeI32(bytes, optionalOffset + 56);
        var dataDirectoryOffset = optionalOffset + (isPe32Plus ? 112 : 96);
        var exportRva = HasRange(bytes, dataDirectoryOffset, 8) ? PeI32(bytes, dataDirectoryOffset) : 0;
        var importRva = HasRange(bytes, dataDirectoryOffset + 8, 8) ? PeI32(bytes, dataDirectoryOffset + 8) : 0;
        var sectionOffset = optionalOffset + optionalHeaderSize;
        var sections = ReadPeSections(bytes, sectionOffset, sectionCount);
        var exports = ReadPeExports(bytes, sections, exportRva).Take(64).ToArray();
        var imports = ReadPeImports(bytes, sections, importRva, isPe32Plus).ToArray();
        var strings = ExtractAsciiStrings(bytes, sections, 4, 600);
        var stringFamilies = BuildRuntimeStringFamilies(strings).ToArray();
        var engineStrings = strings.Where(IsInterestingRuntimeString).Select(value => value.Value).Distinct(StringComparer.Ordinal).Take(16).ToArray();
        var sampleStrings = engineStrings.Length > 0
            ? engineStrings
            : strings.Select(value => value.Value).Distinct(StringComparer.Ordinal).Take(12).ToArray();
        var importCount = imports.Sum(library => library.Count);

        if (exports.All(export => !export.Name.Equals("GetEffectOverlayTable", StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add("GetEffectOverlayTable export not named in this PE export table; ordinal export may still be used.");
        }
        if (exports.All(export => !export.Name.Equals("InitMagicPRX", StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add("InitMagicPRX export not named in this PE export table; ordinal export may still be used.");
        }
        if (overlay == null)
        {
            warnings.Add("No overlay CSV row attached for this DLL id.");
        }

        return new RuntimeMagicDllRecord(
            TryParseMagicDllId(Path.GetFileNameWithoutExtension(dllPath)) ?? string.Empty,
            Path.GetFileName(dllPath),
            dllPath,
            Path.GetRelativePath(magicFilesRoot, dllPath).Replace('\\', '/'),
            bytes.LongLength,
            Sha256Hex(bytes),
            MachineName(machine),
            isPe32Plus ? "PE32+" : "PE32",
            DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            $"0x{imageBase:X}",
            $"0x{entryPointRva:X}",
            sizeOfImage,
            sections.Length,
            sections,
            exports,
            imports,
            importCount,
            strings.Length,
            strings.Count(value => IsInterestingRuntimeString(value)),
            sampleStrings,
            stringFamilies,
            overlay != null,
            overlay?.Slots ?? Array.Empty<OverlaySlotRecord>(),
            warnings.ToArray());
    }

    private static RuntimeDllSectionRecord[] ReadPeSections(byte[] bytes, int sectionOffset, int sectionCount)
    {
        var sections = new List<RuntimeDllSectionRecord>();
        for (var index = 0; index < sectionCount; index++)
        {
            var offset = sectionOffset + index * 40;
            if (!HasRange(bytes, offset, 40))
            {
                break;
            }

            var nameBytes = bytes.Skip(offset).Take(8).TakeWhile(value => value != 0).ToArray();
            var name = Encoding.ASCII.GetString(nameBytes);
            sections.Add(new RuntimeDllSectionRecord(
                name,
                $"0x{PeI32(bytes, offset + 12):X}",
                PeI32(bytes, offset + 8),
                PeI32(bytes, offset + 20),
                PeI32(bytes, offset + 16),
                $"0x{PeU32(bytes, offset + 36):X8}"));
        }

        return sections.ToArray();
    }

    private static RuntimeDllExportRecord[] ReadPeExports(byte[] bytes, IReadOnlyList<RuntimeDllSectionRecord> sections, int exportRva)
    {
        if (exportRva == 0 || !TryPeRvaToOffset(sections, exportRva, out var exportOffset) || !HasRange(bytes, exportOffset, 40))
        {
            return Array.Empty<RuntimeDllExportRecord>();
        }

        var ordinalBase = PeI32(bytes, exportOffset + 16);
        var functionCount = PeI32(bytes, exportOffset + 20);
        var nameCount = PeI32(bytes, exportOffset + 24);
        var functionsRva = PeI32(bytes, exportOffset + 28);
        var namesRva = PeI32(bytes, exportOffset + 32);
        var ordinalsRva = PeI32(bytes, exportOffset + 36);
        if (!TryPeRvaToOffset(sections, functionsRva, out var functionsOffset))
        {
            return Array.Empty<RuntimeDllExportRecord>();
        }

        var namesByIndex = new Dictionary<int, string>();
        if (TryPeRvaToOffset(sections, namesRva, out var namesOffset)
            && TryPeRvaToOffset(sections, ordinalsRva, out var ordinalsOffset))
        {
            for (var index = 0; index < Math.Min(nameCount, 512); index++)
            {
                var nameRvaOffset = namesOffset + index * 4;
                var ordinalOffset = ordinalsOffset + index * 2;
                if (!HasRange(bytes, nameRvaOffset, 4) || !HasRange(bytes, ordinalOffset, 2))
                {
                    break;
                }

                var nameRva = PeI32(bytes, nameRvaOffset);
                var ordinalIndex = PeU16(bytes, ordinalOffset);
                if (TryPeRvaToOffset(sections, nameRva, out var nameOffset))
                {
                    namesByIndex[ordinalIndex] = ReadAsciiZ(bytes, nameOffset, 180);
                }
            }
        }

        var exports = new List<RuntimeDllExportRecord>();
        for (var index = 0; index < Math.Min(functionCount, 512); index++)
        {
            var functionOffset = functionsOffset + index * 4;
            if (!HasRange(bytes, functionOffset, 4))
            {
                break;
            }

            var rva = PeI32(bytes, functionOffset);
            TryPeRvaToOffset(sections, rva, out var fileOffset);
            namesByIndex.TryGetValue(index, out var name);
            exports.Add(new RuntimeDllExportRecord(
                ordinalBase + index,
                name ?? string.Empty,
                $"0x{rva:X}",
                fileOffset >= 0 ? $"0x{fileOffset:X}" : string.Empty));
        }

        return exports.ToArray();
    }

    private static RuntimeDllImportLibraryRecord[] ReadPeImports(byte[] bytes, IReadOnlyList<RuntimeDllSectionRecord> sections, int importRva, bool isPe32Plus)
    {
        if (importRva == 0 || !TryPeRvaToOffset(sections, importRva, out var importOffset))
        {
            return Array.Empty<RuntimeDllImportLibraryRecord>();
        }

        var libraries = new List<RuntimeDllImportLibraryRecord>();
        for (var descriptorOffset = importOffset; HasRange(bytes, descriptorOffset, 20) && libraries.Count < 64; descriptorOffset += 20)
        {
            var originalThunk = PeI32(bytes, descriptorOffset);
            var nameRva = PeI32(bytes, descriptorOffset + 12);
            var firstThunk = PeI32(bytes, descriptorOffset + 16);
            if (originalThunk == 0 && nameRva == 0 && firstThunk == 0)
            {
                break;
            }

            var library = TryPeRvaToOffset(sections, nameRva, out var nameOffset)
                ? ReadAsciiZ(bytes, nameOffset, 260)
                : $"rva_0x{nameRva:X}";
            var thunkRva = originalThunk != 0 ? originalThunk : firstThunk;
            var imports = new List<string>();
            if (TryPeRvaToOffset(sections, thunkRva, out var thunkOffset))
            {
                var step = isPe32Plus ? 8 : 4;
                var ordinalMask = isPe32Plus ? 0x8000000000000000UL : 0x80000000UL;
                for (var t = thunkOffset; HasRange(bytes, t, step) && imports.Count < 48; t += step)
                {
                    var value = isPe32Plus ? PeU64(bytes, t) : PeU32(bytes, t);
                    if (value == 0)
                    {
                        break;
                    }
                    if ((value & ordinalMask) != 0)
                    {
                        imports.Add($"Ordinal_{value & 0xFFFF:X}");
                    }
                    else if (TryPeRvaToOffset(sections, (int)value, out var importByNameOffset) && HasRange(bytes, importByNameOffset, 3))
                    {
                        imports.Add(ReadAsciiZ(bytes, importByNameOffset + 2, 220));
                    }
                }
            }

            libraries.Add(new RuntimeDllImportLibraryRecord(library, imports.Count, imports.Take(24).ToArray()));
        }

        return libraries.ToArray();
    }

    private static RuntimeDllStringRecord[] ExtractAsciiStrings(byte[] bytes, IReadOnlyList<RuntimeDllSectionRecord> sections, int minLen, int maxCount)
    {
        var strings = new List<RuntimeDllStringRecord>();
        var start = -1;
        for (var index = 0; index <= bytes.Length; index++)
        {
            var current = index < bytes.Length ? bytes[index] : (byte)0;
            var printable = current >= 0x20 && current <= 0x7E;
            if (printable)
            {
                if (start < 0)
                {
                    start = index;
                }
                continue;
            }

            if (start >= 0 && index - start >= minLen)
            {
                var value = Encoding.ASCII.GetString(bytes, start, index - start);
                var section = sections.FirstOrDefault(s => start >= s.RawPointer && start < s.RawPointer + s.RawSize);
                strings.Add(new RuntimeDllStringRecord(start, section?.Name ?? string.Empty, value));
                if (strings.Count >= maxCount)
                {
                    break;
                }
            }

            start = -1;
        }

        return strings.ToArray();
    }

    private static IEnumerable<RuntimeDllStringFamilyRecord> BuildRuntimeStringFamilies(IReadOnlyList<RuntimeDllStringRecord> strings)
    {
        return strings
            .GroupBy(value => ClassifyRuntimeString(value.Value), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new RuntimeDllStringFamilyRecord(
                group.Key,
                group.Count(),
                group.Select(value => value.Value).Distinct(StringComparer.Ordinal).Take(6).ToArray()));
    }

    private static string ClassifyRuntimeString(string value)
    {
        if (value.StartsWith("ppp", StringComparison.Ordinal)) return "PPP particle/resource";
        if (value.StartsWith("Ego", StringComparison.Ordinal)) return "Ego runtime object";
        if (value.Contains("SeSep", StringComparison.OrdinalIgnoreCase)) return "SeSep/sound cue";
        if (value.Contains("Virtuos", StringComparison.OrdinalIgnoreCase) || value.Contains("Yonishi", StringComparison.OrdinalIgnoreCase)) return "diagnostic/runtime warning";
        if (value.Contains(".phyre", StringComparison.OrdinalIgnoreCase) || value.Contains(".dds", StringComparison.OrdinalIgnoreCase)) return "asset path";
        if (value.Contains("%", StringComparison.Ordinal) || value.Contains("error", StringComparison.OrdinalIgnoreCase)) return "diagnostic/format";
        return "other";
    }

    private static bool IsInterestingRuntimeString(RuntimeDllStringRecord value)
        => IsInterestingRuntimeString(value.Value);

    private static bool IsInterestingRuntimeString(string value)
        => value.StartsWith("ppp", StringComparison.Ordinal)
           || value.StartsWith("Ego", StringComparison.Ordinal)
           || value.Contains("SeSep", StringComparison.OrdinalIgnoreCase)
           || value.Contains("Virtuos", StringComparison.OrdinalIgnoreCase)
           || value.Contains("Yonishi", StringComparison.OrdinalIgnoreCase)
           || value.Contains(".phyre", StringComparison.OrdinalIgnoreCase)
           || value.Contains(".dds", StringComparison.OrdinalIgnoreCase);

    private static string[] CopyPreviewImages(string sourcePreviewDir, string outputRoot, string folderName)
    {
        if (!Directory.Exists(sourcePreviewDir))
        {
            return Array.Empty<string>();
        }

        var pngFiles = Directory.EnumerateFiles(sourcePreviewDir, "*.png", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(MaxPreviewCopiesPerEntry)
            .ToArray();

        if (pngFiles.Length == 0)
        {
            return Array.Empty<string>();
        }

        var targetDir = Path.Combine(outputRoot, "thumbs", folderName);
        Directory.CreateDirectory(targetDir);

        var urls = new List<string>();
        for (var index = 0; index < pngFiles.Length; index++)
        {
            var sourcePath = pngFiles[index];
            var fileName = $"{index:00}_{Path.GetFileName(sourcePath)}";
            var targetPath = Path.Combine(targetDir, fileName);
            File.Copy(sourcePath, targetPath, true);
            urls.Add(Path.GetRelativePath(outputRoot, targetPath).Replace('\\', '/'));
        }

        return urls.ToArray();
    }

    private static OverlayIndex BuildOverlayIndex(string summaryPath, string tableCsvPath)
    {
        var summary = File.Exists(summaryPath)
            ? JsonSerializer.Deserialize<OverlaySummary>(File.ReadAllText(summaryPath), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            }) ?? OverlaySummary.Empty
            : OverlaySummary.Empty;

        if (!File.Exists(tableCsvPath))
        {
            return new OverlayIndex(summary, Array.Empty<OverlayRow>());
        }

        var rows = ReadCsvRows(tableCsvPath)
            .Select(values => new OverlayRow(
                GetValue(values, "magic_id"),
                GetValue(values, "dll_name"),
                ParseInt(GetValue(values, "slot_count_read")),
                ParseInt(GetValue(values, "nonzero_slot_count")),
                ParseInt(GetValue(values, "unique_target_count")),
                GetValue(values, "slot_kind_signature"),
                GetValue(values, "host_offset_signature"),
                GetValue(values, "table_rva"),
                BuildOverlaySlots(values)))
            .Where(row => !string.IsNullOrWhiteSpace(row.MagicId))
            .ToArray();

        return new OverlayIndex(summary, rows);
    }

    private static OverlaySlotRecord[] BuildOverlaySlots(IReadOnlyDictionary<string, string> row)
    {
        var slots = new List<OverlaySlotRecord>();
        for (var index = 0; index < 16; index++)
        {
            var prefix = $"slot_{index:D2}";
            var kind = GetValue(row, $"{prefix}_kind");
            var role = DescribeOverlaySlot(index, kind);
            slots.Add(new OverlaySlotRecord(
                index,
                GetValue(row, $"{prefix}_va"),
                GetValue(row, $"{prefix}_rva"),
                kind,
                GetValue(row, $"{prefix}_section"),
                role.Name,
                role.Confidence,
                role.Evidence));
        }

        return slots.ToArray();
    }

    private static OverlaySlotRole DescribeOverlaySlot(int index, string kind)
    {
        if (kind.Equals("null", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(kind))
        {
            return new OverlaySlotRole("OverlaySlotUnused", "high", "Null slot in the extracted GetEffectOverlayTable surface.");
        }

        if (kind.Equals("data", StringComparison.OrdinalIgnoreCase))
        {
            return new OverlaySlotRole("OverlayResourceOrRecordTable", "medium", "Data pointer in the callback surface; often the resource/table side rather than executable callback code.");
        }

        return index switch
        {
            0 => new("OverlayEntryInitCreate", "medium-high", "Primary DLL entry callback candidate for creating/registering local effect state and PPP/Ego resources."),
            1 => new("OverlayEarlyTickOrNoopA", "medium", "Early phase callback candidate; often a small no-op in simple effects."),
            2 => new("OverlayEarlyTickOrNoopB", "medium", "Early phase sibling candidate; repeated RVA aliases are common."),
            3 => new("OverlayPhaseGateOrPreRoutine", "medium-high", "Phase gate/pre-routine callback candidate from sampled DLL passes."),
            4 => new("OverlayMainUpdateOrRoutine", "medium-high", "Main per-frame/update routine candidate for effect task state machines."),
            5 => new("OverlayLateTickOrNoopA", "medium", "Late phase callback candidate; frequently no-op in small effects."),
            6 => new("OverlayLateTickOrNoopB", "medium", "Late phase sibling candidate; frequently aliases another no-op."),
            8 => new("OverlayOptionalFamilyCallback0", "medium", "Optional family-specific callback candidate."),
            9 => new("OverlayOptionalFamilyCallback1", "medium", "Optional family-specific callback candidate."),
            10 => new("OverlayOptionalFamilyCallback2", "medium", "Optional family-specific callback candidate."),
            11 => new("OverlayOptionalFamilyCallback3", "medium", "Optional family-specific callback candidate."),
            12 => new("OverlayOptionalFamilyCallback4", "medium", "Optional family-specific callback candidate."),
            13 => new("OverlayOptionalFamilyCallback5", "medium", "Optional family-specific callback candidate."),
            14 => new("OverlayOptionalFamilyCallback6", "medium", "Optional family-specific callback candidate."),
            15 => new("OverlayTailNoopOrCleanup", "medium", "Tail callback candidate; commonly a no-op/default return path in simple effects."),
            _ => new($"OverlaySlot{index:D2}Callback", "low", "Callback slot with not-yet-specialized semantic role."),
        };
    }

    private static RuntimeEvidence BuildRuntimeEvidence(OverlaySummary summary, string exeBridgeCsvPath)
    {
        var functions = File.Exists(exeBridgeCsvPath)
            ? ReadCsvRows(exeBridgeCsvPath)
                .Select(values => new ExeBridgeFunction(
                    GetValue(values, "start"),
                    GetValue(values, "name"),
                    ParseIntHexAware(GetValue(values, "size")),
                    ParseInt(GetValue(values, "code_refs_to")),
                    ParseInt(GetValue(values, "code_refs_from")),
                    ParseInt(GetValue(values, "unique_callees"))))
                .Where(row => !string.IsNullOrWhiteSpace(row.Start))
                .ToArray()
            : Array.Empty<ExeBridgeFunction>();

        return new RuntimeEvidence(summary, functions);
    }

    private static Dictionary<string, CaptureEvidenceRecord> BuildCaptureEvidenceIndex(string magicCaptureLabRoot)
    {
        var result = new Dictionary<string, CaptureEvidenceRecord>(StringComparer.OrdinalIgnoreCase);
        var hashIndexPath = Path.Combine(magicCaptureLabRoot, "magic_hash_index.csv");
        if (File.Exists(hashIndexPath))
        {
            foreach (var group in ReadCsvRows(hashIndexPath)
                         .Where(row => !string.IsNullOrWhiteSpace(GetAnyValue(row, "magic_id", "magic")))
                         .GroupBy(row => NormalizeMagicId(GetAnyValue(row, "magic_id", "magic")), StringComparer.OrdinalIgnoreCase))
            {
                var samplePaths = group
                    .Select(row => GetAnyValue(row, "path", "rel"))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Take(12)
                    .ToArray();
                var dims = group
                    .Select(row =>
                    {
                        var width = GetAnyValue(row, "width", "w");
                        var height = GetAnyValue(row, "height", "h");
                        return !string.IsNullOrWhiteSpace(width) && !string.IsNullOrWhiteSpace(height)
                            ? $"{width}x{height}"
                            : string.Empty;
                    })
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                result[group.Key] = new CaptureEvidenceRecord(
                    group.Key,
                    group.Count(),
                    dims,
                    samplePaths,
                    null,
                    false,
                    "hash_inventory_ready_no_recipe");
            }
        }

        var viewerRoot = Path.Combine(magicCaptureLabRoot, "viewer");
        if (Directory.Exists(viewerRoot))
        {
            foreach (var recipePath in Directory.EnumerateFiles(viewerRoot, "recipe_*.js", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileNameWithoutExtension(recipePath);
                var magicId = fileName.Split('_').LastOrDefault();
                if (string.IsNullOrWhiteSpace(magicId))
                {
                    continue;
                }

                var normalized = NormalizeMagicId(magicId);
                result.TryGetValue(normalized, out var existing);
                result[normalized] = existing is null
                    ? new CaptureEvidenceRecord(normalized, 0, Array.Empty<string>(), Array.Empty<string>(), Path.GetRelativePath(magicCaptureLabRoot, recipePath).Replace('\\', '/'), true, "recipe_script_present")
                    : existing with
                    {
                        RecipeScriptPath = Path.GetRelativePath(magicCaptureLabRoot, recipePath).Replace('\\', '/'),
                        HasRecipeScript = true,
                        EvidenceBand = "recipe_script_present",
                    };
            }
        }

        return result;
    }

    private static MagicCatalogEntry[] BuildCatalogEntries(
        IReadOnlyDictionary<string, Ps2PackageRecord> ps2Packages,
        IReadOnlyDictionary<string, Ps3MagicRecord> ps3Magic,
        OverlayIndex overlayIndex,
        IReadOnlyDictionary<string, RuntimeMagicDllRecord> runtimeDlls,
        BatEffSummary batEff,
        IReadOnlyDictionary<string, CaptureEvidenceRecord> captureEvidence,
        string outputRoot)
    {
        var allIds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ps2Packages.Keys) allIds.Add(id);
        foreach (var id in ps3Magic.Keys) allIds.Add(id);
        foreach (var row in overlayIndex.Rows) allIds.Add(row.MagicId);
        foreach (var id in runtimeDlls.Keys) allIds.Add(id);
        foreach (var id in captureEvidence.Keys) allIds.Add(id);

        var overlayById = overlayIndex.Rows.ToDictionary(row => row.MagicId, StringComparer.OrdinalIgnoreCase);
        var entries = new List<MagicCatalogEntry>();

        foreach (var magicId in allIds.OrderBy(value => ParseSortableId(value)).ThenBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            ps2Packages.TryGetValue(magicId, out var package);
            ps3Magic.TryGetValue(magicId, out var ps3);
            overlayById.TryGetValue(magicId, out var overlay);
            runtimeDlls.TryGetValue(magicId, out var runtimeDll);
            captureEvidence.TryGetValue(magicId, out var capture);

            var sharedAnchors = package == null
                ? Array.Empty<string>()
                : package.SampleFiles
                    .Select(path => Path.GetFileNameWithoutExtension(path))
                    .Where(name => !string.IsNullOrWhiteSpace(name) && batEff.Stems.Contains(name, StringComparer.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            var decisionBand = InferDecisionBand(package, ps3, overlay, runtimeDll);
            var laneTags = new[]
            {
                package != null ? "ps2_package" : null,
                ps3 != null ? "ps3_magic" : null,
                runtimeDll != null ? "runtime_dll_reader" : null,
                overlay != null ? "magicfiles_overlay" : null,
                sharedAnchors.Length > 0 ? "shared_bat_eff_anchor" : null,
            }.Where(value => value != null).Cast<string>().ToArray();

            var previewUrls = ps3?.PreviewUrls ?? Array.Empty<string>();
            var coverPreviewUrl = previewUrls.FirstOrDefault() ?? package?.Tm2Previews.FirstOrDefault(preview => !string.IsNullOrWhiteSpace(preview.PreviewUrl))?.PreviewUrl;
            var crosswalk = BuildCrosswalk(package, ps3, overlay, runtimeDll, capture, sharedAnchors);

            entries.Add(new MagicCatalogEntry(
                magicId,
                $"magic_{magicId}",
                decisionBand,
                laneTags,
                package,
                ps3,
                overlay,
                runtimeDll,
                capture,
                previewUrls,
                sharedAnchors,
                crosswalk,
                new EntrySources(
                    package?.RelativePath,
                    ps3?.RelativePath,
                    runtimeDll?.RelativePath,
                    runtimeDll?.DllName ?? overlay?.DllName,
                    coverPreviewUrl)));
        }

        return entries.ToArray();
    }

    private static string[] BuildCrosswalk(Ps2PackageRecord? package, Ps3MagicRecord? ps3, OverlayRow? overlay, RuntimeMagicDllRecord? runtimeDll, CaptureEvidenceRecord? capture, string[] sharedAnchors)
    {
        var rows = new List<string>();
        rows.Add("magic id stays in the documented 0..1023 kernel namespace");

        if (package != null)
        {
            rows.Add($"PS2 package present: {package.PackageClass} / {package.FileCount} files");
            if (package.Tm2Previews.Length > 0)
            {
                rows.Add($"PS2 TM2 previews ready: {package.Tm2Previews.Count(preview => !string.IsNullOrWhiteSpace(preview.PreviewUrl))}/{package.Tm2Previews.Length}");
            }
            if (package.ModelCandidates.Length > 0)
            {
                rows.Add($"PS2 model carriers present: {package.ModelCandidates.Length} RSD bundle candidates");
            }
        }
        else
        {
            rows.Add("PS2 package missing in dat_ov/mag_*");
        }

        if (ps3 != null)
        {
            rows.Add($"PS3 HD folder present: {ps3.TextureCount} dds.phyre textures");
        }
        else
        {
            rows.Add("PS3 HD folder missing in ps3data/magic");
        }

        if (overlay != null)
        {
            rows.Add($"magicFiles overlay present: {overlay.NonzeroSlotCount}/{overlay.SlotCountRead} nonzero slots, signature {overlay.SlotKindSignature}");
        }
        else
        {
            rows.Add("magicFiles overlay missing in the local batch index");
        }

        if (runtimeDll != null)
        {
            rows.Add($"runtime DLL read from disk: {runtimeDll.DllName}, {runtimeDll.PeKind}, {runtimeDll.ExportCount} exports, {runtimeDll.ImportCount} imports, {runtimeDll.SectionCount} sections");
            if (runtimeDll.EngineStringCount > 0)
            {
                rows.Add($"runtime DLL engine strings: {runtimeDll.EngineStringCount} PPP/Ego/asset/diagnostic strings indexed");
            }
        }
        else
        {
            rows.Add("runtime DLL missing/unread in magicFiles/FFX");
        }

        if (sharedAnchors.Length > 0)
        {
            rows.Add($"shared bat_eff anchors: {string.Join(", ", sharedAnchors)}");
        }
        else
        {
            rows.Add("no shared bat_eff anchor names proved for this entry");
        }

        if (capture != null)
        {
            rows.Add($"capture lab inventory: {capture.HashIndexedTextureCount} indexed textures / recipe script {(capture.HasRecipeScript ? "present" : "missing")}");
        }
        else
        {
            rows.Add("capture lab pilot evidence missing for this entry");
        }

        rows.Add("causal bridge kernel -> mag_* -> bat_eff remains blocked");
        rows.Add("overlay slot semantics remain blocked");
        return rows.ToArray();
    }

    private static string InferDecisionBand(Ps2PackageRecord? package, Ps3MagicRecord? ps3, OverlayRow? overlay, RuntimeMagicDllRecord? runtimeDll)
    {
        if (package != null && ps3 != null && (overlay != null || runtimeDll != null))
        {
            return "multi_lane_candidate";
        }

        if (ps3 != null && (overlay != null || runtimeDll != null))
        {
            return "hd_runtime_candidate";
        }

        if (package != null)
        {
            return "ps2_package_candidate";
        }

        if (overlay != null || runtimeDll != null)
        {
            return "overlay_only_candidate";
        }

        return "blocked_no_known_lane";
    }

    private static void WriteCsv(string path, IReadOnlyList<MagicCatalogEntry> entries)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("magic_id,decision_band,ps2_package,ps3_magic,runtime_dll,magicfiles_overlay,preview_count,ps2_tm2_preview_count,ps2_model_candidate_count,shared_bat_eff_anchor_count");
        foreach (var entry in entries)
        {
            writer.WriteLine(string.Join(',',
                entry.MagicId,
                entry.DecisionBand,
                entry.Ps2Package != null ? "1" : "0",
                entry.Ps3Magic != null ? "1" : "0",
                entry.RuntimeDll != null ? "1" : "0",
                entry.Overlay != null ? "1" : "0",
                entry.PreviewUrls.Length.ToString(CultureInfo.InvariantCulture),
                (entry.Ps2Package?.Tm2Previews.Length ?? 0).ToString(CultureInfo.InvariantCulture),
                (entry.Ps2Package?.ModelCandidates.Length ?? 0).ToString(CultureInfo.InvariantCulture),
                entry.SharedBatEffAnchors.Length.ToString(CultureInfo.InvariantCulture)));
        }
    }

    private static Ps2Tm2PreviewRecord[] BuildPs2Tm2Previews(string packageDirectory, IReadOnlyList<string> files, string outputRoot, string folderName)
    {
        var tm2Files = files
            .Where(path => string.Equals(Path.GetExtension(path), ".tm2", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(MaxTm2PreviewCopiesPerEntry)
            .ToArray();

        if (tm2Files.Length == 0)
        {
            return Array.Empty<Ps2Tm2PreviewRecord>();
        }

        var targetDir = Path.Combine(outputRoot, "tm2-preview", folderName);
        Directory.CreateDirectory(targetDir);

        var previews = new List<Ps2Tm2PreviewRecord>();
        for (var index = 0; index < tm2Files.Length; index++)
        {
            var tm2Path = tm2Files[index];
            var relativePath = Path.GetRelativePath(packageDirectory, tm2Path).Replace('\\', '/');
            if (!TryReadTm2(tm2Path, out var metadata))
            {
                continue;
            }

            string? previewUrl = null;
            if (metadata.PreviewState is "native" or "experimental" && TryBuildTm2PixelBuffer(tm2Path, metadata, out var pixelBuffer))
            {
                var previewFileName = $"{index:00}_{Path.GetFileNameWithoutExtension(tm2Path)}.bmp";
                var previewPath = Path.Combine(targetDir, previewFileName);
                WriteBmp32(previewPath, metadata.Width, metadata.Height, pixelBuffer);
                previewUrl = Path.GetRelativePath(outputRoot, previewPath).Replace('\\', '/');
            }

            previews.Add(new Ps2Tm2PreviewRecord(
                Path.GetFileName(tm2Path),
                relativePath,
                metadata.PreviewState,
                metadata.EvidenceLabel,
                metadata.Variant,
                metadata.Width,
                metadata.Height,
                metadata.ColorCount,
                metadata.Warnings,
                previewUrl));
        }

        return previews.ToArray();
    }

    private static Ps2ModelCandidateRecord[] BuildPs2ModelCandidates(string packageDirectory, IReadOnlyList<string> files, string ffxPs2Root, string outputRoot, string folderName)
    {
        var rsdFiles = files
            .Where(path => string.Equals(Path.GetExtension(path), ".rsd", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (rsdFiles.Length == 0)
        {
            return Array.Empty<Ps2ModelCandidateRecord>();
        }

        var candidates = new List<Ps2ModelCandidateRecord>();
        var previewDir = Path.Combine(outputRoot, "rsd-preview", folderName);
        Directory.CreateDirectory(previewDir);

        foreach (var rsdPath in rsdFiles)
        {
            if (!TryReadRsdBundle(rsdPath, ffxPs2Root, out var bundle))
            {
                continue;
            }

            string? previewUrl = null;
            if (TryBuildRsdPreview(bundle, out var previewBuffer, out var previewWidth, out var previewHeight))
            {
                var previewFileName = $"{bundle.Name}.bmp";
                var previewPath = Path.Combine(previewDir, previewFileName);
                WriteBmp32(previewPath, previewWidth, previewHeight, previewBuffer);
                previewUrl = Path.GetRelativePath(outputRoot, previewPath).Replace('\\', '/');
            }

            candidates.Add(new Ps2ModelCandidateRecord(
                bundle.Name,
                Path.GetRelativePath(packageDirectory, rsdPath).Replace('\\', '/'),
                bundle.Lane,
                bundle.Status,
                bundle.PlyName,
                bundle.MatName,
                bundle.VertexCount,
                bundle.PolygonCount,
                bundle.MaterialCount,
                bundle.TextureNames.Length,
                bundle.TexturesResolved,
                previewUrl));
        }

        return candidates.ToArray();
    }

    private static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, OutputJsonOptions));
    }

    private static IReadOnlyList<Dictionary<string, string>> ReadCsvRows(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0)
        {
            return Array.Empty<Dictionary<string, string>>();
        }

        var header = SplitCsvLine(lines[0]);
        var rows = new List<Dictionary<string, string>>();
        for (var index = 1; index < lines.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                continue;
            }

            var values = SplitCsvLine(lines[index]);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var column = 0; column < header.Count; column++)
            {
                row[header[column]] = column < values.Count ? values[column] : string.Empty;
            }
            rows.Add(row);
        }

        return rows;
    }

    private static List<string> SplitCsvLine(string line)
    {
        var values = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var ch = line[index];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (index + 1 < line.Length && line[index + 1] == '"')
                    {
                        current.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(ch);
                }
            }
            else
            {
                if (ch == ',')
                {
                    values.Add(current.ToString());
                    current.Clear();
                }
                else if (ch == '"')
                {
                    inQuotes = true;
                }
                else
                {
                    current.Append(ch);
                }
            }
        }

        values.Add(current.ToString());
        return values;
    }

    private static int CountTrue(params bool[] values) => values.Count(value => value);

    private static int ParseSortableId(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : int.MaxValue;
    private static int ParseInt(string? value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    private static int ParseIntHexAware(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsedHex) ? parsedHex : 0;
        }

        return ParseInt(value);
    }

    private static string GetValue(IReadOnlyDictionary<string, string> row, string key) => row.TryGetValue(key, out var value) ? value : string.Empty;

    private static string GetAnyValue(IReadOnlyDictionary<string, string> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (row.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string ReadHeadHex(string path)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[16];
        var read = stream.Read(buffer, 0, buffer.Length);
        return read == 0 ? "-" : BitConverter.ToString(buffer[..read]).Replace('-', ' ');
    }

    private static byte[] SafeReadAllBytes(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }

    private static string Sha256Hex(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static bool HasRange(byte[] bytes, int offset, int length)
        => offset >= 0 && length >= 0 && offset <= bytes.Length - length;

    private static ushort PeU16(byte[] bytes, int offset)
        => HasRange(bytes, offset, 2) ? BitConverter.ToUInt16(bytes, offset) : (ushort)0;

    private static uint PeU32(byte[] bytes, int offset)
        => HasRange(bytes, offset, 4) ? BitConverter.ToUInt32(bytes, offset) : 0;

    private static int PeI32(byte[] bytes, int offset)
        => HasRange(bytes, offset, 4) ? BitConverter.ToInt32(bytes, offset) : 0;

    private static ulong PeU64(byte[] bytes, int offset)
        => HasRange(bytes, offset, 8) ? BitConverter.ToUInt64(bytes, offset) : 0;

    private static bool TryPeRvaToOffset(IReadOnlyList<RuntimeDllSectionRecord> sections, int rva, out int offset)
    {
        foreach (var section in sections)
        {
            if (!TryParseHexInt(section.VirtualAddress, out var virtualAddress))
            {
                continue;
            }

            var size = Math.Max(section.VirtualSize, section.RawSize);
            if (rva >= virtualAddress && rva < virtualAddress + size)
            {
                offset = section.RawPointer + (rva - virtualAddress);
                return true;
            }
        }

        offset = -1;
        return false;
    }

    private static bool TryParseHexInt(string value, out int parsed)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed);
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
    }

    private static string ReadAsciiZ(byte[] bytes, int offset, int maxLength)
    {
        if (offset < 0 || offset >= bytes.Length)
        {
            return string.Empty;
        }

        var count = 0;
        while (count < maxLength && offset + count < bytes.Length && bytes[offset + count] != 0)
        {
            count++;
        }

        return count == 0 ? string.Empty : Encoding.ASCII.GetString(bytes, offset, count);
    }

    private static string MachineName(ushort machine) => machine switch
    {
        0x014C => "x86",
        0x8664 => "x64",
        0x01C0 => "ARM",
        0xAA64 => "ARM64",
        _ => $"0x{machine:X4}",
    };

    private static string? TryParseMagicDllId(string name)
    {
        if (name.StartsWith("magic_", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name.AsSpan(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            return id.ToString("D4", CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static bool TryReadTm2(string path, out Tm2PreviewMetadata metadata)
    {
        metadata = default;
        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch
        {
            return false;
        }

        if (data.Length < 0x28 || data[0] != (byte)'T' || data[1] != (byte)'I' || data[2] != (byte)'M' || data[3] != (byte)'2')
        {
            return false;
        }

        var paletteBytes = ReadU32(data, 0x14);
        var imageBytes = ReadU32(data, 0x18);
        var colorCount = ReadU16(data, 0x1E);
        var bppish = data[0x23];
        var width = ReadU16(data, 0x24);
        var height = ReadU16(data, 0x26);
        var matchesFormula = data.LongLength == 0x40L + imageBytes + paletteBytes;

        string previewState;
        string evidenceLabel;
        string variant;
        string? blockedReason;
        if (paletteBytes == 1024
            && colorCount == 256
            && bppish == 5
            && imageBytes == (uint)(width * height)
            && matchesFormula)
        {
            previewState = "native";
            evidenceLabel = "proved";
            variant = "indexed_8bpp_candidate";
            blockedReason = null;
        }
        else if (paletteBytes == 64
                 && colorCount == 16
                 && bppish == 4
                 && imageBytes * 2 == width * height
                 && matchesFormula)
        {
            previewState = "experimental";
            evidenceLabel = "structural";
            variant = "indexed_4bpp_candidate";
            blockedReason = null;
        }
        else if (paletteBytes == 0
                 && colorCount == 0
                 && bppish == 1)
        {
            previewState = "blocked";
            evidenceLabel = "structural";
            variant = "direct_or_no_clut_candidate";
            blockedReason = "Direct-color path remains blocked.";
        }
        else
        {
            previewState = "metadata_only";
            evidenceLabel = "guess";
            variant = "other_or_unknown";
            blockedReason = matchesFormula ? null : "Layout mismatch.";
        }

        var warnings = new List<string>
        {
            "Preview only.",
            "No writer, repacker, or source mutation.",
            "Alpha/channel order is still not fully validated.",
            "Swizzle and CLUT order may still be wrong.",
        };

        if (!matchesFormula)
        {
            warnings.Add("Layout formula mismatch: keep this as metadata or blocked, not final decode.");
        }

        metadata = new Tm2PreviewMetadata(
            width,
            height,
            colorCount,
            imageBytes,
            paletteBytes,
            previewState,
            evidenceLabel,
            variant,
            blockedReason,
            warnings.ToArray());
        return true;
    }

    private static bool TryBuildTm2PixelBuffer(string path, Tm2PreviewMetadata metadata, out byte[] pixelBuffer)
    {
        pixelBuffer = Array.Empty<byte>();
        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch
        {
            return false;
        }

        var width = metadata.Width;
        var height = metadata.Height;
        if (width <= 0 || height <= 0 || data.Length < 0x40)
        {
            return false;
        }

        pixelBuffer = new byte[width * height * 4];
        const int imageOffset = 0x40;
        var clutOffset = imageOffset + checked((int)metadata.ImageBytes);

        if (metadata.PreviewState == "native")
        {
            if (data.Length < clutOffset + metadata.PaletteBytes)
            {
                pixelBuffer = Array.Empty<byte>();
                return false;
            }

            for (var index = 0; index < width * height; index++)
            {
                var paletteIndex = data[imageOffset + index];
                WritePaletteColor(data, clutOffset, paletteIndex, pixelBuffer, index * 4);
            }

            return true;
        }

        if (metadata.PreviewState == "experimental")
        {
            if (data.Length < clutOffset + metadata.PaletteBytes)
            {
                pixelBuffer = Array.Empty<byte>();
                return false;
            }

            var pixelIndex = 0;
            for (var sourceIndex = 0; sourceIndex < metadata.ImageBytes && pixelIndex < width * height; sourceIndex++)
            {
                var packed = data[imageOffset + sourceIndex];
                var low = packed & 0x0F;
                var high = (packed >> 4) & 0x0F;

                WritePaletteColor(data, clutOffset, low, pixelBuffer, pixelIndex * 4);
                pixelIndex++;
                if (pixelIndex >= width * height)
                {
                    break;
                }

                WritePaletteColor(data, clutOffset, high, pixelBuffer, pixelIndex * 4);
                pixelIndex++;
            }

            return true;
        }

        pixelBuffer = Array.Empty<byte>();
        return false;
    }

    private static void WritePaletteColor(byte[] data, int clutOffset, int paletteIndex, byte[] pixelBuffer, int targetOffset)
    {
        var sourceOffset = clutOffset + (paletteIndex * 4);
        if (sourceOffset + 3 >= data.Length)
        {
            return;
        }

        pixelBuffer[targetOffset + 0] = data[sourceOffset + 0];
        pixelBuffer[targetOffset + 1] = data[sourceOffset + 1];
        pixelBuffer[targetOffset + 2] = data[sourceOffset + 2];
        pixelBuffer[targetOffset + 3] = NormalizePs2Alpha(data[sourceOffset + 3]);
    }

    private static byte NormalizePs2Alpha(byte raw)
    {
        if (raw == 0)
        {
            return 0;
        }

        var expanded = raw * 2;
        return (byte)Math.Min(255, expanded);
    }

    private static void WriteBmp32(string path, int width, int height, byte[] pixelBuffer)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        const int fileHeaderSize = 14;
        const int infoHeaderSize = 40;
        var imageSize = pixelBuffer.Length;
        var fileSize = fileHeaderSize + infoHeaderSize + imageSize;

        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(fileSize);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(fileHeaderSize + infoHeaderSize);

        writer.Write(infoHeaderSize);
        writer.Write(width);
        writer.Write(-height);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);
        writer.Write(imageSize);
        writer.Write(2835);
        writer.Write(2835);
        writer.Write(0);
        writer.Write(0);
        writer.Write(pixelBuffer);
    }

    private static bool TryReadRsdBundle(string rsdPath, string root, out RsdModelBundle bundle)
    {
        bundle = default;
        try
        {
            var lines = File.ReadAllLines(rsdPath);
            if (lines.Length == 0 || !lines[0].StartsWith("@RSD", StringComparison.Ordinal))
            {
                return false;
            }

            var ply = string.Empty;
            var mat = string.Empty;
            var grp = string.Empty;
            var vgr = string.Empty;
            var textures = new List<string>();
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.StartsWith("PLY=", StringComparison.OrdinalIgnoreCase)) ply = line[4..].Trim();
                else if (line.StartsWith("MAT=", StringComparison.OrdinalIgnoreCase)) mat = line[4..].Trim();
                else if (line.StartsWith("GRP=", StringComparison.OrdinalIgnoreCase)) grp = line[4..].Trim();
                else if (line.StartsWith("VGR=", StringComparison.OrdinalIgnoreCase)) vgr = line[4..].Trim();
                else if (line.StartsWith("TEX[", StringComparison.OrdinalIgnoreCase))
                {
                    var eq = line.IndexOf('=');
                    if (eq >= 0)
                    {
                        var tex = line[(eq + 1)..].Trim();
                        if (tex.Length > 0)
                        {
                            textures.Add(tex);
                        }
                    }
                }
            }

            var dir = Path.GetDirectoryName(rsdPath) ?? string.Empty;
            var plyResolved = ply.Length > 0 && File.Exists(Path.Combine(dir, ply));
            var matResolved = mat.Length > 0 && File.Exists(Path.Combine(dir, mat));
            var (vertices, normals, polygons) = plyResolved ? ReadPlyCounts(Path.Combine(dir, ply)) : (0, 0, 0);
            var materialCount = matResolved ? ReadMatCount(Path.Combine(dir, mat)) : 0;
            var timDir = Path.Combine(dir, "..", "tim");
            var plyFullPath = plyResolved ? Path.Combine(dir, ply) : string.Empty;
            var texturesResolved = textures.Count(tex => File.Exists(Path.Combine(timDir, tex)));

            var lower = rsdPath.ToLowerInvariant();
            var lane = lower.Contains("encount2") ? "encount2"
                : lower.Contains("encount") ? "encount"
                : lower.Contains("mag_") ? "mag"
                : "other";
            var fullyLinked = plyResolved && matResolved && textures.Count > 0 && texturesResolved == textures.Count;

            bundle = new RsdModelBundle(
                Path.GetFileNameWithoutExtension(rsdPath),
                rsdPath,
                Path.GetRelativePath(root, rsdPath).Replace('\\', '/'),
                lane,
                ply,
                plyFullPath,
                mat,
                grp,
                vgr,
                textures.ToArray(),
                vertices,
                normals,
                polygons,
                materialCount,
                plyResolved,
                matResolved,
                texturesResolved,
                fullyLinked ? "proved" : "structural");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryBuildRsdPreview(RsdModelBundle bundle, out byte[] pixelBuffer, out int width, out int height)
    {
        pixelBuffer = Array.Empty<byte>();
        width = 0;
        height = 0;

        if (!TryReadPlyGeometry(bundle.PlyFullPath, out var vertices, out var polygons) || vertices.Length == 0 || polygons.Count == 0)
        {
            return false;
        }

        width = 256;
        height = 256;
        pixelBuffer = new byte[width * height * 4];
        FillColor(pixelBuffer, 14, 18, 24, 255);

        var boundsMin = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var boundsMax = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        foreach (var vertex in vertices)
        {
            boundsMin = Vector3.Min(boundsMin, vertex);
            boundsMax = Vector3.Max(boundsMax, vertex);
        }

        var center = (boundsMin + boundsMax) * 0.5f;
        var size = boundsMax - boundsMin;
        var radius = Math.Max(Math.Max(size.X, size.Y), size.Z);
        if (radius <= 0.0001f)
        {
            return false;
        }

        var yaw = Matrix4x4.CreateRotationY(-0.65f);
        var pitch = Matrix4x4.CreateRotationX(0.48f);
        var tilt = Matrix4x4.CreateRotationZ(0.18f);
        var rotation = yaw * pitch * tilt;

        var transformed = new Vector3[vertices.Length];
        var projected = new Vector2[vertices.Length];
        var minProjected = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var maxProjected = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

        for (var index = 0; index < vertices.Length; index++)
        {
            var local = vertices[index] - center;
            var rotated = Vector3.Transform(local, rotation);
            transformed[index] = rotated;
            var p = new Vector2(rotated.X, -rotated.Y);
            minProjected = Vector2.Min(minProjected, p);
            maxProjected = Vector2.Max(maxProjected, p);
            projected[index] = p;
        }

        var projectedSize = maxProjected - minProjected;
        var scale = MathF.Min(
            (width * 0.72f) / MathF.Max(projectedSize.X, 0.001f),
            (height * 0.72f) / MathF.Max(projectedSize.Y, 0.001f));
        var offset = new Vector2(width * 0.5f, height * 0.56f);

        var screen = new ScreenVertex[vertices.Length];
        for (var index = 0; index < projected.Length; index++)
        {
            screen[index] = new ScreenVertex(
                offset.X + projected[index].X * scale,
                offset.Y + projected[index].Y * scale,
                transformed[index].Z);
        }

        var zBuffer = Enumerable.Repeat(float.PositiveInfinity, width * height).ToArray();
        var light = Vector3.Normalize(new Vector3(0.35f, 0.7f, 0.62f));
        var triangleJobs = new List<PreviewTriangle>(polygons.Count * 2);
        foreach (var polygon in polygons)
        {
            if (polygon.Length < 3)
            {
                continue;
            }

            var faceVertices = polygon.Select(index => transformed[index]).ToArray();
            var faceCenter = faceVertices.Aggregate(Vector3.Zero, static (sum, value) => sum + value) / faceVertices.Length;
            var faceNormal = Vector3.Normalize(Vector3.Cross(faceVertices[1] - faceVertices[0], faceVertices[2] - faceVertices[0]));
            if (!IsFinite(faceNormal))
            {
                continue;
            }

            var shade = Math.Clamp(0.24f + MathF.Abs(Vector3.Dot(faceNormal, light)) * 0.76f, 0.18f, 1f);
            var color = ShadeColor(82, 164, 224, shade);
            var outline = ShadeColor(215, 236, 255, Math.Clamp(shade + 0.18f, 0.32f, 1f));

            for (var index = 1; index < polygon.Length - 1; index++)
            {
                triangleJobs.Add(new PreviewTriangle(
                    screen[polygon[0]],
                    screen[polygon[index]],
                    screen[polygon[index + 1]],
                    faceCenter.Z,
                    color,
                    outline));
            }
        }

        foreach (var triangle in triangleJobs.OrderBy(job => job.Depth))
        {
            RasterizeTriangle(pixelBuffer, zBuffer, width, height, triangle);
        }

        DrawFrame(pixelBuffer, width, height);
        return true;
    }

    private static bool TryReadPlyGeometry(string plyPath, out Vector3[] vertices, out List<int[]> polygons)
    {
        vertices = Array.Empty<Vector3>();
        polygons = new List<int[]>();
        try
        {
            var lines = File.ReadAllLines(plyPath);
            if (lines.Length < 5 || !lines[0].StartsWith("@PLY", StringComparison.Ordinal))
            {
                return false;
            }

            var counts = lines[3].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (counts.Length < 3
                || !int.TryParse(counts[0], out var vertexCount)
                || !int.TryParse(counts[2], out var polygonCount))
            {
                return false;
            }

            var cursor = 5;
            vertices = new Vector3[vertexCount];
            for (var index = 0; index < vertexCount && cursor < lines.Length; index++, cursor++)
            {
                var parts = lines[cursor].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3)
                {
                    return false;
                }

                vertices[index] = new Vector3(
                    float.Parse(parts[0], CultureInfo.InvariantCulture),
                    float.Parse(parts[1], CultureInfo.InvariantCulture),
                    float.Parse(parts[2], CultureInfo.InvariantCulture));
            }

            while (cursor < lines.Length && !lines[cursor].StartsWith("# Polygon", StringComparison.OrdinalIgnoreCase))
            {
                cursor++;
            }

            if (cursor >= lines.Length)
            {
                return false;
            }

            cursor++;
            for (var index = 0; index < polygonCount && cursor < lines.Length; index++, cursor++)
            {
                var parts = lines[cursor].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4)
                {
                    continue;
                }

                var ints = parts.Select(part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : -1).ToArray();
                var remaining = ints.Length - 1;
                var vertexSpan = remaining >= 6 && remaining % 2 == 0 ? remaining / 2 : Math.Min(4, remaining);
                var poly = new List<int>(vertexSpan);
                for (var polyIndex = 1; polyIndex < 1 + vertexSpan && polyIndex < ints.Length; polyIndex++)
                {
                    var value = ints[polyIndex];
                    if (value >= 0 && value < vertices.Length)
                    {
                        poly.Add(value);
                    }
                }

                if (poly.Count >= 3)
                {
                    polygons.Add(poly.ToArray());
                }
            }

            return vertices.Length > 0 && polygons.Count > 0;
        }
        catch
        {
            vertices = Array.Empty<Vector3>();
            polygons = new List<int[]>();
            return false;
        }
    }

    private static (int Vertices, int Normals, int Polygons) ReadPlyCounts(string plyPath)
    {
        try
        {
            var nextIsCounts = false;
            foreach (var raw in File.ReadLines(plyPath))
            {
                var line = raw.Trim();
                if (line.StartsWith("# Number of Vertices", StringComparison.OrdinalIgnoreCase))
                {
                    nextIsCounts = true;
                    continue;
                }

                if (nextIsCounts && line.Length > 0 && !line.StartsWith('#'))
                {
                    var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3
                        && int.TryParse(parts[0], out var vertices)
                        && int.TryParse(parts[1], out var normals)
                        && int.TryParse(parts[2], out var polygons))
                    {
                        return (vertices, normals, polygons);
                    }

                    return (0, 0, 0);
                }
            }
        }
        catch
        {
        }

        return (0, 0, 0);
    }

    private static int ReadMatCount(string matPath)
    {
        try
        {
            var nextIsCount = false;
            foreach (var raw in File.ReadLines(matPath))
            {
                var line = raw.Trim();
                if (line.StartsWith("# Number of Items", StringComparison.OrdinalIgnoreCase))
                {
                    nextIsCount = true;
                    continue;
                }

                if (nextIsCount && line.Length > 0 && !line.StartsWith('#'))
                {
                    return int.TryParse(line, out var count) ? count : 0;
                }
            }
        }
        catch
        {
        }

        return 0;
    }

    private static void RasterizeTriangle(byte[] buffer, float[] zBuffer, int width, int height, PreviewTriangle triangle)
    {
        var minX = Math.Max(0, (int)MathF.Floor(MathF.Min(triangle.A.X, MathF.Min(triangle.B.X, triangle.C.X))));
        var maxX = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(triangle.A.X, MathF.Max(triangle.B.X, triangle.C.X))));
        var minY = Math.Max(0, (int)MathF.Floor(MathF.Min(triangle.A.Y, MathF.Min(triangle.B.Y, triangle.C.Y))));
        var maxY = Math.Min(height - 1, (int)MathF.Ceiling(MathF.Max(triangle.A.Y, MathF.Max(triangle.B.Y, triangle.C.Y))));

        var area = Edge(triangle.A, triangle.B, triangle.C.X, triangle.C.Y);
        if (MathF.Abs(area) < 0.0001f)
        {
            return;
        }

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var px = x + 0.5f;
                var py = y + 0.5f;
                var w0 = Edge(triangle.B, triangle.C, px, py);
                var w1 = Edge(triangle.C, triangle.A, px, py);
                var w2 = Edge(triangle.A, triangle.B, px, py);
                if ((w0 < 0 || w1 < 0 || w2 < 0) && (w0 > 0 || w1 > 0 || w2 > 0))
                {
                    continue;
                }

                w0 /= area;
                w1 /= area;
                w2 /= area;
                var depth = triangle.A.Z * w0 + triangle.B.Z * w1 + triangle.C.Z * w2;
                var bufferIndex = y * width + x;
                if (depth >= zBuffer[bufferIndex])
                {
                    continue;
                }

                zBuffer[bufferIndex] = depth;
                WritePixel(buffer, bufferIndex * 4, triangle.Fill.R, triangle.Fill.G, triangle.Fill.B, 255);
            }
        }

        DrawLine(buffer, width, height, triangle.A, triangle.B, triangle.Outline);
        DrawLine(buffer, width, height, triangle.B, triangle.C, triangle.Outline);
        DrawLine(buffer, width, height, triangle.C, triangle.A, triangle.Outline);
    }

    private static void DrawLine(byte[] buffer, int width, int height, ScreenVertex a, ScreenVertex b, PreviewColor color)
    {
        var x0 = (int)MathF.Round(a.X);
        var y0 = (int)MathF.Round(a.Y);
        var x1 = (int)MathF.Round(b.X);
        var y1 = (int)MathF.Round(b.Y);
        var dx = Math.Abs(x1 - x0);
        var dy = -Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var error = dx + dy;

        while (true)
        {
            if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < height)
            {
                WritePixel(buffer, (y0 * width + x0) * 4, color.R, color.G, color.B, 255);
            }

            if (x0 == x1 && y0 == y1)
            {
                break;
            }

            var error2 = error * 2;
            if (error2 >= dy)
            {
                error += dy;
                x0 += sx;
            }

            if (error2 <= dx)
            {
                error += dx;
                y0 += sy;
            }
        }
    }

    private static float Edge(ScreenVertex a, ScreenVertex b, float x, float y)
        => (x - a.X) * (b.Y - a.Y) - (y - a.Y) * (b.X - a.X);

    private static PreviewColor ShadeColor(byte r, byte g, byte b, float factor)
        => new(
            (byte)Math.Clamp((int)MathF.Round(r * factor), 0, 255),
            (byte)Math.Clamp((int)MathF.Round(g * factor), 0, 255),
            (byte)Math.Clamp((int)MathF.Round(b * factor), 0, 255));

    private static void FillColor(byte[] buffer, byte r, byte g, byte b, byte a)
    {
        for (var index = 0; index < buffer.Length; index += 4)
        {
            WritePixel(buffer, index, r, g, b, a);
        }
    }

    private static void WritePixel(byte[] buffer, int offset, byte r, byte g, byte b, byte a)
    {
        buffer[offset + 0] = b;
        buffer[offset + 1] = g;
        buffer[offset + 2] = r;
        buffer[offset + 3] = a;
    }

    private static void DrawFrame(byte[] buffer, int width, int height)
    {
        var frame = new PreviewColor(58, 73, 92);
        for (var x = 0; x < width; x++)
        {
            WritePixel(buffer, x * 4, frame.R, frame.G, frame.B, 255);
            WritePixel(buffer, ((height - 1) * width + x) * 4, frame.R, frame.G, frame.B, 255);
        }

        for (var y = 0; y < height; y++)
        {
            WritePixel(buffer, (y * width) * 4, frame.R, frame.G, frame.B, 255);
            WritePixel(buffer, (y * width + (width - 1)) * 4, frame.R, frame.G, frame.B, 255);
        }
    }

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static uint ReadU32(byte[] data, int offset) => BitConverter.ToUInt32(data, offset);
    private static ushort ReadU16(byte[] data, int offset) => BitConverter.ToUInt16(data, offset);

    private static string NormalizeMagicId(string value)
    {
        var text = value.Trim();
        if (text.StartsWith("magic_", StringComparison.OrdinalIgnoreCase))
        {
            text = text["magic_".Length..];
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString("D4", CultureInfo.InvariantCulture)
            : text;
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = arg[2..];
            var value = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++index]
                : "true";
            result[key] = value;
        }

        return result;
    }

    private static string GetOption(IReadOnlyDictionary<string, string> options, string key, string fallback)
        => options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
}

public sealed record KernelIndex(
    string KernelRoot,
    int MagicIdMin,
    int MagicIdMax,
    KernelFileRecord[] Files);

public sealed record KernelFileRecord(
    string Name,
    string FullPath,
    string RelativePath,
    long Length,
    string HeadHex,
    string Note);

public sealed record Ps2PackageRecord(
    string MagicId,
    string FolderName,
    string FullPath,
    string RelativePath,
    int FileCount,
    long TotalBytes,
    string PackageClass,
    IReadOnlyDictionary<string, int> ExtensionCounts,
    string[] SampleFiles,
    bool HasTm2,
    bool HasAnimation,
    bool HasModel,
    bool HasParticle,
    Ps2Tm2PreviewRecord[] Tm2Previews,
    Ps2ModelCandidateRecord[] ModelCandidates);

public sealed record Ps2Tm2PreviewRecord(
    string Name,
    string RelativePath,
    string PreviewState,
    string EvidenceLabel,
    string Variant,
    int Width,
    int Height,
    int ColorCount,
    string[] Warnings,
    string? PreviewUrl);

public sealed record Ps2ModelCandidateRecord(
    string Name,
    string RelativePath,
    string Lane,
    string Status,
    string PlyName,
    string MatName,
    int VertexCount,
    int PolygonCount,
    int MaterialCount,
    int TextureRefCount,
    int TexturesResolved,
    string? PreviewUrl);

public sealed record BatEffSummary(
    string FullPath,
    string[] Stems,
    IReadOnlyDictionary<string, int> ExtensionCounts,
    string[] SampleFiles,
    string[] DuplicateFileNames);

public sealed record Ps3MagicRecord(
    string MagicId,
    string FolderName,
    string FullPath,
    string RelativePath,
    int TextureCount,
    string[] SampleTextures,
    string[] PreviewUrls);

public sealed record RuntimeMagicDllRecord(
    string MagicId,
    string DllName,
    string FullPath,
    string RelativePath,
    long FileSize,
    string Sha256,
    string Machine,
    string PeKind,
    string TimeDateStampUtc,
    string ImageBase,
    string EntryPointRva,
    int SizeOfImage,
    int SectionCount,
    RuntimeDllSectionRecord[] Sections,
    RuntimeDllExportRecord[] Exports,
    RuntimeDllImportLibraryRecord[] ImportLibraries,
    int ImportCount,
    int AsciiStringCount,
    int EngineStringCount,
    string[] SampleStrings,
    RuntimeDllStringFamilyRecord[] StringFamilies,
    bool HasOverlayRow,
    OverlaySlotRecord[] OverlaySlots,
    string[] Warnings)
{
    public int ExportCount => Exports.Length;
}

public sealed record RuntimeDllSectionRecord(
    string Name,
    string VirtualAddress,
    int VirtualSize,
    int RawPointer,
    int RawSize,
    string Characteristics);

public sealed record RuntimeDllExportRecord(
    int Ordinal,
    string Name,
    string Rva,
    string FileOffset);

public sealed record RuntimeDllImportLibraryRecord(
    string Library,
    int Count,
    string[] SampleImports);

public sealed record RuntimeDllStringRecord(
    int FileOffset,
    string Section,
    string Value);

public sealed record RuntimeDllStringFamilyRecord(
    string Family,
    int Count,
    string[] Examples);

public sealed record OverlaySummary(
    [property: JsonPropertyName("cluster_count")] int ClusterCount,
    [property: JsonPropertyName("error_count")] int ErrorCount,
    [property: JsonPropertyName("game")] string Game,
    [property: JsonPropertyName("host_offset_signatures")] JsonElement HostOffsetSignatures,
    [property: JsonPropertyName("json_count")] int JsonCount,
    [property: JsonPropertyName("magic_dir")] string MagicDir,
    [property: JsonPropertyName("slot_kind_signatures")] JsonElement SlotKindSignatures,
    [property: JsonPropertyName("with_16_slots")] int With16Slots,
    [property: JsonPropertyName("with_table")] int WithTable)
{
    public static OverlaySummary Empty { get; } = new(
        0,
        0,
        string.Empty,
        default,
        0,
        string.Empty,
        default,
        0,
        0);
}

public sealed record OverlayRow(
    string MagicId,
    string DllName,
    int SlotCountRead,
    int NonzeroSlotCount,
    int UniqueTargetCount,
    string SlotKindSignature,
    string HostOffsetSignature,
    string TableRva,
    OverlaySlotRecord[] Slots);

public sealed record OverlaySlotRecord(
    int Index,
    string VirtualAddress,
    string Rva,
    string Kind,
    string Section,
    string RoleName,
    string Confidence,
    string Evidence);

internal readonly record struct OverlaySlotRole(string Name, string Confidence, string Evidence);

public sealed record OverlayIndex(
    OverlaySummary Summary,
    OverlayRow[] Rows);

public sealed record ExeBridgeFunction(
    string Start,
    string Name,
    int Size,
    int CodeRefsTo,
    int CodeRefsFrom,
    int UniqueCallees);

public sealed record RuntimeEvidence(
    OverlaySummary OverlaySummary,
    ExeBridgeFunction[] ExeFunctions);

public sealed record MagicViewerCatalog(
    DateTimeOffset GeneratedAt,
    string CatalogVersion,
    string SourceTool,
    CatalogSummary Summary,
    KernelIndex Kernel,
    BatEffSummary BatEff,
    RuntimeEvidence RuntimeEvidence,
    MagicCatalogEntry[] Entries);

public sealed record CatalogSummary(
    int EntryCount,
    int MultiLaneCount,
    int Ps2PackageCount,
    int HdRuntimeCount,
    int OverlayCount,
    int RuntimeDllCount,
    int EntriesWithPreviews);

public sealed record MagicCatalogEntry(
    string MagicId,
    string Label,
    string DecisionBand,
    string[] LaneTags,
    Ps2PackageRecord? Ps2Package,
    Ps3MagicRecord? Ps3Magic,
    OverlayRow? Overlay,
    RuntimeMagicDllRecord? RuntimeDll,
    CaptureEvidenceRecord? CaptureEvidence,
    string[] PreviewUrls,
    string[] SharedBatEffAnchors,
    string[] Crosswalk,
    EntrySources Sources);

public sealed record EntrySources(
    string? Ps2PackagePath,
    string? Ps3MagicPath,
    string? RuntimeDllPath,
    string? MagicDllName,
    string? CoverPreviewUrl);

public sealed record CaptureEvidenceRecord(
    string MagicId,
    int HashIndexedTextureCount,
    string[] UniqueDimensions,
    string[] SamplePaths,
    string? RecipeScriptPath,
    bool HasRecipeScript,
    string EvidenceBand);

internal readonly record struct Tm2PreviewMetadata(
    int Width,
    int Height,
    int ColorCount,
    uint ImageBytes,
    uint PaletteBytes,
    string PreviewState,
    string EvidenceLabel,
    string Variant,
    string? BlockedReason,
    string[] Warnings);

internal readonly record struct RsdModelBundle(
    string Name,
    string FullPath,
    string RelativePath,
    string Lane,
    string PlyName,
    string PlyFullPath,
    string MatName,
    string GrpName,
    string VgrName,
    string[] TextureNames,
    int VertexCount,
    int NormalCount,
    int PolygonCount,
    int MaterialCount,
    bool PlyResolved,
    bool MatResolved,
    int TexturesResolved,
    string Status);

internal readonly record struct ScreenVertex(float X, float Y, float Z);
internal readonly record struct PreviewColor(byte R, byte G, byte B);
internal readonly record struct PreviewTriangle(ScreenVertex A, ScreenVertex B, ScreenVertex C, float Depth, PreviewColor Fill, PreviewColor Outline);

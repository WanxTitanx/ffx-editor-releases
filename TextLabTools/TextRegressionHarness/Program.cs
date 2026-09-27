using FFXProjectEditor.Files;
using FFXProjectEditor.Utils.Encoding;
using System.Text;
using System.Text.Json;
using TextRegressionHarness.TextSupport;

Console.OutputEncoding = Encoding.UTF8;

try
{
    HarnessOptions options = HarnessOptions.Parse(args);
    TextRegressionHarnessRunner harness = new(options);
    HarnessRunResult result = harness.Run();

    Directory.CreateDirectory(Path.GetDirectoryName(options.ReportPath)!);
    Directory.CreateDirectory(Path.GetDirectoryName(options.JsonPath)!);

    File.WriteAllText(options.ReportPath, RenderMarkdown(result), new UTF8Encoding(false));
    File.WriteAllText(
        options.JsonPath,
        JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
        new UTF8Encoding(false));

    Console.WriteLine($"Text regression report: {options.ReportPath}");
    Console.WriteLine($"Text regression json: {options.JsonPath}");
    Console.WriteLine($"Sources: {result.TotalSources} | Pass: {result.PassedSources} | Warning: {result.WarningSources} | Fail: {result.FailedSources}");
    Environment.ExitCode = result.FailedSources > 0 ? 1 : 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    Environment.ExitCode = 1;
}

static string RenderMarkdown(HarnessRunResult result)
{
    StringBuilder sb = new();
    sb.AppendLine("# Text Regression Harness");
    sb.AppendLine();
    sb.AppendLine($"- Generated UTC: `{result.GeneratedUtc}`");
    sb.AppendLine($"- Master Path: `{result.MasterPath}`");
    sb.AppendLine($"- Total Sources: `{result.TotalSources}`");
    sb.AppendLine($"- Pass: `{result.PassedSources}`");
    sb.AppendLine($"- Warning: `{result.WarningSources}`");
    sb.AppendLine($"- Fail: `{result.FailedSources}`");
    sb.AppendLine($"- Advisory Checks: `{result.AdvisoryWarningChecks}`");
    sb.AppendLine();

    if (result.GeneralNotes.Count > 0)
    {
        sb.AppendLine("## Run Notes");
        sb.AppendLine();
        foreach (string note in result.GeneralNotes)
        {
            sb.AppendLine($"- {note}");
        }

        sb.AppendLine();
    }

    sb.AppendLine("## Sources");
    sb.AppendLine();

    foreach (HarnessSourceResult source in result.Sources)
    {
        sb.AppendLine($"### {source.DisplayName}");
        sb.AppendLine();
        sb.AppendLine($"- Status: `{source.Status}`");
        sb.AppendLine($"- Family: `{source.Family}`");
        sb.AppendLine($"- Parser Label: `{source.ParserLabel}`");
        sb.AppendLine($"- Relative Path: `{source.RelativePath}`");
        sb.AppendLine($"- Write Path: `{source.WritePath}`");
        sb.AppendLine($"- Scope: `{source.Scope}`");
        sb.AppendLine($"- Probe Evidence: {source.ProbeEvidence}");

        if (source.Notes.Count > 0)
        {
            sb.AppendLine("- Notes:");
            foreach (string note in source.Notes)
            {
                sb.AppendLine($"  - {note}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("| Check | Result | Details |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (HarnessCheckResult check in source.Checks)
        {
            sb.AppendLine($"| {EscapePipe(check.Name)} | {check.Result} | {EscapePipe(check.Details)} |");
        }

        sb.AppendLine();
    }

    return sb.ToString();
}

static string EscapePipe(string value)
{
    return value.Replace("|", "\\|").Replace(Environment.NewLine, "<br/>");
}

sealed class TextRegressionHarnessRunner(HarnessOptions options)
{
    public HarnessRunResult Run()
    {
        List<HarnessSourceResult> results = [];
        foreach (SourceSpec source in BuildSources())
        {
            results.Add(RunSource(source));
        }

        return new HarnessRunResult
        {
            GeneratedUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            MasterPath = options.MasterPath,
            Sources = results,
            GeneralNotes =
            [
                "Round-trip byte identity is advisory only. Structural/model equality is the primary gate for proven safe writers.",
                "Battle text remains read-only by design even when reader proof passes.",
                "Field-string serializer checks are lab diagnostics only; no standalone safe writer is exposed from this harness.",
                "Al Bhed dictionary, pointer-script table, and legacy menumain coverage are reader-only production smoke tests.",
                "Source status ignores advisory-only byte-identity drift. A source stays in warning only when a non-advisory issue remains."
            ]
        };
    }

    List<SourceSpec> BuildSources()
    {
        List<SourceSpec> sources = [];

        string kernelUs = Path.Combine(options.MasterPath, "new_uspc", "battle", "kernel");
        string kernelJp = Path.Combine(options.MasterPath, "jppc", "battle", "kernel");
        string menuUs = Path.Combine(options.MasterPath, "new_uspc", "menu");
        string menuJp = Path.Combine(options.MasterPath, "jppc", "menu");

        AddCuratedNameDescriptionSource(sources, kernelUs, "menu_txt.bin", "Menu Text");
        AddCuratedNameDescriptionSource(sources, kernelUs, "config_txt.bin", "Config Text");
        AddCuratedNameDescriptionSource(sources, kernelUs, "status_txt.bin", "Status Text");
        AddCuratedNameDescriptionSource(sources, kernelUs, "summon_txt.bin", "Summon Text");
        AddCuratedNameDescriptionSource(sources, kernelUs, "arms_txt.bin", "Gear / Arms Text");
        AddCuratedNameDescriptionSource(sources, kernelUs, "item_txt.bin", "Item Text");
        AddCuratedNameDescriptionSource(sources, kernelUs, "mmain_txt.bin", "Main Menu Text");
        AddCuratedNameDescriptionSource(sources, kernelUs, "btlend_txt.bin", "Battle End Text");
        AddCuratedNameDescriptionSource(sources, kernelUs, "build_txt.bin", "Build Text");
        AddCuratedNameDescriptionSource(sources, kernelUs, "name_txt.bin", "Name Text");
        AddCuratedNameDescriptionSource(sources, kernelUs, "save_txt.bin", "Save Text");

        AddExistingSource(sources, new SourceSpec
        {
            Id = "menu_txt2_jp",
            DisplayName = "Menu Text 2 (JP)",
            AbsolutePath = Path.Combine(kernelJp, "menu_txt2.bin"),
            RelativePath = Path.Combine("jppc", "battle", "kernel", "menu_txt2.bin"),
            Decoder = FfxEncoding.JpDecoder,
            Scope = "curated",
            Category = SourceCategory.NameDescription,
            ParserLabel = "READ_STRING_FILE · NAME/DESCRIPTION · JP",
            ExpectedProbeFamily = TextFormatProbeFamily.NameDescription,
            WritePath = "safe writer",
            ProbeEvidence = "Curated family: proven on extracted jppc/inpc copies with the existing name/description serializer."
        });

        AddExistingSource(sources, new SourceSpec
        {
            Id = "btl_txt",
            DisplayName = "Battle Text",
            AbsolutePath = Path.Combine(kernelUs, "btl_txt.bin"),
            RelativePath = Path.Combine("new_uspc", "battle", "kernel", "btl_txt.bin"),
            Decoder = FfxEncoding.UsDecoder,
            Scope = "curated",
            Category = SourceCategory.BattleText,
            ParserLabel = "READ_STRING_FILE · BATTLE TEXT",
            ExpectedProbeFamily = TextFormatProbeFamily.BattleText,
            WritePath = "read-only",
            ProbeEvidence = "Curated family: proven 8-byte battle-text layout. Writer intentionally stays locked."
        });

        AddExistingSource(sources, BuildMonsterSource("monster1", "Monster Localizations 1", Path.Combine(kernelUs, "monster1.bin")));
        AddExistingSource(sources, BuildMonsterSource("monster2", "Monster Localizations 2", Path.Combine(kernelUs, "monster2.bin")));
        AddExistingSource(sources, BuildMonsterSource("monster3", "Monster Localizations 3", Path.Combine(kernelUs, "monster3.bin")));

        HashSet<string> seenPaths = new(
            sources.Select(source => Path.GetFullPath(source.AbsolutePath)),
            StringComparer.OrdinalIgnoreCase);

        AutoProbeDirectory(sources, kernelUs, FfxEncoding.UsDecoder, "US", seenPaths);
        AutoProbeDirectory(sources, kernelJp, FfxEncoding.JpDecoder, "JP", seenPaths);
        AutoProbeDirectory(sources, menuUs, FfxEncoding.UsDecoder, "US", seenPaths);
        AutoProbeDirectory(sources, menuJp, FfxEncoding.JpDecoder, "JP", seenPaths);
        return sources;
    }

    void AddCuratedNameDescriptionSource(List<SourceSpec> sources, string directory, string fileName, string displayName)
    {
        AddExistingSource(sources, new SourceSpec
        {
            Id = Path.GetFileNameWithoutExtension(fileName),
            DisplayName = displayName,
            AbsolutePath = Path.Combine(directory, fileName),
            RelativePath = Path.Combine("new_uspc", "battle", "kernel", fileName),
            Decoder = FfxEncoding.UsDecoder,
            Scope = "curated",
            Category = SourceCategory.NameDescription,
            ParserLabel = "READ_STRING_FILE · NAME/DESCRIPTION",
            ExpectedProbeFamily = TextFormatProbeFamily.NameDescription,
            WritePath = "safe writer",
            ProbeEvidence = "Curated family: proven 4-field keyed text table with automatic offset rebuild."
        });
    }

    SourceSpec BuildMonsterSource(string id, string displayName, string absolutePath)
    {
        return new SourceSpec
        {
            Id = id,
            DisplayName = displayName,
            AbsolutePath = absolutePath,
            RelativePath = Path.Combine("new_uspc", "battle", "kernel", Path.GetFileName(absolutePath)),
            Decoder = FfxEncoding.UsDecoder,
            Scope = "curated",
            Category = SourceCategory.MonsterLocalization,
            ParserLabel = "READ_MONSTER_LOCALIZATIONS",
            ExpectedProbeFamily = null,
            WritePath = "safe writer",
            ProbeEvidence = "Curated family: proven monster localization reader with safe write path for Name / Sensor / Scan."
        };
    }

    void AutoProbeDirectory(List<SourceSpec> sources, string directory, Dictionary<byte, char> decoder, string localeLabel, HashSet<string> seenPaths)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (string path in Directory.EnumerateFiles(directory, "*.bin").OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            string fileName = Path.GetFileName(path);
            string normalizedPath = Path.GetFullPath(path);
            if (!IsAutoProbeCandidate(fileName) || !seenPaths.Add(normalizedPath))
            {
                continue;
            }

            TextFormatProbeResult probe = TextFormatProbe.ProbeFile(path, decoder);
            sources.Add(new SourceSpec
            {
                Id = $"auto_{localeLabel.ToLowerInvariant()}_{Path.GetFileNameWithoutExtension(fileName).Replace('.', '_')}",
                DisplayName = BuildAutoProbeDisplayName(fileName, localeLabel),
                AbsolutePath = path,
                RelativePath = Path.GetRelativePath(options.MasterPath, path),
                Decoder = decoder,
                Scope = "auto-probed",
                Category = MapProbeFamily(probe.Family),
                ParserLabel = probe.ParserModeLabel,
                ExpectedProbeFamily = probe.Family,
                WritePath = probe.Family switch
                {
                    TextFormatProbeFamily.NameDescription => "safe writer",
                    TextFormatProbeFamily.FieldString => "lab serializer only",
                    _ => "read-only"
                },
                ProbeEvidence = probe.ProbeEvidence
            });
        }
    }

    static bool IsAutoProbeCandidate(string fileName)
    {
        return fileName.Contains("txt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "menumain.bin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "albheddic.bin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "menu_script.bin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "battle_script.bin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "system_script.bin", StringComparison.OrdinalIgnoreCase);
    }

    static string BuildAutoProbeDisplayName(string fileName, string localeLabel)
    {
        string friendlyName = fileName.ToLowerInvariant() switch
        {
            "help_txt.bin" => "Help Text",
            "menumain.bin" => "Menu Main",
            "albheddic.bin" => "Al Bhed Dictionary",
            "menu_script.bin" => "Menu Script",
            "battle_script.bin" => "Battle Script",
            "system_script.bin" => "System Script",
            _ => Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ')
        };

        return $"{friendlyName} ({localeLabel}, Auto-Probed)";
    }

    static SourceCategory MapProbeFamily(TextFormatProbeFamily family)
    {
        return family switch
        {
            TextFormatProbeFamily.NameDescription => SourceCategory.NameDescription,
            TextFormatProbeFamily.BattleText => SourceCategory.BattleText,
            TextFormatProbeFamily.FieldString => SourceCategory.FieldString,
            TextFormatProbeFamily.AlBhedDictionary => SourceCategory.AlBhedDictionary,
            TextFormatProbeFamily.PointerScriptTable => SourceCategory.PointerScriptTable,
            TextFormatProbeFamily.LegacyMenuMainResource => SourceCategory.LegacyMenuMainResource,
            _ => SourceCategory.Unsupported
        };
    }

    static void AddExistingSource(List<SourceSpec> sources, SourceSpec source)
    {
        if (File.Exists(source.AbsolutePath))
        {
            sources.Add(source);
        }
    }

    HarnessSourceResult RunSource(SourceSpec source)
    {
        return source.Category switch
        {
            SourceCategory.NameDescription => RunNameDescriptionSource(source),
            SourceCategory.BattleText => RunBattleTextSource(source),
            SourceCategory.MonsterLocalization => RunMonsterLocalizationSource(source),
            SourceCategory.FieldString => RunFieldStringSource(source),
            SourceCategory.AlBhedDictionary => RunAlBhedDictionarySource(source),
            SourceCategory.PointerScriptTable => RunPointerScriptTableSource(source),
            SourceCategory.LegacyMenuMainResource => RunLegacyMenuMainResourceSource(source),
            _ => RunUnsupportedSource(source)
        };
    }

    HarnessSourceResult RunNameDescriptionSource(SourceSpec source)
    {
        List<HarnessCheckResult> checks = [];
        List<string> notes = [];
        byte[] bytes = File.ReadAllBytes(source.AbsolutePath);

        try
        {
            TextFormatProbeResult probe = TextFormatProbe.ProbeBytes(bytes, source.Decoder);
            if (probe.Family != TextFormatProbeFamily.NameDescription)
            {
                checks.Add(Fail("Probe", $"Expected Name/Description but probe returned {probe.Family}. {probe.ProbeEvidence}"));
                return BuildResult(source, "Name/Description", checks, notes);
            }

            checks.Add(Pass("Probe", probe.ProbeEvidence));
            NameDescriptionTextTable_File file = NameDescriptionTextTable_File.Read(bytes, source.Decoder);
            checks.Add(Pass("Read", $"{file.EntryCount} entries · range {file.MinIndex:X2}h..{file.MaxIndex:X2}h"));

            byte[] rebuiltBytes = file.Write(source.Decoder);
            NameDescriptionTextTable_File rebuilt = NameDescriptionTextTable_File.Read(rebuiltBytes, source.Decoder);

            string roundTripIssue = CompareNameDescription(file, rebuilt);
            checks.Add(string.IsNullOrEmpty(roundTripIssue)
                ? Pass("Neutral Round-Trip Model", "Entry count, keys, and all four text fields matched after rebuild.")
                : Fail("Neutral Round-Trip Model", roundTripIssue));

            bool byteEqual = bytes.SequenceEqual(rebuiltBytes);
            checks.Add(byteEqual
                ? Pass("Neutral Byte Identity", "Rebuilt bytes match the original file byte-for-byte.")
                : Warn("Neutral Byte Identity", "Rebuilt bytes differ, but the re-read model stayed equivalent. Treat this as advisory, not as a writer failure.", advisory: true));

            if (!byteEqual)
            {
                notes.Add("Byte-identity drift exists on neutral rebuild. Structural/model proof still passed.");
            }

            return BuildResult(source, "Name/Description", checks, notes);
        }
        catch (Exception ex)
        {
            checks.Add(Fail("Unhandled", ex.Message));
            return BuildResult(source, "Name/Description", checks, notes);
        }
    }

    HarnessSourceResult RunBattleTextSource(SourceSpec source)
    {
        List<HarnessCheckResult> checks = [];
        List<string> notes = [];
        byte[] bytes = File.ReadAllBytes(source.AbsolutePath);

        try
        {
            TextFormatProbeResult probe = TextFormatProbe.ProbeBytes(bytes, source.Decoder);
            if (probe.Family != TextFormatProbeFamily.BattleText)
            {
                checks.Add(Fail("Probe", $"Expected BattleText but probe returned {probe.Family}. {probe.ProbeEvidence}"));
                return BuildResult(source, "BattleText", checks, notes);
            }

            checks.Add(Pass("Probe", probe.ProbeEvidence));
            BattleTextTable_File file = BattleTextTable_File.Read(bytes, source.Decoder);
            checks.Add(Pass("Read", $"{file.EntryCount} entries · {file.TaxonomySummary}"));
            notes.Add("Decoded string candidates remain heuristic. This harness only proves reader stability and exposed taxonomy.");
            return BuildResult(source, "BattleText", checks, notes);
        }
        catch (Exception ex)
        {
            checks.Add(Fail("Unhandled", ex.Message));
            return BuildResult(source, "BattleText", checks, notes);
        }
    }

    HarnessSourceResult RunMonsterLocalizationSource(SourceSpec source)
    {
        List<HarnessCheckResult> checks = [];
        List<string> notes = [];
        byte[] bytes = File.ReadAllBytes(source.AbsolutePath);

        try
        {
            MonX_File file = MonX_File.Read(bytes);
            checks.Add(Pass("Read", $"{file.Entries.Count} entries · previous file count {file.ThisHeader.PreviousFileCount}"));

            byte[] rebuiltBytes = file.Write();
            MonX_File rebuilt = MonX_File.Read(rebuiltBytes);

            string roundTripIssue = CompareMonsterLocalizations(file, rebuilt);
            checks.Add(string.IsNullOrEmpty(roundTripIssue)
                ? Pass("Neutral Round-Trip Model", "Name / Sensor / Scan fields matched after rebuild.")
                : Fail("Neutral Round-Trip Model", roundTripIssue));

            bool byteEqual = bytes.SequenceEqual(rebuiltBytes);
            checks.Add(byteEqual
                ? Pass("Neutral Byte Identity", "Rebuilt bytes match the original file byte-for-byte.")
                : Warn("Neutral Byte Identity", "Rebuilt bytes differ, but the safe text fields re-read identically. Treat this as advisory.", advisory: true));

            if (!byteEqual)
            {
                notes.Add("Byte-identity drift exists on neutral rebuild. Safe text fields still matched after re-read.");
            }

            return BuildResult(source, "MonsterLocalization", checks, notes);
        }
        catch (Exception ex)
        {
            checks.Add(Fail("Unhandled", ex.Message));
            return BuildResult(source, "MonsterLocalization", checks, notes);
        }
    }

    HarnessSourceResult RunFieldStringSource(SourceSpec source)
    {
        List<HarnessCheckResult> checks = [];
        List<string> notes = [];
        byte[] bytes = File.ReadAllBytes(source.AbsolutePath);

        try
        {
            TextFormatProbeResult probe = TextFormatProbe.ProbeBytes(bytes, source.Decoder);
            if (probe.Family != TextFormatProbeFamily.FieldString)
            {
                checks.Add(Fail("Probe", $"Expected FieldString but probe returned {probe.Family}. {probe.ProbeEvidence}"));
                return BuildResult(source, "FieldString", checks, notes);
            }

            checks.Add(Pass("Probe", probe.ProbeEvidence));
            TextTable_File file = TextTable_File.Read(bytes, source.Decoder);
            checks.Add(Pass("Read", $"{file.EntryCount} entries · header length {file.HeaderLength:X4}h"));

            byte[] rebuiltBytes = file.Write(source.Decoder);
            TextTable_File rebuilt = TextTable_File.Read(rebuiltBytes, source.Decoder);

            string roundTripIssue = CompareFieldStrings(file, rebuilt);
            checks.Add(string.IsNullOrEmpty(roundTripIssue)
                ? Pass("Neutral Round-Trip Model", "Regular/simplified text plus flag metadata matched after rebuild.")
                : Fail("Neutral Round-Trip Model", roundTripIssue));

            bool byteEqual = bytes.SequenceEqual(rebuiltBytes);
            checks.Add(byteEqual
                ? Pass("Neutral Byte Identity", "Rebuilt bytes match the original file byte-for-byte.")
                : Warn("Neutral Byte Identity", "Rebuilt bytes differ, but the re-read field-string model stayed equivalent. UI write path remains locked.", advisory: true));

            notes.Add("This is a lab serializer regression only. Field-string files are still read-only in the app.");
            if (!byteEqual)
            {
                notes.Add("Byte-identity drift exists on neutral rebuild. Structural/model proof still passed.");
            }

            return BuildResult(source, "FieldString", checks, notes);
        }
        catch (Exception ex)
        {
            checks.Add(Fail("Unhandled", ex.Message));
            return BuildResult(source, "FieldString", checks, notes);
        }
    }

    HarnessSourceResult RunAlBhedDictionarySource(SourceSpec source)
    {
        List<HarnessCheckResult> checks = [];
        List<string> notes = [];
        byte[] bytes = File.ReadAllBytes(source.AbsolutePath);

        try
        {
            TextFormatProbeResult probe = TextFormatProbe.ProbeBytes(bytes, source.Decoder);
            if (probe.Family != TextFormatProbeFamily.AlBhedDictionary)
            {
                checks.Add(Fail("Probe", $"Expected AlBhedDictionary but probe returned {probe.Family}. {probe.ProbeEvidence}"));
                return BuildResult(source, "AlBhedDictionary", checks, notes);
            }

            checks.Add(Pass("Probe", probe.ProbeEvidence));
            FFXProjectEditor.FfxLib.Text.AlBhedDictionary_File file = FFXProjectEditor.FfxLib.Text.AlBhedDictionary_File.Read(bytes, source.Decoder);
            checks.Add(Pass("Read", $"{file.EntryCount} entries · {file.Summary}"));
            notes.Add("This family is a proven mapping-table reader only. No free-form text writer is exposed.");
            return BuildResult(source, "AlBhedDictionary", checks, notes);
        }
        catch (Exception ex)
        {
            checks.Add(Fail("Unhandled", ex.Message));
            return BuildResult(source, "AlBhedDictionary", checks, notes);
        }
    }

    HarnessSourceResult RunPointerScriptTableSource(SourceSpec source)
    {
        List<HarnessCheckResult> checks = [];
        List<string> notes = [];
        byte[] bytes = File.ReadAllBytes(source.AbsolutePath);

        try
        {
            TextFormatProbeResult probe = TextFormatProbe.ProbeBytes(bytes, source.Decoder);
            if (probe.Family != TextFormatProbeFamily.PointerScriptTable)
            {
                checks.Add(Fail("Probe", $"Expected PointerScriptTable but probe returned {probe.Family}. {probe.ProbeEvidence}"));
                return BuildResult(source, "PointerScriptTable", checks, notes);
            }

            checks.Add(Pass("Probe", probe.ProbeEvidence));
            FFXProjectEditor.FfxLib.Text.PointerScriptTable_File file = FFXProjectEditor.FfxLib.Text.PointerScriptTable_File.Read(bytes, source.Decoder);
            checks.Add(Pass("Read", $"{file.EntryCount} entries · {file.Summary}"));
            notes.Add("This family is structural proof only. Slot topology and decoded scripts are surfaced read-only.");
            return BuildResult(source, "PointerScriptTable", checks, notes);
        }
        catch (Exception ex)
        {
            checks.Add(Fail("Unhandled", ex.Message));
            return BuildResult(source, "PointerScriptTable", checks, notes);
        }
    }

    HarnessSourceResult RunLegacyMenuMainResourceSource(SourceSpec source)
    {
        List<HarnessCheckResult> checks = [];
        List<string> notes = [];
        byte[] bytes = File.ReadAllBytes(source.AbsolutePath);

        try
        {
            TextFormatProbeResult probe = TextFormatProbe.ProbeBytes(bytes, source.Decoder);
            if (probe.Family != TextFormatProbeFamily.LegacyMenuMainResource)
            {
                checks.Add(Fail("Probe", $"Expected LegacyMenuMainResource but probe returned {probe.Family}. {probe.ProbeEvidence}"));
                return BuildResult(source, "LegacyMenuMainResource", checks, notes);
            }

            checks.Add(Pass("Probe", probe.ProbeEvidence));
            FFXProjectEditor.FfxLib.Text.LegacyMenuMainResource_File file = FFXProjectEditor.FfxLib.Text.LegacyMenuMainResource_File.Read(bytes);
            checks.Add(Pass("Read", file.Summary));
            notes.Add("This family is a proven non-text container classification only. It must not be treated as writer-safe text.");
            return BuildResult(source, "LegacyMenuMainResource", checks, notes);
        }
        catch (Exception ex)
        {
            checks.Add(Fail("Unhandled", ex.Message));
            return BuildResult(source, "LegacyMenuMainResource", checks, notes);
        }
    }

    HarnessSourceResult RunUnsupportedSource(SourceSpec source)
    {
        List<HarnessCheckResult> checks = [];
        List<string> notes = [];

        try
        {
            TextFormatProbeResult probe = TextFormatProbe.ProbeFile(source.AbsolutePath, source.Decoder);
            checks.Add(Warn("Probe", probe.ProbeEvidence));
            notes.Add("No proven reader matched this source yet. It remains explicit and read-only by design.");
            return BuildResult(source, "Unsupported", checks, notes);
        }
        catch (Exception ex)
        {
            checks.Add(Fail("Unhandled", ex.Message));
            return BuildResult(source, "Unsupported", checks, notes);
        }
    }

    HarnessSourceResult BuildResult(SourceSpec source, string family, List<HarnessCheckResult> checks, List<string> notes)
    {
        string status = checks.Any(check => check.Result == "fail")
            ? "fail"
            : checks.Any(check => check.Result == "warning" && !check.Advisory)
                ? "warning"
                : "pass";

        return new HarnessSourceResult
        {
            Id = source.Id,
            DisplayName = source.DisplayName,
            RelativePath = source.RelativePath,
            Family = family,
            ParserLabel = source.ParserLabel,
            WritePath = source.WritePath,
            Scope = source.Scope,
            ProbeEvidence = source.ProbeEvidence,
            Status = status,
            Checks = checks,
            Notes = notes
        };
    }

    static HarnessCheckResult Pass(string name, string details) => new() { Name = name, Result = "pass", Details = details, Advisory = false };
    static HarnessCheckResult Warn(string name, string details, bool advisory = false) => new() { Name = name, Result = "warning", Details = details, Advisory = advisory };
    static HarnessCheckResult Fail(string name, string details) => new() { Name = name, Result = "fail", Details = details, Advisory = false };

    static string CompareNameDescription(NameDescriptionTextTable_File expected, NameDescriptionTextTable_File actual)
    {
        if (expected.EntryCount != actual.EntryCount)
        {
            return $"Entry count changed from {expected.EntryCount} to {actual.EntryCount}.";
        }

        if (expected.MinIndex != actual.MinIndex || expected.MaxIndex != actual.MaxIndex || expected.EntryLength != actual.EntryLength)
        {
            return "Header proof changed across neutral rebuild.";
        }

        for (int i = 0; i < expected.Entries.Count; i++)
        {
            NameDescriptionTextTable_Entry a = expected.Entries[i];
            NameDescriptionTextTable_Entry b = actual.Entries[i];

            if (a.NameKey != b.NameKey
                || a.SimplifiedNameKey != b.SimplifiedNameKey
                || a.DescriptionKey != b.DescriptionKey
                || a.SimplifiedDescriptionKey != b.SimplifiedDescriptionKey)
            {
                return $"Entry {a.IndexLabel} changed key metadata after rebuild.";
            }

            if (!TextEquals(a.NameText, b.NameText)
                || !TextEquals(a.SimplifiedNameText, b.SimplifiedNameText)
                || !TextEquals(a.DescriptionText, b.DescriptionText)
                || !TextEquals(a.SimplifiedDescriptionText, b.SimplifiedDescriptionText))
            {
                return $"Entry {a.IndexLabel} changed text content after rebuild.";
            }
        }

        return string.Empty;
    }

    static string CompareFieldStrings(TextTable_File expected, TextTable_File actual)
    {
        if (expected.EntryCount != actual.EntryCount || expected.HeaderLength != actual.HeaderLength)
        {
            return "Field-string header proof changed across neutral rebuild.";
        }

        for (int i = 0; i < expected.Entries.Count; i++)
        {
            TextTable_Entry a = expected.Entries[i];
            TextTable_Entry b = actual.Entries[i];

            if (a.RegularFlags != b.RegularFlags
                || a.RegularChoices != b.RegularChoices
                || a.SimplifiedFlags != b.SimplifiedFlags
                || a.SimplifiedChoices != b.SimplifiedChoices)
            {
                return $"Entry {a.IndexLabel} changed flag metadata after rebuild.";
            }

            if (!TextEquals(a.RegularText, b.RegularText) || !TextEquals(a.SimplifiedText, b.SimplifiedText))
            {
                return $"Entry {a.IndexLabel} changed text content after rebuild.";
            }
        }

        return string.Empty;
    }

    static string CompareMonsterLocalizations(MonX_File expected, MonX_File actual)
    {
        if (expected.Entries.Count != actual.Entries.Count)
        {
            return $"Monster entry count changed from {expected.Entries.Count} to {actual.Entries.Count}.";
        }

        for (int i = 0; i < expected.Entries.Count; i++)
        {
            MonX_File.Entry a = expected.Entries[i];
            MonX_File.Entry b = actual.Entries[i];

            if (!TextEquals(a.Name, b.Name) || !TextEquals(a.Sensor, b.Sensor) || !TextEquals(a.Scan, b.Scan))
            {
                return $"Monster entry {i} changed safe text fields after rebuild.";
            }
        }

        return string.Empty;
    }

    static bool TextEquals(string left, string right)
    {
        return NormalizeText(left) == NormalizeText(right);
    }

    static string NormalizeText(string? value)
    {
        return (value ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
    }
}

sealed class HarnessOptions
{
    public required string MasterPath { get; init; }
    public required string ReportPath { get; init; }
    public required string JsonPath { get; init; }

    public static HarnessOptions Parse(string[] args)
    {
        string? masterPath = null;
        string? reportPath = null;
        string? jsonPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--master":
                    masterPath = ReadValue(args, ref i, "--master");
                    break;

                case "--report":
                    reportPath = ReadValue(args, ref i, "--report");
                    break;

                case "--json":
                    jsonPath = ReadValue(args, ref i, "--json");
                    break;

                case "--help":
                case "-h":
                    throw new InvalidOperationException("Usage: dotnet run --project TextLabTools\\TextRegressionHarness\\TextRegressionHarness.csproj -- --master <path-to-master> --report <report.md> --json <report.json>");

                default:
                    throw new InvalidOperationException($"Unknown argument: {args[i]}");
            }
        }

        if (string.IsNullOrWhiteSpace(masterPath))
        {
            throw new InvalidOperationException("Missing required argument: --master <path-to-master>");
        }

        string normalizedMaster = masterPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(normalizedMaster))
        {
            throw new InvalidOperationException($"Master path does not exist: {normalizedMaster}");
        }

        if (!string.Equals(Path.GetFileName(normalizedMaster), "master", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Expected a folder named 'master', got: {normalizedMaster}");
        }

        string defaultBase = Path.Combine(AppContext.BaseDirectory, "text-regression-last");

        return new HarnessOptions
        {
            MasterPath = normalizedMaster,
            ReportPath = Path.GetFullPath(reportPath ?? defaultBase + ".md"),
            JsonPath = Path.GetFullPath(jsonPath ?? defaultBase + ".json")
        };
    }

    static string ReadValue(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length)
        {
            throw new InvalidOperationException($"Missing value for {name}");
        }

        index++;
        return args[index];
    }
}

sealed class SourceSpec
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string AbsolutePath { get; init; }
    public required string RelativePath { get; init; }
    public required Dictionary<byte, char> Decoder { get; init; }
    public required string Scope { get; init; }
    public required SourceCategory Category { get; init; }
    public required string ParserLabel { get; init; }
    public required TextFormatProbeFamily? ExpectedProbeFamily { get; init; }
    public required string WritePath { get; init; }
    public required string ProbeEvidence { get; init; }
}

enum SourceCategory
{
    NameDescription,
    BattleText,
    MonsterLocalization,
    FieldString,
    AlBhedDictionary,
    PointerScriptTable,
    LegacyMenuMainResource,
    Unsupported
}

sealed class HarnessRunResult
{
    public required string GeneratedUtc { get; init; }
    public required string MasterPath { get; init; }
    public required List<HarnessSourceResult> Sources { get; init; }
    public required List<string> GeneralNotes { get; init; }

    public int TotalSources => Sources.Count;
    public int PassedSources => Sources.Count(source => source.Status == "pass");
    public int WarningSources => Sources.Count(source => source.Status == "warning");
    public int FailedSources => Sources.Count(source => source.Status == "fail");
    public int AdvisoryWarningChecks => Sources.SelectMany(source => source.Checks).Count(check => check.Result == "warning" && check.Advisory);
}

sealed class HarnessSourceResult
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string RelativePath { get; init; }
    public required string Family { get; init; }
    public required string ParserLabel { get; init; }
    public required string WritePath { get; init; }
    public required string Scope { get; init; }
    public required string ProbeEvidence { get; init; }
    public required string Status { get; init; }
    public required List<HarnessCheckResult> Checks { get; init; }
    public required List<string> Notes { get; init; }
}

sealed class HarnessCheckResult
{
    public required string Name { get; init; }
    public required string Result { get; init; }
    public required string Details { get; init; }
    public required bool Advisory { get; init; }
}

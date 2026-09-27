using FFXProjectEditor.Files;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Text;
using FFXProjectEditor.Utils.Encoding;
using System.Text;

sealed class ProductionSafetySmokeOptions
{
    public required string MasterPath { get; init; }
    public string? ReportPath { get; init; }
    public string? JsonPath { get; init; }

    public static ProductionSafetySmokeOptions Parse(string[] args)
    {
        string? masterPath = null;
        string? reportPath = null;
        string? jsonPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--master", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                masterPath = args[++i];
            else if (string.Equals(args[i], "--report", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                reportPath = args[++i];
            else if (string.Equals(args[i], "--json", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                jsonPath = args[++i];
        }

        masterPath ??= @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master";
        if (!Directory.Exists(masterPath))
            throw new DirectoryNotFoundException($"Master path not found: {masterPath}");

        return new ProductionSafetySmokeOptions
        {
            MasterPath = masterPath,
            ReportPath = reportPath,
            JsonPath = jsonPath
        };
    }
}

sealed class ProductionSafetySmokeRunner(ProductionSafetySmokeOptions options)
{
    public ProductionSafetyRunResult Run()
    {
        List<ProductionSafetyCaseResult> cases =
        [
            RunNameDescriptionMutationSmoke(),
            RunMonsterLocalizationMutationSmoke(),
            RunKeyItemReadGuard(),
            RunAutoAbilityReadGuard()
        ];

        return new ProductionSafetyRunResult
        {
            GeneratedUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            MasterPath = options.MasterPath,
            GeneralNotes =
            [
                "This is the minimal Pt17 save-safety absorption slice for ProductionAuditTools.",
                "It preserves the two proven text persistence smokes and makes the two known failing families explicit in a reportable harness.",
                "A failing known-fail guard is honest here: it keeps unsafe claims visible instead of silently passing them through UI or docs.",
                "This harness does not unlock any new writer and must not be used as proof of general writer-safe status."
            ],
            Cases = cases
        };
    }

    ProductionSafetyCaseResult RunNameDescriptionMutationSmoke()
    {
        string relativePath = Path.Combine("new_uspc", "battle", "kernel", "summon_txt.bin");
        string absolutePath = Path.Combine(options.MasterPath, relativePath);

        return RunCase(
            "Name/Description Persistence Smoke",
            "text-safe-slice",
            [relativePath],
            checks =>
            {
                string workRoot = CreateTempWorkRoot();
                try
                {
                    string copyPath = Path.Combine(workRoot, "summon_txt.bin");
                    File.Copy(absolutePath, copyPath, overwrite: true);

                    NameDescriptionTextTable_File file = NameDescriptionTextTable_File.Read(File.ReadAllBytes(copyPath), FfxEncoding.UsDecoder);
                    if (file.Entries.Count < 2)
                        throw new InvalidDataException("Name/Description smoke expected at least 2 entries.");

                    string marker = BuildMarker("ND");
                    file.Entries[1].NameText = marker;
                    File.WriteAllBytes(copyPath, file.Write(FfxEncoding.UsDecoder));

                    NameDescriptionTextTable_File reread = NameDescriptionTextTable_File.Read(File.ReadAllBytes(copyPath), FfxEncoding.UsDecoder);
                    if (!string.Equals(reread.Entries[1].NameText, marker, StringComparison.Ordinal))
                        throw new InvalidDataException("Name/Description smoke failed to persist the edited value after reread.");

                    checks.Add(Pass("Mutation Reread", $"Entry 1 Name persisted as `{marker}` after save + reread."));
                }
                finally
                {
                    DeleteTempWorkRoot(workRoot);
                }
            });
    }

    ProductionSafetyCaseResult RunMonsterLocalizationMutationSmoke()
    {
        string relativePath = Path.Combine("new_uspc", "battle", "kernel", "monster2.bin");
        string absolutePath = Path.Combine(options.MasterPath, relativePath);

        return RunCase(
            "Monster Localization Persistence Smoke",
            "text-safe-slice",
            [relativePath],
            checks =>
            {
                string workRoot = CreateTempWorkRoot();
                try
                {
                    string copyPath = Path.Combine(workRoot, "monster2.bin");
                    File.Copy(absolutePath, copyPath, overwrite: true);

                    MonX_File file = MonX_File.Read(File.ReadAllBytes(copyPath));
                    if (file.Entries.Count < 2)
                        throw new InvalidDataException("Monster localization smoke expected at least 2 entries.");

                    string nameMarker = BuildMarker("MON");
                    string sensorMarker = BuildMarker("SENSOR");
                    string scanMarker = BuildMarker("SCAN");

                    file.Entries[1].Name = nameMarker;
                    file.Entries[1].Sensor = sensorMarker;
                    file.Entries[1].Scan = scanMarker;
                    File.WriteAllBytes(copyPath, file.Write());

                    MonX_File reread = MonX_File.Read(File.ReadAllBytes(copyPath));
                    if (!string.Equals(reread.Entries[1].Name, nameMarker, StringComparison.Ordinal) ||
                        !string.Equals(reread.Entries[1].Sensor, sensorMarker, StringComparison.Ordinal) ||
                        !string.Equals(reread.Entries[1].Scan, scanMarker, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException("Monster localization smoke failed to persist Name/Sensor/Scan after reread.");
                    }

                    checks.Add(Pass("Mutation Reread", $"Name/Sensor/Scan persisted as `{nameMarker}` / `{sensorMarker}` / `{scanMarker}`."));
                }
                finally
                {
                    DeleteTempWorkRoot(workRoot);
                }
            });
    }

    ProductionSafetyCaseResult RunKeyItemReadGuard()
    {
        string relativePath = Path.Combine("new_uspc", "battle", "kernel", "important.bin");
        string absolutePath = Path.Combine(options.MasterPath, relativePath);

        return RunCase(
            "Pt21 Guard - important.bin",
            "pt21-structural-readiness",
            [relativePath],
            checks =>
            {
                byte[] originalBytes = File.ReadAllBytes(absolutePath);
                KeyItemTable table = KeyItem_File.Read(originalBytes);
                checks.Add(Pass("Read Guard", "important.bin read closed successfully on the current corpus."));

                byte[] rebuiltBytes = KeyItem_File.Write(table);
                EnsureByteIdentity(originalBytes, rebuiltBytes, "important.bin no-edit round-trip drifted.");
                checks.Add(Pass("No-Edit Byte Identity", "important.bin no-edit write preserved exact byte identity."));

                KeyItemTable reread = KeyItem_File.Read(rebuiltBytes);
                checks.Add(Pass("Reread", $"important.bin reread closed on {reread.Entries.Count} entries after byte-identical rebuild."));
            },
            notes =>
            {
                notes.Add("Pt21 repurposes the old known-fail guard into a conservative readiness check.");
                notes.Add("This case proves reader closure plus no-edit byte identity only. It does not prove mutation-safe writer status.");
            });
    }

    ProductionSafetyCaseResult RunAutoAbilityReadGuard()
    {
        string abilityRelativePath = Path.Combine("new_uspc", "battle", "kernel", "a_ability.bin");
        string priceRelativePath = Path.Combine("jppc", "battle", "kernel", "arms_rate.bin");
        string abilityAbsolutePath = Path.Combine(options.MasterPath, abilityRelativePath);
        string priceAbsolutePath = Path.Combine(options.MasterPath, priceRelativePath);

        return RunCase(
            "Pt21 Guard - a_ability.bin + arms_rate.bin",
            "pt21-structural-readiness",
            [abilityRelativePath, priceRelativePath],
            checks =>
            {
                byte[] originalAbilityBytes = File.ReadAllBytes(abilityAbsolutePath);
                byte[] originalPriceBytes = File.ReadAllBytes(priceAbsolutePath);
                AutoAbilityTable table = AutoAbility_File.Read(originalAbilityBytes, originalPriceBytes);
                checks.Add(Pass("Read Guard", "a_ability.bin + arms_rate.bin read closed successfully on the current corpus."));

                byte[] rebuiltAbilityBytes = AutoAbility_File.WriteAbilities(table);
                byte[] rebuiltPriceBytes = AutoAbility_File.WritePrices(table);
                EnsureByteIdentity(originalAbilityBytes, rebuiltAbilityBytes, "a_ability.bin no-edit round-trip drifted.");
                EnsureByteIdentity(originalPriceBytes, rebuiltPriceBytes, "arms_rate.bin no-edit round-trip drifted.");
                checks.Add(Pass("No-Edit Byte Identity", "a_ability.bin + arms_rate.bin no-edit write preserved exact byte identity."));

                AutoAbilityTable reread = AutoAbility_File.Read(rebuiltAbilityBytes, rebuiltPriceBytes);
                checks.Add(Pass("Reread", $"a_ability.bin + arms_rate.bin reread closed on {reread.Entries.Count} aligned entries after byte-identical rebuild."));
            },
            notes =>
            {
                notes.Add("Pt21 repurposes the old known-fail guard into a conservative readiness check.");
                notes.Add("This case proves reader closure plus no-edit byte identity only. It does not prove mutation-safe writer status.");
            });
    }

    ProductionSafetyCaseResult RunCase(
        string displayName,
        string family,
        IReadOnlyList<string> relativePaths,
        Action<List<ProductionSafetyCheckResult>> executor,
        Action<List<string>>? noteBuilder = null)
    {
        List<ProductionSafetyCheckResult> checks = [];
        List<string> notes = [];

        noteBuilder?.Invoke(notes);

        try
        {
            executor(checks);
        }
        catch (Exception ex)
        {
            checks.Add(Fail("Case Failure", ex.Message));
        }

        return new ProductionSafetyCaseResult
        {
            DisplayName = displayName,
            Family = family,
            RelativePaths = relativePaths.ToArray(),
            Notes = notes,
            Checks = checks
        };
    }

    static string CreateTempWorkRoot()
    {
        string path = Path.Combine(Path.GetTempPath(), "ffxpe_prod_smoke_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    static void DeleteTempWorkRoot(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    static string BuildMarker(string prefix) => $"{prefix}_{DateTime.UtcNow:HHmmss}";

    static ProductionSafetyCheckResult Pass(string name, string details) => new() { Name = name, Result = "pass", Details = details };
    static ProductionSafetyCheckResult Fail(string name, string details) => new() { Name = name, Result = "fail", Details = details };

    static void EnsureByteIdentity(byte[] originalBytes, byte[] rebuiltBytes, string failureMessage)
    {
        if (originalBytes.Length != rebuiltBytes.Length)
        {
            throw new InvalidDataException(
                $"{failureMessage} Length changed from 0x{originalBytes.Length:X} to 0x{rebuiltBytes.Length:X}.");
        }

        for (int i = 0; i < originalBytes.Length; i++)
        {
            if (originalBytes[i] != rebuiltBytes[i])
            {
                throw new InvalidDataException(
                    $"{failureMessage} First diff at 0x{i:X}: original {originalBytes[i]:X2}, rebuilt {rebuiltBytes[i]:X2}.");
            }
        }
    }
}

sealed class ProductionSafetyRunResult
{
    public required string GeneratedUtc { get; init; }
    public required string MasterPath { get; init; }
    public required IReadOnlyList<string> GeneralNotes { get; init; }
    public required IReadOnlyList<ProductionSafetyCaseResult> Cases { get; init; }

    public int TotalCases => Cases.Count;
    public int PassedCases => Cases.Count(result => string.Equals(result.Status, "pass", StringComparison.Ordinal));
    public int WarningCases => Cases.Count(result => string.Equals(result.Status, "warning", StringComparison.Ordinal));
    public int FailedCases => Cases.Count(result => string.Equals(result.Status, "fail", StringComparison.Ordinal));
}

sealed class ProductionSafetyCaseResult
{
    public required string DisplayName { get; init; }
    public required string Family { get; init; }
    public required IReadOnlyList<string> RelativePaths { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
    public required IReadOnlyList<ProductionSafetyCheckResult> Checks { get; init; }

    public string Status
    {
        get
        {
            if (Checks.Any(check => string.Equals(check.Result, "fail", StringComparison.Ordinal)))
                return "fail";
            if (Checks.Any(check => string.Equals(check.Result, "warning", StringComparison.Ordinal)))
                return "warning";
            return "pass";
        }
    }
}

sealed class ProductionSafetyCheckResult
{
    public required string Name { get; init; }
    public required string Result { get; init; }
    public required string Details { get; init; }
}

static class ProductionSafetyMarkdown
{
    public static string Render(ProductionSafetyRunResult result)
    {
        StringBuilder sb = new();
        sb.AppendLine("# Production Save Safety Smoke");
        sb.AppendLine();
        sb.AppendLine($"- Generated UTC: `{result.GeneratedUtc}`");
        sb.AppendLine($"- Master Path: `{result.MasterPath}`");
        sb.AppendLine($"- Total Cases: `{result.TotalCases}`");
        sb.AppendLine($"- Pass: `{result.PassedCases}`");
        sb.AppendLine($"- Warning: `{result.WarningCases}`");
        sb.AppendLine($"- Fail: `{result.FailedCases}`");
        sb.AppendLine();

        if (result.GeneralNotes.Count > 0)
        {
            sb.AppendLine("## Run Notes");
            sb.AppendLine();
            foreach (string note in result.GeneralNotes)
                sb.AppendLine($"- {note}");

            sb.AppendLine();
        }

        sb.AppendLine("## Cases");
        sb.AppendLine();

        foreach (ProductionSafetyCaseResult resultCase in result.Cases)
        {
            sb.AppendLine($"### {resultCase.DisplayName}");
            sb.AppendLine();
            sb.AppendLine($"- Status: `{resultCase.Status}`");
            sb.AppendLine($"- Family: `{resultCase.Family}`");
            sb.AppendLine($"- Relative Paths: `{string.Join("`, `", resultCase.RelativePaths)}`");

            if (resultCase.Notes.Count > 0)
            {
                sb.AppendLine("- Notes:");
                foreach (string note in resultCase.Notes)
                    sb.AppendLine($"  - {note}");
            }

            sb.AppendLine();
            sb.AppendLine("| Check | Result | Details |");
            sb.AppendLine("| --- | --- | --- |");
            foreach (ProductionSafetyCheckResult check in resultCase.Checks)
            {
                sb.AppendLine($"| {Escape(check.Name)} | {check.Result} | {Escape(check.Details)} |");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    static string Escape(string value) => value.Replace("|", "\\|").Replace(Environment.NewLine, "<br/>");
}

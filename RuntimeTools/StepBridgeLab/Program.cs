using System.Text.Json;

namespace StepBridgeLab;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            var command = args[0].ToLowerInvariant();
            var options = ParseOptions(args.Skip(1).ToArray());

            return command switch
            {
                "ingest" => RunIngest(options),
                "generate" => RunGenerate(options),
                "query" => RunQuery(options),
                "smoke" => RunSmoke(options),
                _ => UnknownCommand(command),
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int RunIngest(IReadOnlyDictionary<string, string> options)
    {
        var inputPath = RequireOption(options, "input");
        var workspaceRoot = RequireOption(options, "workspace");
        var batchId = options.TryGetValue("batch", out var rawBatch) ? rawBatch : "manual";

        var parser = new GhidraXmlParser();
        var program = parser.Parse(inputPath);
        var ingest = ArtifactWorkspace.WriteIngest(workspaceRoot, inputPath, batchId, program);

        PrintJson(new
        {
            ingest.ProgramId,
            ingest.BatchId,
            ingest.RawInputPath,
            ingest.NormalizedPath,
            ingest.InputSha256,
            program.Stats,
        });

        return program.Stats.ErrorCount > 0 ? 2 : 0;
    }

    private static int RunGenerate(IReadOnlyDictionary<string, string> options)
    {
        var workspaceRoot = RequireOption(options, "workspace");
        var programId = RequireOption(options, "program");
        var batchId = RequireOption(options, "batch");

        var program = ArtifactWorkspace.LoadNormalizedProgram(workspaceRoot, programId, batchId);
        var generator = new CodeGenerator();
        var result = generator.Generate(workspaceRoot, program, batchId);

        PrintJson(new
        {
            result.ProgramId,
            result.BatchId,
            result.CatalogPath,
            result.ManifestPath,
            result.GeneratedFiles,
        });

        return 0;
    }

    private static int RunQuery(IReadOnlyDictionary<string, string> options)
    {
        var catalogPath = RequireOption(options, "catalog");
        var searchTerm = RequireOption(options, "symbol");
        var catalog = LoadCatalog(catalogPath);

        var entry = catalog.Entries.FirstOrDefault(item =>
            item.DisplayName.Equals(searchTerm, StringComparison.OrdinalIgnoreCase)
            || item.CanonicalKey.Equals(searchTerm, StringComparison.OrdinalIgnoreCase)
            || item.CanonicalKey.EndsWith($".{searchTerm.ToLowerInvariant()}", StringComparison.OrdinalIgnoreCase)
            || item.Aliases.Any(alias => alias.Equals(searchTerm, StringComparison.OrdinalIgnoreCase)));

        PrintJson(new QueryResult(searchTerm, entry, entry is not null));
        return entry is null ? 3 : 0;
    }

    private static int RunSmoke(IReadOnlyDictionary<string, string> options)
    {
        var inputPath = RequireOption(options, "input");
        var workspaceRoot = RequireOption(options, "workspace");
        var batchId = options.TryGetValue("batch", out var rawBatch) ? rawBatch : "smoke_fixture";
        var querySymbol = options.TryGetValue("symbol", out var rawSymbol) ? rawSymbol : "CdRead";

        var parser = new GhidraXmlParser();
        var program = parser.Parse(inputPath);
        var ingest = ArtifactWorkspace.WriteIngest(workspaceRoot, inputPath, batchId, program);

        var generator = new CodeGenerator();
        var generation = generator.Generate(workspaceRoot, program, ingest.BatchId);
        var catalog = LoadCatalog(generation.CatalogPath);
        var queryMatch = catalog.Entries.FirstOrDefault(item => item.DisplayName.Equals(querySymbol, StringComparison.OrdinalIgnoreCase));

        var report = new SmokeReport(
            program.ProgramId,
            ingest.BatchId,
            program.Stats.FunctionCount,
            program.Stats.SymbolCount,
            program.Stats.WarningCount,
            generation.CatalogPath,
            querySymbol,
            queryMatch is not null);

        ArtifactWorkspace.WriteJson(ArtifactWorkspace.SmokeReportPath(workspaceRoot, program.ProgramId, ingest.BatchId), report);

        PrintJson(new
        {
            ingest.ProgramId,
            ingest.BatchId,
            ingest.NormalizedPath,
            generation.CatalogPath,
            report.QueryTerm,
            report.QueryFound,
            program.Stats,
        });

        return report.QueryFound ? 0 : 4;
    }

    private static CatalogDocument LoadCatalog(string catalogPath)
    {
        var json = File.ReadAllText(catalogPath);
        return JsonSerializer.Deserialize<CatalogDocument>(json, ArtifactWorkspace.JsonOptions)
            ?? throw new InvalidOperationException($"Could not deserialize catalog: {catalogPath}");
    }

    private static IReadOnlyDictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected token '{token}'. Expected --name value pairs.");
            }

            if (i + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for option '{token}'.");
            }

            options[token[2..]] = args[++i];
        }

        return options;
    }

    private static string RequireOption(IReadOnlyDictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value)
            ? value
            : throw new ArgumentException($"Missing required option --{name}.");

    private static void PrintJson<T>(T value) =>
        Console.WriteLine(JsonSerializer.Serialize(value, ArtifactWorkspace.JsonOptions));

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("StepBridgeLab commands:");
        Console.WriteLine("  ingest --input <program.xml> --workspace <dir> [--batch <id>]");
        Console.WriteLine("  generate --workspace <dir> --program <programId> --batch <id>");
        Console.WriteLine("  query --catalog <symbol-catalog.json> --symbol <name-or-key>");
        Console.WriteLine("  smoke --input <program.xml> --workspace <dir> [--batch <id>] [--symbol <name>]");
    }
}

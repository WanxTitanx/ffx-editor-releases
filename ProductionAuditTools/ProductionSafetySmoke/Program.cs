using System.Text;
using System.Text.Json;

Console.OutputEncoding = Encoding.UTF8;

try
{
    ProductionSafetySmokeOptions options = ProductionSafetySmokeOptions.Parse(args);
    ProductionSafetyRunResult result = new ProductionSafetySmokeRunner(options).Run();

    if (!string.IsNullOrWhiteSpace(options.ReportPath))
    {
        EnsureParentDirectory(options.ReportPath);
        File.WriteAllText(options.ReportPath, ProductionSafetyMarkdown.Render(result), new UTF8Encoding(false));
        Console.WriteLine($"Save safety report: {options.ReportPath}");
    }

    if (!string.IsNullOrWhiteSpace(options.JsonPath))
    {
        EnsureParentDirectory(options.JsonPath);
        File.WriteAllText(
            options.JsonPath,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
        Console.WriteLine($"Save safety json: {options.JsonPath}");
    }

    Console.WriteLine(
        $"Cases: {result.TotalCases} | Pass: {result.PassedCases} | Warning: {result.WarningCases} | Fail: {result.FailedCases}");
    Environment.ExitCode = result.FailedCases > 0 ? 1 : 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    Environment.ExitCode = 1;
}

static void EnsureParentDirectory(string path)
{
    string? directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrWhiteSpace(directory))
    {
        Directory.CreateDirectory(directory);
    }
}

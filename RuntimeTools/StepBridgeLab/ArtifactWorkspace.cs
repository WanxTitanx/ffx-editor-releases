using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StepBridgeLab;

public static class ArtifactWorkspace
{
    public const string SchemaVersion = "1.0";
    public const string GeneratorVersion = "0.1.0-pt12";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string NormalizeWorkspaceRoot(string workspaceRoot) =>
        Path.GetFullPath(workspaceRoot);

    public static string NormalizeBatchId(string? batchId)
    {
        var trimmed = string.IsNullOrWhiteSpace(batchId) ? "manual" : batchId.Trim();
        return new string(trimmed.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-').ToArray());
    }

    public static string RawInputPath(string workspaceRoot, string programId, string batchId) =>
        Path.Combine(NormalizeWorkspaceRoot(workspaceRoot), "inputs", "ghidra", programId, "raw", batchId, "program.xml");

    public static string NormalizedProgramPath(string workspaceRoot, string programId, string batchId) =>
        Path.Combine(NormalizeWorkspaceRoot(workspaceRoot), "inputs", "ghidra", programId, "normalized", batchId, "program.json");

    public static string GeneratedRoot(string workspaceRoot, string programId, string batchId) =>
        Path.Combine(NormalizeWorkspaceRoot(workspaceRoot), "generated", programId, batchId);

    public static string CatalogPath(string workspaceRoot, string programId, string batchId) =>
        Path.Combine(GeneratedRoot(workspaceRoot, programId, batchId), "catalog", "symbol-catalog.json");

    public static string SmokeReportPath(string workspaceRoot, string programId, string batchId) =>
        Path.Combine(GeneratedRoot(workspaceRoot, programId, batchId), "reports", "smoke-report.json");

    public static IngestResult WriteIngest(string workspaceRoot, string inputPath, string batchId, NormalizedProgram program)
    {
        batchId = NormalizeBatchId(batchId);
        workspaceRoot = NormalizeWorkspaceRoot(workspaceRoot);

        var rawPath = RawInputPath(workspaceRoot, program.ProgramId, batchId);
        var normalizedPath = NormalizedProgramPath(workspaceRoot, program.ProgramId, batchId);

        Directory.CreateDirectory(Path.GetDirectoryName(rawPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(normalizedPath)!);

        File.Copy(Path.GetFullPath(inputPath), rawPath, true);
        File.WriteAllText(normalizedPath, JsonSerializer.Serialize(program, JsonOptions), Encoding.UTF8);

        return new IngestResult(
            program.ProgramId,
            batchId,
            rawPath,
            normalizedPath,
            ComputeSha256(rawPath));
    }

    public static NormalizedProgram LoadNormalizedProgram(string workspaceRoot, string programId, string batchId)
    {
        var path = NormalizedProgramPath(workspaceRoot, programId, NormalizeBatchId(batchId));
        var json = File.ReadAllText(path, Encoding.UTF8);
        return JsonSerializer.Deserialize<NormalizedProgram>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize normalized program: {path}");
    }

    public static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8);
    }

    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }

    public static string ComputeSha256FromText(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(bytes));
    }
}

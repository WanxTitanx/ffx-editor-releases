using System.Text.Json.Serialization;

namespace StepBridgeLab;

public sealed record NormalizedProgram(
    string ProgramId,
    string ProgramName,
    string SourceFormat,
    string Architecture,
    string Endianness,
    ulong? ImageBase,
    string? LanguageId,
    string? CompilerSpecId,
    IReadOnlyList<NormalizedFunction> Functions,
    IReadOnlyList<NormalizedSymbol> Symbols,
    IReadOnlyList<NormalizedReference> References,
    IReadOnlyList<ulong> EntryPoints,
    IReadOnlyList<ImportIssue> Issues,
    ImportStats Stats);

public sealed record NormalizedFunction(
    string StableId,
    string CanonicalKey,
    string Name,
    ulong? EntryAddress,
    string ReturnType,
    string CallingConvention,
    IReadOnlyList<NormalizedParameter> Parameters,
    SignatureConfidence Confidence,
    SymbolOrigin Origin,
    bool IsThunk,
    bool IsExternal);

public sealed record NormalizedParameter(
    int Ordinal,
    string Name,
    string DataType);

public sealed record NormalizedSymbol(
    string StableId,
    string CanonicalKey,
    string Name,
    string Kind,
    ulong? Address,
    string? Namespace,
    SignatureConfidence Confidence,
    SymbolOrigin Origin);

public sealed record NormalizedReference(
    ulong From,
    ulong To,
    string Type);

public sealed record ImportIssue(
    ImportSeverity Severity,
    string Code,
    string Message,
    string? EntityId = null,
    string? SourcePath = null,
    string? FallbackApplied = null);

public sealed record ImportStats(
    int FunctionCount,
    int SymbolCount,
    int EntryPointCount,
    int ReferenceCount,
    int WarningCount,
    int ErrorCount);

public sealed record IngestResult(
    string ProgramId,
    string BatchId,
    string RawInputPath,
    string NormalizedPath,
    string InputSha256);

public sealed record GenerationResult(
    string ProgramId,
    string BatchId,
    string CatalogPath,
    IReadOnlyList<string> GeneratedFiles,
    string ManifestPath);

public sealed record CatalogDocument(
    string SchemaVersion,
    string CatalogVersion,
    string ProgramId,
    string ProgramName,
    string SourceFormat,
    string GeneratedAtUtc,
    IReadOnlyList<CatalogEntry> Entries);

public sealed record CatalogEntry(
    string CanonicalKey,
    string DisplayName,
    string Kind,
    ulong? Address,
    string StableId,
    string? Namespace,
    string? ReturnType,
    IReadOnlyList<string> ParameterTypes,
    IReadOnlyList<string> Aliases,
    SignatureConfidence Confidence,
    AddressBinding? Binding,
    IReadOnlyList<string> Flags);

public sealed record AddressBinding(
    string AddressType,
    string Value,
    string ResolverVersion,
    string Confidence);

public sealed record QueryResult(
    string SearchTerm,
    CatalogEntry? Entry,
    bool Found);

public sealed record SmokeReport(
    string ProgramId,
    string BatchId,
    int FunctionCount,
    int SymbolCount,
    int WarningCount,
    string CatalogPath,
    string QueryTerm,
    bool QueryFound);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ImportSeverity
{
    Warning,
    Error,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SignatureConfidence
{
    Low,
    Medium,
    High,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SymbolOrigin
{
    Imported,
    Inferred,
    Synthetic,
}

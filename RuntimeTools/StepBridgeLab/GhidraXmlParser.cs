using System.Xml.Linq;

namespace StepBridgeLab;

public sealed class GhidraXmlParser
{
    public NormalizedProgram Parse(string inputPath)
    {
        var xml = File.ReadAllText(inputPath);
        var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var issues = new List<ImportIssue>();

        var programNode = FindProgramNode(document.Root);
        var programName = FirstNonBlank(
            GetAttribute(programNode, "NAME", "PROGRAM_NAME"),
            GetChildValue(programNode, "NAME")) ?? "unnamed_program";

        if (programName == "unnamed_program")
        {
            issues.Add(new ImportIssue(
                ImportSeverity.Warning,
                "missing-program-name",
                "PROGRAM name was missing; using fallback name.",
                SourcePath: "PROGRAM",
                FallbackApplied: programName));
        }

        var programId = BuildProgramId(programName, xml);
        var languageNode = DescendantsNamed(programNode, "LANGUAGE").FirstOrDefault();

        var architecture = FirstNonBlank(
            GetAttribute(languageNode, "PROCESSOR", "PROCESSOR_NAME", "FAMILY"),
            GetAttribute(programNode, "PROCESSOR", "PROCESSOR_NAME", "FAMILY")) ?? "unknown";

        var endianness = FirstNonBlank(
            GetAttribute(languageNode, "ENDIAN", "ENDIANNESS"),
            GetAttribute(programNode, "ENDIAN", "ENDIANNESS")) ?? "unknown";

        var languageId = FirstNonBlank(
            GetAttribute(languageNode, "LANGUAGE_ID", "LANGUAGEID"),
            GetAttribute(programNode, "LANGUAGE_ID", "LANGUAGEID"));

        var compilerSpecId = FirstNonBlank(
            GetAttribute(languageNode, "COMPILER_SPEC_ID", "COMPILERSPECID"),
            GetAttribute(programNode, "COMPILER_SPEC_ID", "COMPILERSPECID"));

        var imageBase = ParseAddress(
            FirstNonBlank(
                GetAttribute(programNode, "IMAGE_BASE", "IMAGEBASE"),
                DescendantsNamed(programNode, "IMAGE_BASE").Select(node => FirstNonBlank(
                    GetAttribute(node, "VALUE"),
                    node.Value)).FirstOrDefault()),
            issues,
            "PROGRAM/IMAGE_BASE");

        var functions = ParseFunctions(programNode, programId, issues);
        var functionAddresses = new HashSet<ulong>(functions.Where(item => item.EntryAddress.HasValue).Select(item => item.EntryAddress!.Value));
        var symbols = ParseSymbols(programNode, programId, functionAddresses, issues);
        var references = ParseReferences(programNode, issues);
        var entryPoints = ParseEntryPoints(programNode, issues);

        functions = Deduplicate(
            functions,
            item => item.StableId,
            issues,
            "function-duplicate-stable-id",
            item => item.StableId);

        symbols = Deduplicate(
            symbols,
            item => item.StableId,
            issues,
            "symbol-duplicate-stable-id",
            item => item.StableId);

        if (architecture == "unknown")
        {
            issues.Add(new ImportIssue(
                ImportSeverity.Warning,
                "missing-architecture",
                "Architecture could not be read from LANGUAGE/PROGRAM metadata.",
                SourcePath: "LANGUAGE"));
        }

        if (endianness == "unknown")
        {
            issues.Add(new ImportIssue(
                ImportSeverity.Warning,
                "missing-endianness",
                "Endianness could not be read from LANGUAGE/PROGRAM metadata.",
                SourcePath: "LANGUAGE"));
        }

        var stats = new ImportStats(
            functions.Count,
            symbols.Count,
            entryPoints.Count,
            references.Count,
            issues.Count(issue => issue.Severity == ImportSeverity.Warning),
            issues.Count(issue => issue.Severity == ImportSeverity.Error));

        return new NormalizedProgram(
            programId,
            programName,
            "ghidra.program.xml",
            architecture,
            endianness,
            imageBase,
            languageId,
            compilerSpecId,
            functions,
            symbols,
            references,
            entryPoints,
            issues,
            stats);
    }

    private static XElement FindProgramNode(XElement? root)
    {
        if (root is null)
        {
            throw new InvalidOperationException("XML document has no root element.");
        }

        return IsNamed(root, "PROGRAM")
            ? root
            : root.Descendants().FirstOrDefault(node => IsNamed(node, "PROGRAM")) ?? root;
    }

    private static List<NormalizedFunction> ParseFunctions(XElement programNode, string programId, List<ImportIssue> issues)
    {
        var functions = new List<NormalizedFunction>();
        var unnamedCounter = 0;

        foreach (var functionNode in DescendantsNamed(programNode, "FUNCTION"))
        {
            var rawName = FirstNonBlank(
                GetAttribute(functionNode, "NAME", "SYMBOL_NAME"),
                GetChildValue(functionNode, "NAME"));

            var address = ParseAddress(
                FirstNonBlank(
                    GetAttribute(functionNode, "ENTRY_POINT", "ENTRY", "ADDRESS", "START", "OFFSET"),
                    GetChildValue(functionNode, "ENTRY_POINT")),
                issues,
                "FUNCTION");

            var functionName = rawName;
            if (string.IsNullOrWhiteSpace(functionName))
            {
                functionName = address.HasValue
                    ? $"sub_{address.Value:x8}"
                    : $"unnamed_function_{++unnamedCounter}";

                issues.Add(new ImportIssue(
                    ImportSeverity.Warning,
                    "missing-function-name",
                    "Function name was missing; using fallback name.",
                    SourcePath: "FUNCTION",
                    FallbackApplied: functionName));
            }

            if (!address.HasValue)
            {
                issues.Add(new ImportIssue(
                    ImportSeverity.Warning,
                    "missing-function-address",
                    $"Function '{functionName}' had no readable entry address.",
                    EntityId: functionName,
                    SourcePath: "FUNCTION"));
            }

            var parameters = ParseParameters(functionNode);
            var returnType = FirstNonBlank(
                GetAttribute(functionNode, "RETURN_TYPE", "RETURNTYPE"),
                GetChildValue(functionNode, "RETURN_TYPE")) ?? "unknown";

            var callingConvention = FirstNonBlank(
                GetAttribute(functionNode, "CALLING_CONVENTION", "CONVENTION"),
                GetChildValue(functionNode, "CALLING_CONVENTION")) ?? "unknown";

            var origin = IsAutoNamed(functionName) ? SymbolOrigin.Inferred : SymbolOrigin.Imported;
            var confidence = CalculateConfidence(functionName, returnType, parameters);
            var stableId = address.HasValue
                ? $"fn_{programId}_{address.Value:x}"
                : $"fn_{programId}_{Slug(functionName)}";
            var canonicalToken = IsAutoNamed(functionName) && address.HasValue
                ? $"addr_{address.Value:x}"
                : functionName;

            if (returnType.Equals("unknown", StringComparison.OrdinalIgnoreCase)
                || parameters.Count == 0)
            {
                issues.Add(new ImportIssue(
                    ImportSeverity.Warning,
                    "incomplete-function-signature",
                    $"Function '{functionName}' has incomplete signature metadata; codegen should prefer descriptor/fallback mode.",
                    EntityId: stableId,
                    SourcePath: "FUNCTION",
                    FallbackApplied: "BindingDescriptor + RawCall"));
            }

            functions.Add(new NormalizedFunction(
                stableId,
                BuildCanonicalKey(programId, "function", canonicalToken),
                functionName,
                address,
                returnType,
                callingConvention,
                parameters,
                confidence,
                origin,
                ParseBoolean(GetAttribute(functionNode, "THUNK", "IS_THUNK")),
                ParseBoolean(GetAttribute(functionNode, "EXTERNAL", "IS_EXTERNAL"))));
        }

        return functions;
    }

    private static IReadOnlyList<NormalizedParameter> ParseParameters(XElement functionNode)
    {
        var parameters = new List<NormalizedParameter>();
        var index = 0;

        foreach (var parameterNode in DescendantsNamed(functionNode, "PARAMETER"))
        {
            var ordinalText = FirstNonBlank(GetAttribute(parameterNode, "ORDINAL", "INDEX"));
            var ordinal = int.TryParse(ordinalText, out var parsedOrdinal) ? parsedOrdinal : index;
            var name = FirstNonBlank(GetAttribute(parameterNode, "NAME"), parameterNode.Value) ?? $"arg{ordinal}";
            var dataType = FirstNonBlank(
                GetAttribute(parameterNode, "DATATYPE", "DATA_TYPE", "TYPE"),
                GetChildValue(parameterNode, "DATATYPE")) ?? "unknown";

            parameters.Add(new NormalizedParameter(ordinal, name, dataType));
            index++;
        }

        return parameters;
    }

    private static List<NormalizedSymbol> ParseSymbols(
        XElement programNode,
        string programId,
        HashSet<ulong> functionAddresses,
        List<ImportIssue> issues)
    {
        var symbols = new List<NormalizedSymbol>();
        var unnamedCounter = 0;

        foreach (var symbolNode in DescendantsNamed(programNode, "SYMBOL"))
        {
            var rawType = FirstNonBlank(GetAttribute(symbolNode, "TYPE", "SYMBOL_TYPE")) ?? "LABEL";
            var address = ParseAddress(
                FirstNonBlank(
                    GetAttribute(symbolNode, "ADDRESS", "ENTRY_POINT", "OFFSET"),
                    GetChildValue(symbolNode, "ADDRESS")),
                issues,
                "SYMBOL");

            if (rawType.Equals("FUNCTION", StringComparison.OrdinalIgnoreCase)
                && address.HasValue
                && functionAddresses.Contains(address.Value))
            {
                continue;
            }

            var name = FirstNonBlank(
                GetAttribute(symbolNode, "NAME", "SYMBOL_NAME"),
                symbolNode.Value);

            if (string.IsNullOrWhiteSpace(name))
            {
                name = address.HasValue
                    ? $"label_{address.Value:x8}"
                    : $"unnamed_symbol_{++unnamedCounter}";

                issues.Add(new ImportIssue(
                    ImportSeverity.Warning,
                    "missing-symbol-name",
                    "Symbol name was missing; using fallback name.",
                    SourcePath: "SYMBOL",
                    FallbackApplied: name));
            }

            var kind = MapSymbolKind(rawType);
            var stableId = address.HasValue
                ? $"sym_{programId}_{kind}_{address.Value:x}"
                : $"sym_{programId}_{kind}_{Slug(name)}";

            symbols.Add(new NormalizedSymbol(
                stableId,
                BuildCanonicalKey(programId, kind, name),
                name,
                kind,
                address,
                FirstNonBlank(GetAttribute(symbolNode, "NAMESPACE")),
                IsAutoNamed(name) ? SignatureConfidence.Low : SignatureConfidence.Medium,
                IsAutoNamed(name) ? SymbolOrigin.Inferred : SymbolOrigin.Imported));
        }

        return symbols;
    }

    private static List<NormalizedReference> ParseReferences(XElement programNode, List<ImportIssue> issues)
    {
        var references = new List<NormalizedReference>();

        foreach (var referenceNode in DescendantsNamed(programNode, "REFERENCE"))
        {
            var from = ParseAddress(FirstNonBlank(
                GetAttribute(referenceNode, "FROM", "FROM_ADDRESS", "SOURCE"),
                GetChildValue(referenceNode, "FROM")), issues, "REFERENCE/FROM");

            var to = ParseAddress(FirstNonBlank(
                GetAttribute(referenceNode, "TO", "TO_ADDRESS", "TARGET"),
                GetChildValue(referenceNode, "TO")), issues, "REFERENCE/TO");

            if (!from.HasValue || !to.HasValue)
            {
                issues.Add(new ImportIssue(
                    ImportSeverity.Warning,
                    "invalid-reference",
                    "Reference was skipped because FROM or TO could not be parsed.",
                    SourcePath: "REFERENCE"));
                continue;
            }

            references.Add(new NormalizedReference(
                from.Value,
                to.Value,
                FirstNonBlank(GetAttribute(referenceNode, "TYPE", "REF_TYPE")) ?? "UNKNOWN"));
        }

        return references;
    }

    private static List<ulong> ParseEntryPoints(XElement programNode, List<ImportIssue> issues)
    {
        var entryPoints = new List<ulong>();

        foreach (var entryNode in DescendantsNamed(programNode, "ENTRY_POINT"))
        {
            var address = ParseAddress(
                FirstNonBlank(GetAttribute(entryNode, "ADDRESS", "ENTRY_POINT"), entryNode.Value),
                issues,
                "ENTRY_POINT");

            if (address.HasValue)
            {
                entryPoints.Add(address.Value);
            }
        }

        return entryPoints.Distinct().OrderBy(value => value).ToList();
    }

    private static List<T> Deduplicate<T>(
        IEnumerable<T> items,
        Func<T, string> keySelector,
        List<ImportIssue> issues,
        string issueCode,
        Func<T, string> entitySelector)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<T>();

        foreach (var item in items)
        {
            var key = keySelector(item);
            if (seen.Add(key))
            {
                unique.Add(item);
                continue;
            }

            issues.Add(new ImportIssue(
                ImportSeverity.Error,
                issueCode,
                $"Duplicate entity key '{key}' was rejected.",
                EntityId: entitySelector(item)));
        }

        return unique;
    }

    private static SignatureConfidence CalculateConfidence(
        string functionName,
        string returnType,
        IReadOnlyList<NormalizedParameter> parameters)
    {
        if (IsAutoNamed(functionName))
        {
            return SignatureConfidence.Low;
        }

        if (returnType.Equals("unknown", StringComparison.OrdinalIgnoreCase)
            || parameters.Any(parameter => parameter.DataType.Equals("unknown", StringComparison.OrdinalIgnoreCase)))
        {
            return SignatureConfidence.Medium;
        }

        return SignatureConfidence.High;
    }

    private static string BuildProgramId(string programName, string xml)
    {
        var slug = Slug(programName);
        if (!string.IsNullOrWhiteSpace(slug))
        {
            return slug;
        }

        return $"program-{ArtifactWorkspace.ComputeSha256FromText(xml)[..8].ToLowerInvariant()}";
    }

    private static string BuildCanonicalKey(string programId, string kind, string name) =>
        $"ffx.{Slug(programId)}.{Slug(kind)}.{Slug(name)}";

    private static string MapSymbolKind(string rawType)
    {
        if (rawType.Equals("FUNCTION", StringComparison.OrdinalIgnoreCase))
        {
            return "function";
        }

        if (rawType.Equals("GLOBAL", StringComparison.OrdinalIgnoreCase)
            || rawType.Equals("LABEL", StringComparison.OrdinalIgnoreCase)
            || rawType.Equals("DATA", StringComparison.OrdinalIgnoreCase))
        {
            return "global";
        }

        if (rawType.Equals("TYPE", StringComparison.OrdinalIgnoreCase))
        {
            return "type";
        }

        return Slug(rawType);
    }

    private static bool ParseBoolean(string? value) =>
        value is not null
        && (value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("y", StringComparison.OrdinalIgnoreCase)
            || value == "1");

    private static bool IsAutoNamed(string value) =>
        value.StartsWith("FUN_", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("LAB_", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("sub_", StringComparison.OrdinalIgnoreCase);

    private static ulong? ParseAddress(string? value, List<ImportIssue> issues, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[2..];
        }

        if (ulong.TryParse(normalized, System.Globalization.NumberStyles.HexNumber, null, out var hexValue))
        {
            return hexValue;
        }

        if (ulong.TryParse(normalized, out var decimalValue))
        {
            return decimalValue;
        }

        issues.Add(new ImportIssue(
            ImportSeverity.Warning,
            "invalid-address",
            $"Could not parse address value '{value}'.",
            SourcePath: sourcePath));

        return null;
    }

    private static string Slug(string value)
    {
        var builder = new List<char>();
        var previousWasLowerOrDigit = false;

        foreach (var ch in value.Trim())
        {
            if (!char.IsLetterOrDigit(ch))
            {
                builder.Add('_');
                previousWasLowerOrDigit = false;
                continue;
            }

            if (char.IsUpper(ch) && previousWasLowerOrDigit)
            {
                builder.Add('_');
            }

            builder.Add(char.ToLowerInvariant(ch));
            previousWasLowerOrDigit = char.IsLower(ch) || char.IsDigit(ch);
        }

        var cleaned = new string(builder.ToArray());

        while (cleaned.Contains("__", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("__", "_", StringComparison.Ordinal);
        }

        cleaned = cleaned.Trim('_');
        return string.IsNullOrWhiteSpace(cleaned) ? "unknown" : cleaned;
    }

    private static IEnumerable<XElement> DescendantsNamed(XContainer? container, string name) =>
        container?.Descendants().Where(node => IsNamed(node, name)) ?? Enumerable.Empty<XElement>();

    private static bool IsNamed(XElement node, string name) =>
        node.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase);

    private static string? GetAttribute(XElement? element, params string[] names)
    {
        if (element is null)
        {
            return null;
        }

        foreach (var name in names)
        {
            var attribute = element.Attributes()
                .FirstOrDefault(item => item.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(attribute?.Value))
            {
                return attribute.Value;
            }
        }

        return null;
    }

    private static string? GetChildValue(XElement? element, string childName)
    {
        if (element is null)
        {
            return null;
        }

        var child = element.Elements().FirstOrDefault(node => IsNamed(node, childName));
        return string.IsNullOrWhiteSpace(child?.Value) ? null : child.Value.Trim();
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}

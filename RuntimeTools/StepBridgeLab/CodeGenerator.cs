using System.Text;

namespace StepBridgeLab;

public sealed class CodeGenerator
{
    public GenerationResult Generate(string workspaceRoot, NormalizedProgram program, string batchId)
    {
        batchId = ArtifactWorkspace.NormalizeBatchId(batchId);
        var generatedRoot = ArtifactWorkspace.GeneratedRoot(workspaceRoot, program.ProgramId, batchId);
        var catalogDir = Path.Combine(generatedRoot, "catalog");
        var codeDir = Path.Combine(generatedRoot, "code", "bindings");
        var manifestDir = Path.Combine(generatedRoot, "manifests");
        var reportDir = Path.Combine(generatedRoot, "reports");

        Directory.CreateDirectory(catalogDir);
        Directory.CreateDirectory(codeDir);
        Directory.CreateDirectory(manifestDir);
        Directory.CreateDirectory(reportDir);

        var members = BuildMembers(program);
        var catalog = new CatalogDocument(
            ArtifactWorkspace.SchemaVersion,
            ArtifactWorkspace.GeneratorVersion,
            program.ProgramId,
            program.ProgramName,
            program.SourceFormat,
            DateTime.UtcNow.ToString("O"),
            members.Select(member => member.Entry).ToList());

        var catalogPath = Path.Combine(catalogDir, "symbol-catalog.json");
        ArtifactWorkspace.WriteJson(catalogPath, catalog);

        var generatedFiles = new List<string>
        {
            WriteFile(Path.Combine(codeDir, "StepBridgeIds.g.cs"), BuildIdsFile(members)),
            WriteFile(Path.Combine(codeDir, "StepBridgeAddresses.g.cs"), BuildAddressesFile(members)),
            WriteFile(Path.Combine(codeDir, "StepBridgeDelegates.g.cs"), BuildDelegatesFile(members)),
            WriteFile(Path.Combine(codeDir, "StepBridgeBindings.g.cs"), BuildBindingsFile(members)),
            WriteFile(Path.Combine(codeDir, "StepBridgeDescriptors.g.cs"), BuildDescriptorsFile(members)),
        };

        var manifestPath = Path.Combine(manifestDir, "generation.manifest.json");
        var manifest = new
        {
            schemaVersion = ArtifactWorkspace.SchemaVersion,
            generatorVersion = ArtifactWorkspace.GeneratorVersion,
            generatedAtUtc = DateTime.UtcNow.ToString("O"),
            programId = program.ProgramId,
            programName = program.ProgramName,
            batchId,
            sourceFormat = program.SourceFormat,
            normalizedInputPath = ArtifactWorkspace.NormalizedProgramPath(workspaceRoot, program.ProgramId, batchId),
            rawInputPath = ArtifactWorkspace.RawInputPath(workspaceRoot, program.ProgramId, batchId),
            generatedFiles = generatedFiles.Select(path => new
            {
                path,
                sha256 = ArtifactWorkspace.ComputeSha256(path),
            }).ToList(),
            warningCount = program.Stats.WarningCount,
            errorCount = program.Stats.ErrorCount,
        };

        ArtifactWorkspace.WriteJson(manifestPath, manifest);

        var summaryPath = Path.Combine(reportDir, "generation-summary.json");
        ArtifactWorkspace.WriteJson(summaryPath, new
        {
            program.ProgramId,
            batchId,
            entries = members.Count,
            typedDelegates = members.Count(member => member.EmitTypedDelegate),
            fallbackOnly = members.Count(member => member.IsFunction && !member.EmitTypedDelegate),
            dataSymbols = members.Count(member => !member.IsFunction),
        });

        generatedFiles.Add(catalogPath);
        generatedFiles.Add(summaryPath);

        return new GenerationResult(program.ProgramId, batchId, catalogPath, generatedFiles, manifestPath);
    }

    private static List<GeneratedMember> BuildMembers(NormalizedProgram program)
    {
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        var members = new List<GeneratedMember>();

        foreach (var function in program.Functions.OrderBy(item => item.EntryAddress ?? ulong.MaxValue).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var name = UniqueMemberName(ToPascalCase(function.Name), usedNames, function.EntryAddress);
            var signature = BuildSignature(function);
            var flags = new List<string> { "Stable" };

            if (!signature.EmitTypedDelegate)
            {
                flags.Add("IncompleteSignature");
                flags.Add("DynamicFallbackOnly");
            }

            members.Add(new GeneratedMember(
                name,
                true,
                signature.EmitTypedDelegate,
                signature.DelegateName,
                function.Name,
                new CatalogEntry(
                    function.CanonicalKey,
                    function.Name,
                    "function",
                    function.EntryAddress,
                    function.StableId,
                    $"ffx.{program.ProgramId}.function",
                    function.ReturnType,
                    function.Parameters.Select(parameter => parameter.DataType).ToList(),
                    new List<string> { function.StableId },
                    function.Confidence,
                    function.EntryAddress.HasValue
                        ? new AddressBinding("absolute", $"0x{function.EntryAddress.Value:x}", ArtifactWorkspace.GeneratorVersion, function.Confidence.ToString())
                        : null,
                    flags),
                signature.ReturnTypeCs,
                signature.Parameters));
        }

        foreach (var symbol in program.Symbols.OrderBy(item => item.Address ?? ulong.MaxValue).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var name = UniqueMemberName(ToPascalCase(symbol.Name), usedNames, symbol.Address);

            members.Add(new GeneratedMember(
                name,
                false,
                false,
                null,
                symbol.Name,
                new CatalogEntry(
                    symbol.CanonicalKey,
                    symbol.Name,
                    symbol.Kind,
                    symbol.Address,
                    symbol.StableId,
                    symbol.Namespace,
                    null,
                    Array.Empty<string>(),
                    new List<string> { symbol.StableId },
                    symbol.Confidence,
                    symbol.Address.HasValue
                        ? new AddressBinding("absolute", $"0x{symbol.Address.Value:x}", ArtifactWorkspace.GeneratorVersion, symbol.Confidence.ToString())
                        : null,
                    new List<string> { "DataSymbol" }),
                null,
                Array.Empty<GeneratedParameter>()));
        }

        return members;
    }

    private static string BuildIdsFile(IEnumerable<GeneratedMember> members)
    {
        var builder = new StringBuilder();
        builder.AppendLine("namespace StepBridgeLab.Generated;");
        builder.AppendLine();
        builder.AppendLine("public static class StepBridgeIds");
        builder.AppendLine("{");
        foreach (var member in members)
        {
            builder.AppendLine($"    public const string {member.MemberName} = \"{member.Entry.CanonicalKey}\";");
        }
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string BuildAddressesFile(IEnumerable<GeneratedMember> members)
    {
        var builder = new StringBuilder();
        builder.AppendLine("namespace StepBridgeLab.Generated;");
        builder.AppendLine();
        builder.AppendLine("public static class StepBridgeAddresses");
        builder.AppendLine("{");
        foreach (var member in members.Where(item => item.Entry.Address.HasValue))
        {
            builder.AppendLine($"    public const ulong {member.MemberName} = 0x{member.Entry.Address!.Value:x}UL;");
        }
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string BuildDelegatesFile(IEnumerable<GeneratedMember> members)
    {
        var builder = new StringBuilder();
        builder.AppendLine("using System;");
        builder.AppendLine();
        builder.AppendLine("namespace StepBridgeLab.Generated;");
        builder.AppendLine();
        builder.AppendLine("public delegate IntPtr RawCallDelegate(IntPtr ctx, IntPtr[] args);");
        builder.AppendLine();

        foreach (var member in members.Where(item => item.EmitTypedDelegate))
        {
            var parameters = string.Join(", ", member.Parameters.Select(parameter => $"{parameter.CSharpType} {parameter.ParameterName}"));
            builder.AppendLine($"public delegate {member.ReturnTypeCs} {member.DelegateName}({parameters});");
        }

        return builder.ToString();
    }

    private static string BuildBindingsFile(IEnumerable<GeneratedMember> members)
    {
        var builder = new StringBuilder();
        builder.AppendLine("namespace StepBridgeLab.Generated;");
        builder.AppendLine();
        builder.AppendLine("public sealed class StepBridgeBindings");
        builder.AppendLine("{");

        foreach (var member in members.Where(item => item.EmitTypedDelegate))
        {
            builder.AppendLine($"    public {member.DelegateName}? {member.MemberName} {{ get; init; }}");
        }

        builder.AppendLine("    public RawCallDelegate? RawCall { get; init; }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string BuildDescriptorsFile(IEnumerable<GeneratedMember> members)
    {
        var builder = new StringBuilder();
        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine();
        builder.AppendLine("namespace StepBridgeLab.Generated;");
        builder.AppendLine();
        builder.AppendLine("[System.Flags]");
        builder.AppendLine("public enum BindingFlags");
        builder.AppendLine("{");
        builder.AppendLine("    None = 0,");
        builder.AppendLine("    Stable = 1 << 0,");
        builder.AppendLine("    Experimental = 1 << 1,");
        builder.AppendLine("    IncompleteSignature = 1 << 2,");
        builder.AppendLine("    DynamicFallbackOnly = 1 << 3,");
        builder.AppendLine("    DataSymbol = 1 << 4,");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("public sealed record BindingDescriptor(");
        builder.AppendLine("    string Id,");
        builder.AppendLine("    string DisplayName,");
        builder.AppendLine("    string Kind,");
        builder.AppendLine("    ulong? Address,");
        builder.AppendLine("    string ReturnType,");
        builder.AppendLine("    IReadOnlyList<string> ParameterTypes,");
        builder.AppendLine("    BindingFlags Flags,");
        builder.AppendLine("    string Confidence);");
        builder.AppendLine();
        builder.AppendLine("public static class StepBridgeDescriptors");
        builder.AppendLine("{");

        foreach (var member in members)
        {
            var parameterTypes = member.Entry.ParameterTypes.Count == 0
                ? "System.Array.Empty<string>()"
                : $"new[] {{ {string.Join(", ", member.Entry.ParameterTypes.Select(type => $"\"{type}\""))} }}";

            builder.AppendLine($"    public static readonly BindingDescriptor {member.MemberName} = new(");
            builder.AppendLine($"        StepBridgeIds.{member.MemberName},");
            builder.AppendLine($"        \"{member.DisplayName}\",");
            builder.AppendLine($"        \"{member.Entry.Kind}\",");
            builder.AppendLine(member.Entry.Address.HasValue
                ? $"        StepBridgeAddresses.{member.MemberName},"
                : "        null,");
            builder.AppendLine($"        \"{member.Entry.ReturnType ?? "n/a"}\",");
            builder.AppendLine($"        {parameterTypes},");
            builder.AppendLine($"        {BuildBindingFlags(member.Entry.Flags)},");
            builder.AppendLine($"        \"{member.Entry.Confidence}\");");
            builder.AppendLine();
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string BuildBindingFlags(IReadOnlyList<string> flags)
    {
        if (flags.Count == 0)
        {
            return "BindingFlags.None";
        }

        return string.Join(" | ", flags.Select(flag => $"BindingFlags.{flag}"));
    }

    private static string WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, Encoding.UTF8);
        return path;
    }

    private static GeneratedSignature BuildSignature(NormalizedFunction function)
    {
        var parameters = new List<GeneratedParameter>();
        var returnTypeCs = "IntPtr";
        var emitTypedDelegate = !function.Name.StartsWith("FUN_", StringComparison.OrdinalIgnoreCase)
            && TryMapType(function.ReturnType, out returnTypeCs);

        foreach (var parameter in function.Parameters.OrderBy(item => item.Ordinal))
        {
            var parameterName = ToCamelCase(parameter.Name);
            if (!TryMapType(parameter.DataType, out var parameterType))
            {
                emitTypedDelegate = false;
                parameterType = "IntPtr";
            }

            parameters.Add(new GeneratedParameter(parameterName, parameterType));
        }

        if (!emitTypedDelegate)
        {
            returnTypeCs = "IntPtr";
        }

        return new GeneratedSignature(
            emitTypedDelegate,
            emitTypedDelegate ? $"{ToPascalCase(function.Name)}Delegate" : null,
            returnTypeCs,
            parameters);
    }

    private static bool TryMapType(string rawType, out string mappedType)
    {
        var normalized = rawType.Trim().ToLowerInvariant();
        normalized = normalized.Replace("const ", string.Empty, StringComparison.Ordinal);

        if (normalized.Contains('*', StringComparison.Ordinal))
        {
            mappedType = "IntPtr";
            return true;
        }

        mappedType = normalized switch
        {
            "void" => "void",
            "bool" => "bool",
            "char" or "s8" or "int8_t" => "sbyte",
            "byte" or "u8" or "uint8_t" => "byte",
            "short" or "s16" or "int16_t" => "short",
            "ushort" or "u16" or "uint16_t" => "ushort",
            "int" or "s32" or "int32_t" or "long" => "int",
            "uint" or "u32" or "uint32_t" or "dword" => "uint",
            "float" => "float",
            "double" => "double",
            "intptr" or "intptr_t" => "IntPtr",
            "unknown" => string.Empty,
            _ => string.Empty,
        };

        return mappedType.Length > 0;
    }

    private static string UniqueMemberName(string baseName, HashSet<string> usedNames, ulong? address)
    {
        var candidate = string.IsNullOrWhiteSpace(baseName) ? "Unknown" : baseName;
        if (usedNames.Add(candidate))
        {
            return candidate;
        }

        candidate = address.HasValue ? $"{candidate}_{address.Value:x}" : $"{candidate}_Alt";
        usedNames.Add(candidate);
        return candidate;
    }

    private static string ToPascalCase(string value)
    {
        var pieces = value
            .Split(new[] { '_', '-', ' ', '.', ':' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(piece => piece.Trim())
            .Where(piece => piece.Length > 0)
            .Select(piece =>
            {
                if (piece.Length == 1)
                {
                    return piece.ToUpperInvariant();
                }

                var restHasMixedCase = piece.Skip(1).Any(char.IsUpper);
                return char.ToUpperInvariant(piece[0]) + (restHasMixedCase ? piece[1..] : piece[1..].ToLowerInvariant());
            });

        var joined = string.Concat(pieces);
        if (joined.Length == 0)
        {
            return "Unknown";
        }

        return char.IsDigit(joined[0]) ? $"_{joined}" : joined;
    }

    private static string ToCamelCase(string value)
    {
        var pascal = ToPascalCase(value);
        return pascal.Length == 1
            ? pascal.ToLowerInvariant()
            : char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }

    private sealed record GeneratedSignature(
        bool EmitTypedDelegate,
        string? DelegateName,
        string ReturnTypeCs,
        IReadOnlyList<GeneratedParameter> Parameters);

    private sealed record GeneratedParameter(
        string ParameterName,
        string CSharpType);

    private sealed record GeneratedMember(
        string MemberName,
        bool IsFunction,
        bool EmitTypedDelegate,
        string? DelegateName,
        string DisplayName,
        CatalogEntry Entry,
        string? ReturnTypeCs,
        IReadOnlyList<GeneratedParameter> Parameters);
}

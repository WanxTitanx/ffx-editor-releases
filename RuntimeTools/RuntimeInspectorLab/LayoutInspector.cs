using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using Xe.BinaryMapper;

namespace RuntimeInspectorLab;

internal static class ProgramEntry
{
    public static int Run(string[] args)
    {
        if (!CliOptions.TryParse(args, out CliOptions options, out string? error))
        {
            Console.Error.WriteLine(error ?? "Invalid arguments.");
            Console.Error.WriteLine();
            Console.Error.WriteLine(CliOptions.HelpText);
            return 1;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(CliOptions.HelpText);
            return 0;
        }

        string expectationPath = options.ExpectationPath
            ?? Path.Combine(AppContext.BaseDirectory, "Presets", "FfxMemorySmoke.expectations.json");

        LayoutExpectationManifest manifest = LayoutExpectationManifest.LoadIfExists(expectationPath);
        using LayoutInspector inspector = new(options.TargetAssemblyPath, manifest);
        InspectionSessionResult result = inspector.Inspect(options.ResolveTypeNames());

        string report = TextReportWriter.Write(result);
        if (!string.IsNullOrWhiteSpace(options.OutputPath))
        {
            string fullOutputPath = Path.GetFullPath(options.OutputPath);
            string? directory = Path.GetDirectoryName(fullOutputPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullOutputPath, report, Encoding.UTF8);
            Console.WriteLine($"RuntimeInspectorLab wrote report to {fullOutputPath}");
        }
        else
        {
            Console.WriteLine(report);
        }

        return options.FailOnMismatch && result.TotalMismatches > 0 ? 2 : 0;
    }
}

internal sealed class CliOptions
{
    public static string HelpText =>
        """
        RuntimeInspectorLab

        Required:
          --target <path>         Path to the target assembly to inspect.

        Optional:
          --types <a;b;c>         Semicolon-separated full type names.
          --preset <name>         Built-in preset. Default: ffx-memory-smoke
          --expect <path>         Expectation manifest JSON path.
          --out <path>            Output report path. Defaults to stdout.
          --fail-on-mismatch      Returns exit code 2 when mismatches exist.
          --help                  Shows this help text.

        Built-in presets:
          ffx-memory-smoke
        """;

    public required string TargetAssemblyPath { get; init; }
    public List<string> TypeNames { get; } = [];
    public string PresetName { get; init; } = KnownPresets.DefaultPresetName;
    public string? ExpectationPath { get; init; }
    public string? OutputPath { get; init; }
    public bool FailOnMismatch { get; init; }
    public bool ShowHelp { get; init; }

    public IEnumerable<string> ResolveTypeNames() =>
        TypeNames.Count > 0 ? TypeNames : KnownPresets.Resolve(PresetName);

    public static bool TryParse(string[] args, out CliOptions options, out string? error)
    {
        options = null!;
        error = null;

        if (args.Length == 0)
        {
            error = "Missing arguments.";
            return false;
        }

        string? target = null;
        string? preset = null;
        string? expect = null;
        string? output = null;
        bool failOnMismatch = false;
        bool help = false;
        List<string> typeNames = [];

        for (int index = 0; index < args.Length; index++)
        {
            string arg = args[index];
            switch (arg)
            {
                case "--help":
                case "-h":
                    help = true;
                    break;
                case "--target":
                    if (!TryReadValue(args, ref index, out target, out error))
                    {
                        return false;
                    }

                    break;
                case "--preset":
                    if (!TryReadValue(args, ref index, out preset, out error))
                    {
                        return false;
                    }

                    break;
                case "--expect":
                    if (!TryReadValue(args, ref index, out expect, out error))
                    {
                        return false;
                    }

                    break;
                case "--out":
                    if (!TryReadValue(args, ref index, out output, out error))
                    {
                        return false;
                    }

                    break;
                case "--types":
                    if (!TryReadValue(args, ref index, out string? rawTypes, out error))
                    {
                        return false;
                    }

                    typeNames.AddRange(rawTypes!
                        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
                case "--fail-on-mismatch":
                    failOnMismatch = true;
                    break;
                default:
                    error = $"Unknown argument: {arg}";
                    return false;
            }
        }

        if (help)
        {
            options = new CliOptions
            {
                TargetAssemblyPath = string.Empty,
                PresetName = preset ?? KnownPresets.DefaultPresetName,
                ExpectationPath = expect,
                OutputPath = output,
                FailOnMismatch = failOnMismatch,
                ShowHelp = true
            };
            options.TypeNames.AddRange(typeNames);
            return true;
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            error = "Missing required --target <path>.";
            return false;
        }

        string fullTargetPath = Path.GetFullPath(target);
        if (!File.Exists(fullTargetPath))
        {
            error = $"Target assembly not found: {fullTargetPath}";
            return false;
        }

        string resolvedPreset = preset ?? KnownPresets.DefaultPresetName;
        if (!KnownPresets.TryResolve(resolvedPreset, out _))
        {
            error = $"Unknown preset '{resolvedPreset}'.";
            return false;
        }

        options = new CliOptions
        {
            TargetAssemblyPath = fullTargetPath,
            PresetName = resolvedPreset,
            ExpectationPath = expect is null ? null : Path.GetFullPath(expect),
            OutputPath = output is null ? null : Path.GetFullPath(output),
            FailOnMismatch = failOnMismatch,
            ShowHelp = false
        };
        options.TypeNames.AddRange(typeNames);
        return true;
    }

    private static bool TryReadValue(string[] args, ref int index, out string? value, out string? error)
    {
        value = null;
        error = null;

        if (index + 1 >= args.Length)
        {
            error = $"Missing value after {args[index]}";
            return false;
        }

        index++;
        value = args[index];
        return true;
    }
}

internal static class KnownPresets
{
    public const string DefaultPresetName = "ffx-memory-smoke";

    private static readonly Dictionary<string, string[]> Presets = new(StringComparer.OrdinalIgnoreCase)
    {
        [DefaultPresetName] =
        [
            "FFXProjectEditor.FfxLib.Memory.MemoryBtl",
            "FFXProjectEditor.FfxLib.Memory.MemoryBtl+BtlDebug",
            "FFXProjectEditor.FfxLib.Memory.MemoryChr",
            "FFXProjectEditor.FfxLib.Memory.MemorySaveData",
            "FFXProjectEditor.FfxLib.Common.StatusByteList",
            "FFXProjectEditor.FfxLib.Common.StatusDurationByteList",
            "FFXProjectEditor.FfxLib.Common.AbilityStatusList"
        ]
    };

    public static IEnumerable<string> Resolve(string presetName) =>
        Presets.TryGetValue(presetName, out string[]? values)
            ? values
            : Array.Empty<string>();

    public static bool TryResolve(string presetName, out string[]? values) =>
        Presets.TryGetValue(presetName, out values);
}

internal sealed class LayoutInspector : IDisposable
{
    private static readonly MethodInfo BinaryMappingWriteMethod = typeof(BinaryMapping)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method =>
            method.Name == nameof(BinaryMapping.WriteObject) &&
            method.IsGenericMethodDefinition &&
            method.GetParameters() is
            [
                { ParameterType: var streamType },
                _,
                { ParameterType: var offsetType }
            ] &&
            streamType == typeof(Stream) &&
            offsetType == typeof(int));

    private readonly LayoutExpectationManifest _manifest;
    private readonly TargetAssemblySession _assemblySession;
    private readonly Dictionary<Type, IReadOnlyList<DataMemberDescriptor>> _dataMembersCache = [];
    private readonly Dictionary<Type, MappedLayoutInfo> _mappedLayoutCache = [];
    private readonly Dictionary<Type, int> _primitiveMappedSizes = BinaryMapperSizeOracle.Build();

    public LayoutInspector(string assemblyPath, LayoutExpectationManifest manifest)
    {
        _manifest = manifest;
        _assemblySession = new TargetAssemblySession(assemblyPath);
    }

    public InspectionSessionResult Inspect(IEnumerable<string> typeNames)
    {
        List<TypeInspection> reports = [];
        List<string> unresolved = [];

        foreach (string typeName in typeNames.Distinct(StringComparer.Ordinal))
        {
            Type? type = _assemblySession.FindType(typeName);
            if (type is null)
            {
                unresolved.Add(typeName);
                continue;
            }

            reports.Add(InspectType(type));
        }

        return new InspectionSessionResult(
            _assemblySession.TargetAssembly.Location,
            reports,
            unresolved,
            reports.Sum(report => report.MismatchCount));
    }

    public void Dispose() => _assemblySession.Dispose();

    private TypeInspection InspectType(Type type)
    {
        TypeInspection report = new()
        {
            FullName = type.FullName ?? type.Name,
            Kind = DescribeKind(type),
            Expectation = _manifest.TryGetTypeExpectation(type.FullName ?? type.Name, out TypeExpectation? expectation)
                ? expectation
                : null
        };

        report.ClrLayout = InspectClrLayout(type);
        if (HasMappedLayout(type))
        {
            report.MappedLayout = InspectMappedLayout(type, new HashSet<Type>());
            ApplyExpectations(report);
        }
        else if (report.Expectation is not null)
        {
            report.Notes.Add(new DiagnosticNote("warn", "type", "expectation-without-mapped-layout", "Expectation exists, but no [Data] members were found."));
            report.MismatchCount++;
        }

        return report;
    }

    private static string DescribeKind(Type type)
    {
        if (type.IsEnum)
        {
            return "enum";
        }

        if (type.IsValueType)
        {
            return "value-type";
        }

        if (type.IsClass)
        {
            return "class";
        }

        return "type";
    }

    private bool HasMappedLayout(Type type) => GetDataMembers(type).Count > 0;

    private ClrLayoutInfo InspectClrLayout(Type type)
    {
        ClrLayoutInfo layout = new()
        {
            TypeName = type.FullName ?? type.Name,
            LayoutKind = type.StructLayoutAttribute?.Value
        };

        if (type.IsEnum)
        {
            Type underlyingType = Enum.GetUnderlyingType(type);
            layout.Status = "ok";
            layout.Size = GetMarshalSizeOrNull(underlyingType);
            layout.Note = $"Enum underlying type: {underlyingType.FullName}";
            return layout;
        }

        if (!type.IsValueType)
        {
            layout.Status = "skipped";
            layout.Note = "CLR layout secondary mode only inspects value types and interop structs.";
            return layout;
        }

        int? size = GetMarshalSizeOrNull(type);
        if (!size.HasValue)
        {
            layout.Status = "unsupported";
            layout.Note = "Marshal.SizeOf failed for this type.";
            return layout;
        }

        layout.Status = "ok";
        layout.Size = size.Value;

        foreach (FieldInfo field in type
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .OrderBy(field => field.MetadataToken))
        {
            int? offset = null;
            try
            {
                offset = Marshal.OffsetOf(type, field.Name).ToInt32();
            }
            catch
            {
                // Ignore unsupported offsets and keep partial info.
            }

            layout.Fields.Add(new ClrFieldLayoutInfo
            {
                Name = field.Name,
                FieldType = GetTypeDisplayName(field.FieldType),
                Offset = offset,
                Size = GetMarshalSizeOrNull(field.FieldType)
            });
        }

        return layout;
    }

    private MappedLayoutInfo InspectMappedLayout(Type type, HashSet<Type> stack)
    {
        if (_mappedLayoutCache.TryGetValue(type, out MappedLayoutInfo? cached))
        {
            return CloneMappedLayout(cached);
        }

        if (!stack.Add(type))
        {
            return new MappedLayoutInfo
            {
                TypeName = type.FullName ?? type.Name,
                Notes =
                {
                    new DiagnosticNote("warn", "type", "cyclic-layout", "Cyclic mapped layout reference detected.")
                }
            };
        }

        MappedLayoutInfo info = new()
        {
            TypeName = type.FullName ?? type.Name
        };

        List<DataMemberDescriptor> members = [.. GetDataMembers(type)];
        if (members.Any(member => !member.Attribute.Offset.HasValue))
        {
            info.Notes.Add(new DiagnosticNote(
                "warn",
                "type",
                "implicit-sequence",
                "One or more [Data] members have no explicit Offset; order is inferred from MetadataToken."));
        }

        int cursor = 0;
        foreach (DataMemberDescriptor member in members)
        {
            MemberLayoutInfo entry = new()
            {
                Name = member.Member.Name,
                MemberType = GetTypeDisplayName(member.ValueType),
                OffsetOrigin = member.Attribute.Offset.HasValue ? "explicit" : "inferred",
                Count = member.Attribute.Count > 0 ? member.Attribute.Count : null,
                Stride = member.Attribute.Stride > 0 ? member.Attribute.Stride : null,
                BitIndex = member.Attribute.BitIndex > 0 ? member.Attribute.BitIndex : null
            };

            entry.Offset = member.Attribute.Offset ?? cursor;
            BinarySizeResult sizeResult = ResolveMemberSize(member, stack);
            entry.Size = sizeResult.Size;
            if (!string.IsNullOrWhiteSpace(sizeResult.Note))
            {
                entry.Notes.Add(sizeResult.Note!);
            }

            if (entry.Size.HasValue)
            {
                entry.EndExclusive = entry.Offset + entry.Size.Value;
                cursor = Math.Max(cursor, entry.EndExclusive.Value);
            }
            else
            {
                cursor = Math.Max(cursor, entry.Offset ?? cursor);
            }

            info.Members.Add(entry);
        }

        PopulateGapsAndCoverage(info);
        if (TryMeasureSerializedSize(type, out int serializedSize, out string? serializedError))
        {
            info.BinaryMapperSerializedSize = serializedSize;
            if (info.ComputedTotalSize.HasValue && info.ComputedTotalSize.Value != serializedSize)
            {
                info.Notes.Add(new DiagnosticNote(
                    "warn",
                    "type",
                    "binarymapper-size-delta",
                    $"Computed total size 0x{info.ComputedTotalSize.Value:X} differs from BinaryMapper write size 0x{serializedSize:X}."));
            }
        }
        else if (!string.IsNullOrWhiteSpace(serializedError))
        {
            info.Notes.Add(new DiagnosticNote("info", "type", "binarymapper-size-unavailable", serializedError));
        }

        stack.Remove(type);
        _mappedLayoutCache[type] = CloneMappedLayout(info);
        return info;
    }

    private BinarySizeResult ResolveMemberSize(DataMemberDescriptor member, HashSet<Type> stack)
    {
        if (member.ValueType == typeof(string))
        {
            int count = member.Attribute.Count > 0 ? member.Attribute.Count : 1;
            return BinarySizeResult.Known(count, count == 1 ? "String without Count defaults to 1 byte in mapped mode." : null);
        }

        if (member.ValueType == typeof(byte[]))
        {
            int count = member.Attribute.Count > 0 ? member.Attribute.Count : 1;
            return BinarySizeResult.Known(count, count == 1 ? "byte[] without Count defaults to 1 byte in mapped mode." : null);
        }

        if (member.ValueType.IsArray)
        {
            Type elementType = member.ValueType.GetElementType()!;
            int count = member.Attribute.Count;
            if (count <= 0)
            {
                return BinarySizeResult.Unknown("Array member is missing Count.");
            }

            BinarySizeResult elementSize = ResolveBinaryTypeSize(elementType, stack);
            if (!elementSize.Size.HasValue)
            {
                return elementSize;
            }

            int step = member.Attribute.Stride > 0 ? member.Attribute.Stride : elementSize.Size.Value;
            if (member.Attribute.Stride > 0 && step < elementSize.Size.Value)
            {
                return BinarySizeResult.Unknown("Stride is smaller than element size.");
            }

            int size = count == 0 ? 0 : ((count - 1) * step) + elementSize.Size.Value;
            return BinarySizeResult.Known(size);
        }

        return ResolveBinaryTypeSize(member.ValueType, stack);
    }

    private BinarySizeResult ResolveBinaryTypeSize(Type type, HashSet<Type> stack)
    {
        if (_primitiveMappedSizes.TryGetValue(type, out int primitiveSize))
        {
            return BinarySizeResult.Known(primitiveSize);
        }

        if (type.IsEnum)
        {
            return ResolveBinaryTypeSize(Enum.GetUnderlyingType(type), stack);
        }

        if (HasMappedLayout(type))
        {
            MappedLayoutInfo nested = InspectMappedLayout(type, stack);
            if (nested.ComputedTotalSize.HasValue)
            {
                return BinarySizeResult.Known(nested.ComputedTotalSize.Value);
            }

            if (nested.BinaryMapperSerializedSize.HasValue)
            {
                return BinarySizeResult.Known(nested.BinaryMapperSerializedSize.Value);
            }

            return BinarySizeResult.Unknown("Nested mapped type has no resolved total size.");
        }

        int? marshalSize = GetMarshalSizeOrNull(type);
        return marshalSize.HasValue
            ? BinarySizeResult.Known(marshalSize.Value, "Fallback CLR/Marshal size used because the member has no mapped schema.")
            : BinarySizeResult.Unknown("No mapped size rule and Marshal.SizeOf is unavailable.");
    }

    private void ApplyExpectations(TypeInspection report)
    {
        if (report.MappedLayout is null || report.Expectation is null)
        {
            return;
        }

        MappedLayoutInfo mapped = report.MappedLayout;
        TypeExpectation expectation = report.Expectation;

        if (expectation.ExpectedSize.HasValue)
        {
            mapped.ExpectedSize = expectation.ExpectedSize.Value;
            mapped.ExpectedSizeSource = expectation.ExpectedSizeSource;

            int? actualSize = mapped.BinaryMapperSerializedSize ?? mapped.ComputedTotalSize;
            if (!actualSize.HasValue)
            {
                report.Notes.Add(new DiagnosticNote("warn", "type", "size-unresolved", "Expected total size exists, but actual size is unresolved."));
                report.MismatchCount++;
            }
            else if (actualSize.Value != expectation.ExpectedSize.Value)
            {
                report.Notes.Add(new DiagnosticNote(
                    "warn",
                    "type",
                    "size-mismatch",
                    $"Expected total size 0x{expectation.ExpectedSize.Value:X} but observed 0x{actualSize.Value:X}. Source: {expectation.ExpectedSizeSource ?? "n/a"}"));
                report.MismatchCount++;
            }
        }

        foreach (MemberLayoutInfo member in mapped.Members)
        {
            if (!expectation.Members.TryGetValue(member.Name, out MemberExpectation? memberExpectation))
            {
                continue;
            }

            member.ExpectedOffset = memberExpectation.ExpectedOffset;
            member.ExpectedSize = memberExpectation.ExpectedSize;
            member.ExpectationSource = memberExpectation.Source;

            if (member.ExpectedOffset.HasValue && member.Offset != member.ExpectedOffset.Value)
            {
                member.Status = "mismatch";
                member.Notes.Add($"expected_offset=0x{member.ExpectedOffset.Value:X}");
                report.MismatchCount++;
            }

            if (member.ExpectedSize.HasValue && member.Size != member.ExpectedSize)
            {
                member.Status = "mismatch";
                member.Notes.Add($"expected_size=0x{member.ExpectedSize.Value:X}");
                report.MismatchCount++;
            }

            if (member.Status == "ok" && (member.ExpectedOffset.HasValue || member.ExpectedSize.HasValue))
            {
                member.Status = "match";
            }
        }

        if (mapped.ExpectedSize.HasValue && mapped.LastOccupiedEndInclusive.HasValue)
        {
            int trailing = mapped.ExpectedSize.Value - (mapped.LastOccupiedEndInclusive.Value + 1);
            if (trailing > 0)
            {
                mapped.TailPadding = trailing;
            }
        }
    }

    private static void PopulateGapsAndCoverage(MappedLayoutInfo info)
    {
        List<MemberLayoutInfo> sizedMembers = info.Members
            .Where(member => member.Offset.HasValue && member.Size.HasValue)
            .OrderBy(member => member.Offset)
            .ThenBy(member => member.Name, StringComparer.Ordinal)
            .ToList();

        if (sizedMembers.Count == 0)
        {
            return;
        }

        List<(int Start, int EndExclusive)> merged = [];
        foreach (MemberLayoutInfo member in sizedMembers)
        {
            int start = member.Offset!.Value;
            int endExclusive = member.EndExclusive!.Value;

            if (merged.Count == 0)
            {
                merged.Add((start, endExclusive));
                continue;
            }

            (int lastStart, int lastEndExclusive) = merged[^1];
            if (start < lastEndExclusive)
            {
                member.Status = member.Status == "mismatch" ? member.Status : "overlap";
                member.Notes.Add($"overlaps previous range ending at 0x{lastEndExclusive - 1:X}");
            }

            if (start <= lastEndExclusive)
            {
                merged[^1] = (lastStart, Math.Max(lastEndExclusive, endExclusive));
            }
            else
            {
                info.Gaps.Add(new GapInfo
                {
                    Start = lastEndExclusive,
                    EndInclusive = start - 1
                });
                merged.Add((start, endExclusive));
            }
        }

        info.ComputedTotalSize = sizedMembers.Max(member => member.EndExclusive);
        info.OccupiedBytes = merged.Sum(range => range.EndExclusive - range.Start);
        info.LastOccupiedStart = merged[^1].Start;
        info.LastOccupiedEndInclusive = merged[^1].EndExclusive - 1;
    }

    private bool TryMeasureSerializedSize(Type type, out int size, out string? error)
    {
        size = 0;
        error = null;

        try
        {
            object? instance = CreateInitializedInstance(type, new HashSet<Type>());
            if (instance is null)
            {
                error = "BinaryMapper size probe skipped because the type could not be instantiated.";
                return false;
            }

            using MemoryStream stream = new(new byte[1024 * 1024], writable: true);
            MethodInfo writer = BinaryMappingWriteMethod.MakeGenericMethod(type);
            writer.Invoke(null, [stream, instance, 0]);
            size = checked((int)stream.Position);
            return true;
        }
        catch (Exception ex)
        {
            error = $"BinaryMapper size probe failed: {ex.GetBaseException().Message}";
            return false;
        }
    }

    private object? CreateInitializedInstance(Type type, HashSet<Type> stack)
    {
        if (type == typeof(string))
        {
            return "\0";
        }

        if (type.IsArray)
        {
            return Array.CreateInstance(type.GetElementType()!, 1);
        }

        if (type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }

        if (type.IsAbstract)
        {
            return null;
        }

        ConstructorInfo? constructor = type.GetConstructor(Type.EmptyTypes);
        if (constructor is null)
        {
            return null;
        }

        object instance = constructor.Invoke(null);
        if (!stack.Add(type))
        {
            return instance;
        }

        foreach (DataMemberDescriptor member in GetDataMembers(type))
        {
            object? value = CreateInitializedMemberValue(member, stack);
            if (value is not null)
            {
                SetMemberValue(instance, member.Member, value);
            }
        }

        stack.Remove(type);
        return instance;
    }

    private object? CreateInitializedMemberValue(DataMemberDescriptor member, HashSet<Type> stack)
    {
        Type valueType = member.ValueType;
        if (valueType == typeof(string))
        {
            int count = member.Attribute.Count > 0 ? member.Attribute.Count : 1;
            return new string('\0', count);
        }

        if (valueType.IsArray)
        {
            int count = member.Attribute.Count > 0 ? member.Attribute.Count : 1;
            Type elementType = valueType.GetElementType()!;
            Array array = Array.CreateInstance(elementType, count);
            if (!elementType.IsValueType && elementType != typeof(string))
            {
                for (int index = 0; index < count; index++)
                {
                    object? element = CreateInitializedInstance(elementType, stack);
                    if (element is not null)
                    {
                        array.SetValue(element, index);
                    }
                }
            }

            return array;
        }

        if (valueType.IsValueType)
        {
            return Activator.CreateInstance(valueType);
        }

        return CreateInitializedInstance(valueType, stack);
    }

    private static void SetMemberValue(object target, MemberInfo member, object value)
    {
        switch (member)
        {
            case PropertyInfo property when property.CanWrite:
                property.SetValue(target, value);
                break;
            case FieldInfo field:
                field.SetValue(target, value);
                break;
        }
    }

    private IReadOnlyList<DataMemberDescriptor> GetDataMembers(Type type)
    {
        if (_dataMembersCache.TryGetValue(type, out IReadOnlyList<DataMemberDescriptor>? cached))
        {
            return cached;
        }

        List<DataMemberDescriptor> members = [];
        foreach (Type current in EnumerateTypeHierarchy(type))
        {
            members.AddRange(current
                .GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(member => member.MemberType is MemberTypes.Property or MemberTypes.Field)
                .Select(member => new { Member = member, Attribute = member.GetCustomAttribute<DataAttribute>(inherit: false) })
                .Where(entry => entry.Attribute is not null)
                .OrderBy(entry => entry.Member.MetadataToken)
                .Select(entry => new DataMemberDescriptor(
                    entry.Member,
                    GetMemberValueType(entry.Member),
                    entry.Attribute!)));
        }

        _dataMembersCache[type] = members;
        return members;
    }

    private static IEnumerable<Type> EnumerateTypeHierarchy(Type type)
    {
        Stack<Type> stack = new();
        for (Type? current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            stack.Push(current);
        }

        while (stack.Count > 0)
        {
            yield return stack.Pop();
        }
    }

    private static Type GetMemberValueType(MemberInfo member) =>
        member switch
        {
            PropertyInfo property => property.PropertyType,
            FieldInfo field => field.FieldType,
            _ => throw new NotSupportedException($"Unsupported member: {member.MemberType}")
        };

    private static int? GetMarshalSizeOrNull(Type type)
    {
        try
        {
            return Marshal.SizeOf(type);
        }
        catch
        {
            return null;
        }
    }

    private static string GetTypeDisplayName(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }

        string genericName = type.GetGenericTypeDefinition().FullName ?? type.Name;
        int tickIndex = genericName.IndexOf('`');
        if (tickIndex >= 0)
        {
            genericName = genericName[..tickIndex];
        }

        string[] arguments = type.GetGenericArguments().Select(GetTypeDisplayName).ToArray();
        return $"{genericName}<{string.Join(", ", arguments)}>";
    }

    private static MappedLayoutInfo CloneMappedLayout(MappedLayoutInfo source)
    {
        MappedLayoutInfo clone = new()
        {
            TypeName = source.TypeName,
            ComputedTotalSize = source.ComputedTotalSize,
            BinaryMapperSerializedSize = source.BinaryMapperSerializedSize,
            ExpectedSize = source.ExpectedSize,
            ExpectedSizeSource = source.ExpectedSizeSource,
            OccupiedBytes = source.OccupiedBytes,
            LastOccupiedStart = source.LastOccupiedStart,
            LastOccupiedEndInclusive = source.LastOccupiedEndInclusive,
            TailPadding = source.TailPadding
        };

        clone.Members.AddRange(source.Members.Select(member => member.Clone()));
        clone.Gaps.AddRange(source.Gaps.Select(gap => new GapInfo { Start = gap.Start, EndInclusive = gap.EndInclusive }));
        clone.Notes.AddRange(source.Notes.Select(note => note.Clone()));
        return clone;
    }
}

internal sealed class TargetAssemblySession : IDisposable
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly Func<AssemblyLoadContext, AssemblyName, Assembly?> _resolveHandler;

    public TargetAssemblySession(string assemblyPath)
    {
        string fullPath = Path.GetFullPath(assemblyPath);
        _resolver = new AssemblyDependencyResolver(fullPath);
        _resolveHandler = ResolveAssembly;
        AssemblyLoadContext.Default.Resolving += _resolveHandler;
        TargetAssembly = LoadAssembly(fullPath);
    }

    public Assembly TargetAssembly { get; }

    public Type? FindType(string fullName) =>
        TargetAssembly.GetType(fullName, throwOnError: false, ignoreCase: false)
        ?? TargetAssembly.GetTypes().FirstOrDefault(type => string.Equals(type.FullName, fullName, StringComparison.Ordinal));

    public void Dispose()
    {
        AssemblyLoadContext.Default.Resolving -= _resolveHandler;
    }

    private Assembly? ResolveAssembly(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        Assembly? existing = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(loaded => string.Equals(loaded.GetName().Name, assemblyName.Name, StringComparison.Ordinal));
        if (existing is not null)
        {
            return existing;
        }

        string? candidatePath = _resolver.ResolveAssemblyToPath(assemblyName);
        return candidatePath is not null && File.Exists(candidatePath)
            ? context.LoadFromAssemblyPath(candidatePath)
            : null;
    }

    private static Assembly LoadAssembly(string assemblyPath)
    {
        Assembly? existing = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(loaded =>
                !string.IsNullOrWhiteSpace(loaded.Location) &&
                string.Equals(Path.GetFullPath(loaded.Location), assemblyPath, StringComparison.OrdinalIgnoreCase));

        return existing ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
    }
}

internal static class BinaryMapperSizeOracle
{
    public static Dictionary<Type, int> Build()
    {
        Dictionary<Type, int> sizes = [];
        Add<byte>(sizes);
        Add<sbyte>(sizes);
        Add<short>(sizes);
        Add<ushort>(sizes);
        Add<int>(sizes);
        Add<uint>(sizes);
        Add<long>(sizes);
        Add<ulong>(sizes);
        Add<float>(sizes);
        Add<double>(sizes);
        Add<bool>(sizes);
        Add<char>(sizes);
        return sizes;
    }

    private static void Add<T>(Dictionary<Type, int> sizes)
    {
        try
        {
            using MemoryStream stream = new(new byte[128], writable: true);
            BinaryMapping.WriteObject(stream, new PrimitiveProbe<T>(), 0);
            sizes[typeof(T)] = checked((int)stream.Position);
        }
        catch
        {
            // Ignore unsupported probe types.
        }
    }

    private sealed class PrimitiveProbe<T>
    {
        [Data]
        public T Value { get; set; } = default!;
    }
}

internal sealed record DataMemberDescriptor(MemberInfo Member, Type ValueType, DataAttribute Attribute);

internal sealed class InspectionSessionResult(
    string assemblyPath,
    List<TypeInspection> types,
    List<string> unresolvedTypeNames,
    int totalMismatches)
{
    public string AssemblyPath { get; } = assemblyPath;
    public List<TypeInspection> Types { get; } = types;
    public List<string> UnresolvedTypeNames { get; } = unresolvedTypeNames;
    public int TotalMismatches { get; } = totalMismatches;
}

internal sealed class TypeInspection
{
    public required string FullName { get; init; }
    public required string Kind { get; init; }
    public ClrLayoutInfo? ClrLayout { get; set; }
    public MappedLayoutInfo? MappedLayout { get; set; }
    public TypeExpectation? Expectation { get; set; }
    public List<DiagnosticNote> Notes { get; } = [];
    public int MismatchCount { get; set; }
}

internal sealed class ClrLayoutInfo
{
    public required string TypeName { get; init; }
    public string Status { get; set; } = "unknown";
    public LayoutKind? LayoutKind { get; set; }
    public int? Size { get; set; }
    public string? Note { get; set; }
    public List<ClrFieldLayoutInfo> Fields { get; } = [];
}

internal sealed class ClrFieldLayoutInfo
{
    public required string Name { get; init; }
    public required string FieldType { get; init; }
    public int? Offset { get; init; }
    public int? Size { get; init; }
}

internal sealed class MappedLayoutInfo
{
    public required string TypeName { get; init; }
    public int? ComputedTotalSize { get; set; }
    public int? BinaryMapperSerializedSize { get; set; }
    public int? ExpectedSize { get; set; }
    public string? ExpectedSizeSource { get; set; }
    public int OccupiedBytes { get; set; }
    public int? LastOccupiedStart { get; set; }
    public int? LastOccupiedEndInclusive { get; set; }
    public int? TailPadding { get; set; }
    public List<MemberLayoutInfo> Members { get; } = [];
    public List<GapInfo> Gaps { get; } = [];
    public List<DiagnosticNote> Notes { get; } = [];
}

internal sealed class MemberLayoutInfo
{
    public required string Name { get; init; }
    public required string MemberType { get; init; }
    public int? Offset { get; set; }
    public int? Size { get; set; }
    public int? EndExclusive { get; set; }
    public int? Count { get; set; }
    public int? Stride { get; set; }
    public int? BitIndex { get; set; }
    public required string OffsetOrigin { get; init; }
    public int? ExpectedOffset { get; set; }
    public int? ExpectedSize { get; set; }
    public string? ExpectationSource { get; set; }
    public string Status { get; set; } = "ok";
    public List<string> Notes { get; } = [];

    public MemberLayoutInfo Clone()
    {
        MemberLayoutInfo clone = new()
        {
            Name = Name,
            MemberType = MemberType,
            Offset = Offset,
            Size = Size,
            EndExclusive = EndExclusive,
            Count = Count,
            Stride = Stride,
            BitIndex = BitIndex,
            OffsetOrigin = OffsetOrigin,
            ExpectedOffset = ExpectedOffset,
            ExpectedSize = ExpectedSize,
            ExpectationSource = ExpectationSource,
            Status = Status
        };
        clone.Notes.AddRange(Notes);
        return clone;
    }
}

internal sealed class GapInfo
{
    public int Start { get; init; }
    public int EndInclusive { get; init; }
    public int Size => (EndInclusive - Start) + 1;
}

internal sealed class DiagnosticNote
{
    public DiagnosticNote(string severity, string scope, string kind, string details)
    {
        Severity = severity;
        Scope = scope;
        Kind = kind;
        Details = details;
    }

    public string Severity { get; }
    public string Scope { get; }
    public string Kind { get; }
    public string Details { get; }

    public DiagnosticNote Clone() => new(Severity, Scope, Kind, Details);
}

internal readonly record struct BinarySizeResult(int? Size, string? Note)
{
    public static BinarySizeResult Known(int size, string? note = null) => new(size, note);
    public static BinarySizeResult Unknown(string note) => new(null, note);
}

internal sealed class LayoutExpectationManifest
{
    private readonly Dictionary<string, TypeExpectation> _types;

    private LayoutExpectationManifest(Dictionary<string, TypeExpectation> types)
    {
        _types = types;
    }

    public static LayoutExpectationManifest LoadIfExists(string path)
    {
        if (!File.Exists(path))
        {
            return new LayoutExpectationManifest(new Dictionary<string, TypeExpectation>(StringComparer.Ordinal));
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        Dictionary<string, TypeExpectation> types = new(StringComparer.Ordinal);
        if (document.RootElement.TryGetProperty("types", out JsonElement typesElement))
        {
            foreach (JsonProperty typeProperty in typesElement.EnumerateObject())
            {
                TypeExpectation expectation = new();
                if (typeProperty.Value.TryGetProperty("expectedSize", out JsonElement expectedSizeElement))
                {
                    expectation.ExpectedSize = HexParsing.TryParseFlexibleInt(expectedSizeElement, out int size)
                        ? size
                        : null;
                }

                if (typeProperty.Value.TryGetProperty("expectedSizeSource", out JsonElement expectedSizeSourceElement))
                {
                    expectation.ExpectedSizeSource = expectedSizeSourceElement.GetString();
                }

                if (typeProperty.Value.TryGetProperty("members", out JsonElement membersElement))
                {
                    foreach (JsonProperty memberProperty in membersElement.EnumerateObject())
                    {
                        MemberExpectation memberExpectation = new();
                        if (memberProperty.Value.TryGetProperty("expectedOffset", out JsonElement expectedOffsetElement))
                        {
                            memberExpectation.ExpectedOffset = HexParsing.TryParseFlexibleInt(expectedOffsetElement, out int offset)
                                ? offset
                                : null;
                        }

                        if (memberProperty.Value.TryGetProperty("expectedSize", out JsonElement expectedMemberSizeElement))
                        {
                            memberExpectation.ExpectedSize = HexParsing.TryParseFlexibleInt(expectedMemberSizeElement, out int memberSize)
                                ? memberSize
                                : null;
                        }

                        if (memberProperty.Value.TryGetProperty("source", out JsonElement sourceElement))
                        {
                            memberExpectation.Source = sourceElement.GetString();
                        }

                        expectation.Members[memberProperty.Name] = memberExpectation;
                    }
                }

                types[typeProperty.Name] = expectation;
            }
        }

        return new LayoutExpectationManifest(types);
    }

    public bool TryGetTypeExpectation(string typeName, out TypeExpectation? expectation) =>
        _types.TryGetValue(typeName, out expectation);
}

internal sealed class TypeExpectation
{
    public int? ExpectedSize { get; set; }
    public string? ExpectedSizeSource { get; set; }
    public Dictionary<string, MemberExpectation> Members { get; } = new(StringComparer.Ordinal);
}

internal sealed class MemberExpectation
{
    public int? ExpectedOffset { get; set; }
    public int? ExpectedSize { get; set; }
    public string? Source { get; set; }
}

internal static class HexParsing
{
    public static bool TryParseFlexibleInt(JsonElement element, out int value)
    {
        value = 0;
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetInt32(out value),
            JsonValueKind.String => TryParseFlexibleInt(element.GetString(), out value),
            _ => false
        };
    }

    public static bool TryParseFlexibleInt(string? raw, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string text = raw.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber, null, out value);
        }

        return int.TryParse(text, out value);
    }
}

internal static class TextReportWriter
{
    public static string Write(InspectionSessionResult result)
    {
        StringBuilder builder = new();
        builder.AppendLine("RILAYOUT v1");
        builder.Append("SESSION|assembly=").Append(result.AssemblyPath).Append("|types=").Append(result.Types.Count).Append("|mismatches=").Append(result.TotalMismatches).AppendLine();

        foreach (string unresolved in result.UnresolvedTypeNames)
        {
            builder.Append("ERROR|type=").Append(unresolved).Append("|kind=unresolved").AppendLine();
        }

        foreach (TypeInspection type in result.Types.OrderBy(report => report.FullName, StringComparer.Ordinal))
        {
            builder.AppendLine();
            builder.Append("TYPE|").Append(type.FullName).Append("|kind=").Append(type.Kind).AppendLine();

            if (type.ClrLayout is not null)
            {
                builder.Append("CLR|status=").Append(type.ClrLayout.Status)
                    .Append("|layout=").Append(type.ClrLayout.LayoutKind?.ToString() ?? "-")
                    .Append("|size=").Append(FormatHexOrDash(type.ClrLayout.Size))
                    .Append("|note=").Append(Escape(type.ClrLayout.Note))
                    .AppendLine();

                if (type.ClrLayout.Fields.Count > 0)
                {
                    builder.AppendLine("CLRFIELDS|offset|size|type|field");
                    foreach (ClrFieldLayoutInfo field in type.ClrLayout.Fields)
                    {
                        builder.Append("CLRFIELD|").Append(FormatHexOrDash(field.Offset))
                            .Append('|').Append(FormatHexOrDash(field.Size))
                            .Append('|').Append(field.FieldType)
                            .Append('|').Append(field.Name)
                            .AppendLine();
                    }
                }
            }

            if (type.MappedLayout is not null)
            {
                builder.Append("SUMMARY|computed_size=").Append(FormatHexOrDash(type.MappedLayout.ComputedTotalSize))
                    .Append("|binarymapper_size=").Append(FormatHexOrDash(type.MappedLayout.BinaryMapperSerializedSize))
                    .Append("|expected_size=").Append(FormatHexOrDash(type.MappedLayout.ExpectedSize))
                    .Append("|occupied_bytes=").Append(FormatHex(type.MappedLayout.OccupiedBytes))
                    .Append("|last_occupied=").Append(FormatRange(type.MappedLayout.LastOccupiedStart, type.MappedLayout.LastOccupiedEndInclusive))
                    .Append("|inner_gaps=").Append(type.MappedLayout.Gaps.Count).Append('/').Append(FormatHex(type.MappedLayout.Gaps.Sum(gap => gap.Size)))
                    .Append("|tail_padding=").Append(FormatHexOrDash(type.MappedLayout.TailPadding))
                    .Append("|mismatches=").Append(type.MismatchCount)
                    .AppendLine();

                builder.AppendLine("FIELDS|offset|size|type|origin|field|status|notes");
                foreach (MemberLayoutInfo member in type.MappedLayout.Members.OrderBy(member => member.Offset).ThenBy(member => member.Name, StringComparer.Ordinal))
                {
                    builder.Append("FIELD|").Append(FormatHexOrDash(member.Offset))
                        .Append('|').Append(FormatHexOrDash(member.Size))
                        .Append('|').Append(member.MemberType)
                        .Append('|').Append(member.OffsetOrigin)
                        .Append('|').Append(member.Name)
                        .Append('|').Append(member.Status)
                        .Append('|').Append(Escape(string.Join("; ", member.Notes)))
                        .AppendLine();
                }

                foreach (GapInfo gap in type.MappedLayout.Gaps)
                {
                    builder.Append("GAP|").Append(FormatRange(gap.Start, gap.EndInclusive))
                        .Append('|').Append(FormatHex(gap.Size))
                        .Append("|padding?|synthetic|gap|info|between occupied spans")
                        .AppendLine();
                }
            }

            foreach (DiagnosticNote note in type.Notes.Concat(type.MappedLayout?.Notes ?? []))
            {
                builder.Append("NOTE|").Append(note.Severity)
                    .Append('|').Append(note.Scope)
                    .Append('|').Append(note.Kind)
                    .Append('|').Append(Escape(note.Details))
                    .AppendLine();
            }
        }

        return builder.ToString();
    }

    private static string FormatHex(int value) => $"0x{value:X}";
    private static string FormatHexOrDash(int? value) => value.HasValue ? FormatHex(value.Value) : "-";
    private static string FormatRange(int? start, int? endInclusive) =>
        start.HasValue && endInclusive.HasValue ? $"{FormatHex(start.Value)}..{FormatHex(endInclusive.Value)}" : "-";
    private static string Escape(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Replace('|', '/').Replace(Environment.NewLine, " ");
}

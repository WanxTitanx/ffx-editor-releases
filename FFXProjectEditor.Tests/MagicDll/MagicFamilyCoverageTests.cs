using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Modules.MagicDllEditor;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicFamilyCoverageTests
{
    [Fact]
    public void EmbeddedSchemas_AreAuditedAgainstEveryNativeRecordWidth()
    {
        var map = MagicFieldMap.LoadEmbedded();
        var parser = new MagicDllParser(new MagicDllParserOptions { FieldMap = map });
        var observations = new Dictionary<string, Usage>(StringComparer.Ordinal);
        int unresolved = 0;
        int nativeTables = 0;
        var tableExceptions = new List<object>();
        foreach (string path in Directory.GetFiles(TestDataPaths.MagicCorpus, "magic_*.dll"))
        {
            var file = parser.Parse(path);
            var table = MagicFpHandlerTable.TryExtractFromDll(file);
            if (table?.NativeTableCount > 0)
                nativeTables++;
            if (table?.NativeTableCount != 1)
                tableExceptions.Add(new { file.DllName, Tables = table?.NativeTableCount ?? 0,
                    Roots = file.Roots.Count, Slots = file.Roots.Sum(r => r.TotalSlots),
                    UnresolvedSlots = file.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots)
                        .Count(slot => slot.OpcodeName == null),
                    ResolvedIndices = table?.Names });
            foreach (var slot in file.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots))
            {
                if (slot.OpcodeName == null) { unresolved++; continue; }
                if (!observations.TryGetValue(slot.OpcodeName, out var usage))
                    observations[slot.OpcodeName] = usage = new Usage();
                usage.Slots++;
                usage.Records += slot.Keyframes.Count;
                usage.Files.Add(file.DllName);
                usage.Widths.Add(slot.NativeRecordWidth);
                if (!usage.WidthFiles.TryGetValue(slot.NativeRecordWidth, out var files))
                    usage.WidthFiles[slot.NativeRecordWidth] = files = new HashSet<string>(StringComparer.Ordinal);
                files.Add(file.DllName);
            }
        }
        Assert.NotEmpty(observations);
        var families = map.Families.Values.OrderBy(s => s.Opcode).Select(schema =>
        {
            observations.TryGetValue(schema.Opcode, out var usage);
            int[] widths = usage?.Widths.OrderBy(w => w).ToArray() ?? Array.Empty<int>();
            var outside = schema.Fields.Where(field => widths.Any(width =>
                field.Offset < 0 || field.Width <= 0 || (long)field.Offset + field.Width > width))
                .Select(field => new { field.Name, field.Offset, field.Width }).ToArray();
            return new { schema.Opcode, schema.HandlerAddr, schema.PayloadConsumer, schema.Editable,
                schema.Status, schema.SemanticsCategory, schema.RealFunc,
                Slots = usage?.Slots ?? 0, Records = usage?.Records ?? 0,
                Files = usage?.Files.Count ?? 0, NativeWidths = widths,
                WidthSamples = usage?.WidthFiles.OrderBy(pair => pair.Key).Select(pair => new
                    { Width = pair.Key, Files = pair.Value.OrderBy(f => f).Take(8).ToArray() }).ToArray(),
                Fields = schema.Fields.Select(field => new { field.Name, field.Offset, field.Width,
                    Type = field.Type.ToString(), field.Semantics,
                    Writable = MagicDllDocument_Wrapper.IsSchemaFieldWritable(schema, field.Offset, field.Width) &&
                        widths.All(width => (long)field.Offset + field.Width <= width) }).ToArray(),
                FieldsOutsideNativeRecord = outside };
        }).ToArray();
        var missing = observations.Where(pair => !map.Families.ContainsKey(pair.Key))
            .OrderByDescending(pair => pair.Value.Slots).Select(pair => new
            { Opcode = pair.Key, pair.Value.Slots, pair.Value.Records, Files = pair.Value.Files.Count,
                NativeWidths = pair.Value.Widths.OrderBy(w => w).ToArray() }).ToArray();
        File.WriteAllText(TestDataPaths.ReportPath("magic-family-coverage.json"),
            JsonSerializer.Serialize(new { Corpus = TestDataPaths.MagicCorpus, Families = families,
                MissingSchemas = missing, UnresolvedSlots = unresolved, NativeTables = nativeTables,
                TableExceptions = tableExceptions },
                new JsonSerializerOptions { WriteIndented = true }));
        Assert.Empty(families.Where(f => f.FieldsOutsideNativeRecord.Length != 0)
            .Select(f => $"{f.Opcode}: native strides [{string.Join(",", f.NativeWidths)}] do not contain every field"));
    }

    private sealed class Usage
    {
        public int Slots;
        public int Records;
        public readonly HashSet<string> Files = new(StringComparer.Ordinal);
        public readonly HashSet<int> Widths = new();
        public readonly Dictionary<int, HashSet<string>> WidthFiles = new();
    }
}

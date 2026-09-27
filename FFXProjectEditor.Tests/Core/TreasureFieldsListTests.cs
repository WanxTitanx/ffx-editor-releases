using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.TreasureMap;
using Xunit;
using Xunit.Abstractions;

namespace FFXProjectEditor.Tests.Core;

public class TreasureFieldsListTests
{
    private readonly ITestOutputHelper _out;
    public TreasureFieldsListTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void List_Fields_With_ChestCandidates()
    {
        string master = @"D:\FFX Extracted\FFX\ffx_ps2\ffx\master";
        if (!Directory.Exists(master)) return;

        var index = TreasureMapIndexBuilder.Build(master);
        var sb = new System.Text.StringBuilder();

        foreach (var g in index.ConfirmedChestCandidates.GroupBy(c => c.FieldId)
                     .OrderByDescending(g => g.Count()))
        {
            int mapObjs = 0;
            var field = index.Fields.FirstOrDefault(f => f.FieldId == g.Key);
            if (field != null)
                mapObjs = MapObjectExtractor.ExtractFromObjectTable(field.MapPath).Count(o => o.IsChestRange);
            sb.AppendLine($"{g.Key}: {g.Count()} workers, {mapObjs} map objs");
        }

        File.WriteAllText(@"C:\Users\wande\Documents\ffx-editor-main\work\_fields_list.txt", sb.ToString());
        _out.WriteLine(sb.ToString());
    }
}
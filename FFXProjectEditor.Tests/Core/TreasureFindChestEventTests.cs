using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.TreasureMap;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace FFXProjectEditor.Tests.Core;

public class TreasureFindChestEventTests
{
    private readonly ITestOutputHelper _out;
    public TreasureFindChestEventTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Find_Bika02_ChestEvents()
    {
        string eventDir = Path.Combine(TestDataPaths.MasterRoot, "jppc", "event", "obj");
        var sb = new System.Text.StringBuilder();
        var files = Directory.EnumerateFiles(eventDir, "bika02*.ebp", SearchOption.AllDirectories).ToArray();
        sb.AppendLine($"bika02 events: {files.Length}");
        foreach (var f in files)
        {
            var result = EventTreasureScanner.Scan(f);
            if (result.Candidates.Count > 0)
            {
                sb.AppendLine($"{Path.GetFileName(f)}: {result.Candidates.Count} candidatos");
                foreach (var c in result.Candidates)
                {
                    sb.AppendLine($"  worker {c.WorkerIndex:X2}: treasures=[{string.Join(",", c.TreasureIds)}] models=[{string.Join(",", c.ModelIds.Select(m => m.ToString("X4")))}] pos={c.Positions.Count} silent={c.UsesSilentGrant}");
                }
            }
        }
        File.WriteAllText(@"C:\Users\wande\Documents\ffx-editor-main\work\_bika_chests.txt", sb.ToString());
        _out.WriteLine(sb.ToString());
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll;

public class MagicTimingCorpusTests
{
    [Fact]
    public void CurrentCorpus_TimingSnapshotsMatchSerializedWordsAndPreserveNoOpEdits()
    {
        var parser = new MagicDllParser(new MagicDllParserOptions { FieldMap = MagicFieldMap.LoadEmbedded() });
        var report = new List<object>();
        string[] paths = Directory.GetFiles(TestDataPaths.MagicCorpus, "magic_*.dll");
        Assert.NotEmpty(paths);
        foreach (string path in paths)
        {
            byte[] original = File.ReadAllBytes(path);
            var file = parser.Parse(path);
            var snapshot = new MagicTimingSnapshot(file);
            foreach (var field in snapshot.Fields)
                Assert.Equal(BitConverter.ToUInt32(original, file.DataSectionRawPtr + field.DataOffset), field.OriginalTick);
            Assert.True(snapshot.TryApply(original, Array.Empty<(MagicTimingField, uint)>(),
                out var identical, out var error), error);
            Assert.Equal(original, identical);
            var start = snapshot.Fields.FirstOrDefault(f => f.Kind == MagicTimingKind.ProgramStart && f.IsEditable);
            if (start != null)
            {
                Assert.True(snapshot.TryApply(original, new[] { (start, start.OriginalTick) },
                    out identical, out error), $"{file.DllName}: {error}");
                Assert.Equal(original, identical);
            }
            report.Add(new { file.DllName, Hash = Convert.ToHexString(SHA256.HashData(original)),
                Fields = snapshot.Fields.Count, Writable = snapshot.Fields.Count(f => f.IsEditable),
                Special = snapshot.Fields.Count(f => f.OriginalTick >= MagicTimingSnapshot.Infinite),
                Fractional = snapshot.Fields.Count(f => f.OriginalTick < MagicTimingSnapshot.Infinite &&
                    f.OriginalTick % 4096 != 0) });
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        File.WriteAllText(TestDataPaths.ReportPath("magic-timing-corpus.json"), JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }));
    }
}

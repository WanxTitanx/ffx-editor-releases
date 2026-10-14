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

public class MagicKeyframeCorpusTests
{
    [Fact]
    public void NativeSequences_PreserveWholeCorpusAndMatchDeclaredCounts()
    {
        string[] paths = Directory.GetFiles(TestDataPaths.MagicCorpus, "magic_*.dll");
        Assert.NotEmpty(paths);
        var parser = new MagicDllParser(new MagicDllParserOptions { FieldMap = MagicFieldMap.LoadEmbedded() });
        var entries = new List<object>();
        var rejected = new List<string>();
        foreach (string path in paths.OrderBy(p => p, StringComparer.Ordinal))
        {
            byte[] before = File.ReadAllBytes(path);
            var file = parser.Parse(path);
            Assert.Equal(before, file.Serialize());
            var slots = file.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots).ToArray();
            foreach (var slot in slots)
            {
                if (slot.Keyframes.Count == 0)
                {
                    rejected.Add($"{file.DllName}:slot@{slot.SlotAbs:X}:w{slot.NativeRecordWidth}:n{slot.KeyframeCount}:{slot.KeyframeError}");
                    continue;
                }
                Assert.Equal((int)slot.KeyframeCount, slot.Keyframes.Count);
                for (int index = 0; index < slot.Keyframes.Count; index++)
                {
                    var frame = slot.Keyframes[index];
                    Assert.Equal(slot.RecordOffset + index * slot.NativeRecordWidth, frame.RecordOffset);
                    Assert.Equal(before.AsSpan(file.DataSectionRawPtr + frame.RecordOffset,
                        slot.NativeRecordWidth).ToArray(), frame.Record);
                }
            }
            Assert.Equal(before, File.ReadAllBytes(path));
            entries.Add(new { file.DllName, Sha256 = Convert.ToHexString(SHA256.HashData(before)),
                Slots = slots.Length, Keyframes = slots.Sum(s => s.Keyframes.Count),
                Rejected = slots.Count(s => s.Keyframes.Count == 0),
                MaximumCount = slots.Select(s => (int)s.KeyframeCount).DefaultIfEmpty().Max() });
        }
        File.WriteAllText(TestDataPaths.ReportPath("magic-keyframes-corpus.json"),
            JsonSerializer.Serialize(new { Corpus = TestDataPaths.MagicCorpus, Entries = entries,
                Rejected = rejected }, new JsonSerializerOptions { WriteIndented = true }));
        // An incomplete parse must be visible as a failed coverage gate, not a success
        // inferred from the fact that the first record is still readable.
        Assert.True(rejected.Count == 0, string.Join(Environment.NewLine, rejected.Take(20)));
    }
}

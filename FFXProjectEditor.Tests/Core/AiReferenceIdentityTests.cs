using System;
using System.IO;
using FFXProjectEditor.Modules.MonsterAiEditor;
using Xunit;

namespace FFXProjectEditor.Tests.Core;

public class AiReferenceIdentityTests
{
    [Fact]
    public void CurrentProjectCannotBeItsOwnVanillaReference()
    {
        string root=Path.Combine(Path.GetTempPath(),"ai-reference-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string current=Path.Combine(root,"m208.bin"), original=Path.Combine(root,"original.bin");
            File.WriteAllBytes(current,new byte[]{1}); File.WriteAllBytes(original,new byte[]{1});
            Assert.False(MonsterAiEditor_DataModel.IsIndependentAiReference(current,current));
            Assert.False(MonsterAiEditor_DataModel.IsIndependentAiReference(Path.Combine(root,".","m208.bin"),current));
            Assert.True(MonsterAiEditor_DataModel.IsIndependentAiReference(original,current));
            Assert.False(MonsterAiEditor_DataModel.IsIndependentAiReference(Path.Combine(root,"missing.bin"),current));
            if (!OperatingSystem.IsWindows())
            {
                string alias=Path.Combine(root,"alias.bin");File.CreateSymbolicLink(alias,current);
                Assert.False(MonsterAiEditor_DataModel.IsIndependentAiReference(alias,current));
            }
        }
        finally { Directory.Delete(root,true); }
    }
}

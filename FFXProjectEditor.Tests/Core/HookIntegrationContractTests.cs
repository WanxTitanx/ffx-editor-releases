using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Mods;
using Xunit;

namespace FFXProjectEditor.Tests.Core;

public class HookIntegrationContractTests
{
    [Fact]
    public void NativeConsumerRejectsBanksLargerThanWordLength()
    {
        var (ability, prices) = HookModAuthoringTests.Kernel();
        Array.Resize(ref ability, 65536);
        Assert.Throws<InvalidDataException>(() => new Mod002Authoring(ability, prices, Mod002Manifest.Default(), "new_uspc"));
    }

    [Fact]
    public void BinaryIdentityCannotBeRenamedByDisplayTextEditing()
    {
        var (ability, prices) = HookModAuthoringTests.Kernel();
        var session = new Mod002Authoring(ability, prices, Mod002Manifest.Default(), "new_uspc");
        var edits = session.Rows.ToArray();
        edits[0] = edits[0] with { Name = "Unrelated ability" };
        Assert.Throws<InvalidDataException>(() => session.Preview(edits));
    }
}

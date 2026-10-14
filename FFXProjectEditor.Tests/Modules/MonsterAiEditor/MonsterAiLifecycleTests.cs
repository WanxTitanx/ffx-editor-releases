using System;
using System.Reflection;
using System.Globalization;
using System.Runtime.CompilerServices;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.Modules.MonsterAiEditor;
using FFXProjectEditor.Resources;
using Xunit;

namespace FFXProjectEditor.Tests.Modules.MonsterAiEditor;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MonsterAiTestCollection
{
    public const string Name = "Monster AI model and catalog";
}

[Collection(MonsterAiTestCollection.Name)]
public class MonsterAiLifecycleTests
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference CreateReleasedModel() => new(new MonsterAiEditor_DataModel());

    [Fact]
    public void CatalogEvent_DoesNotRetainAClosedModelsBuffers()
    {
        var weak = CreateReleasedModel();
        for (int i = 0; i < 3; i++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        }
        Assert.False(weak.IsAlive);
    }

    [Fact]
    public void LiveModel_StillReceivesCatalogRefresh()
    {
        var model = new MonsterAiEditor_DataModel();
        model.TemplateCommandOptions.Clear();
        var changed = (Action?)typeof(KernelMonsterMagicLiveSync)
            .GetField("Changed", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        changed?.Invoke();
        Assert.NotEmpty(model.TemplateCommandOptions);
        model.Dispose();
        GC.KeepAlive(model);
    }

    [Fact]
    public void ExplicitDispose_UnsubscribesWithoutWaitingForCollection()
    {
        var model = new MonsterAiEditor_DataModel();
        model.Dispose(); model.Dispose();
        model.TemplateCommandOptions.Clear();
        var changed = (Action?)typeof(KernelMonsterMagicLiveSync)
            .GetField("Changed", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
        changed?.Invoke();
        Assert.Empty(model.TemplateCommandOptions);
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en-US")]
    public void DependencyWarning_DoesNotDependOnTranslatedFlowText(string locale)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
            var row = new AiPhaseVariableAuditRow("key", "var0", "", "hash", 0, 0x56, 0, 1, null,
                "ID0", "private", "", 1, "w0", "", "", "opaque display text", "", "")
            { HasDataDependency = true };
            var method = typeof(MonsterAiEditor_DataModel).GetMethod("PhaseStepGuardRiskWarning", BindingFlags.NonPublic | BindingFlags.Static)!;
            string text = (string)method.Invoke(null, new object[] { row })!;
            Assert.Contains(Strings.AiPhaseVariableDependencyWarning, text);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}

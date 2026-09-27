using System.Linq;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Ai.Sin;
using Xunit;

namespace FFXProjectEditor.Tests.FfxLib.Ai.Sin;

public sealed class SinPresetRecipeResolverTests
{
    [Fact]
    public void Resolve_Uni003_LowHpRush_GrantsHasteSelfAndSlowToRandomFrontlineTarget()
    {
        SinPresetRecipeResolver.ResolveResult result = SinPresetRecipeResolver.Resolve("UNI-003");

        Assert.True(result.Ok, result.BlockReason);
        SinChainRecipe recipe = Assert.IsType<SinChainRecipe>(result.Recipe);
        SinChainNode node = Assert.Single(recipe.Nodes);
        SinCondition.HpBelowPercent condition = Assert.IsType<SinCondition.HpBelowPercent>(node.Condition);
        Assert.Equal(50, condition.Percent);
        Assert.Equal(2, node.Actions.Count);

        SinAction.PerformCommand haste = Assert.IsType<SinAction.PerformCommand>(node.Actions[0]);
        Assert.Equal(AiSnippetLibrary.SelfRef, haste.Target);
        Assert.Equal(0x3036, haste.CommandOperand);
        Assert.True(haste.Force);

        SinAction.PerformCommandOnRandomFrontlineChr slow =
            Assert.IsType<SinAction.PerformCommandOnRandomFrontlineChr>(node.Actions[1]);
        Assert.Equal(0x3038, slow.CommandOperand);
        Assert.True(slow.Force);
    }
}

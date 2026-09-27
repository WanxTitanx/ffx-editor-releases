using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.Ai.Sin
{
    /// <summary>In-memory monster AI emit result (mod bake / RT2 staging). Not a public Chain Builder apply.</summary>
    public sealed class SinMonsterEmitResult
    {
        public bool Ok { get; init; }
        public required string RecipeId { get; init; }
        public string? Error { get; init; }
        public byte[]? EditedMonster { get; init; }
        public int WorkerIndex { get; init; } = -1;
        public int EntrypointIndex { get; init; } = -1;
        public string? WorkerResolution { get; init; }
        public int AddedRows { get; init; }
        public IReadOnlyList<string> Notes { get; init; } = new List<string>();

        public static SinMonsterEmitResult Fail(string recipeId, string error) => new()
        {
            Ok = false,
            RecipeId = recipeId,
            Error = error,
        };
    }
}

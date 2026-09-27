using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.Modules.MonEditor
{
    internal static class MonsterAiAbilityCorpus_Service
    {
        public sealed class MonsterAiAbilityReference
        {
            public required ushort RawGameIndex { get; init; }
            public required string Source { get; init; }
        }

        public static bool IsAvailable => MonsterAiCorpus_Service.IsAvailable;
        public static string StatusSummary => MonsterAiCorpus_Service.StatusSummary;

        public static IReadOnlyList<MonsterAiAbilityReference> GetAbilityReferences(short rawMonsterId)
        {
            return MonsterAiCorpus_Service.GetAbilityReferences(rawMonsterId)
                .Select(reference => new MonsterAiAbilityReference
                {
                    RawGameIndex = reference.RawGameIndex,
                    Source = reference.Source
                })
                .ToList();
        }
    }
}

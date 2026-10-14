using System;
using System.Collections.Generic;
using FFXProjectEditor.FfxLib.Ai;

namespace FFXProjectEditor.Modules.MonsterAiEditor
{
    internal partial class MonsterAiEditor_DataModel
    {
        IReadOnlyList<AiIndirectDispatchUnit> DetectPromotedIndirectDispatchUnits(byte[] monsterBin)
        {
            if (selectedScript == null || !selectedScript.HasScript)
                return Array.Empty<AiIndirectDispatchUnit>();

            IReadOnlyList<AiIndirectDispatchUnit> detected = AiAutomation.DetectIndirectDispatchUnits(monsterBin, selectedScript);
            return AiComplexFamilyAuthoringPromotion.Promote(selectedScript, detected);
        }
    }
}

using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai;

/// <summary>Literal command operands with an actual, adjacent command consumer.</summary>
internal static class AiDirectCommandSites
{
    public static IEnumerable<AiInstruction> Enumerate(AiScriptFile script, IReadOnlyCollection<ushort> allowed)
    {
        for (int i = 0; i + 1 < script.Instructions.Count; i++)
        {
            AiInstruction push = script.Instructions[i], call = script.Instructions[i + 1];
            if (push.Opcode == 0xAE && push.HasOperand && allowed.Contains(push.Operand)
                && call.Offset == push.Offset + 3 && call.HasOperand
                && (call.Opcode == 0xB5 || call.Opcode == 0xD8)
                && (call.Operand == 0x700B || call.Operand == 0x705A))
                yield return push;
        }
    }
}

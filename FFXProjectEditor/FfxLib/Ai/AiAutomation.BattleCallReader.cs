using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai
{
    // F7.1 (2026-08-02): F5.4 battle-call site reader — extraído do god file AiAutomation.cs.
    public static partial class AiAutomation
    {
        // F5.4: reader de call sites battle-call (familia 0x70xx).
        // Os 8 monstros do censo (m212/m218/m226/m227/m230/m277/m279/m280) nao tem o padrao de
        // dispatch indireto (0 route writes, 0 calls classic perform) - sao battle-call puro:
        // calls diretos 0x70xx com argumentos empilhados (RE: FFX_Atel_DispatchNativeCall 0x877720,
        // tabela estatica g_FFX_Atel_BattleFuncspaceTable 0xC42618; handlers nomeados na IDB).
        // Este reader expoe cada call site como unidade de preview com os argumentos (PUSHII
        // literais) editaveis byte-local; argumentos de var/call aninhado sao read-only.
        public static IReadOnlyList<AiIndirectDispatchUnit> DetectBattleCallSiteUnits(AiScriptFile script)
        {
            TryBuildBattleCallSiteUnits(script, out List<AiIndirectDispatchUnit> units);
            return units;
        }

        static bool TryBuildBattleCallSiteUnits(AiScriptFile script, out List<AiIndirectDispatchUnit> units)
        {
            units = new List<AiIndirectDispatchUnit>();
            IReadOnlyList<AiInstruction> instructions = script.Instructions;
            for (int i = 0; i < instructions.Count; i++)
            {
                AiInstruction call = instructions[i];
                if (call.Opcode != CALL && call.Opcode != CALLPOPA)
                    continue;
                if ((call.Operand & 0xF000) != 0x7000 || call.Operand == 0)
                    continue;
                // classic perform (0x700B/0x705A) e dominio dos detectores de dispatch indireto (F4/F3);
                // aqui expomos o resto da familia battle (call sites diretos).
                if (call.Operand == PerformCommand || call.Operand == ForcePerformCommand)
                    continue;

                string name = AiCallNameCatalog.DisplayName(call.Operand) ?? $"call_{call.Operand:X4}";
                int argc = AiCallNameCatalog.CatalogArgc(call.Operand) ?? 1;
                List<BattleCallArgument> args = CollectBattleCallArguments(instructions, i, argc);
                if (args.Count == 0)
                    continue;

                int unitIndex = units.Count;
                var payloadWrites = args
                    .Select(arg => BuildPreviewNamedWrite(
                        script,
                        $"battle-call.{unitIndex}.arg{arg.ArgPosition}",
                        $"arg{arg.ArgPosition}",
                        arg.ValueSummary,
                        arg.Offset))
                    .ToList();
                var consumers = new List<AiIndirectDispatchConsumer>
                {
                    new(
                        name,
                        PreviewCommandVariableIndex(unitIndex, 0),
                        name,
                        PreviewTargetVariableIndex(unitIndex, 0),
                        "arg1",
                        call.Offset),
                };
                var targetSlots = args
                    .Select((arg, n) => BuildBattleCallArgSlot(unitIndex, n, arg))
                    .ToList();
                string offsetSummary = BuildOffsetSummary(
                    new[] { call.Offset }.Concat(args.Select(arg => arg.Offset)));
                string warning =
                    "Preview battle-call: argumentos lidos por contabilidade de pilha (PUSHII/PUSHV e calls aninhados). " +
                    "Slots literais (PUSHII) sao editaveis byte-local; argumentos de var/call aninhado sao read-only.";
                units.Add(new AiIndirectDispatchUnit(
                    $"battle-call-{call.Offset:X4}",
                    unitIndex,
                    $"call nativo battle {name}",
                    $"call site 0x{call.Offset:X4}: {name} ({argc} arg{(argc == 1 ? "" : "s")}).",
                    $"call id 0x{call.Operand:X4} (namespace 7, battle).",
                    AiIndirectDispatchCapabilityTier.PreviewReadOnly,
                    $"call battle {name}",
                    payloadWrites,
                    consumers,
                    new List<string> { warning },
                    offsetSummary,
                    warning,
                    Array.Empty<AiIndirectDispatchEditableSlot>(),
                    targetSlots,
                    null,
                    null));
            }

            return units.Count > 0;
        }

        sealed record BattleCallArgument(int ArgPosition, int Offset, byte Opcode, ushort Operand, string? NestedCallName)
        {
            public string ValueSummary =>
                NestedCallName != null
                    ? $"{NestedCallName}(...)"
                    : Opcode == PUSHII
                        ? DescribeTargetOperand(Operand)
                        : $"var {Operand:X4}";
        }

        // Contabilidade de pilha para o call em callIndex: devolve os `argc` argumentos mais proximos
        // do call (posicao 1 = mais proximo do call). PUSHII/PUSHV fornecem 1 argumento; um call
        // aninhado (0xB5/0xD8) consome os proprios argumentos e devolve 1 resultado. Janela interrompida
        // por instrucao desconhecida e devolvida honestamente (parcial).
        static List<BattleCallArgument> CollectBattleCallArguments(
            IReadOnlyList<AiInstruction> instructions,
            int callIndex,
            int argc)
        {
            var args = new List<BattleCallArgument>();
            int need = argc;
            int idx = callIndex - 1;
            while (need > 0 && idx >= 0)
            {
                AiInstruction ins = instructions[idx];
                switch (ins.Opcode)
                {
                    case PUSHII:
                    case PUSHV:
                        args.Add(new BattleCallArgument(need, ins.Offset, ins.Opcode, ins.Operand, null));
                        need--;
                        idx--;
                        break;
                    case CALL:
                    case CALLPOPA:
                    {
                        int innerArgc = AiCallNameCatalog.CatalogArgc(ins.Operand) ?? 1;
                        List<BattleCallArgument> inner = CollectBattleCallArguments(instructions, idx, innerArgc);
                        if (inner.Count < innerArgc)
                            return args; // janela do call interno incompleta - parar honestamente
                        args.Add(new BattleCallArgument(
                            need,
                            ins.Offset,
                            ins.Opcode,
                            ins.Operand,
                            AiCallNameCatalog.DisplayName(ins.Operand) ?? $"call_{ins.Operand:X4}"));
                        need--;
                        idx -= inner.Count + 1;
                        break;
                    }
                    default:
                        return args; // instrucao desconhecida - janela interrompida
                }
            }

            return args.OrderBy(arg => arg.ArgPosition).ToList();
        }

        static AiIndirectDispatchEditableTargetSlot BuildBattleCallArgSlot(
            int unitIndex,
            int slotIndex,
            BattleCallArgument arg)
        {
            bool literal = arg.Opcode == PUSHII;
            IReadOnlyList<AiIndirectDispatchEditableOperand> operands = literal
                ? new[]
                {
                    new AiIndirectDispatchEditableOperand(
                        $"battle-call.{unitIndex}.arg{arg.ArgPosition}.literal",
                        $"arg{arg.ArgPosition}",
                        arg.Offset,
                        arg.Opcode,
                        arg.Operand,
                        DescribeTargetOperand(arg.Operand)),
                }
                : Array.Empty<AiIndirectDispatchEditableOperand>();

            return new AiIndirectDispatchEditableTargetSlot(
                $"battle-call.{unitIndex}.arg{arg.ArgPosition}",
                $"arg{arg.ArgPosition}",
                PreviewPseudoVariableIndex(0xD000, unitIndex, slotIndex),
                $"arg{arg.ArgPosition}",
                arg.Offset,
                literal ? AiIndirectDispatchTargetSlotSourceKind.Literal : AiIndirectDispatchTargetSlotSourceKind.ComputedRecipe,
                arg.Operand,
                arg.ValueSummary,
                literal
                    ? "argumento literal (PUSHII) do call battle - editavel byte-local."
                    : "argumento computado (PUSHV ou call aninhado) - read-only na preview.",
                literal,
                literal ? AiTargetRecipeKind.Literal : null,
                operands,
                Array.Empty<AiIndirectDispatchVariableReference>());
        }

    }
}

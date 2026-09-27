using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ai
{
    /// <summary>Semantic context for a phase variable, resolved from family-specific detectors.
    /// Bridges the Advanced Phase Manager's writer knowledge into the Common Phase Manager UI.</summary>
    public sealed record AiVariableSemanticAnnotation(
        string Label,
        string CalculationSummary,
        string CommandName,
        string CommandHex,
        string SourceWriter)
    {
        public string CommandDisplay => string.IsNullOrWhiteSpace(CommandName)
            ? CommandHex
            : $"{CommandName} ({CommandHex})";

        public string FullDisplay => string.IsNullOrWhiteSpace(CalculationSummary)
            ? Label
            : $"{Label} — {CalculationSummary}";
    }

    /// <summary>Runs all family-specific TryBuildDescriptors and resolves variable indices to semantic
    /// annotations. Used by the Common Phase Manager to show what each var means.</summary>
    public static class AiPhaseVariableSemanticResolver
    {
        /// <summary>Build a dictionary of variableIndex -> semantic annotation for a given script.</summary>
        public static Dictionary<int, AiVariableSemanticAnnotation> Resolve(AiScriptFile script)
        {
            var result = new Dictionary<int, AiVariableSemanticAnnotation>();

            if (script == null || !script.HasScript)
                return result;

            // Flux native threshold (Protect: priv0018, Reflect: priv001C)
            ResolveFluxThresholds(script, result);

            // Omnis elemental cluster (priv0024-0030: dispatch table)
            ResolveOmnisCluster(script, result);

            // Mortiorchis companion (0x608C body handoff, 0x60A9 absorption)
            ResolveMortiorchis(script, result);

            return result;
        }

        static void ResolveFluxThresholds(AiScriptFile script, Dictionary<int, AiVariableSemanticAnnotation> result)
        {
            if (!AiFluxNativeThresholdWriter.TryBuildDescriptor(script, "priv0018",
                    out AiFluxNativeThresholdDescriptor? protect, out _) || protect == null)
                return;

            int idx = FindVariableIndex(script, "priv0018");
            if (idx >= 0)
            {
                string calc = protect.HasExplicitNumerator
                    ? $"{protect.DerivedPercent:0.##}% do HP (maxHP/{protect.CurrentDenominator}\u00D7{protect.CurrentNumerator})"
                    : $"{protect.DerivedPercent:0.##}% do HP (maxHP/{protect.CurrentDenominator})";
                result[idx] = new AiVariableSemanticAnnotation(
                    "Threshold Protect",
                    calc, "", "", "FluxNativeThresholdWriter");
            }

            if (!AiFluxNativeThresholdWriter.TryBuildDescriptor(script, "priv001C",
                    out AiFluxNativeThresholdDescriptor? reflect, out _) || reflect == null)
                return;

            int idx2 = FindVariableIndex(script, "priv001C");
            if (idx2 >= 0)
            {
                string calc = reflect.HasExplicitNumerator
                    ? $"{reflect.DerivedPercent:0.##}% do HP (maxHP/{reflect.CurrentDenominator}\u00D7{reflect.CurrentNumerator})"
                    : $"{reflect.DerivedPercent:0.##}% do HP (maxHP/{reflect.CurrentDenominator})";
                result[idx2] = new AiVariableSemanticAnnotation(
                    "Threshold Reflect",
                    calc, "", "", "FluxNativeThresholdWriter");
            }
        }

        static void ResolveOmnisCluster(AiScriptFile script, Dictionary<int, AiVariableSemanticAnnotation> result)
        {
            if (!AiOmnisClusterWriter.TryBuildDescriptors(script,
                    out IReadOnlyList<AiOmnisClusterDescriptor> descriptors, out _))
                return;

            // Omnis descriptors reference PUSHII operands, not variable indices directly.
            // The vars priv0024-0030 are used as dispatch table storage; we annotate them
            // based on the command operands they store.
            foreach (AiOmnisClusterDescriptor d in descriptors)
            {
                string name = AiCommandId.Decode(d.CurrentCommand).Name ?? "";
                string hex = $"0x{d.CurrentCommand:X4}";
                string label = string.IsNullOrWhiteSpace(name) ? $"Comando {hex}" : name;

                // Find vars that reference this command operand (heuristic: PUSHII with this operand)
                foreach (AiVariable v in script.Variables)
                {
                    if (v.Storage != 0x56) continue; // private var only
                    // We store a generic annotation: the Omnis table vars are slotted sequentially
                    if (!result.ContainsKey(v.Index))
                    {
                        result[v.Index] = new AiVariableSemanticAnnotation(
                            $"Omnis — {label}",
                            "", name, hex, "AiOmnisClusterWriter");
                    }
                }
            }
        }

        static void ResolveMortiorchis(AiScriptFile script, Dictionary<int, AiVariableSemanticAnnotation> result)
        {
            if (!AiMortiorchisCompanionWriter.TryBuildDescriptors(script,
                    out IReadOnlyList<AiMortiorchisCompanionDescriptor> descriptors, out _))
                return;

            foreach (AiMortiorchisCompanionDescriptor d in descriptors)
            {
                string name = AiCommandId.Decode(d.CurrentCommand).Name ?? "";
                string label = string.IsNullOrWhiteSpace(name)
                    ? $"Companion 0x{d.CurrentCommand:X4}"
                    : $"Companion — {name}";
                string hex = $"0x{d.CurrentCommand:X4}";

                foreach (AiVariable v in script.Variables)
                {
                    if (v.Storage != 0x56) continue;
                    if (!result.ContainsKey(v.Index))
                    {
                        result[v.Index] = new AiVariableSemanticAnnotation(
                            label, "", name, hex, "AiMortiorchisCompanionWriter");
                    }
                }
            }
        }

        static int FindVariableIndex(AiScriptFile script, string name)
        {
            AiVariable? v = script.Variables.FirstOrDefault(
                x => x.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase));
            return v?.Index ?? -1;
        }
    }
}

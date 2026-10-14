using FFXProjectEditor.FfxLib.NewContent;
using System;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.Tools
{
    internal static class NewContentTextPlanRt0
    {
        public static int Run(string masterRoot)
        {
            Console.WriteLine("=== NewContentTextPlan RT0 (carrier completeness + guardrails) ===");
            Console.WriteLine($"master root : {masterRoot}");

            var cases = new (NewContentKind kind, int id, string expectedCarrier, bool hookRequired)[]
            {
                (NewContentKind.SpellCommand, 8, "battle/kernel/command.bin", false),
                (NewContentKind.ItemCommand, 1, "battle/kernel/item.bin", false),
                (NewContentKind.MonsterMagic, 12, "battle/kernel/monmagic1.bin", false),
                (NewContentKind.AutoAbility, 129, "battle/kernel/a_ability.bin", false),
                (NewContentKind.Gear, 10, "battle/kernel/w_name.bin", false),
                (NewContentKind.Reskin, 10, "battle/kernel/w_name.bin", false),
                (NewContentKind.Ps3MagicTexture, 8, "battle/kernel/command.bin", false),
                (NewContentKind.BattleMessage, 3, "battle/kernel/btl_txt.bin", false),
                (NewContentKind.EventDialogue, 1, "event/obj/<area>/<event>.ebp", false),
                (NewContentKind.HookSidecar, 320, "hook/sidecar-text.json", true),
            };

            int pass = 0;
            foreach (var c in cases)
            {
                NewContentTextPlan plan;
                try
                {
                    plan = NewContentTextPlanResolver.Resolve(c.kind, c.id);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"FAIL {c.kind}: resolver threw {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                bool ok = ValidatePlan(masterRoot, plan, c.expectedCarrier, c.hookRequired, out string reason);
                if (ok)
                {
                    pass++;
                    Console.WriteLine($"PASS {plan.ContentKind,-17} carrier={plan.TextCarrierFile} status={plan.EvidenceStatus} hook={plan.RuntimeHookRequired}");
                }
                else
                {
                    Console.WriteLine($"FAIL {plan.ContentKind,-17} {reason}");
                }
            }

            Console.WriteLine($"summary: {pass}/{cases.Length} carrier plans complete.");
            Console.WriteLine("Scope: this gate proves planning/linkage completeness only; it does not write game files or prove RT2.");
            return pass == cases.Length ? 0 : 1;
        }

        static bool ValidatePlan(string masterRoot, NewContentTextPlan plan, string expectedCarrier, bool hookRequired, out string reason)
        {
            if (!plan.IsComplete(out reason))
                return false;

            if (!string.Equals(plan.TextCarrierFile, expectedCarrier, StringComparison.Ordinal))
            {
                reason = $"expected carrier {expectedCarrier}, got {plan.TextCarrierFile}.";
                return false;
            }

            if (plan.RuntimeHookRequired != hookRequired)
            {
                reason = $"expected hook={hookRequired}, got {plan.RuntimeHookRequired}.";
                return false;
            }

            string combined = string.Join(" ", plan.TextCarrierFile, plan.Notes, string.Join(" ", plan.LinkFields), string.Join(" ", plan.DeployFiles));
            if (combined.Contains("Monster AI", StringComparison.OrdinalIgnoreCase) || combined.Contains("ATEL", StringComparison.OrdinalIgnoreCase))
            {
                reason = "plan leaked Monster AI/ATEL scope.";
                return false;
            }

            if (plan.ContentKind != NewContentKind.HookSidecar && plan.DeployFiles.Any(f => f.StartsWith("hook/", StringComparison.OrdinalIgnoreCase)))
            {
                reason = "native carrier plan unexpectedly deploys hook sidecar.";
                return false;
            }

            if (IsNativeFileCarrier(plan.TextCarrierFile))
            {
                string carrierPath = Path.Combine(masterRoot, plan.Locale, plan.TextCarrierFile.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(carrierPath))
                {
                    reason = $"carrier file not found: {carrierPath}";
                    return false;
                }
            }

            reason = "";
            return true;
        }

        static bool IsNativeFileCarrier(string carrier)
        {
            return !carrier.StartsWith("hook/", StringComparison.OrdinalIgnoreCase)
                && !carrier.Contains('<')
                && !carrier.Contains('*');
        }
    }
}

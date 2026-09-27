using System;
using System.Collections.Generic;
using System.Linq;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Core.LLM;

/// <summary>
/// Guarda do contrato: decisões de aceite/rejeição são 100% código compilado +
/// LlmSessionSettings do usuário. Texto do modelo (e prompt injection nele)
/// NUNCA altera estas regras.
/// </summary>
public static class LlmGuard
{
    /// <summary>Catálogo de receitas comprovadas do perfil 1 (byte-local).</summary>
    public static readonly IReadOnlySet<string> ProvenRecipes = new HashSet<string>
    {
        // PPP family (source-linked, T3 copy-only provada)
        "ppp-sclmove-t3",         // janela 16B de receita PPP comprovada (source-linked, T3)

        // Writer adapters (registrados no WriterAdapterCatalog, RT0 provados)
        "ability-command-t1",     // command.bin/monmagic2.bin grow (AbilityCommandAdapter, RT0 byte-identity)
        "monster-file-t1",        // monster file read/write (MonsterFileAdapter, RT0 185/185 baseline)
        "monster-stat-sheet-t1",  // MonsterStatSheetAdapter (stat block preserve-write, RT0)
        "treasure-edit-t1",       // TreasureAdapter (takara.bin, RT0)
        "atel-phase-rotation-t1", // AtelPhaseRotationAdapter (ATEL script, RT0)

        // Byte-safe writers (RT0-validated, slot-only/preserve guards)
        "monster-magic-grow-t1",  // MonsterMagicGrowWriter (monmagic2.bin grow, RT0)
        "command-grow-t1",        // CommandGrowWriter (command.bin grow, RT0)
        "encounter-table-rebuild-t1", // EncounterTable_File (rebuild seguro, RT0)
        "formation-slot-patch-t1",    // FormationSlotWriter (16-byte slot-only guard, RT0)
        "battle-companion-activation-t1", // BattleCompanionActivationWriter (m213 host/companion, RT0)
        "monster-capture-flag-t1",  // MonsterCaptureFlagWriter (bit-safe, RT0)
        "monster-clone-t1",         // MonsterCloner (m###.bin -> novo slot, RT0)
        "sphere-grid-transplant-t1", // SphereGridDeployPolicy (Safe Transplant Mode, RT0 provado)

        // Perfil 1 genérico — byte-splice crua via BytePatchAdapter (a receita declarada
        // pelo modelo é o rótulo de evidência; a escrita é sempre offset+payload bounded).
        "byte-patch-t1",

        // novas receitas entram SÓ com evidência própria + teste RT0
    };

    public static LlmGuardResult EvaluatePreconditions(
        PatchProposal proposal,
        LlmSessionSettings settings,
        CapabilityDescriptor capability,
        string sourceRoot,
        string actualBeforeHash)
    {
        var failed = new List<string>();

        if (!settings.IsEnabled)
            return Reject(LlmRejectionReason.OptInDisabled, failed, Strings.U_Lbl_GuardOptInDisabled);

        if (!ProvenRecipes.Contains(proposal.RecipeId))
            return Reject(LlmRejectionReason.UnknownRecipe, failed,
                string.Format(Strings.U_Lbl_GuardRecipeNotCatalog, proposal.RecipeId));

        if (!string.Equals(proposal.CapabilityId, capability.Id, StringComparison.Ordinal))
            return Reject(LlmRejectionReason.MissingPrecondition, failed,
                Strings.U_Lbl_GuardCapabilityMismatch);

        if (!IsOperationAllowed(proposal.Operation, settings))
            return Reject(LlmRejectionReason.OperationNotAllowed, failed,
                string.Format(Strings.U_Lbl_GuardOperationNotAllowed, proposal.Operation));

        if (proposal.Operation != PatchOperationKind.ByteReplace)
            return Reject(LlmRejectionReason.OperationNotAllowed, failed,
                Strings.U_Lbl_GuardProfile1Only);

        var path = PathGuard.ValidateSourcePath(sourceRoot, proposal.Target.RelativePath);
        if (!path.IsValid)
            return Reject(LlmRejectionReason.MalformedPayload, failed,
                string.Format(Strings.U_Lbl_GuardPathBlocked, string.Join("; ", path.Errors)));

        if (!string.Equals(actualBeforeHash, proposal.BeforeHash, StringComparison.OrdinalIgnoreCase))
            return Reject(LlmRejectionReason.BeforeHashMismatch, failed,
                Strings.F2_current_file_hash_does_not_match_the_pro_bea2b8e1);

        int newLen = DecodeBase64Length(proposal.Target.NewBytesBase64);
        if (newLen < 0 || newLen != proposal.Target.Length)
            return Reject(LlmRejectionReason.MalformedPayload, failed,
                Strings.U_Lbl_GuardLengthMismatch);
        if (newLen > settings.MaxProposalBytes)
            return Reject(LlmRejectionReason.LimitsExceeded, failed,
                string.Format(Strings.U_Lbl_GuardExceedsMaxBytes, settings.MaxProposalBytes));

        if (proposal.Verifications.Count == 0 || proposal.Verifications.All(v => !v.Required))
            return Reject(LlmRejectionReason.MissingPrecondition, failed,
                Strings.U_Lbl_GuardMissingVerification);

        return new LlmGuardResult
        {
            Allowed = true,
            Reason = LlmRejectionReason.None,
            FailedChecks = Array.Empty<string>(),
            Message = null
        };
    }

    public static bool IsOperationAllowed(PatchOperationKind op, LlmSessionSettings s) => op switch
    {
        PatchOperationKind.ByteReplace => s.CapabilityFlags.HasFlag(LlmCapabilityFlag.ByteReplace),
        PatchOperationKind.Insert     => s.CapabilityFlags.HasFlag(LlmCapabilityFlag.Insert),
        PatchOperationKind.Delete     => s.CapabilityFlags.HasFlag(LlmCapabilityFlag.Delete),
        PatchOperationKind.Grow       => s.CapabilityFlags.HasFlag(LlmCapabilityFlag.Grow),
        _ => false
    };

    private static LlmGuardResult Reject(LlmRejectionReason reason, List<string> failed, string msg)
        => new() { Allowed = false, Reason = reason, FailedChecks = failed, Message = msg };

    private static int DecodeBase64Length(string b64)
    {
        try { return Convert.FromBase64String(b64).Length; }
        catch (FormatException) { return -1; }
    }
}

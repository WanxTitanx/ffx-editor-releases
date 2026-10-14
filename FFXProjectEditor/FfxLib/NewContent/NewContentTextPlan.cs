using System;
using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.NewContent
{
    public enum NewContentKind
    {
        SpellCommand,
        ItemCommand,
        MonsterMagic,
        AutoAbility,
        Gear,
        Reskin,
        Ps3MagicTexture,
        BattleMessage,
        EventDialogue,
        HookSidecar
    }

    public enum NewContentEvidenceStatus
    {
        ShipToday,
        ValidatedOffline,
        WriterLab,
        Rt2Pending,
        HookCandidate,
        HookOnly,
        Blocked,
        ResearchOnly
    }

    public sealed class NewContentTextPlan
    {
        public NewContentKind ContentKind { get; init; }
        public int GameId { get; init; }
        public string Locale { get; init; } = "";
        public string TextCarrierFile { get; init; } = "";
        public int RecordIndex { get; init; }
        public IReadOnlyList<string> LinkFields { get; init; } = Array.Empty<string>();
        public string DisplayName { get; init; } = "";
        public string Description { get; init; } = "";
        public IReadOnlyList<string> DeployFiles { get; init; } = Array.Empty<string>();
        public bool RuntimeHookRequired { get; init; }
        public NewContentEvidenceStatus EvidenceStatus { get; init; }
        public string Notes { get; init; } = "";

        public bool IsComplete(out string reason)
        {
            if (string.IsNullOrWhiteSpace(Locale)) { reason = "Locale is empty."; return false; }
            if (string.IsNullOrWhiteSpace(TextCarrierFile)) { reason = "TextCarrierFile is empty."; return false; }
            if (RecordIndex < 0) { reason = "RecordIndex is negative."; return false; }
            if (LinkFields.Count == 0) { reason = "LinkFields is empty."; return false; }
            if (string.IsNullOrWhiteSpace(DisplayName)) { reason = "DisplayName is empty."; return false; }
            if (string.IsNullOrWhiteSpace(Description)) { reason = "Description is empty."; return false; }
            if (DeployFiles.Count == 0) { reason = "DeployFiles is empty."; return false; }
            reason = "";
            return true;
        }
    }

    public static class NewContentTextPlanResolver
    {
        public static NewContentTextPlan Resolve(
            NewContentKind kind,
            int gameId,
            string locale = "new_uspc",
            string? assetPath = null)
        {
            if (gameId < 0)
                throw new ArgumentOutOfRangeException(nameof(gameId), "Game id must be non-negative.");
            if (string.IsNullOrWhiteSpace(locale))
                throw new ArgumentException("Locale must be provided.", nameof(locale));

            return kind switch
            {
                NewContentKind.SpellCommand => Build(
                    kind,
                    gameId,
                    locale,
                    "battle/kernel/command.bin",
                    new[] { "command id -> Ability_Command record text offsets" },
                    "Spell/command names live in command.bin; learned ids outside the native range still need menu/learn/save work.",
                    false,
                    NewContentEvidenceStatus.ValidatedOffline),

                NewContentKind.ItemCommand => Build(
                    kind,
                    gameId,
                    locale,
                    "battle/kernel/item.bin",
                    new[] { "item command id -> Ability_Command record text offsets" },
                    "Item command text uses the same Ability_Command writer route as command.bin.",
                    false,
                    NewContentEvidenceStatus.ValidatedOffline),

                NewContentKind.MonsterMagic => Build(
                    kind,
                    gameId,
                    locale,
                    gameId < 300 ? "battle/kernel/monmagic1.bin" : "battle/kernel/monmagic2.bin",
                    new[] { "monster magic id -> Ability_Command record text offsets" },
                    "Monster magic text is byte-safe through the preserve-only Ability_Command text pool gate.",
                    false,
                    NewContentEvidenceStatus.ValidatedOffline),

                NewContentKind.AutoAbility => Build(
                    kind,
                    gameId,
                    locale,
                    "battle/kernel/a_ability.bin",
                    new[] { "auto-ability id -> prefix name/description scripts", "arms_rate.bin row for customization price" },
                    "Data-driven auto-abilities use a_ability.bin text; hardcoded-by-id effects still need hook/runtime work.",
                    false,
                    gameId >= 129 ? NewContentEvidenceStatus.WriterLab : NewContentEvidenceStatus.ValidatedOffline,
                    new[] { "battle/kernel/a_ability.bin", "battle/kernel/arms_rate.bin" }),

                NewContentKind.Gear => Build(
                    kind,
                    gameId,
                    locale,
                    "battle/kernel/w_name.bin",
                    new[] { "EquipmentStruct.Name_id @0x00 -> w_name.bin row" },
                    "Gear payload files carry name ids; w_name.bin is the display-name carrier.",
                    false,
                    NewContentEvidenceStatus.ValidatedOffline),

                NewContentKind.Reskin => Build(
                    kind,
                    gameId,
                    locale,
                    "battle/kernel/w_name.bin",
                    new[] { "same gear row/model id keeps the owner name unless explicitly changed" },
                    "A reskin is an asset change, not a new canonical text namespace.",
                    false,
                    NewContentEvidenceStatus.Rt2Pending,
                    string.IsNullOrWhiteSpace(assetPath)
                        ? new[] { "battle/kernel/w_name.bin" }
                        : new[] { "battle/kernel/w_name.bin", assetPath! }),

                NewContentKind.Ps3MagicTexture => Build(
                    kind,
                    gameId,
                    locale,
                    "battle/kernel/command.bin",
                    new[] { "command/effect id -> observed magic_#### texture path" },
                    "The player-facing name remains the command name; .dds.phyre replacement is a visual asset LAB.",
                    false,
                    NewContentEvidenceStatus.WriterLab,
                    string.IsNullOrWhiteSpace(assetPath)
                        ? new[] { "battle/kernel/command.bin", "ps3data/magic/magic_####/tex/d3d11/*.dds.phyre" }
                        : new[] { "battle/kernel/command.bin", assetPath! }),

                NewContentKind.BattleMessage => Build(
                    kind,
                    gameId,
                    locale,
                    "battle/kernel/btl_txt.bin",
                    new[] { "battle text table id -> btl_txt.bin row" },
                    "Battle text is contextual messaging, not a canonical spell/gear namespace.",
                    false,
                    NewContentEvidenceStatus.ValidatedOffline),

                NewContentKind.EventDialogue => Build(
                    kind,
                    gameId,
                    locale,
                    "event/obj/<area>/<event>.ebp",
                    new[] { "event script/dialogue reference -> event text resource" },
                    "Event text is for scene/tutorial/reward dialogue, not global command or gear names.",
                    false,
                    NewContentEvidenceStatus.ValidatedOffline),

                NewContentKind.HookSidecar => Build(
                    kind,
                    gameId,
                    locale,
                    "hook/sidecar-text.json",
                    new[] { "hook-managed runtime key -> sidecar text entry" },
                    "Use only when the native carrier cannot discover or display the new id.",
                    true,
                    NewContentEvidenceStatus.HookCandidate),

                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported new-content kind.")
            };
        }

        static NewContentTextPlan Build(
            NewContentKind kind,
            int gameId,
            string locale,
            string carrier,
            IReadOnlyList<string> linkFields,
            string notes,
            bool hookRequired,
            NewContentEvidenceStatus status,
            IReadOnlyList<string>? deployFiles = null)
        {
            return new NewContentTextPlan
            {
                ContentKind = kind,
                GameId = gameId,
                Locale = locale,
                TextCarrierFile = carrier,
                RecordIndex = gameId,
                LinkFields = linkFields,
                DisplayName = $"{kind} #{gameId}",
                Description = $"Native text carrier plan for {kind} id {gameId}.",
                DeployFiles = deployFiles ?? new[] { carrier },
                RuntimeHookRequired = hookRequired,
                EvidenceStatus = status,
                Notes = notes
            };
        }
    }
}

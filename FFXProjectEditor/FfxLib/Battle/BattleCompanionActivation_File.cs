// ============================================================================
// BattleCompanionActivation_File — read-only reader of hidden companion activation / summon-handoff packages
// PURPOSE : recognizes the proven m213 "preseeded hidden companions" package in specific battles, surfacing
//           typed rows + formation summary + honest Notes. Read-only and no-throw.
// WHY     : a narrow static reading (host btlSetAppear + performCommand 0x408A Summon against #01/#02) proves
//           preseeded-hidden -> reveal/activation candidate — NOT a universal 0x408A meaning nor spawn-from-zero.
// EVIDENCE: RE docs/reverse/...(GUADO_GUARDIAN_COMPLEX_SURFACING_2026-07-01); five battles close the full package.
// MAINT   : stays resolver-based (never reaches Project_Service directly); mcyt00_21 is a drift/collision
//           sentinel — treat as such. Extend only with new proven packages.
// ============================================================================
// BattleCompanionActivation_File — read-only reader for hidden companion activation / summon-handoff packages.
//
// RE truth (GUADO_GUARDIAN_COMPLEX_SURFACING_2026-07-01):
//   In the m213 lane, the host uses Battle.btlSetAppear(Self, 0, 0) at init and later issues
//   performCommand(..., 0x408A [Summon]) against Monster#01 / Monster#02 in six script-token cases:
//     - maca03_20 / _21 / _22
//     - mcyt00_20 / _21 / _22
//   Current-corpus truth is narrower: only five battles still expose slot0=m213 and close the full package.
//   mcyt00_21 keeps the host-script footprint, but the battle.bin currently on disk collides with a Dark Aeons
//   trio and must be treated as a drift/collision sentinel for the battle-editor lane.
//   Those two peers already exist in the battle formation, and the companion monster scripts set
//   Self.BirthAnimation = Hidden for those same CurrentBattle tokens.
//
// This proves a narrow static reading:
//   preseeded hidden companions -> reveal/activation candidate
// It does NOT prove a universal runtime meaning for 0x408A, and it does NOT support spawn-from-zero.
//
// Design rules:
//   1. Narrow + honest: recognize only the proven m213 package.
//   2. Read-only and no-throw: unsupported/missing bits surface as Notes.
//   3. Keep resolver-based monster access: the reader never reaches into Project_Service directly.

using System;
using System.Collections.Generic;
using System.Linq;
using FFXProjectEditor.FfxLib.Ai;
using FFXProjectEditor.FfxLib.Dictionaries;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.FfxLib.Battle
{
    public sealed class BattleCompanionActivationRow
    {
        public required string SlotLabel { get; init; }
        public required ushort TargetOperand { get; init; }
        public required int FormationSlotIndex { get; init; }
        public required int MonsterId { get; init; }
        public required string MonsterLabel { get; init; }
        public required string HostActionLabel { get; init; }
        public required int HostActionOffset { get; init; }
        public required bool HiddenForSelectedBattle { get; init; }
        public required string HiddenBattleIdsLabel { get; init; }
        public required string EvidenceLabel { get; init; }

        public string HostActionOffsetHex => HostActionOffset >= 0 ? $"0x{HostActionOffset:X}" : "—";
        public string HiddenStatusLabel => HiddenForSelectedBattle ? "Hidden in this battle: yes" : "Hidden in this battle: not proven";
    }

    public sealed class BattleCompanionActivation_File
    {
        public required string BattleId { get; init; }
        public required string BattleTokenLabel { get; init; }
        public required string FormationSummary { get; init; }
        public required string ActivationCoverageSummary { get; init; }
        public required string HumanSummary { get; init; }
        public required IReadOnlyList<BattleCompanionActivationRow> Rows { get; init; }
        public required IReadOnlyList<string> Notes { get; init; }
        public required string ProofLane { get; init; }
        public required string WriterPolicy { get; init; }
        public required bool HostUsesBtlSetAppear { get; init; }
        public required bool FormationHasPreseededCompanions { get; init; }
        public required bool SelectedBattleTokenKnown { get; init; }
        public required bool SelectedBattleHasHostSummonPair { get; init; }
        public required bool SpawnFromZeroNotSupported { get; init; }
        public required bool HostTreatsPeerSlotsAsGameplayCompanions { get; init; }
        public required string PeerGameplaySummary { get; init; }

        public bool HasRecognizedPackage =>
            HostUsesBtlSetAppear
            && FormationHasPreseededCompanions
            && SelectedBattleTokenKnown
            && SelectedBattleHasHostSummonPair
            && Rows.Count == 2
            && Rows.All(static row => row.HiddenForSelectedBattle && row.HostActionOffset >= 0);

        static readonly string WriterPolicyReadOnly = "read-only — no writer · no universal opcode semantics";
        static readonly string ProofLaneM213 = "GUADO_GUARDIAN_COMPLEX_SURFACING_2026-07-01 — m213 hidden companion activation / summon-handoff";
        static readonly string SummaryNoPackage = "No hidden companion activation package recognized in this battle.";

        const ushort SelfTarget = 0xFFF3;
        const ushort TargetMonster01 = 0x0015;
        const ushort TargetMonster02 = 0x0016;
        const ushort CurrentBattleFuncId = 0x7024;
        const ushort PerformCommandFuncId = 0x700B;
        const ushort ReadChrPropertyFuncId = 0x700F;
        const ushort WriteChrPropertyFuncId = 0x7018;
        const ushort BtlSetAppearFuncId = 0x7109;
        const ushort IsAliveField = 0x0004;
        const ushort StatusDarknessField = 0x002F;
        const ushort StatusBerserkField = 0x002A;
        const ushort CounterAttackField = 0x003D;
        const ushort BirthAnimationField = 0x0086;
        const short HiddenBirthAnimationValue = 0x0004;
        const ushort SummonCommandId = 0x408A;
        const ushort EyeDropsCommandId = 0x200C;
        const ushort ProtectCommandId = 0x303B;
        const ushort ShellCommandId = 0x303A;
        const ushort BerserkCommandId = 0x400F;
        const ushort AutoPotionCommandId = 0x4010;
        const int HostMonsterId = 213;

        static readonly Dictionary<string, uint> KnownM213BattleTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            ["maca03_20"] = 0x014D0014,
            ["maca03_21"] = 0x014D0015,
            ["maca03_22"] = 0x014D0016,
            ["mcyt00_20"] = 0x01540014,
            ["mcyt00_21"] = 0x01540015,
            ["mcyt00_22"] = 0x01540016,
        };

        static readonly Dictionary<uint, string> KnownM213BattleIds = KnownM213BattleTokens
            .ToDictionary(static pair => pair.Value, static pair => pair.Key);

        public static BattleCompanionActivation_File ReadFromBattleBin(
            string battleId,
            byte[] battleBinBytes,
            Func<int, byte[]?>? monsterBinResolver = null)
        {
            ArgumentNullException.ThrowIfNull(battleBinBytes);
            var notes = new List<string>();

            Battle_File battle;
            try { battle = Battle_File.Read(battleId, battleBinBytes); }
            catch (Exception ex)
            {
                notes.Add($"battle.bin did not decode: {ex.Message}");
                return Empty(battleId, notes);
            }

            Battle_Formation? formation = battle.Formation;
            if (formation == null)
            {
                notes.Add("formation chunk missing or did not decode.");
                return Empty(battleId, notes);
            }

            string formationSummary = BuildFormationSummary(formation);
            bool hasKnownToken = KnownM213BattleTokens.TryGetValue(battleId, out uint selectedToken);
            string battleTokenLabel = hasKnownToken ? $"{battleId} [0x{selectedToken:X8}]" : string.Format(Strings.U_Bb_BattleTokenUnmapped, battleId);
            bool hasPreseededCompanions =
                formation.Slots.Count >= 3
                && !formation.Slots[1].IsEmpty
                && !formation.Slots[2].IsEmpty;

            if (!hasKnownToken)
                notes.Add("CurrentBattle token known here only for the 6 proven battles of lane m213.");

            if (!hasPreseededCompanions)
            {
                notes.Add("formation does not expose two preseeded companions in slot1/slot2.");
                return BuildPartial(
                    battleId,
                    battleTokenLabel,
                    formationSummary,
                    "",
                    "The formation of this battle does not support the proven m213 package (the two preseeded companions in slot1/slot2 are missing).",
                    notes,
                    hostUsesBtlSetAppear: false,
                    formationHasPreseededCompanions: false,
                    selectedBattleTokenKnown: hasKnownToken,
                    spawnFromZeroNotSupported: false,
                    rows: Array.Empty<BattleCompanionActivationRow>());
            }

            Battle_FormationSlot hostSlot = formation.Slots[0];
            if (hostSlot.IsEmpty || hostSlot.DictionaryId != HostMonsterId)
            {
                if (hasKnownToken)
                {
                    notes.Add("battleId/token still belongs to the proven footprint of host m213, but the current battle.bin on disk does not have slot0=m213; treat as drift/collision sentinel for the battle editor lane.");
                    return BuildPartial(
                        battleId,
                        battleTokenLabel,
                        formationSummary,
                        "",
                        $"O token {battleTokenLabel} continua pertencendo ao footprint provado do host m213, mas o battle.bin atual em disco nao e mais uma host battle m213 (slot0!=m213). Isto parece drift/collision de corpus, nao uma 6a battle host valida atual.",
                        notes,
                        hostUsesBtlSetAppear: false,
                        formationHasPreseededCompanions: true,
                        selectedBattleTokenKnown: true,
                        spawnFromZeroNotSupported: false,
                        rows: Array.Empty<BattleCompanionActivationRow>());
                }

                notes.Add("slot0 is not m213; surface remains narrow to the proven host-slot0 package.");
                return BuildPartial(
                    battleId,
                    battleTokenLabel,
                    formationSummary,
                    "",
                    SummaryNoPackage,
                    notes,
                    hostUsesBtlSetAppear: false,
                    formationHasPreseededCompanions: true,
                    selectedBattleTokenKnown: hasKnownToken,
                    spawnFromZeroNotSupported: false,
                    rows: Array.Empty<BattleCompanionActivationRow>());
            }

            if (monsterBinResolver == null)
            {
                notes.Add("monster bin resolver absent; could not close the host/companion proof.");
                return BuildPartial(
                    battleId,
                    battleTokenLabel,
                    formationSummary,
                    "",
                    "Candidate battle for lane m213, but failed to resolve the monster bins to prove Hidden companion activation.",
                    notes,
                    hostUsesBtlSetAppear: false,
                    formationHasPreseededCompanions: true,
                    selectedBattleTokenKnown: hasKnownToken,
                    spawnFromZeroNotSupported: false,
                    rows: Array.Empty<BattleCompanionActivationRow>());
            }

            if (!TryReadAiScript(monsterBinResolver, HostMonsterId, out AiScriptFile? hostScript, notes))
            {
                return BuildPartial(
                    battleId,
                    battleTokenLabel,
                    formationSummary,
                    "",
                    "Candidate battle for lane m213, but the host AI could not be read.",
                    notes,
                    hostUsesBtlSetAppear: false,
                    formationHasPreseededCompanions: true,
                    selectedBattleTokenKnown: hasKnownToken,
                    spawnFromZeroNotSupported: false,
                    rows: Array.Empty<BattleCompanionActivationRow>());
            }

            bool hostUsesBtlSetAppear = DetectBtlSetAppearSelf(hostScript!);
            Dictionary<uint, List<HostActivationHit>> hostHitsByToken = ExtractHostActivationHits(hostScript!);
            HostPeerGameplayEvidence gameplayEvidence = ExtractHostPeerGameplayEvidence(hostScript!);
            string activationCoverage = BuildActivationCoverageSummary(hostHitsByToken, gameplayEvidence);

            if (!hostUsesBtlSetAppear)
                notes.Add("the host AI did not match the pattern btlSetAppear(Self, 0, 0).");
            if (hostHitsByToken.Count == 0)
                notes.Add("no pair 0x408A -> Monster#01/#02 was recognized in the host AI.");

            List<HostActivationHit> selectedHits = hasKnownToken && hostHitsByToken.TryGetValue(selectedToken, out List<HostActivationHit>? hits)
                ? hits
                : new List<HostActivationHit>();

            var rows = new List<BattleCompanionActivationRow>();
            rows.AddRange(BuildCompanionRows(
                battleId,
                formation,
                hasKnownToken ? selectedToken : null,
                TargetMonster01,
                formation.Slots[1],
                selectedHits,
                monsterBinResolver,
                notes));
            rows.AddRange(BuildCompanionRows(
                battleId,
                formation,
                hasKnownToken ? selectedToken : null,
                TargetMonster02,
                formation.Slots[2],
                selectedHits,
                monsterBinResolver,
                notes));

            bool hasSelectedBattleSummonPair =
                hasKnownToken
                && selectedHits.Any(static hit => hit.TargetOperand == TargetMonster01)
                && selectedHits.Any(static hit => hit.TargetOperand == TargetMonster02)
                && rows.Count == 2
                && rows.All(static row => row.HostActionOffset >= 0);

            if (hasKnownToken && !hasSelectedBattleSummonPair)
                notes.Add("the selected CurrentBattle did not close the host pair 0x408A -> Monster#01/#02.");

            bool spawnFromZeroNotSupported =
                hasSelectedBattleSummonPair
                && !formation.Slots[1].IsEmpty
                && !formation.Slots[2].IsEmpty;

            string summary = BuildSummary(
                battleId,
                hostUsesBtlSetAppear,
                hasKnownToken,
                hasSelectedBattleSummonPair,
                spawnFromZeroNotSupported,
                gameplayEvidence.HasGameplayFollowUp,
                rows);

            return new BattleCompanionActivation_File
            {
                BattleId = battleId,
                BattleTokenLabel = battleTokenLabel,
                FormationSummary = formationSummary,
                ActivationCoverageSummary = activationCoverage,
                HumanSummary = summary,
                Rows = rows,
                Notes = notes,
                ProofLane = ProofLaneM213,
                WriterPolicy = WriterPolicyReadOnly,
                HostUsesBtlSetAppear = hostUsesBtlSetAppear,
                FormationHasPreseededCompanions = hasPreseededCompanions,
                SelectedBattleTokenKnown = hasKnownToken,
                SelectedBattleHasHostSummonPair = hasSelectedBattleSummonPair,
                SpawnFromZeroNotSupported = spawnFromZeroNotSupported,
                HostTreatsPeerSlotsAsGameplayCompanions = gameplayEvidence.HasGameplayFollowUp,
                PeerGameplaySummary = gameplayEvidence.Summary,
            };
        }

        static BattleCompanionActivation_File Empty(string battleId, IReadOnlyList<string> notes) => new()
        {
            BattleId = battleId,
            BattleTokenLabel = string.Format(Strings.U_Bb_BattleTokenUnmapped, battleId),
            FormationSummary = "",
            ActivationCoverageSummary = "",
            HumanSummary = SummaryNoPackage,
            Rows = Array.Empty<BattleCompanionActivationRow>(),
            Notes = notes,
            ProofLane = ProofLaneM213,
            WriterPolicy = WriterPolicyReadOnly,
            HostUsesBtlSetAppear = false,
            FormationHasPreseededCompanions = false,
            SelectedBattleTokenKnown = false,
            SelectedBattleHasHostSummonPair = false,
            SpawnFromZeroNotSupported = false,
            HostTreatsPeerSlotsAsGameplayCompanions = false,
            PeerGameplaySummary = "Host follow-up: —",
        };

        static BattleCompanionActivation_File BuildPartial(
            string battleId,
            string battleTokenLabel,
            string formationSummary,
            string activationCoverage,
            string summary,
            IReadOnlyList<string> notes,
            bool hostUsesBtlSetAppear,
            bool formationHasPreseededCompanions,
            bool selectedBattleTokenKnown,
            bool spawnFromZeroNotSupported,
            IReadOnlyList<BattleCompanionActivationRow> rows) => new()
        {
            BattleId = battleId,
            BattleTokenLabel = battleTokenLabel,
            FormationSummary = formationSummary,
            ActivationCoverageSummary = activationCoverage,
            HumanSummary = summary,
            Rows = rows,
            Notes = notes,
            ProofLane = ProofLaneM213,
            WriterPolicy = WriterPolicyReadOnly,
            HostUsesBtlSetAppear = hostUsesBtlSetAppear,
            FormationHasPreseededCompanions = formationHasPreseededCompanions,
            SelectedBattleTokenKnown = selectedBattleTokenKnown,
            SelectedBattleHasHostSummonPair = false,
            SpawnFromZeroNotSupported = spawnFromZeroNotSupported,
            HostTreatsPeerSlotsAsGameplayCompanions = false,
            PeerGameplaySummary = "Host follow-up: —",
        };

        static IEnumerable<BattleCompanionActivationRow> BuildCompanionRows(
            string battleId,
            Battle_Formation formation,
            uint? selectedToken,
            ushort targetOperand,
            Battle_FormationSlot slot,
            IReadOnlyList<HostActivationHit> selectedHits,
            Func<int, byte[]?> monsterBinResolver,
            List<string> notes)
        {
            if (slot.IsEmpty)
                yield break;

            if (!TryReadAiScript(monsterBinResolver, slot.DictionaryId, out AiScriptFile? companionScript, notes))
                yield break;

            HiddenBirthEvidence hidden = ExtractHiddenBirthEvidence(companionScript!);
            bool hiddenForSelectedBattle = selectedToken.HasValue && hidden.Tokens.Contains(selectedToken.Value);
            HostActivationHit? hit = selectedHits.FirstOrDefault(candidate => candidate.TargetOperand == targetOperand);

            string hiddenLabel = hidden.Tokens.Count == 0
                ? "(nenhuma battle Hidden reconhecida)"
                : string.Join(", ", hidden.Tokens
                    .OrderBy(static token => token)
                    .Select(DescribeToken));

            string evidence = BuildEvidenceLabel(hidden, hit);

            yield return new BattleCompanionActivationRow
            {
                SlotLabel = $"{ClassifyMonsterTarget(targetOperand)} / slot{slot.SlotIndex}",
                TargetOperand = targetOperand,
                FormationSlotIndex = slot.SlotIndex,
                MonsterId = slot.DictionaryId,
                MonsterLabel = slot.MonsterLabel,
                HostActionLabel = "0x408A [Summon]",
                HostActionOffset = hit?.SourceOffset ?? -1,
                HiddenForSelectedBattle = hiddenForSelectedBattle,
                HiddenBattleIdsLabel = hiddenLabel,
                EvidenceLabel = evidence,
            };
        }

        static string BuildSummary(
            string battleId,
            bool hostUsesBtlSetAppear,
            bool selectedTokenKnown,
            bool hasSelectedBattleSummonPair,
            bool spawnFromZeroNotSupported,
            bool hostTreatsPeersAsGameplayCompanions,
            IReadOnlyList<BattleCompanionActivationRow> rows)
        {
            if (!selectedTokenKnown)
                return "Lane m213 detected, but this battle does not yet have a CurrentBattle token mapped inside this narrow reader.";

            if (!hostUsesBtlSetAppear)
                return "Host m213 found, but the btlSetAppear(Self,0,0) packet was not recognized with confidence.";

            if (!hasSelectedBattleSummonPair || !spawnFromZeroNotSupported)
                return $"m213 is in the formation, but {battleId} did not match the full proven hidden companion activation package.";

            bool allHidden = rows.Count == 2 && rows.All(static row => row.HiddenForSelectedBattle);
            if (!allHidden)
                return "The companions already exist in the formation and the host uses 0x408A, but BirthAnimation=Hidden did not resolve for both peers in this battle.";

            if (hostTreatsPeersAsGameplayCompanions)
                return "Monster#01/#02 already exist in the formation and spawn hidden; the host uses 0x408A [Summon] as an encounter-driven beat and the same AI later treats slot21/22 as gameplay peers. Spawn from scratch is ruled out; the exact list of native 0x408A flags remains unproven.";

            return "Monster#01/#02 already exist in the formation and spawn hidden; the host m213 uses 0x408A [Summon] to reveal/activate them as companions in this battle. Spawn from scratch is not supported by the evidence in this lane.";
        }

        static bool TryReadAiScript(
            Func<int, byte[]?> monsterBinResolver,
            int monsterId,
            out AiScriptFile? script,
            List<string> notes)
        {
            script = null;
            try
            {
                byte[]? monsterBin = monsterBinResolver(monsterId);
                if (monsterBin == null || monsterBin.Length == 0)
                {
                    notes.Add($"monster bin missing for m{monsterId:D3}.");
                    return false;
                }

                byte[]? aiBytes = AiScript_File.SliceAiFileFromMonster(monsterBin);
                if (aiBytes == null || aiBytes.Length == 0)
                {
                    notes.Add($"AI partition missing for m{monsterId:D3}.");
                    return false;
                }

                script = AiScript_File.Read(aiBytes);
                return true;
            }
            catch (Exception ex)
            {
                notes.Add($"failed reading AI of m{monsterId:D3}: {ex.Message}");
                return false;
            }
        }

        static bool DetectBtlSetAppearSelf(AiScriptFile script)
        {
            IReadOnlyList<AiInstruction> ins = script.Instructions;
            for (int i = 3; i < ins.Count; i++)
            {
                if (!IsCallPopa(ins[i], BtlSetAppearFuncId)) continue;
                if (!IsPushIi(ins[i - 3], out short actor) || actor != unchecked((short)SelfTarget)) continue;
                if (!IsPushIi(ins[i - 2], out short p2) || p2 != 0) continue;
                if (!IsPushIi(ins[i - 1], out short p3) || p3 != 0) continue;
                return true;
            }
            return false;
        }

        static Dictionary<uint, List<HostActivationHit>> ExtractHostActivationHits(AiScriptFile script)
        {
            var hitsByToken = new Dictionary<uint, List<HostActivationHit>>();
            IReadOnlyList<AiInstruction> ins = script.Instructions;

            for (int i = 0; i + 3 < ins.Count; i++)
            {
                if (!IsBattleTokenCheck(script, ins, i, out uint token))
                    continue;

                var hits = new List<HostActivationHit>();
                int windowEnd = FindNextBattleTokenCheck(ins, i + 4);
                for (int j = i + 4; j < windowEnd; j++)
                {
                    if (!IsCallPopa(ins[j], PerformCommandFuncId)) continue;
                    if (!TryReadPerformCommandLiteral(ins, j, out ushort targetOperand, out ushort commandId)) continue;
                    if (commandId != SummonCommandId) continue;
                    if (targetOperand != TargetMonster01 && targetOperand != TargetMonster02) continue;

                    hits.Add(new HostActivationHit
                    {
                        TargetOperand = targetOperand,
                        SourceOffset = ins[j].Offset,
                    });
                }

                if (hits.Count > 0)
                    hitsByToken[token] = hits;
            }

            return hitsByToken;
        }

        static HiddenBirthEvidence ExtractHiddenBirthEvidence(AiScriptFile script)
        {
            var tokens = new HashSet<uint>();
            var sourceOffsets = new List<int>();
            IReadOnlyList<AiInstruction> ins = script.Instructions;

            for (int i = 0; i + 3 < ins.Count; i++)
            {
                if (!IsBattleTokenCheck(script, ins, i, out uint token))
                    continue;

                int windowEnd = FindNextBattleTokenCheck(ins, i + 4);
                for (int j = i + 4; j < windowEnd; j++)
                {
                    if (!IsCallPopa(ins[j], WriteChrPropertyFuncId)) continue;
                    if (!TryReadWriteChrPropertyDirect(ins, j, out ushort actorRef, out ushort fieldId, out short value)) continue;
                    if (actorRef != SelfTarget) continue;
                    if (fieldId != BirthAnimationField) continue;
                    if (value != HiddenBirthAnimationValue) continue;

                    tokens.Add(token);
                    sourceOffsets.Add(ins[j].Offset);
                }
            }

            return new HiddenBirthEvidence
            {
                Tokens = tokens,
                SourceOffsets = sourceOffsets,
            };
        }

        static HostPeerGameplayEvidence ExtractHostPeerGameplayEvidence(AiScriptFile script)
        {
            var readProperties = new HashSet<ushort>();
            var commandIds = new HashSet<ushort>();
            IReadOnlyList<AiInstruction> instructions = script.Instructions;

            for (int i = 0; i < instructions.Count; i++)
            {
                if (IsCall(instructions[i], ReadChrPropertyFuncId)
                    && TryReadReadChrPropertyLiteral(instructions, i, out ushort actorRef, out ushort fieldId)
                    && (actorRef == TargetMonster01 || actorRef == TargetMonster02))
                {
                    readProperties.Add(fieldId);
                }

                if (IsCallPopa(instructions[i], PerformCommandFuncId)
                    && TryReadPerformCommandLiteral(instructions, i, out ushort targetOperand, out ushort commandId)
                    && (targetOperand == TargetMonster01 || targetOperand == TargetMonster02)
                    && commandId != SummonCommandId)
                {
                    commandIds.Add(commandId);
                }
            }

            bool hasGameplayFollowUp = readProperties.Count > 0 || commandIds.Count > 0;
            return new HostPeerGameplayEvidence
            {
                ReadPropertyIds = readProperties,
                CommandIds = commandIds,
                HasGameplayFollowUp = hasGameplayFollowUp,
                Summary = BuildHostPeerGameplaySummary(readProperties, commandIds),
            };
        }

        static bool IsBattleTokenCheck(AiScriptFile script, IReadOnlyList<AiInstruction> ins, int index, out uint token)
        {
            token = 0;
            if (index + 3 >= ins.Count) return false;
            if (!IsCall(ins[index], CurrentBattleFuncId)) return false;
            if (ins[index + 1].Opcode != 0xAD) return false; // PUSHI -> int const pool
            if (ins[index + 2].Opcode != 0x06) return false; // EQ
            if (ins[index + 3].Opcode != 0xD7) return false; // POPXNCJMP
            if (!TryResolveIntConst(script, ins[index + 1].Operand, out int value)) return false;
            token = unchecked((uint)value);
            return true;
        }

        static int FindNextBattleTokenCheck(IReadOnlyList<AiInstruction> ins, int start)
        {
            for (int i = start; i + 3 < ins.Count; i++)
            {
                if (ins[i].Opcode == 0xB5
                    && ins[i].Operand == CurrentBattleFuncId
                    && ins[i + 1].Opcode == 0xAD
                    && ins[i + 2].Opcode == 0x06
                    && ins[i + 3].Opcode == 0xD7)
                {
                    return i;
                }
            }
            return ins.Count;
        }

        static bool TryResolveIntConst(AiScriptFile script, ushort index, out int value)
        {
            value = 0;
            if (script.IntPoolOffset < 0) return false;
            int off = script.IntPoolOffset + index * 4;
            if (off < 0 || off + 4 > script.OriginalAiFileBytes.Length) return false;
            value = BitConverter.ToInt32(script.OriginalAiFileBytes, off);
            return true;
        }

        static bool TryReadPerformCommandLiteral(
            IReadOnlyList<AiInstruction> instructions,
            int callIndex,
            out ushort targetOperand,
            out ushort commandId)
        {
            targetOperand = 0;
            commandId = 0;
            if (callIndex < 2) return false;
            if (!IsPushIi(instructions[callIndex - 2], out short target)) return false;
            if (!IsPushIi(instructions[callIndex - 1], out short command)) return false;
            targetOperand = unchecked((ushort)target);
            commandId = unchecked((ushort)command);
            return true;
        }

        static bool TryReadReadChrPropertyLiteral(
            IReadOnlyList<AiInstruction> instructions,
            int callIndex,
            out ushort actorRef,
            out ushort fieldId)
        {
            actorRef = 0;
            fieldId = 0;
            if (callIndex < 2) return false;
            if (!IsPushIi(instructions[callIndex - 2], out short actor)) return false;
            if (!IsPushIi(instructions[callIndex - 1], out short field)) return false;
            actorRef = unchecked((ushort)actor);
            fieldId = unchecked((ushort)field);
            return true;
        }

        static bool TryReadWriteChrPropertyDirect(
            IReadOnlyList<AiInstruction> instructions,
            int callIndex,
            out ushort actorRef,
            out ushort fieldId,
            out short value)
        {
            actorRef = 0;
            fieldId = 0;
            value = 0;
            if (callIndex < 3) return false;
            if (!IsPushIi(instructions[callIndex - 3], out short actor)) return false;
            if (!IsPushIi(instructions[callIndex - 2], out short field)) return false;
            if (!IsPushIi(instructions[callIndex - 1], out short val)) return false;
            actorRef = unchecked((ushort)actor);
            fieldId = unchecked((ushort)field);
            value = val;
            return true;
        }

        static bool IsCall(AiInstruction instruction, ushort funcId) =>
            instruction.Opcode == 0xB5 && instruction.Operand == funcId;

        static bool IsCallPopa(AiInstruction instruction, ushort funcId) =>
            instruction.Opcode == 0xD8 && instruction.Operand == funcId;

        static bool IsPushIi(AiInstruction instruction, out short value)
        {
            value = 0;
            if (instruction.Opcode != 0xAE) return false;
            value = unchecked((short)instruction.Operand);
            return true;
        }

        static string BuildFormationSummary(Battle_Formation formation)
        {
            List<string> parts = formation.Slots
                .Where(static slot => !slot.IsEmpty)
                .Take(3)
                .Select(static slot => $"slot{slot.SlotIndex}=m{slot.DictionaryId:D3}")
                .ToList();
            return parts.Count == 0 ? "" : "Formation: " + string.Join(", ", parts);
        }

        static string BuildEvidenceLabel(HiddenBirthEvidence hidden, HostActivationHit? hit)
        {
            string host = hit == null ? "host 0x408A: —" : $"host 0x408A @ 0x{hit.SourceOffset:X}";
            string companion = hidden.SourceOffsets.Count == 0
                ? "companion Hidden: —"
                : "companion Hidden @ " + string.Join(", ", hidden.SourceOffsets.Select(static off => $"0x{off:X}"));
            return host + " · " + companion;
        }

        static string BuildActivationCoverageSummary(
            IReadOnlyDictionary<uint, List<HostActivationHit>> hostHitsByToken,
            HostPeerGameplayEvidence gameplayEvidence)
        {
            string summonCoverage = hostHitsByToken.Count == 0
                ? "Host m213: no 0x408A pair recognized."
                : "Host m213 ativa 0x408A em: " + string.Join(", ", hostHitsByToken.Keys
                    .OrderBy(static token => token)
                    .Select(DescribeToken));

            return gameplayEvidence.HasGameplayFollowUp
                ? summonCoverage + " · " + gameplayEvidence.Summary
                : summonCoverage;
        }

        static string BuildHostPeerGameplaySummary(
            IEnumerable<ushort> readProperties,
            IEnumerable<ushort> commandIds)
        {
            List<string> reads = readProperties
                .Select(FormatChrPropertyLabel)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static label => label, StringComparer.Ordinal)
                .ToList();
            List<string> commands = commandIds
                .Select(FormatCommandLabel)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static label => label, StringComparer.Ordinal)
                .ToList();

            if (reads.Count == 0 && commands.Count == 0)
                return "Host follow-up: —";

            var parts = new List<string>();
            if (reads.Count > 0)
                parts.Add("host reads " + JoinPreview(reads));
            if (commands.Count > 0)
                parts.Add("host usa " + JoinPreview(commands));
            return "Host follow-up: " + string.Join(" · ", parts) + " on Monster#01/#02";
        }

        static string JoinPreview(IReadOnlyList<string> values, int maxItems = 4)
        {
            if (values.Count <= maxItems)
                return string.Join("/", values);
            return string.Join("/", values.Take(maxItems)) + "/...";
        }

        static string DescribeToken(uint token) =>
            KnownM213BattleIds.TryGetValue(token, out string? battleId)
                ? $"{battleId} [0x{token:X8}]"
                : $"0x{token:X8}";

        static string FormatChrPropertyLabel(ushort fieldId) => fieldId switch
        {
            IsAliveField => "isAlive",
            StatusDarknessField => "StatusDarkness",
            StatusBerserkField => "StatusBerserk",
            CounterAttackField => "CounterAttack",
            _ => $"prop 0x{fieldId:X4}",
        };

        static string FormatCommandLabel(ushort commandId) => commandId switch
        {
            EyeDropsCommandId => "Eye Drops [0x200C]",
            ProtectCommandId => "Protect [0x303B]",
            ShellCommandId => "Shell [0x303A]",
            BerserkCommandId => "Berserk [0x400F]",
            AutoPotionCommandId => "Auto-Potion [0x4010]",
            _ => $"0x{commandId:X4}",
        };

        static string ClassifyMonsterTarget(ushort operand) => operand switch
        {
            TargetMonster01 => "Monster#01 [0x0015]",
            TargetMonster02 => "Monster#02 [0x0016]",
            _ => $"Monster? [0x{operand:X4}]",
        };

        internal static string BuildMonsterLabel(int monsterId)
        {
            if (Monster_Dictionary.Instance.TryGetValue((short)monsterId, out string? name))
                return $"m{monsterId:D3} - {name}";
            return $"m{monsterId:D3}";
        }

        sealed class HiddenBirthEvidence
        {
            public required HashSet<uint> Tokens { get; init; }
            public required List<int> SourceOffsets { get; init; }
        }

        sealed class HostActivationHit
        {
            public required ushort TargetOperand { get; init; }
            public required int SourceOffset { get; init; }
        }

        sealed class HostPeerGameplayEvidence
        {
            public required HashSet<ushort> ReadPropertyIds { get; init; }
            public required HashSet<ushort> CommandIds { get; init; }
            public required bool HasGameplayFollowUp { get; init; }
            public required string Summary { get; init; }
        }
    }
}

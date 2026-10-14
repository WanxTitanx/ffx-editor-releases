using Avalonia.Threading;
using Binarysharp.MSharp.Assembly.CallingConvention;
using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Battle;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.FfxLib.Memory;
using FFXProjectEditor.Modules.MonEditor;
using FFXProjectEditor.Resources;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Xe.BinaryMapper;

namespace FFXProjectEditor.Modules.LiveBattleLab
{
    internal partial class LiveBattleLab_DataModel : ObservableObject
    {
        const int RvaMsBattleEncountExe = 0x380DE0;
        // CLEAN-battle recipe (proven 2026-06-03): read the player's CURRENT field/group live on the
        // main thread so a forced encounter lands in the REAL arena (no void/placeholder).
        //   field = FFX_Encounter_GetCurrentField()            -> __cdecl int()
        //   group = *(byte*)(FFX_Encounter_GetSceneState()+16) -> __cdecl void*(); group at +16
        const uint RvaEncounterGetCurrentField = 0x48D600;
        const uint RvaEncounterGetSceneState = 0x48C7A0;
        const uint EncounterSceneStateGroupOffset = 16;
        const int AddrEncounterListResource = 0x112A9C4;
        const int AddrEncounterListFieldBase = 0x112A9C8;
        const int AddrEncounterListEncounterBase = 0x112A9CC;
        const int AddrEncounterListFieldCount = 0x112A9D0;
        const int AddrEncounterListSceneToken = 0x112A9D2;
        const int AddrEncounterListSceneKind = 0x112A9D4;
        const int AddrEncounterListSceneSubmode = 0x112A9D5;
        const int AddrEncounterListBattleMode = 0x112A8E0;
        const int RvaEncounterLevel = 0xC421C8;
        const int EncounterLevelNormal = 1;
        const int EncounterLevelTenfold = 2;
        const float ForceEncounterWalkDelta = 512f;
        static readonly int ForceEncounterWalkDeltaBits = BitConverter.SingleToInt32Bits(ForceEncounterWalkDelta);

        readonly DispatcherTimer refreshTimer;
        readonly Dictionary<string, LiveBattleActorSnapshot> lastObservedActors = new(StringComparer.Ordinal);
        Dictionary<ushort, EncounterTable_Entry>? encounterTableById;
        string? encounterTableProjectPath;
        int? lastLiveFieldIdx;
        int? lastLiveGroupIdx;
        int? lastLiveFormationIdx;
        LiveBattleBtlObservationSnapshot? lastObservedBtlSnapshot;
        LiveBattleRuntimePhaseSnapshot? lastObservedPhaseSnapshot;
        uint? lastObservedLastCommand;
        bool lastObservedInBattle;
        string lastObservedBattleLabel = "-";
        int aiObservationSequence;
        bool currentBattleFromForcedRoll;
        int naturalCaptureTickSequence;
        readonly List<LiveBattleNaturalCaptureSample> naturalCaptureSamples = new();

        public ObservableCollection<LiveBattleEnemyRow> RuntimeEnemyRows { get; } = new();
        public ObservableCollection<LiveBattleAiProbeRow> RuntimeAiProbeRows { get; } = new();
        public ObservableCollection<LiveBattleAiEventRow> RuntimeAiEventRows { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanReloadRuntimeFiles))]
        [NotifyPropertyChangedFor(nameof(CanApplyDebugFlags))]
        [NotifyPropertyChangedFor(nameof(ReloadAvailabilitySummary))]
        [NotifyPropertyChangedFor(nameof(DebugAvailabilitySummary))]
        [NotifyPropertyChangedFor(nameof(ForceBattleAvailabilitySummary))]
        private bool gameOpen;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanReloadRuntimeFiles))]
        [NotifyPropertyChangedFor(nameof(CanApplyDebugFlags))]
        [NotifyPropertyChangedFor(nameof(CanForceBattle))]
        [NotifyPropertyChangedFor(nameof(CanForceCurrentBattle))]
        [NotifyPropertyChangedFor(nameof(CanRepeatEncounter))]
        [NotifyPropertyChangedFor(nameof(ReloadAvailabilitySummary))]
        [NotifyPropertyChangedFor(nameof(DebugAvailabilitySummary))]
        [NotifyPropertyChangedFor(nameof(ForceBattleAvailabilitySummary))]
        private bool hookAvailable;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanReloadRuntimeFiles))]
        [NotifyPropertyChangedFor(nameof(ReloadAvailabilitySummary))]
        private bool projectLoaded;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AutoRefreshDisabled))]
        private bool autoRefreshEnabled = true;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanForceBattle))]
        [NotifyPropertyChangedFor(nameof(CanForceCurrentBattle))]
        [NotifyPropertyChangedFor(nameof(CanRepeatEncounter))]
        [NotifyPropertyChangedFor(nameof(ForceBattleAvailabilitySummary))]
        private bool inBattle;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanRepeatEncounter))]
        [NotifyPropertyChangedFor(nameof(CanUseLiveRouting))]
        [NotifyPropertyChangedFor(nameof(ForceBattleAvailabilitySummary))]
        private bool hasCapturedRouting;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanForceBattle))]
        [NotifyPropertyChangedFor(nameof(ForceBattleAvailabilitySummary))]
        private string forceFieldInput = "0";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanForceBattle))]
        [NotifyPropertyChangedFor(nameof(ForceBattleAvailabilitySummary))]
        private string forceGroupInput = "0";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanForceBattle))]
        [NotifyPropertyChangedFor(nameof(ForceBattleAvailabilitySummary))]
        private string forceFormationInput = "0";

        [ObservableProperty] private string hookSummary = Strings.F2_open_ffx_and_load_the_workspace_to_use_t_0edcb965;
        [ObservableProperty] private string battleSummary = "-";
        [ObservableProperty] private string routingSummary = "-";
        [ObservableProperty] private string runtimePhaseSummary = "Runtime phase offline. The hook must be alive to distinguish cold context, prelude, trigger, and battle active.";
        [ObservableProperty] private string passiveRouteSummary = "Passive route truth offline. The hook must be alive to cross-reference SaveData with the encounter table.";
        [ObservableProperty] private string encounterListProbeSummary = "Encounter-list prelude probe offline. The hook must be alive to observe resource_ptr and list bases.";
        [ObservableProperty] private string partySummary = "-";
        [ObservableProperty] private string enemySummary = Strings.F2_no_runtime_snapshot_captured_yet_d2b5252e;
        [ObservableProperty] private string reloadSummary = Strings.U_Lbl_ReloadsProven;
        [ObservableProperty] private string experimentalSummary = "First real bench of Force Battle / Repeat Route (Unsafe): Jarvis now prioritizes the natural encounter routine per field/group to avoid crashing the engine. The exact launch per formation remains parked until the call becomes reliable.";
        [ObservableProperty] private string forceBattleStatus = "No Force Battle attempt sent yet.";
        [ObservableProperty] private string aiProbeSummary = "AI Probe offline. Enter a battle with a live hook to cross-reference the runtime enemy with the static corpus.";
        [ObservableProperty] private string turnOwnerProbeSummary = "Current turn owner has not been proven yet. When the runtime battle enters, the probe will register only candidates observed by action-state.";
        [ObservableProperty] private string aiEventSummary = "Runtime turn log offline. Enter a battle with a live hook to record last command and action-state transitions.";
        [ObservableProperty] private string cohortInput = "";
        [ObservableProperty] private string naturalCaptureSummary = "No natural capture yet. Enter a natural battle, mark the cohort (e.g., nagi00_00 / m020 / Firaga) and capture the tick. The Pt47 contract requires 3 accept captures of the same cohort; bench-replay does NOT count as natural.";
        [ObservableProperty] private string battleName = "-";
        [ObservableProperty] private string encounterIndexLabel = "-";
        [ObservableProperty] private string triggerLabel = "-";
        [ObservableProperty] private string battleStateLabel = "-";
        [ObservableProperty] private string battlefieldLabel = "-";
        [ObservableProperty] private string fieldLabel = "-";
        [ObservableProperty] private string groupLabel = "-";
        [ObservableProperty] private string formationLabel = "-";
        [ObservableProperty] private string routingCursorSummary = "-";
        [ObservableProperty] private string frontlineLabel = "-";
        [ObservableProperty] private string backlineLabel = "-";
        [ObservableProperty] private string encounterTypeLabel = "-";
        [ObservableProperty] private string screenTransitionLabel = "-";
        [ObservableProperty] private string lastCommandLabel = "-";
        [ObservableProperty] private string ambushStateLabel = "-";
        [ObservableProperty] private string battleTypeLabel = "-";
        [ObservableProperty] private string battleEndTypeLabel = "-";
        [ObservableProperty] private string capturedRoutingLabel = "No live routing captured yet.";

        [ObservableProperty] private bool debugInvincibleMon;
        [ObservableProperty] private bool debugInvinciblePly;
        [ObservableProperty] private bool debugMonControl;
        [ObservableProperty] private bool debugFreeCamera;
        [ObservableProperty] private bool debugNoMagicEffects;
        [ObservableProperty] private bool debugNoMpCost;
        [ObservableProperty] private bool debugNoVariance;
        [ObservableProperty] private bool debugAlwaysHit;
        [ObservableProperty] private bool debugAlwaysOverdrive;
        [ObservableProperty] private bool debugAlwaysRareSteal;
        [ObservableProperty] private bool debugAlways9999Damage;
        [ObservableProperty] private bool debugAlways99999Damage;

        public bool AutoRefreshDisabled => !AutoRefreshEnabled;
        public bool CanReloadRuntimeFiles => HookAvailable && ProjectLoaded;
        public bool CanApplyDebugFlags => HookAvailable;
        public bool CanForceBattle => HookAvailable && !InBattle && TryParseForceInputs(out _, out _, out _, out _);
        // One-click CLEAN force: needs the main-thread DINPUT8 probe live (it reads field/group itself).
        public bool CanForceCurrentBattle => HookAvailable && !InBattle && FfxProbe_Service.Instance.IsHooked;
        public bool CanRepeatEncounter => HookAvailable && !InBattle && HasCapturedRouting;
        public bool CanUseLiveRouting => HasCapturedRouting;

        public string ReloadAvailabilitySummary => CanReloadRuntimeFiles
            ? "Workspace, game, and memory hook are alive. These reloads are already writing to the proven runtime buffer."
            : !ProjectLoaded
                ? "Load the workspace first. Without that, Jarvis doesn't know which file to push to the runtime."
                : !GameOpen
                    ? "Workspace ok, but FFX is closed. Open the game to arm in-game reload."
                    : "FFX open, but the memory hook hasn't responded properly. The shell saw the process; this lab only arms when the RAM is truly accessible.";

        public string DebugAvailabilitySummary => CanApplyDebugFlags
            ? "BTL debug block alive. Can read and reapply runtime flags now."
            : !GameOpen
                ? Strings.U_Llb_NoBtlDebugBlock
                : "The game is open, but the memory hook isn't ready yet. That's why the flags appear dead.";

        public string ForceBattleAvailabilitySummary
        {
            get
            {
                if (!GameOpen)
                {
                    return "Force Battle is not armed yet. Open FFX first.";
                }

                if (!HookAvailable)
                {
                    return "The game process was detected, but the memory hook hasn't really responded. Without live RAM, Jarvis doesn't call the engine.";
                }

                if (InBattle)
                {
                    return "Exit the current battle before triggering a new route force. This first version doesn't stack encounters on top of active combat.";
                }

                if (!TryParseForceInputs(out ushort field, out byte group, out byte formation, out string error))
                {
                    return string.Format(Strings.U_Llb_RouteFieldsNotValid, error);
                }

                string armed = string.Format(Strings.U_Llb_ArmedExperimental, field, group, formation, ForceEncounterWalkDeltaBits);
                if (HasCapturedRouting)
                {
                    return string.Format(Strings.U_Llb_RepeatRouteReady, armed);
                }

                return string.Format(Strings.U_Llb_NeedLiveEncounter, armed);
            }
        }

        public LiveBattleLab_DataModel()
        {
            refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(750)
            };
            refreshTimer.Tick += (_, _) => RefreshRuntime();
            SeedBattleSandbox();
            RefreshRuntime();
        }

        partial void OnAutoRefreshEnabledChanged(bool value)
        {
            if (value)
            {
                refreshTimer.Start();
            }
            else
            {
                refreshTimer.Stop();
            }
        }

        public void StartRuntimeTimer()
        {
            if (AutoRefreshEnabled)
            {
                refreshTimer.Start();
            }
        }

        public void StopRuntimeTimer()
        {
            refreshTimer.Stop();
        }

        public void RefreshRuntime()
        {
            ProjectLoaded = Project_Service.Instance.IsProjectLoaded;
            RefreshSandboxProjectDataIfNeeded();
            GameOpen = Process_Service.Instance.IsAlive;
            HookAvailable = GameOpen && MemSharp_Service.Instance.IsAvailable();
            bool wasObservedInBattle = lastObservedInBattle;

            RuntimeEnemyRows.Clear();
            RuntimeAiProbeRows.Clear();
            if (!HookAvailable)
            {
                InBattle = false;
                HookSummary = !ProjectLoaded && !GameOpen
                    ? "Workspace offline and FFX closed. Point to the master and open the game to turn on the laboratory."
                    : ProjectLoaded && !GameOpen
                        ? "Workspace ok, but FFX is closed. Open the game to inspect battle, routing, and live positions."
                        : !ProjectLoaded && GameOpen
                            ? "FFX open, but the workspace wasn't loaded. The shell sees the process, but the laboratory still doesn't know which files to use."
                            : "FFX open, but the memory hook isn't ready yet. The shell detects the process; this lab only turns on when the RAM read really responds.";
                BattleSummary = "-";
                RoutingSummary = "-";
                RuntimePhaseSummary = Strings.U_Lbl_RuntimePhaseOffline;
                PassiveRouteSummary = "Passive route truth offline. Without a live memory hook, any route context outside of battle would be a guess.";
                EncounterListProbeSummary = Strings.U_Lbl_PreludeProbeOffline;
                PartySummary = "-";
                EnemySummary = Strings.U_Lbl_NoLiveHook;
                BattleName = "-";
                EncounterIndexLabel = "-";
                TriggerLabel = "-";
                BattleStateLabel = "-";
                BattlefieldLabel = "-";
                FieldLabel = "-";
                GroupLabel = "-";
                FormationLabel = "-";
                RoutingCursorSummary = "-";
                FrontlineLabel = "-";
                BacklineLabel = "-";
                EncounterTypeLabel = "-";
                ScreenTransitionLabel = "-";
                LastCommandLabel = "-";
                AmbushStateLabel = "-";
                BattleTypeLabel = "-";
                BattleEndTypeLabel = "-";
                AiProbeSummary = "AI Probe offline. The shell may see the process, but this probe only turns on with a truly live memory hook.";
                TurnOwnerProbeSummary = "Turn probe offline. Without a live memory hook, any owner would be a guess.";
                AiEventSummary = "Runtime turn log offline. The hook needs to be live to record actual battle changes.";
                ResetAiProbeObservations(clearLog: true);
                return;
            }

            MemoryBtl? battleState = TryReadBattleState();
            MemorySaveData? saveData = ReadSaveData();
            EncounterListProbeState encounterListState = ReadEncounterListProbeState();
            byte battleActive = MemSharp_Service.Instance.Read<byte>(MemoryMap.ADDR_BATTLE_ACTIVE);
            byte encounterIndex = MemSharp_Service.Instance.Read<byte>(MemoryMap.ADDR_BATTLE_ENCOUNTER_INDEX);
            byte trigger = MemSharp_Service.Instance.Read<byte>(MemoryMap.ADDR_BATTLE_TRIGGER);
            string liveBattleName = MemSharp_Service.Instance.ReadString(MemoryMap.ADDR_BATTLE_NAME, 13).Trim('\0', ' ');

            InBattle = battleActive == 1;
            BattleName = string.IsNullOrWhiteSpace(liveBattleName) ? "-" : liveBattleName;
            EncounterIndexLabel = $"{encounterIndex:D3} ({encounterIndex:X2}h)";
            TriggerLabel = $"{trigger:X2}h";
            PassiveRouteSummary = BuildPassiveRouteSummary(saveData, battleState, BattleName);
            EncounterListProbeSummary = BuildEncounterListProbeSummary(encounterListState);
            LiveBattleRuntimePhaseSnapshot runtimePhase = BuildRuntimePhaseSnapshot(battleState, saveData, encounterListState, battleActive, encounterIndex, trigger, BattleName);
            RuntimePhaseSummary = BuildRuntimePhaseSummary(runtimePhase);
            UpdateRuntimePhaseObservations(runtimePhase);

            if (battleState != null)
            {
                string fieldName = DecodeFieldName(battleState.field_name);
                BattleStateLabel = $"{battleState.battle_state:X2}h";
                BattlefieldLabel = $"{battleState.battlefield_id:X4}h";
                FieldLabel = $"{fieldName} · {battleState.field_idx:D3}";
                GroupLabel = $"{battleState.group_idx:D2}";
                FormationLabel = $"{battleState.formation_idx:D2}";
                RoutingCursorSummary = BuildRoutingCursorSummary(battleState);
                FrontlineLabel = FormatPartySlots(MemSharp_Service.Instance.Read<sbyte>(MemoryMap.ADDR_BATTLE_FORMATION_SLOTS, 3));
                BacklineLabel = FormatRuntimeByteArray(battleState.backline);
                EncounterTypeLabel = $"{battleState.encounter_type:X2}h";
                ScreenTransitionLabel = $"{battleState.screen_transition:X2}h";
                LastCommandLabel = FormatLastCommand(battleState.last_com);
                AmbushStateLabel = $"{battleState.ambush_state:X2}h";
                BattleTypeLabel = $"{battleState.battle_type:X2}h";
                BattleEndTypeLabel = $"{battleState.battle_end_type:X2}h";

                SyncDebugFlagsFrom(battleState.debug_settings);

                if (InBattle)
                {
                    CaptureLiveRouting(battleState.field_idx, battleState.group_idx, battleState.formation_idx, fieldName);
                }

                HookSummary = runtimePhase.PhaseKind switch
                {
                    LiveBattleRuntimePhaseKind.BattleActive =>
                        "Active battle detected. Use this snapshot to validate encounter routing, positions, AI Probe, and memory reload.",
                    LiveBattleRuntimePhaseKind.EncounterTrigger =>
                        "Hook online and encounter boot/transition observed. Jarvis will log battle_state, route swap, and BTL deltas without pretending this already proves exact replay.",
                    LiveBattleRuntimePhaseKind.NaturalPrelude =>
                        "Hook online, no active battle, but an observable prelude appeared. The base lists now help observe the encounter path in read-only mode.",
                    LiveBattleRuntimePhaseKind.ColdContext =>
                        "Hook online, but the BTL seems cold/parked. Outside of battle, the passive route truth is more reliable than selling system_0 as a live route.",
                    LiveBattleRuntimePhaseKind.PostBattle =>
                        "Hook online in post-battle. Some end-of-combat bits may still be settling before the next cold state.",
                    _ =>
                        "Hook online, but no active battle now. It is possible to observe runtime context, reload verified buffers, and repeat the last captured routing."
                };
                BattleSummary = $"InBattle {InBattle} · Name '{BattleName}' · Encounter {EncounterIndexLabel} · Trigger {TriggerLabel} · State {BattleStateLabel} · Phase {runtimePhase.PhaseLabel}";
                RoutingSummary = BuildRoutingSummary(fieldName, battleState, encounterListState);
                PartySummary = $"Front party {FrontlineLabel} · Backline raw {BacklineLabel} · LastCom {LastCommandLabel} · Ambush {AmbushStateLabel}";
            }
            else
            {
                HookSummary = "Hook online, but the BTL struct could not be fully decoded now.";
                BattleSummary = $"InBattle {InBattle} · Name '{BattleName}' · Encounter {EncounterIndexLabel} · Trigger {TriggerLabel} · Phase {runtimePhase.PhaseLabel}";
                RoutingSummary = "BTL struct unavailable. Use the passive route truth and encounter-list probe as read-only context until the struct returns fully.";
                PartySummary = "Party/runtime routing unavailable.";
                RoutingCursorSummary = "BTL route cursors unavailable.";
                EncounterTypeLabel = "-";
                ScreenTransitionLabel = "-";
                LastCommandLabel = "-";
                AmbushStateLabel = "-";
                BattleTypeLabel = "-";
                BattleEndTypeLabel = "-";
            }

            List<LiveBattleRuntimeActor> allyActors = ReadRuntimeActors(MemoryMap.POINTER_BATTLE_PLAYER_LIST, 18, true);
            List<LiveBattleRuntimeActor> enemyActors = ReadRuntimeActors(MemoryMap.POINTER_BATTLE_ENEMY_LIST, MemoryBattle_Util.MonsterListCount, false);

            LoadRuntimeEnemies(enemyActors);
            UpdateAiProbeObservations(wasObservedInBattle, battleState, allyActors, enemyActors);
        }

        public void UseLiveRouting()
        {
            if (!HasCapturedRouting || !lastLiveFieldIdx.HasValue || !lastLiveGroupIdx.HasValue || !lastLiveFormationIdx.HasValue)
            {
                ForceBattleStatus = "No live routing has been captured yet to copy.";
                return;
            }

            ForceFieldInput = lastLiveFieldIdx.Value.ToString();
            ForceGroupInput = lastLiveGroupIdx.Value.ToString();
            ForceFormationInput = lastLiveFormationIdx.Value.ToString();
            ForceBattleStatus = $"Routing vivo copiado para os inputs: field {lastLiveFieldIdx.Value}, group {lastLiveGroupIdx.Value}, formation {lastLiveFormationIdx.Value}.";
        }

        public void ForceBattle()
        {
            if (!HookAvailable)
            {
                ForceBattleStatus = "Force Battle aborted: memory hook is not yet live.";
                return;
            }

            if (InBattle)
            {
                ForceBattleStatus = "Force Battle aborted: an active battle already exists now.";
                return;
            }

            if (!TryParseForceInputs(out ushort field, out byte group, out byte formation, out string error))
            {
                ForceBattleStatus = $"Force Battle abortado: {error}";
                return;
            }

            ExecuteEncounterRoll(field, group, formation, "manual route");
        }

        // One-click CLEAN force: reads the player's CURRENT field/group live (main-thread, via the
        // DINPUT8 probe) and forces THAT encounter -- so it lands in the REAL arena instead of a void
        // placeholder. No manual numbers, no name-vs-id confusion.
        public void ForceCurrentBattle()
        {
            if (!HookAvailable)
            {
                ForceBattleStatus = "Force CURRENT aborted: memory hook is not yet live.";
                return;
            }

            if (InBattle)
            {
                ForceBattleStatus = "Force CURRENT aborted: an active battle already exists now.";
                return;
            }

            FfxProbe_Service probe = FfxProbe_Service.Instance;
            if (!probe.IsHooked)
            {
                ForceBattleStatus = "Force CURRENT requires the DINPUT8 probe (ffx-probe.dll) live on the main thread — it reads the field/group by itself. Now it is not hooked.";
                return;
            }

            try
            {
                // field = FFX_Encounter_GetCurrentField()  (__cdecl int())
                FfxProbe_Service.ProbeResult fieldRes = probe.Call(RvaEncounterGetCurrentField, FfxProbe_Service.AbiCdeclI);
                if (!fieldRes.Ok)
                {
                    ForceBattleStatus = string.Format(Strings.U_Lbl_ForceCurrentAbortedField, fieldRes.Status, fieldRes.Error);
                    return;
                }
                int field = fieldRes.Ret;

                // group = *(byte*)(FFX_Encounter_GetSceneState()+16)  (__cdecl void*())
                FfxProbe_Service.ProbeResult sceneRes = probe.Call(RvaEncounterGetSceneState, FfxProbe_Service.AbiCdeclI);
                if (!sceneRes.Ok)
                {
                    ForceBattleStatus = string.Format(Strings.U_Lbl_ForceCurrentAbortedScene, sceneRes.Status, sceneRes.Error);
                    return;
                }

                int group = 0;
                uint scenePtr = (uint)sceneRes.Ret;
                if (scenePtr != 0)
                {
                    uint groupRva = scenePtr - probe.ModuleBase + EncounterSceneStateGroupOffset;
                    FfxProbe_Service.ProbeResult groupRes = probe.Read(groupRva, 1);
                    if (groupRes.Ok && groupRes.Data is { Length: > 0 })
                    {
                        group = groupRes.Data[0];
                    }
                }

                if (field < 0 || field > ushort.MaxValue || group < 0 || group > byte.MaxValue)
                {
                    ForceBattleStatus = string.Format(Strings.U_Lbl_ForceCurrentOutOfRange, field, group);
                    return;
                }

                ForceFieldInput = field.ToString(CultureInfo.InvariantCulture);
                ForceGroupInput = group.ToString(CultureInfo.InvariantCulture);
                ForceFormationInput = "0";

                ExecuteEncounterRoll((ushort)field, (byte)group, 0, $"current runtime CLEAN (field {field}/group {group} lidos via probe)");
            }
            catch (Exception ex)
            {
                ForceBattleStatus = string.Format(Strings.U_Lbl_ForceCurrentReadFailed, ex.Message);
            }
        }

        public void RepeatEncounter()
        {
            if (!CanRepeatEncounter || !lastLiveFieldIdx.HasValue || !lastLiveGroupIdx.HasValue || !lastLiveFormationIdx.HasValue)
            {
                ForceBattleStatus = "Repeat Route (Unsafe) aborted: no live routing has been captured yet to repeat.";
                return;
            }

            ExecuteEncounterRoll((ushort)lastLiveFieldIdx.Value, (byte)lastLiveGroupIdx.Value, (byte)lastLiveFormationIdx.Value, "repeat last live route (unsafe)");
        }

        public void ApplyDebugFlags()
        {
            if (!CanApplyDebugFlags)
            {
                return;
            }

            MemoryBtl.BtlDebug debug = new()
            {
                debug_invincible_mon = DebugInvincibleMon,
                debug_invincible_ply = DebugInvinciblePly,
                debug_mon_control = DebugMonControl,
                debug_free_camera = DebugFreeCamera,
                debug_no_magic_effects = DebugNoMagicEffects,
                debug_no_mp_cost = DebugNoMpCost,
                debug_no_variance = DebugNoVariance,
                debug_always_hit = DebugAlwaysHit,
                debug_always_available_overdrive = DebugAlwaysOverdrive,
                debug_always_rare_steal = DebugAlwaysRareSteal,
                debug_always_9999_dmg = DebugAlways9999Damage,
                debug_always_99999_dmg = DebugAlways99999Damage
            };

            using MemoryStream stream = new();
            BinaryMapping.WriteObject(stream, debug);
            byte[] bytes = stream.ToArray();
            if (bytes.Length < 0x28)
            {
                Array.Resize(ref bytes, 0x28);
            }

            MemSharp_Service.Instance.Write(MemoryMap.ADDR_BTL_DEBUG_SETTINGS, bytes);
            ReloadSummary = "Debug flags applied to the runtime. Jarvis has re-synchronized the reading of the BTL debug block.";
            RefreshRuntime();
        }

        public void ReadDebugFlagsFromRuntime()
        {
            RefreshRuntime();
            ReloadSummary = "Debug flags re-lidas do runtime atual.";
        }

        public void ReloadCommandsIngame()
        {
            ReloadRuntimeBuffer("Commands", Project_Service.Instance.Path_KernelCommandUs, MemSharp_Service.Instance.Read<int>(MemoryMap.POINTER_FILE_COMMAND));
        }

        public void ReloadItemsIngame()
        {
            ReloadRuntimeBuffer("Items", Project_Service.Instance.Path_KernelItemUs, MemSharp_Service.Instance.Read<int>(MemoryMap.POINTER_FILE_ITEM));
        }

        public void ReloadMonMagic1Ingame()
        {
            ReloadRuntimeBuffer("Monster Commands 1", Project_Service.Instance.Path_KernelMonMagic1Us, MemSharp_Service.Instance.Read<int>(MemoryMap.POINTER_FILE_MONMAGIC1));
        }

        public void ReloadMonMagic2Ingame()
        {
            ReloadRuntimeBuffer("Monster Commands 2", Project_Service.Instance.Path_KernelMonMagic2Us, MemSharp_Service.Instance.Read<int>(MemoryMap.POINTER_FILE_MONMAGIC2));
        }

        public void ReloadAutoAbilitiesIngame()
        {
            MemoryBtl? battleState = TryReadBattleState();
            if (battleState == null)
            {
                ReloadSummary = "Could not decode the BTL struct, so the pointer for a_ability.bin has not been verified now.";
                return;
            }

            ReloadRuntimeBuffer("Auto-Abilities", Project_Service.Instance.Path_KernelAAbilityUs, (int)battleState.ptr_a_ability_bin, battleState.size_a_ability_bin);
        }

        public void ReloadAeonGrowIngame()
        {
            MemoryBtl? battleState = TryReadBattleState();
            if (battleState == null)
            {
                ReloadSummary = "Could not decode the BTL struct, so the pointer for sum_grow.bin has not been verified now.";
                return;
            }

            ReloadRuntimeBuffer("Aeon Grow", Project_Service.Instance.Path_KernelSumGrow, (int)battleState.ptr_sum_grow_bin, battleState.size_sum_grow_bin);
        }

        public void ReloadCustomizationsIngame()
        {
            MemoryBtl? battleState = TryReadBattleState();
            if (battleState == null)
            {
                ReloadSummary = "Could not decode the BTL struct, so the pointer for kaizou.bin has not been verified now.";
                return;
            }

            ReloadRuntimeBuffer("Customizations", Project_Service.Instance.Path_KernelKaizou, (int)battleState.ptr_kaizou_bin, battleState.size_kaizou_bin);
        }

        // Pt47/Pt48 acceptance contract harness. Snapshots one coherent tick of the
        // live battle (post-edge carry only) into the per-actor schema the contract
        // demands. This does NOT promote anything: the ceiling stays at
        // "structural dispatch/VM watch candidate". It only makes the contract runnable.
        public void CaptureNaturalSample()
        {
            if (!HookAvailable)
            {
                NaturalCaptureSummary = "Captura abortada: hook de memoria offline. Nao da para amostrar um tick sem RAM viva.";
                return;
            }

            byte battleActive = MemSharp_Service.Instance.Read<byte>(MemoryMap.ADDR_BATTLE_ACTIVE);
            if (battleActive != 1)
            {
                NaturalCaptureSummary = "Capture aborted: no active battle. The honest AI window and the post-edge carry (BattleActive); outside of it would be cold observation, not natural capture.";
                return;
            }

            MemoryBtl? battleState = TryReadBattleState();
            MemorySaveData? saveData = ReadSaveData();
            EncounterListProbeState encounterListState = ReadEncounterListProbeState();
            byte encounterIndex = MemSharp_Service.Instance.Read<byte>(MemoryMap.ADDR_BATTLE_ENCOUNTER_INDEX);
            byte trigger = MemSharp_Service.Instance.Read<byte>(MemoryMap.ADDR_BATTLE_TRIGGER);
            string battleName = MemSharp_Service.Instance.ReadString(MemoryMap.ADDR_BATTLE_NAME, 13).Trim('\0', ' ');
            string fieldName = battleState != null ? DecodeFieldName(battleState.field_name) : "-";

            LiveBattleRuntimePhaseSnapshot phase = BuildRuntimePhaseSnapshot(
                battleState, saveData, encounterListState, battleActive, encounterIndex, trigger, battleName);

            List<LiveBattleRuntimeActor> enemyActors = ReadRuntimeActors(MemoryMap.POINTER_BATTLE_ENEMY_LIST, MemoryBattle_Util.MonsterListCount, false);
            List<LiveBattleRuntimeActor> allyActors = ReadRuntimeActors(MemoryMap.POINTER_BATTLE_PLAYER_LIST, 18, true);

            List<LiveBattleRuntimeActor> enemyExecutors = enemyActors.Where(actor => actor.RawId > 0).ToList();
            if (enemyExecutors.Count == 0)
            {
                NaturalCaptureSummary = "Capture aborted: no useful enemy actor decoded in this tick.";
                return;
            }

            string cohort = string.IsNullOrWhiteSpace(CohortInput) ? "(unlabeled-cohort)" : CohortInput.Trim();
            string origin = currentBattleFromForcedRoll ? "bench-replay (non-natural-owner)" : "natural";
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            uint lastCom = battleState?.last_com ?? 0;
            string lastComDesc = TryDescribeRawGameIndex((ushort)(lastCom & 0xFFFF), out string description) ? description : "";
            string roster = BuildRosterSnapshot(enemyActors, allyActors);
            naturalCaptureTickSequence++;

            foreach (LiveBattleRuntimeActor actor in enemyExecutors)
            {
                MemoryChr enemy = actor.Chr;
                MonsterAiCorpus_Service.MonsterAiRecord? record = MonsterAiCorpus_Service.GetRecord(actor.RawId);
                string corpusLane = record == null
                    ? "no-corpus"
                    : $"{record.DisplayTitle} · {record.ScriptLineCount}L/{record.WorkerCount}W/{record.VariableCount}V";

                naturalCaptureSamples.Add(new LiveBattleNaturalCaptureSample(
                    naturalCaptureTickSequence,
                    timestamp,
                    cohort,
                    origin,
                    phase.PhaseLabel,
                    string.IsNullOrWhiteSpace(battleName) ? "-" : battleName,
                    fieldName,
                    phase.FieldIdx,
                    phase.GroupIdx,
                    phase.FormationIdx,
                    actor.Slot,
                    actor.RawId,
                    actor.NameLabel,
                    enemy.In_battle,
                    enemy.In_ctb_list,
                    enemy.Stat_exist_flag,
                    enemy.Current_hp,
                    enemy.Max_hp,
                    enemy.Current_ctb,
                    enemy.Max_ctb,
                    enemy.Stat_action,
                    lastCom,
                    lastComDesc,
                    enemy.Ptr_script_chunks,
                    enemy.Ptr_script_data,
                    enemy.Stat_move_target,
                    enemy.Seck_target_id,
                    enemy.Stat_target_list,
                    enemy.Stat_effect_target_flag,
                    enemy.Stat_prov_command_flag,
                    enemy.Provoked_by_id,
                    enemy.Threatened_by_id,
                    corpusLane,
                    roster));
            }

            string warn = currentBattleFromForcedRoll
                ? " AVISO: batalha de Force/Repeat -> marcada bench-replay; NAO conta para o 3-of-3 natural."
                : "";
            NaturalCaptureSummary = $"Tick {naturalCaptureTickSequence:D3} capturado: {enemyExecutors.Count} executor row(s) da coorte '{cohort}'. Buffer acumulado: {naturalCaptureSamples.Count} sample(s).{warn} O label accept/weak-support/reject/noise fica em branco no CSV para voce julgar a coerencia da serie.";
        }

        public void ExportNaturalCaptureCsv(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (naturalCaptureSamples.Count == 0)
            {
                NaturalCaptureSummary = "Nothing to export: the natural capture buffer is empty.";
                return;
            }

            try
            {
                StringBuilder builder = new();
                builder.AppendLine(LiveBattleNaturalCaptureSample.CsvHeader);
                foreach (LiveBattleNaturalCaptureSample sample in naturalCaptureSamples)
                {
                    builder.AppendLine(sample.ToCsvLine());
                }

                File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
                NaturalCaptureSummary = $"Exportado {naturalCaptureSamples.Count} sample(s) para {path}. Coluna claim_ceiling carrega o teto honesto em cada linha; accept_label fica em branco. Buffer mantido (use Clear para zerar).";
            }
            catch (Exception ex)
            {
                NaturalCaptureSummary = string.Format(Strings.U_Lbl_ExportFailed, ex.Message);
            }
        }

        public void ClearNaturalCaptures()
        {
            naturalCaptureSamples.Clear();
            naturalCaptureTickSequence = 0;
            NaturalCaptureSummary = "Natural capture buffer zeroed. Mark the cohort and capture new ticks to start another series.";
        }

        static string BuildRosterSnapshot(IReadOnlyList<LiveBattleRuntimeActor> enemyActors, IReadOnlyList<LiveBattleRuntimeActor> allyActors)
        {
            string enemies = string.Join(",", enemyActors.Where(actor => actor.RawId > 0).Select(actor => $"{actor.Slot:D2}:{actor.RawId:X4}"));
            string allies = string.Join(",", allyActors.Where(actor => actor.Chr.In_battle != 0).Select(actor => $"{actor.Slot:D2}:{actor.NameLabel}"));
            return $"enemies[{enemies}] allies[{allies}]";
        }

        void ExecuteEncounterRoll(ushort field, byte group, byte formation, string sourceLabel)
        {
            bool resumeAutoRefresh = AutoRefreshEnabled;
            int previousEncounterLevel = EncounterLevelNormal;

            try
            {
                if (resumeAutoRefresh)
                {
                    refreshTimer.Stop();
                }

                previousEncounterLevel = MemSharp_Service.Instance.Read<int>(RvaEncounterLevel);
                if (previousEncounterLevel <= 0)
                {
                    previousEncounterLevel = EncounterLevelNormal;
                }

                // HONEST path: when the in-process DINPUT8 probe (ffx-probe.dll) is hooked, run
                // MsBattleEncountExe on the game's MAIN THREAD (atomic scripted-encounter force) via
                // FfxProbe_Service -- no CreateRemoteThread. Fall back to the legacy off-thread Execute
                // (Unsafe) only when the probe is not present.
                int result;
                bool viaProbe = FfxProbe_Service.Instance.IsHooked;
                if (viaProbe)
                {
                    FfxProbe_Service.ProbeResult pr = FfxProbe_Service.Instance.ForceBattle(field, group, formation);
                    result = pr.Ret;
                }
                else
                {
                    MemSharp_Service.Instance.Write(RvaEncounterLevel, EncounterLevelTenfold);
                    result = MemSharp_Service.Instance.Execute<int>(
                        RvaMsBattleEncountExe,
                        CallingConventions.Stdcall,
                        true,
                        field,
                        group,
                        ForceEncounterWalkDeltaBits);
                }

                // Any battle reached through a forced/repeat roll is non-natural by
                // definition. Tag it so natural-capture samples cannot be mislabeled.
                currentBattleFromForcedRoll = true;

                ForceBattleStatus = viaProbe
                    ? $"Force Battle HONESTO via DINPUT8 probe (main thread): MsBattleEncountExe({field}, {group}, formation {formation}) -> ret {result}. Sem CreateRemoteThread ({sourceLabel}). Para uma batalha LIMPA use o field/group atual do runtime acima; se valido, a transicao comeca agora."
                    : $"Encounter roll enviado via MsBattleEncountExe({field}, {group}, walked_delta=0x{ForceEncounterWalkDeltaBits:X8}/{ForceEncounterWalkDelta:0}) usando {sourceLabel} (fallback Unsafe CreateRemoteThread). Formation {formation} fica como referencia; booster {EncounterLevelTenfold} armado temporariamente; retorno bruto {result}. Se a engine aceitar o route, a transicao deve comecar agora.";
                DispatcherTimer.RunOnce(() => RestoreEncounterLevel(previousEncounterLevel, resumeAutoRefresh), TimeSpan.FromMilliseconds(1500));
                DispatcherTimer.RunOnce(RefreshRuntime, TimeSpan.FromMilliseconds(500));
                DispatcherTimer.RunOnce(RefreshRuntime, TimeSpan.FromMilliseconds(1500));
            }
            catch (Exception ex)
            {
                RestoreEncounterLevel(previousEncounterLevel, resumeAutoRefresh);
                ForceBattleStatus = string.Format(Strings.U_Lbl_EncounterRollFailed, ex.Message);
            }
        }

        void RestoreEncounterLevel(int previousEncounterLevel, bool resumeAutoRefresh)
        {
            if (HookAvailable)
            {
                MemSharp_Service.Instance.Write(RvaEncounterLevel, previousEncounterLevel <= 0 ? EncounterLevelNormal : previousEncounterLevel);
            }

            if (resumeAutoRefresh && AutoRefreshEnabled)
            {
                refreshTimer.Start();
            }
        }

        void ReloadRuntimeBuffer(string label, string filePath, int memoryAddress, int maxSize = 0)
        {
            if (!CanReloadRuntimeFiles)
            {
                ReloadSummary = "Hook or workspace offline. Jarvis will not attempt ingame reload without both alive.";
                return;
            }

            if (!File.Exists(filePath))
            {
                ReloadSummary = string.Format(Strings.U_Llb_FileNotFoundAt, label, filePath);
                return;
            }

            if (memoryAddress <= 0)
            {
                ReloadSummary = string.Format(Strings.U_Llb_InvalidRuntimePointer, label, memoryAddress);
                return;
            }

            byte[] bytes = File.ReadAllBytes(filePath);
            if (maxSize > 0 && bytes.Length > maxSize)
            {
                ReloadSummary = string.Format(Strings.U_Llb_FileExceedsBuffer, label, bytes.Length, maxSize);
                return;
            }

            MemSharp_Service.Instance.Write(memoryAddress, bytes, false);
            ReloadSummary = string.Format(Strings.U_Llb_BytesReloaded, label, bytes.Length, memoryAddress);
        }

        MemoryBtl? TryReadBattleState()
        {
            byte[] bytes = MemSharp_Service.Instance.Read<byte>(MemoryMap.ADDR_BTL, 0x2200);
            if (bytes == null || bytes.Length == 0)
            {
                return null;
            }

            using MemoryStream stream = new(bytes);
            return BinaryMapping.ReadObject<MemoryBtl>(stream);
        }

        void LoadRuntimeEnemies(IReadOnlyList<LiveBattleRuntimeActor> enemyActors)
        {
            RuntimeEnemyRows.Clear();
            RuntimeAiProbeRows.Clear();

            if (enemyActors.Count == 0)
            {
                EnemySummary = "No useful enemy actor decoded in the current snapshot.";
                AiProbeSummary = "Without decoded runtime enemy actor, cannot match live slots with AI corpus.";
                return;
            }

            foreach (LiveBattleRuntimeActor actor in enemyActors)
            {
                MemoryChr enemy = actor.Chr;
                short rawMonsterId = actor.RawId;
                if (rawMonsterId <= 0)
                {
                    continue;
                }

                short dictionaryId = (short)(rawMonsterId - 0x1000);
                string monsterName = Monster_Dictionary.Instance.ContainsKey(dictionaryId)
                    ? Monster_Dictionary.Instance[dictionaryId]
                    : "<NOT INDEXED>";

                string hpSummary = FormatHpSummary(enemy);
                string targetSummary = BuildTargetSummary(enemy);
                string statusSummary = BuildStatusSummary(enemy);

                RuntimeEnemyRows.Add(new LiveBattleEnemyRow
                {
                    SlotLabel = actor.SlotLabel,
                    MonsterLabel = actor.NameLabel,
                    RawHex = $"{rawMonsterId:X4}h",
                    HpLabel = hpSummary,
                    StateLabel = $"Exist {(enemy.Stat_exist_flag ? "Y" : "N")} · InBattle {enemy.In_battle} · Action {enemy.Stat_action:X2}h · CTB {enemy.Current_ctb}/{enemy.Max_ctb}",
                    PlacementLabel = $"Area {enemy.Area:X4}h · Pos {enemy.Pos:D2} · Group {enemy.Stat_group:D2}",
                    StatusSummary = statusSummary,
                    TargetSummary = targetSummary,
                    P1 = MemSharp_Service.Instance.Read<float>(actor.Address + 928, false),
                    P2 = MemSharp_Service.Instance.Read<float>(actor.Address + 932, false),
                    P3 = MemSharp_Service.Instance.Read<float>(actor.Address + 936, false),
                    P4 = MemSharp_Service.Instance.Read<float>(actor.Address + 940, false)
                });

                RuntimeAiProbeRows.Add(BuildAiProbeRow(actor.Slot, rawMonsterId, monsterName, enemy));
            }

            EnemySummary = RuntimeEnemyRows.Count == 0
                ? "No live enemy found in the current snapshot."
                : string.Format(Strings.U_Llb_EnemyDecoded, RuntimeEnemyRows.Count);

            AiProbeSummary = RuntimeAiProbeRows.Count == 0
                ? "No live enemy could be cross-matched with AI corpus in this snapshot."
                : string.Format(Strings.U_Llb_EnemyCrossMatched, RuntimeAiProbeRows.Count);
        }

        List<LiveBattleRuntimeActor> ReadRuntimeActors(int listPointerAddress, int count, bool isAlly)
        {
            List<LiveBattleRuntimeActor> actors = new();
            int listAddress = MemSharp_Service.Instance.Read<int>(listPointerAddress);
            if (listAddress <= 0)
            {
                return actors;
            }

            for (int slot = 0; slot < count; slot++)
            {
                int actorAddress = listAddress + slot * MemoryMap.SIZE_BATTLE_CHR_ENTRY;
                byte[] actorBytes = MemSharp_Service.Instance.Read<byte>(actorAddress, MemoryMap.SIZE_BATTLE_CHR_ENTRY, false);
                if (actorBytes == null || actorBytes.Length == 0)
                {
                    continue;
                }

                MemoryChr actor;
                using (MemoryStream stream = new(actorBytes))
                {
                    actor = BinaryMapping.ReadObject<MemoryChr>(stream);
                }

                short rawId = (short)actor.Id;
                if (!isAlly &&
                    rawId <= 0 &&
                    actor.In_battle == 0 &&
                    !actor.Stat_exist_flag &&
                    actor.Stat_action == 0)
                {
                    continue;
                }

                string slotLabel = $"{(isAlly ? "Ally" : "Enemy")} {slot:D2}";
                string nameLabel = ResolveRuntimeActorName(isAlly, slot, rawId, actor.Name);
                actors.Add(new LiveBattleRuntimeActor
                {
                    Key = $"{(isAlly ? "A" : "E")}:{slot:D2}",
                    IsAlly = isAlly,
                    Slot = slot,
                    SlotLabel = slotLabel,
                    NameLabel = nameLabel,
                    DisplayLabel = $"{slotLabel} · {nameLabel}",
                    Address = actorAddress,
                    RawId = rawId,
                    Chr = actor
                });
            }

            return actors;
        }

        void UpdateAiProbeObservations(bool wasObservedInBattle, MemoryBtl? battleState, IReadOnlyList<LiveBattleRuntimeActor> allyActors, IReadOnlyList<LiveBattleRuntimeActor> enemyActors)
        {
            if (!InBattle)
            {
                if (wasObservedInBattle)
                {
                    AddAiEventRow(
                        "Battle End",
                        lastObservedBattleLabel,
                        lastObservedLastCommand.HasValue
                            ? $"BTL snapshot saiu do estado ativo. Ultimo LastCom observado: {FormatLastCommand(lastObservedLastCommand.Value)}."
                            : "BTL snapshot left active state before proving a stable LastCom.");
                }

                lastObservedInBattle = false;
                currentBattleFromForcedRoll = false;
                lastObservedActors.Clear();
                lastObservedBtlSnapshot = null;
                lastObservedLastCommand = null;
                TurnOwnerProbeSummary = RuntimeAiEventRows.Count == 0
                    ? "No active battle. Current turn owner remains unproven and probe awaits a live encounter."
                    : "No active battle. The last runtime log was kept as evidence of the previous battle; current turn owner remains without proven field.";
                AiEventSummary = RuntimeAiEventRows.Count == 0
                    ? "Runtime phase / turn log idle until the next real context change."
                    : $"{RuntimeAiEventRows.Count} runtime observation(s) retidos. O log agora mistura fases, prelude, LastCom, action-state e cluster BTL, sempre no modo observacional.";
                return;
            }

            List<LiveBattleRuntimeActor> allActors = allyActors
                .Concat(enemyActors)
                .ToList();

            if (!wasObservedInBattle)
            {
                lastObservedActors.Clear();
                lastObservedBtlSnapshot = null;
                lastObservedLastCommand = null;
                lastObservedBattleLabel = string.IsNullOrWhiteSpace(BattleName) ? "-" : BattleName;
                AddAiEventRow(
                    "Battle Start",
                    lastObservedBattleLabel,
                    BuildBattleStartSummary(battleState));
            }

            List<LiveBattleRuntimeActor> actionCandidates = allActors
                .Where(IsActionCandidate)
                .ToList();
            TurnOwnerProbeSummary = BuildTurnOwnerProbeSummary(allActors.Count, actionCandidates);

            if (battleState != null)
            {
                LiveBattleBtlObservationSnapshot currentBtlSnapshot = LiveBattleBtlObservationSnapshot.From(battleState);
                if (lastObservedBtlSnapshot != null)
                {
                    string? btlDelta = BuildBtlObservationChangeSummary(lastObservedBtlSnapshot, currentBtlSnapshot);
                    if (!string.IsNullOrWhiteSpace(btlDelta))
                    {
                        AddAiEventRow(
                            "BTL Route / State",
                            "Route / BTL",
                            btlDelta);
                    }
                }

                if (lastObservedLastCommand.HasValue && lastObservedLastCommand.Value != battleState.last_com)
                {
                    AddAiEventRow(
                        "Last Command",
                        DescribeCommandOwnerCandidate(actionCandidates),
                        BuildLastCommandChangeSummary(lastObservedLastCommand.Value, battleState.last_com, actionCandidates));
                }

                lastObservedLastCommand = battleState.last_com;
                lastObservedBtlSnapshot = currentBtlSnapshot;
            }
            else
            {
                lastObservedLastCommand = null;
                lastObservedBtlSnapshot = null;
            }

            Dictionary<string, LiveBattleActorSnapshot> currentSnapshots = new(StringComparer.Ordinal);
            foreach (LiveBattleRuntimeActor actor in allActors)
            {
                LiveBattleActorSnapshot snapshot = LiveBattleActorSnapshot.From(actor);
                currentSnapshots[snapshot.Key] = snapshot;

                if (!lastObservedActors.TryGetValue(snapshot.Key, out LiveBattleActorSnapshot? previous))
                {
                    continue;
                }

                if (previous.ActionState == snapshot.ActionState)
                {
                    continue;
                }

                if (previous.ActionState == 0 && snapshot.ActionState == 0)
                {
                    continue;
                }

                AddAiEventRow(
                    "Action State",
                    snapshot.DisplayLabel,
                    BuildActionTransitionSummary(previous, snapshot));
            }

            lastObservedActors.Clear();
            foreach ((string key, LiveBattleActorSnapshot snapshot) in currentSnapshots)
            {
                lastObservedActors[key] = snapshot;
            }

            lastObservedInBattle = true;
            AiEventSummary = RuntimeAiEventRows.Count == 0
                ? "No relevant transition has been observed yet. The log records runtime phases, battle start, LastCom, non-zero changes of action-state, and relevant deltas of the BTL cluster."
                : $"{RuntimeAiEventRows.Count} runtime observation(s) registradas neste ciclo. O log mostra fases, prelude e pistas temporais honestas; current turn owner ainda nao e um campo provado.";
        }

        LiveBattleAiProbeRow BuildAiProbeRow(int slot, short rawMonsterId, string runtimeMonsterName, MemoryChr enemy)
        {
            MonsterAiCorpus_Service.MonsterAiRecord? record = MonsterAiCorpus_Service.GetRecord(rawMonsterId);
            string hpSummary = FormatHpSummary(enemy);
            string targetSummary = BuildTargetSummary(enemy);
            string statusSummary = BuildStatusSummary(enemy);
            string runtimeSummary = $"Action {enemy.Stat_action:X2}h · {hpSummary} · CTB {enemy.Current_ctb}/{enemy.Max_ctb} · Pos {enemy.Pos:D2} · Area {enemy.Area:X4}h";
            string scriptPointerSummary = BuildScriptPointerSummary(enemy);

            if (record == null)
            {
                return new LiveBattleAiProbeRow
                {
                    SlotLabel = $"Enemy {slot:D2}",
                    MonsterLabel = runtimeMonsterName,
                    RuntimeSummary = runtimeSummary,
                    TargetSummary = targetSummary,
                    StatusSummary = statusSummary,
                    ScriptPointerSummary = scriptPointerSummary,
                    ForcedActionSummary = Strings.U_Lbl_CorpusBlockMissing,
                    AbilityListSummary = "-",
                    CommandReferenceSummary = "-",
                    ScriptSummary = "No static bridge yet.",
                    ScriptPreview = "No AI parser block was located for this monster id."
                };
            }

            return new LiveBattleAiProbeRow
            {
                SlotLabel = $"Enemy {slot:D2}",
                MonsterLabel = record.DisplayTitle,
                RuntimeSummary = runtimeSummary,
                TargetSummary = targetSummary,
                StatusSummary = statusSummary,
                ScriptPointerSummary = scriptPointerSummary,
                ForcedActionSummary = record.ForcedActionSummary,
                AbilityListSummary = record.AbilityListSummary,
                CommandReferenceSummary = record.CommandReferenceSummary,
                ScriptSummary = $"{record.ScriptLineCount} lines · {record.WorkerCount} workers · {record.VariableCount} vars",
                ScriptPreview = record.ShortScriptPreview
            };
        }

        void CaptureLiveRouting(ushort field, byte group, byte formation, string fieldName)
        {
            lastLiveFieldIdx = field;
            lastLiveGroupIdx = group;
            lastLiveFormationIdx = formation;
            HasCapturedRouting = true;
            CapturedRoutingLabel = $"{BattleName} · {fieldName} · field {field:D3} · group {group:D2} · formation {formation:D2}";

            if (!TryParseForceInputs(out _, out _, out _, out _))
            {
                ForceFieldInput = field.ToString();
                ForceGroupInput = group.ToString();
                ForceFormationInput = formation.ToString();
            }
        }

        void SyncDebugFlagsFrom(MemoryBtl.BtlDebug debug)
        {
            DebugInvincibleMon = debug.debug_invincible_mon;
            DebugInvinciblePly = debug.debug_invincible_ply;
            DebugMonControl = debug.debug_mon_control;
            DebugFreeCamera = debug.debug_free_camera;
            DebugNoMagicEffects = debug.debug_no_magic_effects;
            DebugNoMpCost = debug.debug_no_mp_cost;
            DebugNoVariance = debug.debug_no_variance;
            DebugAlwaysHit = debug.debug_always_hit;
            DebugAlwaysOverdrive = debug.debug_always_available_overdrive;
            DebugAlwaysRareSteal = debug.debug_always_rare_steal;
            DebugAlways9999Damage = debug.debug_always_9999_dmg;
            DebugAlways99999Damage = debug.debug_always_99999_dmg;
        }

        static string BuildRoutingCursorSummary(MemoryBtl battleState)
        {
            return $"cur_field {FormatPointer(battleState.ptr_btl_bin_cur_field)} · cur_encounter {FormatPointer(battleState.ptr_btl_bin_cur_encounter)} · cur_group {FormatPointer(battleState.ptr_btl_bin_cur_group)} · cur_formation {FormatPointer(battleState.ptr_btl_bin_cur_formation)}";
        }

        bool TryParseForceInputs(out ushort field, out byte group, out byte formation, out string error)
        {
            field = 0;
            group = 0;
            formation = 0;

            if (!TryParseNumeric(ForceFieldInput, ushort.MinValue, ushort.MaxValue, out int fieldValue))
            {
                error = "Field must be decimal or hex (0x/..h) between 0 and 65535.";
                return false;
            }

            if (!TryParseNumeric(ForceGroupInput, byte.MinValue, byte.MaxValue, out int groupValue))
            {
                error = "Group must be decimal or hex (0x/..h) between 0 and 255.";
                return false;
            }

            if (!TryParseNumeric(ForceFormationInput, byte.MinValue, byte.MaxValue, out int formationValue))
            {
                error = "Formation must be decimal or hex (0x/..h) between 0 and 255.";
                return false;
            }

            field = (ushort)fieldValue;
            group = (byte)groupValue;
            formation = (byte)formationValue;
            error = string.Empty;
            return true;
        }

        static bool TryParseNumeric(string? text, int min, int max, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string trimmed = text.Trim();
            bool isHex = trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                         trimmed.EndsWith("h", StringComparison.OrdinalIgnoreCase);

            if (isHex)
            {
                string hex = trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? trimmed[2..]
                    : trimmed[..^1];

                if (!int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out value))
                {
                    return false;
                }
            }
            else if (!int.TryParse(trimmed, out value))
            {
                return false;
            }

            return value >= min && value <= max;
        }

        internal static string DecodeFieldName(byte[]? fieldNameBytes)
        {
            if (fieldNameBytes == null || fieldNameBytes.Length == 0)
            {
                return "-";
            }

            int length = Array.IndexOf(fieldNameBytes, (byte)0);
            if (length < 0)
            {
                length = fieldNameBytes.Length;
            }

            string decoded = Encoding.ASCII.GetString(fieldNameBytes, 0, length).Trim();
            return string.IsNullOrWhiteSpace(decoded) ? "-" : decoded;
        }

        static string FormatPartySlots(sbyte[] slots)
        {
            if (slots == null || slots.Length == 0)
            {
                return "-";
            }

            string[] names = slots.Select(slot =>
            {
                if (slot < 0)
                {
                    return "(empty)";
                }

                return Character_Dictionary.Instance.ContainsKey(slot)
                    ? Character_Dictionary.Instance[slot]
                    : $"#{slot}";
            }).ToArray();

            return string.Join(" / ", names);
        }

        static string FormatRuntimeByteArray(byte[]? values)
        {
            if (values == null || values.Length == 0)
            {
                return "-";
            }

            return string.Join(" ", values.Select(value => value.ToString("X2") + "h"));
        }

        static string FormatHpSummary(MemoryChr enemy)
        {
            int maxHp = Math.Max(0, enemy.Max_hp);
            int currentHp = Math.Max(0, enemy.Current_hp);
            string pct = maxHp > 0
                ? $"{(currentHp / (double)maxHp) * 100.0:0.0}%"
                : "-";

            return $"{currentHp:N0}/{maxHp:N0} HP · {pct}";
        }

        static string BuildStatusSummary(MemoryChr enemy)
        {
            List<string> statuses = new();

            if (enemy.FlagStatusSufferKo) statuses.Add("KO");
            if (enemy.FlagStatusSufferZombie) statuses.Add("Zombie");
            if (enemy.FlagStatusSufferPetrification) statuses.Add("Stone");
            if (enemy.FlagStatusSufferPoison) statuses.Add("Poison");
            if (enemy.FlagStatusSufferBreakPower) statuses.Add("PowerBreak");
            if (enemy.FlagStatusSufferBreakMagic) statuses.Add("MagicBreak");
            if (enemy.FlagStatusSufferBreakArmor) statuses.Add("ArmorBreak");
            if (enemy.FlagStatusSufferBreakMental) statuses.Add("MentalBreak");
            if (enemy.FlagStatusSufferConfusion) statuses.Add("Confuse");
            if (enemy.FlagStatusSufferBerserk) statuses.Add("Berserk");
            if (enemy.FlagStatusSufferProvoke) statuses.Add("Provoke");
            if (enemy.FlagStatusSufferThreaten) statuses.Add("Threaten");

            AddTimedStatus(statuses, "Sleep", enemy.Status_suffer_turns_left.Sleep);
            AddTimedStatus(statuses, "Silence", enemy.Status_suffer_turns_left.Silence);
            AddTimedStatus(statuses, "Dark", enemy.Status_suffer_turns_left.Darkness);
            AddTimedStatus(statuses, "Shell", enemy.Status_suffer_turns_left.Shell);
            AddTimedStatus(statuses, "Protect", enemy.Status_suffer_turns_left.Protect);
            AddTimedStatus(statuses, "Reflect", enemy.Status_suffer_turns_left.Reflect);
            AddTimedStatus(statuses, "Regen", enemy.Status_suffer_turns_left.Regen);
            AddTimedStatus(statuses, "Haste", enemy.Status_suffer_turns_left.Haste);
            AddTimedStatus(statuses, "Slow", enemy.Status_suffer_turns_left.Slow);

            if (enemy.FlagStatusSufferExtraScan) statuses.Add("Scan");
            if (enemy.FlagStatusSufferExtraShield) statuses.Add("Shield");
            if (enemy.FlagStatusSufferExtraBoost) statuses.Add("Boost");
            if (enemy.FlagStatusSufferExtraAutoLife) statuses.Add("AutoLife");
            if (enemy.FlagStatusSufferExtraCurse) statuses.Add("Curse");
            if (enemy.FlagStatusSufferExtraDefend) statuses.Add("Defend");
            if (enemy.FlagStatusSufferExtraGuard) statuses.Add("Guard");
            if (enemy.FlagStatusSufferExtraSentinel) statuses.Add("Sentinel");
            if (enemy.FlagStatusSufferExtraDoom) statuses.Add("Doom");

            return statuses.Count == 0
                ? "Clean"
                : JoinPreview(statuses, 8);
        }

        static string BuildTargetSummary(MemoryChr enemy)
        {
            List<string> parts = new()
            {
                $"MoveTarget {enemy.Stat_move_target:X2}h",
                $"TargetList {enemy.Stat_target_list:X2}h",
                $"EffectTarget {(enemy.Stat_effect_target_flag ? "Y" : "N")}",
                $"ProvCmd {(enemy.Stat_prov_command_flag ? "Y" : "N")}"
            };

            if (enemy.FlagStatusSufferProvoke || (enemy.Provoked_by_id != 0 && enemy.Provoked_by_id != 0xFF))
            {
                parts.Add($"ProvokedBy {FormatActorHint(enemy.Provoked_by_id)}");
            }

            if (enemy.FlagStatusSufferThreaten || (enemy.Threatened_by_id != 0 && enemy.Threatened_by_id != 0xFF))
            {
                parts.Add($"ThreatenedBy {FormatActorHint(enemy.Threatened_by_id)}");
            }

            return string.Join(" · ", parts);
        }

        // Surfaces the two AI watch candidates (Pt47/Pt48). Read-only: a relational
        // offset shift here is a watch signal, never a proven dispatch edge.
        static string BuildScriptPointerSummary(MemoryChr enemy)
        {
            return $"ScrChunks {enemy.Ptr_script_chunks:X8}h · ScrData {enemy.Ptr_script_data:X8}h · watch candidate (nao prova dispatch edge / worker ativo / linha ativa)";
        }

        static string FormatActorHint(byte actorId)
        {
            if (actorId <= sbyte.MaxValue && Character_Dictionary.Instance.ContainsKey((sbyte)actorId))
            {
                sbyte signedId = (sbyte)actorId;
                return $"{actorId:X2}h ({Character_Dictionary.Instance[signedId]})";
            }

            return $"{actorId:X2}h";
        }

        void ResetAiProbeObservations(bool clearLog)
        {
            lastObservedActors.Clear();
            lastObservedBtlSnapshot = null;
            lastObservedPhaseSnapshot = null;
            lastObservedLastCommand = null;
            lastObservedInBattle = false;
            lastObservedBattleLabel = "-";

            if (clearLog)
            {
                RuntimeAiEventRows.Clear();
                aiObservationSequence = 0;
            }
        }

        void AddAiEventRow(string eventLabel, string actorLabel, string detailLabel)
        {
            aiObservationSequence++;
            RuntimeAiEventRows.Insert(0, new LiveBattleAiEventRow
            {
                SequenceLabel = $"Obs {aiObservationSequence:D3}",
                EventLabel = eventLabel,
                ActorLabel = actorLabel,
                DetailLabel = detailLabel
            });

            while (RuntimeAiEventRows.Count > 48)
            {
                RuntimeAiEventRows.RemoveAt(RuntimeAiEventRows.Count - 1);
            }
        }

        static bool IsActionCandidate(LiveBattleRuntimeActor actor)
        {
            return actor.Chr.In_battle != 0 &&
                   actor.Chr.Stat_exist_flag &&
                   actor.Chr.Stat_action != 0;
        }

        static string BuildBattleStartSummary(MemoryBtl? battleState)
        {
            if (battleState == null)
            {
                return "Active battle detected, but the BTL struct was not complete enough to summarize field/group/formation on this pass.";
            }

            string fieldName = DecodeFieldName(battleState.field_name);
            return $"Field {fieldName} ({battleState.field_idx:D3}) · Group {battleState.group_idx:D2} · Formation {battleState.formation_idx:D2} · LastCom {FormatLastCommand(battleState.last_com)}";
        }

        string BuildPassiveRouteSummary(MemorySaveData? saveData, MemoryBtl? battleState, string liveBattleName)
        {
            if (saveData == null)
            {
                return "Passive route truth unavailable. SaveData was not decoded on this pass.";
            }

            ushort tableId = saveData.now_eventjump_map_no;
            string mapCode = TryResolveEncounterMapCode(tableId) ?? "-";
            string summary = $"Passive route truth: save.eventjump_map_no {tableId} (0x{tableId:X4})";
            if (!string.IsNullOrWhiteSpace(mapCode) && mapCode != "-")
            {
                summary += $" -> {mapCode}";
            }

            summary += $" · map_id {saveData.now_eventjump_map_id} · room {saveData.current_room_id} · spawn {saveData.current_spawnpoint}";

            if (!InBattle && battleState != null && IsColdFieldName(DecodeFieldName(battleState.field_name)))
            {
                summary += $" · shell BTL fria estacionada em {DecodeFieldName(battleState.field_name)}/{liveBattleName}; fora da batalha, esta route passiva e a pista mais honesta do campo real.";
            }

            return summary;
        }

        static string BuildEncounterListProbeSummary(EncounterListProbeState state)
        {
            string summary = $"Encounter-list probe: resource 0x{state.ResourcePointer:X8} · field_base 0x{state.FieldBasePointer:X8} · encounter_base 0x{state.EncounterBasePointer:X8} · field_count {state.FieldCount} · scene 0x{state.SceneToken:X4} · kind {state.SceneKind:X2}h · submode {state.SceneSubmode:X2}h · mode {state.BattleMode:X2}h";
            return state.IsReady
                ? state.HasObservablePreludeSignal
                    ? $"{summary} · prelude observavel apareceu para list helpers read-only."
                    : $"{summary} · list helpers estao online, mas nenhuma prelude visivel apareceu nesta passada."
                : $"{summary} · prelude ainda fria; MsBtlList*/MsFldGetScene continuam contexto perigoso fora da batalha.";
        }

        string BuildRoutingSummary(string fieldName, MemoryBtl battleState, EncounterListProbeState encounterListState)
        {
            string summary = $"Field {FieldLabel} · Battlefield {BattlefieldLabel} · Group {GroupLabel} · Formation {FormationLabel}";
            if (!InBattle && IsColdFieldName(fieldName))
            {
                summary += " · shell BTL fria/parked: use o passive route truth abaixo em vez de vender system_0 como route real.";
            }

            if (!encounterListState.IsReady)
            {
                summary += " · encounter-list bases ainda frias.";
            }

            return summary;
        }

        LiveBattleRuntimePhaseSnapshot BuildRuntimePhaseSnapshot(
            MemoryBtl? battleState,
            MemorySaveData? saveData,
            EncounterListProbeState encounterListState,
            byte battleActive,
            byte encounterIndex,
            byte trigger,
            string liveBattleName)
        {
            string fieldName = battleState != null
                ? DecodeFieldName(battleState.field_name)
                : "-";
            ushort passiveTableId = saveData?.now_eventjump_map_no ?? 0;
            string passiveMapCode = passiveTableId != 0
                ? TryResolveEncounterMapCode(passiveTableId) ?? "-"
                : "-";
            byte battleStateByte = battleState?.battle_state ?? 0;
            byte battleEndType = battleState?.battle_end_type ?? 0;
            byte encounterType = battleState?.encounter_type ?? 0;
            byte screenTransition = battleState?.screen_transition ?? 0;
            LiveBattleRuntimePhaseKind phaseKind = ClassifyRuntimePhase(
                fieldName,
                battleActive,
                trigger,
                battleStateByte,
                battleEndType,
                encounterType,
                screenTransition,
                encounterListState);

            return new LiveBattleRuntimePhaseSnapshot(
                phaseKind,
                battleActive,
                trigger,
                encounterIndex,
                battleStateByte,
                battleEndType,
                encounterType,
                screenTransition,
                encounterListState.IsReady,
                encounterListState.HasObservablePreludeSignal,
                passiveTableId,
                passiveMapCode,
                battleState?.field_idx ?? 0,
                battleState?.group_idx ?? 0,
                battleState?.formation_idx ?? 0,
                fieldName,
                string.IsNullOrWhiteSpace(liveBattleName) ? "-" : liveBattleName);
        }

        static LiveBattleRuntimePhaseKind ClassifyRuntimePhase(
            string fieldName,
            byte battleActive,
            byte trigger,
            byte battleState,
            byte battleEndType,
            byte encounterType,
            byte screenTransition,
            EncounterListProbeState encounterListState)
        {
            if (battleEndType != 0 &&
                battleActive == 0 &&
                battleState == 0)
            {
                return LiveBattleRuntimePhaseKind.PostBattle;
            }

            // Settled active battle (active==1 && state==0x01) wins over residual
            // trigger/encounter/screen bytes that can linger right after the edge.
            // Without this guard a fully-live battle gets misread as EncounterTrigger.
            if (battleActive == 1 && battleState == 0x01)
            {
                return LiveBattleRuntimePhaseKind.BattleActive;
            }

            if (battleState != 0 &&
                (battleState != 0x01 || battleActive != 1))
            {
                return LiveBattleRuntimePhaseKind.EncounterTrigger;
            }

            if ((battleActive != 0 && battleActive != 1) ||
                trigger != 0 ||
                encounterType != 0 ||
                screenTransition != 0)
            {
                return LiveBattleRuntimePhaseKind.EncounterTrigger;
            }

            if (battleActive == 1 || battleState == 0x01)
            {
                return LiveBattleRuntimePhaseKind.BattleActive;
            }

            if (encounterListState.IsReady &&
                encounterListState.HasObservablePreludeSignal)
            {
                return LiveBattleRuntimePhaseKind.NaturalPrelude;
            }

            return IsColdFieldName(fieldName)
                ? LiveBattleRuntimePhaseKind.ColdContext
                : LiveBattleRuntimePhaseKind.FieldRuntimeIdle;
        }

        static string BuildRuntimePhaseSummary(LiveBattleRuntimePhaseSnapshot snapshot)
        {
            return snapshot.PhaseKind switch
            {
                LiveBattleRuntimePhaseKind.ColdContext =>
                    $"Runtime phase: {snapshot.PhaseLabel}. Shell BTL fria/parked em {snapshot.FieldName}/{snapshot.BattleName}; fora da batalha, o passive route truth {snapshot.PassiveRouteLabel} vale mais que a route fria do shell.",
                LiveBattleRuntimePhaseKind.FieldRuntimeIdle =>
                    $"Runtime phase: {snapshot.PhaseLabel}. Field runtime sem trigger nem prelude observavel; passive route {snapshot.PassiveRouteLabel} e a pista mais honesta agora.",
                LiveBattleRuntimePhaseKind.NaturalPrelude =>
                    $"Runtime phase: {snapshot.PhaseLabel}. Prelude observavel apareceu via encounter-list; isso e pista opcional de leitura, nao etapa obrigatoria nem prova de launch path.",
                LiveBattleRuntimePhaseKind.EncounterTrigger =>
                    $"Runtime phase: {snapshot.PhaseLabel}. Primeiro boot observavel da maquina de batalha: battleActive {snapshot.BattleActive} · battle_state {snapshot.BattleState:X2}h · route {snapshot.RouteLabel} · trigger {snapshot.Trigger:X2}h · encounter_type {snapshot.EncounterType:X2}h · screen {snapshot.ScreenTransition:X2}h. Watch only; isso ainda nao e replay exato.",
                LiveBattleRuntimePhaseKind.BattleActive =>
                    $"Runtime phase: {snapshot.PhaseLabel}. Estado de batalha mais assentado: battleActive {snapshot.BattleActive} · battle_state {snapshot.BattleState:X2}h. LastCom, action-state, CTB e target agora sao pistas suplementares mais uteis.",
                LiveBattleRuntimePhaseKind.PostBattle =>
                    $"Runtime phase: {snapshot.PhaseLabel}. battle_end_type {snapshot.BattleEndType:X2}h ainda vivo com battle machine em repouso; route e passive context podem estar assentando apos o combate.",
                _ =>
                    $"Runtime phase: {snapshot.PhaseLabel}."
            };
        }

        void UpdateRuntimePhaseObservations(LiveBattleRuntimePhaseSnapshot currentPhase)
        {
            if (lastObservedPhaseSnapshot == null)
            {
                lastObservedPhaseSnapshot = currentPhase;
                AddAiEventRow(
                    "Runtime Phase",
                    "Runtime / Prelude",
                    $"Initial phase: {BuildCompactRuntimePhaseSummary(currentPhase)}");
                return;
            }

            string? delta = BuildRuntimePhaseChangeSummary(lastObservedPhaseSnapshot, currentPhase);
            lastObservedPhaseSnapshot = currentPhase;
            if (string.IsNullOrWhiteSpace(delta))
            {
                return;
            }

            AddAiEventRow(
                "Runtime Phase",
                "Runtime / Prelude",
                delta);
        }

        static string? BuildRuntimePhaseChangeSummary(LiveBattleRuntimePhaseSnapshot previous, LiveBattleRuntimePhaseSnapshot current)
        {
            bool phaseChanged = previous.PhaseKind != current.PhaseKind;
            bool preludeObserved = !previous.HasObservablePreludeSignal && current.HasObservablePreludeSignal;
            bool triggerRaised = previous.Trigger == 0 && current.Trigger != 0;
            bool battleRaised = previous.BattleActive == 0 && current.BattleActive != 0;
            bool battleDropped = previous.BattleActive != 0 && current.BattleActive == 0;
            bool passiveRouteChanged = previous.PassiveRouteId != current.PassiveRouteId ||
                                       !string.Equals(previous.PassiveMapCode, current.PassiveMapCode, StringComparison.Ordinal);
            bool routeChanged = previous.FieldIdx != current.FieldIdx ||
                                previous.GroupIdx != current.GroupIdx ||
                                previous.FormationIdx != current.FormationIdx ||
                                !string.Equals(previous.FieldName, current.FieldName, StringComparison.Ordinal);

            if (!phaseChanged && !preludeObserved && !triggerRaised && !battleRaised && !battleDropped && !passiveRouteChanged && !routeChanged)
            {
                return null;
            }

            List<string> changes = new();
            if (phaseChanged)
            {
                changes.Add($"{previous.PhaseLabel} -> {current.PhaseLabel}");
            }

            if (preludeObserved)
            {
                changes.Add($"prelude observavel ({current.EncounterPreludeLabel})");
            }

            if (triggerRaised)
            {
                changes.Add($"trigger 0x{current.Trigger:X2} raised");
            }

            if (battleRaised)
            {
                changes.Add($"battle machine 0 -> {current.BattleActive}");
            }

            if (battleDropped)
            {
                changes.Add("battleActive dropped");
            }

            if (passiveRouteChanged)
            {
                changes.Add($"passive_route {previous.PassiveRouteLabel} -> {current.PassiveRouteLabel}");
            }

            if (routeChanged)
            {
                changes.Add($"route {previous.RouteLabel} -> {current.RouteLabel}");
            }

            changes.Add($"now {BuildCompactRuntimePhaseSummary(current)}");
            return string.Join(" · ", changes);
        }

        static string BuildCompactRuntimePhaseSummary(LiveBattleRuntimePhaseSnapshot snapshot)
        {
            return $"{snapshot.PhaseLabel} · active {snapshot.BattleActive} · battle_state 0x{snapshot.BattleState:X2} · route {snapshot.RouteLabel} · trigger 0x{snapshot.Trigger:X2} · encounter 0x{snapshot.EncounterType:X2} · screen 0x{snapshot.ScreenTransition:X2} · passive {snapshot.PassiveRouteLabel}";
        }

        static string BuildTurnOwnerProbeSummary(int decodedActorCount, List<LiveBattleRuntimeActor> actionCandidates)
        {
            if (decodedActorCount == 0)
            {
                return "Active battle, but no actor snapshot has been decoded yet.";
            }

            if (actionCandidates.Count == 0)
            {
                return "No action-state candidate appeared in this snapshot. Current turn owner continues without direct proof.";
            }

            if (actionCandidates.Count == 1)
            {
                LiveBattleRuntimeActor actor = actionCandidates[0];
                return $"Only action-state candidate neste snapshot: {BuildActorCorrelationSummary(actor)}. Isso ainda e pista runtime suplementar, nao owner field provado.";
            }

            string preview = BuildActionCandidateListPreview(actionCandidates);
            return $"{actionCandidates.Count} action-state candidates simultaneos: {preview}. Ainda nao existe owner field provado nesta sonda.";
        }

        static string DescribeCommandOwnerCandidate(List<LiveBattleRuntimeActor> actionCandidates)
        {
            if (actionCandidates.Count == 1)
            {
                return $"{actionCandidates[0].DisplayLabel} (single candidate)";
            }

            if (actionCandidates.Count == 0)
            {
                return "No single actor candidate";
            }

            return $"{actionCandidates.Count} simultaneous candidates";
        }

        static string BuildActionTransitionSummary(LiveBattleActorSnapshot previous, LiveBattleActorSnapshot current)
        {
            List<string> parts = new()
            {
                $"Action {previous.ActionState:X2}h -> {current.ActionState:X2}h",
                $"CTB {current.CurrentCtb}/{current.MaxCtb}",
                previous.CommandExecutionCount != current.CommandExecutionCount
                    ? $"CmdExe {previous.CommandExecutionCount:X2}h -> {current.CommandExecutionCount:X2}h"
                    : $"CmdExe {current.CommandExecutionCount:X2}h",
                $"MoveTarget {current.MoveTarget:X2}h",
                $"TargetList {current.TargetList:X2}h",
                $"EffectTarget {(current.EffectTargetFlag ? "Y" : "N")}",
                $"ProvCmd {(current.ProvCommandFlag ? "Y" : "N")}"
            };

            if (previous.InCtbList != current.InCtbList)
            {
                parts.Add($"CTBList {(current.InCtbList ? "Y" : "N")}");
            }

            if (previous.ScriptChunks != current.ScriptChunks || previous.ScriptData != current.ScriptData)
            {
                parts.Add($"ScrPtr {previous.ScriptChunks:X8}/{previous.ScriptData:X8} -> {current.ScriptChunks:X8}/{current.ScriptData:X8} (watch candidate)");
            }

            return string.Join(" · ", parts);
        }

        static string BuildLastCommandChangeSummary(uint previousCommand, uint currentCommand, List<LiveBattleRuntimeActor> actionCandidates)
        {
            List<string> parts = new()
            {
                $"{FormatLastCommand(previousCommand)} -> {FormatLastCommand(currentCommand)}"
            };

            if (actionCandidates.Count == 1)
            {
                parts.Add($"only candidate in this snapshot {BuildActorCorrelationSummary(actionCandidates[0])}");
            }
            else if (actionCandidates.Count > 1)
            {
                parts.Add($"candidates {BuildActionCandidateListPreview(actionCandidates)}");
            }
            else
            {
                parts.Add("no live action-state candidate in this snapshot");
            }

            return string.Join(" · ", parts);
        }

        static string BuildActionCandidateListPreview(List<LiveBattleRuntimeActor> actionCandidates)
        {
            string preview = string.Join(" / ", actionCandidates
                .Take(3)
                .Select(BuildActorCorrelationSummary));

            if (actionCandidates.Count > 3)
            {
                preview += $" / +{actionCandidates.Count - 3} more";
            }

            return preview;
        }

        static string BuildActorCorrelationSummary(LiveBattleRuntimeActor actor)
        {
            return $"{actor.DisplayLabel} (Act {actor.Chr.Stat_action:X2}h · CTB {actor.Chr.Current_ctb}/{actor.Chr.Max_ctb} · MoveTarget {actor.Chr.Stat_move_target:X2}h · TargetList {actor.Chr.Stat_target_list:X2}h · CmdExe {actor.Chr.Stat_command_exe_count:X2}h · ScrChunks {actor.Chr.Ptr_script_chunks:X8}h · ScrData {actor.Chr.Ptr_script_data:X8}h)";
        }

        static string? BuildBtlObservationChangeSummary(LiveBattleBtlObservationSnapshot previous, LiveBattleBtlObservationSnapshot current)
        {
            List<string> changes = new();

            if (previous.BattleState != current.BattleState)
            {
                changes.Add($"battle_state {previous.BattleState:X2}h -> {current.BattleState:X2}h");
            }

            if (previous.BattleEndType != current.BattleEndType)
            {
                changes.Add($"battle_end_type {previous.BattleEndType:X2}h -> {current.BattleEndType:X2}h");
            }

            if (previous.EncounterType != current.EncounterType)
            {
                changes.Add($"encounter_type {previous.EncounterType:X2}h -> {current.EncounterType:X2}h");
            }

            if (previous.ScreenTransition != current.ScreenTransition)
            {
                changes.Add($"screen_transition {previous.ScreenTransition:X2}h -> {current.ScreenTransition:X2}h");
            }

            if (previous.FieldIdx != current.FieldIdx ||
                previous.GroupIdx != current.GroupIdx ||
                previous.FormationIdx != current.FormationIdx ||
                !string.Equals(previous.FieldName, current.FieldName, StringComparison.Ordinal))
            {
                changes.Add($"route {previous.FieldName}/{previous.FieldIdx:D3}/{previous.GroupIdx:D2}/{previous.FormationIdx:D2} -> {current.FieldName}/{current.FieldIdx:D3}/{current.GroupIdx:D2}/{current.FormationIdx:D2}");
            }

            if (previous.PtrField != current.PtrField ||
                previous.PtrEncounter != current.PtrEncounter ||
                previous.PtrGroup != current.PtrGroup ||
                previous.PtrFormation != current.PtrFormation)
            {
                changes.Add($"cur_ptrs {FormatPointer(previous.PtrField)}/{FormatPointer(previous.PtrEncounter)}/{FormatPointer(previous.PtrGroup)}/{FormatPointer(previous.PtrFormation)} -> {FormatPointer(current.PtrField)}/{FormatPointer(current.PtrEncounter)}/{FormatPointer(current.PtrGroup)}/{FormatPointer(current.PtrFormation)}");
            }

            return changes.Count == 0 ? null : string.Join(" · ", changes);
        }

        static string ResolveRuntimeActorName(bool isAlly, int slot, short rawId, byte[]? nameBytes)
        {
            if (isAlly)
            {
                if (Character_Dictionary.Instance.TryGetValue((sbyte)slot, out string? allyName))
                {
                    return allyName;
                }

                string runtimeName = DecodeRuntimeActorName(nameBytes);
                return string.IsNullOrWhiteSpace(runtimeName) ? $"slot {slot:D2}" : runtimeName;
            }

            if (rawId > 0)
            {
                short dictionaryId = (short)(rawId - 0x1000);
                if (Monster_Dictionary.Instance.TryGetValue(dictionaryId, out string? monsterName))
                {
                    return $"m{dictionaryId:D3} - {monsterName}";
                }
            }

            string fallbackName = DecodeRuntimeActorName(nameBytes);
            if (!string.IsNullOrWhiteSpace(fallbackName))
            {
                return fallbackName;
            }

            return rawId > 0 ? $"{rawId:X4}h" : "<empty>";
        }

        static string DecodeRuntimeActorName(byte[]? nameBytes)
        {
            if (nameBytes == null || nameBytes.Length == 0)
            {
                return string.Empty;
            }

            int length = Array.IndexOf(nameBytes, (byte)0);
            if (length < 0)
            {
                length = nameBytes.Length;
            }

            return Encoding.ASCII.GetString(nameBytes, 0, length).Trim();
        }

        static void AddTimedStatus(List<string> statuses, string label, byte turns)
        {
            if (turns > 0)
            {
                statuses.Add($"{label}({turns})");
            }
        }

        static string JoinPreview(List<string> items, int maxItems)
        {
            if (items.Count <= maxItems)
            {
                return string.Join(" · ", items);
            }

            return string.Join(" · ", items.Take(maxItems)) + $" · +{items.Count - maxItems} more";
        }

        static string FormatLastCommand(uint raw)
        {
            ushort rawGameIndex = (ushort)(raw & 0xFFFF);
            if (TryDescribeRawGameIndex(rawGameIndex, out string description))
            {
                return $"{raw:X8}h · {description}";
            }

            return $"{raw:X8}h";
        }

        static string FormatPointer(uint value)
        {
            return value == 0 ? "-" : $"{value:X8}h";
        }

        MemorySaveData? ReadSaveData()
        {
            byte[] bytes = MemSharp_Service.Instance.Read<byte>(MemoryMap.ADDR_SAVEDATA, MemoryMap.SIZE_SAVEDATA);
            if (bytes == null || bytes.Length < MemoryMap.SIZE_SAVEDATA)
            {
                return null;
            }

            using MemoryStream stream = new(bytes, writable: false);
            return BinaryMapping.ReadObject<MemorySaveData>(stream);
        }

        EncounterListProbeState ReadEncounterListProbeState()
        {
            return new EncounterListProbeState(
                MemSharp_Service.Instance.Read<uint>(AddrEncounterListResource),
                MemSharp_Service.Instance.Read<uint>(AddrEncounterListFieldBase),
                MemSharp_Service.Instance.Read<uint>(AddrEncounterListEncounterBase),
                MemSharp_Service.Instance.Read<ushort>(AddrEncounterListFieldCount),
                MemSharp_Service.Instance.Read<ushort>(AddrEncounterListSceneToken),
                MemSharp_Service.Instance.Read<byte>(AddrEncounterListSceneKind),
                MemSharp_Service.Instance.Read<byte>(AddrEncounterListSceneSubmode),
                MemSharp_Service.Instance.Read<byte>(AddrEncounterListBattleMode));
        }

        string? TryResolveEncounterMapCode(ushort tableId)
        {
            if (!ProjectLoaded)
            {
                return null;
            }

            try
            {
                EnsureEncounterTableLookup();
                if (encounterTableById != null && encounterTableById.TryGetValue(tableId, out EncounterTable_Entry? entry))
                {
                    return entry.Map;
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        void EnsureEncounterTableLookup()
        {
            string? projectPath = Project_Service.Instance.ProjectPath;
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                encounterTableById = null;
                encounterTableProjectPath = null;
                return;
            }

            if (encounterTableById != null &&
                string.Equals(encounterTableProjectPath, projectPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            EncounterTable_File table = EncounterTable_File.Read(File.ReadAllBytes(Project_Service.Instance.Path_KernelEncounterTable));
            encounterTableById = table.Tables.ToDictionary(entry => (ushort)entry.Id);
            encounterTableProjectPath = projectPath;
        }

        static bool IsColdFieldName(string fieldName)
        {
            return string.Equals(fieldName, "-", StringComparison.Ordinal) ||
                   string.Equals(fieldName, "system_0", StringComparison.Ordinal);
        }

        static bool TryDescribeRawGameIndex(ushort rawGameIndex, out string description)
        {
            description = string.Empty;
            try
            {
                byte category = FfxCommon_Util.GetGameCategory(rawGameIndex);
                ushort index = FfxCommon_Util.GetGameIndex(rawGameIndex);
                string name = FfxCommon_Util.GetGameIndexName(category, index);
                if (string.IsNullOrWhiteSpace(name) || name == "<NOT_INDEXED>")
                {
                    return false;
                }

                description = name;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    internal enum LiveBattleRuntimePhaseKind
    {
        ColdContext,
        FieldRuntimeIdle,
        NaturalPrelude,
        EncounterTrigger,
        BattleActive,
        PostBattle
    }

    internal sealed record LiveBattleRuntimePhaseSnapshot(
        LiveBattleRuntimePhaseKind PhaseKind,
        byte BattleActive,
        byte Trigger,
        byte EncounterIndex,
        byte BattleState,
        byte BattleEndType,
        byte EncounterType,
        byte ScreenTransition,
        bool EncounterPreludeReady,
        bool HasObservablePreludeSignal,
        ushort PassiveRouteId,
        string PassiveMapCode,
        ushort FieldIdx,
        byte GroupIdx,
        byte FormationIdx,
        string FieldName,
        string BattleName)
    {
        public string PhaseLabel => PhaseKind switch
        {
            LiveBattleRuntimePhaseKind.ColdContext => "Cold Context",
            LiveBattleRuntimePhaseKind.FieldRuntimeIdle => "Field Runtime Idle",
            LiveBattleRuntimePhaseKind.NaturalPrelude => "Natural Prelude",
            LiveBattleRuntimePhaseKind.EncounterTrigger => "Encounter Trigger",
            LiveBattleRuntimePhaseKind.BattleActive => "Battle Active",
            LiveBattleRuntimePhaseKind.PostBattle => "Post-Battle",
            _ => "Unknown"
        };

        public string EncounterPreludeLabel => EncounterPreludeReady ? "lists online" : "lists offline";

        public string RouteLabel => $"{FieldName}/{FieldIdx:D3}/{GroupIdx:D2}/{FormationIdx:D2}";

        public string PassiveRouteLabel
        {
            get
            {
                if (PassiveRouteId == 0)
                {
                    return "-";
                }

                return string.IsNullOrWhiteSpace(PassiveMapCode) || PassiveMapCode == "-"
                    ? $"{PassiveRouteId} (0x{PassiveRouteId:X4})"
                    : $"{PassiveRouteId} (0x{PassiveRouteId:X4})/{PassiveMapCode}";
            }
        }
    }

    internal sealed record EncounterListProbeState(
        uint ResourcePointer,
        uint FieldBasePointer,
        uint EncounterBasePointer,
        ushort FieldCount,
        ushort SceneToken,
        byte SceneKind,
        byte SceneSubmode,
        byte BattleMode)
    {
        public bool IsReady =>
            ResourcePointer != 0 &&
            FieldBasePointer != 0 &&
            EncounterBasePointer != 0 &&
            FieldCount != 0;

        public bool HasObservablePreludeSignal =>
            SceneToken != 0 ||
            SceneKind != 0 ||
            SceneSubmode != 0 ||
            BattleMode != 0;
    }

    internal sealed record LiveBattleBtlObservationSnapshot(
        byte BattleState,
        byte BattleEndType,
        byte EncounterType,
        byte ScreenTransition,
        ushort FieldIdx,
        byte GroupIdx,
        byte FormationIdx,
        string FieldName,
        uint PtrField,
        uint PtrEncounter,
        uint PtrGroup,
        uint PtrFormation)
    {
        public static LiveBattleBtlObservationSnapshot From(MemoryBtl battleState)
        {
            return new LiveBattleBtlObservationSnapshot(
                battleState.battle_state,
                battleState.battle_end_type,
                battleState.encounter_type,
                battleState.screen_transition,
                battleState.field_idx,
                battleState.group_idx,
                battleState.formation_idx,
                LiveBattleLab_DataModel.DecodeFieldName(battleState.field_name),
                battleState.ptr_btl_bin_cur_field,
                battleState.ptr_btl_bin_cur_encounter,
                battleState.ptr_btl_bin_cur_group,
                battleState.ptr_btl_bin_cur_formation);
        }
    }

    internal sealed class LiveBattleEnemyRow
    {
        public required string SlotLabel { get; init; }
        public required string MonsterLabel { get; init; }
        public required string RawHex { get; init; }
        public required string HpLabel { get; init; }
        public required string StateLabel { get; init; }
        public required string PlacementLabel { get; init; }
        public required string StatusSummary { get; init; }
        public required string TargetSummary { get; init; }
        public required float P1 { get; init; }
        public required float P2 { get; init; }
        public required float P3 { get; init; }
        public required float P4 { get; init; }

        public string P1Label => P1.ToString("0.000");
        public string P2Label => P2.ToString("0.000");
        public string P3Label => P3.ToString("0.000");
        public string P4Label => P4.ToString("0.000");
    }

    internal sealed class LiveBattleAiProbeRow
    {
        public required string SlotLabel { get; init; }
        public required string MonsterLabel { get; init; }
        public required string RuntimeSummary { get; init; }
        public required string TargetSummary { get; init; }
        public required string StatusSummary { get; init; }
        public required string ScriptPointerSummary { get; init; }
        public required string ForcedActionSummary { get; init; }
        public required string AbilityListSummary { get; init; }
        public required string CommandReferenceSummary { get; init; }
        public required string ScriptSummary { get; init; }
        public required string ScriptPreview { get; init; }
    }

    internal sealed class LiveBattleAiEventRow
    {
        public required string SequenceLabel { get; init; }
        public required string EventLabel { get; init; }
        public required string ActorLabel { get; init; }
        public required string DetailLabel { get; init; }
    }

    // One enemy executor row, captured at a single coherent tick, in the exact tuple
    // the Pt6 "schema minimo de captura natural" + Pt47 template demand. Every row
    // carries claim_ceiling so the CSV can never be re-read as more than a watch
    // candidate. accept_label is left blank for the analyst to judge the series.
    internal sealed record LiveBattleNaturalCaptureSample(
        int CaptureTick,
        string Timestamp,
        string Cohort,
        string Origin,
        string Phase,
        string BattleName,
        string FieldName,
        ushort FieldIdx,
        byte GroupIdx,
        byte FormationIdx,
        int ActorSlot,
        short RawMonsterId,
        string MonsterName,
        byte InBattle,
        bool InCtbList,
        bool StatExist,
        int HpCurrent,
        int HpMax,
        int CtbCurrent,
        byte CtbMax,
        byte StatAction,
        uint LastCom,
        string LastComDesc,
        int PtrScriptChunks,
        int PtrScriptData,
        byte StatMoveTarget,
        byte SeckTargetId,
        byte StatTargetList,
        bool StatEffectTargetFlag,
        bool StatProvCommandFlag,
        byte ProvokedById,
        byte ThreatenedById,
        string CorpusLane,
        string RosterSnapshot)
    {
        public const string ClaimCeiling = "structural dispatch/VM watch candidate";

        public static string CsvHeader =>
            "capture_tick,timestamp,cohort,origin,accept_label,phase,battle_name,field_name,field_idx,group_idx,formation_idx," +
            "actor_slot,raw_monster_id,monster_name,in_battle,in_ctb_list,stat_exist,hp_current,hp_max,ctb_current,ctb_max," +
            "stat_action,last_com,last_com_desc,ptr_script_chunks,ptr_script_data,stat_move_target,seck_target_id,stat_target_list," +
            "stat_effect_target_flag,stat_prov_command_flag,provoked_by_id,threatened_by_id,corpus_lane,roster_snapshot,claim_ceiling";

        public string ToCsvLine()
        {
            string[] cells =
            {
                CaptureTick.ToString(CultureInfo.InvariantCulture),
                Timestamp,
                Cohort,
                Origin,
                string.Empty, // accept_label: analyst fills (accept / weak-support / reject / noise)
                Phase,
                BattleName,
                FieldName,
                FieldIdx.ToString(CultureInfo.InvariantCulture),
                GroupIdx.ToString(CultureInfo.InvariantCulture),
                FormationIdx.ToString(CultureInfo.InvariantCulture),
                ActorSlot.ToString(CultureInfo.InvariantCulture),
                $"0x{RawMonsterId:X4}",
                MonsterName,
                InBattle.ToString(CultureInfo.InvariantCulture),
                InCtbList ? "1" : "0",
                StatExist ? "1" : "0",
                HpCurrent.ToString(CultureInfo.InvariantCulture),
                HpMax.ToString(CultureInfo.InvariantCulture),
                CtbCurrent.ToString(CultureInfo.InvariantCulture),
                CtbMax.ToString(CultureInfo.InvariantCulture),
                $"0x{StatAction:X2}",
                $"0x{LastCom:X8}",
                LastComDesc,
                $"0x{PtrScriptChunks:X8}",
                $"0x{PtrScriptData:X8}",
                $"0x{StatMoveTarget:X2}",
                $"0x{SeckTargetId:X2}",
                $"0x{StatTargetList:X2}",
                StatEffectTargetFlag ? "1" : "0",
                StatProvCommandFlag ? "1" : "0",
                $"0x{ProvokedById:X2}",
                $"0x{ThreatenedById:X2}",
                CorpusLane,
                RosterSnapshot,
                ClaimCeiling
            };

            return string.Join(",", cells.Select(EscapeCsv));
        }

        static string EscapeCsv(string value)
        {
            value ??= string.Empty;
            if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }
    }

    internal sealed class LiveBattleRuntimeActor
    {
        public required string Key { get; init; }
        public required bool IsAlly { get; init; }
        public required int Slot { get; init; }
        public required string SlotLabel { get; init; }
        public required string NameLabel { get; init; }
        public required string DisplayLabel { get; init; }
        public required int Address { get; init; }
        public required short RawId { get; init; }
        public required MemoryChr Chr { get; init; }
    }

    internal sealed class LiveBattleActorSnapshot
    {
        public required string Key { get; init; }
        public required string DisplayLabel { get; init; }
        public required byte ActionState { get; init; }
        public required int CurrentCtb { get; init; }
        public required byte MaxCtb { get; init; }
        public required byte MoveTarget { get; init; }
        public required byte TargetList { get; init; }
        public required byte CommandExecutionCount { get; init; }
        public required bool EffectTargetFlag { get; init; }
        public required bool ProvCommandFlag { get; init; }
        public required bool InCtbList { get; init; }
        public required int ScriptChunks { get; init; }
        public required int ScriptData { get; init; }

        public static LiveBattleActorSnapshot From(LiveBattleRuntimeActor actor)
        {
            return new LiveBattleActorSnapshot
            {
                Key = actor.Key,
                DisplayLabel = actor.DisplayLabel,
                ActionState = actor.Chr.Stat_action,
                CurrentCtb = actor.Chr.Current_ctb,
                MaxCtb = actor.Chr.Max_ctb,
                MoveTarget = actor.Chr.Stat_move_target,
                TargetList = actor.Chr.Stat_target_list,
                CommandExecutionCount = actor.Chr.Stat_command_exe_count,
                EffectTargetFlag = actor.Chr.Stat_effect_target_flag,
                ProvCommandFlag = actor.Chr.Stat_prov_command_flag,
                InCtbList = actor.Chr.In_ctb_list,
                ScriptChunks = actor.Chr.Ptr_script_chunks,
                ScriptData = actor.Chr.Ptr_script_data
            };
        }
    }
}

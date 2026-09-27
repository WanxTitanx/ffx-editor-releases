using FFXProjectEditor.Resources;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.FfxLib.Memory;
using FFXProjectEditor.Services;
using FFXProjectEditor.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Timers;
using Xe.BinaryMapper;

namespace FFXProjectEditor.Modules.BattleTracker
{
    internal partial class BattleTracker_DataModel : ObservableObject
    {
        // Settings
        private const byte _enemyCount = 11;
        private const byte _allyCount = 18;
        private const int _timerMilliseconds = 1000;
        [ObservableProperty][NotifyPropertyChangedFor(nameof(AutoRefreshDisabled))][NotifyPropertyChangedFor(nameof(LoadEnabled))] public bool autoRefreshEnabled = false;
        public bool AutoRefreshDisabled => !autoRefreshEnabled;
        // Writing back to the live game (RT2) is opt-in. Read-only is the default.
        public bool LoadEnabled => !autoRefreshEnabled && InBattle && DataLoaded && AllowWrite;
        [ObservableProperty][NotifyPropertyChangedFor(nameof(LoadEnabled))] public bool dataLoaded = false;

        // Honest status surfaced in the UI so the panel is never just mute when empty.
        [ObservableProperty] public string statusText = Strings.U_Bt_Initializing;
        // Explicit RT2 write toggle (default OFF = read-only).
        [ObservableProperty][NotifyPropertyChangedFor(nameof(LoadEnabled))] public bool allowWrite = false;

        // Battle info
        [ObservableProperty][NotifyPropertyChangedFor(nameof(LoadEnabled))] public bool inBattle;
        [ObservableProperty] public string battleName;
        [ObservableProperty] public byte battleIndex;

        // BTL config snapshot (same tiles as Live Battle Lab — read-only)
        [ObservableProperty] public string encounterIndexLabel = "-";
        [ObservableProperty] public string triggerLabel = "-";
        [ObservableProperty] public string fieldLabel = "-";
        [ObservableProperty] public string battlefieldLabel = "-";
        [ObservableProperty] public string groupLabel = "-";
        [ObservableProperty] public string formationLabel = "-";
        [ObservableProperty] public string frontlineLabel = "-";
        [ObservableProperty] public string backlineLabel = "-";
        [ObservableProperty] public string routingCursorSummary = "-";
        [ObservableProperty] public string battleStateLabel = "-";
        [ObservableProperty] public string encounterTypeLabel = "-";
        [ObservableProperty] public string screenTransitionLabel = "-";
        [ObservableProperty] public string lastCommandLabel = "-";
        [ObservableProperty] public string ambushStateLabel = "-";
        [ObservableProperty] public string battleTypeLabel = "-";
        [ObservableProperty] public string battleEndTypeLabel = "-";

        // Enemy and Ally lists
        internal List<sbyte> AlliesInBattle = new();
        internal List<MemoryChr> EnemyChrs = new();
        internal List<MemoryChr> AllyChrs = new();
        internal ObservableCollection<BattleTrackerChr_Wrapper> EnemyList { get; set; } = new();
        internal ObservableCollection<BattleTrackerChr_Wrapper> AllyList { get; set; } = new();
        internal ObservableCollection<BattleTrackerChr_Wrapper> DisplayAllyList { get; set; } = new();
        internal ObservableCollection<BattleTrackerChr_Wrapper> DisplayEnemyList { get; set; } = new();

        // Loaded enemy and ally
        internal ContentControl FrameEnemy { get; set; }
        internal BattleTrackerChr_Wrapper? LoadedEnemy { get; set; }
        internal ContentControl FrameAlly { get; set; }
        internal BattleTrackerChr_Wrapper? LoadedAlly { get; set; }
        [ObservableProperty] private BattleTrackerChr_Wrapper? selectedAlly;
        [ObservableProperty] private BattleTrackerChr_Wrapper? selectedEnemy;

        public BattleTracker_DataModel(ContentControl frameAlly, ContentControl frameEnemy)
        {
            FrameAlly = frameAlly;
            FrameEnemy = frameEnemy;

            for (int i = 0; i < _allyCount; i++)
            {
                AllyList.Add(new BattleTrackerChr_Wrapper());
            }
            for (int i = 0; i < _enemyCount; i++)
            {
                EnemyList.Add(new BattleTrackerChr_Wrapper());
            }

            // Populate immediately on open (mirrors the other live trackers) instead of
            // sitting blank until the user clicks Refresh.
            ReadInfo();
        }

        public void ReadInfo()
        {
            if (!MemSharp_Service.Instance.IsAvailable())
            {
                InBattle = false;
                DataLoaded = false;
                ClearData();
                UpdateStatus();
                return;
            }

            try
            {
                ReadInBattle();

                if (InBattle)
                {
                    ReadBattleData();

                    ReadAllyChrs();
                    ReadEnemyChrs();
                    UpdateAllyList();
                    UpdateEnemyList();
                    LoadDisplayLists();
                    DataLoaded = true;
                }
                else
                {
                    DataLoaded = false;
                    ClearData();
                }
            }
            catch (Exception ex)
            {
                DataLoaded = false;
                StatusText = "Erro ao ler a batalha: " + ex.Message;
                return;
            }

            UpdateStatus();
        }

        public void ReadInBattle()
        {
            InBattle = (MemSharp_Service.Instance.Read<byte>(MemoryMap.ADDR_BATTLE_ACTIVE) == 1);
        }

        // Cheap, never-throwing read of just the battle-active flag (used by the status poll).
        private void ReadInBattleSafe()
        {
            try
            {
                InBattle = MemSharp_Service.Instance.IsAvailable()
                    && MemSharp_Service.Instance.Read<byte>(MemoryMap.ADDR_BATTLE_ACTIVE) == 1;
            }
            catch
            {
                InBattle = false;
            }
        }

        // Always-honest one-liner so the panel is never just two mute frames.
        private void UpdateStatus()
        {
            if (!OperatingSystem.IsWindows())
            {
                StatusText = Strings.U_Bt_MemoryWindowsOnly;
                return;
            }
            if (!Process_Service.Instance.IsAlive)
            {
                StatusText = Strings.U_Bt_ExeNotFound;
                return;
            }
            if (!MemSharp_Service.Instance.IsAvailable())
            {
                StatusText = Strings.U_Bt_AttachFailed;
                return;
            }
            if (!InBattle)
            {
                StatusText = Strings.U_Bt_AttachedNoBattle;
                return;
            }

            int enemies = DisplayEnemyList?.Count ?? 0;
            int allies = DisplayAllyList?.Count ?? 0;
            string name = string.IsNullOrWhiteSpace(BattleName) ? "?" : BattleName;
            StatusText = string.Format(Strings.U_Bt_InBattle, name, enemies, allies)
                + (AllowWrite ? Strings.U_Bt_WriteMode : Strings.U_Bt_ReadOnlyMode);
        }
        public void ReadBattleData()
        {
            BattleName = (MemSharp_Service.Instance.ReadString(MemoryMap.ADDR_BATTLE_NAME, 13));
            BattleIndex = (MemSharp_Service.Instance.Read<byte>(MemoryMap.ADDR_BATTLE_ENCOUNTER_INDEX));
            ApplyBattleSnapshot(BattleTracker_BattleSnapshot.Read());
        }

        void ApplyBattleSnapshot(BattleTracker_BattleSnapshot snapshot)
        {
            EncounterIndexLabel = snapshot.EncounterIndexLabel;
            TriggerLabel = snapshot.TriggerLabel;
            FieldLabel = snapshot.FieldLabel;
            BattlefieldLabel = snapshot.BattlefieldLabel;
            GroupLabel = snapshot.GroupLabel;
            FormationLabel = snapshot.FormationLabel;
            FrontlineLabel = snapshot.FrontlineLabel;
            BacklineLabel = snapshot.BacklineLabel;
            RoutingCursorSummary = snapshot.RoutingCursorSummary;
            BattleStateLabel = snapshot.BattleStateLabel;
            EncounterTypeLabel = snapshot.EncounterTypeLabel;
            ScreenTransitionLabel = snapshot.ScreenTransitionLabel;
            LastCommandLabel = snapshot.LastCommandLabel;
            AmbushStateLabel = snapshot.AmbushStateLabel;
            BattleTypeLabel = snapshot.BattleTypeLabel;
            BattleEndTypeLabel = snapshot.BattleEndTypeLabel;
        }

        void ClearBattleSnapshot() => ApplyBattleSnapshot(BattleTracker_BattleSnapshot.Empty);
        public void ClearData()
        {
            BattleName = "";
            BattleIndex = 0;
            ClearBattleSnapshot();
            DisplayAllyList.Clear();
            DisplayEnemyList.Clear();
            LoadedAlly = null;
            LoadedEnemy = null;
            SelectedAlly = null;
            SelectedEnemy = null;

            FrameAlly.Content = null;
            FrameEnemy.Content = null;
        }

        private void ReadAllyChrs()
        {
            AllyChrs.Clear();

            int allyListAddress = MemSharp_Service.Instance.Read<int>(MemoryMap.POINTER_BATTLE_PLAYER_LIST);
            if (allyListAddress == 0)
                throw new InvalidOperationException("ponteiro da lista de aliados = 0");

            for (int i = 0; i < _allyCount; i++)
            {
                int chrAddress = allyListAddress + i * MemoryMap.SIZE_BATTLE_CHR_ENTRY;

                byte[] chrBytes = MemSharp_Service.Instance.Read<byte>(chrAddress, MemoryMap.SIZE_BATTLE_CHR_ENTRY, false);
                if (chrBytes == null)
                    throw new InvalidOperationException("falha ao ler o aliado " + i);
                MemoryChr thisChr;
                using (MemoryStream stream = new MemoryStream(chrBytes))
                {
                    thisChr = BinaryMapping.ReadObject<MemoryChr>(stream);
                }
                AllyChrs.Add(thisChr);
            }
        }
        private void ReadEnemyChrs()
        {
            EnemyChrs.Clear();

            int enemyListAddress = MemSharp_Service.Instance.Read<int>(MemoryMap.POINTER_BATTLE_ENEMY_LIST);
            if (enemyListAddress == 0)
                throw new InvalidOperationException("ponteiro da lista de inimigos = 0");

            for (int i = 0; i < _enemyCount; i++)
            {
                int chrAddress = enemyListAddress + i * MemoryMap.SIZE_BATTLE_CHR_ENTRY;

                byte[] chrBytes = MemSharp_Service.Instance.Read<byte>(chrAddress, MemoryMap.SIZE_BATTLE_CHR_ENTRY, false);
                if (chrBytes == null)
                    throw new InvalidOperationException("falha ao ler o inimigo " + i);
                MemoryChr thisChr;
                using (MemoryStream stream = new MemoryStream(chrBytes))
                {
                    thisChr = BinaryMapping.ReadObject<MemoryChr>(stream);
                }
                EnemyChrs.Add(thisChr);
            }
        }

        private void UpdateAllyList()
        {
            for (byte i = 0; i < _allyCount; i++)
            {
                MemoryChr thisChr = AllyChrs[i];
                BattleTrackerChr_Wrapper thisWrapper = BattleTrackerChr_Wrapper.Wrap(thisChr);
                thisWrapper.DictionaryId = i;
                thisWrapper.DictionaryName = (Character_Dictionary.Instance.ContainsKey((sbyte)thisWrapper.DictionaryId)) ? Character_Dictionary.Instance[(sbyte)thisWrapper.DictionaryId] : "-";

                PropertyUtil.CopyProperties(thisWrapper, AllyList[i]);
            }
        }
        private void UpdateEnemyList()
        {
            for (int i = 0; i < _enemyCount; i++)
            {
                MemoryChr thisChr = EnemyChrs[i];
                BattleTrackerChr_Wrapper thisWrapper = BattleTrackerChr_Wrapper.Wrap(thisChr);
                thisWrapper.DictionaryId = (short)(thisChr.Id - 0x1000);
                thisWrapper.DictionaryName = (Monster_Dictionary.Instance.ContainsKey(thisWrapper.DictionaryId)) ? Monster_Dictionary.Instance[thisWrapper.DictionaryId] : "-";

                PropertyUtil.CopyProperties(thisWrapper, EnemyList[i]);
            }
        }

        private void LoadDisplayLists()
        {
            DisplayAllyList.Clear();
            DisplayEnemyList.Clear();

            // Allies
            ReadAlliesInBattle();
            foreach (sbyte allyId in AlliesInBattle)
            {
                if (allyId >= 0 && allyId < AllyList.Count)
                {
                    DisplayAllyList.Add(AllyList[allyId]);
                }
            }
            foreach (BattleTrackerChr_Wrapper thisWrapper in AllyList)
            {
                if (!AlliesInBattle.Contains((sbyte)thisWrapper.Id))
                {
                    DisplayAllyList.Add(thisWrapper);
                }
            }

            // Enemies
            foreach(BattleTrackerChr_Wrapper thisWrapper in EnemyList)
            {
                if (thisWrapper.DictionaryId > 0)
                {
                    DisplayEnemyList.Add(thisWrapper);
                }
            }

            EnsureDefaultSelection();
        }

        private void EnsureDefaultSelection()
        {
            if (SelectedAlly == null || !DisplayAllyList.Contains(SelectedAlly))
                SelectedAlly = DisplayAllyList.Count > 0 ? DisplayAllyList[0] : null;

            if (SelectedEnemy == null || !DisplayEnemyList.Contains(SelectedEnemy))
                SelectedEnemy = DisplayEnemyList.Count > 0 ? DisplayEnemyList[0] : null;
        }

        partial void OnSelectedAllyChanged(BattleTrackerChr_Wrapper? value)
        {
            if (value != null)
                LoadAlly(value);
        }

        partial void OnSelectedEnemyChanged(BattleTrackerChr_Wrapper? value)
        {
            if (value != null)
                LoadEnemy(value);
        }

        private void ReadAlliesInBattle()
        {
            AlliesInBattle.Clear();
        
            for(int i = 0; i < 3; i++)
            {
                AlliesInBattle.Add(MemSharp_Service.Instance.Read<sbyte>(MemoryMap.ADDR_BATTLE_FORMATION_SLOTS + i));
            }
        }

        public void LoadAlly(BattleTrackerChr_Wrapper? chrWrapper)
        {
            LoadedAlly = chrWrapper;
            FrameAlly.Content = chrWrapper == null ? null : new BattleTrackerChr_Control(chrWrapper);
        }
        public void LoadEnemy(BattleTrackerChr_Wrapper? chrWrapper)
        {
            LoadedEnemy = chrWrapper;
            FrameEnemy.Content = chrWrapper == null ? null : new BattleTrackerChr_Control(chrWrapper);
        }

        public void LoadIngame()
        {
            if (autoRefreshEnabled || !InBattle || !DataLoaded)
                return;

            int allyListAddress = MemSharp_Service.Instance.Read<int>(MemoryMap.POINTER_BATTLE_PLAYER_LIST);
            for (byte i = 0; i < _allyCount; i++)
            {
                int chrAddress = allyListAddress + i * MemoryMap.SIZE_BATTLE_CHR_ENTRY;
                byte[] chrBytes = MemSharp_Service.Instance.Read<byte>(chrAddress, MemoryMap.SIZE_BATTLE_CHR_ENTRY, false);

                MemoryChr thisChr = AllyChrs[i];
                BattleTrackerChr_Wrapper thisWrapper = AllyList[i];
                PropertyUtil.CopyProperties(thisWrapper, thisChr);

                using (MemoryStream stream = new MemoryStream(chrBytes))
                {
                    BinaryMapping.WriteObject(stream, thisChr, 0);
                }

                MemSharp_Service.Instance.Write(chrAddress, chrBytes, false);
            }

            int enemyListAddress = MemSharp_Service.Instance.Read<int>(MemoryMap.POINTER_BATTLE_ENEMY_LIST);
            for (byte i = 0; i < _enemyCount; i++)
            {
                int chrAddress = enemyListAddress + i * MemoryMap.SIZE_BATTLE_CHR_ENTRY;
                byte[] chrBytes = MemSharp_Service.Instance.Read<byte>(chrAddress, MemoryMap.SIZE_BATTLE_CHR_ENTRY, false);

                MemoryChr thisChr = EnemyChrs[i];
                BattleTrackerChr_Wrapper thisWrapper = EnemyList[i];
                PropertyUtil.CopyProperties(thisWrapper, thisChr);

                using (MemoryStream stream = new MemoryStream(chrBytes))
                {
                    BinaryMapping.WriteObject(stream, thisChr, 0);
                }

                MemSharp_Service.Instance.Write(chrAddress, chrBytes, false);
            }
        }

        /******************************************
         * REFRESH TIMER
         ******************************************/
        private Timer _timer = new Timer();
        public void StartReading()
        {
            _timer = new Timer(_timerMilliseconds); // 1-second interval
            _timer.Elapsed += OnTimerElapsed;
            _timer.AutoReset = true; // Keep running periodically
            _timer.Start();
        }

        public void StopReading()
        {
            if (_timer != null)
            {
                _timer.Elapsed -= OnTimerElapsed; // Unsubscribe event to avoid memory leaks
                _timer.Stop();
                _timer.Dispose();
                _timer = null;
            }
        }

        private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            try
            {
                // Enforce the method to run on the UI thread to access the ContentFrames
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        if (AutoRefreshEnabled)
                        {
                            // Continuous live refresh.
                            ReadInfo();
                        }
                        else
                        {
                            // Keep the status honest every tick and auto-load the FIRST snapshot
                            // when a battle begins, so the user never stares at an empty panel.
                            ReadInBattleSafe();
                            if (InBattle && !DataLoaded)
                            {
                                ReadInfo();
                            }
                            else if (!InBattle && DataLoaded)
                            {
                                DataLoaded = false;
                                ClearData();
                                UpdateStatus();
                            }
                            else
                            {
                                UpdateStatus();
                            }
                        }
                    }
                    catch (Exception ex) { }
                });
            }
            catch (Exception ex) { }
        }
    }
}

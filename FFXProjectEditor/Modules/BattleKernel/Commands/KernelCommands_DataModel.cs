using FFXProjectEditor.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFXProjectEditor.Converters;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Audio;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.FfxLib.Memory;
using FFXProjectEditor.FfxLib.Ps3;
using FFXProjectEditor.Services;
using FFXProjectEditor.Services.Extras;
using FFXProjectEditor.Utils.Editing;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using NAudio.Wave;

namespace FFXProjectEditor.Modules.BattleKernel.Commands
{
    internal partial class KernelCommands_DataModel : ObservableObject
    {
        public Process_Service ProcService => Process_Service.Instance;

        CommandFile_enum CommandFileType { get; }
        List<Ability_Command> CommandsList { get; set; } = new();
        List<KernelCommands_Wrapper> LoadedCommands { get; } = new();
        public ObservableCollection<KernelCommands_Wrapper> DisplayedCommands { get; } = new();
        public ObservableCollection<KernelCommandReferenceIndex_Service.CommandReferenceRow> SelectedCommandReferences { get; } = new();

        [ObservableProperty] private ByteSnapshotEditorSession editSession;
        [ObservableProperty] private KernelCommands_Wrapper? selectedCommand;
        [ObservableProperty] private KernelCommandReferenceIndex_Service.CommandReferenceRow? selectedCommandReference;
        [ObservableProperty] private string selectedCommandSummary = "Pick a command to inspect where the game links it.";
        [ObservableProperty] private string selectedCommandReferenceSummary = "Where-used panel offline until a command is selected.";
        [ObservableProperty] private string selectedCommandReferenceNotes = "Jarvis is currently indexing monster stat sheets, monster loot, and the loaded monster AI corpus.";
        [ObservableProperty] private string anim1EffectSummary = "Anim 1: select a command.";
        [ObservableProperty] private string anim2EffectSummary = "Anim 2: select a command.";
        [ObservableProperty] private bool canViewAnim1Effect;
        [ObservableProperty] private bool canViewAnim2Effect;
        [ObservableProperty] private bool anim1EffectAvailable;
        [ObservableProperty] private bool anim2EffectAvailable;
        [ObservableProperty] private string? anim1EffectFolderPath;
        [ObservableProperty] private string? anim2EffectFolderPath;
        [ObservableProperty] private string battleSfxSummary = "Battle SFX: select a command.";
        [ObservableProperty] private string? battleSfxSeIdDisplay;
        [ObservableProperty] private string? battleSfxWaveIdDisplay;
        [ObservableProperty] private string? battleSfxMagicIdDisplay;
        [ObservableProperty] private string battleSfxRt2Status = "";
        [ObservableProperty] private string battleSfxActionStatus = "";
        [ObservableProperty] private bool battleSfxCorpusAvailable;
        [ObservableProperty] private bool canEditBattleSfx;
        [ObservableProperty] private CommandSoundCorpusLoader.DonorOption? selectedBattleSfxDonor;
        [ObservableProperty] private int battleSfxRecordIndex;
        [ObservableProperty] private bool battleSfxCustomToolsReady;
        [ObservableProperty] private bool battleSfxCustomToolsMissing = true;
        [ObservableProperty] private bool battleSfxFsbankClReady;
        [ObservableProperty] private bool battleSfxFsbankClMissing = true;
        [ObservableProperty] private string battleSfxAudioToolsHealth = "";
        [ObservableProperty] private string? battleSfxCustomWavPath;
        [ObservableProperty] private int battleSfxFsbSampleIndex;
        [ObservableProperty] private string battleSfxCustomValidation = "";
        [ObservableProperty] private string battleSfxCustomStatus = "";
        [ObservableProperty] private string battleSfxSfxLocale = "US";
        [ObservableProperty] private bool battleSfxPatchDllForCustom = true;
        [ObservableProperty] private string battleSfxNewSeIdGaps = "";
        [ObservableProperty] private string battleSfxMapStatus = "";
        [ObservableProperty] private uint? battleSfxSelectedNewSeIdGap;
        [ObservableProperty] private int battleSfxNewSeIdGapValue = 8010;
        [ObservableProperty] private bool battleSfxMirrorJpLocale;
        [ObservableProperty] private string battleSfxNewSeIdStatus = "";

        WaveOutEvent? _customSfxPreviewOut;
        AudioFileReader? _customSfxPreviewReader;

        public ObservableCollection<CommandSoundCorpusLoader.DonorOption> BattleSfxDonorOptions { get; } = new();
        public ObservableCollection<CommandSoundCorpusLoader.SeIdReverseRow> BattleSfxSeIdIndex { get; } = new();

        [ObservableProperty] public bool showDescription = true;
        [ObservableProperty] public bool showAnimations = false;
        [ObservableProperty] public bool showMenu = false;
        [ObservableProperty] public bool showCharacters = false;
        [ObservableProperty] public bool showProperties = true;
        [ObservableProperty] public bool showCosts = false;
        [ObservableProperty] public bool showAttackData = false;
        [ObservableProperty] public bool showElement = false;
        [ObservableProperty] public bool showStatus = false;
        [ObservableProperty] public bool showStatusSpecial = false;
        [ObservableProperty] public bool showBuffs = false;
        [ObservableProperty] public bool showExtra = false;
        [ObservableProperty] private string filterText = string.Empty;
        [ObservableProperty] private string? loadError;

        public List<string> CharacterOptions => new Character_Converter().Options.Values.ToList();
        public List<string> HitCalcTypeOptions => new HitCalcType_Converter().Options.Values.ToList();
        public List<string> DamageFormulaOptions => new DamageFormula_Converter().Options.Values.ToList();

        public bool IsExtraEnabled => HasExtraInfo();
        public bool ShowItemIcons => CommandFileType == CommandFile_enum.Item;
        public bool HasSelectedCommandReferences => SelectedCommandReferences.Count > 0;
        public bool IsMonMagic2Editor => CommandFileType == CommandFile_enum.MonMagic2;
        public bool IsCommandListMutable => CommandFileType != CommandFile_enum.Item;
        public bool CanCloneSelectedCommand => IsCommandListMutable && SelectedCommand != null && string.IsNullOrEmpty(LoadError);
        public bool CanDeleteSelectedCommand => IsCommandListMutable && SelectedCommand != null && LoadedCommands.Count > 1 && string.IsNullOrEmpty(LoadError);
        public bool CanAddCommand => IsCommandListMutable && LoadedCommands.Count > 0 && string.IsNullOrEmpty(LoadError);

        // === Jarvis-UI Phase v2.190.2.0: hero stats + empty state ===
        public string CommandScope => GetCategoryLabel();
        public int TotalCount => LoadedCommands.Count;
        public int FilteredCount => DisplayedCommands.Count;
        public bool HasSelectedCommand => SelectedCommand != null;

        public CommandFile_enum CurrentCommandFileType => CommandFileType;

        public KernelCommands_DataModel(CommandFile_enum commandFileType)
        {
            CommandFileType = commandFileType;
            DisplayedCommands.CollectionChanged += (_, _) => OnPropertyChanged(nameof(FilteredCount));

            try
            {
                LoadCommandsFromBytes(File.ReadAllBytes(GetFilePath()));
            }
            catch (System.Exception ex)
            {
                // A missing/unreadable kernel file must NOT take the whole editor down (it used to:
                // this ctor threw straight through the module-open click). Surface a clear message,
                // log the stack, and leave the list empty so the user can fix the workspace + reopen.
                string path;
                try { path = GetFilePath(); } catch { path = "(unresolved)"; }
                Utils.CrashLog.Write($"KernelCommands load failed for {commandFileType} at '{path}'", ex);
                LoadError =
                    string.Format(Strings.U_Kc_LoadFailed, Path.GetFileName(path)) +
                    string.Format(Strings.U_Kc_LoadExpectedAt, path) +
                    $"{ex.GetType().Name}: {ex.Message}\n" +
                    Strings.U_Kc_LoadHint;
            }

            EditSession = new ByteSnapshotEditorSession(
                BuildFile,
                RestoreFromBytes,
                PersistBytes,
                GetDomainLabel(),
                BuildFile());

            RefreshBattleSfxCorpusCatalog();
            RefreshBattleSfxCustomToolsState();
        }

        void RefreshBattleSfxCustomToolsState()
        {
            BattleSfxCustomToolsReady = FfxAudioToolsLocator.CustomSfxToolsReady;
            BattleSfxCustomToolsMissing = !BattleSfxCustomToolsReady;
            BattleSfxFsbankClReady = FfxAudioToolsLocator.FsbankClAvailable;
            BattleSfxFsbankClMissing = !BattleSfxFsbankClReady;

            FfxAudioToolsHealth_Service.HealthReport health = FfxAudioToolsHealth_Service.Probe();
            BattleSfxAudioToolsHealth = health.Summary;
            BattleSfxMapStatus = SeidFsbMapLoader.DescribeMapStatus();

            if (BattleSfxCustomToolsReady)
                BattleSfxCustomStatus = FfxAudioToolsLocator.BundledToolsStatus;
            if (!SeidFsbMapLoader.IsMapAvailable())
                BattleSfxNewSeIdGaps = "FSB sample map missing — reinstall editor.";
            else
            {
                string? fev = TryResolveFevPath();
                if (fev != null && File.Exists(fev))
                {
                    var gaps = FevLegacyReader.FindSeIdGaps(
                        CommandSoundCorpusLoader.GetSeIdReverseIndex().Select(r => r.SeId).ToList(),
                        fev);
                    BattleSfxNewSeIdGaps = gaps.Count == 0
                        ? "No obvious seId gaps in scan range."
                        : $"Phase 2 gaps: {string.Join(", ", gaps.Take(12))} …";
                    if (gaps.Count > 0)
                    {
                        BattleSfxSelectedNewSeIdGap = gaps[0];
                        BattleSfxNewSeIdGapValue = (int)gaps[0];
                    }
                }
            }
        }

        void RefreshBattleSfxCorpusCatalog()
        {
            BattleSfxCorpusAvailable = CommandSoundCorpusLoader.IsCorpusAvailable();
            BattleSfxRt2Status = CommandSoundCorpusLoader.ReadRt2Status();
            BattleSfxDonorOptions.Clear();
            BattleSfxSeIdIndex.Clear();
            foreach (CommandSoundCorpusLoader.DonorOption donor in CommandSoundCorpusLoader.GetDonorOptions())
                BattleSfxDonorOptions.Add(donor);
            foreach (CommandSoundCorpusLoader.SeIdReverseRow row in CommandSoundCorpusLoader.GetSeIdReverseIndex())
                BattleSfxSeIdIndex.Add(row);
        }

        public void CloneSelectedCommand()
        {
            if (SelectedCommand == null)
                return;

            int donorIndex = SelectedCommand.Index;
            ApplyListMutation(list => KernelCommandListMutator.AppendClone(list, donorIndex, HasExtraInfo()));
            SelectCommandByIndex(LoadedCommands.Count - 1);
        }

        public void AddCommandFromZero()
        {
            ApplyListMutation(list => KernelCommandListMutator.AppendFromEntryZero(list, HasExtraInfo()));
            SelectCommandByIndex(LoadedCommands.Count - 1);
        }

        public void DeleteSelectedCommand()
        {
            if (SelectedCommand == null)
                return;

            int removedIndex = SelectedCommand.Index;
            ApplyListMutation(list => KernelCommandListMutator.RemoveAt(list, removedIndex));
            int nextIndex = Math.Min(removedIndex, LoadedCommands.Count - 1);
            SelectCommandByIndex(nextIndex);
        }

        void ApplyListMutation(Action<List<Ability_Command>> mutate)
        {
            List<Ability_Command> list = LoadedCommands.Select(w => w.Unwrap()).ToList();
            mutate(list);
            ReloadWrappersFromCommandList(list);
            EditSession.NotifyPotentialMutation();
            RefreshCanMutateCommands();
        }

        void ReloadWrappersFromCommandList(List<Ability_Command> list)
        {
            UnsubscribeCommandGraph();
            LoadedCommands.Clear();
            CommandsList = list;

            for (int i = 0; i < list.Count; i++)
            {
                KernelCommands_Wrapper wrapper = KernelCommands_Wrapper.Wrap(list[i]);
                wrapper.Index = i;
                wrapper.UsesItemIcon = CommandFileType == CommandFile_enum.Item;
                LoadedCommands.Add(wrapper);
            }

            SubscribeCommandGraph();
            ApplyFilter();
            OnPropertyChanged(nameof(TotalCount));
        }

        void SelectCommandByIndex(int index)
        {
            KernelCommands_Wrapper? target = LoadedCommands.FirstOrDefault(c => c.Index == index)
                ?? LoadedCommands.LastOrDefault();
            SelectedCommand = target;
        }

        void RefreshCanMutateCommands()
        {
            OnPropertyChanged(nameof(CanCloneSelectedCommand));
            OnPropertyChanged(nameof(CanDeleteSelectedCommand));
            OnPropertyChanged(nameof(CanAddCommand));
        }

        public void ApplyFilter()
        {
            DisplayedCommands.Clear();
            string normalizedFilter = FilterText.Trim().ToLowerInvariant();

            foreach (KernelCommands_Wrapper command in LoadedCommands)
            {
                if (normalizedFilter.Length == 0 ||
                    command.Index.ToString().Contains(normalizedFilter) ||
                    command.Name.ToLower().Contains(normalizedFilter) ||
                    command.Description.ToLower().Contains(normalizedFilter))
                {
                    DisplayedCommands.Add(command);
                }
            }

            if (SelectedCommand == null || !DisplayedCommands.Contains(SelectedCommand))
            {
                SelectedCommand = DisplayedCommands.FirstOrDefault();
            }
        }

        public void Save() => EditSession.Save();
        public void Undo() => EditSession.Undo();
        public void Discard() => EditSession.Discard();

        public void LoadInGame()
        {
            int fileAddress;
            if (CommandFileType == CommandFile_enum.Item)
            {
                fileAddress = MemSharp_Service.Instance.Read<int>(MemoryMap.POINTER_FILE_ITEM);
            }
            else if (CommandFileType == CommandFile_enum.Command)
            {
                fileAddress = MemSharp_Service.Instance.Read<int>(MemoryMap.POINTER_FILE_COMMAND);
            }
            else if (CommandFileType == CommandFile_enum.MonMagic1)
            {
                fileAddress = MemSharp_Service.Instance.Read<int>(MemoryMap.POINTER_FILE_MONMAGIC1);
            }
            else if (CommandFileType == CommandFile_enum.MonMagic2)
            {
                fileAddress = MemSharp_Service.Instance.Read<int>(MemoryMap.POINTER_FILE_MONMAGIC2);
            }
            else return;

            MemSharp_Service.Instance.Write(fileAddress, BuildFile(), false);
        }

        byte[] BuildFile()
        {
            List<Ability_Command> commandList = new();
            foreach (KernelCommands_Wrapper wrapper in LoadedCommands)
            {
                commandList.Add(wrapper.Unwrap());
            }

            return Ability_Command.WriteList(commandList, HasExtraInfo());
        }

        void RestoreFromBytes(byte[] bytes)
        {
            LoadCommandsFromBytes(bytes);
        }

        void PersistBytes(byte[] bytes)
        {
            File.WriteAllBytes(GetFilePath(), bytes);
            CommandsList = Ability_Command.ReadList(bytes, HasExtraInfo());
            if (CommandFileType == CommandFile_enum.Item)
                ItemIcon_Service.Invalidate();
            if (CommandFileType is CommandFile_enum.MonMagic1 or CommandFile_enum.MonMagic2)
                KernelMonsterMagicLiveSync.SyncBytes(CommandFileType, bytes);
        }

        void LoadCommandsFromBytes(byte[] byteFile)
        {
            UnsubscribeCommandGraph();
            LoadedCommands.Clear();
            DisplayedCommands.Clear();

            CommandsList = Ability_Command.ReadList(byteFile, HasExtraInfo());

            for (int i = 0; i < CommandsList.Count; i++)
            {
                KernelCommands_Wrapper wrapper = KernelCommands_Wrapper.Wrap(CommandsList[i]);
                wrapper.Index = i;
                wrapper.UsesItemIcon = CommandFileType == CommandFile_enum.Item;
                LoadedCommands.Add(wrapper);
            }

            SubscribeCommandGraph();
            ApplyFilter();
            RefreshCanMutateCommands();

            if (CommandFileType is CommandFile_enum.MonMagic1 or CommandFile_enum.MonMagic2)
                KernelMonsterMagicLiveSync.SyncBytes(CommandFileType, byteFile);
        }

        partial void OnSelectedCommandChanged(KernelCommands_Wrapper? value)
        {
            OnPropertyChanged(nameof(HasSelectedCommand));

            foreach (KernelCommands_Wrapper wrapper in LoadedCommands)
            {
                wrapper.ShowSelectionMarker = ReferenceEquals(wrapper, value);
            }

            RefreshCanMutateCommands();
            RefreshSelectedCommandReferences();
            RefreshSelectedCommandAnimationLinks();
            RefreshSelectedCommandBattleSfx();
        }

        void RefreshSelectedCommandReferences()
        {
            SelectedCommandReferences.Clear();

            if (SelectedCommand == null)
            {
                SelectedCommandReference = null;
                SelectedCommandSummary = "Pick a command to inspect where the game links it.";
                SelectedCommandReferenceSummary = "Where-used panel offline until a command is selected.";
                SelectedCommandReferenceNotes = "Jarvis is currently indexing monster stat sheets, monster loot, and the loaded monster AI corpus.";
                OnPropertyChanged(nameof(HasSelectedCommandReferences));
                return;
            }

            ushort rawGameIndex = BuildRawGameIndex(SelectedCommand.Index);
            SelectedCommandSummary = $"{SelectedCommand.Name} · {GetCategoryLabel()} #{SelectedCommand.Index} · {rawGameIndex:X4}h";

            foreach (KernelCommandReferenceIndex_Service.CommandReferenceRow reference in KernelCommandReferenceIndex_Service.GetReferences(rawGameIndex))
            {
                SelectedCommandReferences.Add(reference);
            }

            SelectedCommandReferenceSummary = SelectedCommandReferences.Count == 0
                ? "No monster-side references found in the current index."
                : $"{SelectedCommandReferences.Count} reference(s) found across monsters, loot, and AI.";

            SelectedCommandReferenceNotes = KernelCommandReferenceIndex_Service.StatusSummary;
            SelectedCommandReference = SelectedCommandReferences.FirstOrDefault();
            OnPropertyChanged(nameof(HasSelectedCommandReferences));
        }

        void RefreshSelectedCommandAnimationLinks()
        {
            if (SelectedCommand == null)
            {
                Anim1EffectSummary = "Anim 1: select a command.";
                Anim2EffectSummary = "Anim 2: select a command.";
                CanViewAnim1Effect = false;
                CanViewAnim2Effect = false;
                Anim1EffectAvailable = false;
                Anim2EffectAvailable = false;
                Anim1EffectFolderPath = null;
                Anim2EffectFolderPath = null;
                BattleSfxSummary = "Battle SFX: select a command.";
                BattleSfxSeIdDisplay = null;
                BattleSfxWaveIdDisplay = null;
                BattleSfxMagicIdDisplay = null;
                return;
            }

            Anim1EffectSummary = BuildAnimationEffectSummary("Anim 1", SelectedCommand.Anim1Id, out bool canView1, out bool available1, out string? folder1);
            Anim2EffectSummary = BuildAnimationEffectSummary("Anim 2", SelectedCommand.Anim2Id, out bool canView2, out bool available2, out string? folder2);
            CanViewAnim1Effect = canView1;
            CanViewAnim2Effect = canView2;
            Anim1EffectAvailable = available1;
            Anim2EffectAvailable = available2;
            Anim1EffectFolderPath = folder1;
            Anim2EffectFolderPath = folder2;
            RefreshSelectedCommandBattleSfx();
        }

        void RefreshSelectedCommandBattleSfx()
        {
            if (SelectedCommand == null)
            {
                BattleSfxSummary = "Battle SFX: select a command.";
                BattleSfxSeIdDisplay = null;
                BattleSfxWaveIdDisplay = null;
                BattleSfxMagicIdDisplay = null;
                return;
            }

            short anim = SelectedCommand.Anim1Id >= 0 ? SelectedCommand.Anim1Id : SelectedCommand.Anim2Id;
            BattleSfxMagicIdDisplay = anim >= 0 ? $"magic_{anim:D4}" : null;

            CommandSoundCorpusLoader.CommandSoundInfo? corpus =
                CommandSoundCorpusLoader.TryGetForCommand(SelectedCommand.Index)
                ?? (anim >= 0 ? CommandSoundCorpusLoader.TryGetForMagicId(anim) : null);

            if (corpus == null)
            {
                BattleSfxSummary = anim >= 0
                    ? $"Battle SFX: {BattleSfxMagicIdDisplay} — no wave6 corpus row (run --magicdll-sound-corpus-wave6)."
                    : "Battle SFX: no Anim id.";
                BattleSfxSeIdDisplay = null;
                BattleSfxWaveIdDisplay = null;
                return;
            }

            uint dllSeId = 0;
            ushort dllWave = 0;
            bool dllLive = anim >= 0 && TryReadLiveDllSound(anim, out dllSeId, out dllWave);
            BattleSfxSeIdDisplay = corpus.SeId?.ToString() ?? "—";
            BattleSfxWaveIdDisplay = corpus.WaveDataId?.ToString() ?? "—";
            if (dllLive)
            {
                BattleSfxSeIdDisplay = dllSeId.ToString();
                BattleSfxWaveIdDisplay = dllWave.ToString();
            }

            CanEditBattleSfx = anim >= 0 && BattleSfxCorpusAvailable;
            string dllTag = dllLive ? "DLL live" : corpus.Evidence;
            BattleSfxSummary =
                $"Battle SFX: FMOD seId={BattleSfxSeIdDisplay}, waveDataId={BattleSfxWaveIdDisplay} · {dllTag} · {BattleSfxRt2Status}.";

            if (SelectedBattleSfxDonor == null || SelectedBattleSfxDonor.MagicId != anim)
            {
                SelectedBattleSfxDonor = BattleSfxDonorOptions.FirstOrDefault(d => d.MagicId == anim)
                    ?? BattleSfxDonorOptions.FirstOrDefault();
            }

            uint? previewSeId = uint.TryParse(BattleSfxSeIdDisplay, out uint parsedSe) ? parsedSe : corpus.SeId;
            SeidFsbMapLoader.MapRow? mapRow = anim >= 0 && previewSeId is uint se
                ? SeidFsbMapLoader.TryGetForMagicAndSeId(anim, se)
                : anim >= 0
                    ? SeidFsbMapLoader.TryGetForMagicId(anim)
                    : null;
            if (mapRow?.FsbSampleIndex is int idx)
            {
                BattleSfxFsbSampleIndex = idx;
                if (!string.IsNullOrWhiteSpace(mapRow.Evidence))
                    BattleSfxSummary += $" · Play original → FSB #{idx} ({mapRow.Evidence})";
            }
            else if (previewSeId is uint liveSe && TryResolveFsbPreviewIndex(liveSe, out int liveIdx, out string liveEv))
            {
                BattleSfxFsbSampleIndex = liveIdx;
                BattleSfxSummary += $" · Play original → FSB #{liveIdx} ({liveEv})";
            }
            else if (SeidFsbMapLoader.IsMapAvailable())
                BattleSfxSummary += " · FSB index unmapped — use ◀▶ to browse subsongs.";
        }

        public void SetCustomWavPath(string path)
        {
            BattleSfxCustomWavPath = path;
            BattleSfxCustomValidation = "";
            BattleSfxCustomStatus = $"Imported: {Path.GetFileName(path)}";
        }

        [RelayCommand]
        void InstallBattleSfxAudioTools()
        {
            (bool ok, string msg) = FfxAudioToolsBootstrap_Service.RunBootstrap();
            RefreshBattleSfxCustomToolsState();
            BattleSfxCustomStatus = msg;
            BattleSfxActionStatus = ok ? "Audio tools ready." : msg;
        }

        public void ImportFsbankClFromPath(string sourcePath)
        {
            (bool ok, string msg) = FfxFsbankClImport_Service.InstallFromPath(sourcePath);
            RefreshBattleSfxCustomToolsState();
            BattleSfxCustomStatus = msg;
            BattleSfxActionStatus = ok ? "fsbankcl bundled." : msg;
        }

        [RelayCommand]
        void DetectFsbankClFromSdk()
        {
            (bool ok, string msg) = FfxFsbankClImport_Service.TryCopyFromSdkInstall();
            RefreshBattleSfxCustomToolsState();
            BattleSfxCustomStatus = msg;
            BattleSfxActionStatus = ok ? "fsbankcl bundled from SDK." : msg;
        }

        [RelayCommand]
        void VerifyBattleAudioTools()
        {
            (bool ok, string msg) = FfxAudioToolsBootstrap_Service.VerifyBattleAudioTools();
            RefreshBattleSfxCustomToolsState();
            BattleSfxAudioToolsHealth = msg;
            BattleSfxActionStatus = ok ? "Audio tools verified." : msg;
        }

        [RelayCommand]
        void ValidateCustomBattleSfxWav()
        {
            if (!TryResolveCustomAudioContext(out string sfxRoot, out string fsbPath, out int magicId, out uint seId, out string error))
            {
                BattleSfxCustomValidation = error;
                return;
            }

            if (string.IsNullOrWhiteSpace(BattleSfxCustomWavPath) || !File.Exists(BattleSfxCustomWavPath))
            {
                BattleSfxCustomValidation = "Import a WAV first.";
                return;
            }

            string temp = Path.Combine(Path.GetTempPath(), $"ffx_sfx_val_{Guid.NewGuid():N}.wav");
            (bool decOk, string decMsg) = FfxFsbVgmStream_Service.ExportSubsong(fsbPath, BattleSfxFsbSampleIndex, temp);
            if (!decOk)
            {
                BattleSfxCustomValidation = decMsg;
                return;
            }

            (bool valOk, string valMsg) = FfxFsbVgmStream_Service.ValidateReplacement(temp, BattleSfxCustomWavPath);
            BattleSfxCustomValidation = valMsg;
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }

        [RelayCommand]
        void PreviewOriginalBattleSfxSample()
        {
            if (!TryResolveCustomAudioContext(out _, out string fsbPath, out _, out _, out string error))
            {
                BattleSfxCustomStatus = error;
                return;
            }

            string temp = Path.Combine(Path.GetTempPath(), $"ffx_sfx_orig_{Guid.NewGuid():N}.wav");
            (bool ok, string msg) = FfxFsbVgmStream_Service.ExportSubsong(fsbPath, BattleSfxFsbSampleIndex, temp);
            if (!ok)
            {
                BattleSfxCustomStatus = msg;
                return;
            }
            PlayCustomWav(temp, deleteAfter: true);
            BattleSfxCustomStatus =
                $"Play original: FSB subsong #{BattleSfxFsbSampleIndex} (9999_bank00 via DLL seId → common.txt).";
        }

        [RelayCommand]
        void NudgeBattleSfxFsbSamplePrev()
        {
            if (BattleSfxFsbSampleIndex > 0)
                BattleSfxFsbSampleIndex--;
            BattleSfxCustomStatus = $"FSB sample # set to {BattleSfxFsbSampleIndex} (browse).";
        }

        [RelayCommand]
        void NudgeBattleSfxFsbSampleNext()
        {
            BattleSfxFsbSampleIndex++;
            if (BattleSfxFsbSampleIndex > 121)
                BattleSfxFsbSampleIndex = 121;
            BattleSfxCustomStatus = $"FSB sample # set to {BattleSfxFsbSampleIndex} (browse).";
        }

        [RelayCommand]
        void PreviewReplacementBattleSfxWav()
        {
            if (string.IsNullOrWhiteSpace(BattleSfxCustomWavPath) || !File.Exists(BattleSfxCustomWavPath))
            {
                BattleSfxCustomStatus = "Import a WAV first.";
                return;
            }
            PlayCustomWav(BattleSfxCustomWavPath, deleteAfter: false);
            BattleSfxCustomStatus = $"Playing replacement: {Path.GetFileName(BattleSfxCustomWavPath)}";
        }

        void PlayCustomWav(string wavPath, bool deleteAfter)
        {
            StopCustomPreview();
            try
            {
                _customSfxPreviewReader = new AudioFileReader(wavPath);
                _customSfxPreviewOut = new WaveOutEvent();
                _customSfxPreviewOut.PlaybackStopped += (_, _) =>
                {
                    StopCustomPreview();
                    if (deleteAfter)
                    {
                        try { File.Delete(wavPath); } catch { }
                    }
                };
                _customSfxPreviewOut.Init(_customSfxPreviewReader);
                _customSfxPreviewOut.Play();
            }
            catch (Exception ex)
            {
                BattleSfxCustomStatus = ex.Message;
            }
        }

        void StopCustomPreview()
        {
            try { _customSfxPreviewOut?.Stop(); } catch { }
            _customSfxPreviewOut?.Dispose();
            _customSfxPreviewOut = null;
            _customSfxPreviewReader?.Dispose();
            _customSfxPreviewReader = null;
        }

        [RelayCommand]
        void DryRunCustomBattleSfxFsb()
        {
            if (!TryBuildCustomAudioRequest(out CommandSoundPackService.CustomAudioPackRequest request, out string sfxRoot, out string magicRoot, out string error))
            {
                BattleSfxCustomStatus = error;
                return;
            }

            Fsb9999SampleReplaceWriter.ReplaceResult dry = Fsb9999SampleReplaceWriter.ReplaceSample(
                new Fsb9999SampleReplaceWriter.ReplaceRequest(
                    Path.Combine(sfxRoot, "9999_bank00.fsb"),
                    request.FsbSampleIndex0,
                    request.SourceWavPath),
                dryRun: true);
            BattleSfxCustomStatus = dry.Message;
        }

        [RelayCommand]
        void StageCustomBattleSfxPack()
        {
            if (!TryBuildCustomAudioRequest(out CommandSoundPackService.CustomAudioPackRequest request, out string sfxRoot, out string magicRoot, out string error))
            {
                BattleSfxCustomStatus = error;
                return;
            }

            string? repo = CommandSoundCorpusLoader.FindRepoRoot();
            string outDir = repo == null
                ? Path.Combine(Path.GetTempPath(), "command_sound_custom", $"magic_{request.TargetMagicId:D4}")
                : Path.Combine(repo, CommandSoundPackService.CustomAudioOutputDir, $"magic_{request.TargetMagicId:D4}");

            CommandSoundPackService.CustomAudioPackResult result =
                CommandSoundPackService.StageCustomAudioPack(request, sfxRoot, magicRoot, outDir);
            BattleSfxCustomStatus = result.Ok
                ? $"Staged FSB{(result.StagedDllPath != null ? " + DLL" : "")}: {outDir}"
                : result.Message;
        }

        [RelayCommand]
        void DeployCustomBattleSfxPack()
        {
            if (!TryBuildCustomAudioRequest(out CommandSoundPackService.CustomAudioPackRequest request, out string sfxRoot, out string magicRoot, out string error))
            {
                BattleSfxCustomStatus = error;
                return;
            }

            string? repo = CommandSoundCorpusLoader.FindRepoRoot();
            string outDir = repo == null
                ? Path.Combine(Path.GetTempPath(), "command_sound_custom", $"magic_{request.TargetMagicId:D4}")
                : Path.Combine(repo, CommandSoundPackService.CustomAudioOutputDir, $"magic_{request.TargetMagicId:D4}");

            CommandSoundPackService.CustomAudioPackResult staged =
                CommandSoundPackService.StageCustomAudioPack(request, sfxRoot, magicRoot, outDir);
            if (!staged.Ok)
            {
                BattleSfxCustomStatus = staged.Message;
                return;
            }

            CommandSoundPackService.CustomAudioPackResult deployed =
                CommandSoundPackService.DeployCustomAudioPack(staged, sfxRoot, magicRoot);
            BattleSfxCustomStatus = deployed.Ok
                ? $"Deployed FSB + DLL backups: FSB={deployed.FsbBackupPath ?? "n/a"}, DLL={deployed.DllBackupPath ?? "n/a"}"
                : deployed.Message;
            RefreshSelectedCommandBattleSfx();
        }

        [RelayCommand]
        void RestoreCustomBattleSfxBackups()
        {
            if (SelectedCommand == null)
            {
                BattleSfxCustomStatus = "Select a command first.";
                return;
            }

            short anim = SelectedCommand.Anim1Id >= 0 ? SelectedCommand.Anim1Id : SelectedCommand.Anim2Id;
            if (anim < 0)
            {
                BattleSfxCustomStatus = "No Anim id.";
                return;
            }

            if (!TryResolveSfxRoot(out string sfxRoot, out string _))
            {
                BattleSfxCustomStatus = "SFX folder not found (ps3data/sound_pc/sfx).";
                return;
            }

            string magicRoot = Project_Service.Instance.Path_MagicDllRoot
                ?? MagicDllSemanticAnalyzer.DefaultFfxMagicFilesRoot;

            CommandSoundPackService.CustomAudioPackResult restored =
                CommandSoundPackService.RestoreCustomAudioBackups(sfxRoot, magicRoot, anim);
            BattleSfxCustomStatus = restored.Message;
            BattleSfxActionStatus = restored.Message;
            RefreshSelectedCommandBattleSfx();
        }

        [RelayCommand]
        void StageNewSeIdBattleSfxPack()
        {
            if (!TryBuildNewSeIdAudioRequest(out CommandSoundPackService.NewSeIdAudioPackRequest request, out string sfxRoot, out string magicRoot, out string error))
            {
                BattleSfxNewSeIdStatus = error;
                return;
            }

            string? repo = CommandSoundCorpusLoader.FindRepoRoot();
            string outDir = repo == null
                ? Path.Combine(Path.GetTempPath(), "command_sound_new_seid", $"magic_{request.TargetMagicId:D4}")
                : Path.Combine(repo, CommandSoundPackService.NewSeIdAudioOutputDir, $"magic_{request.TargetMagicId:D4}");

            CommandSoundPackService.NewSeIdAudioPackResult result =
                CommandSoundPackService.StageNewSeIdAudioPack(request, sfxRoot, magicRoot, outDir);
            BattleSfxNewSeIdStatus = result.Ok
                ? $"Staged FEV+FSB+DLL: {outDir}"
                : result.Message;
        }

        [RelayCommand]
        void DeployNewSeIdBattleSfxPack()
        {
            if (!TryBuildNewSeIdAudioRequest(out CommandSoundPackService.NewSeIdAudioPackRequest request, out string sfxRoot, out string magicRoot, out string error))
            {
                BattleSfxNewSeIdStatus = error;
                return;
            }

            string? repo = CommandSoundCorpusLoader.FindRepoRoot();
            string outDir = repo == null
                ? Path.Combine(Path.GetTempPath(), "command_sound_new_seid", $"magic_{request.TargetMagicId:D4}")
                : Path.Combine(repo, CommandSoundPackService.NewSeIdAudioOutputDir, $"magic_{request.TargetMagicId:D4}");

            CommandSoundPackService.NewSeIdAudioPackResult staged =
                CommandSoundPackService.StageNewSeIdAudioPack(request, sfxRoot, magicRoot, outDir);
            if (!staged.Ok)
            {
                BattleSfxNewSeIdStatus = staged.Message;
                return;
            }

            CommandSoundPackService.NewSeIdAudioPackResult deployed =
                CommandSoundPackService.DeployNewSeIdAudioPack(staged, sfxRoot, magicRoot, request.MirrorJpLocale);
            BattleSfxNewSeIdStatus = deployed.Ok
                ? $"Deployed new seId {request.NewSeId} — backups FEV/FSB/DLL created. Donor spells unchanged."
                : deployed.Message;
            RefreshSelectedCommandBattleSfx();
        }

        [RelayCommand]
        void RestoreNewSeIdBattleSfxBackups()
        {
            if (SelectedCommand == null)
            {
                BattleSfxNewSeIdStatus = "Select a command first.";
                return;
            }

            short anim = SelectedCommand.Anim1Id >= 0 ? SelectedCommand.Anim1Id : SelectedCommand.Anim2Id;
            if (anim < 0)
            {
                BattleSfxNewSeIdStatus = "No Anim id.";
                return;
            }

            if (!TryResolveSfxRoot(out string sfxRoot, out _))
            {
                BattleSfxNewSeIdStatus = "SFX folder not found.";
                return;
            }

            string magicRoot = Project_Service.Instance.Path_MagicDllRoot
                ?? MagicDllSemanticAnalyzer.DefaultFfxMagicFilesRoot;

            CommandSoundPackService.NewSeIdAudioPackResult restored =
                CommandSoundPackService.RestoreNewSeIdBackups(sfxRoot, magicRoot, anim);
            BattleSfxNewSeIdStatus = restored.Message;
            RefreshSelectedCommandBattleSfx();
        }

        bool TryBuildNewSeIdAudioRequest(
            out CommandSoundPackService.NewSeIdAudioPackRequest request,
            out string sfxRoot,
            out string magicRoot,
            out string error)
        {
            request = default!;
            sfxRoot = "";
            magicRoot = "";
            error = "";

            if (!BattleSfxCustomToolsReady)
            {
                error = "Install audio tools first.";
                return false;
            }

            if (!TryResolveCustomAudioContext(out sfxRoot, out _, out int magicId, out _, out error))
                return false;

            if (SelectedBattleSfxDonor == null)
            {
                error = "Select donor seId for FEV clone metadata.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(BattleSfxCustomWavPath) || !File.Exists(BattleSfxCustomWavPath))
            {
                error = "Import a WAV for the new slot.";
                return false;
            }

            if (BattleSfxNewSeIdGapValue < 8000 || BattleSfxNewSeIdGapValue > 20000)
            {
                error = "New seId gap must be 8000–20000.";
                return false;
            }

            string fev = Path.Combine(sfxRoot, "9999.fev");
            if (FevLegacyReader.ContainsSeId(fev, (uint)BattleSfxNewSeIdGapValue))
            {
                error = $"seId {BattleSfxNewSeIdGapValue} already in FEV — pick another gap.";
                return false;
            }

            magicRoot = Project_Service.Instance.Path_MagicDllRoot
                ?? MagicDllSemanticAnalyzer.DefaultFfxMagicFilesRoot;
            if (!Directory.Exists(magicRoot))
            {
                error = $"magicFiles\\FFX not found: {magicRoot}";
                return false;
            }

            request = new CommandSoundPackService.NewSeIdAudioPackRequest(
                magicId,
                (uint)BattleSfxNewSeIdGapValue,
                SelectedBattleSfxDonor.SeId,
                Path.GetFullPath(BattleSfxCustomWavPath),
                BattleSfxSfxLocale,
                BattleSfxMirrorJpLocale,
                BattleSfxFsbSampleIndex,
                BattleSfxRecordIndex,
                $"UI new seId cmd #{SelectedCommand!.Index}");
            return true;
        }

        bool TryBuildCustomAudioRequest(
            out CommandSoundPackService.CustomAudioPackRequest request,
            out string sfxRoot,
            out string magicRoot,
            out string error)
        {
            request = default!;
            sfxRoot = "";
            magicRoot = "";
            error = "";

            if (!BattleSfxCustomToolsReady)
            {
                error = "Install audio tools first (vgmstream + fsbext).";
                return false;
            }

            if (!TryResolveCustomAudioContext(out sfxRoot, out _, out int magicId, out uint seId, out error))
                return false;

            if (string.IsNullOrWhiteSpace(BattleSfxCustomWavPath) || !File.Exists(BattleSfxCustomWavPath))
            {
                error = "Import a replacement WAV.";
                return false;
            }

            magicRoot = Project_Service.Instance.Path_MagicDllRoot
                ?? MagicDllSemanticAnalyzer.DefaultFfxMagicFilesRoot;
            if (!Directory.Exists(magicRoot))
            {
                error = $"magicFiles\\FFX not found: {magicRoot}";
                return false;
            }

            ushort? wave = SelectedBattleSfxDonor?.WaveDataId
                ?? CommandSoundCorpusLoader.TryGetForMagicId(magicId)?.WaveDataId;

            request = new CommandSoundPackService.CustomAudioPackRequest(
                magicId,
                seId,
                BattleSfxFsbSampleIndex,
                Path.GetFullPath(BattleSfxCustomWavPath),
                BattleSfxSfxLocale,
                BattleSfxPatchDllForCustom,
                wave,
                BattleSfxRecordIndex,
                $"UI custom WAV cmd #{SelectedCommand!.Index}");
            return true;
        }

        bool TryResolveCustomAudioContext(
            out string sfxRoot,
            out string fsbPath,
            out int magicId,
            out uint seId,
            out string error)
        {
            sfxRoot = "";
            fsbPath = "";
            magicId = 0;
            seId = 0;
            error = "";

            if (SelectedCommand == null)
            {
                error = "Select a command first.";
                return false;
            }

            if (SelectedBattleSfxDonor == null)
            {
                error = "Select donor seId.";
                return false;
            }

            if (!TryResolveSfxRoot(out sfxRoot, out error))
                return false;

            fsbPath = Path.Combine(sfxRoot, "9999_bank00.fsb");
            if (!File.Exists(fsbPath))
            {
                error = $"9999_bank00.fsb not found: {fsbPath}";
                return false;
            }

            short anim = SelectedCommand.Anim1Id >= 0 ? SelectedCommand.Anim1Id : SelectedCommand.Anim2Id;
            if (anim < 0)
            {
                error = "No Anim id.";
                return false;
            }

            magicId = anim;
            seId = SelectedBattleSfxDonor.SeId;
            return true;
        }

        static bool TryResolveSfxRoot(out string sfxRoot, out string error)
        {
            sfxRoot = "";
            error = "";
            string loc = "us";
            string? portableSfx = PortablePathResolver.AudioSfxRoot(loc);
            if (!string.IsNullOrWhiteSpace(portableSfx))
            {
                sfxRoot = portableSfx;
                return true;
            }

            string? game = PortablePathResolver.GameInstallRoot;
            if (!string.IsNullOrWhiteSpace(game))
            {
                string viaMods = CommandSoundPackService.ResolveSfxRoot(game, "US");
                if (Directory.Exists(viaMods))
                {
                    sfxRoot = viaMods;
                    return true;
                }
            }

            error = "Could not resolve sound_pc/sfx/us. Configure the PS3 data root (FFX_PS3DATA_ROOT) " +
                    "or game installation (FFX_GAME_ROOT).";
            return false;
        }

        static string? TryResolveFevPath()
        {
            if (!TryResolveSfxRoot(out string sfxRoot, out _))
                return null;
            string fev = Path.Combine(sfxRoot, "9999.fev");
            return File.Exists(fev) ? fev : null;
        }

        static bool TryReadLiveDllSound(int magicId, out uint seId, out ushort waveDataId)
        {
            seId = 0;
            waveDataId = 0;
            if (magicId < 0)
                return false;

            string magicRoot = Project_Service.Instance.Path_MagicDllRoot
                ?? MagicDllSemanticAnalyzer.DefaultFfxMagicFilesRoot;
            string dllPath = Path.Combine(magicRoot, $"magic_{magicId:D4}.dll");
            if (!File.Exists(dllPath))
                return false;

            try
            {
                IReadOnlyList<MagicDllSoundRecordScanner.SeSepHit> hits =
                    MagicDllSoundRecordScanner.Scan(File.ReadAllBytes(dllPath));
                if (hits.Count == 0)
                    return false;

                seId = hits[0].SeId;
                waveDataId = hits[0].WaveDataId;
                return true;
            }
            catch
            {
                return false;
            }
        }

        static bool TryResolveFsbPreviewIndex(uint seId, out int fsbIndex, out string evidence)
        {
            fsbIndex = 0;
            evidence = "";
            if (!TryResolveSfxRoot(out string sfxRoot, out _))
                return false;

            string commonPath = Path.Combine(sfxRoot, "9999_common.txt");
            if (!File.Exists(commonPath))
                return false;

            IReadOnlyList<FevLegacySidecarReader.CommonRow> rows = FevLegacySidecarReader.ReadCommonRows(commonPath);
            Dictionary<uint, int> keyToFsb = FevLegacySidecarReader.BuildKeyToFsbIndexMap(rows);
            SeidFsbMapResolver.ResolveResult? hit = SeidFsbMapResolver.TryResolve(0, seId, null, keyToFsb);
            if (hit == null)
                return false;

            fsbIndex = hit.FsbSampleIndex;
            evidence = hit.Evidence;
            return true;
        }

        [RelayCommand]
        void PreviewBattleSfxPatch()
        {
            if (!TryBuildSoundPackRequest(out CommandSoundPackService.PackRequest request, out string magicRoot, out string error))
            {
                BattleSfxActionStatus = error;
                return;
            }

            MagicDllSoundWriter.PatchResult dry = MagicDllSoundWriter.PatchFromCorpusEntry(
                magicRoot,
                request.TargetMagicId,
                request.DonorSeId,
                request.DonorWaveDataId,
                request.RecordIndex,
                dryRun: true);
            BattleSfxActionStatus = dry.Ok
                ? $"Dry-run OK: seId {dry.RecordsBefore.FirstOrDefault()?.SeId} → {dry.RecordsAfter.FirstOrDefault()?.SeId}"
                : dry.Message;
        }

        [RelayCommand]
        void StageBattleSfxPack()
        {
            if (!TryBuildSoundPackRequest(out CommandSoundPackService.PackRequest request, out string magicRoot, out string error))
            {
                BattleSfxActionStatus = error;
                return;
            }

            string? repo = CommandSoundCorpusLoader.FindRepoRoot();
            string outDir = repo == null
                ? Path.Combine(Path.GetTempPath(), "command_sound_pack")
                : Path.Combine(repo, CommandSoundPackService.DefaultOutputDir, $"cmd_{SelectedCommand!.Index}");

            CommandSoundPackService.PackResult result = CommandSoundPackService.StagePack(request, magicRoot, outDir);
            BattleSfxActionStatus = result.Ok
                ? $"Staged: {result.StagedDllPath}"
                : result.Message;
        }

        [RelayCommand]
        void DeployBattleSfxPack()
        {
            if (!TryBuildSoundPackRequest(out CommandSoundPackService.PackRequest request, out string magicRoot, out string error))
            {
                BattleSfxActionStatus = error;
                return;
            }

            string? repo = CommandSoundCorpusLoader.FindRepoRoot();
            string outDir = repo == null
                ? Path.Combine(Path.GetTempPath(), "command_sound_pack")
                : Path.Combine(repo, CommandSoundPackService.DefaultOutputDir, $"cmd_{SelectedCommand!.Index}");

            CommandSoundPackService.PackResult staged = CommandSoundPackService.StagePack(request, magicRoot, outDir);
            if (!staged.Ok)
            {
                BattleSfxActionStatus = staged.Message;
                return;
            }

            CommandSoundPackService.PackResult deployed = CommandSoundPackService.DeployStaged(staged.StagedDllPath!, magicRoot);
            BattleSfxActionStatus = deployed.Ok
                ? $"Deployed {deployed.DeployedDllPath} (backup: {deployed.BackupPath ?? "none"})"
                : deployed.Message;
            RefreshSelectedCommandBattleSfx();
        }

        [RelayCommand]
        void RestoreBattleSfxBackup()
        {
            if (SelectedCommand == null)
            {
                BattleSfxActionStatus = "Select a command first.";
                return;
            }

            string? magicRoot = Project_Service.Instance.Path_MagicDllRoot;
            if (string.IsNullOrWhiteSpace(magicRoot) || !Directory.Exists(magicRoot))
            {
                BattleSfxActionStatus = "magicFiles\\FFX not found — load a mod workspace with game install root.";
                return;
            }

            short anim = SelectedCommand.Anim1Id >= 0 ? SelectedCommand.Anim1Id : SelectedCommand.Anim2Id;
            if (anim < 0)
            {
                BattleSfxActionStatus = "No Anim id on selected command.";
                return;
            }

            CommandSoundPackService.PackResult restored = CommandSoundPackService.RestoreLatestBackup(magicRoot, anim);
            if (TryResolveSfxRoot(out string sfxRoot, out _))
            {
                CommandSoundPackService.CustomAudioPackResult fsbRestore =
                    CommandSoundPackService.RestoreCustomAudioBackups(sfxRoot, magicRoot, anim);
                BattleSfxActionStatus = $"{restored.Message}; {fsbRestore.Message}";
            }
            else
                BattleSfxActionStatus = restored.Message;
            RefreshSelectedCommandBattleSfx();
        }

        bool TryBuildSoundPackRequest(
            out CommandSoundPackService.PackRequest request,
            out string magicRoot,
            out string error)
        {
            request = default!;
            magicRoot = "";
            error = "";

            if (SelectedCommand == null)
            {
                error = "Select a command first.";
                return false;
            }

            if (SelectedBattleSfxDonor == null)
            {
                error = "Select a donor seId (run --magicdll-sound-corpus-wave6).";
                return false;
            }

            magicRoot = Project_Service.Instance.Path_MagicDllRoot
                ?? MagicDllSemanticAnalyzer.DefaultFfxMagicFilesRoot;
            if (!Directory.Exists(magicRoot))
            {
                error = $"magicFiles\\FFX not found: {magicRoot}";
                return false;
            }

            short anim = SelectedCommand.Anim1Id >= 0 ? SelectedCommand.Anim1Id : SelectedCommand.Anim2Id;
            if (anim < 0)
            {
                error = "Selected command has no Anim id.";
                return false;
            }

            request = new CommandSoundPackService.PackRequest(
                anim,
                SelectedBattleSfxDonor.SeId,
                SelectedBattleSfxDonor.WaveDataId,
                BattleSfxRecordIndex,
                $"UI cmd #{SelectedCommand.Index} donor magic_{SelectedBattleSfxDonor.MagicId:D4}");
            return true;
        }

        static string BuildAnimationEffectSummary(string label, short animationId, out bool canView, out bool available, out string? folderPath)
        {
            canView = animationId >= 0;
            available = false;
            folderPath = null;

            if (!canView)
                return $"{label}: invalid effect id {animationId}.";

            string folderName = $"magic_{animationId:D4}";
            string? ps3Root = Project_Service.Instance.Path_Ps3DataRoot;
            if (string.IsNullOrWhiteSpace(ps3Root) || !Directory.Exists(ps3Root))
                return $"{label}: {folderName} · ps3data root not detected.";

            folderPath = Path.Combine(ps3Root, "magic", folderName);
            if (!Directory.Exists(folderPath))
                return $"{label}: {folderName} · folder not found under ps3data\\magic.";

            int textureCount = 0;
            try
            {
                textureCount = Directory.EnumerateFiles(folderPath, "*.dds.phyre", SearchOption.AllDirectories).Count();
            }
            catch
            {
                return $"{label}: {folderName} · folder found, texture scan failed.";
            }

            available = true;
            string textureWord = textureCount == 1 ? "texture" : "textures";
            return $"{label}: {folderName} · {textureCount:N0} {textureWord} · PS3 Magic preview available.";
        }

        public void OpenAnimationEffectFolder(int slot)
        {
            string? folder = slot == 1 ? Anim1EffectFolderPath : Anim2EffectFolderPath;
            ExtrasFileOpen_Service.TryOpenInExplorer(folder);
        }

        public void RestoreViewState(string? filterText, int? selectedCommandIndex)
        {
            FilterText = filterText ?? string.Empty;
            ApplyFilter();

            if (!selectedCommandIndex.HasValue)
            {
                return;
            }

            KernelCommands_Wrapper? selected = LoadedCommands.FirstOrDefault(wrapper => wrapper.Index == selectedCommandIndex.Value);
            if (selected == null)
            {
                return;
            }

            if (!DisplayedCommands.Contains(selected))
            {
                FilterText = string.Empty;
                ApplyFilter();
            }

            SelectedCommand = selected;
        }

        ushort BuildRawGameIndex(int entryIndex)
        {
            ushort rawGameIndex = 0;
            rawGameIndex = FfxCommon_Util.SetGameCategory(rawGameIndex, GetGameCategory());
            rawGameIndex = FfxCommon_Util.SetGameIndex(rawGameIndex, (ushort)entryIndex);
            return rawGameIndex;
        }

        byte GetGameCategory()
        {
            return CommandFileType switch
            {
                CommandFile_enum.Command => (byte)GameCategory_Enum.Commands,
                CommandFile_enum.Item => (byte)GameCategory_Enum.Items,
                CommandFile_enum.MonMagic1 => (byte)GameCategory_Enum.MonMagic1,
                CommandFile_enum.MonMagic2 => (byte)GameCategory_Enum.MonMagic2,
                _ => (byte)GameCategory_Enum.None
            };
        }

        string GetCategoryLabel()
        {
            return CommandFileType switch
            {
                CommandFile_enum.Command => "Command",
                CommandFile_enum.Item => "Item",
                CommandFile_enum.MonMagic1 => "Monster Command 1",
                CommandFile_enum.MonMagic2 => "Monster Command 2",
                _ => "Unknown"
            };
        }

        void SubscribeCommandGraph()
        {
            foreach (KernelCommands_Wrapper wrapper in LoadedCommands)
            {
                wrapper.PropertyChanged += CommandGraphChanged;
                wrapper.StatusChance.PropertyChanged += CommandGraphChanged;
                wrapper.StatusDuration.PropertyChanged += CommandGraphChanged;

                if (wrapper.ExtraInfo != null)
                {
                    wrapper.ExtraInfo.PropertyChanged += CommandGraphChanged;
                }
            }
        }

        void UnsubscribeCommandGraph()
        {
            foreach (KernelCommands_Wrapper wrapper in LoadedCommands)
            {
                wrapper.PropertyChanged -= CommandGraphChanged;

                if (wrapper.StatusChance != null)
                {
                    wrapper.StatusChance.PropertyChanged -= CommandGraphChanged;
                }

                if (wrapper.StatusDuration != null)
                {
                    wrapper.StatusDuration.PropertyChanged -= CommandGraphChanged;
                }

                if (wrapper.ExtraInfo != null)
                {
                    wrapper.ExtraInfo.PropertyChanged -= CommandGraphChanged;
                }
            }
        }

        void CommandGraphChanged(object? sender, PropertyChangedEventArgs e)
        {
            EditSession?.NotifyPotentialMutation();
            if (ReferenceEquals(sender, SelectedCommand)
                && (e.PropertyName == nameof(KernelCommands_Wrapper.Anim1Id)
                    || e.PropertyName == nameof(KernelCommands_Wrapper.Anim2Id)))
            {
                RefreshSelectedCommandAnimationLinks();
            }
        }

        string GetDomainLabel()
        {
            return CommandFileType switch
            {
                CommandFile_enum.Command => "command table",
                CommandFile_enum.Item => "item command table",
                CommandFile_enum.MonMagic1 => "monster command table 1",
                CommandFile_enum.MonMagic2 => "monster command table 2",
                _ => "kernel data"
            };
        }

        public string GetFilePath()
        {
            if (CommandFileType == CommandFile_enum.Command)
            {
                return Project_Service.Instance.Path_KernelCommandUs;
            }
            if (CommandFileType == CommandFile_enum.Item)
            {
                return Project_Service.Instance.Path_KernelItemUs;
            }
            if (CommandFileType == CommandFile_enum.MonMagic1)
            {
                return Project_Service.Instance.Path_KernelMonMagic1Us;
            }
            if (CommandFileType == CommandFile_enum.MonMagic2)
            {
                return Project_Service.Instance.Path_KernelMonMagic2Us;
            }

            throw new System.Exception("[KernelCommands_DataModel] File type not selected");
        }

        public bool HasExtraInfo()
        {
            if (CommandFileType == CommandFile_enum.Command || CommandFileType == CommandFile_enum.Item)
            {
                return true;
            }
            if (CommandFileType == CommandFile_enum.MonMagic1 || CommandFileType == CommandFile_enum.MonMagic2)
            {
                return false;
            }

            throw new System.Exception("[KernelCommands_DataModel] File type not selected");
        }
    }
}

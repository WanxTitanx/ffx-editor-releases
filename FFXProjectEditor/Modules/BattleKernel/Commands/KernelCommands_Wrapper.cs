using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Common;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.Utils;
using FFXProjectEditor.Utils.Encoding;
using System;
using System.Linq;
using Avalonia.Media.Imaging;
using FFXProjectEditor.FfxLib.Items;
using static FFXProjectEditor.FfxLib.Ability.Ability_Command;

namespace FFXProjectEditor.Modules.BattleKernel.Commands
{
    internal partial class KernelCommands_Wrapper : ObservableObject
    {
        [ObservableProperty] public int index;
        [ObservableProperty] public short anim1Id;
        [ObservableProperty] public short anim2Id;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ItemIconImage))]
        [NotifyPropertyChangedFor(nameof(HasItemIcon))]
        [NotifyPropertyChangedFor(nameof(ShowItemIconFallback))]
        [NotifyPropertyChangedFor(nameof(ItemIconTooltip))]
        public byte iconId;
        [ObservableProperty] public byte casterAnimId;
        [ObservableProperty] public bool flagMenuMainMenu;
        [ObservableProperty] public bool flagMenuOpenCommandMenu;
        [ObservableProperty] public bool flagMenuOpenSpecialMenu;
        [ObservableProperty] public byte subSubMenuCategorization;
        [ObservableProperty] public byte subMenuCategorization;
        [ObservableProperty] public Character_Enum characterUser;
        [ObservableProperty] public bool flagTargetEnabled;
        [ObservableProperty] public bool flagTargetEnemies;
        [ObservableProperty] public bool flagTargetMulti;
        [ObservableProperty] public bool flagTargetSelfOnly;
        [ObservableProperty] public bool flagTargetUnk10;
        [ObservableProperty] public bool flagTargetEitherTeam;
        [ObservableProperty] public bool flagTargetDead;
        [ObservableProperty] public bool flagTargetLongRange;
        [ObservableProperty] public byte targetsAllowed; // Apparently
        [ObservableProperty] public bool flagMisc1UseOutsideCombat;
        [ObservableProperty] public bool flagMisc1UseInCombat;
        [ObservableProperty] public bool flagMisc1DisplayMoveName;
        [ObservableProperty] public bool flagMisc1AffectedByDarkness;
        [ObservableProperty] public bool flagMisc1AffectedByReflect;
        [ObservableProperty] public HitCalcType flagMisc1HitCalcType;
        [ObservableProperty] public bool flagMisc2AbsorbDamage;
        [ObservableProperty] public bool flagMisc2StealItem;
        [ObservableProperty] public bool flagMisc2MenuUse;
        [ObservableProperty] public bool flagMisc2MenuRight;
        [ObservableProperty] public bool flagMisc2MenuLeft;
        [ObservableProperty] public bool flagMisc2DelayS;
        [ObservableProperty] public bool flagMisc2DelayL;
        [ObservableProperty] public bool flagMisc2RandomTargets;
        [ObservableProperty] public bool flagMisc3Piercing;
        [ObservableProperty] public bool flagMisc3AffectedBySilence;
        [ObservableProperty] public bool flagMisc3UseWeaponProps;
        [ObservableProperty] public bool flagMisc3TriggerCommand;
        [ObservableProperty] public bool flagMisc3CastAnimS;
        [ObservableProperty] public bool flagMisc3CastAnimL;
        [ObservableProperty] public bool flagMisc3DestroyCaster;
        [ObservableProperty] public bool flagMisc3MissToAlive;
        [ObservableProperty] public bool flagMisc4ChargeWarriorHealer;
        [ObservableProperty] public bool flagMisc4EmptyOverdrive;
        [ObservableProperty] public bool flagMisc4ShowSpellcastAura;
        [ObservableProperty] public bool flagMisc4RunOffScreen;
        [ObservableProperty] public bool flagMisc4CopycatEnabled;
        [ObservableProperty] public bool flagMisc4Unk20;
        [ObservableProperty] public bool flagMisc4AeonOverdrive;
        [ObservableProperty] public bool flagMisc4Bribe;
        [ObservableProperty] public bool flagDamagePhysical;
        [ObservableProperty] public bool flagDamageMagical;
        [ObservableProperty] public bool flagDamageCanCrit;
        [ObservableProperty] public bool flagDamageGearCritBonus;
        [ObservableProperty] public bool flagDamageHeals;
        [ObservableProperty] public bool flagDamageCleansesStatuses;
        [ObservableProperty] public bool flagDamageSupressBreakDamageLimit;
        [ObservableProperty] public bool flagDamageBreaksDamageLimit;
        [ObservableProperty] public bool stealGil;
        [ObservableProperty] public bool flagPreviewActive;
        [ObservableProperty] public bool flagPreviewHealMp;
        [ObservableProperty] public bool flagPreviewHealStatuses;
        [ObservableProperty] public bool flagPreviewIsMap;
        [ObservableProperty] public bool flagPreviewIsRenameCard;
        [ObservableProperty] public bool flagPreviewIsSphere;
        [ObservableProperty] public bool flagPreviewHealHp;
        [ObservableProperty] public bool flagPreviewIsRenameCard2;
        [ObservableProperty] public bool flagDamageTypeHp;
        [ObservableProperty] public bool flagDamageTypeMp;
        [ObservableProperty] public bool flagDamageTypeCtb;
        [ObservableProperty] public byte moveRank;
        [ObservableProperty] public byte costMp;
        [ObservableProperty] public byte costOverdrive;
        [ObservableProperty] public byte attackCritBonus;
        [ObservableProperty] public DamageFormula_Enum damageFormula;
        [ObservableProperty] public byte attackAccuracy;
        [ObservableProperty] public byte attackPower;
        [ObservableProperty] public byte hitCount;
        [ObservableProperty] public byte shatterChance;

        [ObservableProperty] public bool flagElementFire;
        [ObservableProperty] public bool flagElementBlizzard;
        [ObservableProperty] public bool flagElementThunder;
        [ObservableProperty] public bool flagElementWater;
        [ObservableProperty] public bool flagElementHoly;
        [ObservableProperty] public bool flagElementDark;
        [ObservableProperty] public StatusByteList statusChance;
        [ObservableProperty] public StatusDurationByteList statusDuration;
        [ObservableProperty] public bool flagStatusScan;
        [ObservableProperty] public bool flagStatusDistillPower;
        [ObservableProperty] public bool flagStatusDistillMana;
        [ObservableProperty] public bool flagStatusDistillSpeed;
        [ObservableProperty] public bool flagStatusDistillUnused;
        [ObservableProperty] public bool flagStatusDistillAbility;
        [ObservableProperty] public bool flagStatusShield;
        [ObservableProperty] public bool flagStatusBoost;
        [ObservableProperty] public bool flagStatusEject;
        [ObservableProperty] public bool flagStatusAutoLife;
        [ObservableProperty] public bool flagStatusCurse;
        [ObservableProperty] public bool flagStatusDefend;
        [ObservableProperty] public bool flagStatusGuard;
        [ObservableProperty] public bool flagStatusSentinel;
        [ObservableProperty] public bool flagStatusDoom;

        [ObservableProperty] public bool flagStatBuffCheer;
        [ObservableProperty] public bool flagStatBuffAim;
        [ObservableProperty] public bool flagStatBuffFocus;
        [ObservableProperty] public bool flagStatBuffReflex;
        [ObservableProperty] public bool flagStatBuffLuck;
        [ObservableProperty] public bool flagStatBuffJinx;
        [ObservableProperty] public byte overdriveCategory;
        [ObservableProperty] public byte statBuffValue;

        [ObservableProperty] public bool flagSpecialBuffDoubleHp;
        [ObservableProperty] public bool flagSpecialBuffDoubleMp;
        [ObservableProperty] public bool flagSpecialBuffMpCost0;
        [ObservableProperty] public bool flagSpecialBuffQuartet;
        [ObservableProperty] public bool flagSpecialBuffAlwaysCrit;
        [ObservableProperty] public bool flagSpecialBuffOverdrive150;
        [ObservableProperty] public bool flagSpecialBuffOverdrive200;

        [ObservableProperty] public ExtraCommandInfo extraInfo;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Name))]
        [NotifyPropertyChangedFor(nameof(DisplayName))]
        [NotifyPropertyChangedFor(nameof(ItemIconImage))]
        [NotifyPropertyChangedFor(nameof(HasItemIcon))]
        [NotifyPropertyChangedFor(nameof(ShowItemIconFallback))]
        [NotifyPropertyChangedFor(nameof(ItemIconTooltip))]
        public byte[] nameScriptBytes;
        [ObservableProperty] public byte[] japaneseOnlyText1ScriptBytes;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Description))]
        [NotifyPropertyChangedFor(nameof(DisplayDescription))]
        public byte[] descriptionScriptBytes;
        [ObservableProperty] public byte[] japaneseOnlyText2ScriptBytes;
        // Text ids kept to keep the original data. Ideally this would be calculated.
        [ObservableProperty] public ushort nameScriptId;
        [ObservableProperty] public ushort japaneseOnlyText1ScriptId;
        [ObservableProperty] public ushort descriptionScriptId;
        [ObservableProperty] public ushort japaneseOnlyText2ScriptId;
        // Original text-pool offsets captured at read (beta.27 preserve-only). MUST round-trip through
        // Wrap/Unwrap (PropertyUtil.CopyProperties matches by name) — else WriteList loses the preserve
        // state, falls to the append rebuild, and (with monmagic's empty JP-only fields) NPE'd on open.
        [ObservableProperty] public ushort? originalNameOffset;
        [ObservableProperty] public ushort? originalJapaneseOnlyText1Offset;
        [ObservableProperty] public ushort? originalDescriptionOffset;
        [ObservableProperty] public ushort? originalJapaneseOnlyText2Offset;
        [ObservableProperty] public bool showSelectionMarker;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ItemIconImage))]
        [NotifyPropertyChangedFor(nameof(HasItemIcon))]
        [NotifyPropertyChangedFor(nameof(ShowItemIconFallback))]
        [NotifyPropertyChangedFor(nameof(ItemIconTooltip))]
        public bool usesItemIcon;

        public string Name => FfxEncoding.DecodeScript(nameScriptBytes).GetString(FfxEncoding.UsDecoder);
        public string Description => FfxEncoding.DecodeScript(descriptionScriptBytes).GetString(FfxEncoding.UsDecoder);

        /// <summary>Editable menu name — round-trips to <see cref="NameScriptBytes"/> on Save.</summary>
        public string DisplayName
        {
            get => Name;
            set
            {
                byte[] encoded = EncodeUs(value ?? string.Empty);
                if (nameScriptBytes != null && nameScriptBytes.SequenceEqual(encoded))
                    return;
                NameScriptBytes = encoded;
            }
        }

        /// <summary>Editable battle-menu description — round-trips to <see cref="DescriptionScriptBytes"/> on Save.</summary>
        public string DisplayDescription
        {
            get => Description;
            set
            {
                byte[] encoded = EncodeUs(value ?? string.Empty);
                if (descriptionScriptBytes != null && descriptionScriptBytes.SequenceEqual(encoded))
                    return;
                DescriptionScriptBytes = encoded;
            }
        }

        static byte[] EncodeUs(string text) => FfxEncoding.EncodeString(text, FfxEncoding.UsEncoder).ByteArray;
        public Bitmap? ItemIconImage => UsesItemIcon && ItemIconAtlas.TryResolveBitmap(IconId, Name, out Bitmap? bitmap) ? bitmap : null;
        public bool HasItemIcon => ItemIconImage != null;
        public bool ShowItemIconFallback => UsesItemIcon && !HasItemIcon;
        public string ItemIconTooltip => UsesItemIcon
            ? $"{Name} · item #{Index:D3} · IconId {IconId:X2}h"
            : string.Empty;

        public bool IsSpiraElementWard =>
            Index == CommandGrowWriter.RadiantWardCommandId
            || Index == CommandGrowWriter.UmbralWardCommandId;

        public string SpiraWardHookNote => Index switch
        {
            CommandGrowWriter.RadiantWardCommandId =>
                "Radiant Ward (320 / 0x3140): nullifies 1 Holy hit. Requires ffx-hooks NulWardHook + nul_ward_apply.flag. Encoded cmd @ action pool +0x08 (IDA).",
            CommandGrowWriter.UmbralWardCommandId =>
                "Umbral Ward (321 / 0x3141): nullifies 1 Dark hit. Same hook path. Run: dotnet run -- --nul-ward-static",
            _ => string.Empty,
        };

        public static KernelCommands_Wrapper Wrap(Ability_Command command)
        {
            KernelCommands_Wrapper wrapper = new();
            PropertyUtil.CopyProperties(command, wrapper);
            wrapper.StatusChance = Ability_Command.DeepCloneStatusChance(command.StatusChance);
            wrapper.StatusDuration = Ability_Command.DeepCloneStatusDuration(command.StatusDuration);
            wrapper.NameScriptBytes = Ability_Command.CloneScriptBytes(command.NameScriptBytes);
            wrapper.JapaneseOnlyText1ScriptBytes = Ability_Command.CloneScriptBytes(command.JapaneseOnlyText1ScriptBytes);
            wrapper.DescriptionScriptBytes = Ability_Command.CloneScriptBytes(command.DescriptionScriptBytes);
            wrapper.JapaneseOnlyText2ScriptBytes = Ability_Command.CloneScriptBytes(command.JapaneseOnlyText2ScriptBytes);
            if (command.ExtraInfo != null)
            {
                wrapper.ExtraInfo = new ExtraCommandInfo();
                PropertyUtil.CopyProperties(command.ExtraInfo, wrapper.ExtraInfo);
            }
            return wrapper;
        }

        public Ability_Command Unwrap()
        {
            Ability_Command command = new();
            PropertyUtil.CopyProperties(this, command);
            command.StatusChance = Ability_Command.DeepCloneStatusChance(StatusChance);
            command.StatusDuration = Ability_Command.DeepCloneStatusDuration(StatusDuration);
            command.NameScriptBytes = Ability_Command.CloneScriptBytes(NameScriptBytes);
            command.JapaneseOnlyText1ScriptBytes = Ability_Command.CloneScriptBytes(JapaneseOnlyText1ScriptBytes);
            command.DescriptionScriptBytes = Ability_Command.CloneScriptBytes(DescriptionScriptBytes);
            command.JapaneseOnlyText2ScriptBytes = Ability_Command.CloneScriptBytes(JapaneseOnlyText2ScriptBytes);
            if (ExtraInfo != null)
            {
                command.ExtraInfo = new ExtraCommandInfo();
                PropertyUtil.CopyProperties(ExtraInfo, command.ExtraInfo);
            }
            return command;
        }
    }
}

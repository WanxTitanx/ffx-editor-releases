using System;
using System.Collections.Generic;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.FfxLib.Ability
{
    public enum SpiraReforgeSpellPack
    {
        Kimahri,
        Lulu,
        Yuna,
        Wakka,
        Rikku,
        Tidus,
        Auron,
    }

    public sealed class SpiraReforgeSpellSpec
    {
        public required SpiraReforgeSpellPack Pack { get; init; }
        public required string Name { get; init; }
        public required int DonorId { get; init; }
        public required Character_Enum Owner { get; init; }
        public required Action<Ability_Command> ApplyRecipe { get; init; }
        public bool IsMenuOpener { get; init; }
    }

    /// <summary>
    /// Authoring recipes for Spira Reforge per-character extended commands (wards stay in
    /// <see cref="CommandGrowWriter"/>). Each spec clones a vanilla donor row and applies
    /// <see cref="SpiraReforgeSpellSpec.ApplyRecipe"/> before append.
    /// </summary>
    public static class SpiraReforgeExtendedCommandRecipes
    {
        /// <summary>Engine loop2 case 0xE — Kimahri Blue Magic (+232, 32 slots). NOT case 4 (+296 OD/Ronso).</summary>
        public const byte KimahriBlueMagicSubMenu = PartySpecialSubMenu;
        /// <summary>Engine loop2 case 3 — party Skill submenu (+168). Not 4 (Kimahri OD / +296).</summary>
        public const byte PartySkillSubMenu = 3;
        /// <summary>Engine loop2 case 0xE — +232 array (32 slots). White sub=2 is 24/24 full in vanilla.</summary>
        public const byte PartySpecialSubMenu = 14;
        /// <summary>Engine loop2 case 1 — Black Magic submenu.</summary>
        public const byte BlackMagicSubMenu = 1;
        public const string KimahriBlueMagicMenuName = "Blue Magic";
        /// <summary>Vanilla Special header (#276) — NOT Ronso Rage (#282) which opens the OD +296 ring.</summary>
        public const int KimahriBlueMagicMenuDonorId = 276;
        /// <summary>Vanilla Wht Magic header (#278) — donor for Yuna menu opener row.</summary>
        public const int YunaWhiteMagicMenuDonorId = 278;
        public const string YunaWhiteMagicPlusMenuName = "White Magic+";
        /// <summary>
        /// Engine loop2 case 4 (+296). Yuna-only center ring; Kimahri Blue Magic uses case 14 (+232).
        /// </summary>
        public const byte YunaWhiteMagicPlusSubMenu = (byte)KimahriExtendedCommandWriter.KimahriRonsoRingSubMenu;
        const byte LongBuffDuration = 12;
        const byte PositiveStatBuffStacks = 2;

        const byte DraingaMpCost = 28;
        const byte OsmoseGaMpCost = 12;
        const byte BioraMpCost = 24;
        const byte BioraAttackPower = 58;
        const byte QuadFoulMpCost = 180;
        const byte GuaranteedStatusChance = 254;
        const byte MassBreakMpCost = 120;

        /// <summary>Multi-* tier: slightly above Firaga (58), SpecialMagic breath formula (RT2: viable vs Dark Aeon without break).</summary>
        const byte MultiGaAttackPower = 62;
        const byte MultiGaMpCost = 180;
        const byte MultiGaHitCount = 3;

        const byte JinxBallAttackPower = 16;
        const byte JinxBallMpCost = 200;
        const byte RikkuHeavySkillAttackPower = 16;
        const byte RikkuHeavySkillMpCost = 180;
        const byte MugraHitCount = 2;
        const byte MuggaHitCount = 1;
        const byte BladestormAttackPower = 16;
        const byte BladestormMpCost = 180;
        const byte BladestormHitCount = 3;
        /// <summary>Skill-ring clone base (Delay Attack #6) — not Quick Hit #21.</summary>
        const int TidusBladestormDonorId = 6;

        /// <summary>Row stays in command.bin for stable ids; not in mod kit — no grid teach / RT2 grant.</summary>
        const string AurochsRushUnusedDescription =
            "UNUSED. Not in Spira Reforge kit. Do not teach or grant (#359).";

        public static IReadOnlyList<SpiraReforgeSpellSpec> GetKimahriSpecs()
        {
            List<SpiraReforgeSpellSpec> specs = [];

            specs.Add(new SpiraReforgeSpellSpec
            {
                Pack = SpiraReforgeSpellPack.Kimahri,
                Name = KimahriBlueMagicMenuName,
                DonorId = KimahriBlueMagicMenuDonorId,
                Owner = Character_Enum.Kimahri,
                IsMenuOpener = true,
                ApplyRecipe = ApplyKimahriBlueMagicMenuRecipe,
            });

            if (KimahriExtendedCommandWriter.RonsoRageDonorIds.Length
                != KimahriExtendedCommandWriter.RonsoBlueMageMpCosts.Length)
            {
                throw new InvalidOperationException("Ronso donor ids and MP cost table length mismatch.");
            }

            for (int i = 0; i < KimahriExtendedCommandWriter.RonsoRageDonorIds.Length; i++)
            {
                int donorId = KimahriExtendedCommandWriter.RonsoRageDonorIds[i];
                byte mpCost = KimahriExtendedCommandWriter.RonsoBlueMageMpCosts[i];
                string donorName = DonorName(donorId);
                specs.Add(new SpiraReforgeSpellSpec
                {
                    Pack = SpiraReforgeSpellPack.Kimahri,
                    Name = donorName,
                    DonorId = donorId,
                    Owner = Character_Enum.Kimahri,
                    ApplyRecipe = cmd => ApplyKimahriRonsoRecipe(cmd, donorName, mpCost),
                });
            }

            specs.Add(new SpiraReforgeSpellSpec
            {
                Pack = SpiraReforgeSpellPack.Kimahri,
                Name = KimahriExtendedCommandWriter.DemitaName,
                DonorId = KimahriExtendedCommandWriter.DemiDonorId,
                Owner = Character_Enum.Kimahri,
                ApplyRecipe = ApplyKimahriDemitaRecipe,
            });

            specs.Add(new SpiraReforgeSpellSpec
            {
                Pack = SpiraReforgeSpellPack.Kimahri,
                Name = KimahriExtendedCommandWriter.LancetPlusName,
                DonorId = KimahriExtendedCommandWriter.LancetDonorId,
                Owner = Character_Enum.Kimahri,
                ApplyRecipe = ApplyKimahriLancetPlusRecipe,
            });

            return specs;
        }

        public static IReadOnlyList<SpiraReforgeSpellSpec> GetLuluSpecs() =>
        [
            MultiGaSpec("Multi-Firaga", 73),
            MultiGaSpec("Multi-Blizzaga", 74),
            MultiGaSpec("Multi-Thundaga", 75),
            MultiGaSpec("Multi-Waterga", 76),
            new SpiraReforgeSpellSpec
            {
                Pack = SpiraReforgeSpellPack.Lulu,
                Name = "Drainga",
                DonorId = 80,
                Owner = Character_Enum.Lulu,
                ApplyRecipe = cmd =>
                {
                    Rename(cmd, "Drainga");
                    SetOwned(cmd, Character_Enum.Lulu);
                    SetMulti(cmd);
                    SetMp(cmd, DraingaMpCost);
                },
            },
            new SpiraReforgeSpellSpec
            {
                Pack = SpiraReforgeSpellPack.Lulu,
                Name = "Osmose-ga",
                DonorId = 81,
                Owner = Character_Enum.Lulu,
                ApplyRecipe = cmd =>
                {
                    Rename(cmd, "Osmose-ga");
                    SetOwned(cmd, Character_Enum.Lulu);
                    SetMulti(cmd);
                    SetMp(cmd, OsmoseGaMpCost);
                },
            },
        ];

        public static IReadOnlyList<SpiraReforgeSpellSpec> GetYunaSpecs() =>
        [
            WhiteGaSpec("Reflectga", 60, 36),
            WhiteGaSpec("Protectga", 59, 30),
            WhiteGaSpec("Shellga", 58, 30),
            WhiteGaSpec("Esuna-ga", 51, 36),
            WhiteGaSpec("Dispelga", 61, 36),
        ];

        /// <summary>Trailing menu openers — appended last so stable ids (#343–347 Yuna *ga, #348 Biora, …) stay put.</summary>
        public static IReadOnlyList<SpiraReforgeSpellSpec> GetTrailingMenuOpenerSpecs() =>
        [
            new SpiraReforgeSpellSpec
            {
                Pack = SpiraReforgeSpellPack.Yuna,
                Name = YunaWhiteMagicPlusMenuName,
                DonorId = YunaWhiteMagicMenuDonorId,
                Owner = Character_Enum.Yuna,
                IsMenuOpener = true,
                ApplyRecipe = ApplyYunaWhiteMagicPlusMenuRecipe,
            },
        ];

        public static IReadOnlyList<SpiraReforgeSpellSpec> GetWakkaSpecs() =>
        [
            new SpiraReforgeSpellSpec
            {
                Pack = SpiraReforgeSpellPack.Lulu,
                Name = "Biora",
                DonorId = 77,
                Owner = Character_Enum.Lulu,
                ApplyRecipe = ApplyLuluBioraRecipe,
            },
            new SpiraReforgeSpellSpec
            {
                Pack = SpiraReforgeSpellPack.Wakka,
                Name = "Sleepra",
                DonorId = 8,
                Owner = Character_Enum.Wakka,
                ApplyRecipe = cmd =>
                {
                    MarkDeactivatedRow(cmd, 349, "Sleepra");
                    SetOwned(cmd, Character_Enum.Wakka);
                },
            },
            new SpiraReforgeSpellSpec
            {
                Pack = SpiraReforgeSpellPack.Wakka,
                Name = "Quad Foul",
                DonorId = 15,
                Owner = Character_Enum.Wakka,
                ApplyRecipe = ApplyWakkaQuadFoulRecipe,
            },
            new SpiraReforgeSpellSpec
            {
                Pack = SpiraReforgeSpellPack.Wakka,
                Name = "Twin Reel",
                DonorId = 13,
                Owner = Character_Enum.Wakka,
                ApplyRecipe = cmd =>
                {
                    MarkDeactivatedRow(cmd, 351, "Twin Reel");
                    SetOwned(cmd, Character_Enum.Wakka);
                },
            },
            new SpiraReforgeSpellSpec
            {
                Pack = SpiraReforgeSpellPack.Wakka,
                Name = "Jinx Ball",
                DonorId = 13,
                Owner = Character_Enum.Wakka,
                ApplyRecipe = ApplyWakkaJinxBallRecipe,
            },
        ];

        public static IReadOnlyList<SpiraReforgeSpellSpec> GetRikkuSpecs() =>
        [
            new SpiraReforgeSpellSpec
            {
                Pack = SpiraReforgeSpellPack.Rikku,
                Name = "Mugra",
                DonorId = 20,
                Owner = Character_Enum.Rikku,
                ApplyRecipe = ApplyRikkuMugraRecipe,
            },
            new SpiraReforgeSpellSpec
            {
                Pack = SpiraReforgeSpellPack.Rikku,
                Name = "Mugga",
                DonorId = 20,
                Owner = Character_Enum.Rikku,
                ApplyRecipe = ApplyRikkuMuggaRecipe,
            },
            new SpiraReforgeSpellSpec
            {
                Pack = SpiraReforgeSpellPack.Rikku,
                Name = "Pickpocket",
                DonorId = 22,
                Owner = Character_Enum.Rikku,
                ApplyRecipe = cmd =>
                {
                    Rename(cmd, "Pickpocket");
                    SetOwned(cmd, Character_Enum.Rikku);
                    SetMp(cmd, 4);
                },
            },
        ];

        public static IReadOnlyList<SpiraReforgeSpellSpec> GetTidusSpecs() =>
        [
            new SpiraReforgeSpellSpec
            {
                Pack = SpiraReforgeSpellPack.Tidus,
                Name = "Spiral Slash",
                DonorId = TidusBladestormDonorId,
                Owner = Character_Enum.Tidus,
                ApplyRecipe = cmd =>
                {
                    MarkDeactivatedRow(cmd, 356, "Spiral Slash");
                    SetOwned(cmd, Character_Enum.Tidus);
                },
            },
            new SpiraReforgeSpellSpec
            {
                Pack = SpiraReforgeSpellPack.Tidus,
                Name = "Tidal Combo",
                DonorId = TidusBladestormDonorId,
                Owner = Character_Enum.Tidus,
                ApplyRecipe = cmd =>
                {
                    MarkDeactivatedRow(cmd, 357, "Tidal Combo");
                    SetOwned(cmd, Character_Enum.Tidus);
                },
            },
            new SpiraReforgeSpellSpec
            {
                Pack = SpiraReforgeSpellPack.Tidus,
                Name = "Bladestorm",
                DonorId = TidusBladestormDonorId,
                Owner = Character_Enum.Tidus,
                ApplyRecipe = ApplyTidusBladestormRecipe,
            },
            new SpiraReforgeSpellSpec
            {
                Pack = SpiraReforgeSpellPack.Tidus,
                Name = "Aurochs Rush",
                DonorId = 99,
                Owner = Character_Enum.Tidus,
                ApplyRecipe = cmd =>
                {
                    Rename(cmd, "Aurochs Rush");
                    SetDescription(cmd, AurochsRushUnusedDescription);
                    SetOwned(cmd, Character_Enum.Tidus);
                    SetPartySkillSubMenu(cmd);
                    SetHitCount(cmd, 5);
                    SetMp(cmd, 16);
                    ClearSelfBuffPayload(cmd);
                },
            },
        ];

        public static IReadOnlyList<SpiraReforgeSpellSpec> GetAuronSpecs() =>
        [
            MassBreakSpec("Mass Power Break", 16, MassBreakMpCost),
            MassBreakSpec("Mass Armor Break", 18, MassBreakMpCost),
            MassBreakSpec("Mass Magic Break", 17, MassBreakMpCost),
            MassBreakSpec("Mass Mental Break", 19, MassBreakMpCost),
            new SpiraReforgeSpellSpec
            {
                Pack = SpiraReforgeSpellPack.Auron,
                Name = "Sentinel++",
                DonorId = 35,
                Owner = Character_Enum.Auron,
                ApplyRecipe = cmd =>
                {
                    Rename(cmd, "Sentinel++");
                    SetOwned(cmd, Character_Enum.Auron);
                    SetMulti(cmd);
                    SetMp(cmd, 32);
                    SetLongBuffStatuses(cmd, protect: true, shell: true, haste: true);
                    SetPositiveStatBuffs(
                        cmd,
                        Ability_Command.StatBuffFlags.Cheer | Ability_Command.StatBuffFlags.Aim | Ability_Command.StatBuffFlags.Focus | Ability_Command.StatBuffFlags.Reflex | Ability_Command.StatBuffFlags.Luck,
                        PositiveStatBuffStacks);
                },
            },
            new SpiraReforgeSpellSpec
            {
                Pack = SpiraReforgeSpellPack.Auron,
                Name = "Provokeja",
                DonorId = 38,
                Owner = Character_Enum.Auron,
                ApplyRecipe = cmd =>
                {
                    Rename(cmd, "Provokeja");
                    SetOwned(cmd, Character_Enum.Auron);
                    SetMulti(cmd);
                    SetMp(cmd, 16);
                    SetLongBuffStatuses(cmd, protect: true, shell: true);
                    SetPositiveStatBuffs(
                        cmd,
                        Ability_Command.StatBuffFlags.Cheer | Ability_Command.StatBuffFlags.Focus | Ability_Command.StatBuffFlags.Luck,
                        PositiveStatBuffStacks);
                },
            },
        ];

        public static IReadOnlyList<SpiraReforgeSpellSpec> GetAllAppendSpecs()
        {
            List<SpiraReforgeSpellSpec> all = [];
            all.AddRange(GetKimahriSpecs());
            all.AddRange(GetLuluSpecs());
            all.AddRange(GetYunaSpecs());
            all.AddRange(GetWakkaSpecs());
            all.AddRange(GetRikkuSpecs());
            all.AddRange(GetTidusSpecs());
            all.AddRange(GetAuronSpecs());
            all.AddRange(GetTrailingMenuOpenerSpecs());
            return all;
        }

        static SpiraReforgeSpellSpec MultiGaSpec(string name, int donorId) =>
            new()
            {
                Pack = SpiraReforgeSpellPack.Lulu,
                Name = name,
                DonorId = donorId,
                Owner = Character_Enum.Lulu,
                ApplyRecipe = cmd =>
                {
                    Rename(cmd, name);
                    SetDescription(cmd, "Three random hits on the enemy party.");
                    SetOwned(cmd, Character_Enum.Lulu);
                    SetRandomEnemyHits(cmd);
                    SetMp(cmd, MultiGaMpCost);
                    SetHitCount(cmd, MultiGaHitCount);
                    cmd.AttackPower = MultiGaAttackPower;
                    SetSpecialMagic(cmd);
                },
            };

        static SpiraReforgeSpellSpec WhiteGaSpec(string name, int donorId, byte mp) =>
            new()
            {
                Pack = SpiraReforgeSpellPack.Yuna,
                Name = name,
                DonorId = donorId,
                Owner = Character_Enum.Yuna,
                ApplyRecipe = cmd =>
                {
                    Rename(cmd, name);
                    SetOwned(cmd, Character_Enum.Yuna);
                    SetYunaWhiteMagicPlusSubMenu(cmd);
                    SetMulti(cmd);
                    SetMp(cmd, mp);
                },
            };

        static SpiraReforgeSpellSpec MassBreakSpec(string name, int donorId, byte mp) =>
            new()
            {
                Pack = SpiraReforgeSpellPack.Auron,
                Name = name,
                DonorId = donorId,
                Owner = Character_Enum.Auron,
                ApplyRecipe = cmd =>
                {
                    Rename(cmd, name);
                    SetOwned(cmd, Character_Enum.Auron);
                    SetMulti(cmd);
                    SetMp(cmd, mp);
                },
            };

        static void ApplyKimahriRonsoRecipe(Ability_Command command, string donorName, byte mpCost)
        {
            SetOwned(command, Character_Enum.Kimahri);
            SetMp(command, mpCost);
            SetKimahriBlueMagicSubMenu(command);
            if (donorName.Equals("Thrust Kick", StringComparison.OrdinalIgnoreCase))
                RemoveFieldEject(command);

            SetDescription(command, $"Blue Mage: cast {donorName} with MP instead of Overdrive.");
        }

        static void ApplyKimahriBlueMagicMenuRecipe(Ability_Command command)
        {
            Rename(command, KimahriBlueMagicMenuName);
            SetDescription(command, "Open Kimahri's learned Blue Magic commands.");
            command.CharacterUser = Character_Enum.Kimahri;
            command.CostMp = 0;
            command.CostOverdrive = 0;
            command.OverdriveCategory = 0;
            SetKimahriBlueMagicSubMenu(command);
            // ponytail: donor #276 Special (sub=14 → +232), NOT #282 Ronso (sub=4 → +296 OD).
            command.FlagMenuMainMenu = true;
            command.FlagMenuOpenCommandMenu = false;
            command.FlagMenuOpenSpecialMenu = true;
        }

        static void ApplyYunaWhiteMagicPlusMenuRecipe(Ability_Command command)
        {
            Rename(command, YunaWhiteMagicPlusMenuName);
            SetDescription(command, "Open Yuna's advanced White Magic commands.");
            command.CharacterUser = Character_Enum.Yuna;
            command.CostMp = 0;
            command.CostOverdrive = 0;
            command.OverdriveCategory = 0;
            SetYunaWhiteMagicPlusSubMenu(command);
            command.FlagMenuMainMenu = true;
            command.FlagMenuOpenCommandMenu = true;
            command.FlagMenuOpenSpecialMenu = false;
        }

        static void ApplyKimahriDemitaRecipe(Ability_Command command)
        {
            Rename(command, KimahriExtendedCommandWriter.DemitaName);
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            SetDescription(command, "Cuts all enemies' HP by half.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            SetOwned(command, Character_Enum.Kimahri);
            SetMp(command, 24);
            SetMulti(command);
            SetKimahriBlueMagicSubMenu(command);
        }

        static void ApplyKimahriLancetPlusRecipe(Ability_Command command)
        {
            Rename(command, KimahriExtendedCommandWriter.LancetPlusName);
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            SetDescription(command, "Learn enemy abilities as usable commands.");
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
            SetOwned(command, Character_Enum.Kimahri);
            SetKimahriBlueMagicSubMenu(command);
        }

        static void SetOwned(Ability_Command command, Character_Enum owner)
        {
            command.CharacterUser = owner;
            command.CostOverdrive = 0;
            command.OverdriveCategory = 0;
            // ponytail: submenu placement only — MainMenu routes to the left ring (Attack-level).
            // Menu openers (#322 Kimahri Blue Magic) set MainMenu in their own recipe.
        }

        static void SetKimahriBlueMagicSubMenu(Ability_Command command)
        {
            command.SubMenuCategorization = KimahriBlueMagicSubMenu;
            command.SubSubMenuCategorization = KimahriBlueMagicSubMenu;
        }

        static void SetYunaWhiteMagicPlusSubMenu(Ability_Command command)
        {
            command.SubMenuCategorization = YunaWhiteMagicPlusSubMenu;
            command.SubSubMenuCategorization = YunaWhiteMagicPlusSubMenu;
        }

        static void SetPartySkillSubMenu(Ability_Command command)
        {
            command.SubMenuCategorization = PartySkillSubMenu;
            command.SubSubMenuCategorization = PartySkillSubMenu;
        }

        static void SetPartySpecialSubMenu(Ability_Command command)
        {
            command.SubMenuCategorization = PartySpecialSubMenu;
            command.SubSubMenuCategorization = PartySpecialSubMenu;
        }

        static void SetBlackMagicSubMenu(Ability_Command command)
        {
            command.SubMenuCategorization = BlackMagicSubMenu;
            command.SubSubMenuCategorization = BlackMagicSubMenu;
        }

        static void ApplyLuluBioraRecipe(Ability_Command command)
        {
            Rename(command, "Biora");
            SetDescription(command, "Poison and fire damage on all foes.");
            SetOwned(command, Character_Enum.Lulu);
            SetBlackMagicSubMenu(command);
            SetMulti(command);
            SetMp(command, BioraMpCost);
            command.AttackPower = BioraAttackPower;
            command.FlagElementFire = true;
            command.StatusChance.Poison = GuaranteedStatusChance;
        }

        static void ApplyWakkaQuadFoulRecipe(Ability_Command command)
        {
            Rename(command, "Quad Foul");
            SetDescription(command,
                "Sleep, silence, darkness, and poison on all foes for 3 turns.");
            SetOwned(command, Character_Enum.Wakka);
            SetMulti(command);
            SetMp(command, QuadFoulMpCost);
            command.StatusChance.Poison = GuaranteedStatusChance;
        }

        static void ApplyWakkaJinxBallRecipe(Ability_Command command)
        {
            Rename(command, "Jinx Ball");
            SetDescription(command, "Two-hit strike on one foe.");
            SetOwned(command, Character_Enum.Wakka);
            SetPartySkillSubMenu(command);
            ClearMenuOpenerFlags(command);
            SetHitCount(command, 2);
            SetMp(command, JinxBallMpCost);
            command.AttackPower = JinxBallAttackPower;
            command.FlagTargetMulti = false;
            command.FlagMisc2RandomTargets = false;
            command.FlagMisc3UseWeaponProps = false;
        }

        static void ApplyRikkuMugraRecipe(Ability_Command command)
        {
            Rename(command, "Mugra");
            SetDescription(command, "Two-hit strike and steal attempt on one foe.");
            SetOwned(command, Character_Enum.Rikku);
            SetHitCount(command, MugraHitCount);
            SetMp(command, RikkuHeavySkillMpCost);
            command.AttackPower = RikkuHeavySkillAttackPower;
            command.FlagTargetMulti = false;
            command.FlagMisc2RandomTargets = false;
            command.FlagMisc3UseWeaponProps = false;
        }

        static void ApplyRikkuMuggaRecipe(Ability_Command command)
        {
            Rename(command, "Mugga");
            SetDescription(command, "Strike and steal attempt on all foes.");
            SetOwned(command, Character_Enum.Rikku);
            SetMulti(command);
            SetHitCount(command, MuggaHitCount);
            SetMp(command, RikkuHeavySkillMpCost);
            command.AttackPower = RikkuHeavySkillAttackPower;
            command.FlagMisc2RandomTargets = false;
            command.FlagMisc3UseWeaponProps = false;
        }

        static void ApplyTidusBladestormRecipe(Ability_Command command)
        {
            Rename(command, "Bladestorm");
            SetDescription(command, "Three-hit strike on one foe.");
            SetOwned(command, Character_Enum.Tidus);
            SetPartySkillSubMenu(command);
            SetHitCount(command, BladestormHitCount);
            SetMp(command, BladestormMpCost);
            command.AttackPower = BladestormAttackPower;
            command.FlagTargetMulti = false;
            command.FlagMisc2RandomTargets = false;
            ClearSelfBuffPayload(command);
        }

        static void MarkDeactivatedRow(Ability_Command command, int commandId, string displayName)
        {
            Rename(command, displayName);
            ApplyDeactivatedRowDescription(command, commandId);
        }

        public static void ApplyDeactivatedRowDescription(Ability_Command command, int commandId) =>
            SetDescription(command, DeactivatedRowDescription(commandId));

        static string DeactivatedRowDescription(int commandId) =>
            $"UNUSED. Not in Spira Reforge kit. Do not teach or grant (#{commandId}).";

        /// <summary>Donor was Attack (#0) — strip main-ring menu flags so Skill placement sticks.</summary>
        static void ClearMenuOpenerFlags(Ability_Command command)
        {
            command.FlagMenuMainMenu = false;
            command.FlagMenuOpenCommandMenu = false;
            command.FlagMenuOpenSpecialMenu = false;
        }

        static void ClearSelfBuffPayload(Ability_Command command)
        {
            command.StatBuffFlgs = 0;
            command.StatBuffValue = 0;
            command.StatusChance.Haste = 0;
            command.StatusDuration.Haste = 0;
            command.StatusChance.Protect = 0;
            command.StatusDuration.Protect = 0;
            command.StatusChance.Shell = 0;
            command.StatusDuration.Shell = 0;
        }

        static void SetMulti(Ability_Command command) => command.FlagTargetMulti = true;

        /// <summary>N hits distributed across random foes (Fury / Slice and Dice pattern), not all-enemies Multi.</summary>
        static void SetRandomEnemyHits(Ability_Command command)
        {
            command.FlagTargetMulti = false;
            command.FlagMisc2RandomTargets = true;
        }

        static void SetHitCount(Ability_Command command, byte hitCount) => command.HitCount = hitCount;

        static void SetMp(Ability_Command command, byte mp) => command.CostMp = mp;

        static void SetSpecialMagic(Ability_Command command) =>
            command.DamageFormula = DamageFormula_Enum.SpecialMagic;

        static void SetLongBuffStatuses(
            Ability_Command command,
            bool protect = false,
            bool shell = false,
            bool haste = false)
        {
            if (protect)
            {
                command.StatusChance.Protect = 254;
                command.StatusDuration.Protect = LongBuffDuration;
            }

            if (shell)
            {
                command.StatusChance.Shell = 254;
                command.StatusDuration.Shell = LongBuffDuration;
            }

            if (haste)
            {
                command.StatusChance.Haste = 254;
                command.StatusDuration.Haste = LongBuffDuration;
            }
        }

        static void SetPositiveStatBuffs(
            Ability_Command command,
            Ability_Command.StatBuffFlags flags,
            byte stacks)
        {
            command.StatBuffFlgs |= flags;
            command.StatBuffValue = stacks;
        }

        static void RemoveFieldEject(Ability_Command command)
        {
            command.FlagStatusEject = false;
            command.ShatterChance = 0;
        }

        static void Rename(Ability_Command command, string name)
        {
            command.NameScriptBytes = EncodeUs(name);
            command.JapaneseOnlyText1ScriptBytes = Array.Empty<byte>();
            command.JapaneseOnlyText2ScriptBytes = Array.Empty<byte>();
        }

        static void SetDescription(Ability_Command command, string description) =>
            command.DescriptionScriptBytes = EncodeUs(description);

        static byte[] EncodeUs(string text) =>
            FfxEncoding.EncodeString(text, FfxEncoding.UsEncoder).ByteArray;

        static string DonorName(int donorId) =>
            CommandCharacter_Dictionary.Instance.TryGetValue((ushort)donorId, out string? dictName)
                ? dictName
                : $"#{donorId}";
    }
}

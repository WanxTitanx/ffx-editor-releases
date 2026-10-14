// ============================================================================
// FfxSaveBatchActions — FFXED C0008i/C0009j batch actions ported to native buffer writes
// PURPOSE : a dispatch of save batch presets (all key items, max stats, learn abilities, sphere-grid region
//           edits, blitzball, minigame resets) applied to an in-memory FfxSaveCore. The actionId switch maps
//           directly to the registry's batchActions (FfxSaveRegistry.BatchActionsForSection).
// WHY     : these replicate FFXED's known-good bulk-edit workflows so the editor can offer 1-click presets
//           that are proven on real saves (many are used by Spira Reforge).
// EVIDENCE: FFXED v0.749 C0008i/C0009j; some presets carry hard-coded coordinates (e.g. DarkValeforCoords).
// MAINT   : case 26..30/40/41/45/54/58/59 require a donor save (Import tab) — RequireDonor throws otherwise.
//           Adding a preset user-facing? add the id to FfxSaveRegistry.BatchActionsForSection AND this switch;
//           keep behavior byte-exact to FFXED for the presets already shipped.
// ============================================================================
using System;
using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.Save
{
    /// <summary>
    /// FFXED C0008i / C0009j batch actions ported to native buffer writes.
    /// </summary>
    public static class FfxSaveBatchActions
    {
        static readonly byte[][] SphereTypeRanges =
        [
            [2, 38], [42, 126], [34, 35], [36, 38], [2, 5], [6, 9], [10, 13], [14, 17],
            [18, 21], [22, 25], [26, 29], [30, 33],
        ];

        static readonly byte[] DarkValeforCoords =
        [
            16, 1, 13, 1, 2, 1, 20, 13, 20, 1, 1, 3, 19, 6, 1, 22, 2, 1, 1, 1, 21, 19, 15, 2,
        ];

        public static void Run(FfxSaveCore core, int actionId, int slotIndex = 0, FfxSaveCore? donor = null)
        {
            switch (actionId)
            {
                case 0: GetAllKeyItems(core); CountKeyItems(core); break;
                case 1: GetAllCharacterAbilities(core); break;
                case 3: LearnAllBlitzballTechniques(core); break;
                case 4: GetAllOverdrives(core); break;
                case 5: Get99OfEveryItem(core); break;
                case 6: RemoveFromParty(core); break;
                case 7: MaxAllStats(core); break;
                case 8: GetAllAbilities(core); break;
                case 9: ResetLightningDodgePrizes(core); break;
                case 10:
                    RemoveAllOverdrives(core);
                    SetSphereActivationForCharacter(core, slotIndex, SphereTypeRanges[1], false);
                    break;
                case 11: ActivateAllNodes(core); break;
                case 12: ActivateAbilityNodes(core); break;
                case 13: ActivateSpecialNodes(core); break;
                case 14: ActivateStatNodes(core); break;
                case 15: MaxStatNodeActivationValues(core); break;
                case 16: ZeroStatNodeActivationValues(core); break;
                case 17: MaxStatNodeLevels(core); break;
                case 18: MaxCactuarScore(core); break;
                case 19: TeleportAnimaCloisters(core); break;
                case 21: SetMonsterArenaCaptureTimes(core); break;
                case 22: SetSphereActivationForCharacter(core, slotIndex); break;
                case 23: ClearSphereActivationForCharacter(core, slotIndex); break;
                case 24: ResetOmegaRuinsTreasure(core); break;
                case 25: SetLucaTheaterSpheres(core); break;
                case 26: ImportSphereGridRegion(core, RequireDonor(donor)); break;
                case 27: ImportEquipmentRegion(core, RequireDonor(donor)); break;
                case 28: ImportItemsRegion(core, RequireDonor(donor)); break;
                case 29: ImportBlitzballRegion(core, RequireDonor(donor)); break;
                case 30: ImportEntireSave(core, RequireDonor(donor)); break;
                case 31:
                case 34: break;
                case 32: ActivateLockNodes(core); break;
                case 33: MinimizeStatNodeLevels(core); break;
                case 35: ClearSphereGridActivation(core); break;
                case 36: ZeroSphereGridActivation(core); break;
                case 37: SetSphereGridActivationBit(core, slotIndex, true); break;
                case 38: SetSphereGridActivationBit(core, slotIndex, false); break;
                case 40: ImportCharacterBaseStats(core, RequireDonor(donor)); break;
                case 41: ImportMiscCoords(core, RequireDonor(donor)); break;
                case 42: ClearOverdriveModes(core); break;
                case 43: EnableOverdriveModeFlags(core); break;
                case 44: MaxCharacterPanelStats(core); break;
                case 45: ImportSphereLevelsAndAp(core, RequireDonor(donor)); break;
                case 48: MaxAllBlitzballLevels(core); break;
                case 49: LearnAllBlitzballTechniquesFull(core); break;
                case 50: GetAllBlitzballTechFinds(core); break;
                case 51: GetTechFindsForPlayer(core, slotIndex); break;
                case 52: ResetBlitzballTournament(core); break;
                case 54: ImportCharacterAbilities(core, RequireDonor(donor)); break;
                case 55: NewGamePlusCoords(core); break;
                case 56: ResetBikanelSandragoras(core); break;
                case 57: FixDarkValeforGlitch(core); break;
                case 58: ImportOverdrives(core, RequireDonor(donor)); break;
                case 59: ImportOverdriveModes(core, RequireDonor(donor)); break;
                default:
                    throw new NotSupportedException($"Batch action {actionId} is not ported yet.");
            }
        }

        static FfxSaveCore RequireDonor(FfxSaveCore? donor) =>
            donor ?? throw new NotSupportedException("This action needs a donor save (Import tab or donorPath).");

        static void GetAllKeyItems(FfxSaveCore core)
        {
            foreach (FfxSaveKeyItemSnapshot key in FfxSaveKeyItemSnapshot.ReadAll(core))
            {
                key.Value = true;
                key.Write(core);
            }
        }

        static void CountKeyItems(FfxSaveCore core)
        {
            byte count = 0;
            foreach (FfxSaveKeyItemSnapshot key in FfxSaveKeyItemSnapshot.ReadAll(core))
            {
                if (key.Value && key.Bit is >= 22 and <= 47)
                    count++;
            }

            core.Data[3212] = count;
        }

        static void GetAllCharacterAbilities(FfxSaveCore core)
        {
            for (int i = 22090; i <= 24606; i += 148)
            {
                core.WriteBit(i, 1, true);
                for (int b = 6; b < 33; b++) core.WriteBit(i, b, true);
                for (int b = 34; b < 86; b++) core.WriteBit(i, b, true);
                for (int b = 88; b < 96; b++) core.WriteBit(i, b, true);
            }
        }

        static void LearnAllBlitzballTechniques(FfxSaveCore core)
        {
            // Known-techniques are two 4-byte bitmask banks per player (FFXED stride d(3) = i*4):
            // techs 1..30 live at 4652 + player*4 (bits 1..30), techs 31..60 at 4892 + player*4.
            // NOTE: the old code called WriteBit(4652, i>7) — a byte-local op — so only byte 4652
            // ever changed (player 0, techs 0..7). This writes the real per-player banks for all 60.
            for (int p = 0; p < FfxSaveBlitzball.PlayerCount; p++)
            {
                for (int b = 1; b <= 30; b++)
                {
                    core.WriteSaveBit(4652, b, true, p * 4);
                    core.WriteSaveBit(4892, b, true, p * 4);
                }
            }
        }

        static void GetAllOverdrives(FfxSaveCore core)
        {
            for (int i = 0; i <= 2516; i += 148)
            {
                core.Data[i + 15793] |= 0xF8;
                core.Data[i + 15800] = 0xFF;
                core.Data[i + 15801] = 0x07;
            }
        }

        static void Get99OfEveryItem(FfxSaveCore core)
        {
            for (int i = 0; i < 112; i++)
            {
                core.Data[16141 + (i << 1)] = 32;
                core.Data[16140 + (i << 1)] = (byte)i;
                core.Data[16652 + i] = 99;
            }
        }

        static void RemoveFromParty(FfxSaveCore core)
        {
            for (int i = 5; i <= 11; i++)
            {
                byte v = core.Data[i + 15763];
                if (v == 7) v = 8;
                else if (v == 6 && core.ReadBit(273, 0)) v = 7;
                core.Data[i] = v;
            }
        }

        static void MaxAllStats(FfxSaveCore core)
        {
            for (int i = 0; i <= 2516; i += 148)
            {
                WriteBytes(core, i + 22035, 0, 1, 0x86, 0x9F);
                WriteBytes(core, i + 22039, 0, 0, 39, 15);
                WriteBytes(core, i + 22059, 0, 1, 0x86, 0x9F);
                WriteBytes(core, i + 22063, 0, 0, 39, 15);
                for (int j = 22040; j <= 22047; j++) core.Data[i + j] = 0xFF;
                core.Data[i + 22087] = 99;
                core.Data[i + 22088] = 101;
                Array.Clear(core.Data, i + 22055, 4);
                core.Data[i + 22085] = 0xFF;
                WriteBytes(core, i + 22115, 0, 0, 39, 16);
            }
        }

        static void GetAllAbilities(FfxSaveCore core) => GetAllCharacterAbilities(core);

        static void ResetLightningDodgePrizes(FfxSaveCore core)
        {
            core.Data[1076] = 0xFF;
            core.Data[1090] = 0;
            core.WriteBit(1075, 1, true);
            core.WriteBit(1075, 2, false);
            core.WriteBit(1075, 3, false);
        }

        static void MaxStatNodeActivationValues(FfxSaveCore core)
        {
            for (int i = 8748; i <= 10466; i += 2)
            {
                byte b = core.Data[i];
                if ((b > 1 && b < 39) || (b > 41 && b < 127))
                    core.Data[i + 1] = 127;
            }
        }

        static void ZeroStatNodeActivationValues(FfxSaveCore core)
        {
            for (int i = 8749; i <= 10467; i += 2)
                core.Data[i] = 0;
        }

        static void ActivateAllNodes(FfxSaveCore core) =>
            SetNodeRange(core, n => core.Data[n] != 0xFF, 1, 0);

        static void ActivateAbilityNodes(FfxSaveCore core) =>
            SetNodeRange(core, t =>
            {
                sbyte b = (sbyte)core.Data[t];
                return (b < 42 && b >= 0) || b == -127 || b == 127;
            }, 1, 0);

        static void ActivateSpecialNodes(FfxSaveCore core) =>
            SetNodeRange(core, t =>
            {
                sbyte b = (sbyte)core.Data[t];
                return b is 39 or 40 or 0 or 41 or -127 or 127;
            }, 1, 0);

        static void ActivateStatNodes(FfxSaveCore core) =>
            SetNodeRange(core, t =>
            {
                sbyte b = (sbyte)core.Data[t];
                return b > 1 && b < 39;
            }, 1, 0);

        static void ActivateLockNodes(FfxSaveCore core) =>
            SetNodeRange(core, t =>
            {
                byte b = core.Data[t];
                return b > 41 && b != 127;
            }, 1, 0);

        static void MaxStatNodeLevels(FfxSaveCore core)
        {
            for (int i = 8748; i <= 10466; i += 2)
            {
                byte b = core.Data[i];
                if (b < 2) continue;
                core.Data[i] = b switch
                {
                    <= 5 => 5,
                    <= 9 => 9,
                    <= 13 => 13,
                    <= 17 => 17,
                    <= 21 => 21,
                    <= 25 => 25,
                    <= 29 => 29,
                    <= 33 => 33,
                    <= 35 => 35,
                    < 39 => 36,
                    _ => b,
                };
            }
        }

        static void MinimizeStatNodeLevels(FfxSaveCore core)
        {
            for (int i = 8748; i <= 10466; i += 2)
            {
                byte b = core.Data[i];
                if (b < 2) continue;
                core.Data[i] = b switch
                {
                    <= 5 => 2,
                    <= 9 => 6,
                    <= 13 => 10,
                    <= 17 => 14,
                    <= 21 => 18,
                    <= 25 => 22,
                    <= 29 => 26,
                    <= 33 => 30,
                    <= 35 => 34,
                    < 39 => 38,
                    _ => b,
                };
            }
        }

        static void MaxCactuarScore(FfxSaveCore core)
        {
            core.Data[3265] = 0xFF;
            core.WriteBit(3266, 0, true);
            core.WriteBit(3266, 1, true);
            core.WriteBit(3266, 2, true);
        }

        static void TeleportAnimaCloisters(FfxSaveCore core)
        {
            if (core.ReadBit(15693, 6))
                PaintSphereGridPath(core, 62, 54, 13);
            else if (core.ReadBit(15693, 7))
                PaintSphereGridPath(core, 43, 54, 13);
            else
                PaintSphereGridPath(core, 62, 29, 30);
        }

        static void PaintSphereGridPath(FfxSaveCore core, int x, int y, int z)
        {
            int cursor = PaintSegment(core, 8748, 333, 35);
            cursor = PaintSegment(core, cursor, 25, 36);
            cursor = PaintSegment(core, cursor, 63, 5);
            cursor = PaintSegment(core, cursor, 63, 9);
            cursor = PaintSegment(core, cursor, 63, 13);
            cursor = PaintSegment(core, cursor, 63, 17);
            cursor = PaintSegment(core, cursor, x, 21);
            cursor = PaintSegment(core, cursor, y, 25);
            PaintSegment(core, cursor, z, 29);
        }

        static int PaintSegment(FfxSaveCore core, int start, int count, byte value)
        {
            int end = start + (count << 1);
            int i = start;
            while (i < end)
            {
                sbyte b = (sbyte)core.Data[i];
                if ((b < 42 && b >= 0) || b == -127 || b == 127)
                    core.Data[i] = value;
                else
                    end += 2;
                i += 2;
            }

            return i;
        }

        static void SetMonsterArenaCaptureTimes(FfxSaveCore core)
        {
            for (int i = 16972; i <= 17075; i++)
                core.Data[i] = 10;
        }

        static void SetSphereActivationForCharacter(FfxSaveCore core, int characterIndex) =>
            SetSphereActivationForCharacter(core, characterIndex, null, true);

        static void ClearSphereActivationForCharacter(FfxSaveCore core, int characterIndex)
        {
            for (int i = 8749; i <= 10467; i += 2)
                core.WriteBit(i, characterIndex, false);
        }

        static void SetSphereActivationForCharacter(FfxSaveCore core, int characterIndex, byte[]? typeRange, bool value)
        {
            if (characterIndex < 0)
                return;

            if (typeRange == null)
            {
                for (int i = 8748; i <= 10466; i += 2)
                {
                    byte b = core.Data[i];
                    if ((b > 1 && b < 39) || (b > 41 && b < 127))
                        core.WriteBit(i + 1, characterIndex, value);
                }

                return;
            }

            byte min = typeRange[0];
            byte max = typeRange[1];
            if (characterIndex > 6)
            {
                for (int i = 8748; i <= 10466; i += 2)
                {
                    byte b = core.Data[i];
                    if (b >= min && b <= max)
                        core.Data[i + 1] = value ? (byte)127 : (byte)0;
                }

                return;
            }

            for (int i = 8748; i <= 10466; i += 2)
            {
                byte b = core.Data[i];
                if (b >= min && b <= max)
                    core.WriteBit(i + 1, characterIndex, value);
            }
        }

        static void ResetOmegaRuinsTreasure(FfxSaveCore core)
        {
            for (int bit = 3; bit <= 7; bit++) core.WriteBit(1022, bit, false);
            core.WriteBit(1023, 0, false);
            core.WriteBit(1023, 1, false);
        }

        static void SetLucaTheaterSpheres(FfxSaveCore core)
        {
            core.Data[895] = 71;
            core.Data[896] = 50;
            core.Data[906] = 71;
            core.Data[907] = 50;
        }

        static void ImportSphereGridRegion(FfxSaveCore core, FfxSaveCore donor)
        {
            // Legacy FFXED +64-skew slices (node table 8748..10467, activation 11308..12188,
            // per-char cursor/counters 12588..12601 + Anima cloister byte 15693).
            // NOTE: the GAME reads/writes the native runtime table at save+8684
            // (FfxSaveSphereGridRuntimeTable); these slices are the editor's vanilla-860 model.
            // For grids beyond 860 nodes use the native table path (bulk span 8684..13579).
            CopyRegion(core, donor, 8748, 10467 - 8748 + 1);
            CopyRegion(core, donor, 11308, 12188 - 11308 + 1);
            CopyRegion(core, donor, 12588, 12601 - 12588 + 1);
            core.Data[15693] = donor.Data[15693];
        }

        static void ImportEquipmentRegion(FfxSaveCore core, FfxSaveCore donor)
        {
            CopyRegion(core, donor, 17628, 22027 - 17628 + 1);
            for (int i = 22073; i <= 24589; i += 148)
                core.Data[i] = donor.Data[i];
        }

        static void ImportItemsRegion(FfxSaveCore core, FfxSaveCore donor) =>
            CopyRegion(core, donor, 16140, 16907 - 16140 + 1);

        static void ImportBlitzballRegion(FfxSaveCore core, FfxSaveCore donor)
        {
            CopyRegion(core, donor, 3252, 3257 - 3252 + 1);
            CopyRegion(core, donor, 4652, 5131 - 4652 + 1);
            CopyRegion(core, donor, 5266, 5685 - 5266 + 1);
            CopyRegion(core, donor, 5702, 5749 - 5702 + 1);
            CopyRegion(core, donor, 5974, 6033 - 5974 + 1);
            CopyRegion(core, donor, 6036, 6155 - 6036 + 1);
            CopyRegion(core, donor, 6356, 6715 - 6356 + 1);
            CopyRegion(core, donor, 6732, 7211 - 6732 + 1);
        }

        static void ClearSphereGridActivation(FfxSaveCore core)
        {
            for (int i = 11308; i <= 12188; i++)
                core.Data[i] = 0x7F;
        }

        static void ZeroSphereGridActivation(FfxSaveCore core)
        {
            for (int i = 11308; i <= 12188; i++)
                core.Data[i] = 0;
        }

        static void SetSphereGridActivationBit(FfxSaveCore core, int bitIndex, bool value)
        {
            for (int i = 11308; i <= 12188; i++)
                core.WriteBit(i, bitIndex, value);
        }

        static void ImportCharacterBaseStats(FfxSaveCore core, FfxSaveCore donor)
        {
            for (int i = 0; i <= 2516; i += 148)
                CopyRegion(core, donor, i + 22032, 16);
        }

        static void ImportMiscCoords(FfxSaveCore core, FfxSaveCore donor)
        {
            CopyRegion(core, donor, 250, 2);
            core.Data[248] = donor.Data[248];
            CopyRegion(core, donor, 3116, 2);
        }

        static void ClearOverdriveModes(FfxSaveCore core)
        {
            for (int i = 22164; i <= 24680; i += 148)
            {
                core.Data[i] = 0xFF;
                core.Data[i + 1] = 0xFF;
                core.WriteBit(i + 2, 0, true);
            }

            for (int i = 22124; i <= 24640; i += 148)
            {
                for (int j = 0; j < 34; j++)
                    core.Data[i + j] = 0;
            }
        }

        static void EnableOverdriveModeFlags(FfxSaveCore core)
        {
            for (int i = 22164; i <= 24680; i += 148)
            {
                core.Data[i] = 0xFF;
                core.Data[i + 1] = 0xFF;
                core.WriteBit(i + 2, 0, true);
            }
        }

        static void MaxCharacterPanelStats(FfxSaveCore core) => MaxAllStats(core);

        static void ImportSphereLevelsAndAp(FfxSaveCore core, FfxSaveCore donor)
        {
            for (int i = 22087; i <= 24603; i += 148)
            {
                core.Data[i] = donor.Data[i];
                core.Data[i + 1] = donor.Data[i + 1];
                CopyRegion(core, donor, i - 35, 4);
            }
        }

        static void MaxAllBlitzballLevels(FfxSaveCore core)
        {
            for (int i = 0; i < 60; i++)
            {
                core.Data[5626 + i] = 99;
                core.WriteInt32Le(6036 + i * 2, 9999, 2);
                core.Data[5566 + i] = 5; // capacity bank is contiguous (FFXED action 48 writes 5566..5625)
            }
        }

        static void RemoveAllOverdrives(FfxSaveCore core)
        {
            for (int i = 22090; i <= 24606; i += 148)
            {
                for (int b = 9; b < 96; b++)
                    core.WriteBit(i, b, false);
            }
        }

        static void LearnAllBlitzballTechniquesFull(FfxSaveCore core)
        {
            for (int i = 4652; i <= 4888; i += 4)
            {
                bool keep1 = core.ReadBit(i, 1);
                bool keep2 = core.ReadBit(i, 2);
                WriteBytes(core, i, 0xF8, 0xFF, 0xFF, 127);
                core.WriteBit(i, 1, keep1);
                core.WriteBit(i, 2, keep2);
            }

            for (int i = 4892; i <= 5128; i += 4)
            {
                bool keep = core.ReadBit(i + 3, 6);
                WriteBytes(core, i, 0xFE, 0xFF, 0xFF, 63);
                core.WriteBit(i + 3, 6, keep);
            }

            core.Data[4652] = 0xFE;
            core.Data[4899] = 127;
            LearnAllBlitzballTechniques(core);
        }

        static void GetAllBlitzballTechFinds(FfxSaveCore core)
        {
            byte[] blockA = [0xF8, 0xFF, 0xFF, 127];
            for (int i = 6732; i <= 6968; i += 4)
                WriteBytes(core, i, blockA);
            byte[] blockB = [0xFE, 0xFF, 0xFF, 63];
            for (int i = 6972; i <= 7208; i += 4)
                WriteBytes(core, i, blockB);
        }

        static void GetTechFindsForPlayer(FfxSaveCore core, int playerIndex)
        {
            // Tech-find bitmask uses the same 4-byte-per-player stride as known-techniques
            // (FFXED action 51 writes at playerDelta + 6732/6972, delta = index*4).
            WriteBytes(core, playerIndex * 4 + 6732, 0xF8, 0xFF, 0xFF, 127);
            WriteBytes(core, playerIndex * 4 + 6972, 0xFE, 0xFF, 0xFF, 63);
        }

        static void ResetBlitzballTournament(FfxSaveCore core)
        {
            for (int i = 6476; i <= 6595; i++)
                core.Data[i] = 0;

            int wins = core.ReadInt32Le(6034, 2);
            core.Data[5973] = wins switch
            {
                >= 40 => 9,
                >= 35 => 8,
                >= 25 => 7,
                >= 15 => 6,
                >= 5 => 5,
                _ => 4,
            };

            byte status = core.Data[5855];
            if (status == 20)
            {
                for (int i = 5958; i <= 5971; i++)
                    core.Data[i] = 0;
            }
            else if (status == 21)
            {
                core.Data[5958] = (byte)(core.Data[5790] == core.Data[5786] ? 1 : 0);
                core.Data[5959] = (byte)(core.Data[5790] == core.Data[5785] ? 1 : 0);
                core.Data[5963] = (byte)(core.Data[5791] == core.Data[5789] ? 1 : 0);
                core.Data[5964] = (byte)(core.Data[5791] == core.Data[5788] ? 1 : 0);
                for (int i = 5960; i <= 5971; i++)
                    core.Data[i] = 0;
            }
            else if (status == 22)
            {
                core.Data[5958] = (byte)(core.Data[5790] == core.Data[5786] ? 1 : 0);
                core.Data[5959] = (byte)(core.Data[5790] == core.Data[5785] ? 1 : 0);
                core.Data[5963] = (byte)(core.Data[5791] == core.Data[5789] ? 1 : 0);
                core.Data[5964] = (byte)(core.Data[5791] == core.Data[5788] ? 1 : 0);
                core.Data[5960] = (byte)(core.Data[5792] == core.Data[5784] ? 1 : 0);
                core.Data[5961] = (byte)(core.Data[5792] == core.Data[5790] ? 1 : 0);
                core.Data[5965] = (byte)(core.Data[5793] == core.Data[5787] ? 1 : 0);
                core.Data[5966] = (byte)(core.Data[5793] == core.Data[5791] ? 1 : 0);
                for (int i = 5962; i <= 5971; i++)
                {
                    if (i is 5962 or >= 5967)
                        core.Data[i] = 0;
                }
            }
        }

        static void ImportCharacterAbilities(FfxSaveCore core, FfxSaveCore donor)
        {
            for (int i = 22090; i <= 24606; i += 148)
                CopyRegion(core, donor, i, 12);
        }

        static void NewGamePlusCoords(FfxSaveCore core)
        {
            core.WriteInt32Le(250, 132, 2);
            core.WriteInt32Le(248, 0, 1);
            core.WriteInt32Le(3116, 0, 2);
            core.Data[1068] = 0;
            core.WriteBit(1069, 3, false);
            core.WriteBit(1069, 5, false);
            core.WriteBit(1069, 7, false);
            core.Data[1071] = 0;
            core.WriteBit(1072, 1, false);
            core.WriteBit(1072, 2, false);
            core.Data[916] = 0;
            core.Data[917] = 0;
            core.Data[3387] = 0;
            core.Data[692] = 0;
            core.WriteBit(1073, 3, core.ReadInt32Le(3116, 2) >= 1315);
            core.WriteBit(568, 0, false);
            core.WriteBit(561, 5, false);
            core.Data[16] = core.Data[252];
            core.Data[17] = core.Data[253];
            core.Data[18] = core.Data[254];
            core.Data[19] = core.Data[255];
            for (int i = 5; i <= 11; i++)
            {
                if (core.Data[i] is 6 or 7)
                    core.Data[i] = core.ReadBit(273, 0) ? (byte)7 : (byte)6;
            }
        }

        static void ResetBikanelSandragoras(FfxSaveCore core)
        {
            core.Data[766] = 0;
            core.WriteBit(765, 0, false);
            core.WriteBit(765, 1, false);
        }

        static void FixDarkValeforGlitch(FfxSaveCore core)
        {
            for (int i = 0; i < DarkValeforCoords.Length; i++)
                core.Data[837 + i] = DarkValeforCoords[i];
        }

        static void ImportOverdrives(FfxSaveCore core, FfxSaveCore donor)
        {
            CopyRegion(core, donor, 15788, 13);
            core.WriteBit(15801, 0, donor.ReadBit(15801, 0));
            core.WriteBit(15801, 1, donor.ReadBit(15801, 1));
            core.WriteBit(15801, 2, donor.ReadBit(15801, 2));
            core.WriteBit(15801, 5, donor.ReadBit(15801, 5));
            core.WriteBit(15801, 6, donor.ReadBit(15801, 6));
            core.WriteBit(15802, 1, donor.ReadBit(15802, 1));
            core.WriteBit(15802, 4, donor.ReadBit(15802, 4));
            core.WriteBit(15802, 7, donor.ReadBit(15802, 7));
            core.WriteBit(15803, 2, donor.ReadBit(15803, 2));
            core.WriteBit(15803, 5, donor.ReadBit(15803, 5));
            core.WriteBit(15804, 3, donor.ReadBit(15804, 3));
            core.WriteBit(15805, 2, donor.ReadBit(15805, 2));
            bool preserve = core.ReadBit(15811, 7);
            core.Data[15811] = donor.Data[15811];
            core.WriteBit(15811, 7, preserve);
            CopyRegion(core, donor, 15852, 4);
        }

        static void ImportOverdriveModes(FfxSaveCore core, FfxSaveCore donor)
        {
            for (int i = 0; i <= 2516; i += 148)
            {
                CopyRegion(core, donor, i + 22124, 43);
                core.Data[i + 22084] = donor.Data[i + 22084];
            }
        }

        public static void CopyRegion(FfxSaveCore target, FfxSaveCore donor, int offset, int length) =>
            Array.Copy(donor.Data, offset, target.Data, offset, length);

        public static void ImportEntireSave(FfxSaveCore target, FfxSaveCore donor) =>
            Array.Copy(donor.Data, target.Data, FfxSaveCore.DataSize);

        static void SetNodeRange(FfxSaveCore core, Func<int, bool> predicate, byte type, byte value)
        {
            for (int i = 8748; i <= 10466; i += 2)
            {
                if (!predicate(i))
                    continue;
                core.Data[i] = type;
                core.Data[i + 1] = value;
            }
        }

        static void WriteBytes(FfxSaveCore core, int offset, params byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
                core.Data[offset + i] = bytes[i];
        }
    }
}

using FFXProjectEditor.FfxLib.Save;
using Xunit;

namespace FFXProjectEditor.Tests.FfxLib.Save
{
    /// <summary>
    /// Regression gates for the Blitzball save-layout fixes (2026-09-17).
    ///
    /// The save stores blitzball player fields as SoA banks, not one AoS record:
    ///   technique slots      5266 + index*5 + slot   (AoS 5-byte record — the only one)
    ///   technique capacity   5566 + index            (contiguous 60-byte bank)
    ///   level                5626 + index            (contiguous 60-byte bank)
    ///   known-tech bitmasks  4652 + index*4 / 4892 + index*4 (bits 1..30 each)
    ///   tech-find bitmasks   6732 + index*4 / 6972 + index*4
    ///
    /// Evidence: FFXED v0.749 stride selectors ({i, i*2, i*4, i*5} via d(1)/d(2)/d(3)/d(4))
    /// plus IDA memset(&amp;g_BlitzTechCapacityArr, 5, 0x3C) @ FFX_Encounter_InitStatusEffectBuffers
    /// 0x784660 proving capacity is contiguous. The pre-fix code used stride-5 for capacity
    /// (colliding with Level[] from index 12) and overlapping 1-byte-apart technique reads.
    /// </summary>
    public class FfxSaveBlitzballLayoutTests
    {
        static FfxSaveCore NewCore() => new FfxSaveCore();

        // --- Technique capacity must be the contiguous bank 5566+index ---------------------

        [Fact]
        public void TechniqueCapacity_Write_NeverTouchesLevelBank()
        {
            // The old stride-5 formula landed on Level[0] (5626) already at index 12.
            // Writing capacity for every player must leave the level bank untouched.
            var core = NewCore();
            for (int i = 0; i < FfxSaveBlitzball.PlayerCount; i++)
            {
                var snap = new FfxSaveBlitzballPlayerSnapshot { Index = i, TechniqueCapacity = 5 };
                snap.Write(core);
            }

            for (int i = 0; i < 60; i++)
                Assert.Equal(5, core.Data[5566 + i]);
            for (int i = 0; i < 60; i++)
                Assert.Equal(0, core.Data[5626 + i]); // level bank must be untouched
        }

        [Fact]
        public void TechniqueCapacity_Read_HitsContiguousBank()
        {
            var core = NewCore();
            for (int i = 0; i < 60; i++)
                core.Data[5566 + i] = (byte)(i + 1); // distinct sentinel per slot

            for (int i = 0; i < 60; i++)
                Assert.Equal(i + 1, FfxSaveBlitzballPlayerSnapshot.Read(core, i).TechniqueCapacity);
        }

        // --- Technique slots must be a disjoint AoS record 5266 + index*5 ------------------

        [Fact]
        public void TechniqueSlots_AreDisjointPerPlayer()
        {
            var core = NewCore();
            var p0 = new FfxSaveBlitzballPlayerSnapshot
            {
                Index = 0,
                Technique1 = 38, Technique2 = 11, Technique3 = 15, Technique4 = 26, Technique5 = 40,
            };
            p0.Write(core);

            // Player 0's record must occupy 5266..5270 and player 1's 5271..5275 — no overlap.
            Assert.Equal(new byte[] { 38, 11, 15, 26, 40 }, core.Data[5266..5271]);
            Assert.All(core.Data[5271..5276], b => Assert.Equal(0, b));
        }

        [Fact]
        public void TechniqueSlots_RoundTrip_AllPlayers()
        {
            var core = NewCore();
            for (int i = 0; i < 60; i++)
            {
                var snap = new FfxSaveBlitzballPlayerSnapshot
                {
                    Index = i,
                    Technique1 = (byte)(i + 1),
                    Technique2 = (byte)(i + 2),
                    Technique3 = (byte)(i + 3),
                    Technique4 = (byte)(i + 4),
                    Technique5 = (byte)(i + 5),
                };
                snap.Write(core);
            }

            for (int i = 0; i < 60; i++)
            {
                var snap = FfxSaveBlitzballPlayerSnapshot.Read(core, i);
                Assert.Equal(i + 1, snap.Technique1);
                Assert.Equal(i + 5, snap.Technique5);
                // Slot k must live at 5266 + i*5 + k (not the old overlapping 5266+i+k).
                Assert.Equal(i + 1, core.Data[5266 + i * 5]);
                Assert.Equal(i + 5, core.Data[5270 + i * 5]);
            }
        }

        // --- Batch actions -----------------------------------------------------------------

        [Fact]
        public void MaxAllBlitzballLevels_WritesCapacityContiguously()
        {
            var core = NewCore();
            FfxSaveBatchActions.Run(core, 48);

            for (int i = 0; i < 60; i++)
            {
                Assert.Equal(99, core.Data[5626 + i]);   // level bank
                Assert.Equal(5, core.Data[5566 + i]);    // capacity bank — contiguous
            }
        }

        [Fact]
        public void LearnAllBlitzballTechniques_SetsBothBanksForAllPlayers()
        {
            var core = NewCore();
            FfxSaveBatchActions.Run(core, 3);

            // Bank A (techs 1..30): bits 1..30 set -> bytes 0..3 = 0xFE,0xFF,0xFF,0x7F per player.
            for (int p = 0; p < 60; p++)
            {
                int a = 4652 + p * 4;
                Assert.Equal(0xFE, core.Data[a]);
                Assert.Equal(0xFF, core.Data[a + 1]);
                Assert.Equal(0xFF, core.Data[a + 2]);
                Assert.Equal(0x7F, core.Data[a + 3]);
                int b = 4892 + p * 4;
                Assert.Equal(0xFE, core.Data[b]);
                Assert.Equal(0xFF, core.Data[b + 1]);
                Assert.Equal(0xFF, core.Data[b + 2]);
                Assert.Equal(0x7F, core.Data[b + 3]);
            }
        }

        [Fact]
        public void GetTechFindsForPlayer_UsesPlayerStride4()
        {
            var core = NewCore();
            FfxSaveBatchActions.Run(core, 51, slotIndex: 7);

            // Player 7 -> 6732 + 7*4 = 6760 and 6972 + 28 = 7000 (old code used +index, i.e. 6739).
            Assert.Equal(0xF8, core.Data[6760]);
            Assert.Equal(127, core.Data[6763]);
            Assert.Equal(0xFE, core.Data[7000]);
            Assert.Equal(63, core.Data[7003]);
            Assert.Equal(0, core.Data[6739]); // nothing may be written at the old wrong offset
        }
    }
}

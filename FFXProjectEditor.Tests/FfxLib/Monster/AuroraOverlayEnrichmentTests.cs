using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.Monster;
using Xunit;

namespace FFXProjectEditor.Tests.FfxLib.Monster
{
    /// <summary>
    /// RT0 gates for the Aurora enrichment layer (F1/F3/F5 of the 2026-08-01 Aurora plan):
    ///   1. MonsterAthAnimCatalog — .ath named animation cycles imported as data (source: fahrenheit .ath tables).
    ///   2. GltfContainerWriter — GLB container conversion of the single-buffer data-URI glTFs the export lab emits.
    /// Both are pure-data / pure-format checks: byte-identity of the game assets is untouched (read-only catalog).
    /// </summary>
    public class AuroraOverlayEnrichmentTests
    {
        [Fact]
        public void AthCatalog_NormalizeMonsterId_HandlesAllShapes()
        {
            Assert.Equal("m117", MonsterAthAnimCatalog.NormalizeMonsterId("m117"));
            Assert.Equal("m117", MonsterAthAnimCatalog.NormalizeMonsterId("117"));
            Assert.Equal("m117", MonsterAthAnimCatalog.NormalizeMonsterId("M117"));
            Assert.Equal("m001", MonsterAthAnimCatalog.NormalizeMonsterId("1"));
            Assert.Equal("", MonsterAthAnimCatalog.NormalizeMonsterId(""));
            Assert.Equal("", MonsterAthAnimCatalog.NormalizeMonsterId(null));
        }

        [Fact]
        public void AthCatalog_ImportedTables_NonEmptyAndStable()
        {
            // 3 monsters with cycle tables (m109, m117, m124) — source: fahrenheit MonsterAnimationCycle.cs.
            Assert.True(MonsterAthAnimCatalog.ImportedMonsters.Count >= 3,
                "expected at least the 3 imported .ath cycle tables");
            Assert.True(MonsterAthAnimCatalog.ImportedCycleCount >= 15,
                "expected the documented cycle entries");
            Assert.All(MonsterAthAnimCatalog.ImportedMonsters, m =>
                Assert.StartsWith("m", m, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void AthCatalog_Cycles_HaveUniqueNamesPerMonster()
        {
            foreach (string monster in MonsterAthAnimCatalog.ImportedMonsters)
            {
                IReadOnlyList<MonsterAthAnimCatalog.AnimEntry> cycles =
                    MonsterAthAnimCatalog.GetCycles(monster);
                Assert.NotEmpty(cycles);
                // The game .ath legitimately shares numeric ids between aliases (e.g. m124_lookdown_act01_e and
                // m124_naname_miru_loop01_s both == 29), so the uniqueness invariant is on the NAME, not the id.
                Assert.Equal(cycles.Count, cycles.Select(c => c.Name).Distinct().Count());
                Assert.All(cycles, c =>
                {
                    Assert.False(string.IsNullOrWhiteSpace(c.Name));
                    Assert.True(c.Id >= 0);
                });
            }
        }

        [Fact]
        public void EncounterIndexBridge_MatchesByIdentity_AndReportsUnmatched()
        {
            string dir = Path.Combine(Path.GetTempPath(), "aurora_encidx_rt0_" + Guid.NewGuid().ToString("N"));
            string btl = Path.Combine(dir, "btl");
            string enc = Path.Combine(dir, "0e");
            Directory.CreateDirectory(Path.Combine(btl, "azit03_00"));
            Directory.CreateDirectory(Path.Combine(btl, "solo00_00"));
            Directory.CreateDirectory(enc);
            try
            {
                byte[] battle = { 0x01, 0x00, 0x00, 0x00, 0xAB, 0xCD, 0xEF, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 };
                byte[] other = { 0x09, 0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01 };
                File.WriteAllBytes(Path.Combine(btl, "azit03_00", "azit03_00.bin"), battle);
                File.WriteAllBytes(Path.Combine(btl, "solo00_00", "solo00_00.bin"), other);
                File.WriteAllBytes(Path.Combine(enc, "000A.bin"), other); // 10 = 0xA
                File.WriteAllBytes(Path.Combine(enc, "00EF.bin"), battle);

                FFXProjectEditor.FfxLib.Battle.EncounterIndexBridge index =
                    FFXProjectEditor.FfxLib.Battle.EncounterIndexBridge.Build(btl, enc);

                Assert.Equal(2, index.CorpusBattleCount);
                Assert.Equal(2, index.NoclipEncounterCount);
                Assert.Equal(2, index.BattleIdToEncounter.Count);
                Assert.True(index.TryResolve("azit03_00", out int encId));
                Assert.Equal(0xEF, encId);
                Assert.True(index.TryResolve("solo00_00", out int encId2));
                Assert.Equal(0x0A, encId2);
                Assert.Empty(index.UnmatchedBattleIds);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
            }
        }

        [Fact]
        public void EncounterIndexBridge_UnmatchedWhenHashDiffers()
        {
            string dir = Path.Combine(Path.GetTempPath(), "aurora_encidx_rt0_" + Guid.NewGuid().ToString("N"));
            string btl = Path.Combine(dir, "btl");
            string enc = Path.Combine(dir, "0e");
            Directory.CreateDirectory(Path.Combine(btl, "azit03_00"));
            Directory.CreateDirectory(enc);
            try
            {
                File.WriteAllBytes(Path.Combine(btl, "azit03_00", "azit03_00.bin"), new byte[] { 1, 2, 3 });
                File.WriteAllBytes(Path.Combine(enc, "0001.bin"), new byte[] { 4, 5, 6 });

                FFXProjectEditor.FfxLib.Battle.EncounterIndexBridge index =
                    FFXProjectEditor.FfxLib.Battle.EncounterIndexBridge.Build(btl, enc);

                Assert.False(index.TryResolve("azit03_00", out _));
                Assert.Single(index.UnmatchedBattleIds);
                Assert.Equal("azit03_00", index.UnmatchedBattleIds[0]);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
            }
        }

        [Fact]
        public void AthCatalog_KnownMonster_ReturnsDocumentedNames()
        {
            // Spot-check a documented entry: m117 bat position loop start == 70.
            MonsterAthAnimCatalog.AnimEntry? batPos =
                MonsterAthAnimCatalog.GetCycles("m117")
                    .FirstOrDefault(c => c.Name == "m117_bat_pos_loop01_s");
            Assert.NotNull(batPos);
            Assert.Equal(70, batPos.Id);
            // m001 koura motion ids are documented as 0x100110xx.
            IReadOnlyList<MonsterAthAnimCatalog.AnimEntry> motions = MonsterAthAnimCatalog.GetMotions("m001");
            Assert.NotEmpty(motions);
            Assert.Contains(motions, m => m.Id == 0x1001106D && m.Name == "m001_sp3_07");
        }

        [Fact]
        public void AthCatalog_UnknownMonster_ReturnsEmptyHonestly()
        {
            Assert.Empty(MonsterAthAnimCatalog.GetCycles("m999"));
            Assert.Empty(MonsterAthAnimCatalog.GetMotions("m999"));
        }

        [Fact]
        public void GlbWriter_ConvertsSingleBufferDataUriGltf()
        {
            string dir = Path.Combine(Path.GetTempPath(), "aurora_glb_rt0_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string gltf = Path.Combine(dir, "min.gltf");
                string glb = Path.Combine(dir, "min.glb");
                const string b64 = "AAAAAP//AAD//wAA"; // 12 bytes
                File.WriteAllText(gltf, $@"{{
  ""asset"": {{ ""version"": ""2.0"" }},
  ""buffers"": [ {{ ""byteLength"": 12, ""uri"": ""data:application/octet-stream;base64,{b64}"" }} ],
  ""bufferViews"": [ {{ ""buffer"": 0, ""byteOffset"": 0, ""byteLength"": 12 }} ],
  ""accessors"": [ {{ ""bufferView"": 0, ""componentType"": 5126, ""count"": 3, ""type"": ""VEC3"" }} ],
  ""meshes"": [ {{ ""primitives"": [ {{ ""attributes"": {{ ""POSITION"": 0 }} }} ] }} ],
  ""nodes"": [ {{ ""mesh"": 0 }} ],
  ""scenes"": [ {{ ""nodes"": [ 0 ] }} ],
  ""scene"": 0
}}");

                Assert.True(PhyreModelExportLab.GltfContainerWriter.TryConvertToGlb(gltf, glb));
                byte[] b = File.ReadAllBytes(glb);

                // magic "glTF" + version 2 + total == file length.
                Assert.Equal(0x46546C67u, BitConverter.ToUInt32(b, 0));
                Assert.Equal(2u, BitConverter.ToUInt32(b, 4));
                Assert.Equal((uint)b.Length, BitConverter.ToUInt32(b, 8));

                // JSON chunk: type "JSON", parses, buffer[0] has NO uri (implicit GLB bin).
                uint jsonLen = BitConverter.ToUInt32(b, 12);
                Assert.Equal(0x4E4F534Au, BitConverter.ToUInt32(b, 16));
                string json = System.Text.Encoding.UTF8.GetString(b, 20, (int)jsonLen);
                using (var doc = System.Text.Json.JsonDocument.Parse(json))
                {
                    Assert.Equal(System.Text.Json.JsonValueKind.Array, doc.RootElement.GetProperty("buffers").ValueKind);
                    Assert.False(doc.RootElement.GetProperty("buffers")[0].TryGetProperty("uri", out _));
                }

                // BIN chunk right after padded JSON.
                int jsonPad = (int)((jsonLen + 3u) & ~3u);
                uint binLen = BitConverter.ToUInt32(b, 20 + jsonPad);
                Assert.Equal(12u, binLen);
                Assert.Equal(0x004E4942u, BitConverter.ToUInt32(b, 24 + jsonPad));
                Assert.Equal(12, (int)binLen);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
            }
        }

        [Fact]
        public void GlbWriter_RejectsExternalOrMultipleBuffers()
        {
            string dir = Path.Combine(Path.GetTempPath(), "aurora_glb_rt0_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string external = Path.Combine(dir, "external.gltf");
                File.WriteAllText(external, @"{""asset"":{""version"":""2.0""},""buffers"":[{""uri"":""buf.bin"",""byteLength"":4}]}");
                Assert.False(PhyreModelExportLab.GltfContainerWriter.TryConvertToGlb(external, Path.Combine(dir, "external.glb")));
                Assert.False(File.Exists(Path.Combine(dir, "external.glb")));

                string multi = Path.Combine(dir, "multi.gltf");
                File.WriteAllText(multi,
                    @"{""asset"":{""version"":""2.0""},""buffers"":[{""uri"":""data:application/octet-stream;base64,AAAA""},{""uri"":""data:application/octet-stream;base64,AAAA""}]}");
                Assert.False(PhyreModelExportLab.GltfContainerWriter.TryConvertToGlb(multi, Path.Combine(dir, "multi.glb")));
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
            }
        }
    }
}

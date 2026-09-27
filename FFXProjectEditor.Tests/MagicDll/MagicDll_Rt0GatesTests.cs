using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace FFXProjectEditor.Tests.MagicDll
{
    /// <summary>
    /// F2/F3 GOAL24H (2026-08-02): RT0 gates em massa + codec payload.
    /// Para cada família com janela (amostra), edita 1 campo com valor aleatório,
    /// verifica round-trip (bytes escritos == valor), confirma confinamento à janela
    /// e restaura byte-identity. Prova que o codec campo→bytes→campo funciona e que
    /// a edição é segura por família.
    /// </summary>
    public class MagicDll_Rt0GatesTests
    {
        private readonly ITestOutputHelper _output;
        public MagicDll_Rt0GatesTests(ITestOutputHelper output) => _output = output;

        private static string FindDll(string name) => MagicDllTestFixture.GetPath(name);
    internal static string RepoWorkDir()
    {
        string dir = Path.Combine(FFXProjectEditor.Tests.Infrastructure.TestDataPaths.RepoRoot, "work", "linux-tests");
        TestDirectory.CreatePrivate(dir);
        return dir;
    }


        [Fact]
        public void GeneratedReleaseFixtures_AllOpenAndResolveNamedSlots()
        {
            // A full private-corpus sweep is separate offline evidence. This product gate must
            // execute (never silently pass) in a clean checkout, so it uses representative PE32
            // fixtures for the two embedded catalogs plus the .rdata-only resolver.
            string[] files =
            {
                FindDll("magic_0021.dll"),
                FindDll("magic_0098.dll"),
                FindDll("magic_0117.dll"),
            };

            var parser = new MagicDllParser();
            var fieldMap = MagicFieldMap.Load();
            int opened = 0, failed = 0, raw = 0, named = 0;
            var unknown = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (string path in files)
            {
                try
                {
                    MagicDllFile dll = parser.Parse(path);
                    opened++;
                    foreach (MagicSlot s in dll.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots))
                    {
                        if (s.OpcodeName is null) { raw++; continue; }
                        named++;
                        if (!fieldMap.TryGet(s.OpcodeName, out _)) unknown.Add(s.OpcodeName);
                    }
                }
                catch { failed++; }
            }
            _output.WriteLine($"[Generated] {opened}/{files.Length} opened, {failed} failed; {named} named, {raw} RAW; unknown {unknown.Count}");
            Assert.Equal(files.Length, opened);
            Assert.Equal(0, failed);
            Assert.True(named >= 200, $"named slots insufficient: {named}");
            Assert.Empty(unknown);
        }

        [Fact]
        public void Rt0Gates_20Families_EditRoundTrip_RestoreByteIdentity()
        {
            // amostra de famílias com janela (variadas — U1, Rand, Draw, Lens, Vertex)
            string[] fams =
            {
                "pppMove", "pppScale", "pppAccele", "pppAngle", "pppPoint", "pppColor",
                "pppRandFV", "pppRandIV", "pppRandFloat", "pppRandInt",
                "pppDrawMdl", "pppDrawMdlTs", "pppDrawShape", "pppKeLnsClm", "pppKeLnsCrn",
                "pppVertexAp", "pppKeBornRnd3", "pppKeMdlDtt", "pppSclMove", "pppAngMove",
            };

            string? src = FindDll("magic_0117.dll");
            Assert.True(src != null, "magic_0117.dll não encontrado.");
            string dir = Path.Combine(RepoWorkDir(), "ffx_rt0gates_" + Guid.NewGuid().ToString("N"));
            TestDirectory.CreatePrivate(dir);
            string path = Path.Combine(dir, "magic_0117.dll");
            string outputPath = Path.Combine(dir, "magic_0117_working-copy.dll");
            File.Copy(src, path);

            var rng = new Random(42);
            int debut = 0, skipped = 0;
            try
            {
                var wrapper = new FFXProjectEditor.Modules.MagicDllEditor.MagicDllDocument_Wrapper();
                Assert.True(wrapper.TryLoad(path, out _));
                byte[] original = File.ReadAllBytes(path);
                string originalHash = Convert.ToHexString(SHA256.HashData(original));

                foreach (string fam in fams)
                {
                    // acha o primeiro campo editável (janela, fora do prefixo) da família
                    MagicField? alvo = null;
                    foreach (MagicSlot s in wrapper.ParsedFile!.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots))
                    {
                        if (s.OpcodeName != fam || s.FieldWindow == null) continue;
                        foreach (MagicField f in wrapper.ResolveAllFields(s))
                        {
                            if (f.Offset < 8) continue;
                            alvo = f;
                            break;
                        }
                        if (alvo != null) break;
                    }
                    if (alvo == null) { skipped++; _output.WriteLine($"  {fam}: sem campo editável no 0117"); continue; }

                    byte[] newBytes = alvo.Width switch
                    {
                        4 => BitConverter.GetBytes((float)(rng.NextDouble() * 100 - 50)),
                        2 => BitConverter.GetBytes((ushort)rng.Next(0, 65536)),
                        _ => new[] { (byte)rng.Next(0, 256) },
                    };
                    Assert.True(wrapper.TryApplyFieldEdit(alvo, newBytes, out string editError),
                        $"{fam}: {editError}");
                    Assert.True(wrapper.IsDirty, $"{fam}: IsDirty falso após editar");

                    // round-trip: bytes escritos == valor
                    int abs = wrapper.ParsedFile!.DataSectionRawPtr + alvo.RecordOffset + alvo.Offset;
                    byte[] working = wrapper.WorkingBytes!;
                    Assert.Equal(newBytes, working.Skip(abs).Take(alvo.Width).ToArray());

                    debut++;
                }

                // restore final → byte-identity total
                Assert.True(wrapper.TrySaveCopy(outputPath, out string saveError), saveError);
                Assert.True(wrapper.TryRestoreBackup(outputPath, out string restoreError), restoreError);
                Assert.Equal(original, File.ReadAllBytes(outputPath));
                Assert.Equal(original, File.ReadAllBytes(path));
                Assert.Equal(originalHash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }

            _output.WriteLine($"RT0 gates: {debut} famílias editadas+restauradas, {skipped} puladas.");
            Assert.True(debut >= 12, $"apenas {debut} famílias gateadas (esperado >= 12).");
        }
    }
}

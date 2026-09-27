using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using FFXProjectEditor.FfxLib.Ability;
using FFXProjectEditor.FfxLib.Dictionaries;
using FFXProjectEditor.Utils.Encoding;

namespace FFXProjectEditor.Tools
{
    /// <summary>Offline preflight: FFX.exe hook bytes + command.bin rows 320/321 — no game launch.</summary>
    internal static class NulWardStaticRt2
    {
        const string DefaultExe =
            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\FFX.exe";

        const string DefaultKernelUs =
            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\data\mods\ffx_ps2\ffx\master\new_uspc\battle\kernel\command.bin";

        const string DefaultHooksDll =
            @"D:\SteamLibrary\steamapps\common\FINAL FANTASY FFX&FFX-2 HD Remaster\modules\ffx-hooks.dll";

        const string DefaultOutputDir = @"work\nul_ward_pack";

        static readonly (string Name, uint Rva, byte[] Expected, bool Critical)[] HookSites =
        {
            ("FFX_Battle_ApplyHitDamage_Loop", 0x00389800u, new byte[] { 0x55, 0x8B, 0xEC, 0x83, 0xEC }, true),
            ("FFX_Btl_ApplyActionResults_Aftermath", 0x0038F0B0u, new byte[] { 0x55, 0x8B, 0xEC, 0x83, 0xEC }, true),
            ("FFX_Field_GetActorRecord", 0x00394030u, new byte[] { 0x55, 0x8B, 0xEC, 0x8B, 0x45 }, true),
            ("FFX_Battle_DamageWriteback_mov", 0x0038EDD9u, new byte[] { 0x89, 0x06, 0x01, 0x81, 0x50 }, true),
            ("FFX_Battle_DamageCap_jle", 0x0038EDD5u, new byte[] { 0x7E, 0x02, 0x8B, 0xC3, 0x89 }, false),
            ("FFX_Battle_ComputeHitDamage", 0x0038E680u, new byte[] { 0x55, 0x8B, 0xEC, 0x81, 0xEC }, true),
        };

        public static int Run(string[] args)
        {
            try
            {
                string exePath = ArgValue(args, "--exe") ?? DefaultExe;
                string kernelPath = ArgValue(args, "--kernel") ?? DefaultKernelUs;
                string hooksDll = ArgValue(args, "--hooks-dll") ?? DefaultHooksDll;
                string outputDir = ArgValue(args, "--output") ?? DefaultOutputDir;

                Console.WriteLine("=== Nul Ward STATIC RT2 (offline preflight) ===");
                Console.WriteLine($"exe       : {exePath}");
                Console.WriteLine($"kernel    : {kernelPath}");
                Console.WriteLine($"hooks dll : {hooksDll}");
                Console.WriteLine($"output    : {outputDir}");

                Directory.CreateDirectory(outputDir);

                var checks = new List<object>();
                int fail = 0;

                fail += CheckExeBytes(exePath, checks);
                fail += CheckKernelRows(kernelPath, checks);
                fail += CheckHooksDll(hooksDll, checks);
                fail += CheckDeployFlags(checks);

                var payload = new
                {
                    generated = DateTime.UtcNow.ToString("o"),
                    encodedCmdOffset = "0x08 (IDA sub_7B0C30 / sub_78CF10 — first u16 @ action+8)",
                    encodedRadiant = "0x3140",
                    encodedUmbral = "0x3141",
                    elemFlagsStackOff = "0x2C (ebp+2Ch var_94 in ComputeHitDamage)",
                    actorBlockTide = "0x60E",
                    actorBlockShock = "0x610",
                    checks,
                    failCount = fail,
                    pass = fail == 0,
                };

                string jsonPath = Path.Combine(outputDir, "nul_ward_static_verdict.json");
                File.WriteAllText(jsonPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
                File.WriteAllText(Path.Combine(outputDir, "NUL_WARD_STATIC_RT2.md"), BuildMarkdown(payload));

                Console.WriteLine($"json: {jsonPath}");
                Console.WriteLine(fail == 0 ? "VERDICT: PASS (offline — 1 RT2 battle still required)" : $"VERDICT: FAIL ({fail} check(s))");
                return fail == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        static int CheckExeBytes(string exePath, List<object> checks)
        {
            if (!File.Exists(exePath))
            {
                checks.Add(Fail("FFX.exe", $"missing: {exePath}"));
                return 1;
            }

            if (!PeImageReader.TryOpen(exePath, out PeImageReader? pe) || pe is null)
            {
                checks.Add(Fail("FFX.exe", "invalid PE image"));
                return 1;
            }

            using (pe)
            {
                int fails = 0;
                foreach (var site in HookSites)
                {
                    pe.TryRead(site.Rva, site.Expected.Length, out byte[] actual);
                    bool ok = actual.AsSpan().SequenceEqual(site.Expected);
                    checks.Add(new
                    {
                        group = "exe",
                        site.Name,
                        rva = $"0x{site.Rva:X8}",
                        expected = Hex(site.Expected),
                        actual = Hex(actual),
                        ok,
                        critical = site.Critical,
                    });
                    if (!ok && site.Critical)
                        fails++;
                }

                return fails;
            }
        }

        static int CheckKernelRows(string kernelPath, List<object> checks)
        {
            if (!File.Exists(kernelPath))
            {
                checks.Add(Fail("command.bin", $"missing: {kernelPath}"));
                return 1;
            }

            byte[] bytes = File.ReadAllBytes(kernelPath);
            var rows = Ability_Command.ReadList(bytes, hasExtraInfo: true);
            int fails = 0;

            if (rows.Count < CommandGrowWriter.UmbralWardCommandId + 1)
            {
                checks.Add(Fail("command.bin", $"expected >={CommandGrowWriter.UmbralWardCommandId + 1} rows, got {rows.Count}"));
                return 1;
            }

            fails += CheckWardRow(checks, rows[CommandGrowWriter.RadiantWardCommandId], "Radiant Ward", 0x10, 146, 146);
            fails += CheckWardRow(checks, rows[CommandGrowWriter.UmbralWardCommandId], "Umbral Ward", 0x80, 170, 170);

            checks.Add(new
            {
                group = "kernel",
                name = "entry_count",
                ok = rows.Count >= 322,
                rows = rows.Count,
                length = bytes.Length,
            });
            if (rows.Count < 322) fails++;

            // Replay the engine's command lookup (FFX_Table_GetEntryByIdRange @0x7AB890) offline:
            // proves GetCommandEntryById(320/321) returns the appended rows, not the row-0 fallback.
            // RE: docs/reverse/FFX_NUL_WARD_TEACH_SURFACE_RE_VERDICT_2026-06-16.md §F.
            var lookup = CommandKernelLookupVerifier.VerifyWards(bytes);
            checks.Add(new
            {
                group = "kernel",
                name = "engine_lookup_resolves",
                ok = lookup.Pass,
                radiantOffset = $"0x{lookup.Radiant.EngineOffset:X}",
                radiantInRange = lookup.Radiant.InRange,
                umbralOffset = $"0x{lookup.Umbral.EngineOffset:X}",
                umbralInRange = lookup.Umbral.InRange,
                detail = lookup.Describe(),
            });
            if (!lookup.Pass) fails++;

            return fails;
        }

        static int CheckWardRow(List<object> checks, Ability_Command row, string label, byte elem, short anim1, short anim2)
        {
            int fails = 0;
            string name = FfxEncoding.DecodeScript(row.NameScriptBytes).GetString(FfxEncoding.UsDecoder, withControlCodes: true);

            void Add(string field, bool ok, object? detail = null)
            {
                checks.Add(new { group = "kernel", ward = label, field, ok, detail });
                if (!ok) fails++;
            }

            Add("name", name.Contains(label, StringComparison.OrdinalIgnoreCase), name);
            Add("element", (byte)row.ElementFlgs == elem, $"0x{(byte)row.ElementFlgs:X2}");
            Add("anim1", row.Anim1Id == anim1, row.Anim1Id);
            Add("anim2", row.Anim2Id == anim2, row.Anim2Id);
            Add("mp2", row.CostMp == 2, row.CostMp);
            Add("rank2", row.MoveRank == 2, row.MoveRank);
            Add("white_submenu", row.SubMenuCategorization == 2, row.SubMenuCategorization);
            return fails;
        }

        static int CheckHooksDll(string dllPath, List<object> checks)
        {
            if (!File.Exists(dllPath))
            {
                checks.Add(Fail("ffx-hooks.dll", $"missing: {dllPath}"));
                return 1;
            }

            byte[] dll = File.ReadAllBytes(dllPath);
            bool hasNulWardString = Encoding.ASCII.GetString(dll).Contains("NulWard", StringComparison.Ordinal);
            checks.Add(new
            {
                group = "dll",
                name = "nul_ward_strings",
                ok = hasNulWardString,
                size = dll.Length,
                path = dllPath,
            });
            return hasNulWardString ? 0 : 1;
        }

        static int CheckDeployFlags(List<object> checks)
        {
            string modules = Path.Combine(
                Path.GetDirectoryName(DefaultHooksDll) ?? "",
                "config");
            int fails = 0;

            foreach (var (file, label) in new[]
            {
                ("nul_ward.flag", "log"),
                ("nul_ward_apply.flag", "apply"),
            })
            {
                string path = Path.Combine(modules, file);
                bool ok = File.Exists(path);
                checks.Add(new { group = "deploy", name = label, file, ok, path });
                if (!ok) fails++;
            }

            string novaDisabled = Path.Combine(modules, "nova_super_damage.flag.disabled-for-nul-ward");
            bool novaOk = File.Exists(novaDisabled) || !File.Exists(Path.Combine(modules, "nova_super_damage.flag"));
            checks.Add(new
            {
                group = "deploy",
                name = "nova_conflict_clear",
                ok = novaOk,
                detail = "nova_super_damage.flag must be absent or renamed aside",
            });
            if (!novaOk) fails++;

            return fails;
        }

        static object Fail(string name, string detail) => new { group = "error", name, ok = false, detail };

        static string Hex(byte[] bytes) => bytes.Length == 0 ? "(empty)" : string.Join(" ", bytes.Select(b => $"{b:X2}"));

        sealed class PeImageReader : IDisposable
        {
            readonly FileStream _stream;
            readonly List<(uint VirtualAddress, uint VirtualSize, uint PointerToRawData)> _sections = new();

            PeImageReader(FileStream stream) => _stream = stream;

            public static bool TryOpen(string path, out PeImageReader? reader)
            {
                reader = null;
                try
                {
                    var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    var pe = new PeImageReader(fs);
                    if (!pe.LoadSections())
                    {
                        pe.Dispose();
                        return false;
                    }

                    reader = pe;
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            bool LoadSections()
            {
                Span<byte> buf = stackalloc byte[4];
                _stream.Seek(0x3C, SeekOrigin.Begin);
                if (_stream.Read(buf) != 4) return false;
                int peOffset = BitConverter.ToInt32(buf);
                _stream.Seek(peOffset + 6, SeekOrigin.Begin);
                if (_stream.Read(buf.Slice(0, 2)) != 2) return false;
                ushort sectionCount = BitConverter.ToUInt16(buf);
                _stream.Seek(peOffset + 20, SeekOrigin.Begin);
                if (_stream.Read(buf.Slice(0, 2)) != 2) return false;
                ushort optionalSize = BitConverter.ToUInt16(buf);
                long sectionTable = peOffset + 24 + optionalSize;
                _stream.Seek(sectionTable, SeekOrigin.Begin);
                var secBuf = new byte[40];
                for (int i = 0; i < sectionCount; i++)
                {
                    if (_stream.Read(secBuf) != 40) return false;
                    uint virtualSize = BitConverter.ToUInt32(secBuf, 8);
                    uint virtualAddress = BitConverter.ToUInt32(secBuf, 12);
                    uint raw = BitConverter.ToUInt32(secBuf, 20);
                    _sections.Add((virtualAddress, virtualSize, raw));
                }

                return _sections.Count > 0;
            }

            public bool TryRead(uint rva, int length, out byte[] data)
            {
                data = Array.Empty<byte>();
                foreach (var sec in _sections)
                {
                    if (rva < sec.VirtualAddress || rva >= sec.VirtualAddress + sec.VirtualSize)
                        continue;
                    uint offset = rva - sec.VirtualAddress + sec.PointerToRawData;
                    if (offset + length > _stream.Length)
                        return false;
                    data = new byte[length];
                    _stream.Seek(offset, SeekOrigin.Begin);
                    return _stream.Read(data, 0, length) == length;
                }

                return false;
            }

            public void Dispose() => _stream.Dispose();
        }

        static string BuildMarkdown(object payload)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Nul Ward — static RT2 (offline)");
            sb.AppendLine();
            sb.AppendLine("Validates **FFX.exe** hook sites, **command.bin** rows 320/321, deploy flags — before in-game RT2.");
            sb.AppendLine();
            sb.AppendLine("```json");
            sb.AppendLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            sb.AppendLine("```");
            return sb.ToString();
        }

        static string? ArgValue(string[] args, string key)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }

            return null;
        }
    }
}

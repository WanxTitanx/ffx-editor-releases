using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXProjectEditor.FfxLib.Ps2
{
    // Read-only reader for the PS2 ".wd" sound bank (Square WD header).
    // Proved facts (docs/history/FFX_PS2_WD_DESCRIPTOR_SEMANTICS_2026-06-02.md +
    // RuntimeTools/ReverseHarness no-edit identity 843/843):
    //   header  : "WD" magic, u16 id, u32 bodySize, u32 nProg, u32 nSamp
    //   prog tab: nProg * u32 at 0x20 (prog[0] = descriptor base)
    //   desc[i] : 0x20 bytes -> f0, sampleBodyOffset, loop, vol, pan, pitch, adsr1, adsr2
    //   bodyStart = align32(descBase + nSamp*0x20)
    //   per-sample body = bodyStart + (sbo - sbo[0]); size = delta of consecutive sbo
    // The actual PCM is PlayStation 4-bit ADPCM and is decoded by the external
    // vgmstream oracle (Ps2VgmStream_Service), not here. This reader never mutates files.
    internal sealed class Ps2WdSample
    {
        public required int Index { get; init; }
        public required uint F0 { get; init; }
        public required uint SampleBodyOffset { get; init; }
        public required uint Loop { get; init; }
        public required byte Vol { get; init; }
        public required byte Pan { get; init; }
        public required ushort Pitch { get; init; }
        public required uint Adsr1 { get; init; }
        public required uint Adsr2 { get; init; }
        public required int AbsOffset { get; init; }
        public required int Size { get; init; }

        // vgmstream addresses subsongs 1-based; descriptor i maps to subsong i+1.
        public int Subsong => Index + 1;
        public string Title => $"#{Index:D3} (subsong {Subsong})";
        public string ByteRange => Size > 0 ? $"{Size:N0} B @ 0x{AbsOffset:X}" : $"@ 0x{AbsOffset:X}";
        public string Envelope => $"vol {Vol} · pan {Pan} · pitch {Pitch} · loop 0x{Loop:X} · adsr {Adsr1:X8}/{Adsr2:X8}";
    }

    internal sealed class Ps2WdBankEntry
    {
        public required string Name { get; init; }
        public required string FullPath { get; init; }
        public required string RelativePath { get; init; }
        public required long FileSize { get; init; }
        public required int Id { get; init; }
        public required uint BodySize { get; init; }
        public required int ProgramCount { get; init; }
        public required int SampleCount { get; init; }
        public required int BodyStart { get; init; }
        public required bool Parsed { get; init; }
        public required string Status { get; init; }
        public required IReadOnlyList<Ps2WdSample> Samples { get; init; }

        public string HeaderSummary => Parsed
            ? $"id {Id} · {ProgramCount} prog · {SampleCount} samp · body {BodySize:N0} B"
            : "unparsed (not a WD bank / variant)";
        public string SizeSummary => $"{FileSize:N0} B on disk";
        public string ListSubtitle => Parsed ? $"{SampleCount} samp · {FileSize:N0} B" : "variant";
    }

    internal sealed class Ps2WdAudioSnapshot
    {
        public required IReadOnlyList<Ps2WdBankEntry> Banks { get; init; }
        public required string OverviewSummary { get; init; }
    }

    internal static class Ps2WdAudioReader
    {
        static ushort U16(byte[] d, int o) => (ushort)(d[o] | (d[o + 1] << 8));
        static uint U32(byte[] d, int o) =>
            (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24));

        public static Ps2WdAudioSnapshot Scan(string? ffxPs2Root)
        {
            List<Ps2WdBankEntry> banks = [];
            if (string.IsNullOrWhiteSpace(ffxPs2Root) || !Directory.Exists(ffxPs2Root))
                return new Ps2WdAudioSnapshot { Banks = banks, OverviewSummary = "ffx_ps2 root not detected." };

            IEnumerable<string> wdFiles;
            try
            {
                wdFiles = Directory.EnumerateFiles(ffxPs2Root, "*.wd", SearchOption.AllDirectories);
            }
            catch
            {
                return new Ps2WdAudioSnapshot { Banks = banks, OverviewSummary = "ffx_ps2 .wd scan failed." };
            }

            foreach (string wdPath in wdFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                Ps2WdBankEntry? entry = TryParse(wdPath, ffxPs2Root);
                if (entry != null)
                    banks.Add(entry);
            }

            int parsed = banks.Count(b => b.Parsed);
            int samples = banks.Sum(b => b.SampleCount);
            return new Ps2WdAudioSnapshot
            {
                Banks = banks,
                OverviewSummary = $"{banks.Count} .wd banks · {parsed} parsed · {samples:N0} sample descriptors"
            };
        }

        static Ps2WdBankEntry? TryParse(string wdPath, string root)
        {
            try
            {
                byte[] d = File.ReadAllBytes(wdPath);
                List<Ps2WdSample> samples = [];

                bool parsed = false;
                int id = 0, bodyStart = 0, progCount = 0, sampCount = 0;
                uint bodySize = 0;

                if (d.Length >= 16 && d[0] == (byte)'W' && d[1] == (byte)'D')
                {
                    id = U16(d, 2);
                    bodySize = U32(d, 4);
                    uint nProg = U32(d, 8);
                    uint nSamp = U32(d, 12);

                    if (nProg > 0 && nSamp > 0 && nProg <= 4096 && nSamp <= 65536
                        && 0x20 + 4 * (long)nProg <= d.Length)
                    {
                        progCount = (int)nProg;
                        sampCount = (int)nSamp;
                        int descBase = (int)U32(d, 0x20);
                        bodyStart = (descBase + sampCount * 0x20 + 0x1F) & ~0x1F;

                        // collect raw descriptors first (sbo needed for delta sizing)
                        var sbo = new uint[sampCount];
                        var raw = new (uint f0, uint loop, byte vol, byte pan, ushort pitch, uint a1, uint a2)[sampCount];
                        bool ok = true;
                        for (int i = 0; i < sampCount; i++)
                        {
                            int p = descBase + i * 0x20;
                            if (p + 0x20 > d.Length) { ok = false; break; }
                            raw[i] = (U32(d, p + 0), U32(d, p + 8), d[p + 12], d[p + 13], U16(d, p + 14), U32(d, p + 16), U32(d, p + 20));
                            sbo[i] = U32(d, p + 4);
                        }

                        if (ok)
                        {
                            uint sbo0 = sbo[0];
                            for (int i = 0; i < sampCount; i++)
                            {
                                long next = i + 1 < sampCount ? sbo[i + 1] : sbo0 + bodySize;
                                int size = (int)Math.Max(0, next - sbo[i]);
                                int abs = bodyStart + (int)(sbo[i] - sbo0);
                                samples.Add(new Ps2WdSample
                                {
                                    Index = i,
                                    F0 = raw[i].f0,
                                    SampleBodyOffset = sbo[i],
                                    Loop = raw[i].loop,
                                    Vol = raw[i].vol,
                                    Pan = raw[i].pan,
                                    Pitch = raw[i].pitch,
                                    Adsr1 = raw[i].a1,
                                    Adsr2 = raw[i].a2,
                                    AbsOffset = abs,
                                    Size = size,
                                });
                            }
                            parsed = true;
                        }
                    }
                }

                return new Ps2WdBankEntry
                {
                    Name = Path.GetFileNameWithoutExtension(wdPath),
                    FullPath = wdPath,
                    RelativePath = Path.GetRelativePath(root, wdPath),
                    FileSize = d.LongLength,
                    Id = id,
                    BodySize = bodySize,
                    ProgramCount = progCount,
                    SampleCount = parsed ? sampCount : 0,
                    BodyStart = bodyStart,
                    Parsed = parsed,
                    Status = parsed ? "proved (structural)" : "variant",
                    Samples = samples,
                };
            }
            catch
            {
                return null;
            }
        }
    }
}

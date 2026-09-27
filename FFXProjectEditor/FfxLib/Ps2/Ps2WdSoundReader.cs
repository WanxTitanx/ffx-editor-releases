using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace FFXProjectEditor.FfxLib.Ps2
{
    // READ-ONLY reader for FFX PS2 `.wd` sound banks (VAG/SPU-ADPCM).
    // PROVED layout: docs/history/FFX_PS2_WD_DESCRIPTOR_SEMANTICS_2026-06-02.md
    //   header 16B [magic 'WD' | u16 id | u32 bodySize | u32 nPrograms | u32 nSamples]
    //   program-pointer table @0x20 (nPrograms u32) -> contiguous nSamples 32B descriptors
    //   body_start = align32(progtab[0] + nSamples*0x20); size = delta of sampleBodyOffsets
    //   SDBse variant: sampleBodyOffset biased by sbo[0] (subtract universally).
    // Validated: 818/842 decode to coherent WAV; ReverseHarness no-edit roundtrip 843/843.
    internal enum Ps2WdState
    {
        Decodable,     // structure parses + all SPU blocks valid (vgmstream-ready)
        Tagged,        // SDBse / variant header present, structure parsed
        MetadataOnly,  // parsed header but block validation incomplete (e.g. stereo/streamed tail)
        Blocked
    }

    internal sealed class Ps2WdDescriptor
    {
        public required int Index { get; init; }
        public required int DescriptorOffset { get; init; }
        public required uint Field0 { get; init; }
        public required uint SampleBodyOffset { get; init; }
        public required uint LoopOrSize { get; init; }
        public required byte Volume { get; init; }
        public required byte Pan { get; init; }
        public required ushort SpuPitch { get; init; }
        public required uint Adsr1 { get; init; }
        public required uint Adsr2 { get; init; }
        public required int BodyStart { get; init; }
        public required int SampleSize { get; init; }
        public int SpuFrames => SampleSize / 16;
        public int ApproxSamples => (SampleSize / 16) * 28;
        public string PitchHex => $"0x{SpuPitch:X4}";
    }

    internal sealed class Ps2WdBank
    {
        public required string Name { get; init; }
        public required string FullPath { get; init; }
        public required long FileSize { get; init; }
        public required ushort Id { get; init; }
        public required uint BodySizeField { get; init; }
        public required uint ProgramCount { get; init; }
        public required uint SampleCount { get; init; }
        public required int BodyStart { get; init; }
        public required string Sha256Prefix { get; init; }
        public required Ps2WdState State { get; init; }
        public required IReadOnlyList<uint> ProgramTable { get; init; }
        public required IReadOnlyList<Ps2WdDescriptor> Descriptors { get; init; }
        public required IReadOnlyList<string> Warnings { get; init; }

        public string StateLabel => State switch
        {
            Ps2WdState.Decodable => "Decodable (SPU-ADPCM)",
            Ps2WdState.Tagged => "Tagged variant (SDBse)",
            Ps2WdState.MetadataOnly => "Metadata only",
            _ => "Blocked"
        };
        public int ValidBlockSamples => Descriptors.Count(d => d.SampleSize > 0 && d.SampleSize % 16 == 0);
    }

    internal static class Ps2WdSoundReader
    {
        private const int DescSize = 0x20;
        private const int OffsetTable = 0x20;

        private static ushort U16(byte[] d, int o) => (ushort)(d[o] | (d[o + 1] << 8));
        private static uint U32(byte[] d, int o) =>
            (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24));

        public static bool LooksLikeWd(string path)
        {
            try
            {
                using var fs = File.OpenRead(path);
                Span<byte> h = stackalloc byte[2];
                return fs.Read(h) == 2 && h[0] == (byte)'W' && h[1] == (byte)'D';
            }
            catch { return false; }
        }

        public static Ps2WdBank? Read(string path)
        {
            byte[] d;
            try { d = File.ReadAllBytes(path); }
            catch { return null; }
            int n = d.Length;
            var warnings = new List<string>();
            if (n < 16 || d[0] != (byte)'W' || d[1] != (byte)'D')
                return null;

            ushort id = U16(d, 2);
            uint bodySize = U32(d, 4);
            uint nProg = U32(d, 8);
            uint nSamp = U32(d, 0x0C);
            if (nProg == 0 || nSamp == 0 || nProg > 4096 || nSamp > 65536)
            {
                warnings.Add("implausible program/sample counts");
                return Blocked(path, d, id, bodySize, nProg, nSamp, warnings);
            }

            var progTab = new List<uint>();
            for (int i = 0; i < (int)nProg; i++)
            {
                int o = OffsetTable + 4 * i;
                if (o + 4 > n) { warnings.Add("program table truncated"); return Blocked(path, d, id, bodySize, nProg, nSamp, warnings); }
                progTab.Add(U32(d, o));
            }

            int descBase = (int)progTab[0];
            int bodyStart = (descBase + (int)nSamp * DescSize + 0x1F) & ~0x1F;

            bool tagged = false;
            // SDBse / ASCII tag between table and descriptors shifts nothing in our model
            // (descBase = progtab[0]) but flag it for the surface.
            for (int p = OffsetTable + (int)nProg * 4; p + 5 <= Math.Min(descBase, n); p++)
            {
                if (d[p] == (byte)'S' && d[p + 1] == (byte)'D' && d[p + 2] == (byte)'B') { tagged = true; break; }
            }

            // parse descriptors (contiguous from descBase)
            var raw = new List<(uint f0, uint sbo, uint loop, byte vol, byte pan, ushort pitch, uint a1, uint a2, int ptr)>();
            for (int i = 0; i < (int)nSamp; i++)
            {
                int pp = descBase + i * DescSize;
                if (pp + DescSize > n) { warnings.Add("descriptor array truncated"); break; }
                raw.Add((U32(d, pp), U32(d, pp + 4), U32(d, pp + 8), d[pp + 12], d[pp + 13],
                         U16(d, pp + 14), U32(d, pp + 16), U32(d, pp + 20), pp));
            }
            if (raw.Count == 0) return Blocked(path, d, id, bodySize, nProg, nSamp, warnings);

            uint sbo0 = raw[0].sbo; // SDBse debias (no-op when 0)

            var descs = new List<Ps2WdDescriptor>();
            for (int i = 0; i < raw.Count; i++)
            {
                int start = bodyStart + (int)raw[i].sbo - (int)sbo0;
                uint? next = null;
                for (int j = i + 1; j < raw.Count; j++)
                    if (raw[j].sbo > raw[i].sbo) { next = raw[j].sbo; break; }
                int size = next.HasValue ? (int)(next.Value - raw[i].sbo) : (n - start);
                if (size < 0) size = 0;
                descs.Add(new Ps2WdDescriptor
                {
                    Index = i,
                    DescriptorOffset = raw[i].ptr,
                    Field0 = raw[i].f0,
                    SampleBodyOffset = raw[i].sbo,
                    LoopOrSize = raw[i].loop,
                    Volume = raw[i].vol,
                    Pan = raw[i].pan,
                    SpuPitch = raw[i].pitch,
                    Adsr1 = raw[i].a1,
                    Adsr2 = raw[i].a2,
                    BodyStart = start,
                    SampleSize = size
                });
            }

            // block validity: shift<=12, filter<=5 over every sample's 16B blocks
            int badBlocks = 0, nonMul16 = 0;
            foreach (var s in descs)
            {
                if (s.SampleSize % 16 != 0) nonMul16++;
                for (int k = 0; k < s.SampleSize / 16; k++)
                {
                    int off = s.BodyStart + k * 16;
                    if (off + 16 > n) break;
                    int shift = d[off] & 0x0F, filt = (d[off] >> 4) & 0x0F;
                    if (shift > 12 || filt > 5) badBlocks++;
                }
            }
            if (nonMul16 > 0) warnings.Add($"{nonMul16} sample(s) not 16-byte aligned");
            if (badBlocks > 0) warnings.Add($"{badBlocks} invalid SPU block(s) (likely stereo/streamed variant)");

            Ps2WdState state = badBlocks == 0 && nonMul16 == 0
                ? (tagged ? Ps2WdState.Tagged : Ps2WdState.Decodable)
                : Ps2WdState.MetadataOnly;

            return new Ps2WdBank
            {
                Name = Path.GetFileName(path),
                FullPath = path,
                FileSize = n,
                Id = id,
                BodySizeField = bodySize,
                ProgramCount = nProg,
                SampleCount = nSamp,
                BodyStart = bodyStart,
                Sha256Prefix = Convert.ToHexString(SHA256.HashData(d)).Substring(0, 16),
                State = state,
                ProgramTable = progTab,
                Descriptors = descs,
                Warnings = warnings
            };
        }

        private static Ps2WdBank Blocked(string path, byte[] d, ushort id, uint bodySize, uint nProg, uint nSamp, List<string> warnings)
            => new Ps2WdBank
            {
                Name = Path.GetFileName(path), FullPath = path, FileSize = d.Length, Id = id,
                BodySizeField = bodySize, ProgramCount = nProg, SampleCount = nSamp, BodyStart = 0,
                Sha256Prefix = Convert.ToHexString(SHA256.HashData(d)).Substring(0, 16),
                State = Ps2WdState.Blocked, ProgramTable = Array.Empty<uint>(),
                Descriptors = Array.Empty<Ps2WdDescriptor>(), Warnings = warnings
            };
    }
}

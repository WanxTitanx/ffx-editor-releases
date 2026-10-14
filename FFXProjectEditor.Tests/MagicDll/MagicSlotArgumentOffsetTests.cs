using System;
using System.IO;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Tests.Infrastructure;
using Xunit;

namespace FFXProjectEditor.Tests.MagicDll
{
    /// <summary>
    /// Gate da janela de campos — VERDADE EMPÍRICA (2026-08-01 + atualização 08-02):
    /// O u32 +0x04 do slot é COMPOSTO: u16 baixo (ParameterOffset) = largura NATIVA da
    /// região de argumentos do handler (100% determinística por handler — U1=0x20/32,
    /// draw-leve=0x10/16, PppMem=0x30/48, stubs=0x04...); u16 alto = quantidade de records
    /// ∈ {1..4}. NÃO é um "argument_relative" somado à record-base (interpretação da
    /// outra lane não se sustenta). A janela de operandos (record+0x10..+0x1F para U1)
    /// é PROVADA por T4 runtime (mutação 42.5→2.0 no Power Break teve efeito visual) e
    /// por decompile (handler lê a2[4..7]).
    /// </summary>
    public class MagicSlotArgumentOffsetTests
    {
        private static string CorpusDir => TestDataPaths.MagicCorpus;
        private static readonly MagicDllParser Parser = new();

        [Fact]
        public void NativeStride_IsHandlerDeterministic_AndHighWordCountsKeyframes()
        {
            // Descoberta 2026-08-02: ParameterOffset (u16 baixo do +4) é determinístico
            // POR HANDLER no 0021 — o mesmo handler NUNCA tem dois ParameterOffset
            // diferentes (validação: 161 slots, tabela handler→lo perfeita).
            string path = Path.Combine(CorpusDir, "magic_0021.dll");
            MagicDllFile dll = Parser.Parse(path);

            var loByHandler = new System.Collections.Generic.Dictionary<uint, System.Collections.Generic.HashSet<ushort>>();
            var hiSeen = new System.Collections.Generic.HashSet<ushort>();
            foreach (var slot in dll.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots))
            {
                if (!loByHandler.TryGetValue(slot.HandlerTableIndex, out var set))
                    loByHandler[slot.HandlerTableIndex] = set = new System.Collections.Generic.HashSet<ushort>();
                set.Add(slot.ParameterOffset);
                hiSeen.Add(slot.Flags);
            }

            // Cada handler tem UM único ParameterOffset (largura nativa).
            Assert.All(loByHandler.Values, s => Assert.Single(s));
            // Quantidades ∈ {1..6} neste controle (0021 usa 1..4; 0086/0087 chegam a 6 — validação
            // cross-DLL 2026-08-02). A leitura atual confirma cada sequência e seu terminador.
            Assert.All(hiSeen, f => Assert.InRange(f, (ushort)1, (ushort)6));
            Assert.All(dll.Roots.SelectMany(r => r.Programs).SelectMany(p => p.Slots), slot =>
            {
                Assert.Null(slot.KeyframeError);
                Assert.Equal((int)slot.KeyframeCount, slot.Keyframes.Count);
            });
            // O U1 (handler 7 = pppSclMove no fp.h local do 0021) tem largura nativa 32.
            Assert.Contains<ushort>(32, loByHandler[7]);
        }
    }
}

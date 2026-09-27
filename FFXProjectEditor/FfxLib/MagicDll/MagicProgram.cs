using System;
using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.MagicDll
{
    /// <summary>
    /// Program PPP: head de 40 bytes + slots de 16B (PARSER_SPEC.md §2.3).
    /// O primeiro program de cada seção vive em section+16; a cadeia é encadeada por
    /// next_relative (+0, rel. à section; 0 = fim). O ÚLTIMO program de cada seção é um
    /// SENTINELA com slot_count=0 e next=0 — ele é contado no ProgramCount (0021: 16 =
    /// 15 reais + 1 sentinela), fiel ao analyze_c3_roots.py.
    /// </summary>
    public sealed class MagicProgram
    {
        private const int ProgramHeadSize = 40;   // +0..+39
        private const int SlotSize = 16;          // PPP_SLOT_SIZE

        /// <summary>Offset absoluto do program no .data.</summary>
        public int ProgramAbs { get; }

        /// <summary>Offset absoluto da seção dona (records dos slots são relativos a ela).</summary>
        public int SectionAbs { get; }

        /// <summary>+0 — next_relative: offset do próximo program (rel. à section); 0 = fim/sentinela.</summary>
        public int NextRelative { get; }

        /// <summary>+4 — resource key (u32). NÃO é offset no .data (spec §4.3): é opaca para o parser da DLL.</summary>
        public uint Key { get; }

        /// <summary>+16 — ponteiro (rel. section) para a CURVA DE ANIMAÇÃO 1 (samples u8 por frame — RE 2026-08-02).
        /// 0 = sem curva. CUIDADO: curvas são COMPARTILHADAS entre programs (pointer-trust).</summary>
        public int Curve1Rel { get; }

        /// <summary>+20 — ponteiro (rel. section) para a CURVA DE ANIMAÇÃO 2 (idem). 0 = sem curva.</summary>
        public int Curve2Rel { get; }

        /// <summary>+36 (u16 alto) — flags do program; +36 (u16 baixo) — id sequencial na cadeia (RE 2026-08-02).</summary>
        public ushort Flags36 { get; }

        /// <summary>+36 (u16 baixo) — id sequencial do program na cadeia (0..N-1).</summary>
        public ushort SeqId { get; }

        /// <summary>+8..+36 — dados do program (29 bytes opacos; +8 costuma ser float de estado).</summary>
        public byte[] Head { get; }

        /// <summary>+38 — slot_count (u16).</summary>
        public ushort SlotCount { get; }

        /// <summary>Slots do program (16B cada a partir de +40).</summary>
        public IReadOnlyList<MagicSlot> Slots { get; }

        public MagicProgram(
            int programAbs, int sectionAbs, int nextRelative, uint key,
            byte[] head, ushort slotCount, IReadOnlyList<MagicSlot> slots,
            int curve1Rel = 0, int curve2Rel = 0, ushort flags36 = 0, ushort seqId = 0)
        {
            ProgramAbs = programAbs;
            SectionAbs = sectionAbs;
            NextRelative = nextRelative;
            Key = key;
            Head = head;
            SlotCount = slotCount;
            Slots = slots;
            Curve1Rel = curve1Rel;
            Curve2Rel = curve2Rel;
            Flags36 = flags36;
            SeqId = seqId;
        }

        /// <summary>
        /// Lê o program (head + slots) do .data. A validação estrutural já foi feita pelo
        /// walker do root (limites garantidos); aqui só se lê e resolve os slots.
        /// </summary>
        internal static MagicProgram Create(
            byte[] data,
            int programAbs,
            int sectionAbs,
            MagicFieldMap? fieldMap,
            IReadOnlyDictionary<int, string>? fpNames)
        {
            int nextRelative = BitConverter.ToInt32(data, programAbs);
            uint key = BitConverter.ToUInt32(data, programAbs + 4);
            ushort slotCount = BitConverter.ToUInt16(data, programAbs + 38);

            // RE 2026-08-02: +16/+20 = curvas de animação (rel. section); +36 = (flags<<16)|seq_id.
            int curve1Rel = BitConverter.ToInt32(data, programAbs + 16);
            int curve2Rel = BitConverter.ToInt32(data, programAbs + 20);
            uint w36 = BitConverter.ToUInt32(data, programAbs + 36);
            ushort flags36 = (ushort)(w36 >> 16);
            ushort seqId = (ushort)(w36 & 0xFFFF);

            // +8..+36 (29 bytes) — dados do program, opacos para o parser.
            byte[] head = new byte[29];
            Array.Copy(data, programAbs + 8, head, 0, 29);

            var slots = new List<MagicSlot>(slotCount);
            for (int si = 0; si < slotCount; si++)
            {
                int slotAbs = programAbs + ProgramHeadSize + SlotSize * si;
                slots.Add(MagicSlot.Create(data, slotAbs, sectionAbs, si, fieldMap, fpNames));
            }

            return new MagicProgram(programAbs, sectionAbs, nextRelative, key, head, slotCount, slots,
                curve1Rel, curve2Rel, flags36, seqId);
        }
    }
}

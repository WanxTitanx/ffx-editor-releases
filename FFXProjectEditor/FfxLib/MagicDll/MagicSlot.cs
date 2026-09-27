using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace FFXProjectEditor.FfxLib.MagicDll
{
    /// <summary>
    /// Slot PPP de 16 bytes dentro de um program (PARSER_SPEC.md §2.4):
    /// handler_table_index u32 (+0), parameter_offset u16 (+4), flags u16 (+6),
    /// primary_callback_relative u32 (+8, relativo à SECTION — armadilha documentada),
    /// secondary_callback_relative u32 (+12).
    ///
    /// SEMÂNTICA REAL DO +4 (RE 2026-08-02, validação offline no corpus 0021):
    /// o u32 +4 é COMPOSTO: u16 baixo = ParameterOffset = largura NATIVA da região de
    /// argumentos do handler (100% determinístico por handler: U1=0x20/32, draw-leve=
    /// 0x10/16, PppMem=0x30/48, KeThRes64=0x40, KeThRes96=0x60, stubs=0x04...);
    /// u16 alto = Flags ∈ {1,2,3,4} = modo/canal do slot (varia por slot, não por handler).
    /// NÃO é um offset de janela — mas é a largura nativa (RecordWidth do runtime).
    ///
    /// O record do slot vive em section_abs + primary_callback_relative. Quando o
    /// handler_table_index resolve para um opcode catalogado com payload_consumer=true,
    /// o campo é interpretado pelo schema da família (janela start/width); caso
    /// contrário o slot permanece RAW (nunca inferir janela por tamanho — spec §6.1).
    /// </summary>
    public sealed class MagicSlot
    {
        /// <summary>Índice do slot dentro do program (0-based).</summary>
        public int SlotIndex { get; }

        /// <summary>Offset absoluto do slot no .data.</summary>
        public int SlotAbs { get; }

        /// <summary>+0 — slot[0]: handle/ponteiro do programa (relocado in-place pelo dispatcher 0x7170F0).
        /// O fp.h local do efeito é a representação da tabela programa[+0x20] (índice→opcode) — o uso como
        /// índice LOCAL é equivalente ao mecanismo real (prova: testes + T3/T4).</summary>
        public uint HandlerTableIndex { get; }

        /// <summary>+4 (u16 baixo) — largura NATIVA da região de argumentos do handler
        /// (determinística por handler; 2026-08-02).</summary>
        public ushort ParameterOffset { get; }

        /// <summary>+6 (u16 alto do u32 +4) — flags/modo do slot (1..4).</summary>
        public ushort Flags { get; }

        /// <summary>Largura nativa do record pelo slot (+4 u16 baixo). 0 quando não informativo.</summary>
        public int NativeRecordWidth => ParameterOffset;

        /// <summary>+8 — offset do RECORD relativo à SECTION (não ao root, não ao program).</summary>
        public uint PrimaryCallbackRel { get; }

        /// <summary>+12 — callback secundário (rel. à section).</summary>
        public uint SecondaryCallbackRel { get; }

        /// <summary>Offset absoluto do record no .data (= section_abs + PrimaryCallbackRel).</summary>
        public int RecordOffset { get; }

        /// <summary>Largura do record lida: schema ? max(32, janela) : 32 (default da spec §6.1).</summary>
        public int RecordWidth { get; }

        /// <summary>Bytes do record (âncora hash-gated). Sempre disponível.</summary>
        public byte[] Record { get; }

        /// <summary>SHA-256 do record em hex (contrato T3 — âncora para escrita futura).</summary>
        public string RecordSha256 { get; }

        /// <summary>Opcode resolvido via fp.h local (ou catálogo embutido). Null = slot RAW.</summary>
        public string? OpcodeName { get; }

        /// <summary>Campo interpretado pelo schema (payload_consumer). Null = sem schema aplicável.</summary>
        public MagicField? Field { get; }

        /// <summary>Janela runtime do campo (offsets rel. ao record). Null quando não resolvido.</summary>
        public MagicWindow? FieldWindow { get; }

        public MagicSlot(
            int slotIndex,
            int slotAbs,
            uint handlerTableIndex,
            ushort parameterOffset,
            ushort flags,
            uint primaryCallbackRel,
            uint secondaryCallbackRel,
            int recordOffset,
            int recordWidth,
            byte[] record,
            string recordSha256,
            string? opcodeName,
            MagicField? field,
            MagicWindow? fieldWindow)
        {
            SlotIndex = slotIndex;
            SlotAbs = slotAbs;
            HandlerTableIndex = handlerTableIndex;
            ParameterOffset = parameterOffset;
            Flags = flags;
            PrimaryCallbackRel = primaryCallbackRel;
            SecondaryCallbackRel = secondaryCallbackRel;
            RecordOffset = recordOffset;
            RecordWidth = recordWidth;
            Record = record;
            RecordSha256 = recordSha256;
            OpcodeName = opcodeName;
            Field = field;
            FieldWindow = fieldWindow;
        }
        /// <summary>
        /// Cria o slot e tenta resolver o campo via schema da família (janela start/width).
        /// </summary>
        internal static MagicSlot Create(
            byte[] data,
            int slotAbs,
            int sectionAbs,
            int slotIndex,
            MagicFieldMap? fieldMap,
            IReadOnlyDictionary<int, string>? fpNames)
        {
            uint handler = BitConverter.ToUInt32(data, slotAbs);
            ushort parameterOffset = BitConverter.ToUInt16(data, slotAbs + 4);
            ushort flags = BitConverter.ToUInt16(data, slotAbs + 6);
            uint primaryCb = BitConverter.ToUInt32(data, slotAbs + 8);
            uint secondaryCb = BitConverter.ToUInt32(data, slotAbs + 12);

            // Opcode via fp.h local do efeito (ou catálogo embutido).
            string? opcode = null;
            if (fpNames != null && fpNames.TryGetValue((int)handler, out string? name))
                opcode = name;

            // Record: âncora de bytes. Largura default 32; com schema, cobre a janela.
            int recordOffset = sectionAbs + (int)primaryCb;
            int recordWidth = 32;
            MagicWindow? window = null;
            if (opcode != null && fieldMap != null &&
                fieldMap.TryGet(opcode, out MagicFamilySchema schema) &&
                schema.PayloadConsumer && schema.Window is { } win)
            {
                window = win;
                recordWidth = Math.Max(32, win.Start + win.Width);
            }

            int available = data.Length - recordOffset;
            int width = recordWidth;
            if (available <= 0)
                width = 0;
            else if (available < width)
                width = available;
            byte[] record = width > 0 ? data[recordOffset..(recordOffset + width)] : [];

            string recordSha = ComputeSha256Hex(record);

            // Campo interpretado: resolve o primeiro field do schema (todos os fields do
            // schema compartilham a mesma âncora recordSha256; o editor navega a lista
            // de fields via schema.Fields + valores do MagicField).
            MagicField? field = null;
            if (window != null && opcode != null &&
                fieldMap != null && fieldMap.TryGet(opcode, out MagicFamilySchema schema2))
            {
                foreach (MagicFieldSpec spec in schema2.Fields)
                {
                    MagicField resolved = MagicField.CreateResolved(
                        spec, opcode, record, recordOffset, recordSha);
                    field ??= resolved;
                }
            }

            return new MagicSlot(
                slotIndex, slotAbs, handler, parameterOffset, flags,
                primaryCb, secondaryCb, recordOffset, recordWidth, record, recordSha,
                opcode, field, window);
        }

        /// <summary>SHA-256 em hex (minúsculo) dos bytes.</summary>
        internal static string ComputeSha256Hex(byte[] bytes)
        {
            if (bytes.Length == 0)
                return "";
            byte[] hash = SHA256.HashData(bytes);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}

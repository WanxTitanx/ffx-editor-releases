using System;

namespace FFXProjectEditor.FfxLib.MagicDll
{
    /// <summary>
    /// Tipos de campo suportados pelo schema de famílias PPP (field_map.json):
    /// f32, s32, u32, s16, u16, u8 — todos little-endian.
    /// </summary>
    public enum MagicFieldType
    {
        /// <summary>float de 4 bytes (ex.: delta de escala por frame).</summary>
        F32,

        /// <summary>int32 com sinal.</summary>
        S32,

        /// <summary>uint32.</summary>
        U32,

        /// <summary>int16 com sinal.</summary>
        S16,

        /// <summary>uint16.</summary>
        U16,

        /// <summary>byte (uint8).</summary>
        U8,
    }

    /// <summary>
    /// Definição declarativa de um campo dentro do record do slot (schema da família).
    /// Espelha a entrada "fields" do field_map.json: nome, offset relativo ao record,
    /// largura em bytes e tipo. Não carrega valor — a instância com valor lido é o
    /// <see cref="MagicField"/>.
    /// </summary>
    public sealed class MagicFieldSpec
    {
        /// <summary>Nome do campo. Ex.: "delta_x".</summary>
        public string Name { get; }

        /// <summary>Offset do campo relativo ao início do record (bytes).</summary>
        public int Offset { get; }

        /// <summary>Largura do campo em bytes (1/2/4).</summary>
        public int Width { get; }

        /// <summary>Tipo do campo (f32/s32/u32/s16/u16/u8).</summary>
        public MagicFieldType Type { get; }

        /// <summary>Semântica documentada do campo (notas da família).</summary>
        public string Semantics { get; }

        public MagicFieldSpec(string name, int offset, int width, MagicFieldType type, string semantics)
        {
            Name = name;
            Offset = offset;
            Width = width;
            Type = type;
            Semantics = semantics;
        }
    }

    /// <summary>
    /// Campo resolvido de um slot: a definição do schema (<see cref="MagicFieldSpec"/>)
    /// mais o valor atual lido dos bytes do record (âncora hash-gated T3).
    /// Só existe quando o opcode do slot está catalogado e a família é payload_consumer.
    /// </summary>
    public sealed class MagicField
    {
        /// <summary>Opcode da família. Ex.: "pppSclMove".</summary>
        public string OpcodeName { get; }

        /// <summary>Nome do campo. Ex.: "delta_x".</summary>
        public string Name { get; }

        /// <summary>Offset do campo relativo ao início do record (bytes).</summary>
        public int Offset { get; }

        /// <summary>Largura do campo em bytes.</summary>
        public int Width { get; }

        /// <summary>Tipo do campo.</summary>
        public MagicFieldType Type { get; }

        /// <summary>Semântica documentada do campo.</summary>
        public string Semantics { get; }

        /// <summary>Offset absoluto do record no .data (âncora de escrita).</summary>
        public int RecordOffset { get; }

        /// <summary>SHA-256 do record (hex) — âncora hash-gated para escrita (contrato T3).</summary>
        public string RecordSha256 { get; }

        /// <summary>Valor atual quando o tipo é f32.</summary>
        public float? ValueFloat { get; }

        /// <summary>Valor atual quando o tipo tem sinal (s32/s16).</summary>
        public int? ValueInt { get; }

        /// <summary>Valor atual quando o tipo é sem sinal (u32/u16/u8).</summary>
        public uint? ValueUInt { get; }

        /// <summary>Nome curto do tipo ("f32", "s32", ...).</summary>
        public string TypeName => Type switch
        {
            MagicFieldType.F32 => "f32",
            MagicFieldType.S32 => "s32",
            MagicFieldType.U32 => "u32",
            MagicFieldType.S16 => "s16",
            MagicFieldType.U16 => "u16",
            MagicFieldType.U8 => "u8",
            _ => "?",
        };

        public MagicField(
            string opcodeName,
            string name,
            int offset,
            int width,
            MagicFieldType type,
            string semantics,
            int recordOffset,
            string recordSha256,
            float? valueFloat,
            int? valueInt,
            uint? valueUInt)
        {
            OpcodeName = opcodeName;
            Name = name;
            Offset = offset;
            Width = width;
            Type = type;
            Semantics = semantics;
            RecordOffset = recordOffset;
            RecordSha256 = recordSha256;
            ValueFloat = valueFloat;
            ValueInt = valueInt;
            ValueUInt = valueUInt;
        }
        /// <summary>
        /// Cria o campo resolvido a partir do spec, lendo o valor dos bytes do record
        /// (little-endian). Quando a janela do campo estoura o record (dado corrompido),
        /// o valor fica null — o campo ainda existe para diagnóstico.
        /// </summary>
        internal static MagicField CreateResolved(
            MagicFieldSpec spec,
            string opcodeName,
            byte[] record,
            int recordOffset,
            string recordSha256)
        {
            float? f = null;
            int? i = null;
            uint? u = null;

            if (spec.Offset >= 0 && spec.Width > 0 && spec.Offset + spec.Width <= record.Length)
            {
                ReadOnlySpan<byte> slice = record.AsSpan(spec.Offset, spec.Width);
                switch (spec.Type)
                {
                    case MagicFieldType.F32:
                        f = BitConverter.ToSingle(slice);
                        break;
                    case MagicFieldType.S32:
                        i = BitConverter.ToInt32(slice);
                        break;
                    case MagicFieldType.U32:
                        u = BitConverter.ToUInt32(slice);
                        break;
                    case MagicFieldType.S16:
                        i = BitConverter.ToInt16(slice);
                        break;
                    case MagicFieldType.U16:
                        u = BitConverter.ToUInt16(slice);
                        break;
                    case MagicFieldType.U8:
                        u = slice[0];
                        break;
                }
            }

            return new MagicField(
                opcodeName, spec.Name, spec.Offset, spec.Width, spec.Type, spec.Semantics,
                recordOffset, recordSha256, f, i, u);
        }

        /// <summary>Converte o nome de tipo do JSON ("f32", "s32", ...) no enum.</summary>
        internal static bool TryParseType(string? typeName, out MagicFieldType type)
        {
            switch (typeName)
            {
                case "f32": type = MagicFieldType.F32; return true;
                case "s32": type = MagicFieldType.S32; return true;
                case "u32": type = MagicFieldType.U32; return true;
                case "s16": type = MagicFieldType.S16; return true;
                case "u16": type = MagicFieldType.U16; return true;
                case "u8": type = MagicFieldType.U8; return true;
                default: type = MagicFieldType.U8; return false;
            }
        }
    }
}

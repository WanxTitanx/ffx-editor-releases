using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.MagicDll
{
    /// <summary>
    /// Janela runtime de uma família PPP: intervalo editável dentro do record
    /// (offsets relativos ao record). Ex.: pppSclMove → start 16, width 16
    /// (record+0x10..+0x1F, 4 floats).
    /// </summary>
    public readonly record struct MagicWindow(int Start, int Width);

    /// <summary>
    /// Schema de uma família PPP (espelho da entrada do field_map.json / families\*.json).
    /// Define, por opcode, se o handler consome payload (payload_consumer), a janela
    /// runtime editável e os campos tipados da janela. Fonte: work/magic_editor/field_map.json.
    /// </summary>
    public sealed class MagicFamilySchema
    {
        /// <summary>Opcode da família. Ex.: "pppSclMove".</summary>
        public string Opcode { get; }

        /// <summary>Endereço do handler canônico no EXE (string "0x75C090") — referência, não usado no parse.</summary>
        public string? HandlerAddr { get; }

        /// <summary>
        /// true quando o handler lê o payload do record — é a condição para resolver
        /// campos do slot via schema (PARSER_SPEC.md §6.1). false ⇒ slot RAW.
        /// </summary>
        public bool PayloadConsumer { get; }

        /// <summary>true quando a janela é editável (writer futuro).</summary>
        public bool Editable { get; }

        /// <summary>Janela runtime (offsets relativos ao record). Null quando a família não tem janela.</summary>
        public MagicWindow? Window { get; }

        /// <summary>Match word do program (normalizado). Ex.: "program[0] == ctx[+12]".</summary>
        public string? MatchWord { get; }

        /// <summary>Guard de pausa observado no handler (referência).</summary>
        public string? Guard { get; }

        /// <summary>Campos declarados da janela (definições, sem valor).</summary>
        public IReadOnlyList<MagicFieldSpec> Fields { get; }

        /// <summary>Status no triage C2 (FECHADO / FALTA_SCHEMA_*).</summary>
        public string? Status { get; }

        /// <summary>Descrição de uso (seção do PPP_FAMILIES_USAGE).</summary>
        public string? Usage { get; }

        /// <summary>
        /// Categoria semântica do handler (Transform / Delta/Accum / VFX-Build / Random /
        /// Render / NodeChain / KR-resource / Field/Node/Anim / UI/Debug / Emit / Light/Glare /
        /// Behavior / Draw / outro). Origens: decompile real (IDA probe) + heurística por nome.
        /// Enriquecida em 2026-08-12 (Eixo A2) em todas as famílias do field_map.
        /// </summary>
        public string? SemanticsCategory { get; }

        /// <summary>Função real despachada pelo handler no EXE (nome renomeado no .i64).</summary>
        public string? RealFunc { get; }

        /// <summary>Largura bruta observada no corpus Yonishi (PS2).</summary>
        public int? RawWidthYonishi { get; }

        /// <summary>Largura reconciliada da janela runtime (16/8/9/57...).</summary>
        public int? WidthReconciled { get; }

        /// <summary>
        /// Largura do record usada pelo parser como âncora de bytes (PARSER_SPEC.md §6.1):
        /// 32B default para as famílias U1/DIRECT; cobre a janela inteira quando ela
        /// ultrapassa 32 (ex.: pppKeTh → 8+49=57).
        /// </summary>
        public int CallbackRecordWidth => Window is { } w ? System.Math.Max(32, w.Start + w.Width) : 32;

        public MagicFamilySchema(
            string opcode,
            string? handlerAddr,
            bool payloadConsumer,
            bool editable,
            MagicWindow? window,
            string? matchWord,
            string? guard,
            IReadOnlyList<MagicFieldSpec> fields,
            string? status,
            string? usage,
            int? rawWidthYonishi,
            int? widthReconciled,
            string? semanticsCategory,
            string? realFunc)
        {
            Opcode = opcode;
            HandlerAddr = handlerAddr;
            PayloadConsumer = payloadConsumer;
            Editable = editable;
            Window = window;
            MatchWord = matchWord;
            Guard = guard;
            Fields = fields;
            Status = status;
            Usage = usage;
            RawWidthYonishi = rawWidthYonishi;
            WidthReconciled = widthReconciled;
            SemanticsCategory = semanticsCategory;
            RealFunc = realFunc;
        }
    }
}

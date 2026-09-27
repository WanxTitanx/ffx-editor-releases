using FFXProjectEditor.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;

namespace FFXProjectEditor.Modules.MagicDllEditor;

// ====================================================================================
// Modelos de árvore do Magic DLL Editor (Jarvis-PPP-C2C3 · 2026-08-01).
// Hierarquia: Root → Descriptors → Programs → Slots → Fields.
// Todos os nós herdam MagicNode (Label/MetaText/Children + slot de campo editável),
// o que permite um único TreeDataTemplate no TreeView e um painel de edição uniforme.
// ====================================================================================

/// <summary>Nível de severidade de uma entrada do painel Log &amp; Status.</summary>
public enum MagicLogLevel
{
    Info,
    Warn,
    Error,
}

/// <summary>Entrada imutável do log (cor resolvida via MagicLogLevelToBrushConverter no XAML).</summary>
public sealed record MagicLogEntry(string Text, MagicLogLevel Level);

/// <summary>Tipos de campo suportados pelo painel de edição (tipo-aware).</summary>
public enum MagicFieldType
{
    F32,   // float — NumericUpDown
    S32,   // int   — NumericUpDown
    U32,   // uint  — NumericUpDown
    S16,   // short — NumericUpDown
    U16,   // hex TextBox (0x0000..0xFFFF)
    U8,    // hex TextBox (0x00..0xFF)
    Flag,  // bit dentro de u8/u16 — CheckBox
    Raw,   // família desconhecida — readonly hex
}

/// <summary>
/// Nó base da árvore. Nós de navegação (Root/Grupo/Descriptor/Program/Slot) usam apenas
/// Label/MetaText/Children; nós de campo (MagicFieldNode) preenchem o slot editável
/// (TypeLabel/OffsetLabel/Value/NumericValue) consumido pelo painel direito.
/// </summary>
public abstract partial class MagicNode : ObservableObject
{
    /// <summary>Texto principal exibido na árvore (ex.: "Root", "P0 · magic01", "s3 · FFX_PppHandler_75D0D0").</summary>
    public abstract string Label { get; }

    /// <summary>Texto secundário mono exibido ao lado do label na árvore.</summary>
    public virtual string MetaText => "";

    /// <summary>Filhos do nó (expansão do TreeView).</summary>
    public virtual IReadOnlyList<MagicNode> Children { get; } = Array.Empty<MagicNode>();

    /// <summary>Rótulo de tipo do campo selecionado (f32/s32/u16/u8/flag/raw) — vazio p/ nós de navegação.</summary>
    public virtual string TypeLabel => "";

    /// <summary>Offset do campo dentro do record (ex.: "0x10") — vazio p/ nós de navegação.</summary>
    public virtual string OffsetLabel => "";

    /// <summary>Semântica do campo (schema da família) — vazio p/ nós de navegação.</summary>
    public virtual string SemanticText => "";

    /// <summary>Descrição/detalhe do nó exibida no painel de edição.</summary>
    public virtual string DetailText => "";

    /// <summary>True quando o valor é numérico (f32/s32) → NumericUpDown no painel.</summary>
    public virtual bool IsNumeric => false;

    /// <summary>True quando o valor pode ser editado (false p/ raw e nós de navegação).</summary>
    public virtual bool IsValueEditable => false;

    /// <summary>True quando o campo deve usar o TextBox (tudo que não é numérico).</summary>
    public bool UseValueBox => !IsNumeric;

    /// <summary>Inverso de IsValueEditable (readonly do TextBox — raw e nós de navegação).</summary>
    public bool IsReadOnly => !IsValueEditable;

    /// <summary>True quando SemanticText tem conteúdo (controla IsVisible do hint).</summary>
    public bool HasSemantic => !string.IsNullOrEmpty(SemanticText);

    /// <summary>True quando o nó tem filhos (é uma "pasta" — renderiza visão de pasta no painel).</summary>
    public bool IsContainer => Children.Count > 0;

    /// <summary>Inverso de IsContainer — controla a visão de campo (folha) no painel.</summary>
    public bool IsNotContainer => !IsContainer;

    /// <summary>Dica textual sobre o que está DENTRO da pasta (prévia dos filhos).</summary>
    public virtual string ChildrenHint => Children.Count == 0
        ? "Empty — nothing inside."
        : $"Contains {Children.Count} item(s) — click to drill in.";

    /// <summary>Controle de expansão do TreeView (fly-to/auto-expand; também usado pelo breadcrumb).</summary>
    [ObservableProperty]
    private bool isExpanded;

    /// <summary>Valor editável como texto (u16/u8/raw/flag exibem hex; f32/s32 exibem decimal).</summary>
    [ObservableProperty]
    private string value = "";

    /// <summary>Valor editável numérico (f32/s32) — sincronizado com <see cref="Value"/>.</summary>
    [ObservableProperty]
    private double? numericValue;

    /// <summary>Callback de edição (wire feito pelo ViewModel) — usado para marcar dirty/logar.</summary>
    internal Action<MagicNode>? EditNotified { get; set; }

    partial void OnValueChanged(string value)
    {
        // Sincroniza o campo numérico a partir do texto (parse invariante, sem loop: seta o campo direto).
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            numericValue = parsed;
            OnPropertyChanged(nameof(NumericValue));
        }

        EditNotified?.Invoke(this);
    }

    partial void OnNumericValueChanged(double? value)
    {
        if (value.HasValue)
        {
            this.value = value.Value.ToString("0.#####", CultureInfo.InvariantCulture);
            OnPropertyChanged(nameof(Value));
        }

        EditNotified?.Invoke(this);
    }
}


/// <summary>Nó raiz do documento (header do efeito).</summary>
public sealed class MagicRootNode : MagicNode
{
    public MagicRootNode(string fileName, IReadOnlyList<MagicNode> children)
    {
        FileName = fileName;
        Children = children;
    }

    public string FileName { get; }

    public override string Label => "Root";

    public override string MetaText => FileName;

    public override string DetailText =>
        Strings.U_Md_RootDetail;

    public override IReadOnlyList<MagicNode> Children { get; }
}

/// <summary>Nó de agrupamento (ex.: "Descriptors (3)", "Programs (1)", "PE sections (3)").</summary>
public sealed class MagicGroupNode : MagicNode
{
    public MagicGroupNode(string label, IReadOnlyList<MagicNode> children)
    {
        Label = label;
        Children = children;
    }

    public override string Label { get; }

    public override string MetaText => $"{Children.Count} item(s)";

    public override string DetailText =>
        Children.Count > 0
            ? $"Group with {Children.Count} item(s) — expand in the tree to navigate."
            : "Group is empty — nothing to expand.";

    /// <summary>Prévia dos filhos (ex.: "P0, P1, P2…") — para ver o que tem dentro sem expandir.</summary>
    public override string ChildrenHint =>
        Children.Count == 0
            ? "Empty — nothing inside."
            : $"Contains {Children.Count} item(s): {string.Join(", ", Children.Take(4).Select(c => c.Label))}{(Children.Count > 4 ? "…" : "")} — click to drill in.";

    public override IReadOnlyList<MagicNode> Children { get; }
}

/// <summary>Descriptor de recurso (32B/entry: ptr0_rel/ptr4_rel/ptr8_rel/w12/w16/w20/w24/w28).</summary>
public sealed class MagicDescriptorNode : MagicNode
{
    public MagicDescriptorNode(int index, string keySummary, IReadOnlyList<MagicNode> fields)
    {
        Index = index;
        KeySummary = keySummary;
        Children = fields;
    }

    public int Index { get; }

    /// <summary>Key → modelo/textura via lppEnv 0xC3A4C4 (cadeia draw documentada).</summary>
    public string KeySummary { get; }

    public override string Label => $"#descriptor {Index}";

    public override string MetaText => KeySummary;

    /// <summary>Prévia dos campos do descriptor (ex.: "8 fields — id a, id b…").</summary>
    public override string ChildrenHint =>
        Children.Count == 0
            ? "Empty — no fields inside."
            : $"Contains {Children.Count} field(s): {string.Join(", ", Children.Take(4).Select(c => c.Label))}{(Children.Count > 4 ? "…" : "")} — click to drill in.";

    public override string DetailText =>
        $"Descriptor #{Index} — {KeySummary} (32B: ptr0_rel/ptr4_rel/ptr8_rel/w12/w16/w20/w24/w28). " +
        "Resource reference (model/texture) resolved via the lppEnv 0xC3A4C4 key.";

    public override IReadOnlyList<MagicNode> Children { get; }
}

/// <summary>Programa do efeito (key própria + curvas + lista de slots).</summary>
public sealed class MagicProgramNode : MagicNode
{
    public MagicProgramNode(int index, string key, IReadOnlyList<MagicNode> slots,
        int curve1Rel = 0, int curve2Rel = 0, ushort seqId = 0, string? slotSummary = null)
    {
        Index = index;
        Key = key;
        Children = slots;
        Curve1Rel = curve1Rel;
        Curve2Rel = curve2Rel;
        SeqId = seqId;
        SlotSummary = slotSummary;
    }

    public int Index { get; }

    public string Key { get; }

    /// <summary>Opcodes/acoes dos slots, ex.: "pppSclMove, pppScale, pppPoint".</summary>
    public string? SlotSummary { get; }

    /// <summary>+16 — ponteiro (rel. section) da curva de animação 1 (samples u8/frame). 0 = sem curva.</summary>
    public int Curve1Rel { get; }

    /// <summary>+20 — ponteiro (rel. section) da curva de animação 2. 0 = sem curva.</summary>
    public int Curve2Rel { get; }

    /// <summary>+36 (u16 baixo) — id sequencial do program na cadeia.</summary>
    public ushort SeqId { get; }

    public override string Label => $"P{Index}";

    public override string MetaText =>
        $"{Children.Count} slots" +
        (string.IsNullOrEmpty(SlotSummary) ? "" : $" · {SlotSummary}");

    /// <summary>Prévia dos slots do program (ex.: "5 slots — pppPoint, pppScale…").</summary>
    public override string ChildrenHint =>
        Children.Count == 0
            ? "Empty — no slots inside."
            : $"Contains {Children.Count} slot(s): {string.Join(", ", Children.Take(4).Select(c => c.Label))}{(Children.Count > 4 ? "…" : "")} — click to drill in.";

    public override string DetailText =>
        $"Program P{Index} — key {Key} · {Children.Count} slots. " +
        (string.IsNullOrEmpty(SlotSummary) ? "" : $"Actions: {SlotSummary}. ") +
        (Curve1Rel > 0 || Curve2Rel > 0
            ? string.Format(Strings.U_Md_ProgramCurves, Curve1Rel, Curve2Rel)
            : "") +
        $"seq_id {SeqId}. handler_table_index is LOCAL to the corresponding fp.h (not a universal index).";

    public override IReadOnlyList<MagicNode> Children { get; }
}

/// <summary>Slot de programa (handler_index + record decomposto em campos).</summary>
public sealed class MagicSlotNode : MagicNode
{
    public MagicSlotNode(int index, int handlerIndex, string handlerName, bool isUnknownFamily, IReadOnlyList<MagicNode> fields, MagicSlot? slot = null, string? semanticsCategory = null, string? realFunc = null)
    {
        Index = index;
        HandlerIndex = handlerIndex;
        HandlerName = handlerName;
        IsUnknownFamily = isUnknownFamily;
        Children = fields;
        Slot = slot;
        SemanticsCategory = semanticsCategory;
        RealFunc = realFunc;
    }

    public int Index { get; }

    /// <summary>Opcode/handler index do slot.</summary>
    public int HandlerIndex { get; }

    /// <summary>Nome canônico FFX_PppHandler_&lt;Opcode&gt;.</summary>
    public string HandlerName { get; }

    /// <summary>True quando o opcode não está catalogado (record exibido como raw hex).</summary>
    public bool IsUnknownFamily { get; }

    /// <summary>Slot do parser (âncora do record — alvo do AddField/grow).</summary>
    public MagicSlot? Slot { get; }

    /// <summary>Categoria semântica da família (Eixo A2). Ex.: "Transform", "Delta/Accum", "Random".</summary>
    public string? SemanticsCategory { get; }

    /// <summary>Função real despachada no EXE (nome renomeado no .i64). Ex.: "MagicHost_ApplyEulerZYXTransform".</summary>
    public string? RealFunc { get; }

    public override string Label => HandlerName;

    public override string MetaText =>
        string.IsNullOrEmpty(SemanticsCategory)
            ? $"s{Index}"
            : $"s{Index} · {SemanticsCategory}";

    /// <summary>Prévia dos campos do slot (ex.: "5 fields — scale_x, scale_y…").</summary>
    public override string ChildrenHint =>
        Children.Count == 0
            ? "Empty — no fields inside (raw/unknown family)."
            : $"Contains {Children.Count} field(s): {string.Join(", ", Children.Take(4).Select(c => c.Label))}{(Children.Count > 4 ? "…" : "")} — click to drill in.";

    /// <summary>true quando a categoria é estrutural (draw/render/node-chain — risco no grow/preview).</summary>
    private static readonly System.Collections.Generic.HashSet<string> StructuralCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "VFX-Build", "Render", "NodeChain", "Draw", "KR-resource",
    };

    /// <summary>true quando o handler é estrutural — aviso de risco (grow/preview).</summary>
    public bool IsStructural =>
        SemanticsCategory != null && StructuralCategories.Contains(SemanticsCategory);

    public override string DetailText =>
        MagicPppDescriptionCatalog.Get(HandlerName) +
        "\n\n" +
        $"Slot s{Index} — {HandlerName} (handler_index {HandlerIndex}). " +
        (IsUnknownFamily
            ? "Opcode not catalogued: record shown as raw hex; field editing disabled."
            : "Record decomposed field-by-field by the family schema (proven window).") +
        (string.IsNullOrEmpty(SemanticsCategory) ? "" : $"\n\nSemantic category: {SemanticsCategory}") +
        (string.IsNullOrEmpty(RealFunc) ? "" : $"\nReal function (IDA): {RealFunc}") +
        (IsStructural
            ? "\n\n⚠️ Structural handler (draw/render/resource): value edits may not show in the 3D preview, and record grow may break the preview (noclip fixed strides — see NOCLIP_PREVIEW_LAYOUT_MAPPING)."
            : "");

    /// <summary>true quando este slot não tem campos editáveis (nenhum campo decomposto).</summary>
    public bool IsReadOnlyPpp => Children.Count == 0;

    /// <summary>Motivo do slot ser read-only (exibido quando IsReadOnlyPpp).</summary>
    public string PppReadOnlyReason =>
        IsUnknownFamily
            ? "this family is not catalogued yet — the editor can't decompose its record. The fields are shown as raw hex."
            : "this family has no editable fields yet — the handler doesn't consume a decomposable payload (or the schema is pending).";

    public override IReadOnlyList<MagicNode> Children { get; }
}


/// <summary>Campo editável (tipo-aware) — folha da árvore e alvo do painel de edição.</summary>
public sealed class MagicFieldNode : MagicNode
{
    public MagicFieldNode(
        string name,
        MagicFieldType type,
        int offset,
        int width,
        string semantic,
        string description,
        double minValue = double.MinValue,
        double maxValue = double.MaxValue,
        int flagBit = -1,
        string initialValue = "",
        bool schemaAllowsWrite = false)
    {
        Name = name;
        Type = type;
        Offset = offset;
        Width = width;
        Semantic = semantic;
        Description = description;
        MinValue = minValue;
        MaxValue = maxValue;
        FlagBit = flagBit;
        SchemaAllowsWrite = schemaAllowsWrite;
        Value = initialValue;
        OriginalValue = initialValue; // âncora do dirty flag (bug corrigido 2026-08-01: sem isso, todo campo nascia dirty)
        NumericValue = initialValue.Length > 0
            ? double.TryParse(initialValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null
            : null;
    }

    public string Name { get; }

    public MagicFieldType Type { get; }

    public int Offset { get; }

    public int Width { get; }

    public string Semantic { get; }

    public string Description { get; }

    /// <summary>Campo do parser (âncora RecordOffset/RecordSha256 p/ write-back) — wire feito pelo ViewModel.</summary>
    public MagicField? SourceField { get; set; }

    /// <summary>Faixa conhecida do schema (default: ilimitada).</summary>
    public double MinValue { get; }

    public double MaxValue { get; }

    /// <summary>Bit de flag dentro de u8/u16 (-1 = não é flag).</summary>
    public int FlagBit { get; }

    /// <summary>
    /// True only for an editable payload schema whose proven runtime window contains this field.
    /// The writer repeats the same gate before touching the working bytes.
    /// </summary>
    public bool SchemaAllowsWrite { get; }

    public override string Label => Name;

    public override string MetaText => $"{OffsetLabel} · {Width}B · {TypeLabel}";

    /// <summary>Valor original do documento (âncora do dirty flag).</summary>
    public string OriginalValue { get; private set; }

    /// <summary>True quando o valor editado difere do documento.</summary>
    public bool IsDirty => !string.Equals(Value, OriginalValue, StringComparison.Ordinal);

    /// <summary>Descarta a edição: volta ao valor do documento.</summary>
    public void ResetToOriginal() => Value = OriginalValue;

    /// <summary>Marca o valor atual como persistido (pós-salvar).</summary>
    public void MarkSaved() => OriginalValue = Value;

    public override string TypeLabel => Type switch
    {
        MagicFieldType.F32 => "f32",
        MagicFieldType.S32 => "s32",
        MagicFieldType.U32 => "u32",
        MagicFieldType.S16 => "s16",
        MagicFieldType.U16 => "u16",
        MagicFieldType.U8 => "u8",
        MagicFieldType.Flag => "flag",
        _ => "raw",
    };

    public override string OffsetLabel => $"0x{Offset:X2}";

    /// <summary>Rótulo humano do tipo (ex.: "decimal", "2 bytes hex") — em vez do "u16" cru.</summary>
    public string TypeHumanLabel => Type switch
    {
        MagicFieldType.F32 => "decimal (float)",
        MagicFieldType.S32 => Strings.U_Md_TypeS32,
        MagicFieldType.U32 => Strings.U_Md_TypeU32,
        MagicFieldType.S16 => Strings.U_Md_TypeS16,
        MagicFieldType.U16 => Strings.U_Md_TypeU16,
        MagicFieldType.U8 => Strings.U_Md_TypeU8,
        MagicFieldType.Flag => Strings.U_Md_TypeFlag,
        _ => "dados brutos",
    };

    /// <summary>Motivo de o campo não ser editável (exibido quando IsReadOnly).</summary>
    public string ReadOnlyReason =>
        Type == MagicFieldType.Raw
            ? Strings.U_Md_ReadOnlyRaw
            : Offset < 8
                ? Strings.U_Md_ReadOnlyHeader
                : Strings.U_Md_ReadOnlySchema;

    /// <summary>true quando o campo não é editável (exibe o aviso de read-only).</summary>
    public bool IsReadOnlyNoticeVisible => !IsValueEditable;

    public override string SemanticText => Semantic;

    public override string DetailText =>
        Description.Length > 0
            ? Description
            : $"{Name} — {TypeHumanLabel}, editable value.";

    public override bool IsNumeric => Type is
        MagicFieldType.F32 or MagicFieldType.S32 or MagicFieldType.U32 or MagicFieldType.S16;

    /// <summary>
    /// Editável apenas fora do prefixo protegido (+0..+7 = match word/estado — R6 do
    /// write-back). Campos do prefixo ficam read-only na UI (o write-back já bloqueia;
    /// isto é o polish de UX, 2026-08-01).
    /// </summary>
    public override bool IsValueEditable =>
        SchemaAllowsWrite && Type != MagicFieldType.Raw && Offset >= 8;

    /// <summary>Rótulo do bit de flag ("bit 3") — usado por um futuro CheckBox de flag.</summary>
    public string FlagBitLabel => FlagBit >= 0 ? $"bit {FlagBit}" : "";

    /// <summary>True quando é flag (reservado p/ CheckBox no painel).</summary>
    public bool IsFlag => Type == MagicFieldType.Flag;
}

/// <summary>Nó de seção PE (parse real via PEReader — mostra .text/.rdata/.data com tamanhos).</summary>
public sealed class MagicSectionNode : MagicNode
{
    public MagicSectionNode(string sectionName, int virtualSize, int rawSize)
    {
        SectionName = sectionName;
        VirtualSize = virtualSize;
        RawSize = rawSize;
    }

    public string SectionName { get; }

    public int VirtualSize { get; }

    public int RawSize { get; }

    public override string Label => SectionName;

    public override string MetaText => $"virt 0x{VirtualSize:X} · raw 0x{RawSize:X}";

    public override string DetailText =>
        string.Format(Strings.U_Md_SectionDetail, SectionName, VirtualSize, RawSize);
}

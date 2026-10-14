namespace FFXProjectEditor.FfxLib.MagicDll
{
    /// <summary>
    /// Descriptor de 32 bytes da table2 do root (PARSER_SPEC.md §2.5).
    /// Base = root + u32(root+20); count = u16(root+8); entries de 32B contíguos.
    ///
    /// +0 conta pontos; +20 aponta para XYZ s16, stride 6, relativo ao root.
    /// +28 aponta para o header da malha e pode ser zero em recursos só de pontos.
    /// Nomes Ptr0/Ptr4/Ptr8 preservados por compatibilidade; não são ponteiros.
    /// Evidência: PPP_GEOMETRY_EDITOR_20261003.md.
    /// </summary>
    public sealed class MagicDescriptor
    {
        /// <summary>Índice da entry na tabela (0-based).</summary>
        public int Index { get; }

        /// <summary>Offset absoluto da entry no .data.</summary>
        public int EntryAbs { get; }

        /// <summary>+0 — quantidade de pontos XYZ s16.</summary>
        public uint Ptr0 { get; }

        /// <summary>+4 — word preservada, sem edição semântica.</summary>
        public uint Ptr4 { get; }

        /// <summary>+8 — word preservada, sem edição semântica.</summary>
        public uint Ptr8 { get; }

        /// <summary>+12 — word preservada, sem edição semântica.</summary>
        public uint W12 { get; }

        /// <summary>+16 — word preservada, sem edição semântica.</summary>
        public uint W16 { get; }

        /// <summary>+20 — offset rel. ao root dos pontos XYZ s16 (0 = ausente).</summary>
        public uint W20Rel { get; }

        /// <summary>+24 — offset rel. ao root (opcional; 0 = ausente).</summary>
        public uint W24Rel { get; }

        /// <summary>+28 — offset rel. ao root do header de sub-bloco (opcional; 0 = ausente).</summary>
        public uint W28Rel { get; }

        public MagicDescriptor(
            int index, int entryAbs,
            uint ptr0, uint ptr4, uint ptr8,
            uint w12, uint w16, uint w20Rel, uint w24Rel, uint w28Rel)
        {
            Index = index;
            EntryAbs = entryAbs;
            Ptr0 = ptr0;
            Ptr4 = ptr4;
            Ptr8 = ptr8;
            W12 = w12;
            W16 = w16;
            W20Rel = w20Rel;
            W24Rel = w24Rel;
            W28Rel = w28Rel;
        }
    }
}

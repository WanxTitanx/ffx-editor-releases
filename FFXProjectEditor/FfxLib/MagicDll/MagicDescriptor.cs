namespace FFXProjectEditor.FfxLib.MagicDll
{
    /// <summary>
    /// Descriptor de 32 bytes da table2 do root (PARSER_SPEC.md §2.5).
    /// Base = root + u32(root+20); count = u16(root+8); entries de 32B contíguos.
    ///
    /// Semântica (HIPÓTESE, spec §4): ptr0/ptr4/ptr8 são ids (ptr8 sempre múltiplo de 16);
    /// w12/w16 são tags binárias; w20/w24/w28 são offsets RELATIVOS AO ROOT de bloobs de
    /// dados binários (0 = bloco ausente). O bloco apontado por w28 espelha ptr8/ptr0 em
    /// u16@+16/u16@+18 (PROVADO em 31/31 entries dos 4 efeitos).
    /// </summary>
    public sealed class MagicDescriptor
    {
        /// <summary>Índice da entry na tabela (0-based).</summary>
        public int Index { get; }

        /// <summary>Offset absoluto da entry no .data.</summary>
        public int EntryAbs { get; }

        /// <summary>+0 — id (semântica HIPÓTESE).</summary>
        public uint Ptr0 { get; }

        /// <summary>+4 — id (na maioria das entries == Ptr0).</summary>
        public uint Ptr4 { get; }

        /// <summary>+8 — id (sempre múltiplo de 16 nos exemplos).</summary>
        public uint Ptr8 { get; }

        /// <summary>+12 — tag binária (HIPÓTESE: identidade do recurso).</summary>
        public uint W12 { get; }

        /// <summary>+16 — tag binária (HIPÓTESE: identidade do recurso).</summary>
        public uint W16 { get; }

        /// <summary>+20 — offset rel. ao root de um bloco de dados (0 = ausente).</summary>
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

using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXProjectEditor.FfxLib.MagicDll
{
    /// <summary>
    /// Root do efeito PPP: cabeçalho de 32 bytes + tabelas + cadeias de programs
    /// (PARSER_SPEC.md §2.1). Localizado por varredura de candidatos sobre o .data
    /// (port de find_ppp_resource_roots / _parse_resource_root_candidate do
    /// layer_c_resource.py).
    ///
    /// Layout do cabeçalho (todos little-endian):
    ///   +0  u32  tag (0x31/0x32 observado; HIPÓTESE: versão/tipo do recurso)
    ///   +4  u32  contagem espelhada (u16 hi == u16 lo)
    ///   +6  u16  primary_count (nº de primary sections)
    ///   +8  u16  descriptor_count (entries da table2)
    ///   +10 u16  count3 (entries da table3)
    ///   +12 u16  count4 (entries da table4)
    ///   +16 u32  table1 — offset (rel. root) da primary table
    ///   +20 u32  table2 — offset (rel. root) da descriptor table (entries 32B)
    ///   +24 u32  table3 — offset (rel. root) da tabela auxiliar u32
    ///   +28 u32  table4 — offset (rel. root) da tabela auxiliar de 8B
    /// </summary>
    public sealed class MagicDllRoot
    {
        private const int ProgramHeadSize = 40;
        private const int SlotSize = 16;

        /// <summary>Offset absoluto do root no .data (ex.: 0021 → 0x18B30 = 101168).</summary>
        public int RootAbs { get; }

        /// <summary>+0 — tag/versão do recurso (0x31/0x32 observado).</summary>
        public uint Tag { get; }

        /// <summary>+4 — contagem espelhada (u16 hi == u16 lo).</summary>
        public uint MirrorCount { get; }

        /// <summary>+6 — primary_count (nº de primary sections).</summary>
        public ushort PrimaryCount { get; }

        /// <summary>+8 — descriptor_count (entries da table2).</summary>
        public ushort DescriptorCount { get; }

        /// <summary>+10 — count3 (entries da table3).</summary>
        public ushort Aux3Count { get; }

        /// <summary>+12 — count4 (entries da table4).</summary>
        public ushort Aux4Count { get; }

        /// <summary>+16 — offset (rel. root) da primary table.</summary>
        public int Table1Rel { get; }

        /// <summary>+20 — offset (rel. root) da descriptor table.</summary>
        public int Table2Rel { get; }

        /// <summary>+24 — offset (rel. root) da tabela auxiliar u32.</summary>
        public int Table3Rel { get; }

        /// <summary>+28 — offset (rel. root) da tabela auxiliar de 8B.</summary>
        public int Table4Rel { get; }

        /// <summary>Primary sections (table1 resolvida; offsets relativos ao root).</summary>
        public IReadOnlyList<int> PrimarySections { get; }

        /// <summary>Offsets relativos ao root das tabelas 2/3/4 (espelho do JSON table_offsets).</summary>
        public IReadOnlyList<int> RelocationTableOffsets { get; }

        /// <summary>Entries da descriptor table (32B cada).</summary>
        public IReadOnlyList<MagicDescriptor> Descriptors { get; }

        /// <summary>Programs de todas as seções primárias (inclui os sentinelas slot_count=0).</summary>
        public IReadOnlyList<MagicProgram> Programs { get; }

        /// <summary>handler_table_index distintos usados pelos slots (sorted; índices LOCAIS do fp.h).</summary>
        public IReadOnlyList<int> HandlerIndicesUsed { get; }

        /// <summary>Nº de programs (inclui 1 sentinela por seção).</summary>
        public int ProgramCount => Programs.Count;

        /// <summary>Total de slots em todos os programs.</summary>
        public int TotalSlots => Programs.Sum(p => p.Slots.Count);

        public MagicDllRoot(
            int rootAbs, uint tag, uint mirrorCount,
            ushort primaryCount, ushort descriptorCount, ushort aux3Count, ushort aux4Count,
            int table1Rel, int table2Rel, int table3Rel, int table4Rel,
            IReadOnlyList<int> primarySections,
            IReadOnlyList<int> relocationTableOffsets,
            IReadOnlyList<MagicDescriptor> descriptors,
            IReadOnlyList<MagicProgram> programs,
            IReadOnlyList<int> handlerIndicesUsed)
        {
            RootAbs = rootAbs;
            Tag = tag;
            MirrorCount = mirrorCount;
            PrimaryCount = primaryCount;
            DescriptorCount = descriptorCount;
            Aux3Count = aux3Count;
            Aux4Count = aux4Count;
            Table1Rel = table1Rel;
            Table2Rel = table2Rel;
            Table3Rel = table3Rel;
            Table4Rel = table4Rel;
            PrimarySections = primarySections;
            RelocationTableOffsets = relocationTableOffsets;
            Descriptors = descriptors;
            Programs = programs;
            HandlerIndicesUsed = handlerIndicesUsed;
        }
        /// <summary>
        /// Varre o .data em offsets 4-alinhados e devolve todos os candidatos a root que
        /// passam nas regras de sanidade (port de find_ppp_resource_roots). Tipicamente 1
        /// por DLL (0021/0098/0086/0087: root_count=1 no JSON de evidência).
        /// </summary>
        public static IReadOnlyList<MagicDllRoot> FindRoots(
            byte[] data,
            MagicFieldMap? fieldMap = null,
            IReadOnlyDictionary<int, string>? fpNames = null)
        {
            var roots = new List<MagicDllRoot>();
            for (int offset = 0; offset <= data.Length - 32; offset += 4)
            {
                if (TryParseRoot(data, offset, fieldMap, fpNames, out MagicDllRoot? root))
                    roots.Add(root!);
            }
            return roots;
        }

        /// <summary>
        /// Valida e constrói o root num offset candidato (port de _parse_resource_root_candidate
        /// + describe_programs + describe_descriptor_table do analyze_c3_roots.py).
        /// A validação é feita na MESMA passada da construção (sem re-caminhar).
        /// </summary>
        internal static bool TryParseRoot(
            byte[] data,
            int offset,
            MagicFieldMap? fieldMap,
            IReadOnlyDictionary<int, string>? fpNames,
            out MagicDllRoot? root)
        {
            root = null;
            if (offset < 0 || offset + 32 > data.Length)
                return false;

            // --- Cabeçalho: counts ---
            ushort primaryCount = ReadU16(data, offset + 6);
            ushort count2 = ReadU16(data, offset + 8);
            ushort count3 = ReadU16(data, offset + 10);
            ushort count4 = ReadU16(data, offset + 12);
            if (primaryCount == 0 || primaryCount > 64)
                return false;
            if (count2 > 256 || count3 > 256 || count4 > 256)
                return false;

            // --- Cabeçalho: tabelas (offsets rel. root; >= 32, múltiplos de 4, crescentes) ---
            int table1 = ReadI32(data, offset + 16);
            int table2 = ReadI32(data, offset + 20);
            int table3 = ReadI32(data, offset + 24);
            int table4 = ReadI32(data, offset + 28);
            int[] tableOffsets = { table1, table2, table3, table4 };
            if (tableOffsets.Any(t => t < 32 || t % 4 != 0))
                return false;
            for (int i = 1; i < tableOffsets.Length; i++)
            {
                if (tableOffsets[i] <= tableOffsets[i - 1])
                    return false;
            }
            if (tableOffsets.Any(t => offset + t >= data.Length))
                return false;

            // --- Primary table (table1) ---
            int primaryTable = offset + table1;
            if (primaryTable + 4 * primaryCount > data.Length)
                return false;
            var sections = new List<int>(primaryCount);
            for (int i = 0; i < primaryCount; i++)
            {
                int rel = ReadI32(data, primaryTable + 4 * i);
                if (rel < 32 || rel % 4 != 0)
                    return false;
                sections.Add(rel);
            }

            // --- Primary sections: valida + constrói a cadeia de programs ---
            var programs = new List<MagicProgram>();
            foreach (int sectionRel in sections)
            {
                if (!TryWalkSection(data, offset, sectionRel, fieldMap, fpNames, programs))
                    return false;
            }

            // --- Tabelas auxiliares 2/3/4 (validação de sanidade) ---
            if (!IsValidAuxiliaryTables(data, offset, count2, count3, count4, table2, table3, table4))
                return false;

            // --- Descriptor table (table2) ---
            var descriptors = new List<MagicDescriptor>(count2);
            int descriptorBase = offset + table2;
            for (int i = 0; i < count2; i++)
            {
                int entryAbs = descriptorBase + 32 * i;
                descriptors.Add(new MagicDescriptor(
                    i, entryAbs,
                    ReadU32(data, entryAbs), ReadU32(data, entryAbs + 4), ReadU32(data, entryAbs + 8),
                    ReadU32(data, entryAbs + 12), ReadU32(data, entryAbs + 16),
                    ReadU32(data, entryAbs + 20), ReadU32(data, entryAbs + 24), ReadU32(data, entryAbs + 28)));
            }

            // --- handler_indices_used (sorted distinct, como o JSON) ---
            var handlerIndices = programs
                .SelectMany(p => p.Slots)
                .Select(s => (int)s.HandlerTableIndex)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            root = new MagicDllRoot(
                offset,
                ReadU32(data, offset), ReadU32(data, offset + 4),
                primaryCount, count2, count3, count4,
                table1, table2, table3, table4,
                sections,
                new[] { table2, table3, table4 },
                descriptors, programs, handlerIndices);
            return true;
        }
        /// <summary>
        /// Valida uma primary section e caminha a cadeia de programs construindo-os
        /// (port de _valid_primary_section + walker do describe_programs). Requer ao
        /// menos 1 slot na seção (foundSlot), como no Python.
        /// </summary>
        private static bool TryWalkSection(
            byte[] data,
            int root,
            int sectionRel,
            MagicFieldMap? fieldMap,
            IReadOnlyDictionary<int, string>? fpNames,
            List<MagicProgram> programs)
        {
            int section = root + sectionRel;
            if (section + 56 > data.Length)
                return false;
            int sectionSize = ReadI32(data, section);
            if (sectionSize < 56)
                return false;
            // Extensão tolerante (NOROOT_INVESTIGATION 2026-08-01): algumas DLLs
            // declaram section_size virtual-inflado; truncar ao disponível no .data
            // recupera 229 DLLs (576/581 editáveis) sem falsos positivos.
            int available = data.Length - section;
            if (sectionSize > available)
                sectionSize = available;

            // Duas tabelas counted-u32 no header da seção (+8 e +12).
            if (!IsValidCountedU32Table(data, section, sectionSize, ReadI32(data, section + 8)))
                return false;
            if (!IsValidCountedU32Table(data, section, sectionSize, ReadI32(data, section + 12)))
                return false;

            // Cadeia de programs: section+16 → next (rel. section); 0 ou regressão = fim.
            int program = section + 16;
            var visited = new HashSet<int>();
            bool foundSlot = false;
            while (true)
            {
                int programRel = program - section;
                if (!visited.Add(programRel) || program + ProgramHeadSize > section + sectionSize)
                    return false;

                int slotCount = ReadI16(data, program + 38);
                if (slotCount < 0 || slotCount > 256)
                    return false;
                if (program + ProgramHeadSize + SlotSize * slotCount > section + sectionSize)
                    return false;

                for (int si = 0; si < slotCount; si++)
                {
                    int slotAbs = program + ProgramHeadSize + SlotSize * si;
                    uint handler = ReadU32(data, slotAbs);
                    uint primaryCb = ReadU32(data, slotAbs + 8);
                    uint secondaryCb = ReadU32(data, slotAbs + 12);
                    if (handler > 255)
                        return false;
                    if (primaryCb >= sectionSize || secondaryCb >= sectionSize)
                        return false;
                    foundSlot = true;
                }

                programs.Add(MagicProgram.Create(data, program, section, fieldMap, fpNames));

                int next = ReadI32(data, program);
                if (next == 0)
                    return foundSlot;
                if (next <= programRel || next % 4 != 0)
                    return false;
                program = section + next;
            }
        }

        /// <summary>Valida uma tabela counted-u32 (count u32 + entries u32) dentro da seção.</summary>
        private static bool IsValidCountedU32Table(byte[] data, int section, int sectionSize, int relative)
        {
            if (relative < 16 || relative + 4 > sectionSize)
                return false;
            int table = section + relative;
            int count = ReadI32(data, table);
            if (count > 4096 || relative + 4 + 4 * count > sectionSize)
                return false;
            for (int i = 0; i < count; i++)
            {
                if (ReadU32(data, table + 4 + 4 * i) >= sectionSize)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Valida as tabelas auxiliares 2/3/4 (port de _valid_auxiliary_tables):
        /// todos os ponteiros internos devem ser &lt; resource_size (= len(data) - root).
        /// </summary>
        private static bool IsValidAuxiliaryTables(
            byte[] data, int root,
            int count2, int count3, int count4,
            int table2, int table3, int table4)
        {
            int resourceSize = data.Length - root;

            // table2: entries de 32B; +20/+24/+28 < resourceSize
            int section2 = root + table2;
            if (section2 + 32 * count2 > data.Length)
                return false;
            for (int i = 0; i < count2; i++)
            {
                int entry = section2 + 32 * i;
                if (ReadU32(data, entry + 20) >= resourceSize ||
                    ReadU32(data, entry + 24) >= resourceSize ||
                    ReadU32(data, entry + 28) >= resourceSize)
                    return false;
            }

            // table3: entries u32
            int section3 = root + table3;
            if (section3 + 4 * count3 > data.Length)
                return false;
            for (int i = 0; i < count3; i++)
            {
                if (ReadU32(data, section3 + 4 * i) >= resourceSize)
                    return false;
            }

            // table4: entries de 8B; u32@+4 < resourceSize
            int section4 = root + table4;
            if (section4 + 8 * count4 > data.Length)
                return false;
            for (int i = 0; i < count4; i++)
            {
                if (ReadU32(data, section4 + 8 * i + 4) >= resourceSize)
                    return false;
            }

            return true;
        }

        // --- Leituras little-endian (com bounds-check — fix review 2026-08-01:
        //     offset fora do range -> 0, nunca lança; contrato TryParse nunca-lança) ---

        private static ushort ReadU16(byte[] data, int offset) =>
            offset >= 0 && offset + 2 <= data.Length ? BitConverter.ToUInt16(data, offset) : (ushort)0;

        private static int ReadI16(byte[] data, int offset) =>
            offset >= 0 && offset + 2 <= data.Length ? BitConverter.ToInt16(data, offset) : 0;

        private static uint ReadU32(byte[] data, int offset) =>
            offset >= 0 && offset + 4 <= data.Length ? BitConverter.ToUInt32(data, offset) : 0u;

        private static int ReadI32(byte[] data, int offset) =>
            offset >= 0 && offset + 4 <= data.Length ? BitConverter.ToInt32(data, offset) : 0;
    }
}

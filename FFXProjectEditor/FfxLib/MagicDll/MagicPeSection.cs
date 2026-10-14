namespace FFXProjectEditor.FfxLib.MagicDll
{
    /// <summary>
    /// Seção de um PE32 (espelho da entry de 40B da section table).
    /// Usada pelo <see cref="MagicDllFile"/> para localizar a seção .data e
    /// converter RVA → offset de arquivo.
    /// </summary>
    public sealed class MagicPeSection
    {
        /// <summary>Nome da seção (8 bytes ASCII, sem o NUL de padding). Ex.: ".data".</summary>
        public string Name { get; }

        /// <summary>VirtualSize (+8 da section table) — tamanho carregado em memória.</summary>
        public int VirtualSize { get; }

        /// <summary>VirtualAddress (+12) — RVA base da seção.</summary>
        public int VirtualAddress { get; }

        /// <summary>SizeOfRawData (+16) — tamanho no arquivo (pode diferir do VirtualSize).</summary>
        public int RawSize { get; }

        /// <summary>PointerToRawData (+20) — offset no arquivo.</summary>
        public int RawPtr { get; }

        public MagicPeSection(string name, int virtualSize, int virtualAddress, int rawSize, int rawPtr)
        {
            Name = name;
            VirtualSize = virtualSize;
            VirtualAddress = virtualAddress;
            RawSize = rawSize;
            RawPtr = rawPtr;
        }
    }
}

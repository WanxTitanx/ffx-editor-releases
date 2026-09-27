namespace FFXProjectEditor.FfxLib.Event
{
    /// <summary>ATEL call target namespaces. Function IDs are built as (namespace << 12) | index.
    /// Validated via Ghidra RE (Fahrenheit) against the FFX.exe function pointer tables.</summary>
    public enum AtelCallTargetNamespace
    {
        Common  = 0x0,
        Math    = 0x1,
        SgEvent = 0x4,
        ChEvent = 0x5,
        Camera  = 0x6,
        Battle  = 0x7,
        Map     = 0x8,
        Mount   = 0x9,
        Movie   = 0xB,
        Debug   = 0xC,
        AbiMap  = 0xD,
    }

    /// <summary>Utility helpers for ATEL call target namespaces.</summary>
    public static class AtelCallTargetUtil
    {
        /// <summary>Decode the namespace from an ATEL function ID (high 4 bits).</summary>
        public static AtelCallTargetNamespace NamespaceOf(ushort functionId)
            => (AtelCallTargetNamespace)(functionId >> 12);

        /// <summary>Extract the index within namespace from an ATEL function ID (low 12 bits).</summary>
        public static int IndexOf(ushort functionId)
            => functionId & 0xFFF;

        /// <summary>Build a full ATEL function ID from namespace + index.</summary>
        public static ushort MakeId(AtelCallTargetNamespace ns, int index)
            => (ushort)(((int)ns << 12) | (index & 0xFFF));

        /// <summary>Short string label for a namespace (e.g. "btl", "cam", "std").</summary>
        public static string Label(AtelCallTargetNamespace ns) => ns switch
        {
            AtelCallTargetNamespace.Common  => "std",
            AtelCallTargetNamespace.Math    => "math",
            AtelCallTargetNamespace.SgEvent => "sg",
            AtelCallTargetNamespace.ChEvent => "ch",
            AtelCallTargetNamespace.Camera  => "cam",
            AtelCallTargetNamespace.Battle  => "btl",
            AtelCallTargetNamespace.Map     => "map",
            AtelCallTargetNamespace.Mount   => "mnt",
            AtelCallTargetNamespace.Movie   => "mov",
            AtelCallTargetNamespace.Debug   => "dbg",
            AtelCallTargetNamespace.AbiMap  => "abm",
            _ => "unk",
        };

        /// <summary>Formatted name like "btl::0x700B" (label + hex id).</summary>
        public static string FormatId(ushort functionId)
            => $"{Label(NamespaceOf(functionId))}::{functionId:X4}";
    }
}

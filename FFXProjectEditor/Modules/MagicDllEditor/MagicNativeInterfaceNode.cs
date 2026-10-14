using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MagicDllEditor;

/// <summary>Inspection of the selector-zero interface, independent of PPP registration.</summary>
public sealed class MagicNativeInterfaceNode : MagicNode
{
    private readonly uint _tableRva;
    public MagicNativeInterfaceNode(MagicNativeInterfaceInfo info)
    {
        _tableRva = info.TableRva;
        Children = info.Entries.Select(e => (MagicNode)new EntryNode(e)).ToArray();
    }
    public override string Label => Strings.U_Md_NativeDllInterface;
    public override string MetaText => $"RVA 0x{_tableRva:X}";
    public override string DetailText => Strings.U_Md_NativeDllInterfaceHint;
    public override IReadOnlyList<MagicNode> Children { get; }

    private sealed class EntryNode : MagicNode
    {
        private readonly MagicNativeInterfaceEntry _entry;
        public EntryNode(MagicNativeInterfaceEntry entry)
        {
            _entry = entry;
            Value = entry.Rva is uint rva ? $"RVA 0x{rva:X}" : Strings.U_Md_CallbackAbsent;
        }
        public override string Label => _entry.IsDataPointer ? Strings.U_Md_NativeDllDataPointer :
            string.Format(CultureInfo.CurrentCulture, Strings.U_Md_OverlayCallback, _entry.Index);
        public override string MetaText => Value;
        public override string TypeLabel => _entry.IsDataPointer ? "data" : "callback";
        public override string DetailText => Strings.U_Md_NativeDllInterfaceHint;
        public bool IsReadOnlyNoticeVisible => true;
        public string ReadOnlyReason => Strings.U_Md_NativeDllInterfaceHint;
    }
}

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MagicDllEditor;

public sealed class MagicFieldOverlayNode : MagicNode
{
    public MagicFieldOverlayNode(MagicFieldOverlayInfo info)
    {
        Children = info.CallbackRvas.Select((rva, index) => (MagicNode)new CallbackNode(index, rva)).ToArray();
    }
    public override string Label => Strings.U_Md_FieldOverlay;
    public override string DetailText => Strings.U_Md_FieldOverlayUnregistered;
    public override IReadOnlyList<MagicNode> Children { get; }

    private sealed class CallbackNode : MagicNode
    {
        private readonly int _index;
        public CallbackNode(int index, uint rva)
        {
            _index = index;
            Value = rva == 0 ? Strings.U_Md_CallbackAbsent : $"RVA 0x{rva:X}";
        }
        public override string Label => _index switch
        {
            0 => Strings.U_Md_OverlayInitialize,
            1 => Strings.U_Md_OverlayRelease,
            4 => Strings.U_Md_OverlayUpdate,
            7 => Strings.U_Md_NativeDllDataPointer,
            _ => string.Format(CultureInfo.CurrentCulture, Strings.U_Md_OverlayCallback, _index),
        };
        public override string MetaText => Value;
        public override string TypeLabel => _index == 7 ? "data" : "callback";
        public override string DetailText => _index == 7 ? Strings.U_Md_NativeDllInterfaceHint : Strings.U_Md_CallbackInspection;
        public bool IsReadOnlyNoticeVisible => true;
        public string ReadOnlyReason => DetailText;
    }
}

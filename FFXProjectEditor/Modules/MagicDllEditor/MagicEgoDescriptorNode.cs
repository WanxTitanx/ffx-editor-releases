using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FFXProjectEditor.FfxLib.MagicDll;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Modules.MagicDllEditor;

/// <summary>Static descriptor inspection; code cells and runtime links stay read-only.</summary>
public sealed class MagicEgoDescriptorNode : MagicNode
{
    public MagicEgoDescriptorNode(IReadOnlyList<MagicEgoDescriptorInfo> descriptors)
    {
        Children = descriptors.Select(d => (MagicNode)new DescriptorNode(d)).ToArray();
    }
    public override string Label => Strings.U_Md_EgoDescriptors;
    public override string DetailText => Strings.U_Md_EgoDescriptorHint;
    public override IReadOnlyList<MagicNode> Children { get; }

    private sealed class DescriptorNode : MagicNode
    {
        private readonly MagicEgoDescriptorInfo _descriptor;
        public DescriptorNode(MagicEgoDescriptorInfo descriptor)
        {
            _descriptor = descriptor;
            var children = new List<MagicNode>
            {
                new ReadOnlyNode(Strings.U_Md_EgoHeaderWord, descriptor.HeaderWord16.ToString(CultureInfo.CurrentCulture)),
                new ReadOnlyNode(Strings.U_Md_EgoLink, descriptor.LinkedDescriptorName == null
                    ? $"RVA 0x{descriptor.LinkRva:X}" : $"{descriptor.LinkedDescriptorName} · RVA 0x{descriptor.LinkRva:X}")
            };
            children.AddRange(descriptor.CallbackRvas.Select((rva, index) => (MagicNode)new ReadOnlyNode(
                string.Format(CultureInfo.CurrentCulture, Strings.U_Md_OverlayCallback, index), $"RVA 0x{rva:X}",
                index < descriptor.CallbackKinds.Count ? descriptor.CallbackKinds[index] : MagicEgoCallbackKind.Unknown)));
            Children = children.ToArray();
        }
        public override string Label => _descriptor.Name;
        public override string MetaText => $"RVA 0x{_descriptor.Rva:X}";
        public override string DetailText => Strings.U_Md_EgoDescriptorHint;
        public override IReadOnlyList<MagicNode> Children { get; }
    }

    private sealed class ReadOnlyNode : MagicNode
    {
        private readonly string _label;
        private readonly MagicEgoCallbackKind _kind;
        public ReadOnlyNode(string label, string value, MagicEgoCallbackKind kind = MagicEgoCallbackKind.Unknown)
        {
            _label = label;
            _kind = kind;
            Value = value;
        }
        public override string Label => _label;
        public override string MetaText => OperationLabel(_kind) is { } operation ? $"{Value} · {operation}" : Value;
        public override string DetailText => OperationLabel(_kind) is { } operation
            ? string.Format(CultureInfo.CurrentCulture, Strings.U_Md_EgoOperationHint, operation)
            : Strings.U_Md_EgoDescriptorHint;
        public bool IsReadOnlyNoticeVisible => true;
        public string ReadOnlyReason => DetailText;
    }

    private static string? OperationLabel(MagicEgoCallbackKind kind) => kind switch
    {
        MagicEgoCallbackKind.NoOp => Strings.U_Md_EgoOpNoOp,
        MagicEgoCallbackKind.VisitChildren => Strings.U_Md_EgoOpVisitChildren,
        MagicEgoCallbackKind.UnlinkNode => Strings.U_Md_EgoOpUnlinkNode,
        MagicEgoCallbackKind.VisitTask => Strings.U_Md_EgoOpVisitTask,
        MagicEgoCallbackKind.VisitTaskItem => Strings.U_Md_EgoOpVisitTaskItem,
        MagicEgoCallbackKind.VisitTaskList => Strings.U_Md_EgoOpVisitTaskList,
        MagicEgoCallbackKind.ReleaseParticles => Strings.U_Md_EgoOpReleaseParticles,
        MagicEgoCallbackKind.AddIntegerVector => Strings.U_Md_EgoOpAddIntegerVector,
        MagicEgoCallbackKind.AddFloatVector => Strings.U_Md_EgoOpAddFloatVector,
        MagicEgoCallbackKind.MultiplyFloatVector => Strings.U_Md_EgoOpMultiplyFloatVector,
        MagicEgoCallbackKind.OffsetIntegerVector => Strings.U_Md_EgoOpOffsetIntegerVector,
        MagicEgoCallbackKind.OffsetFloatVector => Strings.U_Md_EgoOpOffsetFloatVector,
        MagicEgoCallbackKind.SumFloatVectors => Strings.U_Md_EgoOpSumFloatVectors,
        MagicEgoCallbackKind.MoveFloatVector => Strings.U_Md_EgoOpMoveFloatVector,
        MagicEgoCallbackKind.IntegrateIntegerVector => Strings.U_Md_EgoOpIntegrateIntegerVector,
        MagicEgoCallbackKind.DampIntegerAngles => Strings.U_Md_EgoOpDampIntegerAngles,
        MagicEgoCallbackKind.TransformLinkedVector => Strings.U_Md_EgoOpTransformLinkedVector,
        MagicEgoCallbackKind.TransformLocalVector => Strings.U_Md_EgoOpTransformLocalVector,
        MagicEgoCallbackKind.ObjectPosition => Strings.U_Md_EgoOpObjectPosition,
        MagicEgoCallbackKind.CircularPosition => Strings.U_Md_EgoOpCircularPosition,
        MagicEgoCallbackKind.ActorAngle => Strings.U_Md_EgoOpActorAngle,
        MagicEgoCallbackKind.ActorPosition => Strings.U_Md_EgoOpActorPosition,
        MagicEgoCallbackKind.ActorScale => Strings.U_Md_EgoOpActorScale,
        MagicEgoCallbackKind.BonePosition => Strings.U_Md_EgoOpBonePosition,
        MagicEgoCallbackKind.ComposeStandardMatrix => Strings.U_Md_EgoOpComposeStandardMatrix,
        _ => null
    };
}

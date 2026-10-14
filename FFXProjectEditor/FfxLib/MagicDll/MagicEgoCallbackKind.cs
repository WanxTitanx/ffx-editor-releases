namespace FFXProjectEditor.FfxLib.MagicDll;

/// <summary>Proven runtime operations for exact native code identities.
/// These annotations never grant a payload or runtime write contract.</summary>
public enum MagicEgoCallbackKind
{
    Unknown,
    NoOp,
    VisitChildren,
    UnlinkNode,
    VisitTask,
    VisitTaskItem,
    VisitTaskList,
    ReleaseParticles,
    AddIntegerVector,
    AddFloatVector,
    MultiplyFloatVector,
    OffsetIntegerVector,
    OffsetFloatVector,
    SumFloatVectors,
    MoveFloatVector,
    IntegrateIntegerVector,
    DampIntegerAngles,
    TransformLinkedVector,
    TransformLocalVector,
    ObjectPosition,
    CircularPosition,
    ActorAngle,
    ActorPosition,
    ActorScale,
    BonePosition,
    ComposeStandardMatrix
}

using CSM.API.Commands;
using ProtoBuf;

namespace CSM.IMTSync.Commands
{
    /// <summary>
    /// Single polymorphic command for all low-frequency IMT mutations.
    /// Fields not relevant to a given <see cref="Type"/> are left at default values.
    ///
    /// Wire-format rule: only ever APPEND new ProtoMember tags. Never reuse a tag for a different
    /// field. Adding a field at a higher tag is safe across versions.
    /// </summary>
    [ProtoContract]
    public class IMTActionCommand : CommandBase
    {
        [ProtoMember(1)] public IMTActionType Type;
        [ProtoMember(2)] public MarkingScope Scope;
        [ProtoMember(3)] public ushort MarkingId;

        // Endpoints. Used by AddRegularLine/AddNormalLine/AddStopLine/AddLaneLine/AddCrosswalkLine
        // and the matching Remove* commands.
        [ProtoMember(4)] public PointRef A;
        [ProtoMember(5)] public PointRef B;

        // Multi-point contour. Used by AddFiller (3+ points) and RemoveFiller (lookup by contour).
        [ProtoMember(6)] public PointRef[] Contour;

        // Style serialization - the entire <Style ... /> XML chunk produced by IMT's own
        // Style.ToXml(). Receiver parses it back via Style.FromXml<T>(...).
        // We embed XML rather than re-modeling all ~40 IMT style types in protobuf - cheap and
        // robust to IMT updates.
        [ProtoMember(7)] public string StyleXml;

        // Alignment for AddRegularLine and AddNormalLine. (IMT.Manager.Alignment enum)
        [ProtoMember(8)] public byte Alignment;

        // For AddFiller: per-contour-vertex alignment (parallel array to Contour). v1 only supports
        // contours made of EnterFillerVertex (the common user-click case); each entry pairs with
        // the same-index PointRef in Contour.
        [ProtoMember(9)] public byte[] ContourAlignments;

        // For SetPointOffset: the new Offset value (meters along the road's incoming edge).
        // Point identity uses field A.
        [ProtoMember(10)] public float Offset;

        // For AddFiller/RemoveFiller v2: richer contour supporting all IFillerVertex subclasses
        // (Enter / LineEnd / Intersect). When this is populated, the legacy Contour /
        // ContourAlignments fields at tags 6/9 are ignored on the receiver.
        [ProtoMember(11)] public FillerVertexRef[] Vertices;

        // Tier-2 LWW version stamp. Lamport-clock-style ulong assigned by the sender.
        // Receiver uses EditClock.ShouldApply(elementId, Version) to gate dispatch and rejects
        // stale edits. Zero is the default; ClearMarking / ResetOffsets are unversioned (always
        // applied), other action types are gated.
        [ProtoMember(12)] public ulong Version;

        // Display name of the player who emitted the action - used by SelectIntersection
        // (presence chat notification). Pulled from CSM.API.Chat.GetCurrentUsername() at send.
        [ProtoMember(13)] public string ClaimantName;

        // CursorPresence: local cursor world position. Throttled at 10 Hz on send. On receive,
        // routed to CSM's ToolSimulatorCursorManager.GetCursorView(senderId).SetLabelContent(...).
        [ProtoMember(14)] public float CursorX;
        [ProtoMember(15)] public float CursorY;
        [ProtoMember(16)] public float CursorZ;
    }
}

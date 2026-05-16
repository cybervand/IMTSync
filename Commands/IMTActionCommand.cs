using CSM.API.Commands;
using ProtoBuf;

namespace CSM.IMTSync.Commands
{
    /// <summary>
    /// Single polymorphic command for all low-frequency IMT mutations.
    /// Fields not relevant to a given <see cref="Type"/> are left at default values.
    /// Phase 1 only uses Type, Scope, MarkingId for ClearMarking.
    /// Later phases will populate A, B, Contour, StyleXml, Alignment.
    ///
    /// Wire-format rule: only ever APPEND new ProtoMember tags. Never reuse a tag for a different
    /// field. Adding a field at a higher tag is safe across versions.
    /// </summary>
    [ProtoContract]
    public class IMTActionCommand : CommandBase
    {
        [ProtoMember(1)]
        public IMTActionType Type;

        [ProtoMember(2)]
        public MarkingScope Scope;

        [ProtoMember(3)]
        public ushort MarkingId;

        // Reserved for Phase 2 (line endpoints, contour, style XML, alignment).
        // Left as comments here so the tag numbering is documented:
        // [ProtoMember(4)] public PointRef A;
        // [ProtoMember(5)] public PointRef B;
        // [ProtoMember(6)] public PointRef[] Contour;
        // [ProtoMember(7)] public string StyleXml;
        // [ProtoMember(8)] public byte Alignment;
    }
}

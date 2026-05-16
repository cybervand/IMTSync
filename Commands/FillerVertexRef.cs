using ProtoBuf;

namespace CSM.IMTSync.Commands
{
    /// <summary>
    /// Discriminated wire format for a single filler-contour vertex.
    /// Replaces the legacy (PointRef[] Contour + byte[] ContourAlignments) pair on AddFiller
    /// and RemoveFiller. Supports all three concrete IFillerVertex subclasses IMT exposes.
    ///
    /// Field-usage per Kind:
    ///   Enter      P1 = point                                            | Align = alignment
    ///   LineEnd    P1 = anchor point (one endpoint of the line)
    ///              P2 = the other endpoint of the line
    ///              (P1+P2 identify the MarkingRegularLine on the receiver)
    ///   Intersect  P1+P2 = line 1 endpoints
    ///              P3+P4 = line 2 endpoints
    /// </summary>
    [ProtoContract]
    public struct FillerVertexRef
    {
        [ProtoMember(1)] public FillerVertexKind Kind;
        [ProtoMember(2)] public PointRef P1;
        [ProtoMember(3)] public PointRef P2;
        [ProtoMember(4)] public PointRef P3;
        [ProtoMember(5)] public PointRef P4;
        [ProtoMember(6)] public byte Align;   // Enter only; ignored for LineEnd/Intersect
    }

    public enum FillerVertexKind : byte
    {
        Enter     = 0,
        LineEnd   = 1,
        Intersect = 2,
    }
}

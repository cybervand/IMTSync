using ProtoBuf;

namespace CSM.IMTSync.Commands
{
    /// <summary>
    /// Wire-format reference to a single MarkingPoint.
    /// 6 bytes total. Identifies a point uniquely across clients (point IDs are deterministic
    /// from network topology).
    ///
    /// Send: extract from IMT.Manager.MarkingPoint via point.Enter.Id (entrance) and point.Index.
    /// Recv: resolve via IMT.API.IDataProviderV1.GetOrCreateXMarking(MarkingId)
    ///        .TryGetEntrance(EntranceId, out e).GetXPoint(Index, out p).
    /// </summary>
    [ProtoContract]
    public struct PointRef
    {
        [ProtoMember(1)] public ushort MarkingId;     // node or segment ID
        [ProtoMember(2)] public ushort EntranceId;    // segment ID for nodes, node ID for segments
        [ProtoMember(3)] public byte   Index;          // position within entrance points
        [ProtoMember(4)] public PointKind Kind;        // entrance / normal / crosswalk / lane
    }

    /// <summary>Selects which GetXPoint accessor to use on receive.</summary>
    public enum PointKind : byte
    {
        Entrance  = 0,  // most common - used by AddRegularLine, AddNormalLine, AddStopLine
        Normal    = 1,  // used by AddNormalLine end-point
        Crosswalk = 2,  // used by AddCrosswalk endpoints
        Lane      = 3,  // used by AddLaneLine endpoints
    }
}

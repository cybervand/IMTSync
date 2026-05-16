using IMT.API;
using IMT.Manager;
using CSM.IMTSync.Commands;

namespace CSM.IMTSync.Services
{
    /// <summary>
    /// Resolves wire-format <see cref="PointRef"/> back into either an IMT.API IPointData
    /// (for API-only receive paths) or an IMT.Manager.MarkingPoint (for direct-internal paths
    /// like Marking.AddRegularLine which take MarkingPointPair&lt;MarkingPoint&gt;).
    /// </summary>
    internal static class PointResolver
    {
        // ----- API path (preferred for simple cases) -----

        public static bool TryResolveEntrance(IDataProviderV1 provider, PointRef r, out IEntrancePointData point)
        {
            point = null;
            if (provider == null) return false;

            // Try node first, then segment
            if (provider.TryGetNodeMarking(r.MarkingId, out var node))
            {
                if (!node.TryGetEntrance(r.EntranceId, out var entrance)) return false;
                return entrance.GetEntrancePoint(r.Index, out point);
            }
            if (provider.TryGetSegmentMarking(r.MarkingId, out var seg))
            {
                if (!seg.TryGetEntrance(r.EntranceId, out var entrance)) return false;
                return entrance.GetEntrancePoint(r.Index, out point);
            }
            return false;
        }

        // ----- Internal path (needed for Marking.AddRegularLine & friends which take
        //       MarkingPointPair of internal MarkingPoint, not IEntrancePointData) -----

        public static bool TryResolveInternalEnterPoint(Marking marking, PointRef r, out MarkingEnterPoint point)
        {
            point = null;
            if (marking == null) return false;
            // Marking exposes Entrances via internal types; we walk to find the matching one.
            // Each Entrance has an Id (ushort) and a list of MarkingEnterPoints with Index (byte).
            foreach (var entrance in marking.Enters)
            {
                if (entrance.Id != r.EntranceId) continue;
                foreach (var ep in entrance.EnterPoints)
                {
                    if (ep != null && ep.Index == r.Index)
                    {
                        point = ep;
                        return true;
                    }
                }
                return false;
            }
            return false;
        }
    }
}

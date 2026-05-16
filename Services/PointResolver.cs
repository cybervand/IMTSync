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
            if (TryResolveInternalPoint(marking, r, out var resolved) && resolved is MarkingEnterPoint enterPoint)
            {
                point = enterPoint;
                return true;
            }
            return false;
        }

        public static bool TryResolveInternalPoint(Marking marking, PointRef r, out MarkingPoint point)
        {
            point = null;
            if (marking == null) return false;

            foreach (var entrance in marking.Enters)
            {
                if (entrance.Id != r.EntranceId) continue;
                return entrance.TryGetPoint(r.Index, ToPointType(r.Kind), out point);
            }
            return false;
        }

        private static MarkingPoint.PointType ToPointType(PointKind kind)
        {
            switch (kind)
            {
                case PointKind.Normal:
                    return MarkingPoint.PointType.Normal;
                case PointKind.Crosswalk:
                    return MarkingPoint.PointType.Crosswalk;
                case PointKind.Lane:
                    return MarkingPoint.PointType.Lane;
                default:
                    return MarkingPoint.PointType.Enter;
            }
        }
    }
}

using System.Collections.Generic;
using IMT.Manager;
using CSM.IMTSync.Commands;

namespace CSM.IMTSync.Services
{
    /// <summary>
    /// IMT IFillerVertex hierarchy ↔ wire-format FillerVertexRef[]. Send side uses
    /// <see cref="TryFromContour"/>; receive side reads back via the structured decode in
    /// IMTActionHandler. RemoveFiller match uses <see cref="Fingerprint"/> to compare contours
    /// across host/client without relying on object identity.
    /// </summary>
    internal static class FillerVertexConverter
    {
        /// <summary>
        /// Converts a contour's raw vertices to the wire format.
        /// Returns false (with a log warning) when the contour contains a vertex type or
        /// reference shape we don't support yet — caller should skip the broadcast.
        /// </summary>
        public static bool TryFromContour(IEnumerable<IFillerVertex> raw, out FillerVertexRef[] result)
        {
            result = null;
            var list = new List<FillerVertexRef>();
            foreach (var v in raw)
            {
                if (v is EnterFillerVertex efv)
                {
                    list.Add(new FillerVertexRef
                    {
                        Kind = FillerVertexKind.Enter,
                        P1 = ToPointRef(efv.Point),
                        Align = (byte)efv.Alignment,
                    });
                }
                else if (v is LineEndFillerVertex lev)
                {
                    var line = lev.Line;
                    if (line == null) { Log.Warn("FillerVertex LineEnd: line is null"); return false; }
                    var anchor = lev.Point;
                    if (anchor == null) { Log.Warn("FillerVertex LineEnd: point is null"); return false; }
                    var otherEnd = SameId(anchor, line.Start) ? line.End : line.Start;
                    list.Add(new FillerVertexRef
                    {
                        Kind = FillerVertexKind.LineEnd,
                        P1 = ToPointRef(anchor),
                        P2 = ToPointRef(otherEnd),
                    });
                }
                else if (v is IntersectFillerVertex ifv)
                {
                    var l1 = ifv.First; var l2 = ifv.Second;
                    if (l1 == null || l2 == null) { Log.Warn("FillerVertex Intersect: a line is null"); return false; }
                    list.Add(new FillerVertexRef
                    {
                        Kind = FillerVertexKind.Intersect,
                        P1 = ToPointRef(l1.Start),
                        P2 = ToPointRef(l1.End),
                        P3 = ToPointRef(l2.Start),
                        P4 = ToPointRef(l2.End),
                    });
                }
                else
                {
                    Log.Warn("FillerVertex: unsupported type " + (v == null ? "<null>" : v.GetType().Name));
                    return false;
                }
            }
            result = list.ToArray();
            return true;
        }

        /// <summary>
        /// Canonical string used to identify a contour for RemoveFiller matching across clients.
        /// Per-vertex key sorted so traversal direction doesn't affect equality.
        /// </summary>
        public static string Fingerprint(FillerVertexRef[] verts)
        {
            if (verts == null) return string.Empty;
            var keys = new List<string>(verts.Length);
            for (int i = 0; i < verts.Length; i++) keys.Add(VertexKey(verts[i]));
            keys.Sort(System.StringComparer.Ordinal);
            return string.Join("|", keys.ToArray());
        }

        // ---- internals ----

        private static PointRef ToPointRef(MarkingPoint p)
        {
            return new PointRef
            {
                MarkingId  = p?.Enter?.Marking?.Id ?? 0,
                EntranceId = p?.Enter?.Id ?? 0,
                Index      = p?.Index ?? 0,
                Kind       = PointKind.Entrance,
            };
        }

        private static bool SameId(MarkingPoint a, MarkingPoint b)
            => a != null && b != null && a.Id == b.Id;

        private static string VertexKey(FillerVertexRef v)
        {
            switch (v.Kind)
            {
                case FillerVertexKind.Enter:
                    return "E:" + PtKey(v.P1);
                case FillerVertexKind.LineEnd:
                    {
                        // line identity is the unordered pair {P1, P2}; but the anchor (P1) matters
                        // because two LineEnd vertices on the same line at opposite ends are distinct.
                        var a = PtKey(v.P1); var b = PtKey(v.P2);
                        var first = System.StringComparer.Ordinal.Compare(a, b) <= 0 ? a : b;
                        var second = ReferenceEquals(first, a) ? b : a;
                        return "L:" + first + "/" + second + "@" + a;
                    }
                case FillerVertexKind.Intersect:
                    {
                        var l1 = LineKey(v.P1, v.P2);
                        var l2 = LineKey(v.P3, v.P4);
                        var first = System.StringComparer.Ordinal.Compare(l1, l2) <= 0 ? l1 : l2;
                        var second = ReferenceEquals(first, l1) ? l2 : l1;
                        return "I:" + first + "&" + second;
                    }
                default:
                    return "?";
            }
        }

        private static string PtKey(PointRef r) => r.EntranceId + ":" + r.Index;
        private static string LineKey(PointRef a, PointRef b)
        {
            var ka = PtKey(a); var kb = PtKey(b);
            return System.StringComparer.Ordinal.Compare(ka, kb) <= 0 ? (ka + "/" + kb) : (kb + "/" + ka);
        }
    }
}

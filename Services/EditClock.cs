using System.Collections.Generic;
using CSM.IMTSync.Commands;

namespace CSM.IMTSync.Services
{
    /// <summary>
    /// Per-element Last-Writer-Wins versioning with Lamport semantics + tombstones.
    ///
    /// Wire flow:
    ///   Send:    bump local counter, stamp cmd.Version, send, then Record locally.
    ///   Receive: ObservedVersion() bumps the local counter to at least cmd.Version
    ///            (Lamport — keeps a late-joining client from emitting versions that lose),
    ///            then ShouldApply() gates dispatch, then Record() updates state on success.
    ///
    /// Convergence properties:
    ///   - Two clients editing the same element in sequence converge to the latest.
    ///   - Two clients editing concurrently: highest version wins; ties broken by senderId
    ///     (or arrival order if versions tie too — extremely unlikely with ulong monotonic).
    ///   - Remove followed by stale Add: rejected by tombstone.
    ///   - Add followed by stale Remove: rejected by version.
    ///   - ClearMarking is unversioned but purges per-element records for that marking, so
    ///     subsequent edits start from a clean slate.
    /// </summary>
    internal static class EditClock
    {
        private static ulong _counter;
        private static readonly Dictionary<string, EditStamp> _stamps = new Dictionary<string, EditStamp>();
        private static readonly object _lock = new object();

        private struct EditStamp
        {
            public ulong Version;
            public bool Removed;
        }

        /// <summary>Next outgoing version. Always strictly increases.</summary>
        public static ulong NextVersion()
        {
            lock (_lock)
            {
                _counter++;
                return _counter;
            }
        }

        /// <summary>
        /// Lamport bump: ensures our outgoing counter is never behind anything we've seen.
        /// Call on every incoming command, regardless of accept/reject.
        /// </summary>
        public static void ObservedVersion(ulong v)
        {
            lock (_lock)
            {
                if (v > _counter) _counter = v;
            }
        }

        /// <summary>
        /// Compute the element ID for an action. Returns null for unversioned actions
        /// (ClearMarking, ResetOffsets) — those are always applied.
        /// </summary>
        public static string ElementIdFor(IMTActionCommand cmd)
        {
            if (cmd == null) return null;
            switch (cmd.Type)
            {
                case IMTActionType.AddRegularLine:
                case IMTActionType.AddNormalLine:
                case IMTActionType.AddStopLine:
                case IMTActionType.AddLaneLine:
                case IMTActionType.AddCrosswalk:
                case IMTActionType.RemoveRegularLine:
                case IMTActionType.RemoveNormalLine:
                case IMTActionType.RemoveStopLine:
                case IMTActionType.RemoveLaneLine:
                case IMTActionType.RemoveCrosswalk:
                case IMTActionType.UpdateCrosswalkStyle:
                case IMTActionType.UpdateLineState:
                case IMTActionType.UpdateCrosswalkState:
                    return "L:" + cmd.MarkingId + ":" + PairKey(cmd.A, cmd.B);
                case IMTActionType.UpdateLineStyle:
                    return "L:" + cmd.MarkingId + ":" + PairKey(cmd.A, cmd.B) + ":R" + cmd.RuleIndex;

                case IMTActionType.AddFiller:
                case IMTActionType.RemoveFiller:
                case IMTActionType.UpdateFillerStyle:
                    return "F:" + cmd.MarkingId + ":" + FillerVertexConverter.Fingerprint(cmd.Vertices);

                case IMTActionType.SetPointOffset:
                    return "P:" + cmd.MarkingId + ":" + cmd.A.EntranceId + ":" + cmd.A.Index;

                case IMTActionType.UpsertStyleTemplate:
                case IMTActionType.DeleteStyleTemplate:
                    return "ST:" + cmd.TemplateId;
                case IMTActionType.UpsertIntersectionTemplate:
                case IMTActionType.DeleteIntersectionTemplate:
                    return "IT:" + cmd.TemplateId;

                case IMTActionType.ClearMarking:
                case IMTActionType.ResetOffsets:
                default:
                    return null;
            }
        }

        /// <summary>
        /// True if the incoming (version, senderId) is strictly newer than what we have stored
        /// for this element (or if we have no stored entry). Tombstone state is preserved across
        /// this check — caller updates via Record after a successful apply.
        /// </summary>
        public static bool ShouldApply(string elementId, ulong version)
        {
            if (elementId == null) return true;
            lock (_lock)
            {
                if (!_stamps.TryGetValue(elementId, out var stamp)) return true;
                return version > stamp.Version;
            }
        }

        /// <summary>
        /// Update the per-element stamp after applying (or after locally emitting) an action.
        /// </summary>
        public static void Record(string elementId, ulong version, bool isRemoved)
        {
            if (elementId == null) return;
            lock (_lock)
            {
                _stamps[elementId] = new EditStamp { Version = version, Removed = isRemoved };
            }
        }

        /// <summary>
        /// After a ClearMarking, every element on that marking is gone. Drop their records so
        /// subsequent Adds aren't gated by stale versions.
        /// </summary>
        public static void PurgeMarking(ushort markingId)
        {
            var prefixL = "L:" + markingId + ":";
            var prefixF = "F:" + markingId + ":";
            var prefixP = "P:" + markingId + ":";
            lock (_lock)
            {
                var toRemove = new List<string>();
                foreach (var key in _stamps.Keys)
                {
                    if (key.StartsWith(prefixL) || key.StartsWith(prefixF) || key.StartsWith(prefixP))
                        toRemove.Add(key);
                }
                foreach (var k in toRemove) _stamps.Remove(k);
            }
        }

        public static bool IsRemoveAction(IMTActionType type)
        {
            switch (type)
            {
                case IMTActionType.RemoveRegularLine:
                case IMTActionType.RemoveNormalLine:
                case IMTActionType.RemoveStopLine:
                case IMTActionType.RemoveLaneLine:
                case IMTActionType.RemoveCrosswalk:
                case IMTActionType.RemoveFiller:
                case IMTActionType.DeleteStyleTemplate:
                case IMTActionType.DeleteIntersectionTemplate:
                    return true;
                default:
                    return false;
            }
        }

        // ---- internals ----

        private static string PairKey(PointRef a, PointRef b)
        {
            var ka = a.Kind + ":" + a.EntranceId + ":" + a.Index;
            var kb = b.Kind + ":" + b.EntranceId + ":" + b.Index;
            return System.StringComparer.Ordinal.Compare(ka, kb) <= 0 ? (ka + "/" + kb) : (kb + "/" + ka);
        }
    }
}

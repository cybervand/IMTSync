using System.Collections.Generic;
using CSM.IMTSync.Commands;
using IMT.Manager;

namespace CSM.IMTSync.Services
{
    internal static class FillerIdMap
    {
        private static readonly Dictionary<string, MarkingFiller> Fillers = new Dictionary<string, MarkingFiller>();
        private static readonly object Lock = new object();

        public static void Remember(IMTActionCommand cmd, MarkingFiller localFiller)
        {
            if (cmd == null || localFiller == null || cmd.FillerId == 0) return;
            lock (Lock)
            {
                Fillers[Key(cmd)] = localFiller;
            }
        }

        /// <summary>
        /// Returns the locally-mapped filler ONLY if it's still present in <paramref name="marking"/>.
        /// A mapping can go stale after ClearMarking, MarkingSnapshot apply, or cascade removal
        /// when a line that owned the filler is deleted. Stale entries are evicted on lookup so a
        /// subsequent caller falls through to contour matching.
        /// </summary>
        public static bool TryGet(IMTActionCommand cmd, Marking marking, out MarkingFiller filler)
        {
            filler = null;
            if (cmd == null || cmd.FillerId == 0 || marking == null) return false;
            lock (Lock)
            {
                var key = Key(cmd);
                if (!Fillers.TryGetValue(key, out var candidate) || candidate == null)
                    return false;

                bool present = false;
                foreach (var f in marking.Fillers)
                {
                    if (ReferenceEquals(f, candidate)) { present = true; break; }
                }
                if (!present)
                {
                    Fillers.Remove(key);
                    return false;
                }

                filler = candidate;
                return true;
            }
        }

        public static void Forget(IMTActionCommand cmd)
        {
            if (cmd == null || cmd.FillerId == 0) return;
            lock (Lock)
            {
                Fillers.Remove(Key(cmd));
            }
        }

        /// <summary>
        /// Drop every cached mapping that targets a given marking — used after ClearMarking and
        /// after a full MarkingSnapshot apply, both of which replace the marking's filler list and
        /// invalidate any held MarkingFiller references.
        /// </summary>
        public static void ForgetMarking(MarkingScope scope, ushort markingId)
        {
            // Key shape: senderId:scope:markingId:fillerId. Substring match on ":scope:markingId:"
            // catches every sender's entry for this marking; senderIds are ints so no colon
            // collisions, and the trailing ':' prevents false hits on fillerIds equal to markingId.
            var midSeg = ":" + scope + ":" + markingId + ":";
            lock (Lock)
            {
                List<string> toRemove = null;
                foreach (var k in Fillers.Keys)
                {
                    if (k.IndexOf(midSeg, System.StringComparison.Ordinal) >= 0)
                    {
                        if (toRemove == null) toRemove = new List<string>();
                        toRemove.Add(k);
                    }
                }
                if (toRemove != null)
                {
                    for (int i = 0; i < toRemove.Count; i++) Fillers.Remove(toRemove[i]);
                }
            }
        }

        private static string Key(IMTActionCommand cmd)
        {
            return cmd.SenderId + ":" + cmd.Scope + ":" + cmd.MarkingId + ":" + cmd.FillerId;
        }
    }
}

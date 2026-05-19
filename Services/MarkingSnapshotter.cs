using System;
using System.Xml.Linq;
using IMT.Manager;
using IMT.Utilities;
using ModsCommon;
using CSM.IMTSync.Commands;

namespace CSM.IMTSync.Services
{
    /// <summary>
    /// Mid-session state push helper. Produces and applies <see cref="IMTActionType.MarkingSnapshot"/>
    /// payloads using IMT's own Marking.ToXml / FromXml machinery — same path as
    /// IMT.Utilities.MoveItIntegration and the savegame extension.
    ///
    /// Use when a new client joins mid-session: the server iterates its loaded markings and
    /// broadcasts one snapshot per marking; the client receives each and replays via
    /// <see cref="Marking.FromXml"/> wrapped in a CsmBridge.StartIgnore scope so our own
    /// postfixes don't echo.
    /// </summary>
    internal static class MarkingSnapshotter
    {
        private static readonly Version _imtVersion = ResolveImtVersion();

        /// <summary>
        /// Build a snapshot command from a loaded marking. Returns null if the marking is empty
        /// (no points/lines/fillers/crosswalks) — no need to ship vacuous payloads.
        /// </summary>
        public static IMTActionCommand BuildSnapshot(Marking marking)
        {
            if (marking == null) return null;
            XElement xml;
            try { xml = marking.ToXml(); }
            catch (Exception ex) { Log.Warn("Marking.ToXml threw: " + ex.Message); return null; }
            if (xml == null) return null;
            // Cheap is-this-marking-empty check: if the only child is the root attributes, skip.
            // Marking.ToXml always includes at least an Id attribute; lines/fillers/crosswalks
            // are child elements. No children == empty marking == nothing to sync.
            bool hasContent = false;
            foreach (var _ in xml.Elements()) { hasContent = true; break; }
            if (!hasContent) return null;

            return new IMTActionCommand
            {
                Type = IMTActionType.MarkingSnapshot,
                Scope = marking is NodeMarking ? MarkingScope.Node : MarkingScope.Segment,
                MarkingId = marking.Id,
                MarkingXml = xml.ToString(SaveOptions.DisableFormatting),
                ImtVersion = _imtVersion.ToString(),
            };
        }

        /// <summary>
        /// Apply a snapshot to the local marking state. Caller should already be inside a
        /// CsmBridge.StartIgnore scope so our postfixes don't re-broadcast.
        /// </summary>
        public static bool TryApply(IMTActionCommand cmd)
        {
            if (cmd == null || string.IsNullOrEmpty(cmd.MarkingXml)) return false;
            if (cmd.MarkingId == 0) return false;

            Marking marking;
            try
            {
                marking = cmd.Scope == MarkingScope.Node
                    ? (Marking)SingletonManager<NodeMarkingManager>.Instance.GetOrCreateMarking(cmd.MarkingId)
                    : (Marking)SingletonManager<SegmentMarkingManager>.Instance.GetOrCreateMarking(cmd.MarkingId);
            }
            catch (Exception ex) { Log.Warn("MarkingSnapshot: GetOrCreateMarking threw: " + ex.Message); return false; }
            if (marking == null) { Log.Warn("MarkingSnapshot: marking unresolved"); return false; }

            XElement xml;
            try { xml = StyleSerializer.LoadXml(cmd.MarkingXml); }
            catch (Exception ex) { Log.Warn("MarkingSnapshot: XML parse threw: " + ex.Message); return false; }
            if (xml == null) return false;

            Version version = _imtVersion;
            if (!string.IsNullOrEmpty(cmd.ImtVersion))
            {
                try { version = new Version(cmd.ImtVersion); }
                catch (Exception) { /* fall back to local version */ }
            }

            try
            {
                marking.Clear();
                // Clear() drops the local MarkingFiller instances; any cached FillerIdMap entries
                // for this marking now point at dead objects. Purge them before FromXml rebuilds
                // a fresh set so subsequent UpdateFillerStyle / RemoveFiller fall through to
                // contour matching instead of poking the stale references.
                FillerIdMap.ForgetMarking(cmd.Scope, cmd.MarkingId);
                marking.FromXml(version, xml, new ObjectsMap(), needUpdate: true);
                Log.Info($"Applied MarkingSnapshot on {cmd.Scope} {cmd.MarkingId} (sender IMT v{cmd.ImtVersion}).");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Marking.FromXml threw: " + ex);
                return false;
            }
        }

        private static Version ResolveImtVersion()
        {
            try { return typeof(Marking).Assembly.GetName().Version ?? new Version(0, 0); }
            catch { return new Version(0, 0); }
        }
    }
}

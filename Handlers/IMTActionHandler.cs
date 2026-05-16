using System;
using CSM.API.Commands;
using CSM.IMTSync.Commands;
using CSM.IMTSync.Services;
using IMT.API;
using IMT.Manager;
using ModsCommon;
// Alignment exists in both IMT.API and IMT.Manager - we use the Manager one (the patch sees it).
using Alignment = IMT.Manager.Alignment;

namespace CSM.IMTSync.Handlers
{
    /// <summary>
    /// Receives IMTActionCommand from CSM and applies it locally.
    /// Wraps every apply in IgnoreHelper so our own postfixes don't re-broadcast.
    /// </summary>
    public class IMTActionHandler : CommandHandler<IMTActionCommand>
    {
        protected override void Handle(IMTActionCommand cmd)
        {
            if (cmd == null) { Log.Warn("Handle called with null command"); return; }

            // Diagnostic - log every incoming command before doing anything else, so two-PC tests
            // can distinguish "packet arrived" from "applied correctly".
            Log.Info($"RECV from sender {cmd.SenderId}: {cmd.Type} on {cmd.Scope} {cmd.MarkingId}");

            try
            {
                using (CsmBridge.StartIgnore())
                {
                    var provider = Helper.GetProvider("CSM.IMTSync");
                    if (provider == null)
                    {
                        Log.Warn("IMT.API.Helper.GetProvider returned null - IMT not loaded?");
                        return;
                    }

                    switch (cmd.Type)
                    {
                        case IMTActionType.ClearMarking:
                            ApplyClear(provider, cmd);
                            break;

                        case IMTActionType.AddRegularLine:
                            ApplyAddRegularLine(cmd);
                            break;

                        default:
                            Log.Warn($"Unhandled IMTActionType: {cmd.Type}");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("IMTActionHandler.Handle threw: " + ex);
            }
        }

        private static void ApplyClear(IDataProviderV1 provider, IMTActionCommand cmd)
        {
            switch (cmd.Scope)
            {
                case MarkingScope.Node:
                    if (provider.TryGetNodeMarking(cmd.MarkingId, out var node))
                    {
                        node.ClearMarkings();
                        Log.Info($"Applied remote ClearMarking on node {cmd.MarkingId}");
                    }
                    else
                    {
                        Log.Info($"Skipped ClearMarking on node {cmd.MarkingId} - no marking exists on this client (no-op).");
                    }
                    break;

                case MarkingScope.Segment:
                    if (provider.TryGetSegmentMarking(cmd.MarkingId, out var seg))
                    {
                        seg.ClearMarkings();
                        Log.Info($"Applied remote ClearMarking on segment {cmd.MarkingId}");
                    }
                    else
                    {
                        Log.Info($"Skipped ClearMarking on segment {cmd.MarkingId} - no marking exists on this client (no-op).");
                    }
                    break;
            }
        }

        /// <summary>
        /// Receive AddRegularLine. Uses internal IMT.Manager types because:
        ///   1. The patch is on Marking.AddRegularLine (internal), so we have type access already.
        ///   2. Constructing an IRegularLineStyleData from XML through the API is awkward; using
        ///      the internal Style.FromXml&lt;RegularLineStyle&gt; is direct.
        ///   3. MarkingPointPair takes internal MarkingPoint, not IEntrancePointData.
        /// </summary>
        private static void ApplyAddRegularLine(IMTActionCommand cmd)
        {
            // Find the internal Marking instance via the per-type singleton manager.
            // (ModsCommon.SingletonManager<T>; method is GetOrCreateMarking, not GetOrCreate.)
            Marking marking = null;
            if (cmd.Scope == MarkingScope.Node)
                marking = SingletonManager<NodeMarkingManager>.Instance.GetOrCreateMarking(cmd.MarkingId);
            else if (cmd.Scope == MarkingScope.Segment)
                marking = SingletonManager<SegmentMarkingManager>.Instance.GetOrCreateMarking(cmd.MarkingId);

            if (marking == null)
            {
                Log.Warn($"AddRegularLine: could not GetOrCreate marking on {cmd.Scope} {cmd.MarkingId}");
                return;
            }

            if (!PointResolver.TryResolveInternalEnterPoint(marking, cmd.A, out var startPt))
            {
                Log.Warn($"AddRegularLine: could not resolve start point ({cmd.A.EntranceId}/{cmd.A.Index})");
                return;
            }
            if (!PointResolver.TryResolveInternalEnterPoint(marking, cmd.B, out var endPt))
            {
                Log.Warn($"AddRegularLine: could not resolve end point ({cmd.B.EntranceId}/{cmd.B.Index})");
                return;
            }

            if (!StyleSerializer.TryFromXml<RegularLineStyle>(cmd.StyleXml, out var style))
            {
                Log.Warn("AddRegularLine: failed to parse style XML");
                return;
            }

            var pair = new MarkingPointPair(startPt, endPt);
            var alignment = (Alignment)cmd.Alignment;
            marking.AddRegularLine(pair, style, alignment);
            Log.Info($"Applied remote AddRegularLine on {cmd.Scope} {cmd.MarkingId}");
        }
    }
}

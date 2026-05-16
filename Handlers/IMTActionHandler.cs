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
            Log.Info($"RECV from sender {cmd.SenderId}: {cmd.Type} on {cmd.Scope} {cmd.MarkingId}");

            try
            {
                using (CsmBridge.StartIgnore())
                {
                    var provider = Helper.GetProvider("CSM.IMTSync");
                    if (provider == null) { Log.Warn("IMT.API.Helper.GetProvider returned null"); return; }

                    var marking = ResolveMarking(cmd);
                    if (marking == null)
                    {
                        Log.Warn($"Could not resolve {cmd.Scope} marking {cmd.MarkingId}");
                        return;
                    }

                    switch (cmd.Type)
                    {
                        // Whole-marking ops
                        case IMTActionType.ClearMarking:  marking.Clear(); LogApplied(cmd); break;
                        case IMTActionType.ResetOffsets:  marking.ResetOffsets(); LogApplied(cmd); break;

                        // Line adds (use internal Manager types: MarkingPointPair + concrete style)
                        case IMTActionType.AddRegularLine: ApplyAddLine<RegularLineStyle>(marking, cmd, (m, p, s) => m.AddRegularLine(p, s, (Alignment)cmd.Alignment)); break;
                        case IMTActionType.AddNormalLine:  ApplyAddLine<RegularLineStyle>(marking, cmd, (m, p, s) => m.AddNormalLine(p, s, (Alignment)cmd.Alignment)); break;
                        case IMTActionType.AddStopLine:    ApplyAddLine<StopLineStyle>(marking, cmd, (m, p, s) => m.AddStopLine(p, s)); break;
                        case IMTActionType.AddLaneLine:    ApplyAddLine<RegularLineStyle>(marking, cmd, (m, p, s) => m.AddLaneLine(p, s)); break;
                        case IMTActionType.AddCrosswalk:   ApplyAddLine<BaseCrosswalkStyle>(marking, cmd, (m, p, s) => m.AddCrosswalkLine(p, s)); break;

                        // Removes (any line type — use TryGetLine then RemoveLine)
                        case IMTActionType.RemoveRegularLine:
                        case IMTActionType.RemoveNormalLine:
                        case IMTActionType.RemoveStopLine:
                        case IMTActionType.RemoveLaneLine:
                        case IMTActionType.RemoveCrosswalk:
                            ApplyRemoveLine(marking, cmd);
                            break;

                        // Fillers
                        case IMTActionType.AddFiller:    ApplyAddFiller(marking, cmd); break;
                        case IMTActionType.RemoveFiller: ApplyRemoveFiller(marking, cmd); break;

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

        // ----- helpers -----

        private static Marking ResolveMarking(IMTActionCommand cmd)
        {
            if (cmd.Scope == MarkingScope.Node)
                return SingletonManager<NodeMarkingManager>.Instance.GetOrCreateMarking(cmd.MarkingId);
            if (cmd.Scope == MarkingScope.Segment)
                return SingletonManager<SegmentMarkingManager>.Instance.GetOrCreateMarking(cmd.MarkingId);
            return null;
        }

        private delegate void AddLineCall<TStyle>(Marking marking, MarkingPointPair pair, TStyle style) where TStyle : Style;

        private static void ApplyAddLine<TStyle>(Marking marking, IMTActionCommand cmd, AddLineCall<TStyle> add) where TStyle : Style
        {
            if (!PointResolver.TryResolveInternalEnterPoint(marking, cmd.A, out var startPt))
            { Log.Warn($"{cmd.Type}: cannot resolve start point ({cmd.A.EntranceId}/{cmd.A.Index})"); return; }
            if (!PointResolver.TryResolveInternalEnterPoint(marking, cmd.B, out var endPt))
            { Log.Warn($"{cmd.Type}: cannot resolve end point ({cmd.B.EntranceId}/{cmd.B.Index})"); return; }
            if (!StyleSerializer.TryFromXml<TStyle>(cmd.StyleXml, out var style))
            { Log.Warn($"{cmd.Type}: failed to parse style XML"); return; }

            try { add(marking, new MarkingPointPair(startPt, endPt), style); LogApplied(cmd); }
            catch (Exception ex) { Log.Error($"{cmd.Type} apply threw: " + ex); }
        }

        private static void ApplyRemoveLine(Marking marking, IMTActionCommand cmd)
        {
            if (!PointResolver.TryResolveInternalEnterPoint(marking, cmd.A, out var startPt))
            { Log.Info($"{cmd.Type}: cannot resolve start point - probably already gone (no-op)"); return; }
            if (!PointResolver.TryResolveInternalEnterPoint(marking, cmd.B, out var endPt))
            { Log.Info($"{cmd.Type}: cannot resolve end point - probably already gone (no-op)"); return; }

            var pair = new MarkingPointPair(startPt, endPt);
            if (!marking.TryGetLine(pair, out var line) || line == null)
            {
                Log.Info($"{cmd.Type}: line not present on this client (no-op).");
                return;
            }
            try { marking.RemoveLine(line); LogApplied(cmd); }
            catch (Exception ex) { Log.Error($"{cmd.Type} apply threw: " + ex); }
        }

        // ----- Fillers -----

        private static void ApplyAddFiller(Marking marking, IMTActionCommand cmd)
        {
            if (cmd.Contour == null || cmd.Contour.Length < 3)
            { Log.Warn($"AddFiller: contour is null or has <3 vertices"); return; }
            if (cmd.ContourAlignments == null || cmd.ContourAlignments.Length != cmd.Contour.Length)
            { Log.Warn($"AddFiller: ContourAlignments missing or wrong length"); return; }

            var verts = new System.Collections.Generic.List<IFillerVertex>(cmd.Contour.Length);
            for (int i = 0; i < cmd.Contour.Length; i++)
            {
                if (!PointResolver.TryResolveInternalEnterPoint(marking, cmd.Contour[i], out var pt))
                { Log.Warn($"AddFiller: cannot resolve contour vertex {i}"); return; }
                verts.Add(new EnterFillerVertex(pt, (Alignment)cmd.ContourAlignments[i]));
            }

            if (!StyleSerializer.TryFromXml<BaseFillerStyle>(cmd.StyleXml, out var style))
            { Log.Warn("AddFiller: failed to parse style XML"); return; }

            try
            {
                var contour = new FillerContour(marking, verts);
                System.Collections.Generic.List<MarkingRegularLine> _;
                marking.AddFiller(contour, style, out _);
                LogApplied(cmd);
            }
            catch (System.Exception ex) { Log.Error("AddFiller apply threw: " + ex); }
        }

        private static void ApplyRemoveFiller(Marking marking, IMTActionCommand cmd)
        {
            if (cmd.Contour == null || cmd.Contour.Length == 0)
            { Log.Warn("RemoveFiller: contour is null/empty"); return; }

            // Resolve incoming contour to internal MarkingPoints (set comparison; order-agnostic
            // because filler [A,B,C] == [B,C,A] physically).
            var wantedIds = new System.Collections.Generic.HashSet<int>();
            foreach (var r in cmd.Contour)
            {
                if (!PointResolver.TryResolveInternalEnterPoint(marking, r, out var pt))
                { Log.Info($"RemoveFiller: cannot resolve contour vertex - probably already gone"); return; }
                wantedIds.Add(pt.Id);
            }

            MarkingFiller match = null;
            int matchCount = 0;
            foreach (var f in marking.Fillers)
            {
                if (f?.Contour == null) continue;
                var fids = new System.Collections.Generic.HashSet<int>();
                foreach (var v in f.Contour.RawVertices)
                    if (v is EnterFillerVertex efv) fids.Add(efv.Point.Id);
                if (fids.Count == wantedIds.Count && fids.SetEquals(wantedIds))
                { match = f; matchCount++; }
            }

            if (match == null) { Log.Info("RemoveFiller: no matching filler on this client (no-op)."); return; }
            if (matchCount > 1) Log.Warn($"RemoveFiller: {matchCount} fillers matched contour; removing first.");

            try { marking.RemoveFiller(match); LogApplied(cmd); }
            catch (System.Exception ex) { Log.Error("RemoveFiller apply threw: " + ex); }
        }

        private static void LogApplied(IMTActionCommand cmd)
        {
            Log.Info($"Applied remote {cmd.Type} on {cmd.Scope} {cmd.MarkingId}");
        }
    }
}

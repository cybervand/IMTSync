using System;
using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using CSM.API.Commands;
using CSM.IMTSync.Commands;
using CSM.IMTSync.Services;
using IMT.API;
using IMT.Manager;
using IMT.Utilities;
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
        private static readonly FieldInfo RegularLineRawRulesField =
            typeof(MarkingRegularLine).GetField("<RawRules>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

        protected override void Handle(IMTActionCommand cmd)
        {
            if (cmd == null) { Log.Warn("Handle called with null command"); return; }

            // Lamport bump: keep our outgoing counter from emitting versions we've already seen.
            EditClock.ObservedVersion(cmd.Version);

            // LWW gate. Stale element-scoped edits are silently dropped; unversioned actions
            // (ClearMarking, ResetOffsets) return null elementId and always pass.
            var elementId = EditClock.ElementIdFor(cmd);
            if (elementId != null && !EditClock.ShouldApply(elementId, cmd.Version))
            {
                Log.Info($"STALE {cmd.Type} v{cmd.Version} on {elementId} from sender {cmd.SenderId} - skipping");
                return;
            }

            if (cmd.Type != IMTActionType.CursorPresence && cmd.Type != IMTActionType.ToolPreview)
                Log.Info($"RECV from sender {cmd.SenderId}: {cmd.Type} v{cmd.Version} on {cmd.Scope} {cmd.MarkingId}");

            // Transient presence signals don't touch IMT state - handle before marking resolution
            // so we don't accidentally GetOrCreateMarking() for a node the remote player is just
            // visiting that we haven't loaded ourselves.
            if (cmd.Type == IMTActionType.SelectIntersection)
            {
                ApplySelectIntersection(cmd);
                return;
            }
            if (cmd.Type == IMTActionType.CursorPresence)
            {
                ApplyCursorPresence(cmd);
                return;
            }
            if (cmd.Type == IMTActionType.ToolPreview)
            {
                ApplyToolPreview(cmd);
                return;
            }
            if (cmd.Type == IMTActionType.SnapshotRequest)
            {
                ApplySnapshotRequest(cmd);
                return;
            }
            if (cmd.Type == IMTActionType.MarkingSnapshot)
            {
                ApplyMarkingSnapshot(cmd);
                return;
            }
            if (cmd.Type == IMTActionType.UpsertStyleTemplate || cmd.Type == IMTActionType.DeleteStyleTemplate ||
                cmd.Type == IMTActionType.UpsertIntersectionTemplate || cmd.Type == IMTActionType.DeleteIntersectionTemplate)
            {
                using (CsmBridge.StartIgnore())
                {
                    if (cmd.Type == IMTActionType.UpsertStyleTemplate || cmd.Type == IMTActionType.UpsertIntersectionTemplate)
                        TemplateSync.ApplyUpsert(cmd);
                    else
                        TemplateSync.ApplyDelete(cmd);
                }
                ImtUiRefresh.RequestCurrentPanel();
                if (elementId != null)
                    EditClock.Record(elementId, cmd.Version, EditClock.IsRemoveAction(cmd.Type));
                return;
            }

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
                        case IMTActionType.ClearMarking:  marking.Clear(); FillerIdMap.ForgetMarking(cmd.Scope, cmd.MarkingId); LogApplied(cmd); break;
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

                        // Single-point offset
                        case IMTActionType.SetPointOffset: ApplySetPointOffset(marking, cmd); break;

                        // Style edits
                        case IMTActionType.UpdateLineStyle:      ApplyUpdateLineStyle(marking, cmd); break;
                        case IMTActionType.UpdateFillerStyle:    ApplyUpdateFillerStyle(marking, cmd); break;
                        case IMTActionType.UpdateCrosswalkStyle: ApplyUpdateCrosswalkStyle(marking, cmd); break;
                        case IMTActionType.UpdateLineState:      ApplyUpdateLineState(marking, cmd); break;
                        case IMTActionType.UpdateCrosswalkState: ApplyUpdateCrosswalkState(marking, cmd); break;

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

            // Record this version even if apply failed (e.g., point not present). We've observed
            // it; a later stale edit for the same element should still be rejected.
            if (elementId != null)
                EditClock.Record(elementId, cmd.Version, EditClock.IsRemoveAction(cmd.Type));

            // ClearMarking wipes element state; drop per-element records so future Adds aren't
            // gated by stamps from before the clear.
            if (cmd.Type == IMTActionType.ClearMarking)
                EditClock.PurgeMarking(cmd.MarkingId);
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

        private delegate MarkingLine AddLineCall<TStyle>(Marking marking, MarkingPointPair pair, TStyle style) where TStyle : Style;

        private static void ApplyAddLine<TStyle>(Marking marking, IMTActionCommand cmd, AddLineCall<TStyle> add) where TStyle : Style
        {
            if (!PointResolver.TryResolveInternalPoint(marking, cmd.A, out var startPt))
            { Log.Warn($"{cmd.Type}: cannot resolve start point ({cmd.A.EntranceId}/{cmd.A.Index})"); return; }
            if (!PointResolver.TryResolveInternalPoint(marking, cmd.B, out var endPt))
            { Log.Warn($"{cmd.Type}: cannot resolve end point ({cmd.B.EntranceId}/{cmd.B.Index})"); return; }
            if (!StyleSerializer.TryFromXml<TStyle>(cmd.StyleXml, out var style))
            { Log.Warn($"{cmd.Type}: failed to parse style XML"); return; }

            try
            {
                var line = add(marking, new MarkingPointPair(startPt, endPt), style);
                if (!string.IsNullOrEmpty(cmd.LineXml))
                    ApplyLineXml(marking, line, cmd.LineXml, cmd.Type.ToString());
                if (!string.IsNullOrEmpty(cmd.CrosswalkXml))
                    ApplyCrosswalkXml(marking, cmd, cmd.Type.ToString());
                LogApplied(cmd);
            }
            catch (Exception ex) { Log.Error($"{cmd.Type} apply threw: " + ex); }
        }

        private static void ApplyRemoveLine(Marking marking, IMTActionCommand cmd)
        {
            if (!PointResolver.TryResolveInternalPoint(marking, cmd.A, out var startPt))
            { Log.Info($"{cmd.Type}: cannot resolve start point - probably already gone (no-op)"); return; }
            if (!PointResolver.TryResolveInternalPoint(marking, cmd.B, out var endPt))
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
            // Prefer the v2 Vertices wire format; fall back to legacy Contour+ContourAlignments
            // only when Vertices is absent (defensive — we control both sides post-v2).
            if (cmd.Vertices != null && cmd.Vertices.Length >= 3)
            {
                var verts = new System.Collections.Generic.List<IFillerVertex>(cmd.Vertices.Length);
                for (int i = 0; i < cmd.Vertices.Length; i++)
                {
                    if (!TryDecodeVertex(marking, cmd.Vertices[i], i, out var v)) return;
                    verts.Add(v);
                }

                if (!StyleSerializer.TryFromXml<BaseFillerStyle>(cmd.StyleXml, out var style))
                { Log.Warn("AddFiller: failed to parse style XML"); return; }

                try
                {
                    var contour = new FillerContour(marking, verts);
                    System.Collections.Generic.List<MarkingRegularLine> _;
                    var filler = marking.AddFiller(contour, style, out _);
                    FillerIdMap.Remember(cmd, filler);
                    LogApplied(cmd);
                }
                catch (System.Exception ex) { Log.Error("AddFiller apply threw: " + ex); }
                return;
            }

            // Legacy decode (pre-v2 wire format) - Enter-only contours
            if (cmd.Contour == null || cmd.Contour.Length < 3)
            { Log.Warn("AddFiller: no Vertices and legacy Contour is null/short"); return; }
            if (cmd.ContourAlignments == null || cmd.ContourAlignments.Length != cmd.Contour.Length)
            { Log.Warn("AddFiller: legacy ContourAlignments missing or wrong length"); return; }

            var lverts = new System.Collections.Generic.List<IFillerVertex>(cmd.Contour.Length);
            for (int i = 0; i < cmd.Contour.Length; i++)
            {
                if (!PointResolver.TryResolveInternalEnterPoint(marking, cmd.Contour[i], out var pt))
                { Log.Warn($"AddFiller (legacy): cannot resolve contour vertex {i}"); return; }
                lverts.Add(new EnterFillerVertex(pt, (Alignment)cmd.ContourAlignments[i]));
            }

            if (!StyleSerializer.TryFromXml<BaseFillerStyle>(cmd.StyleXml, out var lstyle))
            { Log.Warn("AddFiller: failed to parse style XML"); return; }

            try
            {
                var contour = new FillerContour(marking, lverts);
                System.Collections.Generic.List<MarkingRegularLine> _;
                var filler = marking.AddFiller(contour, lstyle, out _);
                FillerIdMap.Remember(cmd, filler);
                LogApplied(cmd);
            }
            catch (System.Exception ex) { Log.Error("AddFiller apply threw: " + ex); }
        }

        private static bool TryDecodeVertex(Marking marking, FillerVertexRef vref, int i, out IFillerVertex v)
        {
            v = null;
            switch (vref.Kind)
            {
                case FillerVertexKind.Enter:
                    {
                        if (!PointResolver.TryResolveInternalPoint(marking, vref.P1, out var p))
                        { Log.Warn($"AddFiller vert {i}: cannot resolve Enter point"); return false; }
                        v = new EnterFillerVertex(p, (Alignment)vref.Align);
                        return true;
                    }
                case FillerVertexKind.LineEnd:
                    {
                        if (!PointResolver.TryResolveInternalPoint(marking, vref.P1, out var p1))
                        { Log.Warn($"AddFiller vert {i}: cannot resolve LineEnd anchor"); return false; }
                        if (!PointResolver.TryResolveInternalPoint(marking, vref.P2, out var p2))
                        { Log.Warn($"AddFiller vert {i}: cannot resolve LineEnd other-end"); return false; }
                        if (!marking.TryGetLine(new MarkingPointPair(p1, p2), out var line) || line == null)
                            line = new MarkingFillerTempLine(marking, p1, p2, (Alignment)vref.Align);
                        var regLine = line as MarkingRegularLine;
                        if (regLine == null)
                        { Log.Warn($"AddFiller vert {i}: LineEnd line is not a RegularLine ({line.GetType().Name})"); return false; }
                        v = new LineEndFillerVertex(p1, regLine);
                        return true;
                    }
                case FillerVertexKind.Intersect:
                    {
                        if (!PointResolver.TryResolveInternalPoint(marking, vref.P1, out var ia))
                        { Log.Warn($"AddFiller vert {i}: Intersect L1 start unresolved"); return false; }
                        if (!PointResolver.TryResolveInternalPoint(marking, vref.P2, out var ib))
                        { Log.Warn($"AddFiller vert {i}: Intersect L1 end unresolved"); return false; }
                        if (!PointResolver.TryResolveInternalPoint(marking, vref.P3, out var ic))
                        { Log.Warn($"AddFiller vert {i}: Intersect L2 start unresolved"); return false; }
                        if (!PointResolver.TryResolveInternalPoint(marking, vref.P4, out var id))
                        { Log.Warn($"AddFiller vert {i}: Intersect L2 end unresolved"); return false; }
                        if (!marking.TryGetLine(new MarkingPointPair(ia, ib), out var l1) || l1 == null)
                        { Log.Warn($"AddFiller vert {i}: Intersect L1 line missing"); return false; }
                        if (!marking.TryGetLine(new MarkingPointPair(ic, id), out var l2) || l2 == null)
                        { Log.Warn($"AddFiller vert {i}: Intersect L2 line missing"); return false; }
                        v = new IntersectFillerVertex(l1, l2);
                        return true;
                    }
                default:
                    Log.Warn($"AddFiller vert {i}: unknown kind {vref.Kind}");
                    return false;
            }
        }

        private static void ApplyToolPreview(IMTActionCommand cmd)
        {
            Marking marking = null;
            if (cmd.PreviewKind != ToolPreviewKind.None)
                marking = ResolveMarking(cmd);
            PresenceStore.UpdatePreview(cmd.SenderId, cmd, marking);
            SnapshotDeferral.FlushReady();
        }

        private static void ApplyRemoveFiller(Marking marking, IMTActionCommand cmd)
        {
            if (FillerIdMap.TryGet(cmd, marking, out var mappedFiller))
            {
                try
                {
                    marking.RemoveFiller(mappedFiller);
                    FillerIdMap.Forget(cmd);
                    LogApplied(cmd);
                }
                catch (System.Exception ex) { Log.Error("RemoveFiller mapped apply threw: " + ex); }
                return;
            }

            // v2 path
            if (cmd.Vertices != null && cmd.Vertices.Length > 0)
            {
                var wantedFp = FillerVertexConverter.Fingerprint(cmd.Vertices);
                MarkingFiller match = null;
                int matchCount = 0;
                foreach (var f in marking.Fillers)
                {
                    if (f?.Contour == null) continue;
                    if (!FillerVertexConverter.TryFromContour(f.Contour.RawVertices, out var fverts)) continue;
                    if (FillerVertexConverter.Fingerprint(fverts) == wantedFp)
                    { match = f; matchCount++; }
                }
                if (match == null) { Log.Info("RemoveFiller: no matching filler on this client (no-op)."); return; }
                if (matchCount > 1) Log.Warn($"RemoveFiller: {matchCount} fillers matched contour; removing first.");
                try { marking.RemoveFiller(match); FillerIdMap.Forget(cmd); LogApplied(cmd); }
                catch (System.Exception ex) { Log.Error("RemoveFiller apply threw: " + ex); }
                return;
            }

            // Legacy decode
            if (cmd.Contour == null || cmd.Contour.Length == 0)
            { Log.Warn("RemoveFiller: no Vertices and no legacy Contour"); return; }

            var wantedIds = new System.Collections.Generic.HashSet<int>();
            foreach (var r in cmd.Contour)
            {
                if (!PointResolver.TryResolveInternalEnterPoint(marking, r, out var pt))
                { Log.Info("RemoveFiller (legacy): cannot resolve contour vertex - probably already gone"); return; }
                wantedIds.Add(pt.Id);
            }

            MarkingFiller lmatch = null;
            int lcount = 0;
            foreach (var f in marking.Fillers)
            {
                if (f?.Contour == null) continue;
                var fids = new System.Collections.Generic.HashSet<int>();
                bool allEnter = true;
                foreach (var v in f.Contour.RawVertices)
                {
                    if (v is EnterFillerVertex efv) fids.Add(efv.Point.Id);
                    else { allEnter = false; break; }
                }
                if (!allEnter) continue;
                if (fids.Count == wantedIds.Count && fids.SetEquals(wantedIds))
                { lmatch = f; lcount++; }
            }

            if (lmatch == null) { Log.Info("RemoveFiller (legacy): no matching filler on this client (no-op)."); return; }
            if (lcount > 1) Log.Warn($"RemoveFiller (legacy): {lcount} fillers matched contour; removing first.");
            try { marking.RemoveFiller(lmatch); FillerIdMap.Forget(cmd); LogApplied(cmd); }
            catch (System.Exception ex) { Log.Error("RemoveFiller apply threw: " + ex); }
        }

        // ----- SetPointOffset -----

        private static void ApplySetPointOffset(Marking marking, IMTActionCommand cmd)
        {
            if (!PointResolver.TryResolveInternalEnterPoint(marking, cmd.A, out var pt))
            { Log.Warn($"SetPointOffset: cannot resolve point ({cmd.A.EntranceId}/{cmd.A.Index})"); return; }
            try
            {
                pt.Offset.Value = cmd.Offset;
                LogApplied(cmd);
            }
            catch (Exception ex) { Log.Error("SetPointOffset apply threw: " + ex); }
        }

        // ----- Mid-session state push -----

        private static void ApplySnapshotRequest(IMTActionCommand cmd)
        {
            // Only the server responds. Clients receive their own broadcast back via SendToAll
            // but ignore (they're the requester). Other clients also ignore.
            try
            {
                var mm = global::CSM.Networking.MultiplayerManager.Instance;
                if (mm == null || mm.CurrentRole != global::CSM.API.Commands.MultiplayerRole.Server)
                {
                    Log.Info($"SnapshotRequest from sender {cmd.SenderId} ignored (role={mm?.CurrentRole.ToString() ?? "(none)"}).");
                    return;
                }
            }
            catch (Exception ex) { Log.Warn("SnapshotRequest role check threw: " + ex.Message); return; }

            int sent = 0;
            try
            {
                foreach (var m in SingletonManager<NodeMarkingManager>.Instance)
                {
                    if (PresenceStore.HasActiveConstruction(MarkingScope.Node, m.Id))
                    {
                        SnapshotDeferral.Request(MarkingScope.Node, m.Id, "snapshot request while construction preview is active");
                        continue;
                    }
                    var snap = MarkingSnapshotter.BuildSnapshot(m);
                    if (snap == null) continue;
                    CsmBridge.SendToAll(snap);
                    sent++;
                }
            }
            catch (Exception ex) { Log.Error("Enumerating NodeMarkings threw: " + ex); }

            try
            {
                foreach (var m in SingletonManager<SegmentMarkingManager>.Instance)
                {
                    if (PresenceStore.HasActiveConstruction(MarkingScope.Segment, m.Id))
                    {
                        SnapshotDeferral.Request(MarkingScope.Segment, m.Id, "snapshot request while construction preview is active");
                        continue;
                    }
                    var snap = MarkingSnapshotter.BuildSnapshot(m);
                    if (snap == null) continue;
                    CsmBridge.SendToAll(snap);
                    sent++;
                }
            }
            catch (Exception ex) { Log.Error("Enumerating SegmentMarkings threw: " + ex); }

            Log.Info($"SnapshotRequest from sender {cmd.SenderId} - sent {sent} marking snapshots.");
        }

        private static void ApplyMarkingSnapshot(IMTActionCommand cmd)
        {
            if (PresenceStore.HasActiveConstruction(cmd.Scope, cmd.MarkingId))
            {
                Log.Info($"MarkingSnapshot on {cmd.Scope} {cmd.MarkingId} ignored because construction preview is active; server will send a fresh snapshot after it goes quiet.");
                return;
            }

            // Wrap in IgnoreScope so the burst of Add* invocations from FromXml don't re-broadcast.
            try
            {
                using (CsmBridge.StartIgnore())
                {
                    if (MarkingSnapshotter.TryApply(cmd))
                        ImtUiRefresh.Request(cmd.Scope, cmd.MarkingId);
                }
            }
            catch (Exception ex) { Log.Error("ApplyMarkingSnapshot threw: " + ex); }
        }

        // ----- CursorPresence (Tier β v1) -----
        // Routes through CSM's ToolSimulatorCursorManager so the cursor sprite + name label
        // render via CSM's PlayerCursorManager, exactly like for vanilla CS tools.

        private static void ApplyCursorPresence(IMTActionCommand cmd)
        {
            try
            {
                var mgr = ColossalFramework.Singleton<global::CSM.BaseGame.Injections.Tools.ToolSimulatorCursorManager>.instance;
                if (mgr == null) return;
                var pcm = mgr.GetCursorView(cmd.SenderId);
                if (pcm == null) return;

                // PCM.Start() creates the sprite invisible. SetCursor(null) makes PCM fall back to
                // DefaultTool.m_cursor (CS's default cursor texture) and flips isVisible=true.
                // Idempotent: SetCursor early-outs if the cursor info hasn't changed.
                pcm.SetCursor(null);

                var pos = new UnityEngine.Vector3(cmd.CursorX, cmd.CursorY, cmd.CursorZ);
                var name = string.IsNullOrEmpty(cmd.ClaimantName) ? ("sender " + cmd.SenderId) : cmd.ClaimantName;
                pcm.SetLabelContent(name, pos);
            }
            catch (Exception ex) { Log.Warn("CursorPresence apply threw: " + ex.Message); }
        }

        // ----- SelectIntersection (Tier 1 presence) -----

        private static void ApplySelectIntersection(IMTActionCommand cmd)
        {
            var who = string.IsNullOrEmpty(cmd.ClaimantName) ? "sender " + cmd.SenderId : cmd.ClaimantName;
            string msg = cmd.MarkingId == 0
                ? $"{who} is no longer editing an intersection."
                : $"{who} is editing {cmd.Scope} {cmd.MarkingId}.";
            try { global::CSM.API.Chat.Instance?.PrintGameMessage(msg); }
            catch (Exception ex) { Log.Warn("Chat.PrintGameMessage threw: " + ex.Message); }

            // Resolve the marking on the receiver so the render hot-path can iterate entrance
            // points without a per-frame manager lookup. GetOrCreateMarking is what IMT's own
            // code uses on first access, so creating a (possibly empty) marking here is safe.
            Marking remoteMarking = null;
            try
            {
                if (cmd.MarkingId != 0)
                {
                    remoteMarking = cmd.Scope == MarkingScope.Node
                        ? (Marking)SingletonManager<NodeMarkingManager>.Instance.GetOrCreateMarking(cmd.MarkingId)
                        : (Marking)SingletonManager<SegmentMarkingManager>.Instance.GetOrCreateMarking(cmd.MarkingId);
                }
            }
            catch (Exception ex) { Log.Warn("SelectIntersection: GetOrCreateMarking threw: " + ex.Message); }

            // Drive the in-world claim ring + entrance-point overlay renderer.
            PresenceStore.Update(cmd.SenderId, cmd.Scope, cmd.MarkingId, cmd.ClaimantName, remoteMarking);

            // Deselection also clears the remote cursor sprite (CSM keeps it cached otherwise).
            if (cmd.MarkingId == 0)
            {
                try
                {
                    ColossalFramework.Singleton<global::CSM.BaseGame.Injections.Tools.ToolSimulatorCursorManager>
                        .instance?.RemoveCursorView(cmd.SenderId);
                }
                catch (Exception ex) { Log.Warn("RemoveCursorView threw: " + ex.Message); }
            }

            Log.Info("Applied remote SelectIntersection: " + msg);
        }

        // ----- Style edits -----

        private static void ApplyUpdateLineState(Marking marking, IMTActionCommand cmd)
        {
            if (!PointResolver.TryResolveInternalPoint(marking, cmd.A, out var p1))
            { Log.Info("UpdateLineState: cannot resolve start point " + DescribePointRef(cmd.A)); return; }
            if (!PointResolver.TryResolveInternalPoint(marking, cmd.B, out var p2))
            { Log.Info("UpdateLineState: cannot resolve end point " + DescribePointRef(cmd.B)); return; }
            if (!marking.TryGetLine(new MarkingPointPair(p1, p2), out var line) || line == null)
            { Log.Info("UpdateLineState: line not present on this client"); return; }

            ApplyLineXml(marking, line, cmd.LineXml, "UpdateLineState");
            LogApplied(cmd);
        }

        private static void ApplyLineXml(Marking marking, MarkingLine line, string xml, string context)
        {
            if (line == null) { Log.Warn(context + ": line is null"); return; }
            var elem = StyleSerializer.LoadXml(xml);
            if (elem == null) { Log.Warn(context + ": failed to parse line XML"); return; }

            try
            {
                var map = new ObjectsMap(false, false);
                elem.SetAttributeValue(nameof(MarkingLine.Id), line.Id);
                elem.SetAttributeValue("T", (int)line.Type);
                ClearRegularLineRules(line);
                line.FromXml(elem, map, false, false);
                marking.Update(line, true, true);
            }
            catch (Exception ex) { Log.Error(context + " apply line XML threw: " + ex); }
        }

        private static void ClearRegularLineRules(MarkingLine line)
        {
            var regLine = line as MarkingRegularLine;
            if (regLine == null) return;

            try
            {
                var rules = RegularLineRawRulesField?.GetValue(regLine) as IList;
                rules?.Clear();
            }
            catch (Exception ex) { Log.Warn("ClearRegularLineRules threw: " + ex.Message); }
        }

        private static void ApplyUpdateLineStyle(Marking marking, IMTActionCommand cmd)
        {
            if (!PointResolver.TryResolveInternalPoint(marking, cmd.A, out var p1))
            { Log.Info("UpdateLineStyle: cannot resolve start point " + DescribePointRef(cmd.A)); return; }
            if (!PointResolver.TryResolveInternalPoint(marking, cmd.B, out var p2))
            { Log.Info("UpdateLineStyle: cannot resolve end point " + DescribePointRef(cmd.B)); return; }
            if (!marking.TryGetLine(new MarkingPointPair(p1, p2), out var line) || line == null)
            { Log.Info("UpdateLineStyle: line not present on this client"); return; }
            var regLine = line as MarkingRegularLine;
            if (regLine == null) { Log.Warn($"UpdateLineStyle: line is not a RegularLine ({line.GetType().Name})"); return; }
            if (regLine.RuleCount == 0)
            { Log.Warn("UpdateLineStyle: line has no rules"); return; }
            if (!StyleSerializer.TryFromXml<LineStyle>(cmd.StyleXml, out var style))
            { Log.Warn("UpdateLineStyle: failed to parse style XML"); return; }
            Log.Info("UpdateLineStyle parsed " + StyleDiagnostics.Describe(style));

            MarkingLineRawRule targetRule = null;
            var currentIndex = 0;
            foreach (var r in regLine.Rules)
            {
                if (currentIndex == cmd.RuleIndex)
                {
                    targetRule = r;
                    break;
                }
                currentIndex++;
            }
            if (targetRule == null)
            {
                Log.Warn($"UpdateLineStyle: rule {cmd.RuleIndex} not present; this client has {regLine.RuleCount} rules");
                return;
            }

            try { targetRule.Style.Value = style; LogApplied(cmd); }
            catch (Exception ex) { Log.Error("UpdateLineStyle apply threw: " + ex); }
        }

        private static void ApplyUpdateFillerStyle(Marking marking, IMTActionCommand cmd)
        {
            if (cmd.Vertices == null || cmd.Vertices.Length < 3)
            { Log.Warn("UpdateFillerStyle: no vertices to identify filler"); return; }
            if (!StyleSerializer.TryFromXml<BaseFillerStyle>(cmd.StyleXml, out var style))
            { Log.Warn("UpdateFillerStyle: failed to parse style XML"); return; }
            Log.Info("UpdateFillerStyle parsed " + StyleDiagnostics.Describe(style));

            if (FillerIdMap.TryGet(cmd, marking, out var mappedFiller))
            {
                try
                {
                    mappedFiller.Style.Value = style;
                    LogApplied(cmd);
                }
                catch (Exception ex) { Log.Error("UpdateFillerStyle mapped apply threw: " + ex); }
                return;
            }

            var wantedFp = FillerVertexConverter.Fingerprint(cmd.Vertices);
            var wantedPoints = FillerVertexConverter.PointKeySet(cmd.Vertices);
            MarkingFiller match = null;
            int matchCount = 0;
            MarkingFiller best = null;
            int bestScore = -1;
            int fillerCount = 0;
            foreach (var f in marking.Fillers)
            {
                if (f?.Contour == null) continue;
                fillerCount++;
                if (!FillerVertexConverter.TryFromContour(f.Contour.RawVertices, out var fverts)) continue;
                if (FillerVertexConverter.Fingerprint(fverts) == wantedFp) { match = f; matchCount++; }

                var score = SharedPointCount(wantedPoints, FillerVertexConverter.PointKeySet(fverts));
                if (score > bestScore)
                {
                    best = f;
                    bestScore = score;
                }
            }
            if (match == null && fillerCount == 1 && best != null)
            {
                match = best;
                Log.Warn("UpdateFillerStyle: exact contour miss; applying to only filler on marking.");
            }
            if (match == null && best != null && bestScore >= 3)
            {
                match = best;
                Log.Warn($"UpdateFillerStyle: exact contour miss; applying best point-overlap match score={bestScore}.");
            }
            if (match == null) { Log.Info("UpdateFillerStyle: no matching filler on this client"); return; }
            if (matchCount > 1) Log.Warn($"UpdateFillerStyle: {matchCount} fillers matched; applying to first.");

            try { match.Style.Value = style; LogApplied(cmd); }
            catch (Exception ex) { Log.Error("UpdateFillerStyle apply threw: " + ex); }
        }

        private static int SharedPointCount(System.Collections.Generic.HashSet<string> left, System.Collections.Generic.HashSet<string> right)
        {
            if (left == null || right == null) return 0;
            var count = 0;
            foreach (var key in left)
            {
                if (right.Contains(key)) count++;
            }
            return count;
        }

        private static void ApplyUpdateCrosswalkStyle(Marking marking, IMTActionCommand cmd)
        {
            if (!string.IsNullOrEmpty(cmd.CrosswalkXml))
            {
                ApplyCrosswalkXml(marking, cmd, "UpdateCrosswalkStyle");
                return;
            }

            if (!StyleSerializer.TryFromXml<BaseCrosswalkStyle>(cmd.StyleXml, out var style))
            { Log.Warn("UpdateCrosswalkStyle: failed to parse style XML"); return; }
            Log.Info("UpdateCrosswalkStyle parsed " + StyleDiagnostics.Describe(style));
            if (!PointResolver.TryResolveInternalPoint(marking, cmd.A, out var p1))
            { Log.Info("UpdateCrosswalkStyle: cannot resolve start point " + DescribePointRef(cmd.A)); return; }
            if (!PointResolver.TryResolveInternalPoint(marking, cmd.B, out var p2))
            { Log.Info("UpdateCrosswalkStyle: cannot resolve end point " + DescribePointRef(cmd.B)); return; }
            if (!marking.TryGetLine(new MarkingPointPair(p1, p2), out var line) || line == null)
            { Log.Info("UpdateCrosswalkStyle: underlying crosswalk line not present"); return; }
            var cwLine = line as MarkingCrosswalkLine;
            if (cwLine == null) { Log.Warn($"UpdateCrosswalkStyle: line is not a CrosswalkLine ({line.GetType().Name})"); return; }

            // Walk the marking's crosswalks to find the one whose CrosswalkLine matches
            MarkingCrosswalk match = null;
            foreach (var cw in marking.Crosswalks)
            {
                if (cw?.CrosswalkLine == cwLine) { match = cw; break; }
            }
            if (match == null) { Log.Info("UpdateCrosswalkStyle: no MarkingCrosswalk references this line"); return; }

            if (!TryResolveOptionalBorder(marking, cmd.HasRightBorder, cmd.RightBorderA, cmd.RightBorderB, "right", out var rightBorder))
                return;
            if (!TryResolveOptionalBorder(marking, cmd.HasLeftBorder, cmd.LeftBorderA, cmd.LeftBorderB, "left", out var leftBorder))
                return;

            try
            {
                match.RightBorder.Value = rightBorder;
                match.LeftBorder.Value = leftBorder;
                match.Style.Value = style;
                LogApplied(cmd);
            }
            catch (Exception ex) { Log.Error("UpdateCrosswalkStyle apply threw: " + ex); }
        }

        private static void ApplyUpdateCrosswalkState(Marking marking, IMTActionCommand cmd)
        {
            ApplyCrosswalkXml(marking, cmd, "UpdateCrosswalkState");
            LogApplied(cmd);
        }

        private static void ApplyCrosswalkXml(Marking marking, IMTActionCommand cmd, string context)
        {
            var styleXml = cmd.StyleXml;
            if (string.IsNullOrEmpty(styleXml))
            {
                var elem = StyleSerializer.LoadXml(cmd.CrosswalkXml);
                var styleElem = elem?.Element(IMT.Manager.Style.XmlName);
                styleXml = StyleSerializer.ToXml(styleElem);
            }

            if (!StyleSerializer.TryFromXml<BaseCrosswalkStyle>(styleXml, out var style))
            { Log.Warn(context + ": failed to parse crosswalk style XML"); return; }

            if (!PointResolver.TryResolveInternalPoint(marking, cmd.A, out var p1))
            { Log.Info(context + ": cannot resolve start point " + DescribePointRef(cmd.A)); return; }
            if (!PointResolver.TryResolveInternalPoint(marking, cmd.B, out var p2))
            { Log.Info(context + ": cannot resolve end point " + DescribePointRef(cmd.B)); return; }
            if (!marking.TryGetLine(new MarkingPointPair(p1, p2), out var line) || line == null)
            { Log.Info(context + ": underlying crosswalk line not present"); return; }
            var cwLine = line as MarkingCrosswalkLine;
            if (cwLine == null) { Log.Warn($"{context}: line is not a CrosswalkLine ({line.GetType().Name})"); return; }

            MarkingCrosswalk match = cwLine.Crosswalk;
            if (match == null)
            {
                foreach (var cw in marking.Crosswalks)
                {
                    if (cw?.CrosswalkLine == cwLine) { match = cw; break; }
                }
            }
            if (match == null) { Log.Info(context + ": no MarkingCrosswalk references this line"); return; }

            if (!TryResolveOptionalBorder(marking, cmd.HasRightBorder, cmd.RightBorderA, cmd.RightBorderB, "right", out var rightBorder))
                return;
            if (!TryResolveOptionalBorder(marking, cmd.HasLeftBorder, cmd.LeftBorderA, cmd.LeftBorderB, "left", out var leftBorder))
                return;

            try
            {
                match.RightBorder.Value = rightBorder;
                match.LeftBorder.Value = leftBorder;
                match.Style.Value = style;
            }
            catch (Exception ex) { Log.Error(context + " apply threw: " + ex); }
        }

        private static bool TryResolveOptionalBorder(Marking marking, bool hasBorder, PointRef a, PointRef b, string name, out MarkingRegularLine border)
        {
            border = null;
            if (!hasBorder) return true;
            if (!PointResolver.TryResolveInternalPoint(marking, a, out var p1))
            { Log.Info($"UpdateCrosswalkStyle: cannot resolve {name} border start {DescribePointRef(a)}"); return false; }
            if (!PointResolver.TryResolveInternalPoint(marking, b, out var p2))
            { Log.Info($"UpdateCrosswalkStyle: cannot resolve {name} border end {DescribePointRef(b)}"); return false; }
            if (!marking.TryGetLine(new MarkingPointPair(p1, p2), out var line) || line == null)
            { Log.Info($"UpdateCrosswalkStyle: {name} border line not present"); return false; }
            border = line as MarkingRegularLine;
            if (border == null)
            { Log.Warn($"UpdateCrosswalkStyle: {name} border is not regular line ({line.GetType().Name})"); return false; }
            return true;
        }

        private static void LogApplied(IMTActionCommand cmd)
        {
            Log.Info($"Applied remote {cmd.Type} on {cmd.Scope} {cmd.MarkingId}");
            ImtUiRefresh.Request(cmd.Scope, cmd.MarkingId);
        }

        private static string DescribePointRef(PointRef r)
        {
            return $"kind={r.Kind} entrance={r.EntranceId} index={r.Index}";
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ColossalFramework.UI;
using HarmonyLib;
using IMT.Manager;
using IMT.Tools;
using IMT.UI.Editors;
using ModsCommon.Utilities;
using UnityEngine;
using CSM.IMTSync.Commands;
using CSM.IMTSync.Services;
using Alignment = IMT.Manager.Alignment;

namespace CSM.IMTSync.Injections
{
    // Helper used by every "add line / remove line" patch to capture point identity uniformly.
    internal static class PatchHelpers
    {
        public static MarkingScope ScopeOf(Marking m) =>
            m is NodeMarking ? MarkingScope.Node :
            m is SegmentMarking ? MarkingScope.Segment :
            MarkingScope.Node;

        public static PointRef ToPointRef(MarkingPoint p, PointKind kind = PointKind.Entrance)
        {
            return new PointRef
            {
                MarkingId = p?.Enter?.Marking?.Id ?? 0,
                EntranceId = p?.Enter?.Id ?? 0,
                Index = p?.Index ?? 0,
                Kind = kind,
            };
        }
    }

    // ----- ClearMarking -----

    [HarmonyPatch(typeof(Marking), nameof(Marking.Clear))]
    public static class Marking_Clear_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Marking __instance)
        {
            if (CsmBridge.IsIgnoring() || __instance == null) return;
            var cmd = new IMTActionCommand
            {
                Type = IMTActionType.ClearMarking,
                Scope = PatchHelpers.ScopeOf(__instance),
                MarkingId = __instance.Id,
            };
            Log.Info($"Local Marking.Clear on {cmd.Scope} {__instance.Id} - broadcasting.");
            CsmBridge.SendToAll(cmd);
        }
    }

    // ----- ResetOffsets -----

    [HarmonyPatch(typeof(Marking), nameof(Marking.ResetOffsets))]
    public static class Marking_ResetOffsets_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Marking __instance)
        {
            if (CsmBridge.IsIgnoring() || __instance == null) return;
            var cmd = new IMTActionCommand
            {
                Type = IMTActionType.ResetOffsets,
                Scope = PatchHelpers.ScopeOf(__instance),
                MarkingId = __instance.Id,
            };
            Log.Info($"Local Marking.ResetOffsets on {cmd.Scope} {__instance.Id} - broadcasting.");
            CsmBridge.SendToAll(cmd);
        }
    }

    // ----- AddRegularLine (the line type the tooltip says "Select a point to create or delete") -----

    [HarmonyPatch(typeof(Marking), nameof(Marking.AddRegularLine))]
    public static class Marking_AddRegularLine_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Marking __instance, MarkingPointPair pointPair, RegularLineStyle style, Alignment alignment)
        {
            if (CsmBridge.IsIgnoring() || __instance == null || style == null) return;
            var styleXml = StyleSerializer.ToXml(style);
            if (styleXml == null) { Log.Warn("AddRegularLine: failed to serialize style; skipping broadcast."); return; }
            var cmd = new IMTActionCommand
            {
                Type = IMTActionType.AddRegularLine,
                Scope = PatchHelpers.ScopeOf(__instance),
                MarkingId = __instance.Id,
                A = PatchHelpers.ToPointRef(pointPair.First),
                B = PatchHelpers.ToPointRef(pointPair.Second),
                StyleXml = styleXml,
                Alignment = (byte)alignment,
            };
            Log.Info($"Local Marking.AddRegularLine on {cmd.Scope} {__instance.Id} - broadcasting.");
            CsmBridge.SendToAll(cmd);
        }
    }

    // ----- AddNormalLine -----

    [HarmonyPatch(typeof(Marking), nameof(Marking.AddNormalLine))]
    public static class Marking_AddNormalLine_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Marking __instance, MarkingPointPair pointPair, RegularLineStyle style, Alignment alignment)
        {
            if (CsmBridge.IsIgnoring() || __instance == null || style == null) return;
            var styleXml = StyleSerializer.ToXml(style);
            if (styleXml == null) return;
            var cmd = new IMTActionCommand
            {
                Type = IMTActionType.AddNormalLine,
                Scope = PatchHelpers.ScopeOf(__instance),
                MarkingId = __instance.Id,
                A = PatchHelpers.ToPointRef(pointPair.First),
                B = PatchHelpers.ToPointRef(pointPair.Second),
                StyleXml = styleXml,
                Alignment = (byte)alignment,
            };
            Log.Info($"Local Marking.AddNormalLine on {cmd.Scope} {__instance.Id} - broadcasting.");
            CsmBridge.SendToAll(cmd);
        }
    }

    // ----- AddStopLine (StopLineStyle, no alignment) -----

    [HarmonyPatch(typeof(Marking), nameof(Marking.AddStopLine))]
    public static class Marking_AddStopLine_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Marking __instance, MarkingPointPair pointPair, StopLineStyle style)
        {
            if (CsmBridge.IsIgnoring() || __instance == null || style == null) return;
            var styleXml = StyleSerializer.ToXml(style);
            if (styleXml == null) return;
            var cmd = new IMTActionCommand
            {
                Type = IMTActionType.AddStopLine,
                Scope = PatchHelpers.ScopeOf(__instance),
                MarkingId = __instance.Id,
                A = PatchHelpers.ToPointRef(pointPair.First),
                B = PatchHelpers.ToPointRef(pointPair.Second),
                StyleXml = styleXml,
            };
            Log.Info($"Local Marking.AddStopLine on {cmd.Scope} {__instance.Id} - broadcasting.");
            CsmBridge.SendToAll(cmd);
        }
    }

    // ----- AddLaneLine (RegularLineStyle, no alignment) -----

    [HarmonyPatch(typeof(Marking), nameof(Marking.AddLaneLine))]
    public static class Marking_AddLaneLine_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Marking __instance, MarkingPointPair pointPair, RegularLineStyle style)
        {
            if (CsmBridge.IsIgnoring() || __instance == null || style == null) return;
            var styleXml = StyleSerializer.ToXml(style);
            if (styleXml == null) return;
            var cmd = new IMTActionCommand
            {
                Type = IMTActionType.AddLaneLine,
                Scope = PatchHelpers.ScopeOf(__instance),
                MarkingId = __instance.Id,
                A = PatchHelpers.ToPointRef(pointPair.First, PointKind.Lane),
                B = PatchHelpers.ToPointRef(pointPair.Second, PointKind.Lane),
                StyleXml = styleXml,
            };
            Log.Info($"Local Marking.AddLaneLine on {cmd.Scope} {__instance.Id} - broadcasting.");
            CsmBridge.SendToAll(cmd);
        }
    }

    // ----- AddCrosswalkLine (BaseCrosswalkStyle abstract) -----

    [HarmonyPatch(typeof(Marking), nameof(Marking.AddCrosswalkLine))]
    public static class Marking_AddCrosswalkLine_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Marking __instance, MarkingPointPair pointPair, BaseCrosswalkStyle style)
        {
            if (CsmBridge.IsIgnoring() || __instance == null || style == null) return;
            var styleXml = StyleSerializer.ToXml(style);
            if (styleXml == null) return;
            var cmd = new IMTActionCommand
            {
                Type = IMTActionType.AddCrosswalk,
                Scope = PatchHelpers.ScopeOf(__instance),
                MarkingId = __instance.Id,
                A = PatchHelpers.ToPointRef(pointPair.First, PointKind.Crosswalk),
                B = PatchHelpers.ToPointRef(pointPair.Second, PointKind.Crosswalk),
                StyleXml = styleXml,
            };
            Log.Info($"Local Marking.AddCrosswalkLine on {cmd.Scope} {__instance.Id} - broadcasting.");
            CsmBridge.SendToAll(cmd);
        }
    }

    // ----- AddFiller (FillerContour, BaseFillerStyle, ref List<MarkingRegularLine>) -----
    // Two overloads exist - we patch only the user-facing 3-arg one. Use TargetMethod() because
    // a List<>.MakeByRefType() call can't appear inside an attribute.
    // v2: now supports all three IFillerVertex subclasses (Enter / LineEnd / Intersect) via
    // FillerVertexConverter. The legacy Contour/ContourAlignments fields are no longer populated;
    // receiver decodes from Vertices (and falls back to legacy if Vertices is empty).

    [HarmonyPatch]
    public static class Marking_AddFiller_Patch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Marking), nameof(Marking.AddFiller),
                new System.Type[] { typeof(FillerContour), typeof(BaseFillerStyle), typeof(List<MarkingRegularLine>).MakeByRefType() });
        }

        [HarmonyPostfix]
        public static void Postfix(Marking __instance, FillerContour contour, BaseFillerStyle style)
        {
            if (CsmBridge.IsIgnoring() || __instance == null || contour == null || style == null) return;

            if (!FillerVertexConverter.TryFromContour(contour.RawVertices, out var verts))
            {
                Log.Warn("AddFiller: skipping broadcast (unsupported vertex in contour).");
                return;
            }
            if (verts.Length < 3) { Log.Warn("AddFiller: contour has fewer than 3 vertices; skipping."); return; }

            var styleXml = StyleSerializer.ToXml(style);
            if (styleXml == null) { Log.Warn("AddFiller: failed to serialize style."); return; }

            var cmd = new IMTActionCommand
            {
                Type = IMTActionType.AddFiller,
                Scope = PatchHelpers.ScopeOf(__instance),
                MarkingId = __instance.Id,
                Vertices = verts,
                StyleXml = styleXml,
            };
            Log.Info($"Local Marking.AddFiller on {cmd.Scope} {__instance.Id} ({verts.Length} verts) - broadcasting.");
            CsmBridge.SendToAll(cmd);
        }
    }

    // ----- RemoveFiller -----

    [HarmonyPatch(typeof(Marking), nameof(Marking.RemoveFiller))]
    public static class Marking_RemoveFiller_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Marking __instance, MarkingFiller filler)
        {
            if (CsmBridge.IsIgnoring() || __instance == null || filler == null || filler.Contour == null) return;

            if (!FillerVertexConverter.TryFromContour(filler.Contour.RawVertices, out var verts))
            {
                Log.Warn("RemoveFiller: skipping broadcast (unsupported vertex in contour).");
                return;
            }

            var cmd = new IMTActionCommand
            {
                Type = IMTActionType.RemoveFiller,
                Scope = PatchHelpers.ScopeOf(__instance),
                MarkingId = __instance.Id,
                Vertices = verts,
            };
            Log.Info($"Local Marking.RemoveFiller on {cmd.Scope} {__instance.Id} ({verts.Length} verts) - broadcasting.");
            CsmBridge.SendToAll(cmd);
        }
    }

    // ----- Presence render + cursor broadcast (Tier α + β v1) -----
    // Single Postfix on RenderOverlay so Harmony has one unambiguous patch on the inherited
    // ToolBase.RenderOverlay. Handles three things per frame: (1) claim ring around each claimed
    // intersection, (2) entrance-point dots at each claimed intersection (matches IMT's overlay
    // for the local user), (3) throttled cursor broadcast at 10 Hz for our own position.
    // Postfixes the IntersectionMarkingTool's per-frame RenderOverlay (inherited public virtual
    // from ModsCommon.BaseTool). Iterates PresenceStore snapshot and draws a colored ring for
    // each remote player's currently-selected intersection. World position is read directly from
    // NetManager (no IMT marking lookup needed on the receiver).

    [HarmonyPatch(typeof(IntersectionMarkingTool), nameof(IntersectionMarkingTool.RenderOverlay))]
    public static class IntersectionMarkingTool_RenderOverlay_Patch
    {
        private const float CursorThrottleSeconds = 0.1f; // 10 Hz
        private const float CursorMinMovementSq   = 0.01f; // (0.1 m)^2 threshold
        private static float _lastCursorTime;
        private static Vector3 _lastCursorPos;

        [HarmonyPostfix]
        public static void Postfix(RenderManager.CameraInfo cameraInfo)
        {
            if (cameraInfo == null) return;

            // (3) Throttled cursor broadcast - runs even when no claims exist
            BroadcastLocalCursor();

            var claims = PresenceStore.Snapshot();
            if (claims.Count == 0) return;

            foreach (var c in claims)
            {
                // Big claim ring at the intersection center
                if (PresenceStore.TryGetWorldPosition(c.Scope, c.MarkingId, out var pos))
                {
                    RenderExtension.RenderCircle(pos, new OverlayData(cameraInfo)
                    {
                        Color = c.RingColor,
                        Width = 24f,
                        RenderLimit = true,
                        AlphaBlend = true,
                    });
                }

                // Tier α: entrance-point dots - same colored circles IMT draws for the local user
                if (c.Marking != null)
                {
                    foreach (var entrance in c.Marking.Enters)
                    {
                        if (entrance == null) continue;
                        foreach (var ep in entrance.EnterPoints)
                        {
                            if (ep == null) continue;
                            RenderExtension.RenderCircle(ep.Position, new OverlayData(cameraInfo)
                            {
                                Color = ep.Color,    // native IMT color for this entrance point
                                Width = 1.6f,        // diameter in meters - matches IMT's marker scale
                                RenderLimit = true,
                                AlphaBlend = true,
                            });
                        }
                    }
                }
            }
        }

        private static void BroadcastLocalCursor()
        {
            if (CsmBridge.IsIgnoring()) return;
            var now = Time.realtimeSinceStartup;
            if (now - _lastCursorTime < CursorThrottleSeconds) return;

            Vector3 pos;
            try
            {
                var cam = Camera.main;
                if (cam == null) return;
                var ray = cam.ScreenPointToRay(Input.mousePosition);
                pos = ColossalFramework.Singleton<CSM.BaseGame.Injections.Tools.ToolSimulatorCursorManager>
                    .instance.DoRaycast(ray, 10000f);
            }
            catch { return; }

            if ((pos - _lastCursorPos).sqrMagnitude < CursorMinMovementSq) return;
            _lastCursorTime = now;
            _lastCursorPos = pos;

            string username = "";
            try { username = CSM.API.Chat.Instance?.GetCurrentUsername() ?? ""; }
            catch (System.Exception ex) { Log.Warn("Username fetch threw: " + ex.Message); }

            var cmd = new IMTActionCommand
            {
                Type = IMTActionType.CursorPresence,
                ClaimantName = username,
                CursorX = pos.x,
                CursorY = pos.y,
                CursorZ = pos.z,
            };
            CsmBridge.SendToAll(cmd);
        }
    }

    // ----- SelectIntersection (Tier 1 presence) -----
    // IntersectionMarkingTool.SetMarking(Marking) is the public setter that fires whenever the
    // user picks a new intersection to edit (or deselects, by passing null). We dedup on identity
    // change so repeated clicks on the same intersection don't spam the chat.

    [HarmonyPatch(typeof(IntersectionMarkingTool), nameof(IntersectionMarkingTool.SetMarking))]
    public static class IntersectionMarkingTool_SetMarking_Patch
    {
        private static ushort _lastId;
        private static MarkingScope _lastScope;

        [HarmonyPostfix]
        public static void Postfix(Marking marking)
        {
            if (CsmBridge.IsIgnoring()) return;
            ushort id = marking?.Id ?? 0;
            MarkingScope scope = marking == null ? MarkingScope.Node : PatchHelpers.ScopeOf(marking);
            if (id == _lastId && scope == _lastScope) return;
            _lastId = id;
            _lastScope = scope;

            string username = "";
            try { username = CSM.API.Chat.Instance?.GetCurrentUsername() ?? ""; }
            catch (System.Exception ex) { Log.Warn("Could not get current username: " + ex.Message); }

            var cmd = new IMTActionCommand
            {
                Type = IMTActionType.SelectIntersection,
                Scope = scope,
                MarkingId = id,
                ClaimantName = username,
            };
            Log.Info(id == 0
                ? "Local SelectIntersection: deselected - broadcasting."
                : $"Local SelectIntersection on {scope} {id} - broadcasting.");
            CsmBridge.SendToAll(cmd);
        }
    }

    // ----- SetPointOffset (drag commit) -----
    // DragPointToolMode.OnMouseDrag writes MarkingPoint.Offset.Value every frame during drag.
    // We only want one network event per gesture, so we postfix OnMouseUp - by then the Offset is
    // already at its final value (set by the last OnMouseDrag tick).

    [HarmonyPatch(typeof(DragPointToolMode), nameof(DragPointToolMode.OnMouseUp))]
    public static class DragPointToolMode_OnMouseUp_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(DragPointToolMode __instance)
        {
            if (CsmBridge.IsIgnoring() || __instance == null) return;
            var pt = __instance.DragPoint;
            if (pt == null) return;
            var marking = pt.Marking;
            if (marking == null) return;
            float value = pt.Offset.Value;
            var cmd = new IMTActionCommand
            {
                Type = IMTActionType.SetPointOffset,
                Scope = PatchHelpers.ScopeOf(marking),
                MarkingId = marking.Id,
                A = PatchHelpers.ToPointRef(pt),
                Offset = value,
            };
            Log.Info($"Local DragPoint commit on {cmd.Scope} {marking.Id} pt {pt.Index} -> {value:F3} - broadcasting.");
            CsmBridge.SendToAll(cmd);
        }
    }

    // ----- SetPointOffset (panel numeric input) -----
    // PointsEditor.OffsetChanged(float value) is invoked by the side-panel offset field. It is
    // a private instance method, so the patch attribute references it by string name.
    // EditObject is inherited public from Editor<TPanel, MarkingEnterPoint>.

    [HarmonyPatch(typeof(PointsEditor), "OffsetChanged")]
    public static class PointsEditor_OffsetChanged_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(PointsEditor __instance, float value)
        {
            if (CsmBridge.IsIgnoring() || __instance == null) return;
            var pt = __instance.EditObject;
            if (pt == null) return;
            var marking = pt.Marking;
            if (marking == null) return;
            var cmd = new IMTActionCommand
            {
                Type = IMTActionType.SetPointOffset,
                Scope = PatchHelpers.ScopeOf(marking),
                MarkingId = marking.Id,
                A = PatchHelpers.ToPointRef(pt),
                Offset = value,
            };
            Log.Info($"Local PointsEditor.OffsetChanged on {cmd.Scope} {marking.Id} pt {pt.Index} -> {value:F3} - broadcasting.");
            CsmBridge.SendToAll(cmd);
        }
    }

    // ----- UpdateFillerStyle -----
    // MarkingFiller.StyleChanged() is the universal "any style field changed" hook on a filler.
    // It's private and invoked from the Action wired to each PropertyValue<T> in the style, so we
    // catch color/width/pattern/etc. with a single patch instead of patching per-property lambdas.

    [HarmonyPatch(typeof(MarkingFiller), "StyleChanged")]
    public static class MarkingFiller_StyleChanged_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(MarkingFiller __instance)
        {
            if (CsmBridge.IsIgnoring() || __instance == null || __instance.Contour == null) return;
            var marking = __instance.Marking;
            if (marking == null) return;
            var style = __instance.Style?.Value;
            if (style == null) return;

            if (!FillerVertexConverter.TryFromContour(__instance.Contour.RawVertices, out var verts))
            {
                Log.Warn("UpdateFillerStyle: skipping broadcast (unsupported vertex in contour).");
                return;
            }
            var styleXml = StyleSerializer.ToXml(style);
            if (styleXml == null) { Log.Warn("UpdateFillerStyle: failed to serialize style."); return; }

            var cmd = new IMTActionCommand
            {
                Type = IMTActionType.UpdateFillerStyle,
                Scope = PatchHelpers.ScopeOf(marking),
                MarkingId = marking.Id,
                Vertices = verts,
                StyleXml = styleXml,
            };
            Log.Info($"Local Filler.StyleChanged on {cmd.Scope} {marking.Id} - broadcasting.");
            CsmBridge.SendToAll(cmd);
        }
    }

    // ----- UpdateCrosswalkStyle -----
    // MarkingCrosswalk.StyleChanged() - identity via the crosswalk's underlying CrosswalkLine.

    [HarmonyPatch(typeof(MarkingCrosswalk), "StyleChanged")]
    public static class MarkingCrosswalk_StyleChanged_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(MarkingCrosswalk __instance)
        {
            if (CsmBridge.IsIgnoring() || __instance == null || __instance.CrosswalkLine == null) return;
            var marking = __instance.Marking;
            if (marking == null) return;
            var style = __instance.Style?.Value;
            if (style == null) return;
            var line = __instance.CrosswalkLine;
            if (line.Start == null || line.End == null) return;

            var styleXml = StyleSerializer.ToXml(style);
            if (styleXml == null) { Log.Warn("UpdateCrosswalkStyle: failed to serialize style."); return; }

            var cmd = new IMTActionCommand
            {
                Type = IMTActionType.UpdateCrosswalkStyle,
                Scope = PatchHelpers.ScopeOf(marking),
                MarkingId = marking.Id,
                A = PatchHelpers.ToPointRef(line.Start, PointKind.Crosswalk),
                B = PatchHelpers.ToPointRef(line.End, PointKind.Crosswalk),
                StyleXml = styleXml,
            };
            Log.Info($"Local Crosswalk.StyleChanged on {cmd.Scope} {marking.Id} - broadcasting.");
            CsmBridge.SendToAll(cmd);
        }
    }

    // ----- UpdateLineStyle -----
    // MarkingLineRawRule.StyleChanged() - identity via the rule's parent line's point pair.
    // v1 simplification: only broadcast for the FIRST rule (most lines have exactly one). Multi-rule
    // lines need a richer wire format (rule index + per-rule edge SupportPoints).

    [HarmonyPatch(typeof(MarkingLineRawRule), "StyleChanged")]
    public static class MarkingLineRawRule_StyleChanged_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(MarkingLineRawRule __instance)
        {
            if (CsmBridge.IsIgnoring() || __instance == null) return;
            var line = __instance.Line as MarkingRegularLine;
            if (line == null) return;             // v1: regular lines only
            var marking = line.Marking;
            if (marking == null) return;
            if (line.RuleCount == 0) return;
            // First rule only - v1 single-rule simplification. Multi-rule lines need richer wire format.
            MarkingLineRawRule firstRule = null;
            foreach (var r in line.Rules) { firstRule = r; break; }
            if (!System.Object.ReferenceEquals(firstRule, __instance)) return;
            var style = __instance.Style?.Value;
            if (style == null) return;

            var styleXml = StyleSerializer.ToXml(style);
            if (styleXml == null) { Log.Warn("UpdateLineStyle: failed to serialize style."); return; }

            var cmd = new IMTActionCommand
            {
                Type = IMTActionType.UpdateLineStyle,
                Scope = PatchHelpers.ScopeOf(marking),
                MarkingId = marking.Id,
                A = PatchHelpers.ToPointRef(line.Start),
                B = PatchHelpers.ToPointRef(line.End),
                StyleXml = styleXml,
            };
            Log.Info($"Local LineRule.StyleChanged on {cmd.Scope} {marking.Id} - broadcasting.");
            CsmBridge.SendToAll(cmd);
        }
    }

    // ----- RemoveLine (handles all line subtypes via MarkingLine.Type) -----
    // Marking has TWO RemoveLine overloads: (MarkingLine) and (MarkingLine, bool recalculate).
    // The user-facing tool-mode delete calls the 1-arg version; explicit type list disambiguates.

    [HarmonyPatch(typeof(Marking), nameof(Marking.RemoveLine), new System.Type[] { typeof(MarkingLine) })]
    public static class Marking_RemoveLine_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Marking __instance, MarkingLine line)
        {
            if (CsmBridge.IsIgnoring() || __instance == null || line == null) return;
            // Map IMT's LineType enum to our IMTActionType. LineType values: Regular=256, Stop=512,
            // Crosswalk=2048, Lane=4096. Defaults to RemoveRegularLine if unrecognized.
            IMTActionType actionType;
            switch (line.Type)
            {
                case LineType.Stop:      actionType = IMTActionType.RemoveStopLine; break;
                case LineType.Crosswalk: actionType = IMTActionType.RemoveCrosswalk; break;
                case LineType.Lane:      actionType = IMTActionType.RemoveLaneLine; break;
                default:                 actionType = IMTActionType.RemoveRegularLine; break;
            }
            var cmd = new IMTActionCommand
            {
                Type = actionType,
                Scope = PatchHelpers.ScopeOf(__instance),
                MarkingId = __instance.Id,
                A = PatchHelpers.ToPointRef(line.Start),
                B = PatchHelpers.ToPointRef(line.End),
            };
            Log.Info($"Local Marking.RemoveLine ({line.Type}) on {cmd.Scope} {__instance.Id} - broadcasting.");
            CsmBridge.SendToAll(cmd);
        }
    }
}

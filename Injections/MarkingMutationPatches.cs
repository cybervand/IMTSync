using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ColossalFramework.Math;
using ColossalFramework.UI;
using HarmonyLib;
using IMT.Manager;
using IMT.Tools;
using IMT.UI.Editors;
using IMT.Utilities;
using ModsCommon;
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

        public static PointRef ToPointRef(MarkingPoint p, PointKind? kind = null)
        {
            return new PointRef
            {
                MarkingId = p?.Enter?.Marking?.Id ?? 0,
                EntranceId = p?.Enter?.Id ?? 0,
                Index = p?.Index ?? 0,
                Kind = kind ?? PointKindOf(p),
            };
        }

        private static PointKind PointKindOf(MarkingPoint p)
        {
            if (p == null) return PointKind.Entrance;
            switch (p.Type)
            {
                case MarkingPoint.PointType.Normal:
                    return PointKind.Normal;
                case MarkingPoint.PointType.Crosswalk:
                    return PointKind.Crosswalk;
                case MarkingPoint.PointType.Lane:
                    return PointKind.Lane;
                default:
                    return PointKind.Entrance;
            }
        }
    }

    internal static class TemplatePatchHelpers
    {
        public static void Send(IMTActionCommand cmd, string source)
        {
            if (CsmBridge.IsIgnoring()) return;
            if (cmd == null)
            {
                Log.Info($"{source} - no template command built.");
                return;
            }
            Log.Info($"{source} - broadcasting {cmd.Type} {cmd.TemplateId}.");
            CsmBridge.SendToAll(cmd);
        }
    }

    // ----- Saved style templates / intersection presets -----
    // These live in IMT's template managers, not in a Marking.ToXml snapshot. Patch the manager
    // layer so line-rule, filler, crosswalk template saves and preset saves all share one path.

    [HarmonyPatch]
    public static class StyleTemplateManager_AddNamedTemplateFromStyle_Patch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(StyleTemplateManager), "AddTemplate", new System.Type[] { typeof(string), typeof(Style), typeof(StyleTemplate).MakeByRefType() }) ??
            AccessTools.Method(typeof(TemplateManager<StyleTemplate, Style>), "AddTemplate", new System.Type[] { typeof(string), typeof(Style), typeof(StyleTemplate).MakeByRefType() });

        [HarmonyPostfix]
        public static void Postfix(bool __result, StyleTemplate template)
        {
            if (!__result) return;
            TemplatePatchHelpers.Send(TemplateSync.BuildUpsert(template), "StyleTemplate AddTemplate(name, style)");
        }
    }

    [HarmonyPatch]
    public static class StyleTemplateManager_TemplateChanged_Patch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(StyleTemplateManager), "TemplateChanged", new System.Type[] { typeof(StyleTemplate) }) ??
            AccessTools.Method(typeof(TemplateManager<StyleTemplate>), "TemplateChanged", new System.Type[] { typeof(StyleTemplate) });

        [HarmonyPostfix]
        public static void Postfix(StyleTemplate template)
        {
            TemplatePatchHelpers.Send(TemplateSync.BuildUpsert(template), "StyleTemplate TemplateChanged");
        }
    }

    [HarmonyPatch]
    public static class StyleTemplateManager_DeleteTemplate_Patch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(StyleTemplateManager), "DeleteTemplate", new System.Type[] { typeof(StyleTemplate) }) ??
            AccessTools.Method(typeof(TemplateManager<StyleTemplate>), "DeleteTemplate", new System.Type[] { typeof(StyleTemplate) });

        [HarmonyPrefix]
        public static void Prefix(StyleTemplate template)
        {
            TemplatePatchHelpers.Send(TemplateSync.BuildDelete(template), "StyleTemplate DeleteTemplate");
        }
    }

    [HarmonyPatch]
    public static class IntersectionTemplateManager_AddTemplateFromMarking_Patch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(IntersectionTemplateManager), "AddTemplate", new System.Type[] { typeof(Marking), typeof(ColossalFramework.Importers.Image), typeof(IntersectionTemplate).MakeByRefType() });

        [HarmonyPostfix]
        public static void Postfix(bool __result, ColossalFramework.Importers.Image image, IntersectionTemplate template)
        {
            if (!__result) return;
            TemplatePatchHelpers.Send(TemplateSync.BuildUpsert(template, image), "IntersectionTemplate AddTemplate(marking)");
        }
    }

    [HarmonyPatch]
    public static class IntersectionTemplateManager_TemplateChanged_Patch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(IntersectionTemplateManager), "TemplateChanged", new System.Type[] { typeof(IntersectionTemplate) }) ??
            AccessTools.Method(typeof(TemplateManager<IntersectionTemplate>), "TemplateChanged", new System.Type[] { typeof(IntersectionTemplate) });

        [HarmonyPostfix]
        public static void Postfix(IntersectionTemplate template)
        {
            TemplatePatchHelpers.Send(TemplateSync.BuildUpsert(template), "IntersectionTemplate TemplateChanged");
        }
    }

    [HarmonyPatch]
    public static class IntersectionTemplateManager_DeleteTemplate_Patch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(IntersectionTemplateManager), "DeleteTemplate", new System.Type[] { typeof(IntersectionTemplate) }) ??
            AccessTools.Method(typeof(TemplateManager<IntersectionTemplate>), "DeleteTemplate", new System.Type[] { typeof(IntersectionTemplate) });

        [HarmonyPrefix]
        public static void Prefix(IntersectionTemplate template)
        {
            TemplatePatchHelpers.Send(TemplateSync.BuildDelete(template), "IntersectionTemplate DeleteTemplate");
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

    [HarmonyPatch]
    public static class Marking_FromXml_Patch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Marking), nameof(Marking.FromXml), new System.Type[] { typeof(System.Version), typeof(System.Xml.Linq.XElement), typeof(ObjectsMap), typeof(bool) });

        [HarmonyPostfix]
        public static void Postfix(Marking __instance)
        {
            if (CsmBridge.IsIgnoring() || __instance == null) return;
            if (global::CSM.API.Commands.Command.SendToAll == null) return;

            var snap = MarkingSnapshotter.BuildSnapshot(__instance);
            if (snap == null) return;

            Log.Info($"Local Marking.FromXml on {snap.Scope} {snap.MarkingId} - broadcasting snapshot.");
            CsmBridge.SendToAll(snap);
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
        public static void Postfix(Marking __instance, MarkingPointPair pointPair, RegularLineStyle style, Alignment alignment, MarkingRegularLine __result)
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
                LineXml = StyleSerializer.ToXml(__result?.ToXml()),
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
        public static void Postfix(Marking __instance, MarkingPointPair pointPair, RegularLineStyle style, Alignment alignment, MarkingNormalLine __result)
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
                LineXml = StyleSerializer.ToXml(__result?.ToXml()),
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
        public static void Postfix(Marking __instance, MarkingPointPair pointPair, StopLineStyle style, MarkingStopLine __result)
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
                LineXml = StyleSerializer.ToXml(__result?.ToXml()),
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
        public static void Postfix(Marking __instance, MarkingPointPair pointPair, RegularLineStyle style, MarkingLaneLine __result)
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
                LineXml = StyleSerializer.ToXml(__result?.ToXml()),
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
        public static void Postfix(Marking __instance, MarkingPointPair pointPair, BaseCrosswalkStyle style, MarkingCrosswalkLine __result)
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
                LineXml = StyleSerializer.ToXml(__result?.ToXml()),
                CrosswalkXml = StyleSerializer.ToXml(__result?.Crosswalk?.ToXml()),
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
        public static void Postfix(Marking __instance, FillerContour contour, BaseFillerStyle style, MarkingFiller __result)
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
                FillerId = __result?.Id ?? 0,
                Vertices = verts,
                StyleXml = styleXml,
            };
            Log.Info($"Local Marking.AddFiller on {cmd.Scope} {__instance.Id} filler={cmd.FillerId} ({verts.Length} verts) - broadcasting.");
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
                FillerId = filler.Id,
                Vertices = verts,
            };
            Log.Info($"Local Marking.RemoveFiller on {cmd.Scope} {__instance.Id} filler={cmd.FillerId} ({verts.Length} verts) - broadcasting.");
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

    // Use TargetMethod() because RenderOverlay is inherited through a generic chain
    // (IntersectionMarkingTool : BaseTool<3> : BaseTool<2> with `public virtual void RenderOverlay(CameraInfo)`).
    // The simple `[HarmonyPatch(typeof(T), nameof(...))]` form sometimes fails at runtime with
    // "Undefined target method" on this chain, so walk the actual base types and return the
    // declared override Harmony needs to patch.

    [HarmonyPatch]
    public static class IntersectionMarkingTool_RenderOverlay_Patch
    {
        private const float CursorThrottleSeconds = 0.1f; // 10 Hz
        private const float CursorMinMovementSq   = 0.01f; // (0.1 m)^2 threshold
        private const float PreviewThrottleSeconds = 0.05f; // 20 Hz
        private static float _lastCursorTime;
        private static Vector3 _lastCursorPos;
        private static float _lastPreviewTime;
        private static bool _previewActive;
        private static ToolPreviewKind _lastLoggedPreviewKind;

        static MethodBase TargetMethod()
        {
            var args = new System.Type[] { typeof(RenderManager.CameraInfo) };
            for (var type = typeof(IntersectionMarkingTool); type != null; type = type.BaseType)
            {
                var method = type.GetMethod("RenderOverlay",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null, args, null);
                if (method != null)
                    return method;
            }

            Log.Error("Could not find IntersectionMarkingTool RenderOverlay(CameraInfo) target.");
            return null;
        }

        [HarmonyPostfix]
        public static void Postfix(RenderManager.CameraInfo cameraInfo)
        {
            if (cameraInfo == null) return;

            // (3) Throttled cursor broadcast - runs even when no claims exist
            BroadcastLocalCursor();
            BroadcastLocalToolPreview();

            // (4) Flush any debounced style edits whose elements have settled (10 Hz max effective rate)
            StyleEditDebouncer.FlushReady();
            SnapshotDeferral.FlushReady();
            TemplateSync.FlushDeferredPreviews();
            ImtUiRefresh.FlushReady();

            var claims = PresenceStore.Snapshot();
            foreach (var c in claims)
            {
                // Big claim ring at the intersection center
                RenderClaimFootprint(cameraInfo, c);

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

            RenderRemoteToolPreviews(cameraInfo);
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
                pos = ColossalFramework.Singleton<global::CSM.BaseGame.Injections.Tools.ToolSimulatorCursorManager>
                    .instance.DoRaycast(ray, 10000f);
            }
            catch { return; }

            if ((pos - _lastCursorPos).sqrMagnitude < CursorMinMovementSq) return;
            _lastCursorTime = now;
            _lastCursorPos = pos;

            string username = "";
            try { username = global::CSM.API.Chat.Instance?.GetCurrentUsername() ?? ""; }
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

        private static void BroadcastLocalToolPreview()
        {
            if (CsmBridge.IsIgnoring()) return;
            var now = Time.realtimeSinceStartup;
            if (now - _lastPreviewTime < PreviewThrottleSeconds) return;
            _lastPreviewTime = now;

            IMTActionCommand cmd = null;
            try { cmd = BuildLocalToolPreview(); }
            catch (System.Exception ex) { Log.Warn("BuildLocalToolPreview threw: " + ex.Message); }

            if (cmd == null)
            {
                SendPreviewClear();
                return;
            }

            if (!_previewActive || _lastLoggedPreviewKind != cmd.PreviewKind)
            {
                _lastLoggedPreviewKind = cmd.PreviewKind;
                Log.Info($"ToolPreview send {cmd.PreviewKind} on {cmd.Scope} {cmd.MarkingId} hasB={cmd.HasB} verts={(cmd.Vertices == null ? 0 : cmd.Vertices.Length)}");
            }
            _previewActive = true;
            CsmBridge.SendToAll(cmd);
        }

        private static IMTActionCommand BuildLocalToolPreview()
        {
            var tool = SingletonTool<IntersectionMarkingTool>.Instance;
            var marking = tool?.Marking;
            if (tool == null || marking == null) return null;

            var mode = tool.Mode;
            var scope = PatchHelpers.ScopeOf(marking);
            var cursor = GetImtCursorPosition(tool, marking);

            if (mode is MakeLineToolMode lineMode && lineMode.SelectPoint != null)
                return BuildPointPreview(ToolPreviewKind.Line, scope, marking, lineMode.SelectPoint, lineMode.HoverPoint, cursor);

            if (mode is MakeCrosswalkToolMode crosswalkMode && crosswalkMode.SelectPoint != null)
                return BuildPointPreview(ToolPreviewKind.Crosswalk, scope, marking, crosswalkMode.SelectPoint, crosswalkMode.HoverPoint, cursor);

            if (mode is MakeFillerToolMode fillerMode)
                return BuildFillerPreview(scope, marking, fillerMode, cursor);

            return null;
        }

        private static IMTActionCommand BuildPointPreview(ToolPreviewKind kind, MarkingScope scope, Marking marking, MarkingPoint select, MarkingPoint hover, Vector3 cursor)
        {
            string username = "";
            try { username = global::CSM.API.Chat.Instance?.GetCurrentUsername() ?? ""; } catch { }

            var cmd = new IMTActionCommand
            {
                Type = IMTActionType.ToolPreview,
                PreviewKind = kind,
                Scope = scope,
                MarkingId = marking.Id,
                ClaimantName = username,
                A = PatchHelpers.ToPointRef(select),
                CursorX = cursor.x,
                CursorY = cursor.y,
                CursorZ = cursor.z,
            };
            if (hover != null)
            {
                cmd.B = PatchHelpers.ToPointRef(hover);
                cmd.HasB = true;
            }
            return cmd;
        }

        private static IMTActionCommand BuildFillerPreview(MarkingScope scope, Marking marking, MakeFillerToolMode mode, Vector3 cursor)
        {
            var contourProp = AccessTools.Property(typeof(MakeFillerToolMode), "Contour");
            var contour = contourProp?.GetValue(mode, null) as FillerContour;
            if (contour == null || contour.IsEmpty) return null;
            if (!FillerVertexConverter.TryFromContour(contour.RawVertices, out var verts)) return null;

            string username = "";
            try { username = global::CSM.API.Chat.Instance?.GetCurrentUsername() ?? ""; } catch { }

            var cmd = new IMTActionCommand
            {
                Type = IMTActionType.ToolPreview,
                PreviewKind = ToolPreviewKind.Filler,
                Scope = scope,
                MarkingId = marking.Id,
                ClaimantName = username,
                Vertices = verts,
                CursorX = cursor.x,
                CursorY = cursor.y,
                CursorZ = cursor.z,
            };

            var selectorProp = AccessTools.Property(typeof(MakeFillerToolMode), "FillerPointsSelector");
            var selector = selectorProp?.GetValue(mode, null);
            var hover = selector?.GetType().GetProperty("HoverPoint")?.GetValue(selector, null) as IFillerVertex;
            if (hover != null && FillerVertexConverter.TryFromContour(new IFillerVertex[] { hover }, out var hoverRef) && hoverRef.Length > 0)
            {
                cmd.HoverVertex = hoverRef[0];
                cmd.HasHoverVertex = true;
            }
            return cmd;
        }

        private static Vector3 GetImtCursorPosition(IntersectionMarkingTool tool, Marking marking)
        {
            try { return tool.Ray.GetRayPosition(marking.Position.y, out _); }
            catch { return _lastCursorPos; }
        }

        private static void SendPreviewClear()
        {
            if (!_previewActive) return;
            _previewActive = false;
            _lastLoggedPreviewKind = ToolPreviewKind.None;
            Log.Info("ToolPreview send clear.");
            CsmBridge.SendToAll(new IMTActionCommand
            {
                Type = IMTActionType.ToolPreview,
                PreviewKind = ToolPreviewKind.None,
            });
        }

        public static void BroadcastLocalToolClosed(string reason)
        {
            if (CsmBridge.IsIgnoring()) return;

            MarkingScope scope = MarkingScope.Node;
            try
            {
                var marking = SingletonTool<IntersectionMarkingTool>.Instance?.Marking;
                if (marking != null)
                    scope = PatchHelpers.ScopeOf(marking);
            }
            catch { }

            _previewActive = false;
            _lastLoggedPreviewKind = ToolPreviewKind.None;
            Log.Info("Local IMT closed - clearing remote presence/previews" + (string.IsNullOrEmpty(reason) ? "." : $" ({reason})."));

            CsmBridge.SendToAll(new IMTActionCommand
            {
                Type = IMTActionType.ToolPreview,
                PreviewKind = ToolPreviewKind.None,
            });
            CsmBridge.SendToAll(new IMTActionCommand
            {
                Type = IMTActionType.SelectIntersection,
                Scope = scope,
                MarkingId = 0,
            });
        }

        public static void RenderRemotePresenceAndPreviews(RenderManager.CameraInfo cameraInfo)
        {
            var claims = PresenceStore.Snapshot();
            foreach (var c in claims)
            {
                RenderClaimFootprint(cameraInfo, c);

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
                                Color = ep.Color,
                                Width = 1.6f,
                                RenderLimit = true,
                                AlphaBlend = true,
                            });
                        }
                    }
                }
            }

            RenderRemoteToolPreviews(cameraInfo);
        }

        private static void RenderClaimFootprint(RenderManager.CameraInfo cameraInfo, PresenceStore.Claim claim)
        {
            if (claim.Marking != null && TryRenderMarkingContour(cameraInfo, claim.Marking, claim.RingColor))
                return;

            if (PresenceStore.TryGetWorldPosition(claim.Scope, claim.MarkingId, out var pos))
            {
                RenderExtension.RenderCircle(pos, new OverlayData(cameraInfo)
                {
                    Color = claim.RingColor,
                    Width = 24f,
                    RenderLimit = true,
                    AlphaBlend = true,
                });
            }
        }

        private static bool TryRenderMarkingContour(RenderManager.CameraInfo cameraInfo, Marking marking, Color color)
        {
            if (marking == null) return false;

            var outline = new OverlayData(cameraInfo)
            {
                Color = color,
                Width = 1.15f,
                RenderLimit = true,
                AlphaBlend = true,
            };

            var trajectories = new List<ITrajectory>();
            var renderedAny = false;
            try
            {
                foreach (var trajectory in marking.Contour)
                {
                    if (trajectory == null || trajectory.IsZero) continue;
                    trajectories.Add(trajectory);
                    renderedAny = true;
                }
            }
            catch (System.Exception ex)
            {
                Log.Warn("Render marking contour threw: " + ex.Message);
                return false;
            }

            if (!renderedAny) return false;
            for (var i = 0; i < trajectories.Count; i++)
                RenderTrajectory(trajectories[i], outline);
            return true;
        }

        private static void RenderTrajectory(ITrajectory trajectory, OverlayData data)
        {
            if (trajectory is StraightTrajectory straight)
            {
                straight.Render(data);
                return;
            }

            if (trajectory is BezierTrajectory bezier)
            {
                bezier.Render(data);
                return;
            }

            new Line3(trajectory.StartPosition, trajectory.EndPosition).GetBezier().RenderBezier(data);
        }

        private static void RenderRemoteToolPreviews(RenderManager.CameraInfo cameraInfo)
        {
            var previews = PresenceStore.PreviewSnapshot();
            foreach (var preview in previews)
            {
                if (preview.Marking == null) continue;
                switch (preview.Kind)
                {
                    case ToolPreviewKind.Line:
                        RenderPointPreview(cameraInfo, preview, false);
                        break;
                    case ToolPreviewKind.Crosswalk:
                        RenderPointPreview(cameraInfo, preview, true);
                        break;
                    case ToolPreviewKind.Filler:
                        RenderFillerPreview(cameraInfo, preview);
                        break;
                }
            }
        }

        private static void RenderPointPreview(RenderManager.CameraInfo cameraInfo, PresenceStore.Preview preview, bool crosswalk)
        {
            if (!PointResolver.TryResolveInternalPoint(preview.Marking, preview.A, out var start)) return;

            Vector3 endPos;
            if (preview.HasB)
            {
                if (!PointResolver.TryResolveInternalPoint(preview.Marking, preview.B, out var end)) return;
                endPos = end.MarkerPosition;
                end.Render(new OverlayData(cameraInfo) { Color = preview.Color, Width = crosswalk ? 0.5f : 0.53f, AlphaBlend = true });
            }
            else
            {
                endPos = preview.CursorPosition;
            }

            start.Render(new OverlayData(cameraInfo) { Color = preview.Color, Width = crosswalk ? 0.5f : 0.53f, AlphaBlend = true });

            var bezier = new Line3(start.MarkerPosition, endPos).GetBezier();
            var data = new OverlayData(cameraInfo)
            {
                Color = preview.Color,
                AlphaBlend = true,
                Width = crosswalk ? MarkingCrosswalkPoint.Shift * 2f : 0.8f,
                Cut = crosswalk,
            };
            bezier.RenderBezier(data);
        }

        private static void RenderFillerPreview(RenderManager.CameraInfo cameraInfo, PresenceStore.Preview preview)
        {
            if (!FillerVertexConverter.TryToVertices(preview.Marking, preview.Vertices, out var vertices)) return;

            var contour = new FillerContour(preview.Marking);
            foreach (var vertex in vertices)
                contour.Add(vertex);

            contour.Render(new OverlayData(cameraInfo) { Color = preview.Color, AlphaBlend = true });

            if (contour.Last == null) return;

            if (preview.HasHoverVertex && FillerVertexConverter.TryToVertex(preview.Marking, preview.HoverVertex, out var hover))
            {
                var part = contour.GetFillerLine(contour.Last, hover);
                if (part.GetTrajectory(out ITrajectory trajectory))
                    trajectory.Render(new OverlayData(cameraInfo) { Color = preview.Color, AlphaBlend = true });
            }
            else
            {
                var bezier = new Line3(contour.Last.Position, preview.CursorPosition).GetBezier();
                bezier.RenderBezier(new OverlayData(cameraInfo) { Color = preview.Color, AlphaBlend = true });
            }
        }
    }

    [HarmonyPatch(typeof(ToolManager), "EndOverlayImpl")]
    public static class ToolManager_EndOverlayImpl_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(RenderManager.CameraInfo cameraInfo)
        {
            if (cameraInfo == null) return;
            IntersectionMarkingTool_RenderOverlay_Patch.RenderRemotePresenceAndPreviews(cameraInfo);
        }
    }

    [HarmonyPatch]
    public static class IntersectionMarkingTool_OnDisable_Patch
    {
        static MethodBase TargetMethod()
        {
            for (var type = typeof(IntersectionMarkingTool); type != null; type = type.BaseType)
            {
                var method = type.GetMethod("OnDisable",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null, System.Type.EmptyTypes, null);
                if (method != null)
                    return method;
            }

            Log.Error("Could not find IntersectionMarkingTool OnDisable target.");
            return null;
        }

        [HarmonyPostfix]
        public static void Postfix(object __instance)
        {
            if (!(__instance is IntersectionMarkingTool)) return;
            IntersectionMarkingTool_RenderOverlay_Patch.BroadcastLocalToolClosed("OnDisable");
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
            try { username = global::CSM.API.Chat.Instance?.GetCurrentUsername() ?? ""; }
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

            // Mid-session state push (Bug 2 fix part 2): if we're the server and we just opened
            // an intersection in IMT, broadcast a fresh snapshot of its current state. This covers
            // the case where the snapshot at session-start didn't include this node because IMT
            // hadn't loaded it yet (lazy-load). Clients now get the host's saved-state markings
            // for any intersection the host visits. Idempotent: receivers replace local state via
            // FromXml — concurrent client edits to the same marking would lose, but for a fresh
            // intersection that's correct behavior.
            if (id != 0 && marking != null)
            {
                try
                {
                    var mm = global::CSM.Networking.MultiplayerManager.Instance;
                    if (mm != null && mm.CurrentRole == global::CSM.API.Commands.MultiplayerRole.Server)
                    {
                        if (PresenceStore.HasActiveConstruction(scope, id))
                        {
                            SnapshotDeferral.Request(scope, id, "selection while construction preview is active");
                            return;
                        }
                        var snap = MarkingSnapshotter.BuildSnapshot(marking);
                        if (snap != null)
                        {
                            Log.Info($"Server SetMarking {scope} {id} - broadcasting snapshot.");
                            CsmBridge.SendToAll(snap);
                        }
                    }
                }
                catch (System.Exception ex) { Log.Warn("Server snapshot-on-SetMarking threw: " + ex.Message); }
            }
        }
    }

    // ----- Template / paste application -----
    // IntersectionMarkingTool.ApplyIntersectionTemplate / PasteMarking / EditMarking each replace
    // the current marking's state by calling Marking.Clear + Marking.FromXml internally. Without
    // intervention that would trigger our per-element patches (AddLine, Marking.Update, etc.) for
    // each rebuilt line/filler/crosswalk, flooding the network. We wrap the apply in IgnoreHelper
    // so those inner patches short-circuit, then broadcast a single MarkingSnapshot at the end.
    //
    // Broadcast from whichever player applied the template. Preset apply is a user edit, so clients
    // need to send the resulting marking state to the host just like line/filler/crosswalk edits do.
    //
    // PasteMarking and EditMarking are private on IMT.Tools.IntersectionMarkingTool, so patched
    // via string name. ApplyIntersectionTemplate is public.

    internal static class TemplateApplyHelpers
    {
        public static void BroadcastSnapshot(IntersectionMarkingTool tool, string source)
        {
            if (tool == null) return;
            if (tool.Mode is BaseOrderToolMode)
            {
                Log.Info($"{source} entered order mode - final snapshot deferred until apply.");
                return;
            }

            BroadcastSnapshot(tool.Marking, source);
        }

        public static void BroadcastSnapshot(Marking marking, string source)
        {
            if (marking == null) return;
            try
            {
                var snap = MarkingSnapshotter.BuildSnapshot(marking);
                if (snap == null) return;
                Log.Info($"{source} on {PatchHelpers.ScopeOf(marking)} {marking.Id} - broadcasting snapshot.");
                CsmBridge.SendToAll(snap);
            }
            catch (System.Exception ex) { Log.Warn($"{source}: snapshot broadcast threw: {ex.Message}"); }
        }
    }

    [HarmonyPatch(typeof(IntersectionMarkingTool), nameof(IntersectionMarkingTool.ApplyIntersectionTemplate))]
    public static class IntersectionMarkingTool_ApplyIntersectionTemplate_Patch
    {
        [HarmonyPrefix]
        public static void Prefix() { global::CSM.API.Helpers.IgnoreHelper.Instance.StartIgnore(); }

        [HarmonyPostfix]
        public static void Postfix(IntersectionMarkingTool __instance)
        {
            try { TemplateApplyHelpers.BroadcastSnapshot(__instance, "ApplyIntersectionTemplate"); }
            finally { global::CSM.API.Helpers.IgnoreHelper.Instance.EndIgnore(); }
        }
    }

    [HarmonyPatch(typeof(IntersectionMarkingTool), "PasteMarking")]
    public static class IntersectionMarkingTool_PasteMarking_Patch
    {
        [HarmonyPrefix]
        public static void Prefix() { global::CSM.API.Helpers.IgnoreHelper.Instance.StartIgnore(); }

        [HarmonyPostfix]
        public static void Postfix(IntersectionMarkingTool __instance)
        {
            try { TemplateApplyHelpers.BroadcastSnapshot(__instance, "PasteMarking"); }
            finally { global::CSM.API.Helpers.IgnoreHelper.Instance.EndIgnore(); }
        }
    }

    [HarmonyPatch(typeof(IntersectionMarkingTool), "EditMarking")]
    public static class IntersectionMarkingTool_EditMarking_Patch
    {
        [HarmonyPrefix]
        public static void Prefix() { global::CSM.API.Helpers.IgnoreHelper.Instance.StartIgnore(); }

        [HarmonyPostfix]
        public static void Postfix(IntersectionMarkingTool __instance)
        {
            try { TemplateApplyHelpers.BroadcastSnapshot(__instance, "EditMarking"); }
            finally { global::CSM.API.Helpers.IgnoreHelper.Instance.EndIgnore(); }
        }
    }

    [HarmonyPatch]
    public static class BaseOrderToolMode_Paste_Patch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(BaseOrderToolMode), "Paste");

        [HarmonyPrefix]
        public static void Prefix(out System.IDisposable __state)
        {
            __state = CsmBridge.StartIgnore();
        }

        [HarmonyFinalizer]
        public static System.Exception Finalizer(System.Exception __exception, System.IDisposable __state)
        {
            __state?.Dispose();
            return __exception;
        }
    }

    [HarmonyPatch]
    public static class BaseEntersOrderToolMode_Exit_Patch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(BaseEntersOrderToolMode), "Exit");

        [HarmonyPrefix]
        public static void Prefix(out System.IDisposable __state)
        {
            __state = CsmBridge.StartIgnore();
        }

        [HarmonyPostfix]
        public static void Postfix(BaseEntersOrderToolMode __instance, bool revert, System.IDisposable __state)
        {
            try
            {
                if (!revert)
                    TemplateApplyHelpers.BroadcastSnapshot(__instance?.Marking, "OrderModeApply");
            }
            finally
            {
                __state?.Dispose();
            }
        }

        [HarmonyFinalizer]
        public static System.Exception Finalizer(System.Exception __exception, System.IDisposable __state)
        {
            __state?.Dispose();
            return __exception;
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

    // FillerChanged is the universal mutation hook on MarkingFiller. It fires on BOTH:
    //   (a) style-type changes (Stripe → Chevron etc.) — invoked by StyleChanged() at its end
    //   (b) per-property changes (color, width, dash) — invoked directly via Style.OnStyleChanged
    // Patching FillerChanged catches both paths. Earlier patches on StyleChanged missed (b) entirely.

    [HarmonyPatch(typeof(MarkingFiller), "FillerChanged")]
    public static class MarkingFiller_FillerChanged_Patch
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
                FillerId = __instance.Id,
                Vertices = verts,
                StyleXml = styleXml,
            };
            // Debounced: rapid slider drags coalesce into one broadcast after 100ms settle.
            var elementId = EditClock.ElementIdFor(cmd) ?? ("F:" + marking.Id);
            StyleEditDebouncer.MarkDirty(elementId, cmd);
        }
    }

    // ----- UpdateLineState -----
    // Marking.Update(MarkingLine, ...) is the common path for line-level settings (alignment,
    // clip sidewalk) and rule structure/style updates. Send the full IMT <L ...> state so
    // receivers replace the whole line instead of trying to mirror every individual control.

    [HarmonyPatch(typeof(Marking), nameof(Marking.Update), new System.Type[] { typeof(MarkingLine), typeof(bool), typeof(bool) })]
    public static class Marking_UpdateLine_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Marking __instance, MarkingLine line)
        {
            if (CsmBridge.IsIgnoring() || __instance == null || line == null) return;
            if (line is MarkingCrosswalkLine) return; // crosswalks have their own full-state packet
            if (line.Start == null || line.End == null) return;

            var lineXml = StyleSerializer.ToXml(line.ToXml());
            if (lineXml == null) { Log.Warn("UpdateLineState: failed to serialize line."); return; }

            var cmd = new IMTActionCommand
            {
                Type = IMTActionType.UpdateLineState,
                Scope = PatchHelpers.ScopeOf(__instance),
                MarkingId = __instance.Id,
                A = PatchHelpers.ToPointRef(line.Start),
                B = PatchHelpers.ToPointRef(line.End),
                LineXml = lineXml,
            };

            var elementId = EditClock.ElementIdFor(cmd) ?? ("L:" + __instance.Id);
            StyleEditDebouncer.MarkDirty(elementId, cmd);
        }
    }

    // ----- UpdateCrosswalkStyle -----
    // MarkingCrosswalk.CrosswalkChanged() is the universal hook (both type and property changes).
    // Identity via the crosswalk's underlying CrosswalkLine endpoints.

    [HarmonyPatch(typeof(MarkingCrosswalk), "CrosswalkChanged")]
    public static class MarkingCrosswalk_CrosswalkChanged_Patch
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
                Type = IMTActionType.UpdateCrosswalkState,
                Scope = PatchHelpers.ScopeOf(marking),
                MarkingId = marking.Id,
                A = PatchHelpers.ToPointRef(line.Start, PointKind.Crosswalk),
                B = PatchHelpers.ToPointRef(line.End, PointKind.Crosswalk),
                StyleXml = styleXml,
                CrosswalkXml = StyleSerializer.ToXml(__instance.ToXml()),
            };
            AddBorderRefs(__instance.RightBorder?.Value, true, cmd);
            AddBorderRefs(__instance.LeftBorder?.Value, false, cmd);
            // Debounced: rapid slider drags coalesce into one broadcast after 100ms settle.
            var elementId = EditClock.ElementIdFor(cmd) ?? ("CW:" + marking.Id);
            StyleEditDebouncer.MarkDirty(elementId, cmd);
        }

        private static void AddBorderRefs(MarkingRegularLine border, bool right, IMTActionCommand cmd)
        {
            if (border == null || border.Start == null || border.End == null) return;
            if (right)
            {
                cmd.HasRightBorder = true;
                cmd.RightBorderA = PatchHelpers.ToPointRef(border.Start);
                cmd.RightBorderB = PatchHelpers.ToPointRef(border.End);
            }
            else
            {
                cmd.HasLeftBorder = true;
                cmd.LeftBorderA = PatchHelpers.ToPointRef(border.Start);
                cmd.LeftBorderB = PatchHelpers.ToPointRef(border.End);
            }
        }
    }

    // ----- UpdateLineStyle -----
    // MarkingLineRawRule.StyleChanged() - identity via the rule's parent line's point pair.
    // v1 simplification: only broadcast for the FIRST rule (most lines have exactly one). Multi-rule
    // lines need a richer wire format (rule index + per-rule edge SupportPoints).

    // MarkingLinePart.RuleChanged is the universal hook (both type and property changes).
    // MarkingLineRawRule inherits it, so TargetMethod() points at the declaring base method and
    // the postfix filters for raw-rule instances.

    [HarmonyPatch]
    public static class MarkingLineRawRule_RuleChanged_Patch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(MarkingLinePart), "RuleChanged");
        }

        [HarmonyPostfix]
        public static void Postfix(MarkingLinePart __instance)
        {
            // Full line state is now sent by Marking_UpdateLine_Patch. Keeping this Harmony patch
            // inert avoids duplicate style-only packets while preserving the old receiver path for
            // compatibility with already-emitted UpdateLineStyle commands.
            return;
#pragma warning disable CS0162
            if (CsmBridge.IsIgnoring() || __instance == null) return;
            var rawRule = __instance as MarkingLineRawRule;
            if (rawRule == null) return;
            var line = rawRule.Line as MarkingRegularLine;
            if (line == null) return;             // v1: regular lines only
            var marking = line.Marking;
            if (marking == null) return;
            if (line.RuleCount == 0) return;
            var ruleIndex = -1;
            var currentIndex = 0;
            foreach (var r in line.Rules)
            {
                if (System.Object.ReferenceEquals(r, rawRule))
                {
                    ruleIndex = currentIndex;
                    break;
                }
                currentIndex++;
            }
            if (ruleIndex < 0) return;
            var style = rawRule.Style?.Value;
            if (style == null) return;

            var styleXml = StyleSerializer.ToXml(style);
            if (styleXml == null) { Log.Warn("UpdateLineStyle: failed to serialize style."); return; }
            Log.Info($"UpdateLineStyle send rule={ruleIndex} " + StyleDiagnostics.Describe(style));

            var cmd = new IMTActionCommand
            {
                Type = IMTActionType.UpdateLineStyle,
                Scope = PatchHelpers.ScopeOf(marking),
                MarkingId = marking.Id,
                A = PatchHelpers.ToPointRef(line.Start),
                B = PatchHelpers.ToPointRef(line.End),
                RuleIndex = ruleIndex,
                StyleXml = styleXml,
            };
            // Debounced: rapid slider drags coalesce into one broadcast after 100ms settle.
            var elementId = EditClock.ElementIdFor(cmd) ?? ("L:" + marking.Id);
            StyleEditDebouncer.MarkDirty(elementId, cmd);
#pragma warning restore CS0162
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

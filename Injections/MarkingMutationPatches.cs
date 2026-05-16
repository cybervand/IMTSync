using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using IMT.Manager;
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

            // We only sync contours composed entirely of EnterFillerVertex (user-clicked entrance
            // points). Contours including LineEndFillerVertex / IntersectFillerVertex are advanced
            // cases that need richer wire format - log + skip for v1.
            var enterVerts = new List<EnterFillerVertex>();
            foreach (var v in contour.RawVertices)
            {
                if (v is EnterFillerVertex efv) enterVerts.Add(efv);
                else
                {
                    Log.Warn($"AddFiller: skipping broadcast - contour contains non-EnterFillerVertex ({v?.GetType().Name}). v1 only supports entrance-point contours.");
                    return;
                }
            }
            if (enterVerts.Count < 3) { Log.Warn("AddFiller: contour has fewer than 3 vertices; skipping."); return; }

            var styleXml = StyleSerializer.ToXml(style);
            if (styleXml == null) { Log.Warn("AddFiller: failed to serialize style."); return; }

            var contourRefs = enterVerts.Select(v => PatchHelpers.ToPointRef(v.Point)).ToArray();
            var alignments = enterVerts.Select(v => (byte)v.Alignment).ToArray();

            var cmd = new IMTActionCommand
            {
                Type = IMTActionType.AddFiller,
                Scope = PatchHelpers.ScopeOf(__instance),
                MarkingId = __instance.Id,
                Contour = contourRefs,
                ContourAlignments = alignments,
                StyleXml = styleXml,
            };
            Log.Info($"Local Marking.AddFiller on {cmd.Scope} {__instance.Id} ({contourRefs.Length} verts) - broadcasting.");
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

            var enterVerts = new List<EnterFillerVertex>();
            foreach (var v in filler.Contour.RawVertices)
            {
                if (v is EnterFillerVertex efv) enterVerts.Add(efv);
                else
                {
                    Log.Warn($"RemoveFiller: skipping broadcast - non-EnterFillerVertex in contour ({v?.GetType().Name}).");
                    return;
                }
            }

            var contourRefs = enterVerts.Select(v => PatchHelpers.ToPointRef(v.Point)).ToArray();
            var alignments = enterVerts.Select(v => (byte)v.Alignment).ToArray();

            var cmd = new IMTActionCommand
            {
                Type = IMTActionType.RemoveFiller,
                Scope = PatchHelpers.ScopeOf(__instance),
                MarkingId = __instance.Id,
                Contour = contourRefs,
                ContourAlignments = alignments,
            };
            Log.Info($"Local Marking.RemoveFiller on {cmd.Scope} {__instance.Id} ({contourRefs.Length} verts) - broadcasting.");
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

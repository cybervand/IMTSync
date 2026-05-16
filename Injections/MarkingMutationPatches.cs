using HarmonyLib;
using IMT.Manager;
using CSM.IMTSync.Commands;
using CSM.IMTSync.Services;

namespace CSM.IMTSync.Injections
{
    /// <summary>
    /// Postfixes on IMT.Manager.Marking mutating methods. Each postfix:
    ///   1. Short-circuits if IgnoreHelper says we're applying a remote command (no rebroadcast).
    ///   2. Builds an IMTActionCommand describing the action.
    ///   3. Sends it via CsmBridge.SendToAll.
    /// </summary>
    [HarmonyPatch(typeof(Marking), nameof(Marking.Clear))]
    public static class Marking_Clear_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Marking __instance)
        {
            if (CsmBridge.IsIgnoring()) return;
            if (__instance == null) return;

            var scope = __instance is NodeMarking ? MarkingScope.Node
                      : __instance is SegmentMarking ? MarkingScope.Segment
                      : MarkingScope.Node;

            var cmd = new IMTActionCommand
            {
                Type = IMTActionType.ClearMarking,
                Scope = scope,
                MarkingId = __instance.Id,
            };

            Log.Info($"Local Marking.Clear on {scope} {__instance.Id} - broadcasting.");
            CsmBridge.SendToAll(cmd);
        }
    }

    /// <summary>
    /// Postfix on Marking.AddRegularLine. Sender extracts marking ID, the two endpoints,
    /// the style XML, and the alignment.
    ///
    /// Signature (verified via Cecil):
    ///   MarkingRegularLine AddRegularLine(MarkingPointPair pointPair, RegularLineStyle style, Alignment alignment)
    /// </summary>
    [HarmonyPatch(typeof(Marking), nameof(Marking.AddRegularLine))]
    public static class Marking_AddRegularLine_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Marking __instance, MarkingPointPair pointPair, RegularLineStyle style, Alignment alignment)
        {
            if (CsmBridge.IsIgnoring()) return;
            if (__instance == null || style == null) return;

            var scope = __instance is NodeMarking ? MarkingScope.Node
                      : __instance is SegmentMarking ? MarkingScope.Segment
                      : MarkingScope.Node;

            var styleXml = StyleSerializer.ToXml(style);
            if (styleXml == null) { Log.Warn("AddRegularLine: failed to serialize style; skipping broadcast."); return; }

            var cmd = new IMTActionCommand
            {
                Type = IMTActionType.AddRegularLine,
                Scope = scope,
                MarkingId = __instance.Id,
                A = ToPointRef(pointPair.First),
                B = ToPointRef(pointPair.Second),
                StyleXml = styleXml,
                Alignment = (byte)alignment,
            };

            Log.Info($"Local Marking.AddRegularLine on {scope} {__instance.Id} (entrances {cmd.A.EntranceId}/{cmd.A.Index} -> {cmd.B.EntranceId}/{cmd.B.Index}) - broadcasting.");
            CsmBridge.SendToAll(cmd);
        }

        private static PointRef ToPointRef(MarkingPoint p)
        {
            return new PointRef
            {
                MarkingId = p?.Enter?.Marking?.Id ?? 0,
                EntranceId = p?.Enter?.Id ?? 0,
                Index = p?.Index ?? 0,
                Kind = PointKind.Entrance,
            };
        }
    }
}

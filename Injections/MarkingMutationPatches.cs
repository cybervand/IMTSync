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
    ///
    /// Phase 1 only patches Clear(). Phase 2 adds AddRegularLine, AddNormalLine, AddStopLine,
    /// AddLaneLine, AddCrosswalkLine, AddFiller, RemoveLine, RemoveCrosswalk, RemoveFiller,
    /// ResetOffsets.
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
                      : MarkingScope.Node; // shouldn't happen - Marking is abstract

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
}

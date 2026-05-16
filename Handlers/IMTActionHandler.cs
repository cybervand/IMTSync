using System;
using CSM.API.Commands;
using CSM.IMTSync.Commands;
using CSM.IMTSync.Services;
using IMT.API;

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

            // Diagnostic - log EVERY incoming command before doing anything else, so two-PC tests
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

                        default:
                            Log.Warn($"Unhandled IMTActionType: {cmd.Type} (Phase 1 only handles ClearMarking)");
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
                        // No-op: this client doesn't have a marking on that node yet.
                        // Common in Phase 1 because we don't sync AddRegularLine etc., so the
                        // client can't have markings the host didn't share. Will become rare
                        // once Phase 2 ships the Add patches.
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
    }
}

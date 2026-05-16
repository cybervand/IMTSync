using System.Reflection;
using CSM.API;
using CSM.API.Commands;
using CSM.IMTSync.Commands;
using CSM.IMTSync.Services;
// Disambiguate from CSM.API.Log (also in scope via "using CSM.API;")
using Log = CSM.IMTSync.Services.Log;

namespace CSM.IMTSync.Mod
{
    /// <summary>
    /// CSM Connection class - discovered automatically by CSM via PluginManager scan.
    /// Lifecycle: RegisterHandlers is called when a map loads with this mod enabled;
    /// UnregisterHandlers when the map unloads.
    /// </summary>
    public class IMTSyncSupport : Connection
    {
        public IMTSyncSupport()
        {
            Name = "IMTSync";
            Enabled = true;
            ModClass = typeof(MyUserMod);
            CommandAssemblies.Add(Assembly.GetExecutingAssembly());
        }

        public override void RegisterHandlers()
        {
            Log.Info("Registering handlers (map load)...");
            Patcher.PatchAll();

            // Mid-session state push: if we're a client (not the server), ask the server to
            // send us all currently-loaded marking snapshots. The server's MarkingSnapshot
            // responses re-create the host's IMT state on our side via Marking.FromXml.
            // CSM has no OnClientConnect hook for extensions, so we piggyback on RegisterHandlers
            // which fires whenever the map loads with this mod enabled - effectively "I just joined".
            try
            {
                var mm = global::CSM.Networking.MultiplayerManager.Instance;
                if (mm != null && mm.CurrentRole == MultiplayerRole.Client)
                {
                    var cmd = new IMTActionCommand { Type = IMTActionType.SnapshotRequest };
                    Log.Info("Client RegisterHandlers - broadcasting SnapshotRequest.");
                    CsmBridge.SendToAll(cmd);
                }
                else
                {
                    Log.Info($"RegisterHandlers role={mm?.CurrentRole.ToString() ?? "(none)"} - no snapshot request needed.");
                }
            }
            catch (System.Exception ex) { Log.Warn("SnapshotRequest send threw: " + ex.Message); }
        }

        public override void UnregisterHandlers()
        {
            Log.Info("Unregistering handlers (map unload)...");
            Patcher.UnpatchAll();
        }
    }
}

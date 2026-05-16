using System.Reflection;
using CSM.API;
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
        }

        public override void UnregisterHandlers()
        {
            Log.Info("Unregistering handlers (map unload)...");
            Patcher.UnpatchAll();
        }
    }
}

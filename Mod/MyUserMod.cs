using ICities;
using CSM.IMTSync.Services;

namespace CSM.IMTSync.Mod
{
    public class MyUserMod : IUserMod
    {
        public string Name => ModMetadata.ModName;
        public string Description => ModMetadata.Description;

        public void OnEnabled()
        {
            Log.Info($"v{ModMetadata.Version} enabled.");
            LogOverlay.EnsureCreated();
        }

        public void OnDisabled()
        {
            Log.Info("disabled.");
            LogOverlay.Destroy();
        }
    }
}

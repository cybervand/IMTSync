using System;
using System.Linq;
using CitiesHarmony.API;
using HarmonyLib;
using CSM.IMTSync.Mod;
using CSM.IMTSync.Services;

namespace CSM.IMTSync
{
    /// <summary>
    /// Harmony entry. Discovers all [HarmonyPatch] classes in this assembly and applies them.
    /// Called by IMTSyncSupport.RegisterHandlers / UnregisterHandlers.
    /// </summary>
    public static class Patcher
    {
        private static bool _patched;

        public static void PatchAll()
        {
            if (_patched) { Log.Info("PatchAll skipped - already patched."); return; }

            // Guard: our patch classes reference IMT.Manager.Marking via typeof(...).
            // If the IntersectionMarkingTool assembly isn't loaded, Harmony's reflection over
            // our assembly will throw FileNotFoundException. Detect that case explicitly so
            // we go inert with a clear warning instead of breaking the whole mod.
            if (!IsImtLoaded())
            {
                Log.Warn("IntersectionMarkingTool assembly is not loaded - IMT-MP is inert. " +
                         "Subscribe to and enable Intersection Marking Tool " +
                         "(https://steamcommunity.com/sharedfiles/filedetails/?id=2140418403) " +
                         "to use multiplayer marking sync.");
                return;
            }

            try
            {
                HarmonyHelper.EnsureHarmonyInstalled();
                var harmony = new Harmony(ModMetadata.HarmonyId);
                harmony.PatchAll(typeof(Patcher).Assembly);
                _patched = true;
                Log.Info("Patched!");
            }
            catch (Exception ex)
            {
                Log.Error("PatchAll failed: " + ex);
            }
        }

        private static bool IsImtLoaded()
        {
            try
            {
                return AppDomain.CurrentDomain.GetAssemblies()
                    .Any(a => a.GetName().Name == "IntersectionMarkingTool");
            }
            catch { return false; }
        }

        public static void UnpatchAll()
        {
            if (!_patched) { Log.Info("UnpatchAll skipped - not patched."); return; }
            try
            {
                new Harmony(ModMetadata.HarmonyId).UnpatchAll(ModMetadata.HarmonyId);
                _patched = false;
                Log.Info("Unpatched.");
            }
            catch (Exception ex)
            {
                Log.Error("UnpatchAll failed: " + ex);
            }
        }
    }
}

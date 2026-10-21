using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Utils;

namespace EnigmasEnhancedForging
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    // Optional: loads Item Salvager first when both are installed, so the container buttons can be adjusted. Works without it.
    [BepInDependency("EnigmaPrime.EnigmasItemSalvager", BepInDependency.DependencyFlags.SoftDependency)]
    // Everyone on a server needs this mod, on the same major.minor version. Jotunn enforces it when players join.
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        // Must match the GUID that Enigma's Item Salvager checks for (MetalScrapHook.cs).
        public const string PluginGuid = "EnigmaPrime.EnigmasEnhancedForging";
        public const string PluginName = "Enigma's Enhanced Forging";
        public const string PluginVersion = "0.0.1";

        internal static ManualLogSource Log;
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            StationConfig.Bind(Config);
            TinScrap.Register();
            SilverScrap.Register();
            FlametalScrap.Register();

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            SalvagerCompat.Apply(_harmony);
            foreach (var m in _harmony.GetPatchedMethods())
                Log.LogInfo($"Patched: {m.DeclaringType?.Name}.{m.Name}");

            Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}

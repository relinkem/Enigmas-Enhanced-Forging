using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;

namespace EnigmasEnhancedForging
{
    // EP_FlametalScrap: a new scrap item with no vanilla equivalent. For now it is a clone of the vanilla Copper Scrap
    // (same model and icon); its own texture comes later. The Item Salvager looks for this exact prefab name.
    // It smelts to a Flametal bar in the Blast Furnace.
    internal static class FlametalScrap
    {
        public const string PrefabName = "EP_FlametalScrap";

        internal static void Register()
        {
            PrefabManager.OnVanillaPrefabsAvailable += Add;
        }

        private static void Add()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Add;
            try
            {
                var cfg = new ItemConfig
                {
                    Name = "Flametal Scrap",
                    Description = "Scrap flametal. Smelt it back into a Flametal bar.",
                    Enabled = false // no crafting recipe, it only comes from salvaging
                };
                ItemManager.Instance.AddItem(new CustomItem(PrefabName, "CopperScrap", cfg));

                ItemManager.Instance.AddItemConversion(new CustomItemConversion(new SmelterConversionConfig
                {
                    Station = "blastfurnace",
                    FromItem = PrefabName,
                    ToItem = "FlametalNew"
                }));
                Plugin.Log.LogInfo("Added " + PrefabName);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("Could not add " + PrefabName + ": " + e);
            }
        }
    }
}

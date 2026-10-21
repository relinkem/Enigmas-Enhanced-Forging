using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;

namespace EnigmasEnhancedForging
{
    // EP_TinScrap: a new scrap item with no vanilla equivalent. For now it is a clone of the vanilla Copper Scrap
    // (same model and icon). The Item Salvager looks for this exact prefab name. It smelts to a Tin bar in the Smelter.
    internal static class TinScrap
    {
        public const string PrefabName = "EP_TinScrap";

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
                    Name = "Tin Scrap",
                    Description = "Scrap tin. Smelt it back into a Tin bar.",
                    Enabled = false // no crafting recipe, it only comes from salvaging
                };
                ItemManager.Instance.AddItem(new CustomItem(PrefabName, "CopperScrap", cfg));

                ItemManager.Instance.AddItemConversion(new CustomItemConversion(new SmelterConversionConfig
                {
                    Station = "smelter",
                    FromItem = PrefabName,
                    ToItem = "Tin"
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

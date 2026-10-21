using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;

namespace EnigmasEnhancedForging
{
    // EP_SilverScrap: a new scrap item with no vanilla equivalent. For now it is a clone of the vanilla Copper Scrap
    // (same model and icon); its own texture comes later. The Item Salvager looks for this exact prefab name.
    // It smelts to a Silver bar in the Smelter.
    internal static class SilverScrap
    {
        public const string PrefabName = "EP_SilverScrap";

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
                    Name = "Silver Scrap",
                    Description = "Scrap silver. Smelt it back into a Silver bar.",
                    Enabled = false // no crafting recipe, it only comes from salvaging
                };
                ItemManager.Instance.AddItem(new CustomItem(PrefabName, "CopperScrap", cfg));

                ItemManager.Instance.AddItemConversion(new CustomItemConversion(new SmelterConversionConfig
                {
                    Station = "smelter",
                    FromItem = PrefabName,
                    ToItem = "Silver"
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

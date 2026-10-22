using HarmonyLib;

namespace EnigmasEnhancedForging
{
    // Bronze is made in the Smelter with this mod, so the vanilla Forge crafting recipes for it (2 Copper + 1 Tin, and the
    // 5 bar version) are removed from the object database. Recipes live in each client's own database, and this mod is
    // required on every client, so everyone ends up with the same recipe list.
    internal static class BronzeRecipe
    {
        internal static void Remove(ObjectDB db)
        {
            if (db == null || db.m_recipes == null) return;
            if (StationConfig.RemoveForgeBronzeRecipe == null || !StationConfig.RemoveForgeBronzeRecipe.Value) return;

            int removed = db.m_recipes.RemoveAll(r =>
                r != null && r.m_item != null && r.m_item.name == "Bronze" &&
                r.m_craftingStation != null && r.m_craftingStation.name == "forge");

            if (removed > 0) Plugin.Log.LogInfo("Removed " + removed + " Forge Bronze recipe(s); Bronze is made in the Smelter.");
        }
    }

    // Runs last, so recipes added by other mods during the same hook are seen too.
    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class BronzeRecipeAwakePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ObjectDB __instance) { BronzeRecipe.Remove(__instance); }
    }

    [HarmonyPatch(typeof(ObjectDB), "CopyOtherDB")]
    internal static class BronzeRecipeCopyPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ObjectDB __instance) { BronzeRecipe.Remove(__instance); }
    }
}

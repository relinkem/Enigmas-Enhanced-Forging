using System;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace EnigmasEnhancedForging
{
    // Fits the station windows to Enigma's Item Salvager's container buttons (Take, Stack, Sort, Drop).
    // Item Salvager is optional. When it is missing, or its code is not what this expects, nothing here runs and
    // the stations behave like vanilla containers.
    //   Take: the game's Take All button, which Item Salvager relabels "Take". Already limited by this mod's lock rules.
    //   Stack: the game's Stack All, which this mod already limits to what the station accepts.
    //   Sort and Drop: hidden on stations (the rows have fixed jobs). Item Salvager's container sort is also blocked there.
    internal static class SalvagerCompat
    {
        private const string SalvagerGuid = "EnigmaPrime.EnigmasItemSalvager";
        private static readonly FieldInfo CurrentContainerF = AccessTools.Field(typeof(InventoryGui), "m_currentContainer");
        private static FieldInfo _chestBtnF, _dropBtnF;

        // True once Item Salvager has been found and fitted. Take All then lives in its button column.
        internal static bool Active;

        // True while the open container window belongs to one of this mod's stations.
        internal static bool StationOpen()
        {
            Container c = OpenContainer();
            if (c == null) return false;
            Inventory inv = c.GetInventory();
            return inv != null && StationContainer.Registry.TryGetValue(inv, out _);
        }

        private static Container OpenContainer()
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || CurrentContainerF == null) return null;
            return CurrentContainerF.GetValue(gui) as Container;
        }

        internal static void Apply(Harmony harmony)
        {
            try
            {
                if (!Chainloader.PluginInfos.TryGetValue(SalvagerGuid, out BepInEx.PluginInfo info) || info.Instance == null)
                {
                    Plugin.Log.LogInfo("Item Salvager not found: station windows use the vanilla container buttons.");
                    return;
                }

                Type sort = info.Instance.GetType().Assembly.GetType("EnigmasItemSalvager.SortInventory");
                MethodInfo runSort = sort == null ? null : AccessTools.Method(sort, "RunSort", new[] { typeof(bool) });
                MethodInfo mirror = sort == null ? null : AccessTools.Method(sort, "MirrorChestButton", new[] { typeof(InventoryGui) });
                _chestBtnF = sort == null ? null : AccessTools.Field(sort, "_chestBtn");
                _dropBtnF = sort == null ? null : AccessTools.Field(sort, "_dropBtn");
                Type reserve = info.Instance.GetType().Assembly.GetType("EnigmasItemSalvager.HotbarReserve_MoveAll");
                MethodInfo salvagerTakeAll = reserve == null ? null : AccessTools.Method(reserve, "Prefix");
                if (runSort == null || mirror == null || _chestBtnF == null || _dropBtnF == null)
                {
                    Plugin.Log.LogWarning("Item Salvager found, but its container buttons are not what this mod expects. Station windows use the vanilla container buttons.");
                    return;
                }

                harmony.Patch(runSort, prefix: new HarmonyMethod(typeof(SalvagerCompat), nameof(SortPrefix)));
                harmony.Patch(mirror, postfix: new HarmonyMethod(typeof(SalvagerCompat), nameof(MirrorPostfix)));
                if (salvagerTakeAll != null)
                    harmony.Patch(salvagerTakeAll, prefix: new HarmonyMethod(typeof(SalvagerCompat), nameof(TakeAllPrefix)));
                else
                    Plugin.Log.LogWarning("Item Salvager's Take All was not found, so station Take rules may be bypassed by it.");
                Active = true;
                Plugin.Log.LogInfo("Item Salvager found: the Sort and Drop buttons are hidden on station windows.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not fit the station windows to Item Salvager, using vanilla buttons: " + e.Message);
            }
        }

        // Item Salvager's own Take All. For a station it steps aside (does nothing and tells the game not to run the
        // original either): the station's Take rules, in StationPatches, do the moving.
        private static bool TakeAllPrefix(Inventory fromInventory, ref bool __result)
        {
            if (fromInventory != null && StationContainer.Registry.TryGetValue(fromInventory, out _))
            {
                __result = false;
                return false;
            }
            return true;
        }

        // Item Salvager's container sort (the Sort button, and the tidy-up after Drop). Skipped for stations.
        private static bool SortPrefix(bool container)
        {
            return !(container && StationOpen());
        }

        // Sort and Drop are hidden by default and only shown while an ordinary container is open. The game forgets the
        // open container the moment a window starts to close, but the window is still on screen while it fades, so
        // nothing changes then: a station's buttons stay hidden until it is gone, and an ordinary chest's stay until
        // it is gone too. Once the window is fully closed they are hidden again, ready for the next one.
        // They are shrunk to nothing (no size means nothing to see or click).
        private static bool _hidden = true;

        private static void MirrorPostfix()
        {
            try
            {
                InventoryGui gui = InventoryGui.instance;
                Container c = OpenContainer();
                bool windowUp = gui != null && gui.m_container != null && gui.m_container.gameObject.activeInHierarchy;
                if (c != null) _hidden = StationOpen();
                else if (!windowUp) _hidden = true;
                SetHidden(_chestBtnF, _hidden);
                SetHidden(_dropBtnF, _hidden);
            }
            catch { }
        }

        private static void SetHidden(FieldInfo field, bool hide)
        {
            RectTransform rt = field == null ? null : field.GetValue(null) as RectTransform;
            if (rt == null) return;
            Vector3 scale = hide ? Vector3.zero : Vector3.one;
            if (rt.localScale != scale) rt.localScale = scale;
        }
    }
}

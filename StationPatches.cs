using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace EnigmasEnhancedForging
{
    internal static class StationPatches
    {
        // Inventory.AddItem(item, amount, x, y, skipValidPositionCheck) is private, so it is called through a delegate.
        private static readonly Func<Inventory, ItemDrop.ItemData, int, int, int, bool, bool> _addAt =
            AccessTools.MethodDelegate<Func<Inventory, ItemDrop.ItemData, int, int, int, bool, bool>>(
                AccessTools.Method(typeof(Inventory), "AddItem",
                    new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int), typeof(bool) }));

        internal static bool AddAt(Inventory inv, ItemDrop.ItemData item, int amount, int x, int y)
        {
            return _addAt(inv, item, amount, x, y, false);
        }
    }

    // Adds a vanilla Container and the station component to the smelter prefab, before anything is spawned from it.
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetSceneAwakePatch
    {
        private static void Postfix(ZNetScene __instance)
        {
            if (StationConfig.SmelterScreen == null || !StationConfig.SmelterScreen.Value) return;
            try
            {
                // Borrow the wooden chest's panel background so the screen looks like any other container.
                Sprite bkg = null;
                foreach (GameObject p in __instance.m_prefabs)
                {
                    if (p == null) continue;
                    Container c = p.GetComponent<Container>();
                    if (c != null && c.m_bkg != null) { bkg = c.m_bkg; break; }
                }

                foreach (GameObject p in __instance.m_prefabs)
                {
                    if (p == null || (p.name != "smelter" && p.name != "blastfurnace" && p.name != "charcoal_kiln" && p.name != "eitrrefinery")) continue;
                    if (p.GetComponent<Container>() != null) continue;

                    Container container = p.AddComponent<Container>();
                    Smelter vanilla = p.GetComponent<Smelter>();
                    container.m_name = vanilla != null && !string.IsNullOrEmpty(vanilla.m_name) ? vanilla.m_name : "$piece_smelter";
                    container.m_width = StationContainer.Width;
                    container.m_height = StationContainer.Height;
                    container.m_bkg = bkg;
                    container.m_privacy = Container.PrivacySetting.Public;
                    p.AddComponent<StationContainer>();
                    Plugin.Log.LogInfo("Station screen added to " + p.name);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Station screen setup failed: " + e);
            }
        }
    }

    // The vanilla smelting loop runs from the smelter's hidden counters. For a station smelter we run our own loop
    // from the container instead. Smelters without a station component (and any smelter that failed to set up) are untouched.
    [HarmonyPatch(typeof(Smelter), "UpdateSmelter")]
    internal static class SmelterUpdatePatch
    {
        private static bool Prefix(Smelter __instance)
        {
            StationContainer station = __instance.GetComponent<StationContainer>();
            if (station == null || !station.Ready) return true;
            station.Tick();
            return false;
        }
    }

    // Fire, animation and smoke follow our loop instead of the hidden counters.
    [HarmonyPatch(typeof(Smelter), nameof(Smelter.IsActive))]
    internal static class SmelterIsActivePatch
    {
        private static void Postfix(Smelter __instance, ref bool __result)
        {
            StationContainer station = __instance.GetComponent<StationContainer>();
            if (station == null || !station.Ready) return;
            // On while working and while the fire and embers are still fading out, so the model's fire object stays lit.
            __result = station.IsRunning() || station.HeatLevel() > 0f;
        }
    }

    // Too hot to touch: nothing can be taken out of a working (or still cooling) station. Each of these is a way the
    // game moves an item out of a container. Every one adds to the destination before removing from the source, so the
    // block has to be at the start of each, never at the removal, or the item would be copied.
    // Runs before every other mod's MoveAll patch (Item Salvager has its own Take All, which would otherwise empty the whole
    // station). The first patch that returns false skips the rest, so these rules decide first.
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveAll))]
    [HarmonyPriority(Priority.First)]
    internal static class HotMoveAllPatch
    {
        private static bool Prefix(Inventory __instance, Inventory fromInventory)
        {
            if (StationContainer.HotBlock(fromInventory, __instance)) return false;
            // Take All in a single-row box takes just that row; in the refinery's main window just the Output row.
            if (StationContainer.ViewMoveAll(fromInventory, __instance)) return false;
            // Any other station: everything comes out the ordinary way. Done here, not left to the game or to another
            // mod's Take All, so the rules above are never bypassed.
            return !StationContainer.MoveEverything(fromInventory, __instance);
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveItemToThis),
        new[] { typeof(Inventory), typeof(ItemDrop.ItemData) })]
    internal static class HotQuickMovePatch
    {
        private static bool Prefix(Inventory __instance, Inventory fromInventory, ItemDrop.ItemData item)
        {
            if (StationContainer.LockBlock(fromInventory, item)) return false;
            return !StationContainer.HotBlock(fromInventory, __instance);
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveItemToThis),
        new[] { typeof(Inventory), typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int) })]
    internal static class HotMoveToSlotPatch
    {
        private static bool Prefix(Inventory __instance, Inventory fromInventory, ItemDrop.ItemData item, ref bool __result)
        {
            if (!StationContainer.LockBlock(fromInventory, item) && !StationContainer.HotBlock(fromInventory, __instance)) return true;
            __result = false;
            return false;
        }
    }

    // Throwing an item out of the container onto the ground.
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.DropItem))]
    internal static class HotDropPatch
    {
        private static bool Prefix(Inventory inventory, ItemDrop.ItemData item, ref bool __result)
        {
            if (!StationContainer.LockBlock(inventory, item) && !StationContainer.HotBlock(inventory, null)) return true;
            __result = false;
            return false;
        }
    }

    // Row rules. They only apply to station inventories, never to the player's inventory or ordinary chests.
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.CanAddItem), new[] { typeof(ItemDrop.ItemData), typeof(int) })]
    internal static class CanAddItemPatch
    {
        private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, int stack, ref bool __result)
        {
            if (!StationContainer.Registry.TryGetValue(__instance, out StationContainer station)) return true;
            __result = station.FreeRoom(__instance, item) >= (stack <= 0 ? item.m_stack : stack);
            return false;
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(ItemDrop.ItemData) })]
    internal static class AddItemAutoPatch
    {
        private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
        {
            if (!StationContainer.Registry.TryGetValue(__instance, out StationContainer station)) return true;
            __result = station.AddToRow(__instance, item);
            return false;
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(ItemDrop.ItemData), typeof(Vector2i) })]
    internal static class AddItemPosPatch
    {
        private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
        {
            if (!StationContainer.Registry.TryGetValue(__instance, out StationContainer station)) return true;
            __result = station.AddToRow(__instance, item);
            return false;
        }
    }

    // Placing an item in a chosen slot (dragging, move all). Loading a saved container passes skipValidPositionCheck = true, so it is left alone.
    [HarmonyPatch(typeof(Inventory), "AddItem",
        new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int), typeof(bool) })]
    internal static class AddItemAtPatch
    {
        private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref int amount, int x, int y,
            bool skipValidPositionCheck, ref bool __result)
        {
            if (skipValidPositionCheck || StationContainer.MachineWrite) return true;
            if (!StationContainer.Registry.TryGetValue(__instance, out StationContainer station)) return true;

            int only = station.ViewRow();
            if (y != station.RowFor(item) || (only >= 0 && y != only) || station.MainLocked(y))
            {
                __result = false;
                return false;
            }

            int cap = station.CapFor(y, item.m_shared.m_maxStackSize);
            ItemDrop.ItemData at = __instance.GetItemAt(x, y);
            int room = cap - (at != null ? at.m_stack : 0);
            if (room <= 0)
            {
                __result = false;
                return false;
            }
            if (amount > room) amount = room;
            return true;
        }
    }

    // Vanilla swaps two different items when one is dropped on an occupied slot: it takes the dragged item out of its
    // source first, then tries to put the other item where it came from. A station can refuse that second step (wrong
    // row), which loses the dragged item. So swaps with a station are simply not allowed; nothing moves.
    [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.DropItem))]
    internal static class DropItemSwapPatch
    {
        private static bool Prefix(InventoryGrid __instance, Inventory fromInventory, ItemDrop.ItemData item,
            int amount, Vector2i pos, ref bool __result)
        {
            Inventory to = __instance.GetInventory();
            if (to == null || fromInventory == null) return true;
            if (!StationContainer.Registry.TryGetValue(to, out _) &&
                !StationContainer.Registry.TryGetValue(fromInventory, out _)) return true;

            ItemDrop.ItemData itemAt = to.GetItemAt(pos.x, pos.y);
            if (itemAt == null || itemAt == item) return true;

            bool swap = (itemAt.m_shared.m_name != item.m_shared.m_name
                         || (item.m_shared.m_maxQuality > 1 && itemAt.m_quality != item.m_quality)
                         || itemAt.m_shared.m_maxStackSize == 1)
                        && item.m_stack == amount;
            if (!swap) return true;

            __result = false;
            return false;
        }
    }

    // Place Stacks on a station: vanilla only moves items the container already holds. Here it moves everything the
    // station accepts (ore, scrap, bars into Input, coal into Coal) from the player's inventory.
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.StackAll))]
    internal static class StackAllPatch
    {
        private static bool Prefix(Inventory __instance, Inventory fromInventory, bool message, ref int __result)
        {
            if (!StationContainer.Registry.TryGetValue(__instance, out StationContainer station)) return true;
            if (Player.m_localPlayer == null) return true;

            int before = __instance.CountItems(null);
            foreach (ItemDrop.ItemData item in new System.Collections.Generic.List<ItemDrop.ItemData>(fromInventory.GetAllItems()))
            {
                int rowFor = station.RowFor(item);
                if (rowFor < 0 || Player.m_localPlayer.IsItemEquiped(item)) continue;
                int only = station.ViewRow();
                if (only >= 0 && rowFor != only) continue; // a single-row box only takes its own item
                if (station.MainLocked(rowFor)) continue;
                if (__instance.AddItem(item)) fromInventory.RemoveItem(item); // a partly added stack stays behind with what is left
            }
            int moved = __instance.CountItems(null) - before;
            if (message)
                Player.m_localPlayer.Message(MessageHud.MessageType.Center,
                    moved > 0 ? "$msg_stackall " + moved : "$msg_stackall_none");
            __result = moved;
            return false;
        }
    }

    // Notes where the game puts Take All, before any other mod moves it.
    [HarmonyPatch(typeof(InventoryGui), "Awake")]
    internal static class NoteTakeAllPatch
    {
        private static void Postfix(InventoryGui __instance)
        {
            StationScreenPatch.NoteTakeAllSpot(__instance);
        }
    }

    // Marks which station an overlay was built for.
    internal class OverlayOwner : MonoBehaviour
    {
        public StationContainer Station;
        public StationContainer.ViewMode View;
        public int Serial;
        public GameObject Dim; // dimming squares drawn over locked rows, removed with the overlay

        private void OnDestroy()
        {
            if (Dim != null) UnityEngine.Object.Destroy(Dim);
        }
    }

    // Remembers how lit the heat box currently looks, so it can fade in instead of jumping to full.
    internal class HeatView : MonoBehaviour
    {
        public float Glow;
        public float Flames;
    }

    // Row colours and flame pictures behind the station screen. Flat images behind the vanilla slots, clicks pass through.
    [HarmonyPatch(typeof(InventoryGui), "UpdateContainer")]
    internal static class StationScreenPatch
    {
        private const string OverlayName = "EP_StationRows";
        private static Sprite _flame;
        private static bool _flameSearched;

        private static void Postfix(InventoryGui __instance)
        {
            try
            {
                InventoryGrid grid = __instance.ContainerGrid;
                if (grid == null || grid.m_gridRoot == null) return;
                RectTransform root = grid.m_gridRoot;
                Transform existing = root.Find(OverlayName);

                Container current = Traverse.Create(__instance).Field("m_currentContainer").GetValue<Container>();
                bool isStation = current != null && current.GetComponent<StationContainer>() != null;

                if (!isStation)
                {
                    StationContainer.OpenStation = null;
                    StationContainer.OpenView = StationContainer.ViewMode.Main;
                    // A box window keeps its hidden slots hidden while it fades out after closing, so the other rows do not
                    // flash into view. They are put back when the next container (or full station window) opens.
                    if (current != null) ApplyView(root, -1);
                    if (existing != null) UnityEngine.Object.Destroy(existing.gameObject);
                    Transform stale = PowerButtonParent(__instance) != null ? PowerButtonParent(__instance).Find("EP_PowerButton") : null;
                    if (stale != null) UnityEngine.Object.Destroy(stale.gameObject);
                    return;
                }
                StationContainer station = current.GetComponent<StationContainer>();
                if (existing != null)
                {
                    // Built for another station (a different kind, or another one of the same kind), or a refinery input
                    // spot was just used: start over.
                    OverlayOwner owner = existing.GetComponent<OverlayOwner>();
                    bool newOpen = owner != null && StationContainer.PendingSerial != owner.Serial &&
                                   Time.time <= StationContainer.PendingUntil;
                    if (owner != null && owner.Station == station && !newOpen)
                    {
                        StationContainer.OpenStation = station;
                        StationContainer.OpenView = owner.View;
                        ApplyView(root, ViewRowOf(owner.View));
                        SetTitle(__instance, station, owner.View);
                        UpdateStatus(existing, station);
                        return;
                    }
                    UnityEngine.Object.DestroyImmediate(existing.gameObject);
                }

                InventoryElement[] elements = root.GetComponentsInChildren<InventoryElement>(true);
                if (elements.Length != StationContainer.Width * StationContainer.Height) return;

                // Put any moved or hidden slots back first, so the overlay is measured against the normal layout.
                ApplyView(root, -1);
                StationContainer.ViewMode view = station.IsRefinery && Time.time <= StationContainer.PendingUntil
                    ? StationContainer.PendingView : StationContainer.ViewMode.Main;
                Build(root, elements, __instance, station, view, StationContainer.PendingSerial);
                StationContainer.OpenStation = station;
                StationContainer.OpenView = view;
                ApplyView(root, ViewRowOf(view));
                SetTitle(__instance, station, view);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Station screen overlay: " + e.Message);
            }
        }

        private static int ViewRowOf(StationContainer.ViewMode view)
        {
            return view == StationContainer.ViewMode.Input ? StationContainer.RowInput
                 : view == StationContainer.ViewMode.Fuel ? StationContainer.RowFuel : -1;
        }

        private static void SetTitle(InventoryGui gui, StationContainer station, StationContainer.ViewMode view)
        {
            if (!station.IsRefinery || gui.m_containerName == null) return;
            if (view == StationContainer.ViewMode.Input) gui.m_containerName.text = "Eitr Refinery: Softtissue";
            else if (view == StationContainer.ViewMode.Fuel) gui.m_containerName.text = "Eitr Refinery: Sap";
        }

        private class PosBox { public Vector2 Pos; }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<InventoryElement, PosBox> _orig =
            new System.Runtime.CompilerServices.ConditionalWeakTable<InventoryElement, PosBox>();
        private static bool _viewApplied;

        // A single-row box shows just one row of the container: that row is moved up to the top and the other slots are
        // hidden and parked far off screen, so nothing can be dropped on them. row = -1 puts everything back.
        // Runs every frame after the game has laid the grid out.
        private static void ApplyView(RectTransform root, int row)
        {
            if (row < 0 && !_viewApplied) return;
            InventoryElement[] els = root.GetComponentsInChildren<InventoryElement>(true);
            if (els.Length != StationContainer.Width * StationContainer.Height) return;
            foreach (InventoryElement e in els)
            {
                if (_orig.TryGetValue(e, out _)) continue;
                _orig.Add(e, new PosBox { Pos = ((RectTransform)e.transform).anchoredPosition });
            }
            if (row < 0)
            {
                foreach (InventoryElement e in els)
                {
                    _orig.TryGetValue(e, out PosBox b);
                    ((RectTransform)e.transform).anchoredPosition = b.Pos;
                    if (!e.gameObject.activeSelf) e.gameObject.SetActive(true);
                }
                _viewApplied = false;
                return;
            }
            _viewApplied = true;
            foreach (InventoryElement e in els)
            {
                _orig.TryGetValue(e, out PosBox b);
                RectTransform rt = (RectTransform)e.transform;
                if (e.Position.y == row)
                {
                    InventoryElement top = At(els, e.Position.x, 0);
                    PosBox tb = null;
                    if (top != null) _orig.TryGetValue(top, out tb);
                    rt.anchoredPosition = tb != null ? tb.Pos : b.Pos;
                    if (!e.gameObject.activeSelf) e.gameObject.SetActive(true);
                }
                else
                {
                    rt.anchoredPosition = b.Pos + new Vector2(0f, -20000f);
                    if (e.gameObject.activeSelf) e.gameObject.SetActive(false);
                }
            }
        }

        private struct Pal { public Color Hot, F1, F2, F3, FuelTint, OutTint; }

        // Colours of the heat glow, flame layers and row tints. Orange for the forges; the Eitr Refinery is green and teal.
        private static Pal PaletteFor(StationContainer st, StationContainer.ViewMode view)
        {
            // The refinery's main window: a magenta and purple box with smoke over it. Its rows keep the green Sap tint.
            if (st != null && st.IsRefinery && view == StationContainer.ViewMode.Main)
                return new Pal
                {
                    Hot = new Color(0.62f, 0.12f, 0.80f, 0.32f),
                    F1 = new Color(0.90f, 0.40f, 0.95f),
                    F2 = new Color(0.55f, 0.25f, 0.90f),
                    F3 = new Color(1.00f, 0.65f, 1.00f),
                    FuelTint = new Color(0.25f, 0.85f, 0.40f, 0.08f),
                    OutTint = new Color(0.40f, 0.65f, 0.65f, 0.05f)
                };
            // The Softtissue box falls through to the plain orange-red heat box (no flames), a warning not to reach in.
            if (st != null && st.IsRefinery && view != StationContainer.ViewMode.Input)
                return new Pal
                {
                    Hot = new Color(0.10f, 0.78f, 0.55f, 0.27f),
                    F1 = new Color(0.60f, 1.00f, 0.75f),
                    F2 = new Color(0.15f, 0.90f, 0.65f),
                    F3 = new Color(0.75f, 1.00f, 0.90f),
                    FuelTint = new Color(0.25f, 0.85f, 0.40f, 0.08f),
                    OutTint = new Color(0.40f, 0.65f, 0.65f, 0.05f)
                };
            return new Pal
            {
                Hot = HeatHot,
                F1 = new Color(1f, 0.85f, 0.35f),
                F2 = new Color(1f, 0.65f, 0.2f),
                F3 = new Color(1f, 0.9f, 0.5f),
                FuelTint = new Color(1f, 0.82f, 0.15f, 0.065f),
                OutTint = new Color(0.45f, 0.65f, 0.85f, 0.045f)
            };
        }

        // Centre of the slot where (x, row) is drawn: in a single-row box the shown row sits at the top.
        private static Vector2 DispCenter(InventoryElement[] elements, int x, int row, int viewRow)
        {
            InventoryElement src = At(elements, x, viewRow >= 0 ? 0 : row);
            return src != null ? CenterOf((RectTransform)src.transform) : Vector2.zero;
        }

        // Where the game puts Take All, noted when the inventory window is created. Another mod (Item Salvager) may move
        // the Take All button later, so the Light / Douse button is placed from these notes, not from the live button.
        private static Transform _takeParent;
        private static Vector2 _takeAnchorMin, _takeAnchorMax, _takePivot, _takeSize, _takePos;
        private static Vector3 _takeScale = Vector3.one;

        internal static void NoteTakeAllSpot(InventoryGui gui)
        {
            if (gui == null || gui.m_takeAllButton == null) return;
            var trt = (RectTransform)gui.m_takeAllButton.transform;
            _takeParent = trt.parent;
            _takeAnchorMin = trt.anchorMin;
            _takeAnchorMax = trt.anchorMax;
            _takePivot = trt.pivot;
            _takeSize = trt.sizeDelta;
            _takeScale = trt.localScale;
            _takePos = trt.anchoredPosition;
        }

        private static Transform PowerButtonParent(InventoryGui gui)
        {
            if (_takeParent != null) return _takeParent;
            return gui != null && gui.m_takeAllButton != null ? gui.m_takeAllButton.transform.parent : null;
        }

        private static Vector2 CenterOf(RectTransform rt)
        {
            return rt.anchoredPosition + new Vector2((0.5f - rt.pivot.x) * rt.rect.width, (0.5f - rt.pivot.y) * rt.rect.height);
        }

        private static InventoryElement At(InventoryElement[] elements, int x, int y)
        {
            foreach (InventoryElement e in elements)
                if (e.Position.x == x && e.Position.y == y) return e;
            return null;
        }

        private static void UpdateStatus(Transform overlay, StationContainer station)
        {
            Transform t = overlay.Find("EP_Status");
            var text = t != null ? t.GetComponent<TMPro.TextMeshProUGUI>() : null;
            if (text != null) text.text = station != null ? station.StatusMessage() : "";
            OverlayOwner own = overlay.GetComponent<OverlayOwner>();
            StationContainer.ViewMode curView = own != null ? own.View : StationContainer.ViewMode.Main;
            Pal pal = PaletteFor(station, curView);
            bool smoke = station != null && station.IsRefinery && curView == StationContainer.ViewMode.Main;
            bool liquid = station != null && station.IsRefinery && curView == StationContainer.ViewMode.Fuel;

            // Take All is greyed out while the contents are too hot to take.
            if (station != null && InventoryGui.instance != null && InventoryGui.instance.m_takeAllButton != null)
                InventoryGui.instance.m_takeAllButton.interactable = !station.IsHot();

            // Light / Douse button label follows the station.
            Transform pbParent = PowerButtonParent(InventoryGui.instance);
            Transform pb = pbParent != null ? pbParent.Find("EP_PowerButton") : null;
            if (pb != null && station != null)
            {
                // The button is a copy of Take All, which may have been greyed out when the copy was made. Light / Douse
                // is always usable.
                Button pbBtn = pb.GetComponent<Button>();
                if (pbBtn != null && !pbBtn.interactable) pbBtn.interactable = true;
                string want = station.PowerOn ? "Douse" : "Light";
                var label = pb.GetComponentInChildren<TMPro.TMP_Text>(true);
                if (label != null) { if (label.text != want) label.text = want; }
                else
                {
                    var legacy = pb.GetComponentInChildren<Text>(true);
                    if (legacy != null && legacy.text != want) legacy.text = want;
                }
            }

            // Heat glow behind the slots: brighter while working, fading out over the cool-down.
            float heat = station != null ? station.HeatLevel() : 0f;
            // The refinery's Sap box is a liquid: its green glow and flames run all the time while there is Sap in it.
            if (station != null && station.IsRefinery && own != null && own.View == StationContainer.ViewMode.Fuel)
                heat = station.HasItemsInRow(StationContainer.RowFuel) ? 1f : 0f;
            foreach (Transform child in overlay)
            {
                if (child.name != "EP_Heat") continue;
                Image img = child.GetComponent<Image>();
                if (img == null) continue;
                // The box stays fully lit until the last quarter of the cool-down, then goes out: dark means safe to touch.
                float glowTarget = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(heat / 0.25f));
                float flameTarget = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((heat - 0.5f) / 0.4f));

                // Fade in when it lights up (glow in about 3 s, flames a little later, in about 4.5 s). Fading out is
                // already gradual, so it follows the targets straight down. A screen opened on a hot forge starts lit.
                HeatView view = child.GetComponent<HeatView>();
                if (view == null)
                {
                    view = child.gameObject.AddComponent<HeatView>();
                    view.Glow = glowTarget;
                    view.Flames = flameTarget;
                }
                float dt = Time.deltaTime;
                view.Glow = glowTarget > view.Glow ? Mathf.Min(glowTarget, view.Glow + dt / 3f) : glowTarget;
                view.Flames = flameTarget > view.Flames ? Mathf.Min(flameTarget, view.Flames + dt / 4.5f) : flameTarget;
                float glow = view.Glow;
                float shownFlames = view.Flames;
                if (curView == StationContainer.ViewMode.Input) shownFlames = 0f; // the Softtissue box has the red glow only
                img.color = Color.Lerp(HeatCold, pal.Hot, glow);

                // Flames die down first, leaving a plain orange ember glow while the station is still too hot.
                Transform f = child.Find("EP_Flames");
                var ri = f != null ? f.GetComponent<RawImage>() : null;
                if (ri != null)
                {
                    Vector2 size = ((RectTransform)child).sizeDelta;
                    if (liquid)
                    {
                        // Liquid: slow rippling light, drifting sideways and a little down.
                        ri.color = new Color(pal.F1.r, pal.F1.g, pal.F1.b, shownFlames * 0.15f);
                        ri.uvRect = new Rect(Time.time * 0.03f + Mathf.Sin(Time.time * 0.5f) * 0.03f, -Time.time * 0.02f, size.x / 130f, size.y / 130f);
                    }
                    else if (smoke)
                    {
                        // Soft clouds: big tiles, slow drift.
                        ri.color = new Color(pal.F1.r, pal.F1.g, pal.F1.b, shownFlames * 0.16f);
                        ri.uvRect = new Rect(Mathf.Sin(Time.time * 0.25f) * 0.05f, -Time.time * 0.035f, size.x / 260f, size.y / 260f);
                    }
                    else
                    {
                        ri.color = new Color(pal.F1.r, pal.F1.g, pal.F1.b, shownFlames * 0.09f);
                        const float tile = 150f; // pixels per repeat of the flame picture
                        ri.uvRect = new Rect(Mathf.Sin(Time.time * 0.7f) * 0.06f, -Time.time * 0.14f, size.x / tile, size.y / tile);
                    }
                }

                // Second flame layer behind the first: bigger, flipped sideways, slower and swaying the other way,
                // so the two drift through each other.
                Transform f2 = child.Find("EP_Flames2");
                var ri2 = f2 != null ? f2.GetComponent<RawImage>() : null;
                if (ri2 != null)
                {
                    Vector2 size = ((RectTransform)child).sizeDelta;
                    float tile2 = liquid ? 190f : smoke ? 340f : 210f;
                    ri2.color = new Color(pal.F2.r, pal.F2.g, pal.F2.b, shownFlames * (liquid ? 0.12f : smoke ? 0.13f : 0.09f));
                    float sp2 = (smoke || liquid) ? 0.4f : 1f;
                    ri2.uvRect = new Rect(0.5f + Mathf.Cos(Time.time * 0.45f * sp2) * 0.08f, -Time.time * 0.09f * sp2 + 0.3f, -size.x / tile2, size.y / tile2);
                }

                // Third layer: smaller tongues, rising and swaying faster than the other two.
                Transform f3 = child.Find("EP_Flames3");
                var ri3 = f3 != null ? f3.GetComponent<RawImage>() : null;
                if (ri3 != null)
                {
                    Vector2 size = ((RectTransform)child).sizeDelta;
                    float tile3 = liquid ? 85f : smoke ? 180f : 95f;
                    ri3.color = new Color(pal.F3.r, pal.F3.g, pal.F3.b, shownFlames * (liquid ? 0.10f : smoke ? 0.10f : 0.07f));
                    float sp3 = (smoke || liquid) ? 0.3f : 1f;
                    ri3.uvRect = new Rect(0.25f + Mathf.Sin(Time.time * 1.3f * sp3) * 0.07f, -Time.time * 0.26f * sp3 + 0.6f, size.x / tile3, size.y / tile3);
                }
            }
        }

        private static Texture2D _flameTex;

        // A tileable picture of soft vertical tongues, drawn once in code. White-yellow with varying transparency.
        private static Texture2D FlameTexture()
        {
            if (_flameTex != null) return _flameTex;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            const float tau = 6.2831853f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    // Wavy tongues: a stripe pattern bent sideways by slower waves, all with whole numbers of repeats so it tiles.
                    float bend = 0.18f * Mathf.Sin(tau * (2f * v + u)) + 0.10f * Mathf.Sin(tau * (3f * v - 2f * u));
                    float tongue = 0.5f + 0.5f * Mathf.Sin(tau * (3f * u + bend));
                    float rise = 0.55f + 0.45f * Mathf.Sin(tau * (2f * v - u));
                    float a = Mathf.Pow(tongue, 2.2f) * rise;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
                }
            }
            tex.Apply();
            _flameTex = tex;
            return tex;
        }

        private static Texture2D _liquidTex;

        // A tileable picture of rippling liquid: bright wavering lines like light through water, over a soft body.
        private static Texture2D LiquidTexture()
        {
            if (_liquidTex != null) return _liquidTex;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            const float tau = 6.2831853f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float w1 = Mathf.Sin(tau * (2f * u + 0.25f * Mathf.Sin(tau * (v + u))));
                    float w2 = Mathf.Sin(tau * (2f * v - u + 0.25f * Mathf.Sin(tau * (2f * u - v))));
                    float w3 = Mathf.Sin(tau * (u + v + 0.2f * Mathf.Sin(tau * 3f * v)));
                    float n = (w1 + w2 + w3) / 3f;
                    float ridge = Mathf.Pow(1f - Mathf.Abs(n), 6f); // thin bright wavering lines
                    float body = 0.5f + 0.5f * Mathf.Sin(tau * (u - 2f * v + 0.2f * w1));
                    float a = Mathf.Clamp01(0.25f * body + 0.9f * ridge);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            _liquidTex = tex;
            return tex;
        }

        private static Texture2D _smokeTex;

        // A tileable soft cloud picture (sum of a few periodic waves), white with varying transparency.
        private static Texture2D SmokeTexture()
        {
            if (_smokeTex != null) return _smokeTex;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            const float tau = 6.2831853f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float n = 0.5f * Mathf.Sin(tau * (u + 0.3f * Mathf.Sin(tau * v)))
                            + 0.3f * Mathf.Sin(tau * (2f * v - u + 0.2f * Mathf.Sin(tau * 2f * u)))
                            + 0.2f * Mathf.Sin(tau * (3f * u + 2f * v + 0.15f * Mathf.Sin(tau * 3f * v)));
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(n * 0.5f + 0.5f));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            }
            tex.Apply();
            _smokeTex = tex;
            return tex;
        }

        private static void Build(RectTransform root, InventoryElement[] elements, InventoryGui gui, StationContainer station,
            StationContainer.ViewMode view, int serial)
        {
            int viewRow = ViewRowOf(view);
            Pal pal = PaletteFor(station, view);
            if (view != StationContainer.ViewMode.Main && gui != null && PowerButtonParent(gui) != null)
            {
                // The box windows have no Light / Douse button; remove the one a full window may have left behind.
                Transform leftover = PowerButtonParent(gui).Find("EP_PowerButton");
                if (leftover != null) UnityEngine.Object.DestroyImmediate(leftover.gameObject);
            }
            var parent = new GameObject(OverlayName, typeof(RectTransform));
            var ownerTag = parent.AddComponent<OverlayOwner>();
            ownerTag.Station = station;
            ownerTag.View = view;
            ownerTag.Serial = serial;
            var prt = (RectTransform)parent.transform;
            prt.SetParent(root, false);
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;
            prt.SetAsFirstSibling();

            Sprite flame = FindFlame();
            bool smokeBox = station != null && station.IsRefinery && view == StationContainer.ViewMode.Main;
            bool liquidBox = station != null && station.IsRefinery && view == StationContainer.ViewMode.Fuel;
            Texture2D layerTex = smokeBox ? SmokeTexture() : liquidBox ? LiquidTexture() : FlameTexture();

            // Heat glow, built first so it sits under everything else: a red-orange square behind every slot, plus a
            // slightly bigger fainter one for the border. Starts invisible; UpdateStatus sets the strength.
            if (view != StationContainer.ViewMode.Input || (station != null && station.IsRefinery))
            {
                float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                foreach (InventoryElement e0 in elements)
                {
                    if (viewRow >= 0 && e0.Position.y != viewRow) continue;
                    RectTransform r0 = (RectTransform)e0.transform;
                    Vector2 c0 = DispCenter(elements, e0.Position.x, e0.Position.y, viewRow);
                    minX = Mathf.Min(minX, c0.x - r0.rect.width * 0.5f);
                    maxX = Mathf.Max(maxX, c0.x + r0.rect.width * 0.5f);
                    minY = Mathf.Min(minY, c0.y - r0.rect.height * 0.5f);
                    maxY = Mathf.Max(maxY, c0.y + r0.rect.height * 0.5f);
                }
                // Border as wide as the gap between two neighbouring slots.
                float gap = 8f;
                InventoryElement g0 = At(elements, 0, 0), g1 = At(elements, 1, 0);
                if (g0 != null && g1 != null)
                {
                    float d = Mathf.Abs(CenterOf((RectTransform)g1.transform).x - CenterOf((RectTransform)g0.transform).x)
                              - ((RectTransform)g0.transform).rect.width;
                    if (d > 1f) gap = d;
                }
                InventoryElement anchorEl = At(elements, 0, 0);
                if (anchorEl != null)
                {
                    Image box = NewImage(prt, "EP_Heat", (RectTransform)anchorEl.transform);
                    box.sprite = HeatSprite();
                    box.type = Image.Type.Sliced;
                    box.color = HeatCold;

                    // Flames: a scrolling wavy picture inside the box, clipped to the box's rounded shape.
                    var mask = box.gameObject.AddComponent<Mask>();
                    mask.showMaskGraphic = true;
                    var fgo2 = new GameObject("EP_Flames2", typeof(RectTransform)); // created first, so it sits behind EP_Flames
                    var frt3 = (RectTransform)fgo2.transform;
                    frt3.SetParent(box.rectTransform, false);
                    frt3.anchorMin = Vector2.zero;
                    frt3.anchorMax = Vector2.one;
                    frt3.offsetMin = Vector2.zero;
                    frt3.offsetMax = Vector2.zero;
                    var raw2 = fgo2.AddComponent<RawImage>();
                    raw2.texture = layerTex;
                    raw2.raycastTarget = false;
                    raw2.color = new Color(1f, 0.65f, 0.2f, 0f);

                    var fgo = new GameObject("EP_Flames", typeof(RectTransform));
                    var frt2 = (RectTransform)fgo.transform;
                    frt2.SetParent(box.rectTransform, false);
                    frt2.anchorMin = Vector2.zero;
                    frt2.anchorMax = Vector2.one;
                    frt2.offsetMin = Vector2.zero;
                    frt2.offsetMax = Vector2.zero;
                    var raw = fgo.AddComponent<RawImage>();
                    raw.texture = layerTex;
                    raw.raycastTarget = false;
                    raw.color = new Color(1f, 0.85f, 0.35f, 0f);

                    // Third layer on top: smaller flames that rise and sway faster.
                    var fgo3 = new GameObject("EP_Flames3", typeof(RectTransform));
                    var frt4 = (RectTransform)fgo3.transform;
                    frt4.SetParent(box.rectTransform, false);
                    frt4.anchorMin = Vector2.zero;
                    frt4.anchorMax = Vector2.one;
                    frt4.offsetMin = Vector2.zero;
                    frt4.offsetMax = Vector2.zero;
                    var raw3 = fgo3.AddComponent<RawImage>();
                    raw3.texture = layerTex;
                    raw3.raycastTarget = false;
                    raw3.color = new Color(1f, 0.9f, 0.5f, 0f);
                    box.rectTransform.anchoredPosition = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
                    box.rectTransform.sizeDelta = new Vector2(maxX - minX + gap * 2f, maxY - minY + gap * 2f);
                }
            }

            for (int row = 0; row < StationContainer.Height; row++)
            {
                if (viewRow >= 0 && row != viewRow) continue;
                int drow = viewRow >= 0 ? 0 : row; // a single-row box is drawn in the top row's place
                InventoryElement first = At(elements, 0, drow);
                InventoryElement last = At(elements, StationContainer.Width - 1, drow);
                if (first == null || last == null) continue;

                RectTransform frt = (RectTransform)first.transform;
                RectTransform lrt = (RectTransform)last.transform;
                Vector2 c1 = CenterOf(frt);
                Vector2 c2 = CenterOf(lrt);
                float w = frt.rect.width;
                float h = frt.rect.height;

                // Status line just above the Input row (empty unless the station is stuck).
                if (row == StationContainer.RowInput && view == StationContainer.ViewMode.Main)
                {
                    var go = new GameObject("EP_Status", typeof(RectTransform));
                    // Inactive while the text component is added and given the game's font, so TextMeshPro does not
                    // log "LiberationSans SDF font asset not found" when it wakes up with no font yet.
                    go.SetActive(false);
                    var srt = (RectTransform)go.transform;
                    srt.SetParent(prt, false);
                    srt.anchorMin = frt.anchorMin;
                    srt.anchorMax = frt.anchorMax;
                    srt.pivot = new Vector2(0.5f, 0f);
                    srt.anchoredPosition = new Vector2((c1.x + c2.x) * 0.5f, c1.y + h * 0.5f + 2f);
                    srt.sizeDelta = new Vector2(w * StationContainer.Width, 24f);
                    var tmp = go.AddComponent<TMPro.TextMeshProUGUI>();
                    if (gui != null && gui.m_containerName != null) tmp.font = gui.m_containerName.font;
                    tmp.fontSize = 16f;
                    tmp.alignment = TMPro.TextAlignmentOptions.Center;
                    tmp.color = new Color(1f, 0.6f, 0.25f, 1f);
                    tmp.raycastTarget = false;
                    tmp.enableWordWrapping = false;
                    tmp.text = "";
                    go.SetActive(true);

                    // Start / Stop button at the right end, above the status line: a copy of the Take All button so it
                    // looks like the game's own.
                    if (gui != null && gui.m_takeAllButton != null)
                    {
                        Transform old = PowerButtonParent(gui).Find("EP_PowerButton");
                        if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
                        GameObject clone = UnityEngine.Object.Instantiate(gui.m_takeAllButton.gameObject, PowerButtonParent(gui), false);
                        clone.name = "EP_PowerButton";
                        foreach (MonoBehaviour mb in clone.GetComponents<MonoBehaviour>())
                            if (mb != null && mb.GetType().Name == "UIGamePad") UnityEngine.Object.Destroy(mb);
                        var brt = (RectTransform)clone.transform;
                        var trt = (RectTransform)gui.m_takeAllButton.transform;
                        if (_takeParent != null)
                        {
                            // With Item Salvager, Take All moves to its button column, so Light / Douse takes the game's
                            // Take All spot on the left. Without it, it sits just below that spot as before.
                            brt.anchorMin = _takeAnchorMin;
                            brt.anchorMax = _takeAnchorMax;
                            brt.pivot = _takePivot;
                            brt.sizeDelta = _takeSize;
                            brt.localScale = _takeScale;
                            brt.anchoredPosition = SalvagerCompat.Active ? _takePos : _takePos - new Vector2(0f, _takeSize.y + 6f);
                        }
                        else
                        {
                            brt.anchorMin = trt.anchorMin;
                            brt.anchorMax = trt.anchorMax;
                            brt.pivot = trt.pivot;
                            brt.sizeDelta = trt.sizeDelta;
                            brt.localScale = trt.localScale;
                            brt.anchoredPosition = trt.anchoredPosition - new Vector2(0f, trt.rect.height + 6f);
                        }
                        Button btn = clone.GetComponent<Button>();
                        btn.onClick = new Button.ButtonClickedEvent();
                        btn.onClick.AddListener(() =>
                        {
                            Container cur = Traverse.Create(gui).Field("m_currentContainer").GetValue<Container>();
                            StationContainer st = cur != null ? cur.GetComponent<StationContainer>() : null;
                            if (st != null) st.TogglePower();
                        });
                    }
                }

                // Plain rows are left alone. Fuel rows (Coal in the smelter, Wood in the kiln) are a faint warm yellow with
                // fire pits, Output rows a faint cool grey-blue. One tint square per slot, the same size as the slot.
                StationContainer.RowKind kind = station.KindOf(row);
                Color tint;
                if (kind == StationContainer.RowKind.Fuel) tint = pal.FuelTint;
                else if (kind == StationContainer.RowKind.Output) tint = pal.OutTint;
                else continue;

                for (int x = 0; x < StationContainer.Width; x++)
                {
                    InventoryElement e = At(elements, x, drow);
                    if (e == null) continue;
                    Image img = NewImage(prt, "EP_RowTint", frt);
                    img.color = tint;
                    RectTransform irt = img.rectTransform;
                    irt.anchoredPosition = CenterOf((RectTransform)e.transform);
                    irt.sizeDelta = new Vector2(w, h);
                }

                if (kind == StationContainer.RowKind.Fuel && flame != null && !station.IsRefinery) // liquid, no fire pits
                {
                    for (int x = 0; x < StationContainer.Width; x++)
                    {
                        InventoryElement e = At(elements, x, drow);
                        if (e == null) continue;
                        Image f = NewImage(prt, "EP_Flame", frt);
                        f.sprite = flame;
                        f.preserveAspect = true;
                        f.color = new Color(1f, 1f, 1f, 0.22f);
                        RectTransform rt = f.rectTransform;
                        rt.anchoredPosition = CenterOf((RectTransform)e.transform);
                        rt.sizeDelta = new Vector2(w * 0.9f, h * 0.9f);
                    }
                }
            }

            // The refinery's main window only shows the Softtissue and Sap rows, so those two rows are dimmed to say
            // "look, don't touch". Drawn over the items (last in the grid), clicks pass through.
            if (station.IsRefinery && view == StationContainer.ViewMode.Main)
            {
                var dimGo = new GameObject("EP_Dim", typeof(RectTransform));
                var drt = (RectTransform)dimGo.transform;
                drt.SetParent(root, false);
                drt.anchorMin = Vector2.zero;
                drt.anchorMax = Vector2.one;
                drt.offsetMin = Vector2.zero;
                drt.offsetMax = Vector2.zero;
                drt.SetAsLastSibling();
                ownerTag.Dim = dimGo;
                for (int row = 0; row < StationContainer.Height; row++)
                {
                    if (row == StationContainer.RowOutput) continue;
                    for (int x = 0; x < StationContainer.Width; x++)
                    {
                        InventoryElement e = At(elements, x, row);
                        if (e == null) continue;
                        RectTransform ert = (RectTransform)e.transform;
                        Image d = NewImage(drt, "EP_DimSlot", ert);
                        d.color = new Color(0.04f, 0.04f, 0.05f, 0.5f);
                        d.rectTransform.anchoredPosition = CenterOf(ert);
                        d.rectTransform.sizeDelta = new Vector2(ert.rect.width, ert.rect.height);
                    }
                }
            }
        }

        private static Image NewImage(RectTransform parent, string name, RectTransform likeElement)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = likeElement.anchorMin;
            rt.anchorMax = likeElement.anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            Image img = go.AddComponent<Image>();
            img.raycastTarget = false;
            return img;
        }

        private static Sprite _heatSprite;
        // The box behind the slots: a faint dark grey when cold, red-orange when working, fading between over the cool-down.
        private static readonly Color HeatCold = new Color(0.08f, 0.08f, 0.08f, 0.38f);
        private static readonly Color HeatHot = new Color(1f, 0.36f, 0.08f, 0.27f);

        // A rounded box with a raised (embossed) rim, drawn once in code and stretched with nine-slice. Grey tones so the
        // colour set on the Image tints it: light rim on the top-left, dark rim on the bottom-right.
        private static Sprite HeatSprite()
        {
            if (_heatSprite != null) return _heatSprite;
            const int size = 64;
            const float radius = 14f;
            const float rim = 4f;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Distance inside the rounded rectangle (positive inside), using the corner circles.
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                    float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float dist = radius - Mathf.Sqrt(dx * dx + dy * dy);          // inside the corner arc
                    float edge = Mathf.Min(Mathf.Min(x + 0.5f, size - x - 0.5f), Mathf.Min(y + 0.5f, size - y - 0.5f));
                    float inside = (dx == 0f && dy == 0f) ? edge : Mathf.Min(dist, edge);
                    float alpha = Mathf.Clamp01(inside + 0.5f);                   // soft anti-aliased edge
                    float shade = 0.80f;
                    if (inside < rim)
                    {
                        // Which way the rim faces: up-left is lit, down-right is in shadow (y goes up in textures).
                        Vector2 n = new Vector2(dx, dy);
                        if (n.sqrMagnitude < 0.0001f)
                        {
                            float l = x + 0.5f, r = size - x - 0.5f, b = y + 0.5f, t = size - y - 0.5f;
                            float m = Mathf.Min(Mathf.Min(l, r), Mathf.Min(b, t));
                            n = m == l ? new Vector2(-1f, 0f) : m == r ? new Vector2(1f, 0f) : m == b ? new Vector2(0f, -1f) : new Vector2(0f, 1f);
                        }
                        n.Normalize();
                        float lit = Vector2.Dot(n, new Vector2(-0.7071f, 0.7071f)); // +1 faces the light (up-left)
                        float k = 1f - Mathf.Clamp01(inside / rim);
                        shade = Mathf.Lerp(0.80f, lit > 0f ? 1.0f : 0.45f, Mathf.Abs(lit) * k);
                    }
                    tex.SetPixel(x, y, new Color(shade, shade, shade, alpha));
                }
            }
            tex.Apply();
            _heatSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(16f, 16f, 16f, 16f));
            return _heatSprite;
        }

        private static Sprite FindFlame()
        {
            if (_flame != null || _flameSearched) return _flame;
            _flameSearched = true;
            foreach (Sprite s in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (s != null && s.name == "firepit") { _flame = s; break; }
            }
            if (_flame == null) Plugin.Log.LogWarning("Flame sprite 'firepit' not found, Coal row has no flame picture.");
            return _flame;
        }
    }
}

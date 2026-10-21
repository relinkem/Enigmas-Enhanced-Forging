using System.Collections.Generic;
using HarmonyLib;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace EnigmasEnhancedForging
{
    // Added to a station prefab next to a vanilla Container. The Container holds the items (vanilla grid, vanilla
    // buttons, vanilla saving). This component decides which row each item belongs in and opens the screen when the
    // player uses the station. Rows: 0 Input, 1 Coal, 2 Output (take only).
    internal class StationContainer : MonoBehaviour
    {
        public const int Width = 4;
        public const int Height = 3;
        public const int RowInput = 0;
        public const int RowFuel = 1;
        public const int RowOutput = 2;

        // Every station inventory, so the inventory patches can tell ours from the player's and from chests.
        internal static readonly ConditionalWeakTable<Inventory, StationContainer> Registry =
            new ConditionalWeakTable<Inventory, StationContainer>();

        // True only while the station itself is putting items into its own rows (conversion, and later the smelting
        // output). The row rules in StationPatches step aside for that; the player never gets this.
        internal static bool MachineWrite;

        private readonly HashSet<string> _inputs = new HashSet<string>();
        private string _fuelName = "";
        private Smelter _smelter;
        private Container _container;
        private Inventory _registered;
        private ZNetView _nview;
        private Traverse _updateRoof, _updateSmoke, _updateState, _haveRoof, _blockedSmoke;

        // Keys on the smelter's ZDO for the run in progress.
        private const string KeyItem = "ep_item";      // prefab name of the item being melted, "" when idle
        private const string KeyProgress = "ep_prog";  // seconds melted so far
        private const string KeyTime = "ep_time";      // world time of the last tick, for catching up after being away
        private const string KeyOn = "ep_on";          // 0 = switched off by the player (default on)
        private const string KeyActive = "ep_active";  // 1 while the smelter is running (drives fire and smoke)
        private const string KeyHotUntil = "ep_hot";   // world time until which the contents are too hot to take out

        // True once the station has everything it needs to run its own smelting loop.
        internal bool Ready { get; private set; }

        private void Start()
        {
            _smelter = GetComponent<Smelter>();
            _container = GetComponent<Container>();
            if (_smelter == null || _container == null) return;

            // Inputs come from the Smelter's own conversion list, so whatever the game (or another mod) lets it melt is accepted.
            foreach (Smelter.ItemConversion c in _smelter.m_conversion)
                if (c != null && c.m_from != null) _inputs.Add(c.m_from.gameObject.name);

            // Copper and Tin bars are accepted too: they go into the Bronze alloy (added in a later step).
            string prefab = PrefabName();
            if (prefab == "smelter")
            {
                _inputs.Add("Copper");
                _inputs.Add("Tin");
            }

            // The Charcoal Kiln burns wood into coal: no fuel row, and both lower rows are output.
            IsKiln = prefab == "charcoal_kiln";
            IsRefinery = prefab == "eitrrefinery";
            _fuelName = !IsKiln && _smelter.m_fuelItem != null ? _smelter.m_fuelItem.gameObject.name : "";

            _nview = GetComponent<ZNetView>();
            _updateRoof = Traverse.Create(_smelter).Method("UpdateRoof");
            _updateSmoke = Traverse.Create(_smelter).Method("UpdateSmoke");
            _updateState = Traverse.Create(_smelter).Method("UpdateState");
            _haveRoof = Traverse.Create(_smelter).Field("m_haveRoof");
            _blockedSmoke = Traverse.Create(_smelter).Field("m_blockedSmoke");
            Ready = _nview != null && (IsKiln || _fuelName.Length > 0);

            _registered = _container.GetInventory();
            if (_registered != null)
            {
                Registry.Remove(_registered);
                Registry.Add(_registered, this);
            }

            // Route the station's own switches to the screen. They replace the add ore, add coal and empty switches.
            if (IsRefinery)
            {
                // The Eitr Refinery keeps its two input spots: the top one opens the Softtissue box, the front one the Sap box.
                WireView(_smelter.m_addOreSwitch, ViewMode.Input, "Softtissue");
                WireView(_smelter.m_addWoodSwitch, ViewMode.Fuel, "Sap");
            }
            else
            {
                Wire(_smelter.m_addOreSwitch);
                Wire(_smelter.m_addWoodSwitch);
            }
            Wire(_smelter.m_emptyOreSwitch);

            // A smelter built before this mod (or before the screen was turned on) may hold ore, coal or finished bars in
            // the vanilla hidden counters. Move them into the container. Wait a moment so the container has loaded first.
            Invoke(nameof(MigrateLegacy), 0.5f);
            if (_modelLogged.Add(prefab)) { Plugin.Log.LogInfo("Station model log for " + prefab + ":"); LogModel(); }
        }

        private static readonly HashSet<string> _modelLogged = new HashSet<string>();

        // True for the Charcoal Kiln (wood in, coal out). The Smelter keeps the Input / Coal / Output layout.
        internal bool IsKiln { get; private set; }

        // True for the Eitr Refinery: Softtissue and Sap each have their own small box window (a view of one row of the
        // same container), and the main window is opened from the body of the refinery.
        internal bool IsRefinery { get; private set; }

        // Which window the player opened on a station. Main is the full 3-row screen; Input and Fuel are the refinery's
        // single-row boxes.
        internal enum ViewMode { Main, Input, Fuel }

        // Set by the input spot just before it opens the container, and picked up when the screen is built. Valid for a
        // moment only, so a stale value can never turn up on a later window.
        internal static ViewMode PendingView = ViewMode.Main;
        internal static int PendingSerial;
        internal static float PendingUntil;

        // The station and window the local player has open right now (null / Main when none).
        internal static StationContainer OpenStation;
        internal static ViewMode OpenView = ViewMode.Main;

        // The one row the open box window shows, or -1 when the full window is open (or this station is not open).
        internal int ViewRow()
        {
            if (OpenStation != this) return -1;
            return OpenView == ViewMode.Input ? RowInput : OpenView == ViewMode.Fuel ? RowFuel : -1;
        }

        // Most items one slot of a row can hold. The refinery's two boxes have their own limits.
        internal int CapFor(int row, int itemMaxStack)
        {
            if (IsRefinery && row == RowInput) return Capped(StationConfig.RefineryInputSlotMax.Value, itemMaxStack);
            if (IsRefinery && row == RowFuel) return Capped(StationConfig.RefineryFuelSlotMax.Value, itemMaxStack);
            return StationConfig.SlotCap(row, itemMaxStack);
        }

        private static int Capped(int cfg, int itemMaxStack)
        {
            return cfg > 0 ? Mathf.Min(cfg, itemMaxStack) : itemMaxStack;
        }

        // Makes a switch's click area bigger (by the factor, around its centre), so it is easy to hit and the station's
        // body behind it does not win the click.
        private static void EnlargeColliders(GameObject go, float factor)
        {
            if (go == null || factor <= 1.001f) return;
            try
            {
                foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
                {
                    if (c.isTrigger) continue;
                    var box = c as BoxCollider;
                    var sph = c as SphereCollider;
                    var cap = c as CapsuleCollider;
                    var mesh = c as MeshCollider;
                    if (box != null) box.size = box.size * factor;
                    else if (sph != null) sph.radius *= factor;
                    else if (cap != null) { cap.radius *= factor; cap.height *= factor; }
                    else if (mesh != null && mesh.sharedMesh != null)
                    {
                        Bounds b = mesh.sharedMesh.bounds;
                        var extra = c.gameObject.AddComponent<BoxCollider>();
                        extra.center = b.center;
                        extra.size = b.size * factor;
                    }
                    Plugin.Log.LogInfo("Softtissue spot: enlarged " + c.GetType().Name + " on " + c.gameObject.name + " x" + factor);
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Could not enlarge the Softtissue spot: " + e.Message);
            }
        }

        private void WireView(Switch sw, ViewMode view, string what)
        {
            if (sw == null) return;
            if (view == ViewMode.Input) EnlargeColliders(sw.gameObject, StationConfig.RefinerySoftSwitchSize.Value);
            sw.m_onUse = (caller, user, item) =>
            {
                if (item != null || _container == null) return false;
                PendingView = view;
                PendingSerial++;
                PendingUntil = Time.time + 1f;
                return _container.Interact(user, false, false);
            };
            sw.m_onHover = () => Localization.instance.Localize(
                (_smelter != null && !string.IsNullOrEmpty(_smelter.m_name) ? _smelter.m_name : "$piece_eitrrefinery") +
                " (" + what + ")\n[<color=yellow><b>$KEY_Use</b></color>] $piece_container_open");
        }

        // In the refinery's main window the Softtissue and Sap rows only show what is inside: nothing can be put into
        // them or taken out of them there (that is done in their own boxes). Only the Output row is handled by hand.
        internal bool MainLocked(int row)
        {
            return IsRefinery && OpenStation == this && OpenView == ViewMode.Main && row != RowOutput;
        }

        // True (and the player is told) when an item is being taken out of a locked row of the refinery's main window.
        internal static bool LockBlock(Inventory from, ItemDrop.ItemData item)
        {
            if (from == null || item == null || !Registry.TryGetValue(from, out StationContainer st)) return false;
            if (!st.MainLocked(item.m_gridPos.y)) return false;
            if (Player.m_localPlayer != null)
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, "Use the Softtissue and Sap spots to change these");
            return true;
        }

        // Take All inside a single-row box takes only that box's row. In the refinery's main window it takes only the
        // Output row. Anything else is left to the game.
        internal static bool ViewMoveAll(Inventory from, Inventory to)
        {
            if (from == null || to == null || !Registry.TryGetValue(from, out StationContainer st)) return false;
            if (Registry.TryGetValue(to, out _)) return false;
            int row = st.ViewRow();
            if (row < 0 && st.IsRefinery && OpenStation == st && OpenView == ViewMode.Main) row = RowOutput;
            if (row < 0) return false;
            // Added one by one with the normal AddItem (not into the spot the item had in the station), so other mods that
            // watch the player's inventory, like Item Salvager's hotbar reservation, still apply.
            foreach (ItemDrop.ItemData item in new List<ItemDrop.ItemData>(from.GetAllItems()))
                if (item.m_gridPos.y == row && to.AddItem(item)) from.RemoveItem(item);
            return true;
        }

        // Take All from a station when no row rule applies: every item goes to the player the ordinary way.
        internal static bool MoveEverything(Inventory from, Inventory to)
        {
            if (from == null || to == null || !Registry.TryGetValue(from, out _)) return false;
            if (Registry.TryGetValue(to, out _)) return false;
            foreach (ItemDrop.ItemData item in new List<ItemDrop.ItemData>(from.GetAllItems()))
                if (to.AddItem(item)) from.RemoveItem(item);
            return true;
        }

        internal enum RowKind { Plain, Fuel, Output }

        // What each row is for, used by the screen to colour it and by the station to find its output slots.
        internal RowKind KindOf(int row)
        {
            if (IsKiln) return row == RowInput ? RowKind.Fuel : RowKind.Output; // wood row looks like a fire pit
            return row == RowFuel ? RowKind.Fuel : row == RowOutput ? RowKind.Output : RowKind.Plain;
        }

        private int FirstOutputRow { get { return IsKiln ? 1 : RowOutput; } }

        // One-time look at the forge model for anything besides the fire object that could be lit (animators, glowing
        // materials), so a glow that does not fade with the rest can be found in the log.
        private void LogModel()
        {
            try
            {
                Smelter s = _smelter;
                Plugin.Log.LogInfo("Forge model: enabled=" + NameOf(s.m_enabledObject) + ", disabled=" + NameOf(s.m_disabledObject) +
                                   ", haveFuel=" + NameOf(s.m_haveFuelObject) + ", haveOre=" + NameOf(s.m_haveOreObject) +
                                   ", noOre=" + NameOf(s.m_noOreObject));
                if (s.m_animators != null)
                {
                    foreach (Animator a in s.m_animators)
                    {
                        if (a == null) continue;
                        var ps = new List<string>();
                        foreach (AnimatorControllerParameter p in a.parameters) ps.Add(p.name + ":" + p.type);
                        var clips = new List<string>();
                        if (a.runtimeAnimatorController != null)
                            foreach (AnimationClip c in a.runtimeAnimatorController.animationClips) clips.Add(c.name);
                        Plugin.Log.LogInfo("Forge model animator: " + PathOf(a.transform) + ", controller " +
                                           (a.runtimeAnimatorController != null ? a.runtimeAnimatorController.name : "none") +
                                           ", params [" + string.Join(", ", ps.ToArray()) + "], clips [" + string.Join(", ", clips.ToArray()) + "]");
                    }
                }
                foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
                {
                    if (r is ParticleSystemRenderer) continue;
                    foreach (Material m in r.sharedMaterials)
                    {
                        if (m == null) continue;
                        string emis = m.HasProperty("_EmissionColor")
                            ? "emission " + m.GetColor("_EmissionColor") + (m.IsKeywordEnabled("_EMISSION") ? " (on)" : " (off)")
                            : "no emission";
                        Plugin.Log.LogInfo("Forge model renderer: " + PathOf(r.transform) + ", material " + m.name + ", shader " + m.shader.name + ", " + emis);
                    }
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Forge model log failed: " + e.Message);
            }
        }

        private static string NameOf(GameObject go)
        {
            return go != null ? go.name : "none";
        }

        private string PathOf(Transform t)
        {
            string path = t.name;
            while (t.parent != null && t.parent != transform)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

        private void OnDestroy()
        {
            if (_registered != null) Registry.Remove(_registered);
        }

        private string PrefabName()
        {
            return gameObject.name.Replace("(Clone)", "").Trim();
        }

        private void Wire(Switch sw)
        {
            if (sw == null) return;
            sw.m_onUse = OnUse;
            sw.m_onHover = OnHover;
        }

        private bool OnUse(Switch caller, Humanoid user, ItemDrop.ItemData item)
        {
            if (item != null || _container == null) return false;
            return _container.Interact(user, false, false);
        }

        private string OnHover()
        {
            string title = _smelter != null && !string.IsNullOrEmpty(_smelter.m_name) ? _smelter.m_name : "$piece_smelter";
            return Localization.instance.Localize(title + "\n[<color=yellow><b>$KEY_Use</b></color>] $piece_container_open");
        }

        // One-time conversion of a vanilla smelter's contents into the container rows:
        // coal to the Coal row, queued ore / scrap / bars to the Input row, finished bars to the Output row.
        // The vanilla counters are cleared afterwards, so this does nothing on later loads.
        private void MigrateLegacy()
        {
            try
            {
                ZNetView nview = GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid() || !nview.IsOwner() || _container == null || _smelter == null) return;
                Inventory inv = _container.GetInventory();
                if (inv == null) return;
                ZDO zdo = nview.GetZDO();

                float fuel = zdo.GetFloat(ZDOVars.s_fuel);
                int coal = Mathf.FloorToInt(fuel);
                int queued = zdo.GetInt(ZDOVars.s_queued);
                int ready = zdo.GetInt(ZDOVars.s_spawnAmount);
                if (coal <= 0 && queued <= 0 && ready <= 0) return;

                // Coal
                if (coal > 0) Place(inv, _smelter.m_fuelItem, coal, false);

                // Queued ore, scrap and bars: count each kind, then place them
                var counts = new Dictionary<string, int>();
                for (int i = 0; i < queued; i++)
                {
                    string name = zdo.GetString("item" + i);
                    if (string.IsNullOrEmpty(name)) continue;
                    counts.TryGetValue(name, out int n);
                    counts[name] = n + 1;
                }
                foreach (KeyValuePair<string, int> kv in counts)
                {
                    Smelter.ItemConversion conv = FindConversion(kv.Key);
                    if (conv == null || conv.m_from == null)
                    {
                        Plugin.Log.LogWarning("Smelter conversion: unknown queued item " + kv.Key + " x" + kv.Value + ", skipped.");
                        continue;
                    }
                    Place(inv, conv.m_from, kv.Value, false);
                }

                // Finished bars waiting to be emptied
                if (ready > 0)
                {
                    Smelter.ItemConversion conv = FindConversion(zdo.GetString(ZDOVars.s_spawnOre));
                    if (conv != null && conv.m_to != null) Place(inv, conv.m_to, ready, true);
                    else Plugin.Log.LogWarning("Smelter conversion: unknown finished item, skipped.");
                }

                // Clear the vanilla counters (keep the part of a coal that is less than one)
                zdo.Set(ZDOVars.s_fuel, fuel - coal);
                for (int i = 0; i < queued; i++) zdo.Set("item" + i, "");
                zdo.Set(ZDOVars.s_queued, 0);
                zdo.Set(ZDOVars.s_bakeTimer, 0f);
                zdo.Set(ZDOVars.s_spawnOre, "");
                zdo.Set(ZDOVars.s_spawnAmount, 0);

                Traverse.Create(_container).Method("Save").GetValue();
                Plugin.Log.LogInfo("Converted a smelter's contents into the station screen: " + coal + " coal, " +
                                   queued + " queued, " + ready + " finished.");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("Smelter conversion failed: " + e);
            }
        }

        private Smelter.ItemConversion FindConversion(string fromName)
        {
            if (string.IsNullOrEmpty(fromName)) return null;
            foreach (Smelter.ItemConversion c in _smelter.m_conversion)
                if (c != null && c.m_from != null && c.m_from.gameObject.name == fromName) return c;
            return null;
        }

        // Puts count of an item into the right row. Whatever does not fit (a small slot cap, say) is dropped next to the
        // station rather than lost.
        private void Place(Inventory inv, ItemDrop prefab, int count, bool output)
        {
            if (prefab == null || count <= 0) return;
            ItemDrop.ItemData item = prefab.m_itemData.Clone();
            item.m_dropPrefab = prefab.gameObject;
            item.m_stack = count;
            if (output) AddToOutput(inv, item);
            else AddToRow(inv, item);
            if (item.m_stack > 0)
            {
                // Same spot the vanilla smelter puts its finished bars: its output point, out in front.
                Transform spot = _smelter.m_outputPoint;
                Vector3 pos = spot != null ? spot.position : transform.position + Vector3.up * 0.5f;
                Quaternion rot = spot != null ? spot.rotation : Quaternion.identity;
                Plugin.Log.LogInfo("Smelter conversion: no room for " + item.m_stack + " " + prefab.gameObject.name +
                                   ", dropped in front of the smelter.");
                ItemDrop.DropItem(item, 0, pos, rot);
            }
        }

        // The station putting its own products into the Output row (the player cannot do this). Merges into matching
        // stacks first, then empty slots, up to the Output slot cap.
        internal void AddToOutput(Inventory inv, ItemDrop.ItemData item)
        {
            MachineWrite = true;
            try
            {
                int cap = StationConfig.SlotCap(RowOutput, item.m_shared.m_maxStackSize);
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int row = FirstOutputRow; row <= RowOutput; row++)
                    {
                        for (int x = 0; x < Width && item.m_stack > 0; x++)
                        {
                            ItemDrop.ItemData at = inv.GetItemAt(x, row);
                            if (pass == 0 && (at == null || !at.IsSameType(item))) continue;
                            if (pass == 1 && at != null) continue;
                            int room = cap - (at != null ? at.m_stack : 0);
                            if (room <= 0) continue;
                            StationPatches.AddAt(inv, item, Mathf.Min(room, item.m_stack), x, row);
                        }
                    }
                }
            }
            finally
            {
                MachineWrite = false;
            }
        }

        // ---- Smelting (stage 2): ore and scrap in, bars out ------------------------------------------------------------

        // Lit by the player. A new station starts unlit and does nothing until Light is pressed.
        internal bool PowerOn => _nview != null && _nview.IsValid() && _nview.GetZDO().GetInt(KeyOn, 0) != 0;

        internal void TogglePower()
        {
            if (!Ready || _nview == null || !_nview.IsValid()) return;
            if (!_nview.IsOwner()) _nview.ClaimOwnership();
            ZDO zdo = _nview.GetZDO();
            if (!PowerOn)
            {
                // Light only takes if there is something to run. Otherwise it stays unlit and the status line says why.
                Inventory inv = _container != null ? _container.GetInventory() : null;
                if (inv == null || !StartNext(zdo, inv)) { _lightFailUntil = Time.time + 6f; return; }
                zdo.Set(KeyOn, 1);
                PlayAddEffects();
                return;
            }

            // Douse: stop at once. Nothing is spent until a run finishes, so the items simply stay where they are;
            // lighting it again starts the run's counter from zero. Cool-down starts now.
            bool wasWorking = zdo.GetInt(KeyActive) == 1 || zdo.GetString(KeyItem).Length > 0;
            zdo.Set(KeyOn, 0);
            zdo.Set(KeyItem, "");
            zdo.Set(KeyProgress, 0f);
            zdo.Set(KeyActive, 0);
            if (wasWorking)
                zdo.Set(KeyHotUntil, ZNet.instance.GetTime().Ticks + (long)(StationConfig.CoolDownSeconds.Value * System.TimeSpan.TicksPerSecond));
        }

        // The station's own "something was added" show, kept from vanilla but played when the player lights it instead of
        // on every add: the puffs and sounds of ore going in (smoke from the kiln, the floof of the smelter, the
        // refinery's green splash) and of fuel going in, plus the model's short add animation where it has one.
        private void PlayAddEffects()
        {
            try
            {
                PlayList(_smelter.m_oreAddedEffects);
                PlayList(_smelter.m_fuelAddedEffects);
                Traverse added = Traverse.Create(_smelter).Field("m_addedOreTime");
                if (added.FieldExists()) added.SetValue(Time.time);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Add effects failed: " + e.Message);
            }
        }

        private void PlayList(EffectList list)
        {
            if (list == null || list.m_effectPrefabs == null || list.m_effectPrefabs.Length == 0) return;
            list.Create(transform.position, transform.rotation);
        }

        // True when the given row holds at least one item.
        internal bool HasItemsInRow(int row)
        {
            Inventory inv = _container != null ? _container.GetInventory() : null;
            if (inv == null) return false;
            foreach (ItemDrop.ItemData item in inv.GetAllItems())
                if (item.m_gridPos.y == row && item.m_stack > 0) return true;
            return false;
        }

        internal bool IsRunning()
        {
            return _nview != null && _nview.IsValid() && _nview.GetZDO().GetInt(KeyActive) == 1;
        }

        // Too hot to take anything out: while the smelter is working, and for the cool-down time after it stops.
        internal bool IsHot()
        {
            if (_nview == null || !_nview.IsValid() || ZNet.instance == null) return false;
            ZDO zdo = _nview.GetZDO();
            if (zdo.GetInt(KeyActive) == 1) return true;
            return ZNet.instance.GetTime().Ticks < zdo.GetLong(KeyHotUntil, 0L);
        }

        // ---- The forge's own fire and glow (the 3D model), faded in and out with the heat ----------------------------------

        private class LightRef { public Light L; public Traverse Base; public float Orig; public bool Flicker; }
        private class PartRef { public ParticleSystem P; public float Rate; public bool IsGlow; }
        private readonly List<LightRef> _lights = new List<LightRef>();
        private readonly List<PartRef> _parts = new List<PartRef>();
        private bool _fireInit;
        private float _glowShown = -1f, _flamesShown = -1f;

        // Fire and glow targets from the heat level. Same curves as the screen: the glow holds until the last quarter of
        // the cool-down, the flames go out earlier and leave the embers glowing.
        internal static void FireTargets(float heat, out float glow, out float flames)
        {
            // Flames go out first (heat 0.9 down to 0.5), then the glow fades slowly over the last half of the cool-down.
            glow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(heat / 0.5f));
            flames = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((heat - 0.5f) / 0.4f));
        }

        private class GlowRef { public Renderer R; public Material[] Mats; public List<Color[]> Orig = new List<Color[]>(); public string[] Props; }
        private readonly List<GlowRef> _glowMeshes = new List<GlowRef>();
        private readonly List<AudioSource> _loopSources = new List<AudioSource>();
        private readonly List<Traverse> _loopSounds = new List<Traverse>(); // the refinery's own running loop, driven explicitly
        private readonly List<Traverse> _fireSounds = new List<Traverse>(); // the crackling loop's volume, follows the flames
        private static readonly string[] ColorProps = { "_TintColor", "_EmissionColor" };

        // Wind-up and wind-down of the machine: 0 (stopped) to 1 (full speed). Drives the animation speed and the running loop.
        private float _spin = -1f;
        private bool _spinHeld;
        private readonly Dictionary<Animator, bool[]> _animHas = new Dictionary<Animator, bool[]>(); // has "active", has "activef"

        private bool Working()
        {
            return _nview != null && _nview.IsValid() && _nview.GetZDO().GetInt(KeyActive) == 1;
        }

        // Keeps the machine's animators running (at the current spin speed) while it winds up or down. The game switches
        // the animation off the moment the machine stops; this holds it on and slows it down instead.
        private float _coastEnd = -1f;
        private bool _downPhase;   // the machine is winding down (not working)
        private const float StartSpeed = 0.35f;     // animation speed the instant a machine is lit (it then speeds up to full)
        private const float SlowestSpeed = 0.2f;    // the animation never runs slower than this while winding down, so it keeps visibly moving
        private const float RestTolerance = 0.012f; // within this much of a cycle of the resting point counts as at rest
        private const float CoastLimit = 25f;       // safety: never finish the last cycle for longer than this (seconds)
        private const float CoastSeconds = 3.5f;    // the last cycle is paced to take about this long
        private const float CoastFloor = 0.1f;      // the last cycle never crawls slower than this until it arrives
        private const float MaxSpeedChange = 0.5f;  // the animation speed never changes faster than this per second, so it cannot jerk
        private readonly Dictionary<Animator, float> _animSpeed = new Dictionary<Animator, float>();
        private float _lastApplyT = -1f;

        // Moves an animator's speed toward a target without ever stepping, so every hand-off (start, wind-down, last turn) is smooth.
        private void GlideSpeed(Animator an, float target, float step, bool noSpeedUp)
        {
            float cur;
            if (!_animSpeed.TryGetValue(an, out cur) || !_spinHeld) cur = target;
            if (noSpeedUp && target > cur) target = cur;
            cur = Mathf.MoveTowards(cur, target, step);
            _animSpeed[an] = cur;
            an.speed = cur;
        }

        // The "activef" value to hold while the animation is being driven. The Blast Furnace ignores it for speed. The
        // refinery blends between a still clip and its grinding clip with it, and that blend also stretches the clip's
        // length (the still clip is hours long), so anything under 1 freezes the grinding. It is brought up to 1 over the
        // first moments of a start, then held at 1 for the whole wind-down.
        private float HeldActiveF(bool coasting)
        {
            if (IsRefinery) return (_downPhase || coasting) ? 1f : Mathf.Clamp01(_spin / 0.1f);
            return coasting ? SlowestSpeed : Mathf.Clamp01(_spin);
        }

        private void ApplySpin()
        {
            if (_spin < 0f || _smelter == null || _smelter.m_animators == null) return;
            bool stopping = _spin <= 0.01f && !Working();
            if (stopping && _spinHeld && _coastEnd < 0f) _coastEnd = Time.time + CoastLimit;
            if (!stopping) _coastEnd = -1f;
            bool anyCoasting = false;
            float step = _lastApplyT < 0f ? 0f : MaxSpeedChange * Mathf.Max(0f, Time.time - _lastApplyT);
            _lastApplyT = Time.time;
#if DEV
            bool logNow = StationConfig.AnimDebug.Value && Time.time >= _nextAnimLog && (_spinHeld || _spin > 0f);
#endif
            foreach (Animator an in _smelter.m_animators)
            {
                if (an == null) continue;
                bool[] has;
                if (!_animHas.TryGetValue(an, out has))
                {
                    has = new bool[2];
                    foreach (AnimatorControllerParameter ap in an.parameters)
                    {
                        if (ap.name == "active" && ap.type == AnimatorControllerParameterType.Bool) has[0] = true;
                        if (ap.name == "activef" && ap.type == AnimatorControllerParameterType.Float) has[1] = true;
                    }
                    _animHas[an] = has;
                }
                if (!has[0] && !has[1]) continue;
#if DEV
                if (logNow) LogAnimator(an, has, stopping);
                if (StationConfig.AnimDebug.Value && _spinHeld) CheckHitch(an, has);
#endif
                if (!stopping)
                {
                    if (has[0]) an.SetBool("active", true);
                    if (has[1]) an.SetFloat("activef", HeldActiveF(false));
                    // Starting: begins at a gentle speed (not the animator's normal speed, which made the first moments
                    // jump) and speeds up to full. Winding down: slows from full, never below the slowest speed.
                    GlideSpeed(an, _downPhase ? Mathf.Max(SlowestSpeed, _spin) : Mathf.Lerp(StartSpeed, 1f, _spin), step, false);
                    _spinHeld = true;
                }
                else if (_spinHeld)
                {
                    // Cooled down. If the animation is part way through a turn, it finishes that one turn slowly, easing
                    // to a stop at its resting point, and only then is reset to the stopped state. So it never stops short
                    // and snaps back.
                    AnimatorStateInfo si = an.GetCurrentAnimatorStateInfo(0);
                    float frac = si.normalizedTime - Mathf.Floor(si.normalizedTime);
                    bool atRest = frac < RestTolerance || frac > 1f - RestTolerance || an.IsInTransition(0);
                    if (!atRest && Time.time < _coastEnd)
                    {
                        if (has[0]) an.SetBool("active", true);
                        if (has[1]) an.SetFloat("activef", HeldActiveF(true));
                        // Pace the rest of the turn to take about CoastSeconds. The speed is proportional to what is left of the
                        // turn, so it eases out by itself, and it only ever slows down from where the wind-down left it.
                        float turnSeconds = si.length * Mathf.Max(0.01f, an.speed);   // the turn's length at normal speed
                        float left = 1f - frac;
                        GlideSpeed(an, Mathf.Clamp(left * turnSeconds / CoastSeconds, CoastFloor, 0.6f), step, true);
                        anyCoasting = true;
                    }
                    else
                    {
                        // No Rebind: that cuts straight to the default pose and shows as a snap. Switching the state off lets
                        // the animator blend into the stopped state the way the game itself does.
                        an.speed = StartSpeed; // so the first frame of the next start is gentle, not a burst at full speed
                        _animSpeed.Remove(an);
                        if (has[0]) an.SetBool("active", false);
                        if (has[1]) an.SetFloat("activef", 0f);
                    }
                }
            }
#if DEV
            if (logNow) _nextAnimLog = Time.time + 0.5f;
#endif
            if (stopping && !anyCoasting) { _spinHeld = false; _coastEnd = -1f; }
        }

#if DEV
        // Developer log of what an animator is doing (removed before release).
        private float _nextAnimLog;
        private readonly Dictionary<Animator, float[]> _hitchPrev = new Dictionary<Animator, float[]>(); // time, frame, speed used

        // Logs any frame where the animation advanced by noticeably more or less than its speed says it should.
        private int _tickFrame = -1;
        private void CheckHitch(Animator an, bool[] has)
        {
            try
            {
                if (Time.frameCount == _tickFrame) { _hitchPrev.Remove(an); return; } // the once-a-second tick runs before the animator updates
                if (an.IsInTransition(0)) { _hitchPrev.Remove(an); return; }
                AnimatorStateInfo si = an.GetCurrentAnimatorStateInfo(0);
                float nt = si.normalizedTime;
                float[] prev;
                if (_hitchPrev.TryGetValue(an, out prev) && prev[1] == Time.frameCount - 1 && si.length > 0.01f)
                {
                    float expected = Time.deltaTime * prev[2] / (si.length / Mathf.Max(0.01f, an.speed));
                    float actual = nt - prev[0];
                    if (Mathf.Abs(actual - expected) > expected * 0.4f + 0.002f)
                        Plugin.Log.LogInfo("[AnimHitch] " + gameObject.name + "/" + an.gameObject.name + " moved " + actual.ToString("0.0000") +
                            " expected " + expected.ToString("0.0000") + " dt=" + Time.deltaTime.ToString("0.000") + " speedUsed=" + prev[2].ToString("0.00") +
                            " len=" + si.length.ToString("0.00") + " nt=" + nt.ToString("0.000") + " active=" + (has[0] ? an.GetBool("active").ToString() : "-") +
                            " activef=" + (has[1] ? an.GetFloat("activef").ToString("0.00") : "-") + " spin=" + _spin.ToString("0.00"));
                }
                _hitchPrev[an] = new float[] { nt, Time.frameCount, an.speed };
            }
            catch (System.Exception) { }
        }

        private void LogAnimator(Animator an, bool[] has, bool stopping)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                foreach (AnimatorClipInfo ci in an.GetCurrentAnimatorClipInfo(0))
                    sb.Append(ci.clip != null ? ci.clip.name : "?").Append(':').Append(ci.weight.ToString("0.00")).Append(' ');
                if (an.IsInTransition(0))
                {
                    sb.Append("=> ");
                    foreach (AnimatorClipInfo ci in an.GetNextAnimatorClipInfo(0))
                        sb.Append(ci.clip != null ? ci.clip.name : "?").Append(':').Append(ci.weight.ToString("0.00")).Append(' ');
                }
                AnimatorStateInfo si = an.GetCurrentAnimatorStateInfo(0);
                Plugin.Log.LogInfo("[AnimDebug] " + gameObject.name + "/" + an.gameObject.name +
                    " spin=" + _spin.ToString("0.00") + " stopping=" + stopping +
                    " active=" + (has[0] ? an.GetBool("active").ToString() : "-") +
                    " activef=" + (has[1] ? an.GetFloat("activef").ToString("0.00") : "-") +
                    " speed=" + an.speed.ToString("0.00") + " t=" + si.normalizedTime.ToString("0.00") +
                    " len=" + si.length.ToString("0.00") + " layers=" + an.layerCount +
                    " transition=" + an.IsInTransition(0) + " clips=[" + sb.ToString().Trim() + "]");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("AnimDebug failed: " + e.Message); }
        }
#endif

        private void LateUpdate()
        {
            if (!Ready || _smelter == null || _smelter.m_enabledObject == null) return;
            {
                // Spin value follows the machine: up while it works, down when it stops.
                bool working = Working();
                if (_spin < 0f) _spin = working ? 1f : 0f; // a screen or world loaded on a running machine starts at full speed
                float up = Mathf.Max(0.1f, StationConfig.WindUpSeconds.Value);
                float down = Mathf.Max(0.1f, StationConfig.WindDownMatchesCoolDown.Value ? StationConfig.CoolDownSeconds.Value : StationConfig.WindDownSeconds.Value);
                _downPhase = !working;
                _spin = working ? Mathf.Min(1f, _spin + Time.deltaTime / up) : Mathf.Max(0f, _spin - Time.deltaTime / down);
                if (StationConfig.WindAnimation.Value) ApplySpin();
            }
            float glowT, flamesT;
            FireTargets(HeatLevel(), out glowT, out flamesT);
            if (_glowShown < 0f) { _glowShown = glowT; _flamesShown = flamesT; }
            float dt = Time.deltaTime;
            // Lighting up: the fire comes first (about 3 s), and the glow only starts once the fire is well going, then
            // takes about 4 s. Going out follows the targets straight down: fire first, then the slow glow fade.
            _flamesShown = flamesT > _flamesShown ? Mathf.Min(flamesT, _flamesShown + dt / 3f) : flamesT;
            if (glowT > _glowShown)
            {
                // Wait for the fire when a fire is due, unless it is still warm from a moment ago (relit while cooling).
                float ready = flamesT > 0.01f && _glowShown < 0.05f ? Mathf.Clamp01((_flamesShown - 0.35f) / 0.3f) : 1f;
                _glowShown = Mathf.Min(glowT, _glowShown + dt / 4f * ready);
            }
            else _glowShown = glowT;

            GameObject fire = _smelter.m_enabledObject;
            if (!_fireInit)
            {
                if (!fire.activeInHierarchy) return; // collect once it has switched on for the first time
                CollectFire(fire);
                _fireInit = true;
            }

            foreach (LightRef r in _lights)
            {
                if (r.L == null) continue;
                float v = r.Orig * _glowShown;
                if (r.Flicker) r.Base.SetValue(v); else r.L.intensity = v;
            }
            foreach (GlowRef g in _glowMeshes)
            {
                if (g.R == null) continue;
                g.R.enabled = _glowShown > 0.01f;
                for (int m = 0; m < g.Mats.Length; m++)
                {
                    if (g.Mats[m] == null) continue;
                    for (int p = 0; p < g.Props.Length; p++)
                    {
                        if (g.Props[p] == null || !g.Mats[m].HasProperty(g.Props[p])) continue;
                        Color o = g.Orig[m][p];
                        g.Mats[m].SetColor(g.Props[p], new Color(o.r * _glowShown, o.g * _glowShown, o.b * _glowShown, o.a * _glowShown));
                    }
                }
            }
            foreach (Traverse vol in _fireSounds) vol.SetValue(_flamesShown * StationConfig.FireCrackleVolume.Value);
            // The refinery's running loop: its volume was found stuck at zero while the machine was working, so it is set
            // here: full while the machine runs, fading out with the flames after it stops.
            float loopLevel = StationConfig.WindAnimation.Value ? Mathf.Clamp01(_spin) : Mathf.Clamp01(_flamesShown);
            foreach (Traverse vol in _loopSounds) vol.SetValue(loopLevel * StationConfig.RefineryLoopVolume.Value);
            // The animation also writes the sound's own volume (it was found at 0 while the machine ran), and that runs
            // before this, so the volume is set here as the last word of the frame.
            foreach (AudioSource src in _loopSources)
            {
                if (src == null) continue;
                float level = loopLevel * StationConfig.RefineryLoopVolume.Value;
                src.volume = level;
                if (StationConfig.WindAnimation.Value) src.pitch = Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(_spin)); // deeper as it winds down
                if (level > 0.01f && fire.activeInHierarchy && src.enabled && !src.isPlaying) src.Play();
            }
            foreach (PartRef r in _parts)
            {
                if (r.P == null) continue;
                float level = r.IsGlow ? _glowShown : _flamesShown;
                ParticleSystem.EmissionModule em = r.P.emission;
                em.rateOverTimeMultiplier = r.Rate * level;
                em.enabled = level > 0.02f;
            }
        }

        // Finds the lights and particle effects under the forge's "enabled" object, and logs what is there once so the
        // names can be checked. Particle effects named glow, heat or ember follow the glow; the rest are flames.
        private void CollectFire(GameObject fire)
        {
            foreach (Light l in fire.GetComponentsInChildren<Light>(true))
            {
                LightFlicker flick = l.GetComponent<LightFlicker>();
                var r = new LightRef { L = l, Flicker = flick != null };
                if (r.Flicker)
                {
                    r.Base = Traverse.Create(flick).Field("m_baseIntensity");
                    r.Orig = r.Base.GetValue<float>();
                }
                else r.Orig = l.intensity;
                _lights.Add(r);
            }
            // The crackling fire sound: ZSFX multiplies its volume by a modifier, which we drive with the flames.
            foreach (ZSFX sfx in fire.GetComponentsInChildren<ZSFX>(true))
            {
                Traverse v = Traverse.Create(sfx).Field("m_volumeModifier");
                if (!v.FieldExists()) continue;
                if (IsRefinery)
                {
                    _loopSounds.Add(v); // the refinery's own running loop, switched on and off with the machine
                    AudioSource src = sfx.GetComponent<AudioSource>();
                    if (src != null) _loopSources.Add(src);
                }
                else _fireSounds.Add(v);
            }

            // The meshes under the fire object (the red-orange glow inside the forge). Each gets its own copy of its
            // materials so only this forge fades, and its colour is scaled with the glow.
            foreach (MeshRenderer mr in fire.GetComponentsInChildren<MeshRenderer>(true))
            {
                var g = new GlowRef { R = mr, Mats = mr.materials, Props = ColorProps };
                var found = new List<string>();
                for (int m = 0; m < g.Mats.Length; m++)
                {
                    var arr = new Color[ColorProps.Length];
                    for (int p = 0; p < ColorProps.Length; p++)
                        if (g.Mats[m] != null && g.Mats[m].HasProperty(ColorProps[p]))
                        {
                            arr[p] = g.Mats[m].GetColor(ColorProps[p]);
                            found.Add(ColorProps[p]);
                        }
                    g.Orig.Add(arr);
                }
                string shader = g.Mats.Length > 0 && g.Mats[0] != null ? g.Mats[0].shader.name : "none";
                Plugin.Log.LogInfo("Forge fire mesh " + mr.name + ": shader " + shader + ", colour properties: " +
                                   (found.Count > 0 ? string.Join(", ", found.ToArray()) : "none found"));
                _glowMeshes.Add(g);
            }
            foreach (ParticleSystem p in fire.GetComponentsInChildren<ParticleSystem>(true))
            {
                string n = p.name.ToLowerInvariant();
                _parts.Add(new PartRef
                {
                    P = p,
                    Rate = p.emission.rateOverTimeMultiplier,
                    IsGlow = n.Contains("glow") || n.Contains("heat") || n.Contains("ember")
                });
            }
            Plugin.Log.LogInfo("Forge fire: " + _lights.Count + " light(s), " + _parts.Count + " particle effect(s) under " + fire.name);
            LogTree(fire.transform, "  ");
        }

        private static void LogTree(Transform t, string indent)
        {
            var types = new List<string>();
            foreach (Component c in t.GetComponents<Component>())
                if (c != null && !(c is Transform)) types.Add(c.GetType().Name);
            Plugin.Log.LogInfo("Forge fire tree:" + indent + t.name + (types.Count > 0 ? " [" + string.Join(", ", types.ToArray()) + "]" : ""));
            foreach (Transform child in t) LogTree(child, indent + "  ");
        }

        // 0 (cold) to 1 (working). After it stops it fades out over the cool-down time. Drives the glow behind the slots.
        internal float HeatLevel()
        {
            if (_nview == null || !_nview.IsValid() || ZNet.instance == null) return 0f;
            ZDO zdo = _nview.GetZDO();
            if (zdo.GetInt(KeyActive) == 1) return 1f;
            long until = zdo.GetLong(KeyHotUntil, 0L);
            long nowTicks = ZNet.instance.GetTime().Ticks;
            if (until <= nowTicks) return 0f;
            float total = Mathf.Max(1f, StationConfig.CoolDownSeconds.Value) * System.TimeSpan.TicksPerSecond;
            return Mathf.Clamp01((until - nowTicks) / total);
        }

        // True (and the player is told) when something is being taken out of a hot station inventory. Moving things
        // around inside the same inventory is not taking them out.
        internal static bool HotBlock(Inventory from, Inventory to)
        {
            if (from == null || from == to) return false;
            if (!Registry.TryGetValue(from, out StationContainer station) || !station.IsHot()) return false;
            if (Player.m_localPlayer != null)
            {
                string msg = "Too hot to touch";
                if (station.IsRefinery)
                    msg = OpenStation == station && OpenView == ViewMode.Input
                        ? "You'd lose your fingers" // the Softtissue box is where it gets ground up
                        : "Too acidic to touch";
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, msg);
            }
            return true;
        }

        // Text shown above the Input row while the station is stuck: "" when there is nothing to say (empty, running,
        // or something in Input can be smelted). Worked out from the inventory, so every player at the screen sees it.
        internal string StatusMessage()
        {
            if (_nview == null || !_nview.IsValid() || _container == null) return "";
            Inventory inv = _container.GetInventory();
            if (inv == null) return "";
            ZDO zdo = _nview.GetZDO();
            if (!PowerOn)
            {
                // Unlit: always say why nothing can start, or tell the player it is ready to light.
                string why = Problem(inv, true);
                return why.Length > 0 ? why : "Press Light to start";
            }
            if (zdo.GetInt(KeyActive) == 1 || zdo.GetString(KeyItem).Length > 0) return "";
            return Problem(inv, false);
        }

        private float _lightFailUntil;

        // Why nothing can start right now, or "" when something can.
        private string Problem(Inventory inv, bool sayEmpty)
        {
            bool any = false;
            for (int x = 0; x < Width; x++)
                if (inv.GetItemAt(x, RowInput) != null) { any = true; break; }
            if (!any) return sayEmpty ? (IsRefinery ? "No Softtissue loaded" : "Nothing in the Input row") : "";

            int coalHave = CountIn(inv, RowFuel, _fuelName);
            bool needCoal = false;

            bool bronzePossible = TryPlanBronze(inv, out List<string> units);
            if (bronzePossible)
            {
                Run bronze = BuildRun(BronzeTag + string.Join("|", units.ToArray()));
                if (bronze != null)
                {
                    if (coalHave >= bronze.Coal) return "";
                    needCoal = true;
                }
            }
            for (int x = 0; x < Width; x++)
            {
                ItemDrop.ItemData at = inv.GetItemAt(x, RowInput);
                if (at == null) continue;
                string name = ItemName(at);
                if (bronzePossible && (CopperFamily.Contains(name) || TinFamily.Contains(name))) continue;
                Run run = BuildRun(name);
                if (run == null) continue;
                if (coalHave >= run.Coal) return "";
                needCoal = true;
            }
            return needCoal ? (IsRefinery ? "Not enough Sap" : "Not enough coal") : "Insufficient materials to continue recipe";
        }

        private static EffectList _doneEffects;
        private static EffectList _hissEffects;
        private static bool _doneChecked;

        // The effects played when a bar comes out. The vanilla smelter's are a loud clank and a hiss; here the cauldron's
        // crafting effects (the bubbling) are used instead. Both lists are logged once so the choice can be checked.
        private void PlayDoneEffects()
        {
            // The refinery has its own pitch and volume for the finish sounds; ShapeSound picks these up while they are set.
            if (IsRefinery)
            {
                _pitchOverride = StationConfig.RefineryPitch.Value;
                _gainOverride = StationConfig.RefineryVolume.Value;
            }
            try { PlayDoneEffectsCore(); }
            finally { _pitchOverride = -1f; _gainOverride = -1f; }
        }

        private static float _pitchOverride = -1f, _gainOverride = -1f;

        private bool CfgBubbling { get { return StationConfig.FinishBubbling.Value; } }
        // The refinery plays the bubbling and a hammer thud, not the cooking-done sound.
        private bool CfgCookingDone { get { return !IsRefinery && StationConfig.FinishCookingDone.Value; } }
        private string CfgHiss
        {
            get
            {
                if (IsRefinery) return StationConfig.RefineryThudPrefab.Value != null ? StationConfig.RefineryThudPrefab.Value.Trim() : "";
                string v = StationConfig.FinishHissPrefab != null ? StationConfig.FinishHissPrefab.Value : "";
                return v != null ? v.Trim() : "";
            }
        }

        private void PlayDoneEffectsCore()
        {
            if (IsKiln)
            {
                // The kiln keeps its own finish sound (the steam puff), slowed down and faded like the smelter's.
                GameObject[] puff = _smelter.m_produceEffects.Create(transform.position, transform.rotation);
                if (puff != null) foreach (GameObject go in puff) ShapeSound(go, 1f);
                return;
            }
            if (!_doneChecked)
            {
                _doneChecked = true;
                try
                {
                    Plugin.Log.LogInfo("Smelter produce effects: " + DescribeEffects(_smelter.m_produceEffects));
                    if (ZNetScene.instance != null)
                    {
                        var hits = new List<string>();
                        foreach (GameObject pf in ZNetScene.instance.m_prefabs)
                        {
                            if (pf == null) continue;
                            string n = pf.name.ToLowerInvariant();
                            if (n.StartsWith("sfx_") && (n.Contains("hiss") || n.Contains("steam") || n.Contains("sizzle") ||
                                n.Contains("extinguish") || n.Contains("quench") || n.Contains("boil") || n.Contains("bubbl") || n.Contains("fizz")))
                                hits.Add(pf.name);
                        }
                        Plugin.Log.LogInfo("Possible hiss sounds: " + (hits.Count > 0 ? string.Join(", ", hits.ToArray()) : "(none found)"));

                        // The "meat is done" sound of the cooking stations and meat racks, played alongside the bubbling.
                        foreach (GameObject pf in ZNetScene.instance.m_prefabs)
                        {
                            if (pf == null) continue;
                            CookingStation cook = pf.GetComponent<CookingStation>();
                            if (cook == null || cook.m_doneEffect == null || cook.m_doneEffect.m_effectPrefabs.Length == 0) continue;
                            Plugin.Log.LogInfo("Cooking done effect: " + pf.name + " -> " + DescribeEffects(cook.m_doneEffect));
                            bool rack = pf.name.ToLowerInvariant().Contains("meatrack");
                            if (_hissEffects == null || rack) _hissEffects = cook.m_doneEffect;
                        }
                    }
                    GameObject cauldron = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("piece_cauldron") : null;
                    CraftingStation cs = cauldron != null ? cauldron.GetComponent<CraftingStation>() : null;
                    if (cs == null)
                    {
                        Plugin.Log.LogWarning("Cauldron not found, keeping the smelter's own finish effects.");
                    }
                    else
                    {
                        Plugin.Log.LogInfo("Cauldron craft effects: " + DescribeEffects(cs.m_craftItemEffects));
                        Plugin.Log.LogInfo("Cauldron done effects: " + DescribeEffects(cs.m_craftItemDoneEffects));
                        if (cs.m_craftItemEffects != null && cs.m_craftItemEffects.m_effectPrefabs.Length > 0) _doneEffects = cs.m_craftItemEffects;
                        else if (cs.m_craftItemDoneEffects != null && cs.m_craftItemDoneEffects.m_effectPrefabs.Length > 0) _doneEffects = cs.m_craftItemDoneEffects;
                    }
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning("Finish effects lookup failed: " + e.Message);
                }
            }
            if (CfgBubbling)
            {
                EffectList list = _doneEffects ?? _smelter.m_produceEffects;
                GameObject[] spawned = list.Create(transform.position, transform.rotation);
                if (_doneEffects != null && spawned != null)
                    foreach (GameObject go in spawned) ShapeSound(go, 1f);
            }

            // The cooking "done" sound alongside the bubbling.
            if (CfgCookingDone && _hissEffects != null)
            {
                GameObject[] extra = _hissEffects.Create(transform.position, transform.rotation);
                if (extra != null) foreach (GameObject go in extra) ShapeSound(go, 1f);
            }

            // The Blast Furnace plays its own vanilla finish effects (untouched) as the third sound, where the smelter
            // uses the FinishHissPrefab steam puff.
            if (PrefabName() != "smelter" && !IsRefinery)
            {
                _smelter.m_produceEffects.Create(transform.position, transform.rotation);
                return;
            }

            // An extra sound as well, if one is named in the config.
            string hiss = CfgHiss;
            if (hiss.Length > 0 && ZNetScene.instance != null)
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(hiss);
                if (prefab != null) ShapeSound(Object.Instantiate(prefab, transform.position, transform.rotation), 1f);
                else if (!_hissWarned) { _hissWarned = true; Plugin.Log.LogWarning("FinishHissPrefab '" + hiss + "' not found."); }
            }
        }

        private static bool _hissWarned;

        // Makes a one-shot sound fade in and out over its whole length, slightly slower (lower pitch), and gives it a
        // place in the world so it is heard at the forge and not everywhere.
        internal static void ShapeSound(GameObject go, float volume)
        {
            if (go == null) return;
            float pitch = Mathf.Clamp(_pitchOverride > 0f ? _pitchOverride : StationConfig.FinishSoundPitch.Value, 0.1f, 1f);
            volume *= _gainOverride >= 0f ? _gainOverride : StationConfig.FinishSoundVolume.Value;
            foreach (ZSFX z in go.GetComponentsInChildren<ZSFX>(true))
            {
                float len = 0f;
                foreach (AudioClip c in z.m_audioClips) if (c != null) len = Mathf.Max(len, c.length);
                if (len <= 0f) len = 1.5f;
                float lengthNow = len / pitch;
                z.m_fadeInDuration = Mathf.Min(0.3f, lengthNow * 0.2f);
                z.m_fadeOutDuration = Mathf.Min(0.7f, lengthNow * 0.35f);
                z.m_fadeOutOnAwake = true;
                z.m_fadeOutDelay = Mathf.Max(0f, lengthNow - z.m_fadeOutDuration);
                z.m_minPitch = pitch;
                z.m_maxPitch = pitch;
                z.m_minVol *= volume;
                z.m_maxVol *= volume;
                AudioSource src = z.GetComponent<AudioSource>();
                if (src != null && src.spatialBlend < 0.5f)
                {
                    src.spatialBlend = 1f;
                    src.minDistance = Mathf.Max(src.minDistance, 3f);
                    src.maxDistance = Mathf.Max(src.maxDistance, 30f);
                    src.rolloffMode = AudioRolloffMode.Linear;
                }
            }
        }

        private static string DescribeEffects(EffectList list)
        {
            if (list == null || list.m_effectPrefabs == null || list.m_effectPrefabs.Length == 0) return "(none)";
            var names = new List<string>();
            foreach (EffectList.EffectData d in list.m_effectPrefabs) names.Add(d.m_prefab != null ? d.m_prefab.name : "null");
            return string.Join(", ", names.ToArray());
        }

#if DEV
        // One line (plus animators) for the epaudio debug command: where the station is and what state it thinks it is in.
        internal string DebugReport(Vector3 from)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                sb.Append("  station ").Append(PrefabName()).Append(" at ").Append(transform.position.ToString("F1"))
                  .Append(" dist ").Append(Vector3.Distance(from, transform.position).ToString("0.0"));
                if (_nview != null && _nview.IsValid())
                {
                    ZDO zdo = _nview.GetZDO();
                    sb.Append(", ready ").Append(Ready).Append(", on ").Append(zdo.GetInt(KeyOn)).Append(", active ").Append(zdo.GetInt(KeyActive))
                      .Append(", owner ").Append(_nview.IsOwner());
                }
                GameObject fire = _smelter != null ? _smelter.m_enabledObject : null;
                if (fire != null)
                    sb.Append(", fire ").Append(fire.activeSelf ? "on" : "off").Append(" at ").Append(fire.transform.position.ToString("F1"))
                      .Append(" (offset ").Append((fire.transform.position - transform.position).ToString("F1")).Append(")");
                if (_smelter != null)
                {
                    WearNTear wear = GetComponent<WearNTear>();
                    if (wear != null) sb.Append("\n    health ").Append(wear.GetHealthPercentage().ToString("0.00"));
                    var listed = new HashSet<Animator>(_smelter.m_animators ?? new Animator[0]);
                    foreach (ZSFX z in _smelter.GetComponentsInChildren<ZSFX>(true))
                    {
                        AudioSource src = z.GetComponent<AudioSource>();
                        sb.Append("\n    zsfx ").Append(z.transform.parent != null ? z.transform.parent.name + "/" : "").Append(z.name)
                          .Append(" activeInHierarchy ").Append(z.gameObject.activeInHierarchy)
                          .Append(", maxVol ").Append(z.m_maxVol.ToString("0.00"))
                          .Append(", modifier ").Append(Traverse.Create(z).Field("m_volumeModifier").GetValue<float>().ToString("0.00"))
                          .Append(", fadeIn ").Append(z.m_fadeInDuration.ToString("0.0")).Append(", fadeOut ").Append(z.m_fadeOutDuration.ToString("0.0"))
                          .Append(", fadeOutOnAwake ").Append(z.m_fadeOutOnAwake);
                        if (src != null) sb.Append(", source vol ").Append(src.volume.ToString("0.00")).Append(" playing ").Append(src.isPlaying).Append(" loop ").Append(src.loop);
                    }
                    foreach (Animator an in _smelter.GetComponentsInChildren<Animator>(true))
                    {
                        sb.Append("\n    animator ").Append(listed.Contains(an) ? "[driven by the smelter] " : "[NOT driven by the smelter] ")
                          .Append(an.transform.parent != null ? an.transform.parent.name + "/" : "").Append(an.gameObject.name).Append(" enabled ").Append(an.enabled)
                          .Append(", active ").Append(an.gameObject.activeInHierarchy).Append(", speed ").Append(an.speed.ToString("0.00"));
                        try
                        {
                            AnimatorClipInfo[] ci = an.GetCurrentAnimatorClipInfo(0);
                            AnimatorStateInfo si = an.GetCurrentAnimatorStateInfo(0);
                            sb.Append(", clip ").Append(ci.Length > 0 && ci[0].clip != null ? ci[0].clip.name : "none")
                              .Append(" at ").Append(si.normalizedTime.ToString("0.00"));
                        }
                        catch (System.Exception) { }
                        foreach (AnimatorControllerParameter ap in an.parameters)
                        {
                            if (ap.type == AnimatorControllerParameterType.Bool) sb.Append(", ").Append(ap.name).Append("=").Append(an.GetBool(ap.name));
                            else if (ap.type == AnimatorControllerParameterType.Float) sb.Append(", ").Append(ap.name).Append("=").Append(an.GetFloat(ap.name).ToString("0.00"));
                        }
                    }
                }
            }
            catch (System.Exception e) { sb.Append(" (report failed: ").Append(e.Message).Append(")"); }
            return sb.ToString();
        }
#endif

        private enum StepResult { Idle, Blocked, Progressed }

        // Called once a second in place of the vanilla smelter loop.
        internal void Tick()
        {
            if (_nview == null || !_nview.IsValid() || _container == null) return;

            _updateRoof.GetValue();
            if (_smelter.m_smokeSpawner != null) _updateSmoke.GetValue(); // the refinery has no smoke spawner
            _updateState.GetValue();
#if DEV
            _tickFrame = Time.frameCount;
#endif
            if (StationConfig.WindAnimation.Value) ApplySpin(); // the game just switched the animation off; hold it on while winding down
            if (!_nview.IsOwner()) return;

            Inventory inv = _container.GetInventory();
            if (inv == null) return;
            ZDO zdo = _nview.GetZDO();

            // Time since the last tick, so a smelter keeps working while the player is away (capped at one hour, like vanilla).
            System.DateTime now = ZNet.instance.GetTime();
            long last = zdo.GetLong(KeyTime, now.Ticks);
            zdo.Set(KeyTime, now.Ticks);
            double seconds = (now - new System.DateTime(last)).TotalSeconds;
            if (seconds < 0) seconds = 0;
            if (seconds > 3600) seconds = 3600;
            // Keep the sub-second remainder out of it: a normal tick is about one second, which is one step.
            int steps = Mathf.Max(1, Mathf.FloorToInt((float)seconds));

            bool active = false;
            bool changed = false;
            bool ranDry = false;
            for (int i = 0; i < steps; i++)
            {
                StepResult r = Step(zdo, inv, ref changed);
                if (r == StepResult.Progressed) { active = true; continue; }
                ranDry = r == StepResult.Idle && PowerOn; // lit but nothing left it can run
                break; // idle or blocked: nothing more to do with the rest of the time
            }

            // Ran out of coal or ore: go out by itself. The cool-down counts from the last working tick, and the
            // forge waits unlit (button back to Light) for the player to add what is missing.
            if (ranDry)
            {
                zdo.Set(KeyOn, 0);
                zdo.Set(KeyItem, "");
                zdo.Set(KeyProgress, 0f);
            }

            zdo.Set(KeyActive, active ? 1 : 0);
            if (active)
                zdo.Set(KeyHotUntil, now.Ticks + (long)(StationConfig.CoolDownSeconds.Value * System.TimeSpan.TicksPerSecond));
            if (changed) Traverse.Create(_container).Method("Save").GetValue();
        }

        private bool RoofOrSmokeBlocked()
        {
            bool roof = _haveRoof.GetValue<bool>();
            bool smoke = _blockedSmoke.GetValue<bool>();
            return (_smelter.m_requiresRoof && !roof) || smoke;
        }

        private static readonly HashSet<string> CopperFamily = new HashSet<string> { "CopperOre", "CopperScrap", "Copper" };
        private static readonly HashSet<string> TinFamily = new HashSet<string> { "TinOre", "EP_TinScrap", "Tin" };
        private const string BronzeTag = "BRONZE|";

        // A run: what it melts, what it makes, and what it costs.
        private class Run
        {
            public readonly List<string> Inputs = new List<string>();
            public float Seconds;
            public int Coal;
            public ItemDrop.ItemData Product;
            public int Count = 1;
        }

        // Seconds and coal to melt one unit of this item: ore, scrap, or an already-smelted bar (alloying only).
        private static void UnitCost(string name, out float secs, out float coal)
        {
            // Only the two alloy bars are charged as bars. Everything else the station melts is scrap (name has "Scrap")
            // or raw ore, whatever it is called (FlametalOreNew, or an ore another mod adds).
            if (name.Contains("Scrap")) { secs = StationConfig.ScrapSeconds.Value; coal = StationConfig.ScrapCoal.Value; }
            else if (name == "Copper" || name == "Tin") { secs = StationConfig.BarSeconds.Value; coal = StationConfig.BarCoal.Value; }
            else { secs = StationConfig.OreSeconds.Value; coal = StationConfig.OreCoal.Value; }
        }

        private static ItemDrop BronzePrefab()
        {
            if (ObjectDB.instance == null) return null;
            GameObject go = ObjectDB.instance.GetItemPrefab("Bronze");
            return go != null ? go.GetComponent<ItemDrop>() : null;
        }

        // Builds a run from the saved run name: a single item name (plain smelting), or BRONZE|a|b|c.
        private Run BuildRun(string cur)
        {
            var run = new Run();
            if (IsKiln)
            {
                // Wood to coal at the kiln's own speed, no fuel needed.
                Smelter.ItemConversion kc = FindConversion(cur);
                if (kc == null || kc.m_to == null) return null;
                run.Inputs.Add(cur);
                run.Product = kc.m_to.m_itemData.Clone();
                run.Product.m_dropPrefab = kc.m_to.gameObject;
                run.Product.m_stack = run.Count;
                run.Seconds = Mathf.Max(1f, _smelter.m_secPerProduct);
                run.Coal = 0;
                return run;
            }
            if (IsRefinery)
            {
                // Softtissue to Eitr at the refinery's own conversion, with its own time and Sap cost (config).
                Smelter.ItemConversion rc = FindConversion(cur);
                if (rc == null || rc.m_to == null) return null;
                run.Inputs.Add(cur);
                run.Product = rc.m_to.m_itemData.Clone();
                run.Product.m_dropPrefab = rc.m_to.gameObject;
                run.Product.m_stack = run.Count;
                run.Seconds = StationConfig.RefinerySeconds.Value;
                run.Coal = StationConfig.RefinerySap.Value;
                return run;
            }
            if (cur.StartsWith(BronzeTag))
            {
                foreach (string n in cur.Substring(BronzeTag.Length).Split('|'))
                    if (n.Length > 0) run.Inputs.Add(n);
                ItemDrop bronze = BronzePrefab();
                if (bronze == null) return null;
                run.Product = bronze.m_itemData.Clone();
                run.Product.m_dropPrefab = bronze.gameObject;
                run.Count = Mathf.Clamp(StationConfig.BronzeOutput.Value, 1, 3);
            }
            else
            {
                Smelter.ItemConversion conv = FindConversion(cur);
                if (conv == null || conv.m_to == null) return null; // bars have no plain recipe
                run.Inputs.Add(cur);
                run.Product = conv.m_to.m_itemData.Clone();
                run.Product.m_dropPrefab = conv.m_to.gameObject;
            }
            if (run.Inputs.Count == 0) return null;
            run.Product.m_stack = run.Count;

            float coal = 0f;
            foreach (string n in run.Inputs)
            {
                UnitCost(n, out float s, out float c);
                run.Seconds += s;
                coal += c;
            }
            run.Coal = Mathf.CeilToInt(coal - 0.001f); // the run needs its whole coal, rounded up
            return run;
        }

        // Two copper-family items and one tin-family item from the Input row, in slot order, if there are enough.
        private bool TryPlanBronze(Inventory inv, out List<string> units)
        {
            units = new List<string>();
            int cu = 0, sn = 0;
            for (int x = 0; x < Width; x++)
            {
                ItemDrop.ItemData at = inv.GetItemAt(x, RowInput);
                if (at == null) continue;
                string name = ItemName(at);
                bool isCu = CopperFamily.Contains(name);
                bool isSn = TinFamily.Contains(name);
                if (!isCu && !isSn) continue;
                for (int i = 0; i < at.m_stack; i++)
                {
                    if (isCu && cu < 2) { units.Add(name); cu++; }
                    else if (isSn && sn < 1) { units.Add(name); sn++; }
                }
            }
            return cu == 2 && sn == 1;
        }

        private bool HasInputs(Inventory inv, List<string> inputs)
        {
            var need = new Dictionary<string, int>();
            foreach (string n in inputs)
            {
                need.TryGetValue(n, out int k);
                need[n] = k + 1;
            }
            foreach (KeyValuePair<string, int> kv in need)
                if (CountIn(inv, RowInput, kv.Key) < kv.Value) return false;
            return true;
        }

        private int CountIn(Inventory inv, int row, string prefabName)
        {
            int n = 0;
            for (int x = 0; x < Width; x++)
            {
                ItemDrop.ItemData at = inv.GetItemAt(x, row);
                if (at != null && ItemName(at) == prefabName) n += at.m_stack;
            }
            return n;
        }

        private void TakeFrom(Inventory inv, int row, string prefabName, int amount)
        {
            for (int x = 0; x < Width && amount > 0; x++)
            {
                ItemDrop.ItemData at = inv.GetItemAt(x, row);
                if (at == null || ItemName(at) != prefabName) continue;
                int take = Mathf.Min(amount, at.m_stack);
                inv.RemoveItem(at, take);
                amount -= take;
            }
        }


        // How many of this item the Output row could still take.
        private int OutputRoom(Inventory inv, ItemDrop.ItemData sample)
        {
            int cap = StationConfig.SlotCap(RowOutput, sample.m_shared.m_maxStackSize);
            int room = 0;
            for (int row = FirstOutputRow; row <= RowOutput; row++)
            {
                for (int x = 0; x < Width; x++)
                {
                    ItemDrop.ItemData at = inv.GetItemAt(x, row);
                    if (at == null) room += cap;
                    else if (at.IsSameType(sample)) room += Mathf.Max(0, cap - at.m_stack);
                }
            }
            return room;
        }

        // Picks the next run the Coal row can pay for. A Bronze run comes first: whenever the Input row holds two
        // copper-family items and one tin-family item, those are never smelted on their own. Anything beyond that is
        // smelted separately into plain bars. Bars have no plain recipe, so they only ever go into Bronze.
        // Nothing is taken yet: the items and coal are only spent when the run is done.
        private bool StartNext(ZDO zdo, Inventory inv)
        {
            int coalHave = CountIn(inv, RowFuel, _fuelName);

            bool bronzePossible = TryPlanBronze(inv, out List<string> units);
            if (bronzePossible)
            {
                string tag = BronzeTag + string.Join("|", units.ToArray());
                Run bronze = BuildRun(tag);
                if (bronze != null && coalHave >= bronze.Coal)
                {
                    zdo.Set(KeyItem, tag);
                    zdo.Set(KeyProgress, 0f);
                    return true;
                }
            }

            for (int x = 0; x < Width; x++)
            {
                ItemDrop.ItemData at = inv.GetItemAt(x, RowInput);
                if (at == null) continue;
                string name = ItemName(at);
                if (bronzePossible && (CopperFamily.Contains(name) || TinFamily.Contains(name))) continue; // waiting for coal for Bronze
                Run run = BuildRun(name);
                if (run == null || coalHave < run.Coal) continue;
                zdo.Set(KeyItem, name);
                zdo.Set(KeyProgress, 0f);
                return true;
            }
            return false;
        }

        private StepResult Step(ZDO zdo, Inventory inv, ref bool changed)
        {
            if (!PowerOn) return StepResult.Idle; // unlit or doused: nothing runs
            if (RoofOrSmokeBlocked()) return StepResult.Blocked;

            string cur = zdo.GetString(KeyItem);

            // Bronze comes first: if a plain copper or tin run is going but a Bronze run is now possible, drop the plain
            // run (nothing was spent yet) and let StartNext pick Bronze, or wait for its coal.
            if (cur.Length > 0 && !cur.StartsWith(BronzeTag) &&
                PowerOn && (CopperFamily.Contains(cur) || TinFamily.Contains(cur)) && TryPlanBronze(inv, out _))
            {
                zdo.Set(KeyItem, "");
                zdo.Set(KeyProgress, 0f);
                cur = "";
            }

            if (cur.Length == 0)
            {
                if (!PowerOn || !StartNext(zdo, inv)) return StepResult.Idle;
                cur = zdo.GetString(KeyItem);
            }

            // The run is only valid while its items and its coal are still there (the player may have taken them out).
            Run run = BuildRun(cur);
            if (run == null || !HasInputs(inv, run.Inputs) || CountIn(inv, RowFuel, _fuelName) < run.Coal)
            {
                zdo.Set(KeyItem, "");
                zdo.Set(KeyProgress, 0f);
                return StepResult.Idle;
            }

            float prog = zdo.GetFloat(KeyProgress);
            if (prog >= run.Seconds && OutputRoom(inv, run.Product) < run.Count) return StepResult.Blocked; // waiting for room in Output

            prog += 1f;
            if (prog < run.Seconds)
            {
                zdo.Set(KeyProgress, prog);
                return StepResult.Progressed;
            }

            // Done: spend the items and the coal, put the bars in Output.
            if (OutputRoom(inv, run.Product) < run.Count)
            {
                zdo.Set(KeyProgress, run.Seconds);
                return StepResult.Blocked;
            }
            foreach (string n in run.Inputs) TakeFrom(inv, RowInput, n, 1);
            TakeFrom(inv, RowFuel, _fuelName, run.Coal);
            AddToOutput(inv, run.Product);
            PlayDoneEffects();
            zdo.Set(KeyItem, "");
            zdo.Set(KeyProgress, 0f);
            changed = true;
            return StepResult.Progressed;
        }

        internal static string ItemName(ItemDrop.ItemData item)
        {
            return item != null && item.m_dropPrefab != null ? item.m_dropPrefab.name : "";
        }

        // The row an item belongs in, or -1 if the station does not take it. Nothing goes into Output by hand.
        internal int RowFor(ItemDrop.ItemData item)
        {
            string n = ItemName(item);
            if (n.Length == 0) return -1;
            if (!IsKiln && n == _fuelName) return RowFuel;
            if (_inputs.Contains(n)) return RowInput;
            return -1;
        }

        // How many of this item the station could still take.
        internal int FreeRoom(Inventory inv, ItemDrop.ItemData item)
        {
            int row = RowFor(item);
            if (row < 0) return 0;
            int only = ViewRow();
            if (only >= 0 && row != only) return 0; // a single-row box only takes its own item
            if (MainLocked(row)) return 0;
            int cap = CapFor(row, item.m_shared.m_maxStackSize);
            int room = 0;
            for (int x = 0; x < Width; x++)
            {
                ItemDrop.ItemData at = inv.GetItemAt(x, row);
                if (at == null) room += cap;
                else if (at.IsSameType(item)) room += Mathf.Max(0, cap - at.m_stack);
            }
            return room;
        }

        // Automatic placement (used by stack all, other mods, and anything that does not pick a slot):
        // fill matching stacks in the item's row first, then empty slots in that row.
        internal bool AddToRow(Inventory inv, ItemDrop.ItemData item)
        {
            int row = RowFor(item);
            if (row < 0) return false;
            int only = ViewRow();
            if (only >= 0 && row != only) return false;
            if (MainLocked(row)) return false;

            for (int pass = 0; pass < 2; pass++)
            {
                for (int x = 0; x < Width && item.m_stack > 0; x++)
                {
                    ItemDrop.ItemData at = inv.GetItemAt(x, row);
                    if (pass == 0 && (at == null || !at.IsSameType(item))) continue;
                    if (pass == 1 && at != null) continue;
                    StationPatches.AddAt(inv, item, item.m_stack, x, row);
                }
            }
            return item.m_stack <= 0;
        }
    }
}

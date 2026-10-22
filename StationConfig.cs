using BepInEx.Configuration;
using UnityEngine;

namespace EnigmasEnhancedForging
{
    // Settings for the station screens. Shows up in BepInEx\config\EnigmaPrime.EnigmasEnhancedForging.cfg.
    internal static class StationConfig
    {
        public static ConfigEntry<bool> SmelterScreen;
        public static ConfigEntry<int> InputSlotMax;
        public static ConfigEntry<int> FuelSlotMax;
        public static ConfigEntry<int> OutputSlotMax;

        // Smelting cost per metal melted. Time in seconds, coal in whole coal (a run needs the coal rounded up).
        public static ConfigEntry<float> OreSeconds;
        public static ConfigEntry<float> OreCoal;
        public static ConfigEntry<float> ScrapSeconds;
        public static ConfigEntry<float> ScrapCoal;
        public static ConfigEntry<float> BarSeconds;
        public static ConfigEntry<float> BarCoal;
        public static ConfigEntry<float> CoolDownSeconds;
        public static ConfigEntry<float> FinishSoundPitch;
        public static ConfigEntry<float> FinishSoundVolume;
        public static ConfigEntry<float> FireCrackleVolume;
        public static ConfigEntry<string> FinishHissPrefab;
#if DEV
        public static ConfigEntry<bool> DumpSounds;
#endif
        public static ConfigEntry<bool> FinishBubbling;
        public static ConfigEntry<bool> FinishCookingDone;
        public static ConfigEntry<int> BronzeOutput;
        public static ConfigEntry<bool> RemoveForgeBronzeRecipe;
        public static ConfigEntry<int> RefineryInputSlotMax;
        public static ConfigEntry<int> RefineryFuelSlotMax;
        public static ConfigEntry<float> RefinerySeconds;
        public static ConfigEntry<int> RefinerySap;
        public static ConfigEntry<float> RefineryPitch;
        public static ConfigEntry<float> RefineryVolume;
        public static ConfigEntry<float> RefineryLoopVolume;
        public static ConfigEntry<bool> WindAnimation;
        public static ConfigEntry<float> WindUpSeconds;
        public static ConfigEntry<float> WindDownSeconds;
        public static ConfigEntry<bool> WindDownMatchesCoolDown;
#if DEV
        public static ConfigEntry<bool> AnimDebug;
#endif
        public static ConfigEntry<string> RefineryThudPrefab;
        public static ConfigEntry<float> RefinerySoftSwitchSize;

        public static void Bind(ConfigFile cfg)
        {
            SmelterScreen = cfg.Bind("Stations", "SmelterScreen", true,
                "Replace the Smelter's add ore and add coal switches with an inventory screen (input, coal and output rows). Needs a game restart to change.");

            var range = new AcceptableValueRange<int>(0, 200); // 200 = double the biggest vanilla stack (coal, 100)

            InputSlotMax = cfg.Bind("Stations", "InputSlotMax", 0,
                new ConfigDescription("Most items one slot of the Input row can hold. 0 means a full stack.", range));
            FuelSlotMax = cfg.Bind("Stations", "FuelSlotMax", 0,
                new ConfigDescription("Most items one slot of the Coal row can hold. 0 means a full stack.", range));
            OutputSlotMax = cfg.Bind("Stations", "OutputSlotMax", 0,
                new ConfigDescription("Most items one slot of the Output row can hold. 0 means a full stack.", range));

            RefineryInputSlotMax = cfg.Bind("Refinery", "InputSlotMax", 25,
                new ConfigDescription("Most Softtissue one slot of the Eitr Refinery's Softtissue box can hold. 4 slots, so 25 is 100 Softtissue, about an hour of refining. 0 means a full stack.", range));
            RefineryFuelSlotMax = cfg.Bind("Refinery", "SapSlotMax", 25,
                new ConfigDescription("Most Sap one slot of the Eitr Refinery's Sap box can hold. 4 slots, so 25 is 100 Sap, about an hour of refining. 0 means a full stack.", range));

            RefinerySeconds = cfg.Bind("Refinery", "SecondsPerEitr", 40f,
                new ConfigDescription("Seconds to refine one Softtissue into Eitr (the game's own value is 40).", new AcceptableValueRange<float>(1f, 600f)));
            RefinerySap = cfg.Bind("Refinery", "SapPerEitr", 1,
                new ConfigDescription("Sap used to refine one Softtissue (the game's own value is 1).", new AcceptableValueRange<int>(0, 20)));
            RefineryPitch = cfg.Bind("Refinery", "FinishSoundPitch", 0.5f,
                new ConfigDescription("Pitch and speed of the finish sounds when an Eitr comes out of the refinery. 1 is the original, lower is slower and deeper.", new AcceptableValueRange<float>(0.1f, 1f)));
            RefineryVolume = cfg.Bind("Refinery", "FinishSoundVolume", 1f,
                new ConfigDescription("Volume of the refinery's finish sounds (bubbling and the cooking-done hiss).", new AcceptableValueRange<float>(0f, 1f)));

            RefineryLoopVolume = cfg.Bind("Refinery", "RunningSoundVolume", 1f,
                new ConfigDescription("Volume of the refinery's own running sound while it refines (0 is silent).", new AcceptableValueRange<float>(0f, 1f)));
            RefineryThudPrefab = cfg.Bind("Refinery", "FinishThudPrefab", "sfx_build_hammer_stone",
                "Name of the sound prefab played with the bubbling when an Eitr comes out (a hammer thud by default; try sfx_build_hammer_wood, sfx_build_hammer_metal or sfx_build_hammer_default). Leave empty for none.");
            RefinerySoftSwitchSize = cfg.Bind("Refinery", "SofttissueSpotSize", 2.5f,
                new ConfigDescription("How much bigger the click area of the Softtissue spot (top of the refinery) is than the game's own, so it is not lost to the refinery's body. 1 is the original. Needs a game restart to change.", new AcceptableValueRange<float>(1f, 6f)));

            WindAnimation = cfg.Bind("Animation", "WindUpAndDown", true,
                "Machine animations (Blast Furnace wheels, the refinery's grinding) and the refinery's running sound speed up when the machine starts and slow down when it stops, instead of switching on and off.");
            WindUpSeconds = cfg.Bind("Animation", "WindUpSeconds", 4f,
                new ConfigDescription("Seconds for a machine to reach full speed after it is lit.", new AcceptableValueRange<float>(0.1f, 60f)));
#if DEV
            AnimDebug = cfg.Bind("Animation", "AnimDebug", true,
                "Developer only (removed before release): writes the machine animators' state to the log twice a second while they move.");
#endif
            WindDownMatchesCoolDown = cfg.Bind("Animation", "WindDownMatchesCoolDown", true,
                "When on, a machine slows down over the whole cool-down time, so it comes to rest just as the contents cool and can be taken. When off, WindDownSeconds is used instead.");
            WindDownSeconds = cfg.Bind("Animation", "WindDownSeconds", 10f,
                new ConfigDescription("Seconds for a machine to slow to a stop after it is doused or runs out. Only used when WindDownMatchesCoolDown is off.", new AcceptableValueRange<float>(0.1f, 120f)));

            var secs = new AcceptableValueRange<float>(1f, 600f);
            var coal = new AcceptableValueRange<float>(0f, 20f);
            OreSeconds = cfg.Bind("Smelting", "OreSeconds", 30f,
                new ConfigDescription("Seconds to melt one ore into a bar.", secs));
            OreCoal = cfg.Bind("Smelting", "OreCoal", 2f,
                new ConfigDescription("Coal used to melt one ore.", coal));
            ScrapSeconds = cfg.Bind("Smelting", "ScrapSeconds", 15f,
                new ConfigDescription("Seconds to melt one scrap into a bar (already smelted once, so faster).", secs));
            ScrapCoal = cfg.Bind("Smelting", "ScrapCoal", 1f,
                new ConfigDescription("Coal used to melt one scrap.", coal));
            BarSeconds = cfg.Bind("Smelting", "BarSeconds", 10f,
                new ConfigDescription("Seconds to alloy one bar into Bronze (used by the Bronze run).", secs));
            RemoveForgeBronzeRecipe = cfg.Bind("Smelting", "RemoveForgeBronzeRecipe", true,
                "Remove the vanilla Forge crafting recipes for Bronze, so Bronze can only be made in the Smelter. Needs a game restart to change.");
            BronzeOutput = cfg.Bind("Smelting", "BronzeOutput", 3,
                new ConfigDescription("Bronze bars made by one Bronze run (2 copper-family items plus 1 tin-family item). The cost is the same whatever you pick; only the yield changes.",
                    new AcceptableValueRange<int>(1, 3)));
            CoolDownSeconds = cfg.Bind("Smelting", "CoolDownSeconds", 20f,
                new ConfigDescription("While a smelter is working, and for this many seconds after it stops, everything inside is too hot to take out. Adding items is always allowed.",
                    new AcceptableValueRange<float>(0f, 600f)));
            FinishSoundPitch = cfg.Bind("Sound", "FinishSoundPitch", 0.5f,
                new ConfigDescription("Pitch and speed of the bubbling sound when a bar comes out. 1 is the original, lower is slower and deeper.",
                    new AcceptableValueRange<float>(0.1f, 1f)));
            FinishSoundVolume = cfg.Bind("Sound", "FinishSoundVolume", 1f,
                new ConfigDescription("Volume of the sounds played when a bar comes out (bubbling, done chime). Separate from the fire crackle.",
                    new AcceptableValueRange<float>(0f, 1f)));
            FireCrackleVolume = cfg.Bind("Sound", "FireCrackleVolume", 1f,
                new ConfigDescription("Volume of the fire crackling while a forge is lit. Separate from the finish sounds.",
                    new AcceptableValueRange<float>(0f, 1f)));
            FinishHissPrefab = cfg.Bind("Sound", "FinishHissPrefab", "sfx_kiln_produce",
                "Name of an extra sound prefab played with the bubbling when a bar comes out (a steam puff). Leave empty for none.");
            FinishBubbling = cfg.Bind("Sound", "FinishBubbling", true,
                "Play the cauldron bubbling when a bar comes out. Turn off (with FinishCookingDone) to test an extra sound on its own via FinishHissPrefab.");
            FinishCookingDone = cfg.Bind("Sound", "FinishCookingDone", true,
                "Play the meat rack 'done' sound when a bar comes out.");
#if DEV
            DumpSounds = cfg.Bind("Sound", "DumpSounds", false,
                "Developer tool: on the next game start, write every sound effect in the game to EnhancedForging_SoundDump.txt in the config folder, so a better finish sound can be picked. Turn it off again afterwards.");
#endif
            BarCoal = cfg.Bind("Smelting", "BarCoal", 0.6667f,
                new ConfigDescription("Coal used to alloy one bar into Bronze (used by the Bronze run).", coal));
        }

        // Cap for one slot in the given row, never above the item's own stack size.
        public static int SlotCap(int row, int itemMaxStack)
        {
            int cfg = row == StationContainer.RowInput ? InputSlotMax.Value
                    : row == StationContainer.RowFuel ? FuelSlotMax.Value
                    : OutputSlotMax.Value;
            return cfg > 0 ? Mathf.Min(cfg, itemMaxStack) : itemMaxStack;
        }
    }
}

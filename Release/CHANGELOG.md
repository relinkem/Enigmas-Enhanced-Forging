# Changelog

## 0.1.2
- New: the Frost Kiln now has its own screen. Put Ice in, take Frozen Fuel out, and Light or Douse it like the other stations. It glows cold blue, and its glow and sound fade out as it cools.
- Stations now cost more to build, because each one holds far more than in vanilla. The Smelter, Charcoal Kiln, Blast Furnace and Frost Kiln cost 3 times vanilla, and the Eitr Refinery costs 2 times. Each has its own setting in the new Build cost section of the config (needs a game restart).
- The Light button is always visible now, and is greyed out while there is nothing to start (no input, or not enough fuel).
- The Charcoal Kiln's inner glow now fades out smoothly as it cools.
- Fixed: the fire crackle could go quiet or disappear after relighting a station before it had cooled down.

## 0.1.1
- Bronze can now only be made in the Smelter. The Forge's own Bronze crafting recipes are removed. Turn this off with the `RemoveForgeBronzeRecipe` setting (needs a game restart).
- Fixed a harmless "font asset not found" warning in the log each time a station window opened.

## 0.1.0
First public release, a work in progress.
- Inventory-style screens for the Smelter, Charcoal Kiln, Blast Furnace and Eitr Refinery, with Input, Coal and Output rows (the Refinery has a Softtissue box, a Sap box and a main window).
- Light and Douse button with a status line. Machines start unlit and douse themselves when they run dry.
- Cool-down lock: nothing can be taken while a machine works and for 20 seconds after it stops.
- Bronze in one Smelter run (2 copper items plus 1 tin item for 3 Bronze), with cheaper times and coal for scrap and bars.
- New items: Tin Scrap, Silver Scrap and Flametal Scrap, smelted back into bars. Item Salvager returns scrap instead of bars when this mod is installed.
- Wind-up and wind-down animations for the Blast Furnace and the Eitr Refinery.
- Finish sounds, with adjustable volume and pitch.
- Works with Enigma's Item Salvager: Take and Stack on station screens, Sort and Drop hidden. Vanilla buttons without it.
- Not done yet: Spinning Wheel, Windmill and Frost Kiln.

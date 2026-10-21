# Enigma's Enhanced Forging

*Load the furnace, press Light, and go do something else.*

Smelters, kilns and the Eitr Refinery turn into machines you fill up and walk away from. Each one gets an inventory-style screen with rows for input, fuel and output, a Light and Douse button, a cool-down before you can reach inside, and wheels and gears that wind up and slow down instead of snapping on and off. Everyone on the server needs this mod.

**This is an early, work in progress release.** The Smelter, Charcoal Kiln, Blast Furnace and Eitr Refinery are done. The Spinning Wheel, Windmill and Frost Kiln still work like vanilla and will be added in later versions.

## Features

- **Inventory screens instead of switches.** Open a Smelter, Charcoal Kiln or Blast Furnace and you get a grid with an Input row, a Coal row and an Output row. The Eitr Refinery gets a Softtissue box, a Sap box and a main window for the Eitr.
- **Walk-away loads.** A full load of coal and ore keeps a smelter busy for about an hour without you touching it.
- **Light and Douse.** Machines start unlit. Press Light to start, Douse to stop. A status line tells you why a machine will not start, for example when the coal runs short.
- **Cool-down lock.** While a machine is working, and for 20 seconds after it stops, you cannot take anything out. Adding is always allowed. The fire and glow fade in and out with the heat.
- **Bronze in one run.** Put two copper items and one tin item in the Smelter and get 3 Bronze. Made from ore, that run costs 90 seconds and 6 coal (vanilla needs 270 seconds and 18 coal for the same 3 Bronze).
- **Scrap is cheaper to melt.** Scrap melts in 15 seconds for 1 coal, bars alloy in 10 seconds, ore stays at the vanilla 30 seconds for 2 coal.
- **New scrap items.** Tin Scrap, Silver Scrap and Flametal Scrap, smelted back into bars. With [Enigma's Item Salvager](https://thunderstore.io/c/valheim/p/Enigma_Prime/Enigmas_Item_Salvager/) installed, salvaged metal comes back as scrap that you remelt.
- **Winding animations.** Blast Furnace wheels and the Refinery's grinding speed up when lit, and slow down over the cool-down, finishing one last gentle turn before they stop.
- **Finish sounds.** A slowed bubbling when a bar comes out, and a hammer thud for the Refinery. Volume and pitch are adjustable.
- **Works with Item Salvager.** On a station screen you get Take and Stack, and the Sort and Drop buttons are hidden. Without Item Salvager you get the vanilla buttons.

## How it works

Each station is a container, so other mods treat it like a chest. Items go to the right row by themselves, you cannot put anything in the Output row, and the machine reads only the Input and Coal rows. Press Light and it runs until it runs out, then douses itself, cools down, and lets you take the result.

Refinery: click the top of the refinery for the Softtissue box, the front for the Sap box, and anywhere else for the main window. Take on the main window takes only the Eitr.

## Download

Install from Thunderstore with r2modman or the Thunderstore app once it is listed there. Until then, download the zip from the [GitHub releases page](https://github.com/relinkem/Enigmas-Enhanced-Forging/releases) and put the DLL in `BepInEx/plugins`. [Jotunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/) and BepInExPack Valheim are required.

To change settings in game, install Configuration Manager and press F1.

## Settings

Settings are in `BepInEx/config/EnigmaPrime.EnigmasEnhancedForging.cfg` after the first run. Smelting costs, slot sizes, cool-down, animation timing and sounds can all be changed. Full list:

[docs/CONFIG_REFERENCE.md](https://github.com/relinkem/Enigmas-Enhanced-Forging/blob/main/docs/CONFIG_REFERENCE.md)

## Multiplayer and servers

Everyone on the server needs the mod, on the same major and minor version.

## Compatibility

- Works with Enigma's Item Salvager (optional). Salvager is loaded first when both are installed, and its Take and Stack buttons are used on the station screens.
- Only the Smelter, Blast Furnace, Charcoal Kiln and Eitr Refinery are changed. Other mods' smelters are left alone.

## Known issues

- Work in progress: the Spinning Wheel, Windmill and Frost Kiln are not converted yet.
- Multiplayer: the Refinery's add effects and screen glow are not synced to other players.
- To be safe, empty your smelters and refinery before installing on an existing world.
- Controller support is built in through the game's own inventory screen but untested. Please report problems.
- The new scrap items reuse the look of vanilla Copper Scrap for now.

## Support

<a href="https://ko-fi.com/enigmaprime"><img src="https://raw.githubusercontent.com/relinkem/Enigmas-Enhanced-Forging/main/images/Ko-Fi-badge-web.png" alt="I asked AI for help and it told me to get Ko-fi" width="220" align="left" hspace="16"></a>

If you like the mod and want to say thanks, you can:<br>[buy me a coffee on Ko-fi](https://ko-fi.com/enigmaprime)

<br clear="left">

Bug reports and ideas are just as welcome. Open an issue on [GitHub](https://github.com/relinkem/Enigmas-Enhanced-Forging/issues) and include your BepInEx log (`LogOutput.log`) and, if it is a settings problem, your config file.

## More mods by Enigma Prime

<a href="https://thunderstore.io/c/valheim/p/Enigma_Prime/Enigmas_Realistic_Smoke_Rework/"><img src="https://raw.githubusercontent.com/RELinkem/Enigmas-Realistic-Smoke-Rework/main/images/icon.png" alt="Enigma's Realistic Smoke Rework icon" width="96" align="left" hspace="16"></a>

**[Enigma's Realistic Smoke Rework](https://thunderstore.io/c/valheim/p/Enigma_Prime/Enigmas_Realistic_Smoke_Rework/)**<br>Smoke that rises, pools and bends in the wind, with flames that follow their fuel.

<br clear="left">

<a href="https://thunderstore.io/c/valheim/p/Enigma_Prime/Enigmas_Item_Salvager/"><img src="https://raw.githubusercontent.com/relinkem/Enigmas-Item-Salvager/main/Release/icon.png" alt="Enigma's Item Salvager icon" width="96" align="left" hspace="16"></a>

**[Enigma's Item Salvager](https://thunderstore.io/c/valheim/p/Enigma_Prime/Enigmas_Item_Salvager/)**<br>Take your gear apart at the Workbench, Forge, Black Forge or Galdr Table and get some of the materials back.

<br clear="left">

<a href="https://thunderstore.io/c/valheim/p/Enigma_Prime/Enigmas_Useful_Tasty_Mead/"><img src="https://raw.githubusercontent.com/relinkem/Enigmas-Useful-Tasty-Mead/main/Release/icon.png" alt="Enigma's Useful Tasty Mead icon" width="96" align="left" hspace="16"></a>

**[Enigma's Useful Tasty Mead](https://thunderstore.io/c/valheim/p/Enigma_Prime/Enigmas_Useful_Tasty_Mead/)**<br>A sweet mead that makes your food count as freshly eaten again, lifting your max health, stamina and Eitr before fading back.

<br clear="left">

## How this mod was made

The ideas, the design, and every decision about how forging should work are mine: machines you fill and walk away from, scrap that has to be remelted instead of coming back as a clean bar, and a cool-down so a hot furnace is not something you can just reach into. However, I am not a programmer. The code was written by Claude, an AI assistant made by Anthropic, working from my direction through [Claude Code](https://claude.com/claude-code).

I want to be upfront about that. I am the one who plays the game, so I test every build in Valheim, read the game log for errors, and send back anything that looks or feels wrong until it behaves the way I intended. The code was also checked against Valheim's own code so it hooks in the right places. If you find a bug, please open an issue and I will work on a fix.

Not affiliated with or endorsed by Anthropic.

## Changelog

See [CHANGELOG.md](https://github.com/relinkem/Enigmas-Enhanced-Forging/blob/main/CHANGELOG.md).

## License

All rights reserved. You are free to download and use the mod, but please don't redistribute, modify or re-upload it without asking first, see [LICENSE](https://github.com/relinkem/Enigmas-Enhanced-Forging/blob/main/LICENSE). Valheim is the property of Iron Gate AB and Coffee Stain Publishing. This mod is not affiliated with or endorsed by them.

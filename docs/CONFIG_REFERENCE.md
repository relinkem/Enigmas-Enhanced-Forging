# Settings reference

Settings are written to `BepInEx/config/EnigmaPrime.EnigmasEnhancedForging.cfg` the first time the game runs with the mod. Edit the file, or use Configuration Manager (F1) in game. Times are in seconds.

## Stations

| Setting | Default | What it does |
|---|---|---|
| SmelterScreen | true | Replace the Smelter's add ore and add coal switches with an inventory screen. Needs a game restart. |
| InputSlotMax | 0 | Most items one slot of the Input row can hold. 0 means a full stack. |
| FuelSlotMax | 0 | Most items one slot of the Coal row can hold. 0 means a full stack. |
| OutputSlotMax | 0 | Most items one slot of the Output row can hold. 0 means a full stack. |

## Smelting

| Setting | Default | What it does |
|---|---|---|
| OreSeconds | 30 | Seconds to melt one ore into a bar. |
| OreCoal | 2 | Coal used to melt one ore. |
| ScrapSeconds | 15 | Seconds to melt one scrap into a bar. |
| ScrapCoal | 1 | Coal used to melt one scrap. |
| BarSeconds | 10 | Seconds to alloy one bar into Bronze. |
| BarCoal | 0.6667 | Coal used to alloy one bar into Bronze. |
| BronzeOutput | 3 | Bronze bars made by one Bronze run (2 copper items plus 1 tin item). The cost is for the 3 metals melted, whatever the output. |
| RemoveForgeBronzeRecipe | true | Remove the vanilla Forge crafting recipes for Bronze, so Bronze can only be made in the Smelter. Needs a game restart. |
| CoolDownSeconds | 20 | While a machine works, and for this many seconds after it stops, everything inside is locked. |

## Refinery

| Setting | Default | What it does |
|---|---|---|
| InputSlotMax | 25 | Most Softtissue one slot of the Softtissue box can hold (4 slots). |
| SapSlotMax | 25 | Most Sap one slot of the Sap box can hold (4 slots). |
| SecondsPerEitr | 40 | Seconds to refine one Softtissue into Eitr. |
| SapPerEitr | 1 | Sap used for each Eitr. |
| FinishSoundPitch | 0.5 | Pitch and speed of the finish sounds. 1 is the original, lower is slower and deeper. |
| FinishSoundVolume | 1 | Volume of the finish sounds. |
| RunningSoundVolume | 1 | Volume of the refinery's own running sound. 0 is silent. |
| FinishThudPrefab | sfx_build_hammer_stone | Sound prefab played as the thud when Eitr comes out. |
| SofttissueSpotSize | 2.5 | Size of the click area at the top of the refinery that opens the Softtissue box. Needs a game restart. |

## Animation

| Setting | Default | What it does |
|---|---|---|
| WindUpAndDown | true | Machine animations speed up when lit and slow down when stopped, instead of switching on and off. |
| WindUpSeconds | 4 | Seconds to reach full speed after lighting. |
| WindDownMatchesCoolDown | true | Slow down over the whole cool-down so the machine comes to rest as the contents cool. |
| WindDownSeconds | 10 | Seconds to slow to a stop. Only used when WindDownMatchesCoolDown is off. |

## Sound

| Setting | Default | What it does |
|---|---|---|
| FinishSoundPitch | 0.5 | Pitch and speed of the bubbling sound when a bar comes out. |
| FinishSoundVolume | 1 | Volume of the sounds played when a bar comes out. |
| FireCrackleVolume | 1 | Volume of the fire crackling while a forge is lit. |
| FinishHissPrefab | sfx_kiln_produce | Extra sound prefab played with the bubbling (a steam puff). Leave empty for none. |
| FinishBubbling | true | Play the cauldron bubbling when a bar comes out. |
| FinishCookingDone | true | Play the "done" sound when a bar comes out. |


## Build cost

Multipliers on every ingredient of the vanilla build cost. 1 is the game's own cost. Range 1 to 10. All need a game restart, and only affect machines built after the change.

| Setting | Default | What it does |
|---|---|---|
| SmelterCostMultiplier | 3 | Multiplies the Smelter's build cost. |
| CharcoalKilnCostMultiplier | 3 | Multiplies the Charcoal Kiln's build cost. |
| BlastFurnaceCostMultiplier | 3 | Multiplies the Blast Furnace's build cost. |
| EitrRefineryCostMultiplier | 2 | Multiplies the Eitr Refinery's build cost. |
| FrostKilnCostMultiplier | 3 | Multiplies the Frost Kiln's build cost. |

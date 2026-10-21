# Building from source

Visual Studio with the .NET desktop workload and the .NET Framework 4.8 targeting pack.

1. Install Valheim, and a r2modman profile named `Mod Preview` with BepInExPack Valheim and Jotunn. The project reads game DLLs from the Steam folder and `Jotunn.dll` from that profile. Paths are at the top of `EnigmasEnhancedForging.csproj`.
2. Put `BepInEx.dll` and `0Harmony.dll` (from BepInExPack, `BepInEx/core`) in a `libs` folder next to the project file.
3. Build. The DLL is copied into the `Mod Preview` profile only.

Debug builds define `DEV` and include developer tools that Release builds leave out: animation logging (`AnimDebug`), and the console commands `forgedump`, `salvagerart`, `spritedump`, `epsound` and `epaudio`.

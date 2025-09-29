# InterprocessLib
[![Thunderstore Badge](https://modding.resonite.net/assets/available-on-thunderstore.svg)](https://thunderstore.io/c/resonite/)

A library for [Resonite](https://resonite.com/) that allows BepisLoader plugins to send data to BepInEx renderer plugins and vice versa.

Technically the library itself doesn't depend on BepisLoader or BepInEx, so it could be used in other environments.

## Installation (Manual)
1. Install [BepisLoader](https://github.com/ResoniteModding/BepisLoader) for Resonite.
2. Download the latest release ZIP file (e.g., `Nytra-InterprocessLib-1.0.0.zip`) from the [Releases](https://github.com/Nytra/ResoniteInterprocessLib/releases) page.
3. Extract the ZIP and copy the `plugins` folder to your BepInEx folder in your Resonite installation directory and the renderer directory:
   - **Default location:** `C:\Program Files (x86)\Steam\steamapps\common\Resonite\BepInEx\`
   - **Renderer default location:** `C:\Program Files (x86)\Steam\steamapps\common\Resonite\Renderer\BepInEx\`
4. Start the game. If you want to verify that the mod is working you can check your BepInEx logs.

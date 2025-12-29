# InterprocessLib

A library for [Resonite](https://resonite.com/) that allows mods to send data to other processes such as the Unity renderer.

The library only depends on `Renderite.Shared`.

BepisLoader, BepInEx, RML and Standalone example projects are included in the Tests folder.

## Usage

After including the library in your project, all you have to do is create your own instance of the `Messenger` class. You can do this at any time, even before Resonite starts.

### !!! Make sure both processes register with the same name !!!

```
var messenger = new Messenger("PluginName");
```

From here you can use the messenger to send data or receive data.

```
messenger.SendValue<int>("TestValue", 637);
```

```
messenger.ReceiveValue<int>("TestValue", (val) =>
{
	Log($"TestValue: {val}");
});
```

For BepisLoader, BepInEx and RML, if you have a config entry in both processes with the same type and name, you can sync them like this:

```
messenger.SyncConfigEntry(MyConfigEntry);
```

You can also work with lists:

```
var list = new List<float>();
list.Add(2f);
list.Add(7f);
list.Add(21f);
messenger.SendValueCollection<List<float>, float>("TestValueList", list);
```

```
messenger.ReceiveValueCollection<List<float>, float>("TestValueList", (list) => 
{
	Log($"TestValueList: {string.Join(",", list!)}");
});
```

You can send any class or struct that has the IMemoryPackable interface:

```
var cmd = new TestCommand();
cmd.Value = 2932;
cmd.Text = "Hello world";
cmd.Time = DateTime.Now;
messenger.SendObject("TestCustomRendererCommand", cmd);
```

```
messenger.ReceiveObject<TestCommand>("TestCustomRendererCommand", (recvCmd) =>
{
	Log($"TestCustomRendererCommand: {recvCmd?.Value}, {recvCmd?.Text}, {recvCmd?.Time}");
});
```

For more examples, you can check the tests files: 

https://github.com/Nytra/ResoniteInterprocessLib/blob/main/InterprocessLib.Shared/Tests.cs

https://github.com/Nytra/ResoniteInterprocessLib/blob/main/Tests/InterprocessLib.BepInEx.Tests/BepInExTests.cs

https://github.com/Nytra/ResoniteInterprocessLib/blob/main/Tests/InterprocessLib.BepisLoader.Tests/BepisLoaderTests.cs

https://github.com/Nytra/ResoniteInterprocessLib/blob/main/Tests/InterprocessLib.RML.Tests/RML_Tests.cs

https://github.com/Nytra/ResoniteInterprocessLib/blob/main/Tests/InterprocessLib.Standalone.Tests/Program.cs

## Installation (BepisLoader/BepInEx) (Manual)
1. Install [BepisLoader](https://thunderstore.io/c/resonite/p/ResoniteModding/BepisLoader/) and [BepInExRenderer](https://thunderstore.io/c/resonite/p/ResoniteModding/BepInExRenderer/) and [RenderiteHook](https://thunderstore.io/c/resonite/p/ResoniteModding/RenderiteHook/) for Resonite.
2. Download the latest release ZIP file (e.g., `Nytra-InterprocessLib-1.0.0.zip`) from the [Releases](https://github.com/Nytra/ResoniteInterprocessLib/releases) page.
3. Extract the ZIP and copy the `plugins` folder to your BepInEx folder in your Resonite installation directory and the renderer directory:
   - **Default location:** `C:\Program Files (x86)\Steam\steamapps\common\Resonite\BepInEx\`
   - **Renderer default location:** `C:\Program Files (x86)\Steam\steamapps\common\Resonite\Renderer\BepInEx\`
4. Start the game. If you want to verify that the mod is working you can check your BepInEx logs.

## Installation (RML) (Manual)
1. Install [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader) for Resonite.
2. Download the latest release file `InterprocessLib.FrooxEngine.dll` and optionally the RML extensions `InterprocessLib.RML_Extensions.dll` from the [Releases](https://github.com/Nytra/ResoniteInterprocessLib/releases) page.
3. Put those downloaded files into your 'rml_libs' folder in your Resonite installation directory:
   - **Default location:** `C:\Program Files (x86)\Steam\steamapps\common\Resonite\rml_libs\`
4. Start the game. If you want to verify that the mod is working you can check your Resonite logs.
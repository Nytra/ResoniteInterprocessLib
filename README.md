# InterprocessLib

A library for [Resonite](https://resonite.com/) that allows mods to send data to the renderer and back.

The library only depends on `Renderite.Shared`, meaning it could work with other mod loaders e.g. MonkeyLoader.

BepisLoader and BepInEx projects are included.

## Usage

After including the library in your project, all you have to do is create your own instance of the `Messenger` class. You can do this at any time, even before Resonite starts.

```
var messenger = new Messenger("PluginName");
```

From here you can use the object to send data or to register callbacks to receive data. If you use the object before Resonite starts, the commands will get queued up.

```
messenger.SendValue<int>("TestValue", 637);
```

It's not much use to send a value if nothing in the other process knows to receive it. 

```
messenger.ReceiveValue<int>("TestValue", (val) =>
{
	Log($"TestValue: {val}");
});
```

If you want to send more complex data such as custom memory-packable structs and classes, you must register the types when you instantiate the messenger.

There are two lists that can be provided: the first is for `IMemoryPackable` class types, and the second is for `unmanaged` value types.

```
var messenger = new Messenger("UsingCustomTypes", [typeof(TestCommand), typeof(TestNestedPackable), typeof(TestPackable), typeof(RendererInitData)], [typeof(TestStruct), typeof(TestNestedStruct)]);
```

After doing this you can now send and receive those custom types.

```
var cmd = new TestCommand();
cmd.Value = 2932;
cmd.Text = "Hello world";
cmd.Time = DateTime.Now;
messenger.SendObject("TestCustomRendererCommand", cmd);
```

and to receive:

```
messenger.ReceiveObject<TestCommand>("TestCustomRendererCommand", (recvCmd) =>
{
	Log($"TestCustomRendererCommand: {recvCmd?.Value}, {recvCmd?.Text}, {recvCmd?.Time}");
});
```

For more examples you can check the tests files: 

https://github.com/Nytra/ResoniteInterprocessLib/blob/main/Extra/Tests.cs

https://github.com/Nytra/ResoniteInterprocessLib/blob/main/InterprocessLib.BepInEx.Tests/UnityPluginTests.cs

https://github.com/Nytra/ResoniteInterprocessLib/blob/main/InterprocessLib.BepisLoader.Tests/PluginTests.cs

## Installation (Manual)
1. Install [BepisLoader](https://github.com/ResoniteModding/BepisLoader) and [BepInExRenderer](https://thunderstore.io/c/resonite/p/ResoniteModding/BepInExRenderer/) and [RenderiteHook](https://thunderstore.io/c/resonite/p/ResoniteModding/RenderiteHook/) for Resonite.
2. Download the latest release ZIP file (e.g., `Nytra-InterprocessLib-1.0.0.zip`) from the [Releases](https://github.com/Nytra/ResoniteInterprocessLib/releases) page.
3. Extract the ZIP and copy the `plugins` folder to your BepInEx folder in your Resonite installation directory and the renderer directory:
   - **Default location:** `C:\Program Files (x86)\Steam\steamapps\common\Resonite\BepInEx\`
   - **Renderer default location:** `C:\Program Files (x86)\Steam\steamapps\common\Resonite\Renderer\BepInEx\`
4. Start the game. If you want to verify that the mod is working you can check your BepInEx logs.

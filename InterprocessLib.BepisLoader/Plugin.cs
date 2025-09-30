using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.NET.Common;
using BepInExResoniteShim;
using Elements.Core;
using Renderite.Shared;

namespace InterprocessLib;

[ResonitePlugin(PluginMetadata.GUID, PluginMetadata.NAME, PluginMetadata.VERSION, PluginMetadata.AUTHORS, PluginMetadata.REPOSITORY_URL)]
[BepInDependency(BepInExResoniteShim.PluginMetadata.GUID, BepInDependency.DependencyFlags.HardDependency)]
public class Plugin : BasePlugin
{
	internal static new ManualLogSource? Log;
	internal static ConfigEntry<bool>? TestBool;
	internal static ConfigEntry<float>? TestFloat;

	public override void Load()
	{
		Log = base.Log;
		Log.LogEvent += (sender, eventArgs) => 
		{
			switch (eventArgs.Level)
			{
				case LogLevel.Error:
					UniLog.Error($"[{PluginMetadata.NAME}] {eventArgs.Data}");
					break;
				case LogLevel.Warning:
					UniLog.Warning($"[{PluginMetadata.NAME}] {eventArgs.Data}");
					break;
				default:
					UniLog.Log($"[{PluginMetadata.NAME}] {eventArgs.Data}");
					break;
			}
		};

		BepisResoniteWrapper.ResoniteHooks.OnEngineReady += () => 
		{
			Messaging.OnCommandReceived += CommandHandler;
			Messaging.Host.OnFailure = FailHandler;
			Messaging.Host.OnWarning = WarnHandler;
			Messaging.Host.OnDebug = DebugHandler;

			Test();
		};

		TestBool = Config.Bind("General", "TestBool", false);
		TestBool.SettingChanged += (sender, args) =>
		{
			Log.LogInfo($"TestBool changed in FrooxEngine: {TestBool.Value}");
			Messaging.Send(TestBool);
		};

		TestFloat = Config.Bind("General", "TestFloat", 0f);
	}

	void FailHandler(Exception ex)
	{
		Log!.LogError("Exception in messaging system:\n" + ex);
	}

	void WarnHandler(string msg)
	{
		Log!.LogWarning(msg);
	}
	
	void DebugHandler(string msg)
	{
		Log!.LogDebug(msg);
	}

	void CommandHandler(RendererCommand command, int messageSize)
	{
		
	}

	public static void Test()
	{
		Messaging.Receive<int>("TestInt", (val) =>
		{
			Plugin.Log!.LogInfo($"Test: Got TestInt: {val}");
			Plugin.TestFloat!.Value = val;

			Messaging.Send("TestCommand");
		});
		Messaging.Receive("TestString", (str) =>
		{
			Plugin.Log!.LogInfo($"Test: Got TestString: {str}");
		});
		Messaging.Receive("TestCallback", () =>
		{
			Plugin.Log!.LogInfo($"Test: Got TestCallback.");
		});
	}
}

public static partial class Messaging
{
	public static void Send<T>(ConfigEntry<T> configEntry) where T : unmanaged
	{
		Send(configEntry.Definition.Key, configEntry.Value);
	}

	public static void Send(ConfigEntry<string> configEntry)
	{
		Send(configEntry.Definition.Key, configEntry.Value);
	}

	public static void Receive<T>(ConfigEntry<T> configEntry, Action<T> callback) where T : unmanaged
	{
		Receive(configEntry.Definition.Key, callback);
	}

	public static void Receive(ConfigEntry<string> configEntry, Action<string> callback)
	{
		Receive(configEntry.Definition.Key, callback);
	}
}
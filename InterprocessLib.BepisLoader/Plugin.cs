using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.NET.Common;
using BepInExResoniteShim;
using Elements.Core;
using FrooxEngine;
using Renderite.Shared;
using System.Reflection;

namespace InterprocessLib;

[ResonitePlugin(PluginMetadata.GUID, PluginMetadata.NAME, PluginMetadata.VERSION, PluginMetadata.AUTHORS, PluginMetadata.REPOSITORY_URL)]
[BepInDependency(BepInExResoniteShim.PluginMetadata.GUID, BepInDependency.DependencyFlags.HardDependency)]
internal class Plugin : BasePlugin
{
	public static new ManualLogSource? Log;
	public static ConfigEntry<bool>? TestBool;
	public static ConfigEntry<float>? TestFloat;

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

	void CommandHandler(RendererCommand command, int messageSize)
	{
		
	}

	static void Test()
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

	private static void FailHandler(Exception ex)
	{
		Plugin.Log!.LogError("Exception in messaging system:\n" + ex);
	}

	private static void WarnHandler(string msg)
	{
		Plugin.Log!.LogWarning(msg);
	}

	private static void DebugHandler(string msg)
	{
		Plugin.Log!.LogDebug(msg);
	}

	static Messaging()
	{
		if (Engine.Current?.RenderSystem is null)
			ThrowNotReady();

		var renderSystemMessagingHost = (RenderiteMessagingHost?)typeof(RenderSystem).GetField("_messagingHost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Engine.Current!.RenderSystem);

		if (renderSystemMessagingHost is null)
			throw new InvalidOperationException("Engine is not configured to use a renderer!");

		_host = new MessagingHost(true, renderSystemMessagingHost!.QueueName, renderSystemMessagingHost.QueueCapacity, renderSystemMessagingHost);
		_host.OnFailure = FailHandler;
		_host.OnWarning = WarnHandler;
		_host.OnDebug = DebugHandler;
		RunPostInit();
	}
}
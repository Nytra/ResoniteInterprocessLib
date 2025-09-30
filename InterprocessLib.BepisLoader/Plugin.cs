using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.NET.Common;
using BepInExResoniteShim;
using Elements.Core;
using FrooxEngine;
using System.Reflection;

namespace InterprocessLib;

[ResonitePlugin(PluginMetadata.GUID, PluginMetadata.NAME, PluginMetadata.VERSION, PluginMetadata.AUTHORS, PluginMetadata.REPOSITORY_URL)]
[BepInDependency(BepInExResoniteShim.PluginMetadata.GUID, BepInDependency.DependencyFlags.HardDependency)]
internal class Plugin : BasePlugin
{
	public static ManualLogSource? Logger;
	public static ConfigEntry<bool>? TestBool;
	public static ConfigEntry<float>? TestFloat;

	public override void Load()
	{
		Logger = base.Log;
		Logger.LogEvent += (sender, eventArgs) => 
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
			Messaging.OnFailure = FailHandler;
			Messaging.OnWarning = WarnHandler;
			Messaging.OnDebug = DebugHandler;

			Messaging.Init();

			Test();
		};

		TestBool = Config.Bind("General", "TestBool", false);
		TestBool.SettingChanged += (sender, args) =>
		{
			Logger.LogInfo($"TestBool changed in FrooxEngine: {TestBool.Value}");
			Messaging.Send(TestBool);
		};

		TestFloat = Config.Bind("General", "TestFloat", 0f);
	}

	private static void Test()
	{
		Messaging.Receive<int>("TestInt", (val) =>
		{
			Logger!.LogInfo($"Test: Got TestInt: {val}");
			TestFloat!.Value = val;

			Messaging.Send("TestCommand");
		});
		Messaging.Receive("TestString", (str) =>
		{
			Logger!.LogInfo($"Test: Got TestString: {str}");
		});
		Messaging.Receive("TestCallback", () =>
		{
			Logger!.LogInfo($"Test: Got TestCallback.");
		});
	}

	private static void FailHandler(Exception ex)
	{
		Logger!.LogError("Exception in messaging system:\n" + ex);
	}

	private static void WarnHandler(string msg)
	{
		Logger!.LogWarning(msg);
	}

	private static void DebugHandler(string msg)
	{
		Logger!.LogDebug(msg);
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

	internal static void Init()
	{
		if (_backend is not null) return;

		if (Engine.Current?.RenderSystem is null)
			ThrowNotReady();

		var renderSystemMessagingHost = (RenderiteMessagingHost?)typeof(RenderSystem).GetField("_messagingHost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Engine.Current!.RenderSystem);

		if (renderSystemMessagingHost is null)
			throw new InvalidOperationException("Engine is not configured to use a renderer!");

		_backend = new MessagingBackend(true, renderSystemMessagingHost!.QueueName, renderSystemMessagingHost.QueueCapacity, renderSystemMessagingHost);
		_backend.OnCommandReceived = OnCommandReceived;
		_backend.OnFailure = OnFailure;
		_backend.OnWarning = OnWarning;
		_backend.OnDebug = OnDebug;
		FinishInitialization();
	}
}
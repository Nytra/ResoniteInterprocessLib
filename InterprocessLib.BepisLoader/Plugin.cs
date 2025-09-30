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
	public static ConfigEntry<int>? TestInt;
	public static ConfigEntry<string>? TestString;
	public static ConfigEntry<int>? CallbackCount;
	private static Messenger? _messenger;

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
			Messenger.OnFailure = FailHandler;
			Messenger.OnWarning = WarnHandler;
			Messenger.OnDebug = DebugHandler;

			Messenger.Init();

			_messenger = new Messenger("Nytra");

			Test();
		};

		TestBool = Config.Bind("General", nameof(TestBool), false);
		TestInt = Config.Bind("General", nameof(TestInt), 0);
		TestString = Config.Bind("General", nameof(TestString), "Hello!");
		CallbackCount = Config.Bind("General", nameof(CallbackCount), 0);

		TestBool.SettingChanged += (sender, args) =>
		{
			_messenger!.Send(TestBool);
		};
	}

	private static void Test()
	{
		_messenger!.Receive<int>(nameof(TestInt), (val) =>
		{
			TestInt!.Value = val;
			_messenger.Send(nameof(TestString), Engine.VersionNumber ?? "");
		});
		_messenger.Receive(nameof(TestString), (str) =>
		{
			TestString!.Value = str;
			_messenger.Send("TestCallback");
		});
		_messenger.Receive("TestCallback", () =>
		{
			CallbackCount!.Value += 1;
		});
	}

	private static void FailHandler(Exception ex)
	{
		Logger!.LogError("Exception in InterprocessLib messaging host:\n" + ex);
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

public partial class Messenger
{
	public void Send<T>(ConfigEntry<T> configEntry) where T : unmanaged
	{
		Send(configEntry.Definition.Key, configEntry.Value);
	}

	public void Send(ConfigEntry<string> configEntry)
	{
		Send(configEntry.Definition.Key, configEntry.Value);
	}

	public void Receive<T>(ConfigEntry<T> configEntry, Action<T> callback) where T : unmanaged
	{
		Receive(configEntry.Definition.Key, callback);
	}

	public void Receive(ConfigEntry<string> configEntry, Action<string> callback)
	{
		Receive(configEntry.Definition.Key, callback);
	}

	internal static void Init()
	{
		if (IsInitialized) return;

		if (Engine.Current?.RenderSystem is null)
			ThrowNotReady();

		var renderSystemMessagingHost = (RenderiteMessagingHost?)typeof(RenderSystem).GetField("_messagingHost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Engine.Current!.RenderSystem);

		if (renderSystemMessagingHost is null)
			throw new InvalidOperationException("Engine is not configured to use a renderer!");

		_host = new MessagingHost(true, renderSystemMessagingHost!.QueueName, renderSystemMessagingHost.QueueCapacity, renderSystemMessagingHost);
		_host.OnCommandReceived = OnCommandReceived;
		_host.OnFailure = OnFailure;
		_host.OnWarning = OnWarning;
		_host.OnDebug = OnDebug;
		FinishInitialization();
	}
}
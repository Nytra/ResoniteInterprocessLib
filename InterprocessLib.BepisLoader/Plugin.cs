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
	private static bool _debugLoggingEnabled;

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
				case LogLevel.Debug:
					if (_debugLoggingEnabled)
					{
						UniLog.Log($"[{PluginMetadata.NAME}] [DEBUG] {eventArgs.Data}");
					}
					break;
				default:
					UniLog.Log($"[{PluginMetadata.NAME}] {eventArgs.Data}");
					break;
			}
		};

		foreach (var logListener in BepInEx.Logging.Logger.Listeners)
		{
			if (logListener.LogLevelFilter >= LogLevel.Debug)
			{
				Log.LogInfo("Debug logging is enabled");
				_debugLoggingEnabled = true;
				break;
			}
		}

		BepisResoniteWrapper.ResoniteHooks.OnEngineReady += () => 
		{
			Messenger.OnFailure = FailHandler;
			Messenger.OnWarning = WarnHandler;
			Messenger.OnDebug = DebugHandler;
			Messenger.OnCommandReceived = CommandHandler;
			Messenger.Init();
			Messenger.FinishInitialization();
		};
	}

	private static void FailHandler(Exception ex)
	{
		Log!.LogError("Exception in InterprocessLib messaging host:\n" + ex.ToString());
	}

	private static void WarnHandler(string msg)
	{
		Log!.LogWarning(msg);
	}

	private static void DebugHandler(string msg)
	{
		Log!.LogDebug(msg);
	}

	private static void CommandHandler(RendererCommand command, int messageSize)
	{

	}
}

public partial class Messenger
{
	private Dictionary<ConfigEntryBase, bool> _syncStates = new();

	public void SyncConfigEntry<T>(ConfigEntry<T> configEntry) where T : unmanaged
	{
		_syncStates[configEntry] = true;
		SendConfigEntry<T>(configEntry);
		configEntry.SettingChanged += (sender, args) =>
		{
			if (_syncStates.TryGetValue(configEntry, out bool value) && value == true)
				SendConfigEntry<T>(configEntry);
		};
		ReceiveConfigEntry<T>(configEntry);
	}

	public void SyncConfigEntry(ConfigEntry<string> configEntry)
	{
		_syncStates[configEntry] = true;
		SendConfigEntry(configEntry);
		configEntry.SettingChanged += (sender, args) =>
		{
			if (_syncStates.TryGetValue(configEntry, out bool value) && value == true)
				SendConfigEntry(configEntry);
		};
		ReceiveConfigEntry(configEntry);
	}

	public void SendConfigEntry<T>(ConfigEntry<T> configEntry) where T : unmanaged
	{
		SendValue(configEntry.Definition.Key, configEntry.Value);
	}

	public void SendConfigEntry(ConfigEntry<string> configEntry)
	{
		SendString(configEntry.Definition.Key, configEntry.Value);
	}

	public void ReceiveConfigEntry<T>(ConfigEntry<T> configEntry) where T : unmanaged
	{
		ReceiveValue<T>(configEntry.Definition.Key, (val) =>
		{
			_syncStates[configEntry] = false;
			configEntry.Value = val;
			_syncStates[configEntry] = true;
		});
	}

	public void ReceiveConfigEntry(ConfigEntry<string> configEntry)
	{
		ReceiveString(configEntry.Definition.Key, (str) =>
		{
			_syncStates[configEntry] = false;
			configEntry.Value = str!;
			_syncStates[configEntry] = true;
		});
	}

	internal static void Init()
	{
		if (IsInitialized) return;

		if (Engine.Current?.RenderSystem is null)
			throw new InvalidOperationException("Messenger is not ready to be used yet!");

		var renderSystemMessagingHost = (RenderiteMessagingHost?)typeof(RenderSystem).GetField("_messagingHost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Engine.Current!.RenderSystem);

		if (renderSystemMessagingHost is null)
			throw new InvalidOperationException("Engine is not configured to use a renderer!");

		Host = new MessagingHost(true, renderSystemMessagingHost!.QueueName, renderSystemMessagingHost.QueueCapacity, renderSystemMessagingHost);
		Host.OnCommandReceived = OnCommandReceived;
	}
}
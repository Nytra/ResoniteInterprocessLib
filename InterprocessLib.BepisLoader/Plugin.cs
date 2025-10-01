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
	private static bool _debugLoggingEnabled;

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
			if (logListener.LogLevelFilter == LogLevel.Debug)
			{
				_debugLoggingEnabled = true;
				break;
			}
		}

		BepisResoniteWrapper.ResoniteHooks.OnEngineReady += () => 
		{
			Messenger.OnFailure = FailHandler;
			Messenger.OnWarning = WarnHandler;
			Messenger.OnDebug = DebugHandler;
			Messenger.Init();
			Messenger.FinishInitialization();
		};
	}

	private static void FailHandler(Exception ex)
	{
		Logger!.LogError("Exception in InterprocessLib messaging host:\n" + ex.ToString());
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
			throw new InvalidOperationException("Messenger is not ready to be used yet!");

		var renderSystemMessagingHost = (RenderiteMessagingHost?)typeof(RenderSystem).GetField("_messagingHost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Engine.Current!.RenderSystem);

		if (renderSystemMessagingHost is null)
			throw new InvalidOperationException("Engine is not configured to use a renderer!");

		Host = new MessagingHost(true, renderSystemMessagingHost!.QueueName, renderSystemMessagingHost.QueueCapacity, renderSystemMessagingHost);
		Host.OnCommandReceived = OnCommandReceived;
		Host.OnFailure = OnFailure;
		Host.OnWarning = OnWarning;
		Host.OnDebug = OnDebug;
	}
}
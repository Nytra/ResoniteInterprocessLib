using BepInEx;
using BepInEx.Logging;
using BepInEx.NET.Common;
using BepInExResoniteShim;
using Elements.Core;
using Renderite.Shared;

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
using BepInEx;
using BepInEx.Logging;
using BepInEx.NET.Common;
using BepInExResoniteShim;
using Elements.Core;
using FrooxEngine;

namespace InterprocessLib;

[ResonitePlugin(PluginMetadata.GUID, PluginMetadata.NAME, PluginMetadata.VERSION, PluginMetadata.AUTHORS, PluginMetadata.REPOSITORY_URL)]
[BepInDependency(BepInExResoniteShim.PluginMetadata.GUID, BepInDependency.DependencyFlags.HardDependency)]
internal class BepisLoaderBootstrap : BasePlugin
{
	public static new ManualLogSource? Log;

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
					UniLog.Log($"[{PluginMetadata.NAME}] [DEBUG] {eventArgs.Data}");
					break;
				default:
					UniLog.Log($"[{PluginMetadata.NAME}] {eventArgs.Data}");
					break;
			}
		};

		if (Messenger.Host is null)
		{
			//Task.Run(PreInitLoop);
		}

		BepisResoniteWrapper.ResoniteHooks.OnEngineReady += () => 
		{ 
			if (Messenger.Host is null)
			{
				Init();
			}
		};
	}

	private static void Init()
	{
		Messenger.OnFailure = FailHandler;
		Messenger.OnWarning = WarnHandler;
#if DEBUG
			Messenger.OnDebug = DebugHandler;
#endif
		Log!.LogInfo("Messenger initialized.");
	}

	private static async void PreInitLoop()
	{
		var renderSystem = Engine.Current?.RenderSystem;
		if (renderSystem is null)
		{
			await Task.Delay(1);
			PreInitLoop();
		}
		else
		{
			await Task.Delay(1); // This delay is needed otherwise it doesn't work
			Init();
		}
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
}
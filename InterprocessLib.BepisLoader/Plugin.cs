using BepInEx;
using BepInEx.Logging;
using BepInEx.NET.Common;
using BepInExResoniteShim;
using Elements.Core;
using FrooxEngine;

namespace InterprocessLib;

[ResonitePlugin(PluginMetadata.GUID, PluginMetadata.NAME, PluginMetadata.VERSION, PluginMetadata.AUTHORS, PluginMetadata.REPOSITORY_URL)]
[BepInDependency(BepInExResoniteShim.PluginMetadata.GUID, BepInDependency.DependencyFlags.HardDependency)]
internal class Plugin : BasePlugin
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

		Messenger.OnFailure = FailHandler;
		Messenger.OnWarning = WarnHandler;

#if DEBUG
		Messenger.OnDebug = DebugHandler;
#endif

		BepisResoniteWrapper.ResoniteHooks.OnEngineReady += () => 
		{
			InitLoop();
		};
	}

	// Sometimes OnEngineReady is too early to initialize
	// FrameIndex = 120 is when the loading bar disappears
	private static void InitLoop()
	{
		var renderSystem = Engine.Current?.RenderSystem;
		if (renderSystem is null || (renderSystem.HasRenderer && renderSystem.FrameIndex < 120))
		{
			Engine.Current!.GlobalCoroutineManager.RunInUpdates(1, InitLoop);
		}
		else
		{
			Messenger.Init();
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
		Log!.LogInfo(msg);
	}
}
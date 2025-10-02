using BepInEx;
using BepInEx.Logging;
using BepInEx.NET.Common;
using BepInExResoniteShim;
using Elements.Core;
using FrooxEngine;
using HarmonyLib;
using Renderite.Shared;

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

		HarmonyInstance.PatchAll();

		Task.Run(PreInitLoop);

		Messenger.OnFailure = FailHandler;
		Messenger.OnWarning = WarnHandler;

		//Messenger.OnDebug = DebugHandler;

		BepisResoniteWrapper.ResoniteHooks.OnEngineReady += () => 
		{
			Messenger.FinishInitialization();
		};
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

[HarmonyPatch(typeof(PolymorphicMemoryPackableEntity<RendererCommand>), "InitTypes")]
class TypesPatch
{
	static bool Prefix(ref List<Type> types)
	{
		foreach(var type in TypeManager.NewTypes)
		{
			types.AddUnique(type);
		}
		return true;
	}
}
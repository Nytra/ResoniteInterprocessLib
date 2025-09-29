using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.NET.Common;
using BepInExResoniteShim;
using Elements.Core;
using FrooxEngine;
using HarmonyLib;
using Renderite.Shared;

namespace InterprocessLib.BepisLoader;

[ResonitePlugin(PluginMetadata.GUID, PluginMetadata.NAME, PluginMetadata.VERSION, PluginMetadata.AUTHORS, PluginMetadata.REPOSITORY_URL)]
[BepInDependency(BepInExResoniteShim.PluginMetadata.GUID, BepInDependency.DependencyFlags.HardDependency)]
public class Plugin : BasePlugin
{
	internal static new ManualLogSource? Log;
	internal static ConfigEntry<bool>? TestBool;
	internal static ConfigEntry<float>? TestFloat;
	public static MessagingHost? MessagingHost;

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
			var renderSystemMessagingHost = (RenderiteMessagingHost)AccessTools.Field(typeof(RenderSystem), "_messagingHost").GetValue(Engine.Current.RenderSystem)!;
			MessagingHost = new MessagingHost(true, renderSystemMessagingHost.QueueName, renderSystemMessagingHost.QueueCapacity, renderSystemMessagingHost);
			MessagingHost.OnCommandReceieved += CommandHandler;
			MessagingHost.OnFailure += FailHandler;
			MessagingHost.OnWarning += WarnHandler;
			MessagingHost.OnDebug += DebugHandler;

			Tests.Test();
		};

		TestBool = Config.Bind("General", "TestBool", false);
		TestBool.SettingChanged += (sender, args) =>
		{
			Log.LogInfo($"TestBool changed in FrooxEngine: {TestBool.Value}");
			var command = new ValueCommand<bool>();
			command.Id = "TestBool";
			command.Value = TestBool.Value;
			MessagingHost!.SendCommand(command);
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
}

class Tests
{
	public static void Test()
	{
		Plugin.MessagingHost!.RegisterValueCallback<float>("TestFloat", (val) =>
		{
			Plugin.Log!.LogInfo($"Test: Got TestFloat: {val}");
			Plugin.TestFloat!.Value = val;
			var send = new IdentifiableCommand();
			send.Id = "TestCommand";
			Plugin.MessagingHost!.SendCommand(send);
		});
		Plugin.MessagingHost!.RegisterStringCallback("TestString", (str) => 
		{
			Plugin.Log!.LogInfo($"Test: Got TestString: {str}");
		});
		Plugin.MessagingHost!.RegisterCallback("TestCallback", () =>
		{
			Plugin.Log!.LogInfo($"Test: Got TestCallback.");
		});
	}
}
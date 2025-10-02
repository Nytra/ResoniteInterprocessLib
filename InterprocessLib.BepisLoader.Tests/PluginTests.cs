//#define DEBUG

using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.NET.Common;
using Elements.Core;
using Renderite.Shared;

namespace InterprocessLib.Tests;

#if DEBUG

[BepInExResoniteShim.ResonitePlugin(PluginMetadata.GUID, PluginMetadata.NAME, PluginMetadata.VERSION, PluginMetadata.AUTHORS, PluginMetadata.REPOSITORY_URL)]
[BepInDependency(BepInExResoniteShim.PluginMetadata.GUID, BepInDependency.DependencyFlags.HardDependency)]
public class Plugin : BasePlugin
{
	public static new ManualLogSource? Log;
	public static ConfigEntry<bool>? RunTestsToggle;
	public static Messenger? _messenger;
	public static Messenger? _unknownMessenger;
	public static Messenger? _another;
	public static ConfigEntry<int>? SyncTest;
	public static ConfigEntry<bool>? CheckSyncToggle;
	public static ConfigEntry<int>? SyncTestOutput;
	public static ConfigEntry<bool>? ResetToggle;

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

		_messenger = new Messenger("InterprocessLib.Tests", [typeof(TestCommand), typeof(TestNestedPackable), typeof(TestPackable), typeof(RendererInitData)], [typeof(TestStruct), typeof(TestNestedStruct), typeof(HapticPointState), typeof(ShadowType)]);
		_another = new("InterprocessLib.Tests.Another", [typeof(TestCommand), typeof(TestNestedPackable), typeof(TestPackable), typeof(RendererInitData)], [typeof(TestStruct), typeof(TestNestedStruct), typeof(HapticPointState), typeof(ShadowType)]);
		_unknownMessenger = new Messenger("InterprocessLib.Tests.UnknownMessengerFrooxEngine");

		Tests.RunTests(_messenger, _unknownMessenger!, Log!.LogInfo);

		SyncTest = Config.Bind("General", "SyncTest", 34);
		_messenger.SyncConfigEntry(SyncTest);

		RunTestsToggle = Config.Bind("General", "RunTests", false);
		CheckSyncToggle = Config.Bind("General", "CheckSync", false);
		SyncTestOutput = Config.Bind("General", "SyncTestOutput", 0);
		ResetToggle = Config.Bind("General", "ResetToggle", false);
		RunTestsToggle!.SettingChanged += (sender, args) =>
		{
			_messenger!.SendEmptyCommand("RunTests");
			Tests.RunTests(_messenger, _unknownMessenger!, Log!.LogInfo);
		};
		CheckSyncToggle!.SettingChanged += (sender, args) =>
		{
			_messenger.SendEmptyCommand("CheckSync");
		};
		ResetToggle!.SettingChanged += (sender, args) => 
		{ 
			_messenger.SendEmptyCommand("Reset");
		};
		_messenger.ReceiveValue<int>("SyncTestOutput", (val) => 
		{ 
			SyncTestOutput!.Value = val;
		});
	}
}

#endif
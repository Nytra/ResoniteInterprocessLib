using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.NET.Common;
using Elements.Core;

namespace InterprocessLib.Tests;

[BepInExResoniteShim.ResonitePlugin(PluginMetadata.GUID, PluginMetadata.NAME, PluginMetadata.VERSION, PluginMetadata.AUTHORS, PluginMetadata.REPOSITORY_URL)]
[BepInDependency(BepInExResoniteShim.PluginMetadata.GUID, BepInDependency.DependencyFlags.HardDependency)]
internal class Plugin : BasePlugin
{
	public static new ManualLogSource? Log;
	public static ConfigEntry<bool>? RunTestsToggle;
	public static Messenger? _messenger;
	public static Messenger? _unknownMessenger;
	public static ConfigEntry<int>? SyncTest;
	public static ConfigEntry<bool>? CheckSyncToggle;
	public static ConfigEntry<int>? SyncTestOutput;

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

		_messenger = new Messenger("InterprocessLib.Tests", [typeof(TestCommand), typeof(TestNestedPackable), typeof(TestPackable)], [typeof(TestStruct), typeof(TestNestedStruct)]);
		_unknownMessenger = new Messenger("InterprocessLib.Tests.UnknownMessengerFrooxEngine");

		Tests.RunTests(_messenger, _unknownMessenger!, Log!.LogInfo);

		SyncTest = Config.Bind("General", "SyncTest", 34);
		_messenger.SyncConfigEntry(SyncTest);

		RunTestsToggle = Config.Bind("General", "RunTests", false);
		CheckSyncToggle = Config.Bind("General", "CheckSync", false);
		SyncTestOutput = Config.Bind("General", "SyncTestOutput", 0);
		RunTestsToggle!.SettingChanged += (sender, args) =>
		{
			_messenger!.SendEmptyCommand("RunTests");
			Tests.RunTests(_messenger, _unknownMessenger!, Log!.LogInfo);
		};
		CheckSyncToggle!.SettingChanged += (sender, args) =>
		{
			_messenger.SendEmptyCommand("CheckSync");
		};
		_messenger.ReceiveValue<int>("SyncTestOutput", (val) => 
		{ 
			SyncTestOutput!.Value = val;
		});
		
	}
}
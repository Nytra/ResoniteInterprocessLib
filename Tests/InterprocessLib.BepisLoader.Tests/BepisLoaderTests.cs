//#define TEST_SPAWN_PROCESS

using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.NET.Common;
using Elements.Core;
using Renderite.Shared;
using System.Diagnostics;

namespace InterprocessLib.Tests;

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
	public static ConfigEntry<double>? LatencyMilliseconds;

#if TEST_SPAWN_PROCESS
	public static Messenger? _customMessenger;
	public static ConfigEntry<bool>? SpawnProcessToggle;
	private static Random _rand = new();
	private static string? _customQueueName;
	private static Process? _customProcess;
#endif

	private static void CommandHandler(RendererCommand command, int messageSize)
	{
	}

	private static void FailHandler(Exception ex)
	{
		Log!.LogError($"[Child Process Messaging Host] Exception in custom messaging host: {ex}");
	}

	private static void WarnHandler(string msg)
	{
		Log!.LogWarning($"[Child Process Messaging Host] {msg}");
	}

	private static void DebugHandler(string msg)
	{
		Log!.LogDebug($"[Child Process Messaging Host] {msg}");
	}

#if TEST_SPAWN_PROCESS
	private static void SpawnProcess()
	{
		if (_customProcess is not null && !_customProcess.HasExited) return;
		_customQueueName ??= $"MyCustomQueue{_rand.Next()}";
		Log!.LogInfo("Child process queue name: " + _customQueueName);
		_customMessenger ??= new Messenger("InterprocessLib.Tests", true, _customQueueName, additionalObjectTypes: [typeof(TestCommand), typeof(TestNestedPackable), typeof(TestPackable), typeof(RendererInitData)], additionalValueTypes: [typeof(TestStruct), typeof(TestNestedStruct), typeof(HapticPointState), typeof(ShadowType)]);
		_customProcess = new Process();

		string projectConfiguration;
#if DEBUG
		projectConfiguration = "Debug";
#else
		projectConfiguration = "Release";
#endif

		_customProcess.StartInfo.FileName = @$"S:\Projects\ResoniteModDev\_THUNDERSTORE\InterprocessLib\Tests\InterprocessLib.Standalone.Tests\bin\{projectConfiguration}\net9.0\InterprocessLib.Standalone.Tests.exe";
		_customProcess.StartInfo.Arguments = _customQueueName;
		_customProcess.StartInfo.UseShellExecute = true; // Run in a new window
		_customProcess.StartInfo.WindowStyle = ProcessWindowStyle.Normal;
		_customProcess.Start();
		Tests.RunTests(_customMessenger, Log!.LogInfo);
	}
#endif

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

		Tests.RunTests(_messenger, Log!.LogInfo);
		Tests.RunTests(_unknownMessenger, Log!.LogInfo);
		Tests.RunTests(_another, Log!.LogInfo);

#if TEST_SPAWN_PROCESS
		SpawnProcess();
		SpawnProcessToggle = Config.Bind("General", "SpawnChildProcess", false);
		SpawnProcessToggle.SettingChanged += (sender, args) =>
		{
			SpawnProcess();
		};
#endif

		SyncTest = Config.Bind("General", "SyncTest", 34);
		_messenger.SyncConfigEntry(SyncTest);

		RunTestsToggle = Config.Bind("General", "RunTests", false);
		CheckSyncToggle = Config.Bind("General", "CheckSync", false);
		SyncTestOutput = Config.Bind("General", "SyncTestOutput", 0);
		ResetToggle = Config.Bind("General", "ResetToggle", false);
		LatencyMilliseconds = Config.Bind("General", "LatencyMilliseconds", -1.0);

		_messenger.CheckLatency(latency =>
		{
			LatencyMilliseconds.Value = latency.TotalMilliseconds;
		});

		RunTestsToggle!.SettingChanged += (sender, args) =>
		{
			_messenger!.SendEmptyCommand("RunTests");
			Tests.RunTests(_messenger, Log!.LogInfo);
			Tests.RunTests(_unknownMessenger, Log!.LogInfo);
			Tests.RunTests(_another, Log!.LogInfo);
			_messenger.CheckLatency(latency => 
			{ 
				LatencyMilliseconds.Value = latency.TotalMilliseconds;
			});
#if TEST_SPAWN_PROCESS
			if (_customMessenger is not null && _customProcess != null && !_customProcess.HasExited)
			{
				Tests.RunTests(_customMessenger, Log!.LogInfo);
			}
#endif
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
#define TEST_SPAWN_PROCESS

using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.NET.Common;
using Elements.Core;
using FrooxEngine;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace InterprocessLib.Tests;

[BepInExResoniteShim.ResonitePlugin(PluginMetadata.GUID, PluginMetadata.NAME, PluginMetadata.VERSION, PluginMetadata.AUTHORS, PluginMetadata.REPOSITORY_URL)]
[BepInDependency(BepInExResoniteShim.PluginMetadata.GUID, BepInDependency.DependencyFlags.HardDependency)]
public class Plugin : BasePlugin
{
	public static new ManualLogSource? Log;
	public static ConfigEntry<bool>? RunTestsToggle;
	public static Messenger? _messenger;
	
	public static ConfigEntry<int>? MyValue;
	public static ConfigEntry<bool>? CheckSyncToggle;
	public static ConfigEntry<int>? SyncTestOutput;
	public static ConfigEntry<bool>? ResetToggle;
	public static ConfigEntry<bool>? CheckLatencyToggle;
	public static ConfigEntry<double>? UnityLatencyMilliseconds;
	private static DateTime _lastPingTime;

#if TEST_SPAWN_PROCESS
	public static Messenger? _customMessenger;
	public static ConfigEntry<bool>? CreateCustomQueueToggle;
	public static ConfigEntry<DateTime>? LastCustomQueuePing;
	public static ConfigEntry<string>? CustomQueueName;
	private static Random _rand = new();
	private static string? _customQueueName;
#endif

#if TEST_SPAWN_PROCESS
	private static void SpawnProcess()
	{
		_customMessenger?.Dispose();
		_customQueueName = $"MyCustomQueue{_rand.Next()}";
		CustomQueueName!.Value = _customQueueName;
		Log!.LogInfo("Child process queue name: " + _customQueueName);
		_customMessenger = new Messenger("InterprocessLib.Tests", true, _customQueueName);
		_customMessenger.ReceiveEmptyCommand("Ping", () =>
		{
			LastCustomQueuePing!.Value = DateTime.Now;
			_customMessenger.SendEmptyCommand("Ping");
		});

		var customProcess = new Process();
		customProcess.StartInfo.FileName = "dotnet";
		customProcess.StartInfo.Arguments = $"/home/nytra/code/ResoniteInterprocessLib/Tests/InterprocessLib.Standalone.Tests/bin/Debug/net10.0/InterprocessLib.Standalone.Tests.dll {_customQueueName}";
		customProcess.StartInfo.RedirectStandardOutput = true;
		//psi.UseShellExecute = false;
		customProcess.OutputDataReceived += (sender, args) => Log.LogInfo($"Received from custom process: {args.Data}");
		customProcess.Start();
		customProcess.BeginOutputReadLine();
		
		Tests.RunTests(_customMessenger, Log!.LogInfo);
	}
#endif

	public override void Load()
	{
		Log = base.Log;

		Messenger.OnWarning += Log.LogWarning;
		Messenger.OnFailure += Log.LogError;
		Messenger.OnDebug += Log.LogDebug;

		_messenger = new Messenger("InterprocessLib.Tests");

		Tests.RunTests(_messenger, Log!.LogInfo);

#if TEST_SPAWN_PROCESS
		CreateCustomQueueToggle = Config.Bind("General", "SpawnChildProcess", false);
		CreateCustomQueueToggle.SettingChanged += (sender, args) =>
		{
			SpawnProcess();
		};
		LastCustomQueuePing = Config.Bind("General", "LastProcessHeartbeat", DateTime.MinValue);
		CustomQueueName = Config.Bind("General", "CustomQueueName", "");
		SpawnProcess();
		BepisResoniteWrapper.ResoniteHooks.OnEngineReady += () => Engine.Current.OnShutdown += () => _customMessenger?.Dispose();
#endif

		MyValue = Config.Bind("General", "SyncTest", 34);
		_messenger.SyncConfigEntry(MyValue);

		RunTestsToggle = Config.Bind("General", "RunTests", false);
		CheckSyncToggle = Config.Bind("General", "CheckSync", false);
		SyncTestOutput = Config.Bind("General", "SyncTestOutput", 0);
		ResetToggle = Config.Bind("General", "ResetToggle", false);
		UnityLatencyMilliseconds = Config.Bind("General", "LatencyMilliseconds", -1.0);
		CheckLatencyToggle = Config.Bind("General", "CheckLatencyToggle", false);

		_messenger.ReceiveEmptyCommand("Ping", () =>
		{
			UnityLatencyMilliseconds.Value = (DateTime.UtcNow - _lastPingTime).TotalMilliseconds;
		});

		RunTestsToggle!.SettingChanged += (sender, args) =>
		{
			_messenger!.SendEmptyCommand("RunTests");
			Tests.RunTests(_messenger, Log!.LogInfo);
		};
		CheckSyncToggle!.SettingChanged += (sender, args) =>
		{
			_messenger.SendEmptyCommand("CheckSync");
		};
		ResetToggle!.SettingChanged += (sender, args) => 
		{ 
			_messenger.SendEmptyCommand("Reset");
		};
		CheckLatencyToggle!.SettingChanged += (sender, args) =>
		{
			_lastPingTime = DateTime.UtcNow;
			_messenger.SendEmptyCommand("Ping");
		};
		_messenger.ReceiveValue<int>("SyncTestOutput", (val) => 
		{ 
			SyncTestOutput!.Value = val;
		});
	}
}
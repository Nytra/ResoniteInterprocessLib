//#define TEST_SPAWN_PROCESS

using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.NET.Common;
using Elements.Core;
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
	public static ConfigEntry<double>? LatencyMilliseconds;

#if TEST_SPAWN_PROCESS
	public static Messenger? _customMessenger;
	public static ConfigEntry<bool>? SpawnProcessToggle;
	public static ConfigEntry<DateTime>? LastProcessHeartbeat;
	private static Random _rand = new();
	private static string? _customQueueName;
	private static Process? _customProcess;
#endif

#if TEST_SPAWN_PROCESS
	private static void SpawnProcess()
	{
		_customProcess?.Kill();
		_customQueueName = $"MyCustomQueue{_rand.Next()}";
		Log!.LogInfo("Child process queue name: " + _customQueueName);
		_customMessenger = new Messenger("InterprocessLib.Tests", true, _customQueueName);
		_customMessenger!.ReceiveEmptyCommand("Heartbeat", () =>
		{
			LastProcessHeartbeat!.Value = DateTime.Now;
			_customMessenger.SendEmptyCommand("HeartbeatResponse");
		});
		_customProcess = new Process();

		string projectConfiguration;

#if DEBUG
		projectConfiguration = "Debug";
#else
		projectConfiguration = "Release";
#endif

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			_customProcess.StartInfo.FileName = @$"S:\Projects\ResoniteModDev\_THUNDERSTORE\InterprocessLib\Tests\InterprocessLib.Standalone.Tests\bin\{projectConfiguration}\net10.0\InterprocessLib.Standalone.Tests.exe";
		else
			_customProcess.StartInfo.FileName = @$"/home/nytra/code/ResoniteInterprocessLib/Tests/InterprocessLib.Standalone.Tests/bin/{projectConfiguration}/net10.0/InterprocessLib.Standalone.Tests";

		_customProcess.StartInfo.Arguments = $"{_customQueueName}";
		//_customProcess.StartInfo.UseShellExecute = true; // Run in a new window
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

		_messenger = new Messenger("InterprocessLib.Tests");

		Tests.RunTests(_messenger, Log!.LogInfo);

#if TEST_SPAWN_PROCESS
		SpawnProcess();
		SpawnProcessToggle = Config.Bind("General", "SpawnChildProcess", false);
		SpawnProcessToggle.SettingChanged += (sender, args) =>
		{
			SpawnProcess();
		};
		LastProcessHeartbeat = Config.Bind("General", "LastProcessHeartbeat", DateTime.MinValue);
#endif

		MyValue = Config.Bind("General", "SyncTest", 34);
		_messenger.SyncConfigEntry(MyValue);

		RunTestsToggle = Config.Bind("General", "RunTests", false);
		CheckSyncToggle = Config.Bind("General", "CheckSync", false);
		SyncTestOutput = Config.Bind("General", "SyncTestOutput", 0);
		ResetToggle = Config.Bind("General", "ResetToggle", false);
		LatencyMilliseconds = Config.Bind("General", "LatencyMilliseconds", -1.0);
		CheckLatencyToggle = Config.Bind("General", "CheckLatencyToggle", false);

		_messenger.ReceivePing((latency) =>
		{
			LatencyMilliseconds.Value = latency.TotalMilliseconds;
		});
		_messenger.SendPing();

		RunTestsToggle!.SettingChanged += (sender, args) =>
		{
			_messenger!.SendEmptyCommand("RunTests");
			Tests.RunTests(_messenger, Log!.LogInfo);

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
		CheckLatencyToggle!.SettingChanged += (sender, args) =>
		{
			_messenger.SendPing();
		};
		_messenger.ReceiveValue<int>("SyncTestOutput", (val) => 
		{ 
			SyncTestOutput!.Value = val;
		});
	}
}
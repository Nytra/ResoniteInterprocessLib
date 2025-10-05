#define TEST_SPAWN_PROCESS

using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.NET.Common;
using Elements.Core;
using Renderite.Shared;
using System.Diagnostics;

namespace InterprocessLib.Tests;

class MyPool : IMemoryPackerEntityPool
{
	T IMemoryPackerEntityPool.Borrow<T>()
	{
		return Pool<T>.Borrow();
	}

	void IMemoryPackerEntityPool.Return<T>(T value)
	{
		Pool<T>.ReturnCleaned(ref value);
	}
}

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

	private static MessagingBackend? _customBackend;
	public static ConfigEntry<bool>? SpawnProcessToggle;
	private static Random _rand = new();
#pragma warning disable CS0169 
	private static string? _customQueueName;
#pragma warning restore

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

	private static void SpawnProcess()
	{
#if TEST_SPAWN_PROCESS
		_customQueueName ??= $"MyCustomQueue{_rand.Next()}";
		Log!.LogInfo("Child process queue name: " + _customQueueName);
		_customBackend ??= new MessagingBackend(true, _customQueueName, 1024 * 1024, new MyPool(), CommandHandler, FailHandler, WarnHandler, DebugHandler);
		var customHostMessenger = new Messenger("InterprocessLib.Tests", _customBackend, [typeof(TestCommand), typeof(TestNestedPackable), typeof(TestPackable), typeof(RendererInitData)], [typeof(TestStruct), typeof(TestNestedStruct), typeof(HapticPointState), typeof(ShadowType)]);
		var process = new Process();
		process.StartInfo.FileName = @"S:\Projects\ResoniteModDev\_THUNDERSTORE\InterprocessLib\Tests\InterprocessLib.Standalone.Tests\bin\Release\net9.0\InterprocessLib.Standalone.Tests.exe";
		process.StartInfo.Arguments = _customQueueName;
		process.StartInfo.UseShellExecute = true; // Run in a new window
		process.StartInfo.WindowStyle = ProcessWindowStyle.Normal;
		process.Start();
		Tests.RunTests(customHostMessenger, Log!.LogInfo);
#endif
	}

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
		SpawnProcess();

		SyncTest = Config.Bind("General", "SyncTest", 34);
		_messenger.SyncConfigEntry(SyncTest);

		RunTestsToggle = Config.Bind("General", "RunTests", false);
		CheckSyncToggle = Config.Bind("General", "CheckSync", false);
		SyncTestOutput = Config.Bind("General", "SyncTestOutput", 0);
		ResetToggle = Config.Bind("General", "ResetToggle", false);
		SpawnProcessToggle = Config.Bind("General", "SpawnChildProcess", false);
		SpawnProcessToggle.SettingChanged += (sender, args) =>
		{
			SpawnProcess();
		};
		RunTestsToggle!.SettingChanged += (sender, args) =>
		{
			_messenger!.SendEmptyCommand("RunTests");
			Tests.RunTests(_messenger, Log!.LogInfo);
			Tests.RunTests(_unknownMessenger, Log!.LogInfo);
			Tests.RunTests(_another, Log!.LogInfo);
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
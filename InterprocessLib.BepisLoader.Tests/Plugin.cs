using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.NET.Common;
using Elements.Core;
using FrooxEngine;

namespace InterprocessLib.Tests;

[BepInExResoniteShim.ResonitePlugin(PluginMetadata.GUID, PluginMetadata.NAME, PluginMetadata.VERSION, PluginMetadata.AUTHORS, PluginMetadata.REPOSITORY_URL)]
[BepInDependency(BepInExResoniteShim.PluginMetadata.GUID, BepInDependency.DependencyFlags.HardDependency)]
internal class Plugin : BasePlugin
{
	public static ManualLogSource? Logger;
	public static ConfigEntry<bool>? TestBool;
	public static ConfigEntry<int>? TestInt;
	public static ConfigEntry<string>? TestString;
	public static ConfigEntry<int>? CallbackCount;
	private static Messenger? _messenger;

	public override void Load()
	{
		Logger = base.Log;
		Logger.LogEvent += (sender, eventArgs) => 
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

		_messenger = new Messenger("InterprocessLib.BepisLoader.Tests");
		Test();
	}

	private void Test()
	{
		TestBool = Config.Bind("General", nameof(TestBool), false);
		TestInt = Config.Bind("General", nameof(TestInt), 0);
		TestString = Config.Bind("General", nameof(TestString), "Hello!");
		CallbackCount = Config.Bind("General", nameof(CallbackCount), 0);
		TestBool!.SettingChanged += (sender, args) =>
		{
			_messenger!.Send("Test", TestBool.Value);
		};
		_messenger!.Receive<int>("Test", (val) =>
		{
			TestInt!.Value = val;
			_messenger.Send("Test", Engine.VersionNumber ?? "");
		});
		_messenger.Receive("Test", (str) =>
		{
			TestString!.Value = str;
			_messenger.Send("Test");
		});
		_messenger.Receive("Test", () =>
		{
			CallbackCount!.Value += 1;
		});
	}
}
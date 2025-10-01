using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.NET.Common;
using Elements.Core;
using FrooxEngine;
using Renderite.Shared;
using System.Data;

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

		_messenger = new Messenger("InterprocessLib.Tests", [typeof(TestCommand), typeof(TestObject), typeof(TestObject2)]);
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
			_messenger!.Send(TestBool);
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

		var cmd = new TestCommand();
		cmd.Value = 2932;
		cmd.Text = "Hello world";
		cmd.Time = DateTime.Now;

		_messenger.SendObject("TestCmd", cmd);

		_messenger.Send("NullStr", null);

		var testObj = new TestObject();
		testObj.Value = 0xF8;
		testObj.Obj = cmd;
		_messenger.SendObject("TestObj", testObj);

		_messenger.ReceiveObject<TestCommand>("TestCmd2", (cmd2) =>
		{
			Logger!.LogInfo($"TestCommand: {cmd2.Value} {cmd2.Text} {cmd2.Time}");
		});

		Log.LogInfo($"Hello!");

		_messenger.ReceiveObject<TestObject2>("TestObj2", (obj2) =>
		{
			Logger!.LogInfo($"TestObject2: {obj2.Value}");
		});
	} 
}

class TestCommand : RendererCommand
{
	public ulong Value;
	public string Text = "";
	public DateTime Time;
	public override void Pack(ref MemoryPacker packer)
	{
		packer.Write(Value);
		packer.Write(Text);
		packer.Write(Time);
	}

	public override void Unpack(ref MemoryUnpacker unpacker)
	{
		unpacker.Read(ref Value);
		unpacker.Read(ref Text);
		unpacker.Read(ref Time);
	}
}

class TestObject : IMemoryPackable
{
	public byte Value;
	public TestCommand Obj;

	public void Pack(ref MemoryPacker packer)
	{
		packer.Write(Value);
		packer.WriteObject(Obj);
	}

	public void Unpack(ref MemoryUnpacker unpacker)
	{
		unpacker.Read(ref Value);
		unpacker.ReadObject(ref Obj);
	}
}

class TestObject2 : IMemoryPackable
{
	public uint Value;

	public void Pack(ref MemoryPacker packer)
	{
		packer.Write(Value);
	}

	public void Unpack(ref MemoryUnpacker unpacker)
	{
		unpacker.Read(ref Value);
	}
}
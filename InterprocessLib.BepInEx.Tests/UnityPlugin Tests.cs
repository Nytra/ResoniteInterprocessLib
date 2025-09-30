using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Renderite.Shared;
using UnityEngine;

namespace InterprocessLib.Tests;

[BepInPlugin("Nytra.InterprocessLib.BepInEx.Tests", "InterprocessLib.BepInEx.Tests", "1.0.0")]
internal class UnityPlugin : BaseUnityPlugin
{
	public static ManualLogSource? Log;
	public static ConfigEntry<bool>? TestBool;
	private static Messenger? _messenger;

	void Awake()
	{
		Log = base.Logger;
		_messenger = new("InterprocessLib.Tests", [typeof(MyThing), typeof(TestObject)]);
		Test();
	}

	void Test()
	{
		TestBool = Config.Bind("General", "TestBool", false);
		_messenger!.Receive(TestBool, (val) =>
		{
			_messenger.Send("Test", Time.frameCount);
		});
		_messenger.Receive("Test", (str) =>
		{
			_messenger.Send("Test", str + UnityEngine.Random.value.ToString());
		});
		_messenger.Receive("Test", () => 
		{ 
			_messenger.Send("Test");

			var cmd = new MyThing();
			cmd.Value = 9999;
			cmd.Text = "HWowowow";
			cmd.Time = DateTime.MinValue;

			_messenger.SendObject("TestObject2", cmd);
		});
		_messenger.ReceiveObject<MyThing>("TestObject", (thing) => 
		{ 
			Log!.LogInfo($"MyThing: {thing.Value} {thing.Text} {thing.Time}");
		});
		_messenger.Receive("NullStr", (str) => { });

		_messenger.ReceiveObject<TestObject>("TestObj2", (obj) => 
		{ 
			Log!.LogInfo($"TestObject: {obj.Value} {obj.Obj.Value} {obj.Obj.Text} {obj.Obj.Time}");
		});
	}
}

class MyThing : RendererCommand
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
	public MyThing Obj;

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
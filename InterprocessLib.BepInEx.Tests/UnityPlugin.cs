using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
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
		_messenger = new("InterprocessLib.Tests");
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
		});
	}
}
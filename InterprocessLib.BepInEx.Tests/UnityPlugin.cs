using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace InterprocessLib.Tests;

[BepInPlugin("Nytra.InterprocessLib.BepInEx.Tests", "InterprocessLib.BepInEx.Tests", "1.0.0")]
internal class UnityPlugin : BaseUnityPlugin
{
	public static ManualLogSource? Log;
	private static Messenger? _messenger;

	void Awake()
	{
		Log = base.Logger;
		_messenger = new("InterprocessLib.Tests");
		Test();
	}

	void Test()
	{
		_messenger!.Receive<bool>("Test", (val) =>
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
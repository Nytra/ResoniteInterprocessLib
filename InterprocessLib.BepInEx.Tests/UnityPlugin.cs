using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Renderite.Unity;
using System.Reflection;
using UnityEngine;
using InterprocessLib;

namespace InterprocessLib.Tests;

[BepInPlugin("Nytra.InterprocessLibTest.BepInEx", "InterprocessLibTest.BepInEx", "1.0.0")]
internal class UnityPlugin : BaseUnityPlugin
{
	public static ManualLogSource? Log;
	private static Messenger? _messenger;

	void Awake()
	{
		Log = base.Logger;
		_messenger = new("Nytra");
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
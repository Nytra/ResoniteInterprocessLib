using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace InterprocessLib.Tests;

[BepInPlugin("Nytra.InterprocessLib.BepInEx.Tests", "InterprocessLib.BepInEx.Tests", "1.0.0")]
internal class UnityPlugin : BaseUnityPlugin
{
	public static ManualLogSource? Log;
	private static Messenger? _messenger;
	private static Messenger? _unknownMessenger;
	private static ConfigEntry<int>? SyncTest;

	void Awake()
	{
		Log = base.Logger;
		_messenger = new("InterprocessLib.Tests", [typeof(TestCommand), typeof(TestNestedPackable), typeof(TestPackable)], [typeof(TestStruct), typeof(TestNestedStruct)]);
		_unknownMessenger = new("InterprocessLib.Tests.UnknownMessengerUnity");
		SyncTest = Config.Bind("General", "SyncTest", 34);
		_messenger.SyncConfigEntry(SyncTest);
		_messenger!.ReceiveEmptyCommand("RunTests", () =>
		{
			Tests.RunTests(_messenger, _unknownMessenger!, Log!.LogInfo);
		});
		_messenger.ReceiveEmptyCommand("CheckSync", () => 
		{ 
			_messenger.SendValue<int>("SyncTestOutput", SyncTest.Value);
		});
		Tests.RunTests(_messenger, _unknownMessenger!, Log!.LogInfo);
	}
}
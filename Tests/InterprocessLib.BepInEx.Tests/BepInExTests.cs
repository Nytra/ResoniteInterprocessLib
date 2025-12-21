using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Renderite.Shared;

namespace InterprocessLib.Tests;

[BepInPlugin("Nytra.InterprocessLib.BepInEx.Tests", "InterprocessLib.BepInEx.Tests", "1.0.1")]
public class UnityPlugin : BaseUnityPlugin
{
	public static ManualLogSource? Log;
	private static Messenger? _messenger;
	private static Messenger? _unknownMessenger;
	private static Messenger? _testObsoleteConstructor;
	public static ConfigEntry<int>? SyncTest;

	void Awake()
	{
		Log = base.Logger;
		_messenger = new("InterprocessLib.Tests");
		_testObsoleteConstructor = new("InterprocessLib.Tests.ObsoleteConstructor", [], []);
		_unknownMessenger = new("InterprocessLib.Tests.UnknownMessengerUnity");
		SyncTest = Config.Bind("General", "SyncTest", 34);
		_messenger.SyncConfigEntry(SyncTest);
		_messenger!.ReceiveEmptyCommand("RunTests", () =>
		{
			Tests.RunTests(_messenger, Log!.LogInfo);
			Tests.RunTests(_unknownMessenger, Log!.LogInfo);
			Tests.RunTests(_testObsoleteConstructor, Log!.LogInfo);
		});
		_messenger.ReceiveEmptyCommand("CheckSync", () => 
		{ 
			_messenger.SendValue<int>("SyncTestOutput", SyncTest.Value);
		});
		_messenger.ReceiveEmptyCommand("Reset", () => 
		{ 
			SyncTest.Value = 0;
		});
		Tests.RunTests(_messenger, Log!.LogInfo);
		Tests.RunTests(_unknownMessenger, Log!.LogInfo);
		Tests.RunTests(_testObsoleteConstructor, Log!.LogInfo);
	}
}
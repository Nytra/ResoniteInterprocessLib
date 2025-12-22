//#define TEST_OBSOLETE_CONSTRUCTOR

using ResoniteModLoader;

namespace InterprocessLib.Tests;

public class RML_Tests : ResoniteMod
{
	public override string Name => "InterprocessLib.RML.Tests";

	public override string Author => "Nytra";

	public override string Version => "1.0.1";

	public override string Link => "https://github.com/Nytra/ResoniteInterprocessLib";

	[AutoRegisterConfigKey]
	private static ModConfigurationKey<bool> RunTestsToggle = new ModConfigurationKey<bool>("RunTestsToggle", "RunTestsToggle:", () => false);
	[AutoRegisterConfigKey]
	private static ModConfigurationKey<int> SyncTest = new ModConfigurationKey<int>("SyncTest", "CheckSyncToggle:", () => 0);
	[AutoRegisterConfigKey]
	private static ModConfigurationKey<bool> CheckSyncToggle = new ModConfigurationKey<bool>("CheckSyncToggle", "SyncTestOutput:", () => false);
	[AutoRegisterConfigKey]
	private static ModConfigurationKey<int> SyncTestOutput = new ModConfigurationKey<int>("SyncTestOutput", "SyncTestOutput:", () => 0);
	[AutoRegisterConfigKey]
	private static ModConfigurationKey<bool> ResetToggle = new ModConfigurationKey<bool>("ResetToggle", "ResetToggle:", () => false);
	[AutoRegisterConfigKey]
	private static ModConfigurationKey<double> SendLatencyMilliseconds = new ModConfigurationKey<double>("SendLatencyMilliseconds", "SendLatencyMilliseconds:", () => -1.0);
	[AutoRegisterConfigKey]
	private static ModConfigurationKey<double> RecvLatencyMilliseconds = new ModConfigurationKey<double>("RecvLatencyMilliseconds", "RecvLatencyMilliseconds:", () => -1.0);

	public static Messenger? _messenger;
	public static Messenger? _unknownMessenger;

#if TEST_OBSOLETE_CONSTRUCTOR
	public static Messenger? _testObsoleteConstructor;
#endif

	public override void OnEngineInit()
	{
		_messenger = new Messenger("InterprocessLib.Tests");
		_unknownMessenger = new Messenger("InterprocessLib.Tests.UnknownMessengerFrooxEngine");

#if TEST_OBSOLETE_CONSTRUCTOR
		_testObsoleteConstructor = new("InterprocessLib.Tests.ObsoleteConstructor", [], []);
#endif

		Tests.RunTests(_messenger, Msg);
		Tests.RunTests(_unknownMessenger, Msg);

#if TEST_OBSOLETE_CONSTRUCTOR
		Tests.RunTests(_testObsoleteConstructor, Msg);
#endif

		_messenger.CheckLatency((send, recv) =>
		{
			SendLatencyMilliseconds.Value = send.TotalMilliseconds;
			RecvLatencyMilliseconds.Value = recv.TotalMilliseconds;
		});

		_messenger.SyncConfigEntry(SyncTest);

		RunTestsToggle!.OnChanged += (object? newValue) =>
		{
			_messenger!.SendEmptyCommand("RunTests");
			Tests.RunTests(_messenger, Msg);
			Tests.RunTests(_unknownMessenger, Msg);

#if TEST_OBSOLETE_CONSTRUCTOR
			Tests.RunTests(_testObsoleteConstructor, Msg);
#endif
			_messenger.CheckLatency((send, recv) =>
			{
				SendLatencyMilliseconds.Value = send.TotalMilliseconds;
				RecvLatencyMilliseconds.Value = recv.TotalMilliseconds;
			});
		};
		CheckSyncToggle!.OnChanged += (object? newValue) =>
		{
			_messenger.SendEmptyCommand("CheckSync");
		};
		ResetToggle!.OnChanged += (object? newValue) =>
		{
			_messenger.SendEmptyCommand("Reset");
		};
		_messenger.ReceiveValue<int>("SyncTestOutput", (val) =>
		{
			SyncTestOutput!.Value = val;
		});
	}
}
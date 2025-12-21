using Renderite.Shared;
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
	private static ModConfigurationKey<double> RoundTripLatencyMilliseconds = new ModConfigurationKey<double>("RoundTripLatencyMilliseconds", "RoundTripLatencyMilliseconds:", () => -1.0);

	public static Messenger? _messenger;
	public static Messenger? _unknownMessenger;
	public static Messenger? _testObsoleteConstructor;

	public override void OnEngineInit()
	{
		_messenger = new Messenger("InterprocessLib.Tests");
		_testObsoleteConstructor = new("InterprocessLib.Tests.ObsoleteConstructor", [], []);
		_unknownMessenger = new Messenger("InterprocessLib.Tests.UnknownMessengerFrooxEngine");

#pragma warning disable CS8622
		Tests.RunTests(_messenger, Msg);
		Tests.RunTests(_unknownMessenger, Msg);
		Tests.RunTests(_testObsoleteConstructor, Msg);
#pragma warning restore CS8622

		_messenger.CheckLatency(latency => RoundTripLatencyMilliseconds!.Value = latency.TotalMilliseconds);

		_messenger.SyncConfigEntry(SyncTest);

		RunTestsToggle!.OnChanged += (object? newValue) =>
		{
			_messenger!.SendEmptyCommand("RunTests");
#pragma warning disable CS8622
			Tests.RunTests(_messenger, Msg);
			Tests.RunTests(_unknownMessenger, Msg);
			Tests.RunTests(_testObsoleteConstructor, Msg);
#pragma warning restore CS8622
			_messenger.CheckLatency(latency => RoundTripLatencyMilliseconds!.Value = latency.TotalMilliseconds);
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
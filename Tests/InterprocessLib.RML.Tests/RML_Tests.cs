using ResoniteModLoader;
using System.Runtime.CompilerServices;

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
	private static ModConfigurationKey<bool> CheckLatencyToggle = new ModConfigurationKey<bool>("CheckLatencyToggle", "CheckLatencyToggle:", () => false);
	[AutoRegisterConfigKey]
	private static ModConfigurationKey<double> LatencyMilliseconds = new ModConfigurationKey<double>("LatencyMilliseconds", "LatencyMilliseconds:", () => -1.0);

	public static Messenger? _messenger;
	private static DateTime _lastPingTime;

	public override void OnEngineInit()
	{
		_messenger = new Messenger("InterprocessLib.Tests");

		Tests.RunTests(_messenger, Msg);

		_messenger.ReceiveEmptyCommand("Ping", () =>
		{
			LatencyMilliseconds.Value = (DateTime.UtcNow - _lastPingTime).TotalMilliseconds;
		});
		_lastPingTime = DateTime.UtcNow;
		_messenger.SendEmptyCommand("Ping");

		_messenger.SyncConfigEntry(SyncTest);

		RunTestsToggle!.OnChanged += (object? newValue) =>
		{
			_messenger!.SendEmptyCommand("RunTests");
			Tests.RunTests(_messenger, Msg);
		};
		CheckSyncToggle!.OnChanged += (object? newValue) =>
		{
			_messenger.SendEmptyCommand("CheckSync");
		};
		ResetToggle!.OnChanged += (object? newValue) =>
		{
			_messenger.SendEmptyCommand("Reset");
		};
		CheckLatencyToggle.OnChanged += (object? newValue) =>
		{
			_lastPingTime = DateTime.UtcNow;
			_messenger.SendEmptyCommand("Ping");
		};
		_messenger.ReceiveValue<int>("SyncTestOutput", (val) =>
		{
			SyncTestOutput!.Value = val;
		});
	}
}
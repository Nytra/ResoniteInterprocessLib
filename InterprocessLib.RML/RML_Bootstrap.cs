using ResoniteModLoader;

namespace InterprocessLib;

//internal class RML_Bootstrap : ResoniteMod
//{
//	public override string Name => "InterprocessLib.RML";

//	public override string Author => "Nytra";

//	public override string Version => "1.0.1";

//	public override string Link => "https://github.com/Nytra/ResoniteInterprocessLib";

//	public override void OnEngineInit()
//	{
//		if (Messenger.Host is null)
//		{
//			Messenger.OnFailure = FailHandler;
//			Messenger.OnWarning = WarnHandler;
//#if DEBUG
//			Messenger.OnDebug = DebugHandler;
//#endif
//			FrooxEngineInit.Init();
//			Msg("Messenger initialized.");
//		}
//	}

//	private static void FailHandler(Exception ex)
//	{
//		Error("Exception in InterprocessLib messaging host:\n" + ex.ToString());
//	}

//	private static void WarnHandler(string msg)
//	{
//		Warn(msg);
//	}

//	private static void DebugHandler(string msg)
//	{
//		Debug(msg);
//	}
//}

public static class RML_Extensions
{
	private static Dictionary<ModConfigurationKey, bool> _syncStates = new();

	public static void SyncConfigEntry<T>(this Messenger messenger, ModConfigurationKey<T> configEntry) where T : unmanaged
	{
		_syncStates[configEntry] = true;
		if (Messenger.IsAuthority)
			messenger.SendConfigEntry<T>(configEntry);
		configEntry.OnChanged += (object? newValue) =>
		{
			if (_syncStates.TryGetValue(configEntry, out bool value) && value == true)
				messenger.SendConfigEntry<T>(configEntry);
		};
		messenger.ReceiveConfigEntry<T>(configEntry);
	}

	public static void SyncConfigEntry(this Messenger messenger, ModConfigurationKey<string> configEntry)
	{
		_syncStates[configEntry] = true;
		if (Messenger.IsAuthority)
			messenger.SendConfigEntry(configEntry);
		configEntry.OnChanged += (object? newValue) =>
		{
			if (_syncStates.TryGetValue(configEntry, out bool value) && value == true)
				messenger.SendConfigEntry(configEntry);
		};
		messenger.ReceiveConfigEntry(configEntry);
	}

	public static void SendConfigEntry<T>(this Messenger messenger, ModConfigurationKey<T> configEntry) where T : unmanaged
	{
		messenger.SendValue(configEntry.Name, configEntry.Value);
	}

	public static void SendConfigEntry(this Messenger messenger, ModConfigurationKey<string> configEntry)
	{
		messenger.SendString(configEntry.Name, configEntry.Value!);
	}

	public static void ReceiveConfigEntry<T>(this Messenger messenger, ModConfigurationKey<T> configEntry) where T : unmanaged
	{
		messenger.ReceiveValue<T>(configEntry.Name, (val) =>
		{
			_syncStates[configEntry] = false;
			configEntry.Value = val;
			_syncStates[configEntry] = true;
		});
	}

	public static void ReceiveConfigEntry(this Messenger messenger, ModConfigurationKey<string> configEntry)
	{
		messenger.ReceiveString(configEntry.Name, (str) =>
		{
			_syncStates[configEntry] = false;
			configEntry.Value = str!;
			_syncStates[configEntry] = true;
		});
	}
}
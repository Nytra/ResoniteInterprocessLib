using ResoniteModLoader;

namespace InterprocessLib;

public static class RML_Extensions
{
	private static Dictionary<ModConfigurationKey, bool> _syncStates = new();

	public static void SyncConfigEntry<T>(this Messenger messenger, ModConfigurationKey<T> configEntry) where T : unmanaged
	{
		messenger.ReceiveConfigEntry<T>(configEntry);
		_syncStates[configEntry] = true;
		if (messenger.IsAuthority == true)
			messenger.SendConfigEntry<T>(configEntry);
		configEntry.OnChanged += (object? newValue) =>
		{
			if (_syncStates.TryGetValue(configEntry, out bool value) && value == true)
				messenger.SendConfigEntry<T>(configEntry);
		};
	}

	public static void SyncConfigEntry(this Messenger messenger, ModConfigurationKey<string> configEntry)
	{
		messenger.ReceiveConfigEntry(configEntry);
		_syncStates[configEntry] = true;
		if (messenger.IsAuthority == true)
			messenger.SendConfigEntry(configEntry);
		configEntry.OnChanged += (object? newValue) =>
		{
			if (_syncStates.TryGetValue(configEntry, out bool value) && value == true)
				messenger.SendConfigEntry(configEntry);
		};
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
using BepInEx.Configuration;

namespace InterprocessLib;

public static class BepInExExtensions
{
	private static Dictionary<ConfigEntryBase, bool> _syncStates = new();

	public static void SyncConfigEntry<T>(this Messenger messenger, ConfigEntry<T> configEntry) where T : unmanaged
	{
		messenger.ReceiveConfigEntry<T>(configEntry);
		_syncStates[configEntry] = true;
		if (messenger.IsAuthority == true)
			messenger.SendConfigEntry<T>(configEntry);
		configEntry.SettingChanged += (sender, args) =>
		{
			if (_syncStates.TryGetValue(configEntry, out bool value) && value == true)
				messenger.SendConfigEntry<T>(configEntry);
		};
	}

	public static void SyncConfigEntry(this Messenger messenger, ConfigEntry<string> configEntry)
	{
		messenger.ReceiveConfigEntry(configEntry);
		_syncStates[configEntry] = true;
		if (messenger.IsAuthority == true)
			messenger.SendConfigEntry(configEntry);
		configEntry.SettingChanged += (sender, args) =>
		{
			if (_syncStates.TryGetValue(configEntry, out bool value) && value == true)
				messenger.SendConfigEntry(configEntry);
		};
	}

	public static void SendConfigEntry<T>(this Messenger messenger, ConfigEntry<T> configEntry) where T : unmanaged
	{
		messenger.SendValue(configEntry.Definition.Key, configEntry.Value);
	}

	public static void SendConfigEntry(this Messenger messenger, ConfigEntry<string> configEntry)
	{
		messenger.SendString(configEntry.Definition.Key, configEntry.Value);
	}

	public static void ReceiveConfigEntry<T>(this Messenger messenger, ConfigEntry<T> configEntry) where T : unmanaged
	{
		messenger.ReceiveValue<T>(configEntry.Definition.Key, (val) =>
		{
			_syncStates[configEntry] = false;
			configEntry.Value = val;
			_syncStates[configEntry] = true;
		});
	}

	public static void ReceiveConfigEntry(this Messenger messenger, ConfigEntry<string> configEntry)
	{
		messenger.ReceiveString(configEntry.Definition.Key, (str) =>
		{
			_syncStates[configEntry] = false;
			configEntry.Value = str!;
			_syncStates[configEntry] = true;
		});
	}
}
using BepInEx.Configuration;

namespace InterprocessLib;

public partial class Messenger
{
	private Dictionary<ConfigEntryBase, bool> _syncStates = new();

	public void SyncConfigEntry<T>(ConfigEntry<T> configEntry) where T : unmanaged
	{
		_syncStates[configEntry] = true;
		SendConfigEntry<T>(configEntry);
		configEntry.SettingChanged += (sender, args) =>
		{
			if (_syncStates.TryGetValue(configEntry, out bool value) && value == true)
				SendConfigEntry<T>(configEntry);
		};
		ReceiveConfigEntry<T>(configEntry);
	}

	public void SyncConfigEntry(ConfigEntry<string> configEntry)
	{
		_syncStates[configEntry] = true;
		SendConfigEntry(configEntry);
		configEntry.SettingChanged += (sender, args) =>
		{
			if (_syncStates.TryGetValue(configEntry, out bool value) && value == true)
				SendConfigEntry(configEntry);
		};
		ReceiveConfigEntry(configEntry);
	}

	public void SendConfigEntry<T>(ConfigEntry<T> configEntry) where T : unmanaged
	{
		SendValue(configEntry.Definition.Key, configEntry.Value);
	}

	public void SendConfigEntry(ConfigEntry<string> configEntry)
	{
		SendString(configEntry.Definition.Key, configEntry.Value);
	}

	public void ReceiveConfigEntry<T>(ConfigEntry<T> configEntry) where T : unmanaged
	{
		ReceiveValue<T>(configEntry.Definition.Key, (val) =>
		{
			_syncStates[configEntry] = false;
			configEntry.Value = val;
			_syncStates[configEntry] = true;
		});
	}

	public void ReceiveConfigEntry(ConfigEntry<string> configEntry)
	{
		ReceiveString(configEntry.Definition.Key, (str) =>
		{
			_syncStates[configEntry] = false;
			configEntry.Value = str!;
			_syncStates[configEntry] = true;
		});
	}
}
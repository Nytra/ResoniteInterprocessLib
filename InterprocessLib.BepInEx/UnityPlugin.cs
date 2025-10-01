using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Renderite.Shared;
using Renderite.Unity;
using System.Reflection;

namespace InterprocessLib;

[BepInPlugin("Nytra.InterprocessLib.BepInEx", "InterprocessLib.BepInEx", "1.0.0")]
internal class UnityPlugin : BaseUnityPlugin
{
	public static ManualLogSource? Log;
	private static bool _initialized;

	void Awake()
	{
		Log = base.Logger;
		Update();
	}

	void Update()
	{
		if (_initialized) return;

		if (RenderingManager.Instance is null) return;

		Messenger.OnWarning = WarnHandler;
		Messenger.OnFailure = FailHandler;
		Messenger.OnDebug = DebugHandler;
		Messenger.OnCommandReceived = CommandHandler;

		Messenger.Init();

		_initialized = true;
	}

	private static void FailHandler(Exception ex)
	{
		Log!.LogError("Exception in InterprocessLib messaging host:\n" + ex.ToString());
	}

	private static void WarnHandler(string msg)
	{
		Log!.LogWarning(msg);
	}

	private static void DebugHandler(string msg)
	{
		Log!.LogDebug(msg);
	}

	private static void CommandHandler(RendererCommand command, int messageSize)
	{
		if (command is MessengerReadyCommand && !Messenger.IsInitialized)
		{
			Messenger.FinishInitialization();
			Messenger.OnCommandReceived = null;
		}
	}
}

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

	internal static void Init()
	{
		if (RenderingManager.Instance is null)
			throw new InvalidOperationException("Messenger is not ready to be used yet!");

		var getConnectionParametersMethod = typeof(RenderingManager).GetMethod("GetConnectionParameters", BindingFlags.Instance | BindingFlags.NonPublic);

		object[] parameters = { "", 0L };

		if (!(bool)getConnectionParametersMethod.Invoke(RenderingManager.Instance, parameters))
		{
			throw new ArgumentException("Could not get connection parameters from RenderingManager!");
		}

		Host = new(false, (string)parameters[0], (long)parameters[1], PackerMemoryPool.Instance);
		Host.OnCommandReceived = OnCommandReceived;
	}
}
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Renderite.Unity;
using System.Reflection;
using UnityEngine;

namespace InterprocessLib;

[BepInPlugin("Nytra.InterprocessLib.BepInEx", "InterprocessLib.BepInEx", "1.0.0")]
internal class UnityPlugin : BaseUnityPlugin
{
	public static ManualLogSource? Log;
	private static bool _initialized;
	private static Messenger? _messenger;

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

		Messenger.Init();

		_messenger = new("InterprocessLib");

		_initialized = true;

		Test();
	}

	void Test()
	{
		_messenger!.Receive<bool>("Test", (val) =>
		{
			_messenger.Send("Test", Time.frameCount);
		});
		_messenger.Receive("Test", (str) =>
		{
			_messenger.Send("Test", str + UnityEngine.Random.value.ToString());
		});
		_messenger.Receive("Test", () => 
		{ 
			_messenger.Send("Test");
		});
	}

	private static void FailHandler(Exception ex)
	{
		Log!.LogError("Exception in InterprocessLib messaging host:\n" + ex);
	}

	private static void WarnHandler(string msg)
	{
		Log!.LogWarning(msg);
	}

	private static void DebugHandler(string msg)
	{
		Log!.LogDebug(msg);
	}
}

public partial class Messenger
{
	public void Send<T>(ConfigEntry<T> configEntry) where T : unmanaged
	{
		Send(configEntry.Definition.Key, configEntry.Value);
	}

	public void Send(ConfigEntry<string> configEntry)
	{
		Send(configEntry.Definition.Key, configEntry.Value);
	}

	public void Receive<T>(ConfigEntry<T> configEntry, Action<T> callback) where T : unmanaged
	{
		Receive(configEntry.Definition.Key, callback);
	}

	public void Receive(ConfigEntry<string> configEntry, Action<string> callback)
	{
		Receive(configEntry.Definition.Key, callback);
	}

	internal static void Init()
	{
		if (RenderingManager.Instance is null)
			ThrowNotReady();

		var getConnectionParametersMethod = typeof(RenderingManager).GetMethod("GetConnectionParameters", BindingFlags.Instance | BindingFlags.NonPublic);

		object[] parameters = { "", 0L };

		if (!(bool)getConnectionParametersMethod.Invoke(RenderingManager.Instance, parameters))
		{
			throw new ArgumentException("Could not get connection parameters from RenderingManager!");
		}

		_host = new(false, (string)parameters[0], (long)parameters[1], PackerMemoryPool.Instance);
		_host.OnCommandReceived = OnCommandReceived;
		_host.OnFailure = OnFailure;
		_host.OnWarning = OnWarning;
		_host.OnDebug = OnDebug;
		FinishInitialization();
	}
}
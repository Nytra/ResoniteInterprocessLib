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

	void Awake()
	{
		Log = base.Logger;
		Update();
	}

	void Update()
	{
		if (_initialized) return;

		if (RenderingManager.Instance is null) return;

		Messaging.OnWarning = WarnHandler;
		Messaging.OnFailure = FailHandler;
		Messaging.OnDebug = DebugHandler;

		Messaging.Init();

		_initialized = true;

		Test();
	}

	static void Test()
	{
		Messaging.Receive<bool>("TestBool", (val) =>
		{
			Log!.LogInfo($"Unity got TestBool: {val}");

			Messaging.Send("TestInt", Time.frameCount);
		});
		Messaging.Receive("TestCommand", () =>
		{
			Log!.LogInfo($"Unity got TestCommand");

			Messaging.Send("TestCallback");
		});
	}

	private static void FailHandler(Exception ex)
	{
		Log!.LogError("Exception in messaging system:\n" + ex);
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

public static partial class Messaging
{
	public static void Send<T>(ConfigEntry<T> configEntry) where T : unmanaged
	{
		Send(configEntry.Definition.Key, configEntry.Value);
	}

	public static void Send(ConfigEntry<string> configEntry)
	{
		Send(configEntry.Definition.Key, configEntry.Value);
	}

	public static void Receive<T>(ConfigEntry<T> configEntry, Action<T> callback) where T : unmanaged
	{
		Receive(configEntry.Definition.Key, callback);
	}

	public static void Receive(ConfigEntry<string> configEntry, Action<string> callback)
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

		_backend = new(false, (string)parameters[0], (long)parameters[1], PackerMemoryPool.Instance);
		_backend.OnCommandReceived = OnCommandReceived;
		_backend.OnFailure = OnFailure;
		_backend.OnWarning = OnWarning;
		_backend.OnDebug = OnDebug;
		FinishInitialization();
	}
}
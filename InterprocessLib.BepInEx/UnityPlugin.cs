using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Renderite.Shared;
using Renderite.Unity;
using UnityEngine;

namespace InterprocessLib;

[BepInPlugin("Nytra.InterprocessLib.BepInEx", "InterprocessLib.BepInEx", "1.0.0")]
public class Plugin : BaseUnityPlugin
{
	internal static ManualLogSource? Log;
	private static bool _initialized;

	void Awake()
	{
		Log = base.Logger;
	}

	void Update()
	{
		if (_initialized) return;

		if (RenderingManager.Instance is null) return;

		Messaging.OnCommandReceived += CommandHandler;
		Messaging.Host.OnFailure = FailHandler;
		Messaging.Host.OnWarning = WarnHandler;
		Messaging.Host.OnDebug = DebugHandler;

		_initialized = true;

		Test();
	}

	void FailHandler(Exception ex)
	{
		Log!.LogError("Exception in messaging system:\n" + ex);
	}

	void WarnHandler(string msg)
	{
		Log!.LogWarning(msg);
	}

	void DebugHandler(string msg)
	{
		Log!.LogDebug(msg);
	}

	void CommandHandler(RendererCommand command, int messageSize)
	{

	}

	public static void Test()
	{
		Messaging.Receive<bool>("TestBool", (val) =>
		{
			Plugin.Log!.LogInfo($"Unity got TestBool: {val}");

			Messaging.Send("TestInt", Time.frameCount);
		});
		Messaging.Receive("TestCommand", () =>
		{
			Plugin.Log!.LogInfo($"Unity got TestCommand");

			Messaging.Send("TestCallback");
		});
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
}
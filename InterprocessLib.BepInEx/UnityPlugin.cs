using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Renderite.Shared;
using Renderite.Unity;
using System.Reflection;
using UnityEngine;

namespace InterprocessLib;

[BepInPlugin("Nytra.InterprocessLib.BepInEx", "InterprocessLib.BepInEx", "1.0.0")]
internal class Plugin : BaseUnityPlugin
{
	public static ManualLogSource? Log;
	private static bool _initialized;

	void Awake()
	{
		Log = base.Logger;
	}

	void Update()
	{
		if (_initialized) return;

		if (RenderingManager.Instance is null) return;

		Messaging.Init();

		Messaging.OnCommandReceived += CommandHandler;

		_initialized = true;

		Test();
	}

	void CommandHandler(RendererCommand command, int messageSize)
	{

	}

	static void Test()
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

public class Messaging : MessagingBase
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

	private static void FailHandler(Exception ex)
	{
		Plugin.Log!.LogError("Exception in messaging system:\n" + ex);
	}

	private static void WarnHandler(string msg)
	{
		Plugin.Log!.LogWarning(msg);
	}

	private static void DebugHandler(string msg)
	{
		Plugin.Log!.LogDebug(msg);
	}

	internal static void Init()
	{
		if (_host is not null) return;

		if (RenderingManager.Instance is null)
			ThrowNotReady();

		var getConnectionParametersMethod = typeof(RenderingManager).GetMethod("GetConnectionParameters", BindingFlags.Instance | BindingFlags.NonPublic);

		object[] parameters = { "", 0L };

		if (!(bool)getConnectionParametersMethod.Invoke(RenderingManager.Instance, parameters))
		{
			throw new ArgumentException("Could not get connection parameters from RenderingManager!");
		}

		_host = new(false, (string)parameters[0], (long)parameters[1], PackerMemoryPool.Instance);
		_host._onFailure = FailHandler;
		_host._onWarning = WarnHandler;
		_host._onDebug = DebugHandler;
		RunPostInit();
	}

	static Messaging()
	{
		if (_host is null)
			Init();
	}
}
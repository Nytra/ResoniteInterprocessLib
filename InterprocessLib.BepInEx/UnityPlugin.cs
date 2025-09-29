using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Renderite.Unity;
using UnityEngine;
using Renderite.Shared;

namespace InterprocessLib.BepInEx;

[BepInPlugin("Nytra.InterprocessLib.BepInEx", "InterprocessLib.BepInEx", "1.0.0")]
public class Plugin : BaseUnityPlugin
{
	internal static ManualLogSource? Log;
	private static bool _initialized;
	public static MessagingHost? MessagingHost;

	void Awake()
	{
		Log = base.Logger;
	}

	void Update()
	{
		if (_initialized) return;

		if (RenderingManager.Instance is null) return;

		if (PackerMemoryPool.Instance is null) return;

		var getConnectionParametersMethod = AccessTools.Method(typeof(RenderingManager), "GetConnectionParameters");

		object[] parameters = { "", 0L };

		if (!(bool)getConnectionParametersMethod.Invoke(RenderingManager.Instance, parameters))
		{
			throw new Exception("Could not get connection parameters for mod IPC!");
		}

		MessagingHost = new(false, (string)parameters[0], (long)parameters[1], PackerMemoryPool.Instance);
		MessagingHost.OnCommandReceieved += CommandHandler;
		MessagingHost.OnFailure += FailHandler;
		MessagingHost.OnWarning += WarnHandler;
		MessagingHost.OnDebug += DebugHandler;

		_initialized = true;

		Tests.Test();
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
}

class Tests
{
	public static void Test()
	{
		Plugin.MessagingHost!.RegisterValueCallback<bool>("TestBool", (val) =>
		{
			Plugin.Log!.LogInfo($"Unity got TestBool: {val}");

			var response = new ValueCommand<float>();
			response.Id = "TestFloat";
			response.Value = Time.frameCount;
			Plugin.MessagingHost!.SendCommand(response);
		});
		Plugin.MessagingHost!.RegisterCallback("TestCommand", () => 
		{
			Plugin.Log!.LogInfo($"Unity got TestCommand");

			var response = new Command();
			response.Id = "TestCallback";
			Plugin.MessagingHost!.SendCommand(response);
		});
	}
}
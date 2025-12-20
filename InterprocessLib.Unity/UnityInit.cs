using System.Reflection;
using Renderite.Shared;
using Renderite.Unity;

#if DEBUG
using UnityEngine;
#endif

namespace InterprocessLib;

internal static class UnityInit
{
	public static void Init()
	{
		if (Messenger.DefaultInitStarted)
			throw new InvalidOperationException("Messenger default host initialization has already been started!");

		Messenger.DefaultInitStarted = true;

		//Task.Run(InitLoop);
		InitLoop();
	}
	private static async void InitLoop()
	{
		Messenger.OnWarning = (msg) =>
		{
			UnityEngine.Debug.LogWarning($"[InterprocessLib] [WARN] {msg}");
		};
		Messenger.OnFailure = (ex) =>
		{
			UnityEngine.Debug.LogError($"[InterprocessLib] [ERROR] Error in InterprocessLib Messaging Host!\n{ex}");
		};
#if DEBUG
			Messenger.OnDebug = (msg) => 
			{
				UnityEngine.Debug.Log($"[InterprocessLib] [DEBUG] {msg}");
			};
#endif

		//UnityEngine.Debug.Log("Init");

		var args = Environment.GetCommandLineArgs();
		string? fullQueueName = null;
		for (int i = 0; i < args.Length; i++)
		{
			if (args[i].EndsWith("QueueName", StringComparison.InvariantCultureIgnoreCase))
			{
				fullQueueName = args[i + 1];
				break;
			}
		}

		MessagingSystem? system = null;

		var engineSharedMemoryPrefix = fullQueueName?.Substring(0, fullQueueName.IndexOf('_'));
		if (fullQueueName is null || engineSharedMemoryPrefix!.Length == 0)
		{
			var fallbackTask = Messenger.GetFallbackSystem(false, MessagingManager.DEFAULT_CAPACITY, PackerMemoryPool.Instance, null, Messenger.OnFailure, Messenger.OnWarning, Messenger.OnDebug);
			fallbackTask.Wait();
			system = fallbackTask.Result;
			if (system is null)
				throw new EntryPointNotFoundException("Unable to get fallback messaging system!");
		}
		else
		{
			
			system = new MessagingSystem(false, $"InterprocessLib-{engineSharedMemoryPrefix}", MessagingManager.DEFAULT_CAPACITY, PackerMemoryPool.Instance, null, Messenger.OnFailure, Messenger.OnWarning, Messenger.OnDebug);
			system.Connect();
		}

		lock (Messenger.LockObj)
		{
			Messenger.PreInit(system);
			Messenger.SetDefaultSystem(system);
			system.Initialize();
		}

		// while (RenderingManager.Instance is null)
		// 	await Task.Delay(1);

		// var initFinalizedField = typeof(RenderingManager).GetField("_initFinalized", BindingFlags.Instance | BindingFlags.NonPublic);

		// while ((bool)initFinalizedField.GetValue(RenderingManager.Instance) != true)
		// 	await Task.Delay(1);

		//UnityEngine.Debug.Log("DONE");
	}
}
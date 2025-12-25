using Renderite.Shared;
using Renderite.Unity;
using UnityEngine;

namespace InterprocessLib;

internal static class Initializer
{
	public static void Init()
	{
		Messenger.OnWarning += (msg) =>
		{
			Debug.LogWarning($"[InterprocessLib] [WARN] {msg}");
		};
		Messenger.OnFailure += (ex) =>
		{
			Debug.LogError($"[InterprocessLib] [ERROR] {ex}");
		};
#if DEBUG
		Messenger.OnDebug += (msg) => 
		{
			Debug.Log($"[InterprocessLib] [DEBUG] {msg}");
		};
#endif

		var args = Environment.GetCommandLineArgs();
		string? fullQueueName = null;
		for (int i = 0; i < args.Length; i++)
		{
			if (args[i].Equals("-QueueName", StringComparison.InvariantCultureIgnoreCase))
			{
				fullQueueName = args[i + 1];
				break;
			}
		}

		MessagingSystem? system = null;

		var engineSharedMemoryPrefix = fullQueueName?.Substring(0, fullQueueName.IndexOf('_'));
		if (fullQueueName is null || engineSharedMemoryPrefix!.Length == 0)
		{
			throw new InvalidDataException("Could not get default Unity queue name!");
		}
		else
		{
			system = new MessagingSystem(false, $"InterprocessLib-{engineSharedMemoryPrefix}", MessagingManager.DEFAULT_CAPACITY, PackerMemoryPool.Instance, Messenger.FailHandler, Messenger.WarnHandler, Messenger.DebugHandler);
		}

		Messenger.InitializeDefaultSystem(system);
	}
}
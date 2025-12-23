using Elements.Core;
using Renderite.Shared;

namespace InterprocessLib;

internal class FrooxEnginePool : IMemoryPackerEntityPool
{
	public static readonly FrooxEnginePool Instance = new();

	T IMemoryPackerEntityPool.Borrow<T>()
	{
		return Pool<T>.Borrow();
	}

	void IMemoryPackerEntityPool.Return<T>(T value)
	{
		Pool<T>.ReturnCleaned(ref value);
	}
}

internal static class Initializer
{
	public static void Init()
	{
		var args = Environment.GetCommandLineArgs();
		string? queueName = null;
		for (int i = 0; i < args.Length; i++)
		{
			if (args[i].Equals("-shmprefix", StringComparison.InvariantCultureIgnoreCase))
			{
				queueName = args[i + 1];
				break;
			}
		}

		Messenger.OnWarning = (msg) =>
		{
			UniLog.Warning($"[InterprocessLib] [WARN] {msg}");
		};
		Messenger.OnFailure = (ex) =>
		{
			UniLog.Error($"[InterprocessLib] [ERROR] Error in InterprocessLib Messaging Backend!\n{ex}");
		};
#if DEBUG
		Messenger.OnDebug = (msg) => 
		{
			UniLog.Log($"[InterprocessLib] [DEBUG] {msg}");
		};
#endif

		MessagingSystem? system = null;

		// If the queue name is null then the engine doesn't have a renderer, such as when it's a headless
		if (queueName is null)
		{
			Messenger.OnWarning?.Invoke("Default shared memory queue name is null! This can happen on headless. Attempting to use fallback...");
			var task = Messenger.GetFallbackSystem("Fallback", true, MessagingManager.DEFAULT_CAPACITY, FrooxEnginePool.Instance, Messenger.OnFailure, Messenger.OnWarning, Messenger.OnDebug);
			task.Wait();
			system = task.Result;
			if (system is null)
			{
				throw new EntryPointNotFoundException("Unable to get fallback messaging system!");
			}
		}
		else
		{
			system = new MessagingSystem(true, $"InterprocessLib-{queueName}", MessagingManager.DEFAULT_CAPACITY, FrooxEnginePool.Instance, Messenger.OnFailure, Messenger.OnWarning, Messenger.OnDebug);
			system.Connect();
		}

		lock (Messenger.LockObj)
		{
			Messenger.PreInit(system);
			Messenger.SetDefaultSystem(system);
			system.Initialize();
		}
		
		//Engine.Current.OnShutdown += system.Dispose;
	}
}
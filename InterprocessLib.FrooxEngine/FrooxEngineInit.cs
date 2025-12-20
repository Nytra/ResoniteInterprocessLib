using Elements.Core;
using FrooxEngine;
using Renderite.Shared;
using System.Reflection;

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

internal static class FrooxEngineInit
{
	public static void Init()
	{
		if (Messenger.DefaultInitStarted)
			throw new InvalidOperationException("Messenger default backend initialization has already been started!");

		Messenger.DefaultInitStarted = true;

		InnerInit();
	}
	private static void InnerInit()
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

		if (queueName is null)
		{
			Messenger.OnDebug?.Invoke("Shared memory unique id is null! Attempting to use fallback...");
			var task = Messenger.GetFallbackSystem(true, MessagingManager.DEFAULT_CAPACITY, FrooxEnginePool.Instance, null, Messenger.OnFailure, Messenger.OnWarning, Messenger.OnDebug);
			task.Wait();
			system = task.Result;
			if (system is null)
			{
				throw new EntryPointNotFoundException("Unable to get fallback messaging system!");
			}
		}
		else
		{
			system = new MessagingSystem(true, $"InterprocessLib-{queueName}", MessagingManager.DEFAULT_CAPACITY, FrooxEnginePool.Instance, null, Messenger.OnFailure, Messenger.OnWarning, Messenger.OnDebug);
			system.Connect();
		}

		lock (Messenger.LockObj)
		{
			Messenger.PreInit(system);
			Messenger.SetDefaultSystem(system);
			system.Initialize();
		}
		
		//Engine.Current.OnShutdown += system.Dispose; // this might fix the rare occurence that Renderite.Host stays open after exiting Resonite
	}
}
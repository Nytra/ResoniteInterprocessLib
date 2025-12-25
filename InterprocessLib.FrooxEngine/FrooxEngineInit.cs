using Elements.Core;
using FrooxEngine;
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
	public static async void Init()
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

		Messenger.OnWarning += (msg) =>
		{
			UniLog.Warning($"[InterprocessLib] [WARN] {msg}");
		};
		Messenger.OnFailure += (ex) =>
		{
			UniLog.Error($"[InterprocessLib] [ERROR] {ex}");
		};
#if DEBUG
		Messenger.OnDebug += (msg) => 
		{
			UniLog.Log($"[InterprocessLib] [DEBUG] {msg}");
		};
#endif

		MessagingSystem system;

		if (queueName is null)
		{
			throw new InvalidDataException("Could not get default FrooxEngine queue name! If this is a headless, you need to use the other Messenger constructor to manually specify a queue name.");
		}
		else
		{
			system = new MessagingSystem(true, $"InterprocessLib-{queueName}", MessagingManager.DEFAULT_CAPACITY, FrooxEnginePool.Instance, Messenger.FailHandler, Messenger.WarnHandler, Messenger.DebugHandler);
			system.Connect();
		}

		Messenger.InitializeDefaultSystem(system);
		
		while (Engine.Current is null)
			await Task.Delay(1);

		Engine.Current.OnShutdown += system.Dispose;
	}
}
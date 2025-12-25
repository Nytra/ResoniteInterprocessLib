using System.IO.Enumeration;
using System.Text.Json.Nodes;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.ProtoFlux;
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

internal static class Defaults
{
	private static MessagingQueue? _defaultQueue;
    public static MessagingQueue DefaultQueue
	{
		get
		{
			if (_defaultQueue is not null) return _defaultQueue;

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

			if (queueName is null)
			{
				throw new InvalidDataException("Could not get default FrooxEngine queue name! If this is a headless, you need to use the other Messenger constructor to manually specify a queue name.");
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

			_defaultQueue = new MessagingQueue(true, $"InterprocessLib-{queueName}", MessagingManager.DEFAULT_CAPACITY, FrooxEnginePool.Instance, Messenger.FailHandler, Messenger.WarnHandler, Messenger.DebugHandler);

			Task.Run(async () =>
			{
				while (Engine.Current is null)
					await Task.Delay(1);

				Engine.Current.OnShutdown += _defaultQueue.Dispose; // This is important- as the authority process we need to dispose of the queue on shutdown, otherwise the shared memory files don't get deleted (at least on Linux)
			});

			return _defaultQueue;
		}
	}
    public static IMemoryPackerEntityPool DefaultPool => FrooxEnginePool.Instance;
}
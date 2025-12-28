using Renderite.Shared;
using Renderite.Unity;
using UnityEngine;

namespace InterprocessLib;

internal class Defaults
{
	private static MessagingQueue? _defaultQueue;
	public static IMemoryPackerEntityPool DefaultPool => PackerMemoryPool.Instance;
	public static MessagingQueue DefaultQueue
	{
		get
		{
			if (_defaultQueue is not null) return _defaultQueue;

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

			if (fullQueueName is null) throw new InvalidDataException("QueueName argument is null!");

			var engineSharedMemoryPrefix = fullQueueName.Substring(0, fullQueueName.IndexOf('_'));

			_defaultQueue = new MessagingQueue(false, $"InterprocessLib-{engineSharedMemoryPrefix}", MessagingManager.DEFAULT_CAPACITY, PackerMemoryPool.Instance, Messenger.FailHandler, Messenger.WarnHandler, Messenger.DebugHandler);

			Application.quitting += _defaultQueue.Dispose;

			return _defaultQueue;
		}
	}
}
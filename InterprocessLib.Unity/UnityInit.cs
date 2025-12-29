using Renderite.Shared;
using Renderite.Unity;
using UnityEngine;

namespace InterprocessLib;

internal class Defaults
{
	public static bool DefaultIsAuthority => false;
	public static IMemoryPackerEntityPool DefaultPool => PackerMemoryPool.Instance;

	private static string? _defaultQueuePrefix;
	public static string DefaultQueuePrefix
	{
		get
		{
			if (_defaultQueuePrefix is not null) return _defaultQueuePrefix;

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

			_defaultQueuePrefix = fullQueueName.Substring(0, fullQueueName.IndexOf('_'));

			return _defaultQueuePrefix;
		}
	}
	public static void Init()
	{
		// This only exists so it can be called to trigger the static constructor
	}
	static Defaults()
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
		Application.quitting += Messenger.Shutdown;
	}
}
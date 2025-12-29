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
	public static bool DefaultIsAuthority => true;

	public static string DefaultQueuePrefix
	{
		get
		{
			if (field is not null) return field;

			var args = Environment.GetCommandLineArgs();
			for (int i = 0; i < args.Length; i++)
			{
				if (args[i].Equals("-shmprefix", StringComparison.InvariantCultureIgnoreCase))
				{
					field = args[i + 1];
					break;
				}
			}

			if (field is null)
				throw new InvalidDataException("Could not get default FrooxEngine queue prefix! If this is a headless, you need to use the other Messenger constructor to manually specify a queue name.");

			return field;
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
		Task.Run(async () =>
		{
			while (Engine.Current is null)
				await Task.Delay(1);

			Engine.Current.OnShutdown += Messenger.Shutdown;
		});
	}
    public static IMemoryPackerEntityPool DefaultPool => FrooxEnginePool.Instance;
}
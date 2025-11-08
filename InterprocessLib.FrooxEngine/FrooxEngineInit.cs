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

		Task.Run(InitLoop);
	}
	private static async void InitLoop()
	{
		// Engine.SharedMemoryPrefix is assigned just before the RenderSystem is created
		while (Engine.Current?.RenderSystem is null)
		{
			await Task.Delay(1);
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
		string uniqueId = Engine.Current.SharedMemoryPrefix;

		if (uniqueId is null)
		{
			system = await Messenger.GetFallbackSystem(true, MessagingManager.DEFAULT_CAPACITY, FrooxEnginePool.Instance, null, Messenger.OnFailure, Messenger.OnWarning, Messenger.OnDebug);
			if (system is null)
			{
				throw new EntryPointNotFoundException("Unable to get fallback messaging system!");
			}
		}
		else
		{
			system = new MessagingSystem(true, $"InterprocessLib-{uniqueId}", MessagingManager.DEFAULT_CAPACITY, FrooxEnginePool.Instance, null, Messenger.OnFailure, Messenger.OnWarning, Messenger.OnDebug);
		}

		Messenger.SetDefaultSystem(system);
		Engine.Current.OnShutdown += system.Dispose; // this might fix the rare occurence that Renderite.Host stays open after exiting Resonite
		system.Connect();
	}
}
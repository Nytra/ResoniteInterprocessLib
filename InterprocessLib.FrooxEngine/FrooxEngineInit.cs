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
	private static void CommandHandler(RendererCommand command, int messageSize)
	{
	}
	public static void Init()
	{
		if (Messenger.DefaultInitStarted)
			throw new InvalidOperationException("Messenger default backend initialization has already been started!");

		Messenger.DefaultInitStarted = true;

		Task.Run(InitLoop);
	}
	private static async void InitLoop()
	{
		if (Engine.Current?.RenderSystem?.Engine is null)
		{
			await Task.Delay(1);
			InitLoop();
		}
		else
		{
			await Task.Delay(100);

			var renderSystemMessagingHost = (RenderiteMessagingHost?)typeof(RenderSystem).GetField("_messagingHost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Engine.Current!.RenderSystem);
			if (renderSystemMessagingHost is null)
				throw new InvalidOperationException("Engine is not configured to use a renderer!");

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

			var host = new MessagingSystem(true, renderSystemMessagingHost!.QueueName + "InterprocessLib", renderSystemMessagingHost.QueueCapacity, FrooxEnginePool.Instance, CommandHandler, Messenger.OnFailure, Messenger.OnWarning, Messenger.OnDebug);
			Messenger.SetDefaultSystem(host);
			Engine.Current.OnShutdown += host.Dispose;
			host.Connect();
			// The authority process automatically initializes when it receives a MessengerReadyCommand from the non-authority process
		}
	}
}
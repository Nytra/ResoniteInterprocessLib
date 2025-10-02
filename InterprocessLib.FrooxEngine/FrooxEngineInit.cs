using Elements.Core;
using FrooxEngine;
using Renderite.Shared;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace InterprocessLib;

internal static class FrooxEngineInit
{
	private static void CommandHandler(RendererCommand command, int messageSize)
	{
	}
	public static void Init()
	{
		if (Messenger.Host is not null)
			throw new InvalidOperationException("Messenger has already been initialized!");

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
			//await Task.Delay(100);

			var renderSystemMessagingHost = (RenderiteMessagingHost?)typeof(RenderSystem).GetField("_messagingHost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Engine.Current!.RenderSystem);
			if (renderSystemMessagingHost is null)
				throw new InvalidOperationException("Engine is not configured to use a renderer!");

			Messenger.OnWarning = (msg) => 
			{
				UniLog.Warning($"[InterprocessLib] {msg}");
			};
			Messenger.OnFailure = (ex) => 
			{ 
				UniLog.Error($"[InterprocessLib] Error in InterprocessLib Messaging Host!\n{ex}");
			};
			#if DEBUG
			Messenger.OnDebug = (msg) => 
			{
				UniLog.Log($"[InterprocessLib][DEBUG] {msg}");
			};
			#endif
			Messenger.IsAuthority = true;
			Messenger.Host = new MessagingHost(Messenger.IsAuthority, renderSystemMessagingHost!.QueueName, renderSystemMessagingHost.QueueCapacity, renderSystemMessagingHost, CommandHandler, Messenger.OnFailure, Messenger.OnWarning, Messenger.OnDebug);
			Messenger.FinishInitialization();
		}
	}
}
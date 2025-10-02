using FrooxEngine;
using Renderite.Shared;
using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("InterprocessLib.BepisLoader")]
[assembly: InternalsVisibleTo("InterprocessLib.RML")]

namespace InterprocessLib;

public static class FrooxEngineInit
{
	private static void CommandHandler(RendererCommand command, int messageSize)
	{
	}
	public static void Init()
	{
		if (Messenger.Host is not null)
			throw new InvalidOperationException("Messenger has already been initialized!");

		if (Engine.Current?.RenderSystem is null)
			throw new InvalidOperationException("Messenger is not ready to be used yet!");

		var renderSystemMessagingHost = (RenderiteMessagingHost?)typeof(RenderSystem).GetField("_messagingHost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Engine.Current!.RenderSystem);

		if (renderSystemMessagingHost is null)
			throw new InvalidOperationException("Engine is not configured to use a renderer!");

		Messenger.IsAuthority = true;
		Messenger.Host = new MessagingHost(Messenger.IsAuthority, renderSystemMessagingHost!.QueueName, renderSystemMessagingHost.QueueCapacity, renderSystemMessagingHost, CommandHandler, Messenger.OnFailure, Messenger.OnWarning, Messenger.OnDebug);
		Messenger.FinishInitialization();
	}
}
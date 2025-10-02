using FrooxEngine;
using Renderite.Shared;
using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("InterprocessLib.BepisLoader")]
[assembly: InternalsVisibleTo("InterprocessLib.RML")]

namespace InterprocessLib;

public partial class Messenger
{
	private const bool IsFrooxEngine = true;

	private static void CommandHandler(RendererCommand command, int messageSize)
	{
	}

	internal static void Init()
	{
		if (IsInitialized) return;

		if (Engine.Current?.RenderSystem is null)
			throw new InvalidOperationException("Messenger is not ready to be used yet!");

		var renderSystemMessagingHost = (RenderiteMessagingHost?)typeof(RenderSystem).GetField("_messagingHost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Engine.Current!.RenderSystem);

		if (renderSystemMessagingHost is null)
			throw new InvalidOperationException("Engine is not configured to use a renderer!");

		_host = new MessagingHost(IsAuthority, renderSystemMessagingHost!.QueueName, renderSystemMessagingHost.QueueCapacity, renderSystemMessagingHost, CommandHandler, OnFailure, OnWarning, OnDebug);
	}
}
using FrooxEngine;
using System.Reflection;

namespace InterprocessLib;

public static partial class Messaging
{
	static Messaging()
	{
		if (Engine.Current?.RenderSystem is null)
			ThrowNotReady();

		var renderSystemMessagingHost = (RenderiteMessagingHost?)typeof(RenderSystem).GetField("_messagingHost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Engine.Current!.RenderSystem);

		Host = new MessagingHost(true, renderSystemMessagingHost!.QueueName, renderSystemMessagingHost.QueueCapacity, renderSystemMessagingHost);
	}
}
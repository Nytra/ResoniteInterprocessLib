using System.Diagnostics;
using System.Security;
using Renderite.Shared;

namespace InterprocessLib;

internal static class Initializer
{
	public static async void Init()
	{
		MessagingSystem system = await Messenger.GetFallbackSystem(false);
        Messenger.InitializeDefaultSystem(system);
	}
}
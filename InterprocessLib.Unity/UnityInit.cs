using Renderite.Shared;
using Renderite.Unity;
using System.Reflection;

namespace InterprocessLib;

internal static class UnityInit
{
	private static void CommandHandler(RendererCommand command, int messageSize)
	{
		if (Messenger.IsInitialized) return;

		if (command is MessengerReadyCommand)
		{
			Messenger.FinishInitialization();
		}
	}
	public static void Init()
	{
		if (Messenger.Host is not null)
			throw new InvalidOperationException("Messenger has already been initialized!");

		Task.Run(InitLoop);
	}
	private static async void InitLoop()
	{
		if (RenderingManager.Instance is null)
		{
			await Task.Delay(1);
			InitLoop();
		}
		else
		{
			var getConnectionParametersMethod = typeof(RenderingManager).GetMethod("GetConnectionParameters", BindingFlags.Instance | BindingFlags.NonPublic);

			object[] parameters = { "", 0L };

			if (!(bool)getConnectionParametersMethod.Invoke(RenderingManager.Instance, parameters))
			{
				throw new ArgumentException("Could not get connection parameters from RenderingManager!");
			}

			Messenger.OnWarning = (msg) =>
			{
				UnityEngine.Debug.LogWarning($"[InterprocessLib] [WARN] {msg}");
			};
			Messenger.OnFailure = (ex) =>
			{
				UnityEngine.Debug.LogError($"[InterprocessLib] [ERROR] Error in InterprocessLib Messaging Host!\n{ex}");
			};
#if DEBUG
			Messenger.OnDebug = (msg) => 
			{
				UnityEngine.Debug.Log($"[InterprocessLib] [DEBUG] {msg}");
			};
#endif

			Messenger.IsAuthority = false;
			Messenger.Host = new(Messenger.IsAuthority, (string)parameters[0], (long)parameters[1], PackerMemoryPool.Instance, CommandHandler, Messenger.OnFailure, Messenger.OnWarning, Messenger.OnDebug);
		}
	}
}
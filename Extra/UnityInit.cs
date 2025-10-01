using Renderite.Shared;
using Renderite.Unity;
using System.Reflection;

namespace InterprocessLib;

public partial class Messenger
{
	private const bool IsFrooxEngine = false;

	private static void CommandHandler(RendererCommand command, int messageSize)
	{
		if (IsInitialized) return;

		if (command is MessengerReadyCommand)
		{
			FinishInitialization();
		}
	}

	internal static void Init()
	{
		if (RenderingManager.Instance is null)
			throw new InvalidOperationException("Messenger is not ready to be used yet!");

		var getConnectionParametersMethod = typeof(RenderingManager).GetMethod("GetConnectionParameters", BindingFlags.Instance | BindingFlags.NonPublic);

		object[] parameters = { "", 0L };

		if (!(bool)getConnectionParametersMethod.Invoke(RenderingManager.Instance, parameters))
		{
			throw new ArgumentException("Could not get connection parameters from RenderingManager!");
		}

		_host = new(IsAuthority, (string)parameters[0], (long)parameters[1], PackerMemoryPool.Instance, CommandHandler, OnFailure, OnWarning, OnDebug);
	}
}
using Renderite.Unity;
using System.Reflection;

namespace InterprocessLib;

public static partial class Messaging
{
	static Messaging()
	{
		if (RenderingManager.Instance is null)
			ThrowNotReady();

		var getConnectionParametersMethod = typeof(RenderingManager).GetMethod("GetConnectionParameters", BindingFlags.Instance | BindingFlags.NonPublic);

		object[] parameters = { "", 0L };

		if (!(bool)getConnectionParametersMethod.Invoke(RenderingManager.Instance, parameters))
		{
			throw new ArgumentException("Could not get connection parameters from RenderingManager!");
		}

		Host = new(false, (string)parameters[0], (long)parameters[1], PackerMemoryPool.Instance);
	}
}
using Renderite.Shared;

namespace InterprocessLib;

internal static class Initializer
{
	public static void Init()
	{
		Messenger.OnWarning = (msg) =>
		{
			Console.WriteLine($"[InterprocessLib] [WARN] {msg}");
		};
		Messenger.OnFailure = (ex) =>
		{
			Console.WriteLine($"[InterprocessLib] [ERROR] Error in InterprocessLib Messaging Backend!\n{ex}");
		};
#if DEBUG
		Messenger.OnDebug = (msg) => 
		{
			Console.WriteLine($"[InterprocessLib] [DEBUG] {msg}");
		};
#endif

		MessagingSystem? system = null;

        var task = Messenger.GetFallbackSystem("Fallback", false, MessagingManager.DEFAULT_CAPACITY, FallbackPool.Instance, Messenger.OnFailure, Messenger.OnWarning, Messenger.OnDebug);
        task.Wait();
        system = task.Result;
        if (system is null)
        {
            throw new EntryPointNotFoundException("Unable to get fallback messaging system!");
        }

        lock (Messenger.LockObj)
		{
			Messenger.PreInit(system);
			Messenger.SetDefaultSystem(system);
			system.Initialize();
		}
	}
}
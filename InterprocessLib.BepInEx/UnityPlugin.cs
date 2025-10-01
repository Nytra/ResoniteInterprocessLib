using BepInEx;
using BepInEx.Logging;
using Renderite.Unity;

namespace InterprocessLib;

[BepInPlugin("Nytra.InterprocessLib.BepInEx", "InterprocessLib.BepInEx", "1.0.0")]
internal class UnityPlugin : BaseUnityPlugin
{
	public static ManualLogSource? Log;
	private static bool _initialized;

	void Awake()
	{
		Log = base.Logger;
		Update();
	}

	void Update()
	{
		if (_initialized) return;

		if (RenderingManager.Instance is null) return;

		Messenger.OnWarning = WarnHandler;
		Messenger.OnFailure = FailHandler;

#if DEBUG
		Messenger.OnDebug = DebugHandler;
#endif

		Messenger.Init();

		_initialized = true;
	}

	private static void FailHandler(Exception ex)
	{
		Log!.LogError("Exception in InterprocessLib messaging host:\n" + ex.ToString());
	}

	private static void WarnHandler(string msg)
	{
		Log!.LogWarning(msg);
	}

	private static void DebugHandler(string msg)
	{
		Log!.LogDebug(msg);
	}
}
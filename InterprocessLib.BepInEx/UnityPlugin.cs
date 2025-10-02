using BepInEx;
using BepInEx.Logging;
using Renderite.Unity;

namespace InterprocessLib;

[BepInPlugin("Nytra.InterprocessLib.BepInEx", "InterprocessLib.BepInEx", "1.0.1")]
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

		// Sometimes it's bad to initialize too early
		// Main engine sends MessengerReadyCommand at FrameIndex = 120, so it needs to be before then
		if (RenderingManager.Instance.LastFrameIndex < 60) return;

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
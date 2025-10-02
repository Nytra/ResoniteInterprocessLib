using BepInEx;
using BepInEx.Logging;
using Renderite.Unity;

namespace InterprocessLib;

[BepInPlugin("Nytra.InterprocessLib.BepInEx", "InterprocessLib.BepInEx", "1.0.1")]
internal class UnityBootstrap : BaseUnityPlugin
{
	public static ManualLogSource? Log;

	void Awake()
	{
		Log = base.Logger;
		Update();
	}

	void Update()
	{
		if (Messenger.Host is not null)
		{
			Destroy(this);
			return;
		}

		if (RenderingManager.Instance is null) return;

		Messenger.OnWarning = WarnHandler;
		Messenger.OnFailure = FailHandler;

#if DEBUG
		Messenger.OnDebug = DebugHandler;
#endif

		UnityInit.Init();
		Log!.LogInfo("Messenger initialized.");

		Destroy(this);
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
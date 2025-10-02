using Elements.Core;
using FrooxEngine;
using Renderite.Shared;
using ResoniteModLoader;
using System.Reflection;

namespace InterprocessLib;

internal class RML_Mod : ResoniteMod
{
	public override string Name => "InterprocessLib.RML";

	public override string Author => "Nytra";

	public override string Version => "1.0.1";

	public override string Link => "https://github.com/Nytra/ResoniteInterprocessLib";

	private static List<Type> CoreTypes => (List<Type>)typeof(PolymorphicMemoryPackableEntity<RendererCommand>).GetField("types", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

	public override void OnEngineInit()
	{
		//var harmony = new Harmony("owo.Nytra.InterprocessLib");
		//harmony.PatchAll();

		Messenger.OnFailure = FailHandler;
		Messenger.OnWarning = WarnHandler;
		//Messenger.OnDebug = DebugHandler;

		if (Messenger.IsInitialized)
			throw new Exception("Already initialized!");

		//Task.Run(PreInitLoop);
		Engine.Current.RunPostInit(() => 
		{
			Messenger.Init();
			var list = new List<Type>();
			list.AddRange(CoreTypes);
			foreach (var type in TypeManager.NewTypes)
			{
				list.AddUnique(type);
			}
			IdentifiableCommand.InitNewTypes(list);
			Msg("Messenger initialized.");
			Messenger.FinishInitialization();
		});
	}

	//private static async void PreInitLoop()
	//{
	//	var renderSystem = Engine.Current?.RenderSystem;
	//	if (renderSystem is null)
	//	{
	//		await Task.Delay(1);
	//		PreInitLoop();
	//	}
	//	else
	//	{
	//		await Task.Delay(1); // This delay is needed otherwise it doesn't work
			
	//	}
	//}

	private static void FailHandler(Exception ex)
	{
		Error("Exception in InterprocessLib messaging host:\n" + ex.ToString());
	}

	private static void WarnHandler(string msg)
	{
		Warn(msg);
	}

	private static void DebugHandler(string msg)
	{
		Msg(msg);
	}
}

public static class RML_Extensions
{
	private static Dictionary<ModConfigurationKey, bool> _syncStates = new();

	public static void SyncConfigEntry<T>(this Messenger? messenger, ModConfigurationKey<T> configEntry) where T : unmanaged
	{
		_syncStates[configEntry] = true;
		if (Messenger.IsAuthority)
			messenger.SendConfigEntry<T>(configEntry);
		configEntry.OnChanged += (object? newValue) =>
		{
			if (_syncStates.TryGetValue(configEntry, out bool value) && value == true)
				messenger.SendConfigEntry<T>(configEntry);
		};
		messenger.ReceiveConfigEntry<T>(configEntry);
	}

	public static void SyncConfigEntry(this Messenger? messenger, ModConfigurationKey<string> configEntry)
	{
		_syncStates[configEntry] = true;
		if (Messenger.IsAuthority)
			messenger.SendConfigEntry(configEntry);
		configEntry.OnChanged += (object? newValue) =>
		{
			if (_syncStates.TryGetValue(configEntry, out bool value) && value == true)
				messenger.SendConfigEntry(configEntry);
		};
		messenger.ReceiveConfigEntry(configEntry);
	}

	public static void SendConfigEntry<T>(this Messenger? messenger, ModConfigurationKey<T> configEntry) where T : unmanaged
	{
		messenger.SendValue(configEntry.Name, configEntry.Value);
	}

	public static void SendConfigEntry(this Messenger? messenger, ModConfigurationKey<string> configEntry)
	{
		messenger.SendString(configEntry.Name, configEntry.Value);
	}

	public static void ReceiveConfigEntry<T>(this Messenger? messenger, ModConfigurationKey<T> configEntry) where T : unmanaged
	{
		messenger.ReceiveValue<T>(configEntry.Name, (val) =>
		{
			_syncStates[configEntry] = false;
			configEntry.Value = val;
			_syncStates[configEntry] = true;
		});
	}

	public static void ReceiveConfigEntry(this Messenger? messenger, ModConfigurationKey<string> configEntry)
	{
		messenger.ReceiveString(configEntry.Name, (str) =>
		{
			_syncStates[configEntry] = false;
			configEntry.Value = str!;
			_syncStates[configEntry] = true;
		});
	}
}
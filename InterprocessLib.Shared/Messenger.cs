using Renderite.Shared;

namespace InterprocessLib;

/// <summary>
/// Simple interprocess messaging API.
/// </summary>
public class Messenger
{
	private static MessagingSystem? _defaultSystem;

	private MessagingSystem? _customSystem;

	private MessagingSystem? CurrentSystem => _customSystem ?? _defaultSystem;

	/// <summary>
	/// The underlying interprocess queue name for this instance
	/// </summary>
	public string? QueueName => CurrentSystem?.QueueName;

	/// <summary>
	/// The capacity of the underlying interprocess queue for this instance
	/// </summary>
	public long? QueueCapacity => CurrentSystem?.QueueCapacity;

	/// <summary>
	/// If true the messenger will send commands immediately, otherwise commands will wait in a queue until the non-authority process initializes its interprocess connection.
	/// </summary>
	public bool IsInitialized => CurrentSystem?.IsInitialized ?? false;

	/// <summary>
	/// Does this process have authority over the other process? Might be null if the library has not fully initialized yet
	/// </summary>
	public bool? IsAuthority => CurrentSystem?.IsAuthority;

	/// <summary>
	/// Is the interprocess connection available? this might be false if the library has not fully initialized, or if there has been a failure in the interprocess queue
	/// </summary>
	public bool IsConnected => CurrentSystem?.IsConnected ?? false;

	internal static bool DefaultInitStarted = false;

	/// <summary>
	/// Called when the backend connection has a critical error
	/// </summary>
	public static Action<Exception>? OnFailure;

	/// <summary>
	/// Called when something potentially bad/unexpected happens
	/// </summary>
	public static Action<string>? OnWarning;

	/// <summary>
	/// Called with additional debugging information
	/// </summary>
	public static Action<string>? OnDebug;

	private static List<Action>? _defaultPostInitActions = new();

	private static List<Action<MessagingSystem>>? _defaultPreInitActions = new();

	private string _ownerId;

	//private static HashSet<string> _defaultBackendRegisteredOwnerIds = new();

	private List<Type>? _additionalObjectTypes;

	private List<Type>? _additionalValueTypes;

	private static MessagingSystem? _fallbackSystem = null;

	private static bool _runningFallbackSystemInit = false;

	internal static object LockObj = new();

	internal static async Task<MessagingSystem?> GetFallbackSystem(bool isAuthority, long queueCapacity, IMemoryPackerEntityPool? pool = null, RenderCommandHandler? commandHandler = null, Action<Exception>? failhandler = null, Action<string>? warnHandler = null, Action<string>? debugHandler = null, Action? postInitCallback = null)
	{
		var startTime = DateTime.UtcNow;
		int waitTimeMs = 2500;
		while (_runningFallbackSystemInit && (DateTime.UtcNow - startTime).TotalMilliseconds < waitTimeMs * 2)
			await Task.Delay(1);

		if (_fallbackSystem is not null) return _fallbackSystem;

		_runningFallbackSystemInit = true;

		var now = DateTime.UtcNow;
		int minuteInDay = now.Hour * 60 + now.Minute;
		var system1 = new MessagingSystem(isAuthority, $"InterprocessLib-{minuteInDay}", queueCapacity, pool ?? FallbackPool.Instance, commandHandler, failhandler, warnHandler, debugHandler, postInitCallback);
		system1.Connect();
		system1.Initialize();
		if (isAuthority)
		{
			_fallbackSystem = system1;
			_runningFallbackSystemInit = false;
			return system1;
		}
		var cancel1 = new CancellationTokenSource();
		system1.PingCallback = (latency) => 
		{ 
			cancel1.Cancel();
		};
		system1.SendPackable(new PingCommand() { Time = now });
		try
		{
			await Task.Delay(waitTimeMs, cancel1.Token);
		}
		catch (TaskCanceledException)
		{
		}
		if (cancel1.IsCancellationRequested)
		{
			_fallbackSystem = system1;
		}
		else
		{
			// try the previous minute, in case the other process started just before the minute ticked over (too bad if it ticked over from 1439 to 0)
			system1.Dispose();
			var cancel2 = new CancellationTokenSource(); 
			var system2 = new MessagingSystem(isAuthority, $"InterprocessLib-{minuteInDay - 1}", queueCapacity, pool ?? FallbackPool.Instance, commandHandler, failhandler, warnHandler, debugHandler, postInitCallback);
			system2.Connect();
			system2.Initialize();
			system2.PingCallback = (latency) => 
			{ 
				cancel2.Cancel();
			};
			system2.SendPackable(new PingCommand() { Time = now });
			try
			{
				await Task.Delay(waitTimeMs, cancel2.Token);
			}
			catch (TaskCanceledException)
			{
			}
			if (cancel2.IsCancellationRequested)
			{
				_fallbackSystem = system2;
			}
			else
			{
				system2.Dispose();
			}
		}
		_runningFallbackSystemInit = false;
		return _fallbackSystem;
	}

	/// <summary>
	/// Creates an instance with a unique owner
	/// </summary>
	/// <param name="ownerId">Unique identifier for this instance in this process. Should match the other process.</param>
	/// <param name="additionalObjectTypes">Optional list of additional <see cref="IMemoryPackable"/> class types you want to be able to send or receieve. Types you want to use that are vanilla go in here too.</param>
	/// <param name="additionalValueTypes">Optional list of additional unmanaged types you want to be able to send or receieve.</param>
	/// <exception cref="ArgumentNullException"></exception>
	/// <exception cref="EntryPointNotFoundException"></exception>
	public Messenger(string ownerId, List<Type>? additionalObjectTypes = null, List<Type>? additionalValueTypes = null)
	{
		if (ownerId is null)
			throw new ArgumentNullException(nameof(ownerId));

		_ownerId = ownerId;

		_additionalObjectTypes = additionalObjectTypes;

		_additionalValueTypes = additionalValueTypes;

		if (_defaultSystem is null)
		{
			DefaultRunPreInit(Register);
		}
		else
		{
			Register();
		}

		if (_defaultSystem is null && !DefaultInitStarted)
		{
			var frooxEngineInitType = Type.GetType("InterprocessLib.FrooxEngineInit");
			if (frooxEngineInitType is not null)
			{
				frooxEngineInitType.GetMethod("Init", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)!.Invoke(null, null);
			}
			else
			{
				var unityInitType = Type.GetType("InterprocessLib.UnityInit");
				if (unityInitType is not null)
				{
					unityInitType.GetMethod("Init", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)!.Invoke(null, null);
				}
				else
				{
					var fallbackSystemTask = GetFallbackSystem(false, MessagingManager.DEFAULT_CAPACITY, FallbackPool.Instance, null, OnFailure, OnWarning, OnDebug, null);
					fallbackSystemTask.Wait();
					if (fallbackSystemTask.Result is not MessagingSystem fallbackSystem)
						throw new EntryPointNotFoundException("Could not find InterprocessLib initialization type!");
					else
						_defaultSystem = fallbackSystemTask.Result;
				}
			}
		}
	}

	/// <summary>
	/// Creates an instance with a unique owner and connects to a custom queue so you can talk to any process
	/// </summary>
	/// <param name="ownerId">Unique identifier for this instance in this process. Should match the other process.</param>
	/// <param name="isAuthority">Does this process have authority over the other process? The authority process should always be started first.</param>
	/// <param name="queueName">Custom queue name. Should match the other process.</param>
	/// <param name="pool">Custom pool for borrowing and returning memory-packable types.</param>
	/// <param name="queueCapacity">Capacity for the custom queue in bytes.</param>
	/// <param name="additionalObjectTypes">Optional list of additional <see cref="IMemoryPackable"/> class types you want to be able to send or receieve. Types you want to use that are vanilla go in here too.</param>
	/// <param name="additionalValueTypes">Optional list of additional unmanaged types you want to be able to send or receieve.</param>
	/// <exception cref="ArgumentNullException"></exception>
	/// <exception cref="EntryPointNotFoundException"></exception>
	public Messenger(string ownerId, bool isAuthority, string queueName, IMemoryPackerEntityPool? pool = null, long queueCapacity = 1024*1024, List<Type>? additionalObjectTypes = null, List<Type>? additionalValueTypes = null)
	{
		if (ownerId is null)
			throw new ArgumentNullException(nameof(ownerId));

		IMemoryPackerEntityPool? actualPool = pool;
		if (actualPool is null)
		{
			var frooxEnginePoolType = Type.GetType("InterprocessLib.FrooxEnginePool");
			if (frooxEnginePoolType is not null)
			{
				actualPool = (IMemoryPackerEntityPool)frooxEnginePoolType.GetField("Instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)!.GetValue(null)!;
			}
			else
			{
				var unityPoolType = Type.GetType("Renderite.Unity.PackerMemoryPool");
				if (unityPoolType is not null)
				{
					actualPool = (IMemoryPackerEntityPool)unityPoolType.GetField("Instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)!.GetValue(null)!;
				}
				else
				{
					throw new EntryPointNotFoundException("Could not find default IMemoryPackerEntityPool!");
				}
			}
		}

		if (MessagingSystem.TryGetRegisteredSystem(queueName) is not MessagingSystem existingSystem)
		{
			_customSystem = new MessagingSystem(isAuthority, queueName, queueCapacity, actualPool, null, OnFailure, OnWarning, OnDebug);
		}
		else
		{
			_customSystem = existingSystem;
		}

		_ownerId = ownerId;

		_additionalObjectTypes = additionalObjectTypes;

		_additionalValueTypes = additionalValueTypes;

		Register();

		if (!_customSystem!.IsInitialized)
		{
			_customSystem.Connect();
			_customSystem.Initialize();
		}
	}

	internal static void PreInit(MessagingSystem system)
	{
		system.SetPostInitActions(_defaultPostInitActions);
		//_defaultPostInitActions = null;

		foreach (var act in _defaultPreInitActions!)
		{
			try
			{
				act(system);
			}
			catch (Exception ex)
			{
				OnWarning?.Invoke($"Exception running pre-init action:\n{ex}");
			}
		}
		_defaultPreInitActions = null;
	}

	internal static void SetDefaultSystem(MessagingSystem system)
	{
		_defaultSystem = system;
		_defaultPostInitActions = null;
	}

	private void RunPostInit(Action act)
	{
		lock (LockObj)
		{
			if (IsInitialized)
			{
				act();
				return;
			}
			if (CurrentSystem is null)
				DefaultRunPostInit(act);
			else
				CurrentSystem.RunPostInit(act);
		}
	}

	private void Register(MessagingSystem system)
	{
		if (system.HasOwner(_ownerId))
		{
			OnWarning?.Invoke($"Owner {_ownerId} has already been registered in this process for messaging backend with queue name: {system.QueueName}");
		}
		else
			system.RegisterOwner(_ownerId);

		if (_additionalObjectTypes is not null)
		{
			system.TypeManager.InitObjectTypeList(_additionalObjectTypes.Where(t => !system.TypeManager.IsObjectTypeInitialized(t)).ToList());
		}
		if (_additionalValueTypes is not null)
		{
			system.TypeManager.InitValueTypeList(_additionalValueTypes.Where(t => !system.TypeManager.IsValueTypeInitialized(t)).ToList());
		}
	}

	private void Register()
	{
		Register(CurrentSystem!);
	}

	private static void DefaultRunPreInit(Action<MessagingSystem> act)
	{
		if (_defaultSystem is null)
		{
			_defaultPreInitActions!.Add(act);
		}
		else
			throw new InvalidOperationException("Default host already did pre-init!");
	}

	private static void DefaultRunPostInit(Action act)
	{
		if (_defaultSystem is null)
		{
			_defaultPostInitActions!.Add(act);
		}
		else
			throw new InvalidOperationException("Default host already initialized!");
	}

	public void SendValue<T>(string id, T value) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => SendValue(id, value));
			return;
		}

		if (!CurrentSystem!.TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {value.GetType().Name} needs to be registered first!");

		var command = new ValueCommand<T>();
		command.Owner = _ownerId;
		command.Id = id;
		command.Value = value;
		CurrentSystem!.SendPackable(command);
	}

	public void SendValueList<T>(string id, List<T> list) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => SendValueList(id, list));
			return;
		}

		if (!CurrentSystem!.TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		var command = new ValueCollectionCommand<List<T>, T>();
		command.Owner = _ownerId;
		command.Id = id;
		command.Values = list;
		CurrentSystem!.SendPackable(command);
	}

	public void SendValueHashSet<T>(string id, HashSet<T> hashSet) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => SendValueHashSet(id, hashSet));
			return;
		}

		if (!CurrentSystem!.TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		var command = new ValueCollectionCommand<HashSet<T>, T>();
		command.Owner = _ownerId;
		command.Id = id;
		command.Values = hashSet;
		CurrentSystem!.SendPackable(command);
	}

	public void SendValueArray<T>(string id, T[] array) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => SendValueArray(id, array));
			return;
		}

		if (!CurrentSystem!.TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		var command = new ValueArrayCommand<T>();
		command.Owner = _ownerId;
		command.Id = id;
		command.Values = array;
		CurrentSystem!.SendPackable(command);
	}

	public void SendString(string id, string str)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => SendString(id, str));
			return;
		}

		var command = new StringCommand();
		command.Owner = _ownerId;
		command.Id = id;
		command.String = str;
		CurrentSystem!.SendPackable(command);
	}

	public void SendStringList(string id, List<string> list)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => SendStringList(id, list));
			return;
		}

		var command = new StringListCommand();
		command.Owner = _ownerId;
		command.Id = id;
		command.Values = list;
		CurrentSystem!.SendPackable(command);
	}

	public void SendEmptyCommand(string id)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => SendEmptyCommand(id));
			return;
		}

		var command = new EmptyCommand();
		command.Owner = _ownerId;
		command.Id = id;
		CurrentSystem!.SendPackable(command);
	}

	public void SendObject<T>(string id, T? obj) where T : class, IMemoryPackable, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => SendObject(id, obj));
			return;
		}

		if (!CurrentSystem!.TypeManager.IsObjectTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		var wrapper = new ObjectCommand<T>();
		wrapper.Object = obj;
		wrapper.Owner = _ownerId;
		wrapper.Id = id;

		CurrentSystem!.SendPackable(wrapper);
	}

	public void SendObjectList<T>(string id, List<T> list) where T : class, IMemoryPackable, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => SendObjectList(id, list));
			return;
		}

		if (!CurrentSystem!.TypeManager.IsObjectTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		var command = new ObjectCollectionCommand<List<T>, T>();
		command.Owner = _ownerId;
		command.Id = id;
		command.Objects = list;
		CurrentSystem!.SendPackable(command);
	}

	public void SendObjectHashSet<T>(string id, HashSet<T> hashSet) where T : class, IMemoryPackable, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => SendObjectHashSet(id, hashSet));
			return;
		}

		if (!CurrentSystem!.TypeManager.IsObjectTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		var command = new ObjectCollectionCommand<HashSet<T>, T>();
		command.Owner = _ownerId;
		command.Id = id;
		command.Objects = hashSet;
		CurrentSystem!.SendPackable(command);
	}

	public void SendObjectArray<T>(string id, T[] array) where T : class, IMemoryPackable, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => SendObjectArray(id, array));
			return;
		}

		if (!CurrentSystem!.TypeManager.IsObjectTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		var command = new ObjectArrayCommand<T>();
		command.Owner = _ownerId;
		command.Id = id;
		command.Objects = array;
		CurrentSystem!.SendPackable(command);
	}

	//public void SendObjectCollection<C, T>(string id, C collection) where C : ICollection<T>, new() where T : class, IMemoryPackable, new()
	//{
	//	if (id is null)
	//		throw new ArgumentNullException(nameof(id));

	//	if (IsInitialized != true)
	//	{
	//		RunPostInit(() => SendObjectCollection<C, T>(id, collection));
	//		return;
	//	}

	//	if (!CurrentSystem!.TypeManager.IsObjectTypeInitialized<T>())
	//		throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

	//	var command = new ObjectCollectionCommand<C, T>();
	//	command.Owner = _ownerId;
	//	command.Id = id;
	//	command.Objects = collection;
	//	CurrentSystem!.SendPackable(command);
	//}

	public void ReceiveValue<T>(string id, Action<T> callback) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => ReceiveValue(id, callback));
			return;
		}

		if (!CurrentSystem!.TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		CurrentSystem!.RegisterValueCallback(_ownerId, id, callback);
	}

	public void ReceiveValueList<T>(string id, Action<List<T>> callback) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => ReceiveValueList(id, callback));
			return;
		}

		if (!CurrentSystem!.TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		CurrentSystem!.RegisterValueCollectionCallback<List<T>, T>(_ownerId, id, callback);
	}

	public void ReceiveValueHashSet<T>(string id, Action<HashSet<T>> callback) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => ReceiveValueHashSet(id, callback));
			return;
		}

		if (!CurrentSystem!.TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		CurrentSystem!.RegisterValueCollectionCallback<HashSet<T>, T>(_ownerId, id, callback);
	}

	public void ReceiveValueArray<T>(string id, Action<T[]> callback) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => ReceiveValueArray(id, callback));
			return;
		}

		if (!CurrentSystem!.TypeManager.IsValueTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		CurrentSystem!.RegisterValueArrayCallback(_ownerId, id, callback);
	}

	// This won't work because we can't possibly register every type of collection ahead of time
	//public void ReceiveValueCollection<C, T>(string id, Action<C> callback) where C : ICollection<T>, new() where T : unmanaged
	//{
	//	if (id is null)
	//		throw new ArgumentNullException(nameof(id));

	//	if (IsInitialized != true)
	//	{
	//		RunPostInit(() => ReceiveValueCollection<C, T>(id, callback));
	//		return;
	//	}

	//	if (!CurrentSystem!.TypeManager.IsValueTypeInitialized<T>())
	//		throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

	//	CurrentSystem!.RegisterValueCollectionCallback<C, T>(_ownerId, id, callback);
	//}

	public void ReceiveString(string id, Action<string?> callback)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => ReceiveString(id, callback));
			return;
		}

		CurrentSystem!.RegisterStringCallback(_ownerId, id, callback);
	}

	public void ReceiveStringList(string id, Action<List<string>?> callback)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => ReceiveStringList(id, callback));
			return;
		}

		CurrentSystem!.RegisterStringListCallback(_ownerId, id, callback);
	}

	public void ReceiveEmptyCommand(string id, Action callback)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => ReceiveEmptyCommand(id, callback));
			return;
		}

		CurrentSystem!.RegisterEmptyCallback(_ownerId, id, callback);
	}

	public void ReceiveObject<T>(string id, Action<T> callback) where T : class, IMemoryPackable, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => ReceiveObject(id, callback));
			return;
		}

		if (!CurrentSystem!.TypeManager.IsObjectTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		CurrentSystem!.RegisterObjectCallback(_ownerId, id, callback);
	}

	public void ReceiveObjectList<T>(string id, Action<List<T>> callback) where T : class, IMemoryPackable, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => ReceiveObjectList(id, callback));
			return;
		}

		if (!CurrentSystem!.TypeManager.IsObjectTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		CurrentSystem!.RegisterObjectCollectionCallback<List<T>, T>(_ownerId, id, callback);
	}

	public void ReceiveObjectHashSet<T>(string id, Action<HashSet<T>> callback) where T : class, IMemoryPackable, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => ReceiveObjectHashSet(id, callback));
			return;
		}

		if (!CurrentSystem!.TypeManager.IsObjectTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		CurrentSystem!.RegisterObjectCollectionCallback<HashSet<T>, T>(_ownerId, id, callback);
	}

	public void ReceiveObjectArray<T>(string id, Action<T[]> callback) where T : class, IMemoryPackable, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (IsInitialized != true)
		{
			RunPostInit(() => ReceiveObjectArray(id, callback));
			return;
		}

		if (!CurrentSystem!.TypeManager.IsObjectTypeInitialized<T>())
			throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

		CurrentSystem!.RegisterObjectArrayCallback(_ownerId, id, callback);
	}

	public void CheckLatency(Action<TimeSpan> callback)
	{
		if (!IsInitialized)
		{
			RunPostInit(() => CheckLatency(callback));
			return;
		}

		CurrentSystem!.PingCallback = callback;

		var pingCommand = new PingCommand();
		pingCommand.Time = DateTime.UtcNow;
		CurrentSystem!.SendPackable(pingCommand);
	}

	// public void SendTypeCommand(Type type)
	// {
	// 	if (type is null)
	// 		throw new ArgumentNullException(nameof(type));

	// 	if (IsInitialized != true)
	// 	{
	// 		RunPostInit(() => SendTypeCommand(type));
	// 		return;
	// 	}

	// 	var typeCommand = new TypeCommand();
	// 	typeCommand.Type = type;

	// 	Messenger.OnDebug?.Invoke($"Sending new type to register: {type.FullName}");
	// 	CurrentSystem!.SendPackable(typeCommand);
	// }

	// This won't work because we can't possibly register every type of collection ahead of time
	//public void ReceiveObjectCollection<C, T>(string id, Action<C> callback) where C : ICollection<T>, new() where T : class, IMemoryPackable, new()
	//{
	//	if (id is null)
	//		throw new ArgumentNullException(nameof(id));

	//	if (IsInitialized != true)
	//	{
	//		RunPostInit(() => ReceiveObjectCollection<C, T>(id, callback));
	//		return;
	//	}

	//	if (!CurrentSystem!.TypeManager.IsObjectTypeInitialized<T>())
	//		throw new InvalidOperationException($"Type {typeof(T).Name} needs to be registered first!");

	//	CurrentSystem!.RegisterObjectCollectionCallback<C, T>(_ownerId, id, callback);
	//}
}
using Renderite.Shared;

namespace InterprocessLib;

/// <summary>
/// Simple interprocess messaging API.
/// </summary>
public class Messenger : IDisposable
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

	private static MessagingSystem? _fallbackSystem = null;

	private static bool _runningFallbackSystemInit = false;

	internal static readonly object LockObj = new();

	private DateTime _lastPingTime;

	internal static async Task<MessagingSystem?> GetFallbackSystem(string ownerId, bool isAuthority, long queueCapacity, IMemoryPackerEntityPool? pool = null, Action<Exception>? failhandler = null, Action<string>? warnHandler = null, Action<string>? debugHandler = null, Action? postInitCallback = null)
	{
		OnDebug?.Invoke("GetFallbackSystem called");

		var startTime = DateTime.UtcNow;
		int waitTimeMs = 5000;
		while (_runningFallbackSystemInit && (DateTime.UtcNow - startTime).TotalMilliseconds < waitTimeMs * 2)
			await Task.Delay(1);

		if (_fallbackSystem is not null) return _fallbackSystem;

		_runningFallbackSystemInit = true;

		var now = DateTime.UtcNow;
		int minuteInDay = now.Hour * 60 + now.Minute;
		var system1 = new MessagingSystem(isAuthority, $"InterprocessLib-{ownerId}{minuteInDay}", queueCapacity, pool ?? FallbackPool.Instance, failhandler, warnHandler, debugHandler, postInitCallback);
		system1.Connect();
		if (isAuthority)
		{
			_fallbackSystem = system1;
			_runningFallbackSystemInit = false;
			return system1;
		}
		var cancel1 = new CancellationTokenSource();
		system1.RegisterCallback<PingCommand>((ping) => 
		{ 
			cancel1.Cancel();
			system1.RegisterCallback<PingCommand>(null);
		});
		system1.SendPackable(new PingCommand());
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
			var system2 = new MessagingSystem(isAuthority, $"InterprocessLib-{ownerId}{minuteInDay - 1}", queueCapacity, pool ?? FallbackPool.Instance, failhandler, warnHandler, debugHandler, postInitCallback);
			system2.Connect();
			system2.RegisterCallback<PingCommand>((ping) => 
			{ 
				cancel2.Cancel();
				system2.RegisterCallback<PingCommand>(null);
			});
			system2.SendPackable(new PingCommand());
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
	/// <param name="additionalObjectTypes">Unused parameter kept for backwards compatibility.</param>
	/// <param name="additionalValueTypes">Unused parameter kept for backwards compatibility.</param>
	/// <exception cref="ArgumentNullException"></exception>
	/// <exception cref="EntryPointNotFoundException"></exception>
	[Obsolete("Use the other constructors that don't take Type lists", false)]
	public Messenger(string ownerId, List<Type>? additionalObjectTypes = null, List<Type>? additionalValueTypes = null)
	{
		if (ownerId is null)
			throw new ArgumentNullException(nameof(ownerId));

		_ownerId = ownerId;

		DefaultInit();
	}

	/// <summary>
	/// Creates an instance with a unique owner
	/// </summary>
	/// <param name="ownerId">Unique identifier for this instance in this process. Should match the other process.</param>
	/// <exception cref="ArgumentNullException"></exception>
	/// <exception cref="EntryPointNotFoundException"></exception>
	public Messenger(string ownerId)
	{
		if (ownerId is null)
			throw new ArgumentNullException(nameof(ownerId));

		_ownerId = ownerId;

		DefaultInit();
	}

	private void DefaultInit()
	{
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
			DefaultInitStarted = true;
			var initType = Type.GetType("InterprocessLib.Initializer");
			if (initType is not null)
			{
				initType.GetMethod("Init", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)!.Invoke(null, null);
			}
			else
			{
				throw new EntryPointNotFoundException("Could not find InterprocessLib initialization type!");
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
	/// <exception cref="ArgumentNullException"></exception>
	/// <exception cref="EntryPointNotFoundException"></exception>
	public Messenger(string ownerId, bool isAuthority, string queueName, IMemoryPackerEntityPool? pool = null, long queueCapacity = 1024*1024)
	{
		if (ownerId is null)
			throw new ArgumentNullException(nameof(ownerId));

		if (queueName is null)
			throw new ArgumentNullException(nameof(queueName));

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
					actualPool = FallbackPool.Instance;
				}
			}
		}

		if (MessagingSystem.TryGetRegisteredSystem(queueName) is not MessagingSystem existingSystem)
		{
			_customSystem = new MessagingSystem(isAuthority, queueName, queueCapacity, actualPool, OnFailure, OnWarning, OnDebug);
		}
		else
		{
			_customSystem = existingSystem;
		}

		_ownerId = ownerId;

		Register();

		if (!_customSystem.IsConnected)
		{
			_customSystem.Connect();
		}

		if (!_customSystem!.IsInitialized)
		{
			_customSystem.Initialize();
		}
	}

	internal static void PreInit(MessagingSystem system)
	{
		system.SetPostInitActions(_defaultPostInitActions);
		_defaultPostInitActions = null;

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

		if (!IsInitialized)
		{
			RunPostInit(() => SendValue(id, value));
			return;
		}

		var command = new ValueCommand<T>();
		command.Owner = _ownerId;
		command.Id = id;
		command.Value = value;
		CurrentSystem!.SendPackable(command);
	}

	[Obsolete("Use SendValueCollection instead.")]
	public void SendValueList<T>(string id, List<T>? list) where T : unmanaged
	{
		SendValueCollection<List<T>, T>(id, list);
	}

	[Obsolete("Use SendValueCollection instead.")]
	public void SendValueHashSet<T>(string id, HashSet<T>? hashSet) where T : unmanaged
	{
		SendValueCollection<HashSet<T>, T>(id, hashSet);
	}

	public void SendValueCollection<C, T>(string id, C? collection) where C : ICollection<T>?, new() where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => SendValueCollection<C, T>(id, collection));
			return;
		}

		var command = new ValueCollectionCommand<C, T>();
		command.Owner = _ownerId;
		command.Id = id;
		command.Values = collection;
		CurrentSystem!.SendPackable(command);
	}

	public void SendValueArray<T>(string id, T[]? array) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => SendValueArray(id, array));
			return;
		}

		var command = new ValueArrayCommand<T>();
		command.Owner = _ownerId;
		command.Id = id;
		command.Values = array;
		CurrentSystem!.SendPackable(command);
	}

	public void SendString(string id, string? str)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
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

	[Obsolete("Use SendStringCollection instead.")]
	public void SendStringList(string id, List<string>? list)
	{
		SendStringCollection<List<string>>(id, list);
	}

	public void SendStringCollection<C>(string id, IReadOnlyCollection<string?>? collection) where C : ICollection<string>?, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => SendStringCollection<C>(id, collection));
			return;
		}

		var command = new StringCollectionCommand<C>();
		command.Owner = _ownerId;
		command.Id = id;
		command.Strings = collection;
		CurrentSystem!.SendPackable(command);
	}

	public void SendStringArray(string id, string?[]? array)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => SendStringArray(id, array));
			return;
		}

		var command = new StringArrayCommand();
		command.Owner = _ownerId;
		command.Id = id;
		command.Strings = array;
		CurrentSystem!.SendPackable(command);
	}

	public void SendEmptyCommand(string id)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => SendEmptyCommand(id));
			return;
		}

		var command = new EmptyCommand();
		command.Owner = _ownerId;
		command.Id = id;
		CurrentSystem!.SendPackable(command);
	}

	public void SendObject<T>(string id, T? obj) where T : class?, IMemoryPackable?, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => SendObject(id, obj));
			return;
		}

		var command = new ObjectCommand<T>();
		command.Object = obj;
		command.Owner = _ownerId;
		command.Id = id;

		CurrentSystem!.SendPackable(command);
	}

	[Obsolete("Use SendObjectCollection instead.")]
	public void SendObjectList<T>(string id, List<T>? list) where T : class?, IMemoryPackable?, new()
	{
		SendObjectCollection<List<T>, T>(id, list);
	}

	public void SendObjectCollection<C, T>(string id, C? collection) where C : ICollection<T>?, new() where T : class?, IMemoryPackable?, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => SendObjectCollection<C, T>(id, collection));
			return;
		}

		var command = new ObjectCollectionCommand<C, T>();
		command.Owner = _ownerId;
		command.Id = id;
		command.Objects = collection;
		CurrentSystem!.SendPackable(command);
	}

	public void SendObjectArray<T>(string id, T[]? array) where T : class?, IMemoryPackable?, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => SendObjectArray(id, array));
			return;
		}

		var command = new ObjectArrayCommand<T>();
		command.Owner = _ownerId;
		command.Id = id;
		command.Objects = array;
		CurrentSystem!.SendPackable(command);
	}

	public void ReceiveValue<T>(string id, Action<T>? callback) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => ReceiveValue(id, callback));
			return;
		}

		CurrentSystem!.RegisterCallback<ValueCommand<T>>(_ownerId, id, (cmd) => callback?.Invoke(cmd.Value));
	}

	[Obsolete("Use ReceiveValueCollection instead.")]
	public void ReceiveValueList<T>(string id, Action<List<T>?>? callback) where T : unmanaged
	{
		ReceiveValueCollection<List<T>, T>(id, callback);
	}

	[Obsolete("Use ReceiveValueCollection instead.")]
	public void ReceiveValueHashSet<T>(string id, Action<HashSet<T>?>? callback) where T : unmanaged
	{
		ReceiveValueCollection<HashSet<T>, T>(id, callback);
	}

	public void ReceiveValueCollection<C, T>(string id, Action<C>? callback) where C : ICollection<T>?, new() where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => ReceiveValueCollection<C, T>(id, callback));
			return;
		}

		CurrentSystem!.RegisterCallback<ValueCollectionCommand<C, T>>(_ownerId, id, (cmd) => callback?.Invoke(cmd.Values!));
	}

	public void ReceiveValueArray<T>(string id, Action<T[]?>? callback) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => ReceiveValueArray(id, callback));
			return;
		}

		CurrentSystem!.RegisterCallback<ValueArrayCommand<T>>(_ownerId, id, (cmd) => callback?.Invoke(cmd.Values));
	}

	public void ReceiveString(string id, Action<string?>? callback)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => ReceiveString(id, callback));
			return;
		}

		CurrentSystem!.RegisterCallback<StringCommand>(_ownerId, id, (cmd) => callback?.Invoke(cmd.String));
	}

	[Obsolete("Use ReceiveStringCollection instead.")]
	public void ReceiveStringList(string id, Action<List<string>?>? callback)
	{
		ReceiveStringCollection(id, callback);
	}

	public void ReceiveStringCollection<C>(string id, Action<C>? callback) where C : ICollection<string>?, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => ReceiveStringCollection(id, callback));
			return;
		}

		CurrentSystem!.RegisterCallback<StringCollectionCommand<C>>(_ownerId, id, (cmd) => callback?.Invoke((C)cmd.Strings!));
	}

	public void ReceiveStringArray(string id, Action<string?[]?>? callback)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => ReceiveStringArray(id, callback));
			return;
		}

		CurrentSystem!.RegisterCallback<StringArrayCommand>(_ownerId, id, (cmd) => callback?.Invoke(cmd.Strings));
	}

	public void ReceiveEmptyCommand(string id, Action? callback)
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => ReceiveEmptyCommand(id, callback));
			return;
		}

		CurrentSystem!.RegisterCallback<EmptyCommand>(_ownerId, id, (cmd) => callback?.Invoke());
	}

	public void ReceiveObject<T>(string id, Action<T>? callback) where T : class?, IMemoryPackable?, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => ReceiveObject(id, callback));
			return;
		}

		CurrentSystem!.RegisterCallback<ObjectCommand<T>>(_ownerId, id, (cmd) => callback?.Invoke(cmd.Object!));
	}

	[Obsolete("Use ReceiveObjectCollection instead.")]
	public void ReceiveObjectList<T>(string id, Action<List<T>?>? callback) where T : class?, IMemoryPackable?, new()
	{
		ReceiveObjectCollection<List<T>, T>(id, callback);
	}

	public void ReceiveObjectCollection<C, T>(string id, Action<C>? callback) where C : ICollection<T>?, new() where T : class?, IMemoryPackable?, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => ReceiveObjectCollection<C, T>(id, callback));
			return;
		}

		CurrentSystem!.RegisterCallback<ObjectCollectionCommand<C, T>>(_ownerId, id, (cmd) => callback?.Invoke(cmd.Objects!));
	}

	public void ReceiveObjectArray<T>(string id, Action<T[]>? callback) where T : class?, IMemoryPackable?, new()
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			RunPostInit(() => ReceiveObjectArray(id, callback));
			return;
		}

		CurrentSystem!.RegisterCallback<ObjectArrayCommand<T>>(_ownerId, id, (cmd) => callback?.Invoke(cmd.Objects!));
	}

	/// <summary>
	/// Send a ping message which will be received and then sent back by the other process
	/// Can be used to check if the other process is active, or to check the latency of the connection
	/// Register a ping callback with <see cref="ReceivePing"/> first.
	/// </summary>
	public void SendPing()
	{
		if (!IsInitialized)
		{
			RunPostInit(() => SendPing());
			return;
		}

		var pingCommand = new PingCommand();
		_lastPingTime = DateTime.UtcNow;
		CurrentSystem!.SendPackable(pingCommand);
	}

	/// <summary>
	/// Register a delegate to be called when this process gets a ping response
	/// Calling <see cref="SendPing"/> should then result in the delegate being called shortly after if the other process is active
	/// </summary>
	/// <param name="callback">The delegate to be called when the ping response gets received</param>
	public void ReceivePing(Action<TimeSpan> callback)
	{
		if (!IsInitialized)
		{
			RunPostInit(() => ReceivePing(callback));
			return;
		}

		CurrentSystem!.RegisterCallback<PingCommand>((ping) => callback?.Invoke(DateTime.UtcNow - _lastPingTime));
	}

    public void Dispose()
    {
        CurrentSystem!.UnregisterOwner(_ownerId);
		_customSystem = null;
    }
}
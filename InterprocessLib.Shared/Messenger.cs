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

	private bool IsInitialized => CurrentSystem?.IsInitialized ?? false;

	internal static bool DefaultInitStarted = false;

	/// <summary>
	/// Called when the interprocess queue has a critical error
	/// </summary>
	public static event Action<Exception>? OnFailure;

	/// <summary>
	/// Called when something potentially bad/unexpected happens
	/// </summary>
	public static event Action<string>? OnWarning;

	/// <summary>
	/// Called with debugging information
	/// </summary>
	public static event Action<string>? OnDebug;

	private static List<Action>? _defaultPostInitActions = new();

	private static List<Action>? _defaultPreInitActions = new();

	private string _ownerId;

	internal static readonly object LockObj = new();

	private DateTime _lastPingTime;

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

			if (!DefaultInitStarted)
			{
				DefaultInitStarted = true;
				var initType = Type.GetType("InterprocessLib.Initializer");
				if (initType is not null)
				{
					initType.GetMethod("Init", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)!.Invoke(null, null);
				}
				else
				{
					throw new EntryPointNotFoundException("Could not find default InterprocessLib initialization type!");
				}
			}
		}
		else
		{
			Register();
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

	internal static void WarnHandler(string str)
	{
		OnWarning?.Invoke(str);
	}

	internal static void FailHandler(Exception ex)
	{
		OnFailure?.Invoke(ex);
	}

	internal static void DebugHandler(string str)
	{
		OnDebug?.Invoke(str);
	}

	internal static void InitializeDefaultSystem(MessagingSystem system)
	{
		lock (LockObj)
		{
			_defaultSystem = system;

			foreach (var act in _defaultPreInitActions!)
			{
				try
				{
					act();
				}
				catch (Exception ex)
				{
					WarnHandler($"Exception running pre-init action:\n{ex}");
				}
			}
			_defaultPreInitActions = null;
			
			system.Connect();
			system.Initialize();

			foreach (var act in _defaultPostInitActions!)
			{
				try
				{
					act();
				}
				catch (Exception ex)
				{
					WarnHandler($"Exception running post-init action:\n{ex}");
				}
			}
			_defaultPostInitActions = null;
		}
	}

	private void DefaultRunPostInit(Action act)
	{
		lock (LockObj)
		{
			if (IsInitialized)
				act();
			else
				_defaultPostInitActions!.Add(act);
		}
	}

	private void DefaultRunPreInit(Action act)
	{
		lock (LockObj)
		{
			if (IsInitialized)
				act();
			else
				_defaultPreInitActions!.Add(act);
		}
	}

	private void Register()
	{
		if (CurrentSystem!.HasOwner(_ownerId))
			WarnHandler($"Owner {_ownerId} has already been registered!");
		else
			CurrentSystem.RegisterOwner(_ownerId);

		// CurrentSystem.RegisterCallback<ValueCommand<bool>>(_ownerId, "Ping", (ping) =>
		// {
		// 	if (!ping.Value)
		// 	{
		// 		ping.Value = true;
		// 		CurrentSystem.SendPackable(ping);
		// 	}
		// });
	}

	public void SendValue<T>(string id, T value) where T : unmanaged
	{
		if (id is null)
			throw new ArgumentNullException(nameof(id));

		if (!IsInitialized)
		{
			DefaultRunPostInit(() => SendValue(id, value));
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
			DefaultRunPostInit(() => SendValueCollection<C, T>(id, collection));
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
			DefaultRunPostInit(() => SendValueArray(id, array));
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
			DefaultRunPostInit(() => SendString(id, str));
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
			DefaultRunPostInit(() => SendStringCollection<C>(id, collection));
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
			DefaultRunPostInit(() => SendStringArray(id, array));
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
			DefaultRunPostInit(() => SendEmptyCommand(id));
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
			DefaultRunPostInit(() => SendObject(id, obj));
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
			DefaultRunPostInit(() => SendObjectCollection<C, T>(id, collection));
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
			DefaultRunPostInit(() => SendObjectArray(id, array));
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
			DefaultRunPostInit(() => ReceiveValue(id, callback));
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
			DefaultRunPostInit(() => ReceiveValueCollection<C, T>(id, callback));
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
			DefaultRunPostInit(() => ReceiveValueArray(id, callback));
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
			DefaultRunPostInit(() => ReceiveString(id, callback));
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
			DefaultRunPostInit(() => ReceiveStringCollection(id, callback));
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
			DefaultRunPostInit(() => ReceiveStringArray(id, callback));
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
			DefaultRunPostInit(() => ReceiveEmptyCommand(id, callback));
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
			DefaultRunPostInit(() => ReceiveObject(id, callback));
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
			DefaultRunPostInit(() => ReceiveObjectCollection<C, T>(id, callback));
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
			DefaultRunPostInit(() => ReceiveObjectArray(id, callback));
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
			DefaultRunPostInit(() => SendPing());
			return;
		}

		var cmd = new PingCommand();
		cmd.Owner = _ownerId;
		cmd.Id = "Ping";
		_lastPingTime = DateTime.UtcNow;
		CurrentSystem!.SendPackable(cmd);
	}

	/// <summary>
	/// Register a delegate to be called when this process gets a ping
	/// Calling <see cref="SendPing"/> will then result in the delegate being called shortly after if the other process is active
	/// </summary>
	/// <param name="callback">The delegate to be called when the ping response gets received</param>
	public void ReceivePing(Action<TimeSpan> callback)
	{
		if (!IsInitialized)
		{
			DefaultRunPostInit(() => ReceivePing(callback));
			return;
		}

		CurrentSystem!.RegisterCallback<PingCommand>(_ownerId, "Ping", (ping) => callback?.Invoke(DateTime.UtcNow - _lastPingTime));
	}

    public void Dispose()
    {
        CurrentSystem?.UnregisterOwner(_ownerId);
    }
}